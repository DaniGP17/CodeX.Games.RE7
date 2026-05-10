using CodeX.Core.Engine;
using CodeX.Core.Numerics;
using CodeX.Core.Utilities;
using CodeX.Games.RE7.Common;
using CodeX.Games.RE7.PAK;
using CodeX.Games.RE7.RSZ;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace CodeX.Games.RE7.Files
{

    public class SceneFile : PiecePack
    {
        public int InfoCount;
        public int ResourceCount;
        public int FolderCount;
        public int PrefabCount;
        public int UserDataCount;

        public List<SceneGameObjectInfo> GameObjects = new();
        public List<SceneFolderInfo> Folders = new();
        public List<SceneResourceInfo> Resources = new();
        public List<ScenePrefabInfo> Prefabs = new();
        public List<SceneUserDataInfo> UserData = new();
        public RSZData RszData;

        public SceneNode Root;
        public HashSet<string> ExternalScenePaths = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SceneFile> ResolvedExternalScenes = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<JenkHash, string> PieceSourceScenes = new();

        public SceneFile() : base() { }
        public SceneFile(GameArchiveFileInfo info) : base(info) { }
        public SceneFile(GameArchiveFileInfo info, byte[] data) : base(info)
        {
            Load(data);
        }

        public override void Load(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                br.ReadBytes(4); //"SCN\0" magic
                InfoCount = br.ReadInt32();
                ResourceCount = br.ReadInt32();
                FolderCount = br.ReadInt32();
                PrefabCount = br.ReadInt32();
                UserDataCount = br.ReadInt32();

                ulong folderInfoOffset = br.ReadUInt64();
                ulong resourceInfoOffset = br.ReadUInt64();
                ulong prefabInfoOffset = br.ReadUInt64();
                ulong userDataInfoOffset = br.ReadUInt64();
                ulong dataOffset = br.ReadUInt64();

                for (int i = 0; i < InfoCount; i++)
                {
                    GameObjects.Add(new SceneGameObjectInfo
                    {
                        InstanceId = new Guid(br.ReadBytes(16)),
                        Id = br.ReadInt32(),
                        ParentId = br.ReadInt32(),
                        ComponentCount = br.ReadInt32(),
                        PrefabId = br.ReadInt32(),
                    });
                }

                br.BaseStream.Seek((long)folderInfoOffset, SeekOrigin.Begin);
                for (int i = 0; i < FolderCount; i++)
                {
                    Folders.Add(new SceneFolderInfo { Id = br.ReadInt32(), ParentId = br.ReadInt32() });
                }

                br.BaseStream.Seek((long)resourceInfoOffset, SeekOrigin.Begin);
                for (int i = 0; i < ResourceCount; i++)
                {
                    Resources.Add(new SceneResourceInfo
                    {
                        Name = br.ReadNullTerminatedString(br.ReadInt64(), Encoding.Unicode),
                    });
                }

                br.BaseStream.Seek((long)prefabInfoOffset, SeekOrigin.Begin);
                for (int i = 0; i < PrefabCount; i++)
                {
                    Prefabs.Add(new ScenePrefabInfo
                    {
                        Name = br.ReadNullTerminatedString(br.ReadInt64(), Encoding.Unicode),
                    });
                }

                br.BaseStream.Seek((long)userDataInfoOffset, SeekOrigin.Begin);
                for (int i = 0; i < UserDataCount; i++)
                {
                    var ud = new SceneUserDataInfo { TypeId = br.ReadUInt32() };
                    br.ReadBytes(4); //padding
                    ud.Name = br.ReadNullTerminatedString(br.ReadInt64(), Encoding.Unicode);
                    UserData.Add(ud);
                }

                br.BaseStream.Seek((long)dataOffset, SeekOrigin.Begin);
                RszData = RszReader.Read(br);

                BuildHierarchy();
            }
            catch (Exception ex)
            {
                LoadException = ex;
            }
        }

        public override byte[] Save() => null;
        public override void Read(MetaNodeReader reader) { }
        public override void Write(MetaNodeWriter writer) { }

        private void BuildHierarchy()
        {
            if (RszData == null) return;
            var nodes = new Dictionary<int, SceneNode>();

            //Folders first: they own external scene references (.scn paths) used by
            //level streaming. We pull the Folder instance via ObjectTable[id] -> Classes
            //and parse the typed ViaFolder out of it.
            for (int f = 0; f < Folders.Count; f++)
            {
                var folder = Folders[f];
                var node = new SceneFolderNode { Id = folder.Id, ParentId = folder.ParentId };

                if (folder.Id >= 0 && folder.Id < RszData.ObjectTable.Count)
                {
                    int classIdx = RszData.ObjectTable[folder.Id];
                    if (classIdx > 0 && classIdx < RszData.Classes.Count && RszData.Classes[classIdx] != null)
                        node.Folder = ViaFolder.Parse(RszData.Classes[classIdx]);
                }

                if (!string.IsNullOrEmpty(node.Folder?.ScenePath) && node.Folder.ScenePath.Contains(".scn"))
                    ExternalScenePaths.Add(node.Folder.ScenePath);

                nodes[folder.Id] = node;
            }

            foreach (var go in GameObjects)
            {
                var node = new SceneGameObjectNode
                {
                    Id = go.Id,
                    ParentId = go.ParentId,
                    InstanceId = go.InstanceId,
                };

                if (go.Id < RszData.ObjectTable.Count)
                {
                    int goClassIdx = RszData.ObjectTable[go.Id];
                    if (goClassIdx > 0 && goClassIdx < RszData.Classes.Count && RszData.Classes[goClassIdx] != null)
                        node.GameObject = ViaGameObject.Parse(RszData.Classes[goClassIdx]);
                }

                //Components live at consecutive object-table slots immediately after
                //the gameobject entry. ComponentCount controls how many we walk.
                for (int c = 0; c < go.ComponentCount; c++)
                {
                    int compTableIdx = go.Id + 1 + c;
                    if (compTableIdx >= RszData.ObjectTable.Count) break;
                    int classIdx = RszData.ObjectTable[compTableIdx];
                    if (classIdx > 0 && classIdx < RszData.Classes.Count && RszData.Classes[classIdx] != null)
                        node.Components.Add(RszData.Classes[classIdx]);
                }

                var transformComp = node.Components.FirstOrDefault(c => c.Name == "via.Transform");
                if (transformComp != null) node.Transform = ViaTransform.Parse(transformComp);

                nodes[go.Id] = node;
            }

            //Synthetic root collects everything whose parent isn't part of this scene
            //(i.e. roots, or refs into an external scene we haven't merged in yet).
            var root = new SceneFolderNode { Id = -1, ParentId = -1 };
            foreach (var node in nodes.Values)
            {
                if (node.ParentId < 0 || !nodes.TryGetValue(node.ParentId, out var parent))
                    root.Children.Add(node);
                else
                    parent.Children.Add(node);
            }

            Root = root;
        }

        #region External scene loading + hierarchy merge

        public void LoadExternalScenes(RE7FileManager fman, Dictionary<string, SceneFile> cache = null)
        {
            if (fman == null) return;
            cache ??= new Dictionary<string, SceneFile>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawPath in ExternalScenePaths)
            {
                var key = NormaliseScenePath(rawPath);
                if (string.IsNullOrEmpty(key)) continue;

                if (cache.TryGetValue(key, out var existing))
                {
                    //Already loaded somewhere in the tree (or we are mid-load on a cycle).
                    //Reuse the same instance so a second branch that references it gets the
                    //same merged hierarchy.
                    if (existing != null) ResolvedExternalScenes[key] = existing;
                    continue;
                }

                var entry = fman.FindEntryByInnerPath(rawPath);
                if (entry == null)
                {
                    cache[key] = null;//remember the miss so we don't search again
                    continue;
                }

                var data = entry.Archive?.ExtractFile(entry);
                if (data == null) { cache[key] = null; continue; }

                var inner = new SceneFile(entry, data);
                if (inner.LoadException != null)
                {
                    cache[key] = null;
                    continue;
                }

                cache[key] = inner;
                ResolvedExternalScenes[key] = inner;

                inner.LoadExternalScenes(fman, cache);
                inner.MergeExternalHierarchies();
            }
        }

        public void MergeExternalHierarchies()
        {
            if (Root == null) return;
            var visited = new HashSet<SceneNode>(ReferenceEqualityComparer.Instance);
            MergeNode(Root, visited);
        }

        private void MergeNode(SceneNode node, HashSet<SceneNode> visited)
        {
            if (!visited.Add(node)) return;

            if (node is SceneFolderNode folderNode && !string.IsNullOrEmpty(folderNode.Folder?.ScenePath))
            {
                var key = NormaliseScenePath(folderNode.Folder.ScenePath);
                if (!string.IsNullOrEmpty(key)
                    && ResolvedExternalScenes.TryGetValue(key, out var externalScene)
                    && externalScene?.Root != null)
                {
                    foreach (var child in externalScene.Root.Children)
                        folderNode.Children.Add(child);
                }
            }

            foreach (var child in node.Children) MergeNode(child, visited);
        }

        private static string NormaliseScenePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            var p = path.Replace('\\', '/').ToLowerInvariant().TrimStart('/');

            const string prefix = "natives/stm/";
            if (p.StartsWith(prefix)) p = p[prefix.Length..];

            var lastDot = p.LastIndexOf('.');
            if (lastDot > 0 && lastDot < p.Length - 1)
            {
                bool allDigits = true;
                for (int i = lastDot + 1; i < p.Length; i++)
                {
                    if (p[i] < '0' || p[i] > '9') { allDigits = false; break; }
                }
                if (allDigits) p = p[..lastDot];
            }

            return p;
        }

        #endregion

        #region Mesh expansion (scene -> placed Pieces)

        private static readonly Matrix4x4 _yupToZup = new(
            1, 0, 0, 0,
            0, 0, 1, 0,
            0,-1, 0, 0,
            0, 0, 0, 1);
        private static readonly Matrix4x4 _zupToYup = new(
            1, 0, 0, 0,
            0, 0,-1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);

        public void BuildPieces(RE7FileManager fman)
        {
            if (Root == null || fman == null) return;
            Pieces ??= new Dictionary<JenkHash, Piece>();

            var meshCache = new Dictionary<string, Piece>(StringComparer.OrdinalIgnoreCase);
            int instCounter = 0;
            int instAdded = 0;

            void Walk(SceneNode node, Matrix4x4 parentWorldYup, string sourceScene)
            {
                var nodeWorldYup = parentWorldYup;
                var currentSource = sourceScene;

                //A SceneFolderNode whose Folder.ScenePath matches a resolved external
                //scene marks the boundary into that scene's subtree - track it so every
                //placed piece beneath this folder is attributed to that scene. Chained
                //externals (folder -> external whose own folders also reference scenes)
                //naturally update currentSource at each boundary.
                if (node is SceneFolderNode folder && !string.IsNullOrEmpty(folder.Folder?.ScenePath))
                {
                    var folderKey = NormaliseScenePath(folder.Folder.ScenePath);
                    if (!string.IsNullOrEmpty(folderKey) && ResolvedExternalScenes.ContainsKey(folderKey))
                    {
                        currentSource = folderKey;
                    }
                }

                if (node is SceneGameObjectNode go)
                {
                    if (go.Transform != null)
                    {
                        var t = go.Transform;
                        var local = Matrix4x4.CreateScale(t.Scale)
                                  * Matrix4x4.CreateFromQuaternion(t.Rotation)
                                  * Matrix4x4.CreateTranslation(t.Position);
                        nodeWorldYup = local * parentWorldYup;
                    }

                    foreach (var component in go.Components)
                    {
                        if (component.Name != "via.render.Mesh") continue;
                        var meshPath = component.Get<string>("v2") ?? "";
                        if (string.IsNullOrEmpty(meshPath)) continue;

                        var sourcePiece = TryGetMeshPiece(meshPath, fman, meshCache);
                        if (sourcePiece == null) continue;

                        var worldZup = _zupToYup * nodeWorldYup * _yupToZup;
                        var worldMat3x4 = new Matrix3x4(worldZup);

                        var instName = $"{Path.GetFileNameWithoutExtension(meshPath)}#{go.Id}_{instCounter++}";
                        var instPiece = ClonePieceWithTransform(sourcePiece, worldMat3x4, instName);
                        if (instPiece == null) continue;

                        var key = JenkHash.GenHash(instName);
                        Pieces[key] = instPiece;
                        PieceSourceScenes[key] = currentSource;
                        instAdded++;
                    }
                }

                foreach (var child in node.Children) Walk(child, nodeWorldYup, currentSource);
            }

            Walk(Root, Matrix4x4.Identity, "");
        }

        private static Piece TryGetMeshPiece(string innerPath, RE7FileManager fman, Dictionary<string, Piece> cache)
        {
            var key = innerPath.Replace('\\', '/').ToLowerInvariant().TrimStart('/');
            const string prefix = "natives/stm/";
            if (key.StartsWith(prefix)) key = key[prefix.Length..];

            if (cache.TryGetValue(key, out var cached)) return cached;

            var entry = fman.FindEntryByInnerPath(key);
            if (entry == null) { cache[key] = null; return null; }

            var data = entry.Archive?.ExtractFile(entry);
            if (data == null) { cache[key] = null; return null; }

            var pack = fman.LoadPiecePack(entry, data, true);
            var piece = (pack as MeshFile)?.Piece ?? pack?.Piece;
            cache[key] = piece;
            return piece;
        }

        private static Piece ClonePieceWithTransform(Piece source, Matrix3x4 worldZup, string newName)
        {
            if (source?.Lods == null || source.Lods.Length == 0) return null;

            var clonedLods = new PieceLod[source.Lods.Length];
            for (int li = 0; li < source.Lods.Length; li++)
            {
                var lod = source.Lods[li];
                if (lod?.Models == null) continue;

                var clonedModels = new Model[lod.Models.Length];
                for (int mi = 0; mi < lod.Models.Length; mi++)
                {
                    var srcModel = lod.Models[mi];
                    if (srcModel?.Meshes == null) { clonedModels[mi] = srcModel; continue; }

                    var clonedMeshes = new Mesh[srcModel.Meshes.Length];
                    for (int meshIdx = 0; meshIdx < srcModel.Meshes.Length; meshIdx++)
                    {
                        var srcMesh = srcModel.Meshes[meshIdx];
                        if (srcMesh == null) { clonedMeshes[meshIdx] = null; continue; }
                        var c = srcMesh.Clone();
                        c.MeshTransform = worldZup;
                        c.MeshTransformMode = 1u;
                        clonedMeshes[meshIdx] = c;
                    }

                    clonedModels[mi] = new Model { Name = srcModel.Name, Meshes = clonedMeshes };
                }

                clonedLods[li] = new PieceLod { Models = clonedModels, LodDist = lod.LodDist };
            }

            var piece = new Piece
            {
                Name = newName,
                Lods = clonedLods,
                MetersPerUnit = source.MetersPerUnit,
            };
            piece.UpdateAllModels();

            //Recompute bounds in world space by transforming the source's local AABB.
            var srcMin = source.BoundingBox.Minimum;
            var srcMax = source.BoundingBox.Maximum;
            var corners = new Vector3[8]
            {
                new(srcMin.X, srcMin.Y, srcMin.Z),
                new(srcMax.X, srcMin.Y, srcMin.Z),
                new(srcMin.X, srcMax.Y, srcMin.Z),
                new(srcMax.X, srcMax.Y, srcMin.Z),
                new(srcMin.X, srcMin.Y, srcMax.Z),
                new(srcMax.X, srcMin.Y, srcMax.Z),
                new(srcMin.X, srcMax.Y, srcMax.Z),
                new(srcMax.X, srcMax.Y, srcMax.Z),
            };
            var wMin = new Vector3(float.MaxValue);
            var wMax = new Vector3(float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var w = worldZup.Transform(corners[i]);
                wMin = Vector3.Min(wMin, w);
                wMax = Vector3.Max(wMax, w);
            }
            piece.BoundingBox = new BoundingBox(wMin, wMax);
            piece.BoundingSphere = new BoundingSphere((wMin + wMax) * 0.5f, (wMax - wMin).Length() * 0.5f);
            return piece;
        }

        #endregion
    }

    #region Header info structs

    public struct SceneFolderInfo
    {
        public int Id;
        public int ParentId;
    }

    public struct SceneResourceInfo
    {
        public string Name;
    }

    public struct ScenePrefabInfo
    {
        public string Name;
    }

    public struct SceneUserDataInfo
    {
        public uint TypeId;
        public string Name;
    }

    public struct SceneGameObjectInfo
    {
        public Guid InstanceId;
        public int Id;
        public int ParentId;
        public int ComponentCount;
        public int PrefabId;
    }

    #endregion

    #region Hierarchy nodes

    public abstract class SceneNode
    {
        public int Id;
        public int ParentId;
        public List<SceneNode> Children = new();
    }

    public class SceneFolderNode : SceneNode
    {
        public ViaFolder Folder;
    }

    public class SceneGameObjectNode : SceneNode
    {
        public Guid InstanceId;
        public ViaGameObject GameObject;
        public ViaTransform Transform;
        public List<RszClass> Components = new();
    }

    #endregion
}
