using CodeX.Core.Engine;
using CodeX.Core.Numerics;
using CodeX.Core.Utilities;
using CodeX.Games.RE7.PAK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace CodeX.Games.RE7.Files
{
    public class MeshFile : PiecePack
    {
        public const uint MagicValue = 0x4853454D; //"MESH"
        public const uint VersionRE7RT = 21041600;

        public MeshHeader Header;
        public MeshLayout Layout;
        public MeshBuffer Buffer;
        public Dictionary<ushort, string> Strings = [];
        public Dictionary<ushort, string> MaterialNames = [];
        public Dictionary<ushort, string> JointNames = [];

        //Records which material name was assigned to each generated CodeX Mesh, so a follow-up
        //ApplyMaterials() call can wire textures from the matching .mdf2 entry.
        public Dictionary<Mesh, string> MeshMaterials = [];
        public SkeletonLayout? SkeletonInfo;
        public BoundingBoxLayout? BoundingBoxes;

        public MeshFile() : base() { }
        public MeshFile(GameArchiveFileInfo info) : base(info) { }
        public MeshFile(GameArchiveFileInfo info, byte[] data) : base(info)
        {
            Load(data);
        }

        public override void Load(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                ReadHeader(br);
                ReadMeshLayout(br);
                ReadMeshBodies(br);
                ReadStrings(br);
                ReadMaterialNames(br);
                ReadBuffer(br);
                ReadVertexAndIndexData();
                ReadSkeleton(br);
                ReadBoundingBoxes(br);
                ReadJointNames(br);

                BuildPiece();
            }
            catch (Exception ex)
            {
                LoadException = ex;
            }
        }

        public override byte[] Save() => null; //TODO

        #region Binary parsing

        private void ReadHeader(BinaryReader br)
        {
            Header.Magic = br.ReadUInt32();
            Header.Version = br.ReadUInt32();
            Header.FileSize = br.ReadUInt32();
            Header.LODGroupHash = br.ReadUInt32();
            Header.Flags = (MeshFlags)br.ReadByte();
            Header.SolvedOffset = br.ReadByte();
            Header.StringCount = br.ReadUInt16();
            Header.LightMapInfo = br.ReadUInt32();
            Header.MeshOffset = br.ReadUInt64();
            Header.ShadowMeshOffset = br.ReadUInt64();
            Header.OccluderMeshOffset = br.ReadUInt64();
            Header.SkeletonOffset = br.ReadUInt64();
            Header.NormalRecalculationOffset = br.ReadUInt64();
            Header.BlendShapeOffset = br.ReadUInt64();
            Header.BoundingBoxOffset = br.ReadUInt64();
            Header.BufferOffset = br.ReadUInt64();
            Header.GroupPivotOffset = br.ReadUInt64();
            Header.MeshClusterNameIndexList = br.ReadUInt64();
            Header.JointNameIndexList = br.ReadUInt64();
            Header.ChannelNameIndexList = br.ReadUInt64();
            Header.StringTabletOffset = br.ReadUInt64();

            if (Header.Magic != MagicValue)
            {
                throw new InvalidDataException("Invalid MESH magic.");
            }
        }

        private void ReadMeshLayout(BinaryReader br)
        {
            if (Header.MeshOffset == 0) return;

            br.BaseStream.Seek((long)Header.MeshOffset, SeekOrigin.Begin);

            Layout = new MeshLayout
            {
                LODCount = br.ReadByte(),
                TotalClusterCount = br.ReadByte(),
                UVCount = br.ReadByte(),
                SkinWeightCount = br.ReadByte(),
                MaxDrawCallCount = br.ReadUInt16(),
                Has32BitIndexBuffer = br.ReadByte(),
                SharedLODBits = br.ReadByte(),
                BoundingX = br.ReadSingle(),
                BoundingY = br.ReadSingle(),
                BoundingZ = br.ReadSingle(),
                BoundingRadius = br.ReadSingle(),
                AabbMinX = br.ReadSingle(),
                AabbMinY = br.ReadSingle(),
                AabbMinZ = br.ReadSingle(),
                PadMin = br.ReadSingle(),
                AabbMaxX = br.ReadSingle(),
                AabbMaxY = br.ReadSingle(),
                AabbMaxZ = br.ReadSingle(),
                PadMax = br.ReadSingle(),
                MeshPointersOffset = br.ReadUInt64(),
                MeshOffsets = new List<ulong>(),
                MeshBodies = new List<MeshBody>(),
            };

            br.BaseStream.Seek((long)Layout.MeshPointersOffset, SeekOrigin.Begin);
            for (int i = 0; i < Layout.LODCount; i++)
            {
                Layout.MeshOffsets.Add(br.ReadUInt64());
            }
        }

        private void ReadMeshBodies(BinaryReader br)
        {
            if (Layout.MeshOffsets == null) return;

            for (int i = 0; i < Layout.MeshOffsets.Count; i++)
            {
                br.BaseStream.Seek((long)Layout.MeshOffsets[i], SeekOrigin.Begin);

                var body = new MeshBody
                {
                    PartCount = br.ReadByte(),
                    VertexFormat = br.ReadByte(),
                    Reserved = br.ReadBytes(2),
                    LODFactor = br.ReadSingle(),
                    PartsOffsetListOffset = br.ReadUInt64(),
                    PartsOffset = new List<ulong>(),
                    Parts = new List<MeshPart>(),
                };

                br.BaseStream.Seek((long)body.PartsOffsetListOffset, SeekOrigin.Begin);
                for (int j = 0; j < body.PartCount; j++)
                {
                    body.PartsOffset.Add(br.ReadUInt64());
                }

                for (int j = 0; j < body.PartCount; j++)
                {
                    br.BaseStream.Seek((long)body.PartsOffset[j], SeekOrigin.Begin);
                    body.Parts.Add(ReadMeshPart(br));
                }

                Layout.MeshBodies.Add(body);
            }
        }

        private static MeshPart ReadMeshPart(BinaryReader br)
        {
            var part = new MeshPart
            {
                PartId = br.ReadByte(),
                ClusterCount = br.ReadByte(),
                Reserved = br.ReadBytes(6),
                VertexCount = br.ReadUInt32(),
                IndexCount = br.ReadUInt32() / 3,
            };

            part.Clusters = new MeshCluster[part.ClusterCount];
            for (int k = 0; k < part.ClusterCount; k++)
            {
                part.Clusters[k] = ReadMeshCluster(br);
            }

            return part;
        }

        private static MeshCluster ReadMeshCluster(BinaryReader br)
        {
            return new MeshCluster
            {
                MaterialId = br.ReadByte(),
                IsQuad = br.ReadByte(),
                Reserved = br.ReadBytes(2),
                IndexCount = br.ReadUInt32() / 3,
                StartIndexLocation = br.ReadUInt32(),
                BaseVertexLocation = br.ReadInt32(),
                StreamingOffsetBytes = br.ReadInt32(),
                StreamingPlatformSpecificOffsetBytes = br.ReadInt32(),
            };
        }

        private void ReadStrings(BinaryReader br)
        {
            Strings = new Dictionary<ushort, string>();
            for (int i = 0; i < Header.StringCount; i++)
            {
                Strings[(ushort)i] = string.Empty;
            }

            if (Header.StringTabletOffset == 0) return;

            br.BaseStream.Seek((long)Header.StringTabletOffset, SeekOrigin.Begin);
            for (int i = 0; i < Header.StringCount; i++)
            {
                ulong stringOffset = br.ReadUInt64();
                long currentPos = br.BaseStream.Position;
                br.BaseStream.Seek((long)stringOffset, SeekOrigin.Begin);

                var sb = new StringBuilder();
                byte b;
                while ((b = br.ReadByte()) != 0)
                {
                    sb.Append((char)b);
                }

                Strings[(ushort)i] = sb.ToString();
                br.BaseStream.Seek(currentPos, SeekOrigin.Begin);
            }
        }

        private void ReadMaterialNames(BinaryReader br)
        {
            if (Header.MeshClusterNameIndexList == 0) return;

            br.BaseStream.Seek((long)Header.MeshClusterNameIndexList, SeekOrigin.Begin);
            for (int i = 0; i < Layout.TotalClusterCount; i++)
            {
                ushort nameIndex = br.ReadUInt16();
                var name = Strings.TryGetValue(nameIndex, out var s) ? s : string.Empty;
                MaterialNames[(ushort)MaterialNames.Count] = name;
            }
        }

        private void ReadBuffer(BinaryReader br)
        {
            if (Header.BufferOffset == 0) return;

            br.BaseStream.Seek((long)Header.BufferOffset, SeekOrigin.Begin);

            Buffer = new MeshBuffer
            {
                ElementListOffset = br.ReadUInt64(),
                VertexBufferOffset = br.ReadUInt64(),
                IndexBufferOffset = br.ReadUInt64(),
            };

            if (Header.Version == VersionRE7RT)
            {
                br.ReadUInt64(); //RE7RT has an extra 8-byte field here
            }

            Buffer.VertexBufferSize = br.ReadUInt32();
            Buffer.IndexBufferSize = br.ReadUInt32();
            Buffer.ElementCount = br.ReadUInt16();
            Buffer.TotalElementCount = br.ReadUInt16();
            Buffer.ShadowMeshIndexBufferOffset = br.ReadUInt32();
            Buffer.OccluderMeshIndexBufferOffset = br.ReadUInt32();

            br.BaseStream.Seek((long)Buffer.ElementListOffset, SeekOrigin.Begin);
            Buffer.Elements = new List<BufferElement>();
            for (int i = 0; i < Buffer.TotalElementCount; i++)
            {
                Buffer.Elements.Add(new BufferElement
                {
                    InputSlot = (VertexElementSlot)br.ReadUInt16(),
                    ByteStride = br.ReadUInt16(),
                    ByteOffset = br.ReadUInt32(),
                });
            }

            br.BaseStream.Seek((long)Buffer.VertexBufferOffset, SeekOrigin.Begin);
            Buffer.VertexBuffer = br.ReadBytes((int)Buffer.VertexBufferSize);

            br.BaseStream.Seek((long)Buffer.IndexBufferOffset, SeekOrigin.Begin);
            Buffer.IndexBuffer = br.ReadBytes((int)Buffer.IndexBufferSize);
        }

        private void ReadVertexAndIndexData()
        {
            if (Layout.MeshBodies == null) return;
            if (Buffer.VertexBuffer == null || Buffer.IndexBuffer == null) return;

            int globalVertexCount = 0;

            for (int i = 0; i < Layout.MeshBodies.Count; i++)
            {
                var body = Layout.MeshBodies[i];
                for (int j = 0; j < body.Parts.Count; j++)
                {
                    var part = body.Parts[j];
                    for (int k = 0; k < part.Clusters.Length; k++)
                    {
                        ref var cluster = ref part.Clusters[k];

                        int vertexCount = (k + 1 < part.Clusters.Length)
                            ? part.Clusters[k + 1].BaseVertexLocation - cluster.BaseVertexLocation
                            : (int)part.VertexCount - (cluster.BaseVertexLocation - globalVertexCount);

                        if (vertexCount < 0) vertexCount = 0;
                        ReadClusterIndices(ref cluster);
                        ReadClusterVertices(ref cluster, vertexCount);
                    }

                    globalVertexCount += (int)part.VertexCount;
                }
            }
        }

        private void ReadClusterIndices(ref MeshCluster cluster)
        {
            var indices = new ushort[cluster.IndexCount * 3];
            int srcOffset = (int)(cluster.StartIndexLocation * 2);
            int byteCount = (int)cluster.IndexCount * 6;
            if (srcOffset < 0 || srcOffset + byteCount > Buffer.IndexBuffer.Length)
            {
                cluster.Indices = Array.Empty<ushort>();
                return;
            }
            System.Buffer.BlockCopy(Buffer.IndexBuffer, srcOffset, indices, 0, byteCount);
            cluster.Indices = indices;
        }

        private void ReadClusterVertices(ref MeshCluster cluster, int vertexCount)
        {
            cluster.VertexCount = vertexCount;
            if (vertexCount <= 0 || Buffer.Elements == null) return;

            var seenSlots = new HashSet<VertexElementSlot>();

            foreach (var element in Buffer.Elements)
            {
                if (!seenSlots.Add(element.InputSlot)) continue;

                int srcStart = (cluster.BaseVertexLocation * element.ByteStride) + (int)element.ByteOffset;

                switch (element.InputSlot)
                {
                    case VertexElementSlot.Position:
                        cluster.Positions = ReadPositions(srcStart, element.ByteStride, vertexCount);
                        break;
                    case VertexElementSlot.Normal:
                        ReadNormalsAndTangents(srcStart, element.ByteStride, vertexCount, out cluster.Normals, out cluster.Tangents);
                        break;
                    case VertexElementSlot.Uv0:
                        cluster.Uv0 = ReadUVs(srcStart, element.ByteStride, vertexCount);
                        break;
                    case VertexElementSlot.Uv1:
                        cluster.Uv1 = ReadUVs(srcStart, element.ByteStride, vertexCount);
                        break;
                    case VertexElementSlot.Weights:
                        ReadBoneWeights(srcStart, element.ByteStride, vertexCount, out cluster.BoneIndices, out cluster.BoneWeights);
                        break;
                    case VertexElementSlot.Color:
                    default:
                        break;
                }
            }
        }

        private Vector3[] ReadPositions(int srcStart, int stride, int count)
        {
            //RE Engine is Y-up, CodeX is Z-up.
            //Apply a -90° rotation around X: (x, y, z) -> (x, -z, y).
            var arr = new Vector3[count];
            var src = Buffer.VertexBuffer;
            for (int n = 0; n < count; n++)
            {
                int o = srcStart + n * stride;
                if (o + 12 > src.Length) break;
                var x = BitConverter.ToSingle(src, o + 0);
                var y = BitConverter.ToSingle(src, o + 4);
                var z = BitConverter.ToSingle(src, o + 8);
                arr[n] = new Vector3(x, -z, y);
            }
            return arr;
        }

        private void ReadNormalsAndTangents(int srcStart, int stride, int count, out PackedSnorm4[] normals, out PackedSnorm4[] tangents)
        {
            //Same Y-up -> Z-up swap as positions, applied component-wise on the snorm8 data.
            //sbyte -128 would overflow when negated, so we clamp to -127 (still ~ -1.0).
            normals = new PackedSnorm4[count];
            tangents = new PackedSnorm4[count];
            var src = Buffer.VertexBuffer;
            for (int n = 0; n < count; n++)
            {
                int o = srcStart + n * stride;
                if (o + 8 > src.Length) break;
                normals[n] = SwapYZ((sbyte)src[o + 0], (sbyte)src[o + 1], (sbyte)src[o + 2], (sbyte)src[o + 3]);
                tangents[n] = SwapYZ((sbyte)src[o + 4], (sbyte)src[o + 5], (sbyte)src[o + 6], (sbyte)src[o + 7]);
            }
        }

        private static PackedSnorm4 SwapYZ(sbyte x, sbyte y, sbyte z, sbyte w)
        {
            //(x, y, z, w) -> (x, -z, y, w). Negating sbyte.MinValue (-128) overflows; the
            //correct positive saturation is +127 (since -(-128) would be +128, unrepresentable).
            //Returning -127 here flips the sign of the new Y component for any normal/tangent
            //whose Z byte is exactly -128, which inverts those vertices' Y axis. In Y-up source
            //data those are normals pointing strongly in -Z, which after the rotation should
            //become +Y in CodeX — but with the bug they become -Y, so walls oriented that way
            //render with their normal pointing into the surface (white in SSAO, dark in lighting).
            sbyte negZ = z == sbyte.MinValue ? (sbyte)127 : (sbyte)-z;
            return new PackedSnorm4(x, negZ, y, w);
        }

        private Vector2[] ReadUVs(int srcStart, int stride, int count)
        {
            var arr = new Vector2[count];
            var src = Buffer.VertexBuffer;
            for (int n = 0; n < count; n++)
            {
                int o = srcStart + n * stride;
                if (o + 4 > src.Length) break;
                arr[n] = new Vector2(
                    (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(src, o + 0)),
                    (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(src, o + 2)));
            }
            return arr;
        }

        private void ReadBoneWeights(int srcStart, int stride, int count, out byte[] indices, out byte[] weights)
        {
            //RE7 packs 8 indices + 8 weights as 16 bytes; But CodeX uses 4+4
            indices = new byte[count * 4];
            weights = new byte[count * 4];
            var src = Buffer.VertexBuffer;
            for (int n = 0; n < count; n++)
            {
                int o = srcStart + n * stride;
                if (o + 16 > src.Length) break;
                indices[n * 4 + 0] = src[o + 0];
                indices[n * 4 + 1] = src[o + 1];
                indices[n * 4 + 2] = src[o + 2];
                indices[n * 4 + 3] = src[o + 3];
                weights[n * 4 + 0] = src[o + 8];
                weights[n * 4 + 1] = src[o + 9];
                weights[n * 4 + 2] = src[o + 10];
                weights[n * 4 + 3] = src[o + 11];
            }
        }

        private void ReadSkeleton(BinaryReader br)
        {
            if (Header.SkeletonOffset == 0) return;

            br.BaseStream.Seek((long)Header.SkeletonOffset, SeekOrigin.Begin);

            var skel = new SkeletonLayout
            {
                JointCount = br.ReadUInt32(),
                JointRemapIndicesCount = br.ReadUInt32(),
            };
            br.ReadUInt64(); //padding
            skel.JointNodeOffset = br.ReadUInt64();
            skel.LocalMatrixOffset = br.ReadUInt64();
            skel.WorldMatrixOffset = br.ReadUInt64();
            skel.InverseBindMatrixOffset = br.ReadUInt64();

            skel.JointRemapIndices = new ushort[skel.JointRemapIndicesCount];
            for (int i = 0; i < skel.JointRemapIndicesCount; i++)
            {
                skel.JointRemapIndices[i] = br.ReadUInt16();
            }

            br.BaseStream.Seek((long)skel.JointNodeOffset, SeekOrigin.Begin);
            skel.JointNodes = new JointNode[skel.JointCount];
            for (int i = 0; i < skel.JointCount; i++)
            {
                skel.JointNodes[i] = new JointNode
                {
                    Index = br.ReadUInt16(),
                    ParentIndex = br.ReadUInt16(),
                    SiblingIndex = br.ReadUInt16(),
                    ChildIndex = br.ReadUInt16(),
                    SymmetryIndex = br.ReadUInt16(),
                };
                br.ReadBytes(6); //padding
            }

            br.BaseStream.Seek((long)skel.LocalMatrixOffset, SeekOrigin.Begin);
            skel.LocalMatrices = ReadMatrices(br, (int)skel.JointCount);

            br.BaseStream.Seek((long)skel.WorldMatrixOffset, SeekOrigin.Begin);
            skel.WorldMatrices = ReadMatrices(br, (int)skel.JointCount);

            br.BaseStream.Seek((long)skel.InverseBindMatrixOffset, SeekOrigin.Begin);
            skel.InverseBindMatrices = ReadMatrices(br, (int)skel.JointCount);

            SkeletonInfo = skel;
        }

        private static Matrix4x4[] ReadMatrices(BinaryReader br, int count)
        {
            var arr = new Matrix4x4[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = new Matrix4x4(
                    br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
                    br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
                    br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
                    br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            }
            return arr;
        }

        private void ReadBoundingBoxes(BinaryReader br)
        {
            if (Header.BoundingBoxOffset == 0) return;

            br.BaseStream.Seek((long)Header.BoundingBoxOffset, SeekOrigin.Begin);

            var bbox = new BoundingBoxLayout
            {
                BoundingBoxCount = br.ReadUInt32(),
            };
            br.ReadUInt32(); //padding
            bbox.BoxesOffset = br.ReadUInt64();
            bbox.Boxes = new AABBData[bbox.BoundingBoxCount];

            br.BaseStream.Seek((long)bbox.BoxesOffset, SeekOrigin.Begin);
            for (int i = 0; i < bbox.BoundingBoxCount; i++)
            {
                bbox.Boxes[i].Min = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                br.ReadSingle(); //padMin
                bbox.Boxes[i].Max = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                br.ReadSingle(); //padMax
            }

            BoundingBoxes = bbox;
        }

        private void ReadJointNames(BinaryReader br)
        {
            if (Header.JointNameIndexList == 0 || SkeletonInfo == null) return;

            br.BaseStream.Seek((long)Header.JointNameIndexList, SeekOrigin.Begin);
            for (int i = 0; i < SkeletonInfo.Value.JointCount; i++)
            {
                ushort nameIndex = br.ReadUInt16();
                var name = Strings.TryGetValue(nameIndex, out var s) ? s : string.Empty;
                JointNames[nameIndex] = name;
            }
        }

        #endregion

        #region CodeX Piece building

        private void BuildPiece()
        {
            if (Layout.MeshBodies == null || Layout.MeshBodies.Count == 0) return;

            var pieceName = FileInfo?.Name ?? Path.GetFileName(FilePath ?? "mesh");
            var lods = new List<PieceLod>(Layout.MeshBodies.Count);

            foreach (var body in Layout.MeshBodies)
            {
                var models = new List<Model>(body.Parts.Count);
                for (int p = 0; p < body.Parts.Count; p++)
                {
                    var meshes = new List<Mesh>(body.Parts[p].Clusters.Length);
                    for (int c = 0; c < body.Parts[p].Clusters.Length; c++)
                    {
                        var cluster = body.Parts[p].Clusters[c];
                        var matName = MaterialNames.TryGetValue(cluster.MaterialId, out var mn) ? mn : null;
                        var cm = BuildMesh(cluster, $"{pieceName}_lod{lods.Count}_part{p}_cluster{c}");
                        if (cm != null)
                        {
                            meshes.Add(cm);
                            if (!string.IsNullOrEmpty(matName)) MeshMaterials[cm] = matName;
                        }
                    }
                    if (meshes.Count > 0)
                    {
                        models.Add(new Model { Name = $"{pieceName}_lod{lods.Count}_part{p}", Meshes = meshes.ToArray() });
                    }
                }
                if (models.Count > 0)
                {
                    var lodDist = body.LODFactor > 0 ? body.LODFactor : 10000.0f;
                    lods.Add(new PieceLod { Models = models.ToArray(), LodDist = lodDist });
                }
            }

            if (lods.Count == 0) return;

            var piece = new Piece { Name = pieceName, Lods = lods.ToArray() };

            //AABB and bounding sphere from the header are also in RE Engine's Y-up frame; rotate
            //them the same way the per-vertex positions are rotated. Piece.UpdateBounds() will
            //recompute exact bounds from the swapped vertex data, so this is just a sane initial
            //value in case any consumer reads bounds before that.
            var rawMin = new Vector3(Layout.AabbMinX, Layout.AabbMinY, Layout.AabbMinZ);
            var rawMax = new Vector3(Layout.AabbMaxX, Layout.AabbMaxY, Layout.AabbMaxZ);
            var rotMin = new Vector3(rawMin.X, -rawMax.Z, rawMin.Y);
            var rotMax = new Vector3(rawMax.X, -rawMin.Z, rawMax.Y);
            piece.BoundingBox = new BoundingBox(rotMin, rotMax);
            piece.BoundingSphere = new BoundingSphere(
                new Vector3(Layout.BoundingX, -Layout.BoundingZ, Layout.BoundingY),
                Layout.BoundingRadius);

            piece.UpdateAllModels();
            piece.UpdateBounds();

            Piece = piece;
            Pieces = new Dictionary<JenkHash, Piece> { { JenkHash.GenHash(pieceName ?? string.Empty), piece } };
        }

        private Mesh BuildMesh(MeshCluster cluster, string name)
        {
            if (cluster.Positions == null || cluster.Positions.Length == 0) return null;
            if (cluster.Indices == null || cluster.Indices.Length == 0) return null;

            var hasNormals = cluster.Normals != null;
            var hasUv0 = cluster.Uv0 != null;
            var hasUv1 = cluster.Uv1 != null;
            var hasSkin = cluster.BoneIndices != null && cluster.BoneWeights != null;

            //semantic codes: P=position, N=normal, X=tangent, T=texcoord, W=bone weights, I=bone indices
            //format codes are hex digits matching VertexElementFormat values:
            //  '3'=Float3, '4'=Float4, '5'=Colour (R8G8B8A8 UNORM), '6'=UByte4, '7'=Half2
            var semanticBuilder = new StringBuilder();
            var formatBuilder = new StringBuilder();
            void addElement(char semantic, char format)
            {
                semanticBuilder.Append(semantic);
                formatBuilder.Append(format);
            }

            addElement('P', '3'); //Position Float3
            if (hasNormals)
            {
                //Float4 instead of Colour (UNORM byte): the bias-and-rescale path the shader uses
                //for Colour normals leaves tangent.w (the bitangent sign) in 0..1, so a -1 sign
                //collapses to ~0 and the bitangent vanishes on UV-mirrored islands — those islands
                //then render almost black with SSAO going full-occluded. Decoding snorm8 -> float
                //in CPU and uploading as Float4 keeps tangent.w as a real ±1, mirroring what
                //REAssetExplorer does in its renderer.
                addElement('N', '4'); //Normal Float4
                addElement('X', '4'); //Tangent Float4 (xyz + bitangent sign in w)
            }
            if (hasUv0) addElement('T', '7'); //UV0 Half2
            if (hasUv1) addElement('T', '7'); //UV1 Half2
            if (hasSkin)
            {
                addElement('W', '6'); //Bone weights UByte4
                addElement('I', '6'); //Bone indices UByte4
            }

            var layout = new VertexLayout(semanticBuilder.ToString(), formatBuilder.ToString());
            int stride = layout.Stride;
            int vertexCount = cluster.Positions.Length;
            var vertexData = new byte[stride * vertexCount];

            for (int v = 0; v < vertexCount; v++)
            {
                int dst = v * stride;

                //position
                var p = cluster.Positions[v];
                BitConverter.GetBytes(p.X).CopyTo(vertexData, dst + 0);
                BitConverter.GetBytes(p.Y).CopyTo(vertexData, dst + 4);
                BitConverter.GetBytes(p.Z).CopyTo(vertexData, dst + 8);
                int o = 12;

                if (hasNormals)
                {
                    //Standard snorm8 -> float decode (clamp -128 to -1.0; -127..127 -> ~-1..1).
                    var n = cluster.Normals[v];
                    BitConverter.GetBytes(MathF.Max(n.X / 127.0f, -1.0f)).CopyTo(vertexData, dst + o + 0);
                    BitConverter.GetBytes(MathF.Max(n.Y / 127.0f, -1.0f)).CopyTo(vertexData, dst + o + 4);
                    BitConverter.GetBytes(MathF.Max(n.Z / 127.0f, -1.0f)).CopyTo(vertexData, dst + o + 8);
                    BitConverter.GetBytes(MathF.Max(n.W / 127.0f, -1.0f)).CopyTo(vertexData, dst + o + 12);
                    o += 16;
                    var t = cluster.Tangents[v];
                    BitConverter.GetBytes(MathF.Max(t.X / 127.0f, -1.0f)).CopyTo(vertexData, dst + o + 0);
                    BitConverter.GetBytes(MathF.Max(t.Y / 127.0f, -1.0f)).CopyTo(vertexData, dst + o + 4);
                    BitConverter.GetBytes(MathF.Max(t.Z / 127.0f, -1.0f)).CopyTo(vertexData, dst + o + 8);
                    BitConverter.GetBytes(MathF.Max(t.W / 127.0f, -1.0f)).CopyTo(vertexData, dst + o + 12);
                    o += 16;
                }

                if (hasUv0)
                {
                    var uv = cluster.Uv0[v];
                    BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)uv.X)).CopyTo(vertexData, dst + o + 0);
                    BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)uv.Y)).CopyTo(vertexData, dst + o + 2);
                    o += 4;
                }
                if (hasUv1)
                {
                    var uv = cluster.Uv1[v];
                    BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)uv.X)).CopyTo(vertexData, dst + o + 0);
                    BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)uv.Y)).CopyTo(vertexData, dst + o + 2);
                    o += 4;
                }

                if (hasSkin)
                {
                    System.Buffer.BlockCopy(cluster.BoneWeights, v * 4, vertexData, dst + o, 4);
                    o += 4;
                    System.Buffer.BlockCopy(cluster.BoneIndices, v * 4, vertexData, dst + o, 4);
                    o += 4;
                }
            }

            var mesh = new Mesh
            {
                Name = name,
                Topology = MeshTopology.TriangleList,
                VertexLayout = layout,
                VertexStride = stride,
                VertexCount = vertexCount,
                VertexData = vertexData,
                Indices = cluster.Indices,
            };

            mesh.UpdateVertexLayoutDependencies();
            mesh.UpdateBounds();

            mesh.SetDefaultShader();
            mesh.ShaderInputs = mesh.Shader.CreateShaderInputs();
            mesh.Textures = new Texture[3];

            mesh.ShaderInputs.SetFloat(0x4D52C5FF, 0.0f);  //AlphaScale
            mesh.ShaderInputs.SetUInt32(0x249983FD, 60);   //ParamsMapConfig = AlbedoAlphaMetalNormalAlphaRoughness
            mesh.ShaderInputs.SetFloat(0xDA9702A9, 0.0f);  //MeshMetallicity
            mesh.ShaderInputs.SetFloat(0x92176B1A, 0.5f);  //MeshSmoothness

            return mesh;
        }

        public void ApplyMaterials(Mdf2File mdf2, RE7FileManager fman)
        {
            if (mdf2 == null || fman == null || Piece == null) return;
            if (MeshMaterials == null || MeshMaterials.Count == 0) return;

            //Cache decoded textures so meshes that share a material don't pay for re-decoding.
            var texCache = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in MeshMaterials)
            {
                var mesh = kvp.Key;
                var matName = kvp.Value;
                var mat = mdf2.FindByName(matName);
                if (mat == null)
                {
                    continue;
                }

                if (mesh.Textures == null || mesh.Textures.Length < 3)
                {
                    mesh.Textures = new Texture[3];
                }

                foreach (var texRef in mat.Textures)
                {
                    var slot = MapTextureTypeToSlot(texRef.Type);
                    if (slot < 0)
                    {
                        continue;
                    }

                    if (!texCache.TryGetValue(texRef.FilePath ?? string.Empty, out var tex))
                    {
                        tex = LoadTextureFromInnerPath(texRef.FilePath, fman);
                        texCache[texRef.FilePath ?? string.Empty] = tex;
                    }
                    if (tex != null) mesh.Textures[slot] = tex;
                }
            }
        }

        private static IEnumerable<string> AllMaterialNames(Mdf2File mdf2)
        {
            if (mdf2?.Materials == null) yield break;
            foreach (var m in mdf2.Materials) yield return m.Name ?? "(null)";
        }

        private static int MapTextureTypeToSlot(string type)
        {
            //CodeX DefaultShader uses three slots: 0 = AlbedoMap, 1 = NormalMap, 2 = ParamsMap.
            //RE7's actual channel layout doesn't fit cleanly:
            //  - BaseMetalMap        RGB = albedo, A = metallic
            //  - NormalRoughnessMap  RG  = normal XY, A = roughness
            //  - ATOS (AlphaTranslucentOcclusionSSSMap) R=alpha, G=translucent, B=AO, A=SSS
            if (string.IsNullOrEmpty(type)) return -1;
            const StringComparison oic = StringComparison.OrdinalIgnoreCase;

            if (type.Contains("BaseMetalMap", oic) || type.Contains("ALBM", oic) ||
                type.Contains("BaseMap", oic)     || type.Contains("Albedo", oic) ||
                type.Contains("BaseDielectric", oic))
            {
                return 0;
            }
            if (type.Contains("NormalRoughness", oic) || type.Contains("NRMR", oic) ||
                type.Contains("Normal", oic))
            {
                return 1;
            }
            return -1;
        }

        private static Texture LoadTextureFromInnerPath(string innerPath, RE7FileManager fman)
        {
            if (string.IsNullOrEmpty(innerPath)) return null;
            var entry = fman.FindEntryByInnerPath(innerPath);
            if (entry == null) return null;
            var pack = fman.LoadTexturePack(entry);
            return pack?.GetDefaultTexture();
        }

        #endregion
    }

    #region Parsed structures (kept exposed for inspection / downstream tooling)

    public struct MeshHeader
    {
        public uint Magic;
        public uint Version;
        public uint FileSize;
        public uint LODGroupHash;
        public MeshFlags Flags;
        public byte SolvedOffset;
        public ushort StringCount;
        public uint LightMapInfo;
        public ulong MeshOffset;
        public ulong ShadowMeshOffset;
        public ulong OccluderMeshOffset;
        public ulong SkeletonOffset;
        public ulong NormalRecalculationOffset;
        public ulong BlendShapeOffset;
        public ulong BoundingBoxOffset;
        public ulong BufferOffset;
        public ulong GroupPivotOffset;
        public ulong MeshClusterNameIndexList;
        public ulong JointNameIndexList;
        public ulong ChannelNameIndexList;
        public ulong StringTabletOffset;
    }

    public struct MeshLayout
    {
        public byte LODCount;
        public byte TotalClusterCount;
        public byte UVCount;
        public byte SkinWeightCount;
        public ushort MaxDrawCallCount;
        public byte Has32BitIndexBuffer;
        public byte SharedLODBits;
        public float BoundingX, BoundingY, BoundingZ, BoundingRadius;
        public float AabbMinX, AabbMinY, AabbMinZ, PadMin;
        public float AabbMaxX, AabbMaxY, AabbMaxZ, PadMax;
        public ulong MeshPointersOffset;
        public List<ulong> MeshOffsets;
        public List<MeshBody> MeshBodies;
    }

    public struct MeshBody
    {
        public byte PartCount;
        public byte VertexFormat;
        public byte[] Reserved;
        public float LODFactor;
        public ulong PartsOffsetListOffset;
        public List<ulong> PartsOffset;
        public List<MeshPart> Parts;
    }

    public struct MeshPart
    {
        public byte PartId;
        public byte ClusterCount;
        public byte[] Reserved;
        public uint VertexCount;
        public uint IndexCount;
        public MeshCluster[] Clusters;
    }

    public struct MeshCluster
    {
        public byte MaterialId;
        public byte IsQuad;
        public byte[] Reserved;
        public uint IndexCount;
        public uint StartIndexLocation;
        public int BaseVertexLocation;
        public int StreamingOffsetBytes;
        public int StreamingPlatformSpecificOffsetBytes;
        public int VertexCount;

        public Vector3[] Positions;
        public PackedSnorm4[] Normals;
        public PackedSnorm4[] Tangents;
        public Vector2[] Uv0;
        public Vector2[] Uv1;
        public byte[] BoneIndices; //4 per vertex
        public byte[] BoneWeights; //4 per vertex
        public ushort[] Indices;   //flat (a,b,c,a,b,c,...)
    }

    public struct MeshBuffer
    {
        public ulong ElementListOffset;
        public ulong VertexBufferOffset;
        public ulong IndexBufferOffset;
        public uint VertexBufferSize;
        public uint IndexBufferSize;
        public ushort ElementCount;
        public ushort TotalElementCount;
        public uint ShadowMeshIndexBufferOffset;
        public uint OccluderMeshIndexBufferOffset;
        public List<BufferElement> Elements;
        public byte[] VertexBuffer;
        public byte[] IndexBuffer;
    }

    public struct BufferElement
    {
        public VertexElementSlot InputSlot;
        public ushort ByteStride;
        public uint ByteOffset;
    }

    public enum VertexElementSlot : ushort
    {
        Position = 0,
        Normal = 1,
        Uv0 = 2,
        Uv1 = 3,
        Weights = 4,
        Color = 5,
    }

    [Flags] public enum MeshFlags : byte
    {
        None = 0,
        IsSkinning = 0x01,
        HasJoint = 0x02,
        HasBlendShape = 0x04,
        HasVertexGroup = 0x08,
        QuadEnable = 0x10,
        StreamingBVH = 0x20,
        HasTertiaryUV = 0x40,
    }

    public struct PackedSnorm4
    {
        public sbyte X, Y, Z, W;
        public PackedSnorm4(sbyte x, sbyte y, sbyte z, sbyte w) { X = x; Y = y; Z = z; W = w; }
    }

    public struct SkeletonLayout
    {
        public uint JointCount;
        public uint JointRemapIndicesCount;
        public ulong JointNodeOffset;
        public ulong LocalMatrixOffset;
        public ulong WorldMatrixOffset;
        public ulong InverseBindMatrixOffset;
        public ushort[] JointRemapIndices;
        public JointNode[] JointNodes;
        public Matrix4x4[] LocalMatrices;
        public Matrix4x4[] WorldMatrices;
        public Matrix4x4[] InverseBindMatrices;
    }

    public struct JointNode
    {
        public ushort Index;
        public ushort ParentIndex;
        public ushort SiblingIndex;
        public ushort ChildIndex;
        public ushort SymmetryIndex;
    }

    public struct BoundingBoxLayout
    {
        public uint BoundingBoxCount;
        public ulong BoxesOffset;
        public AABBData[] Boxes;
    }

    public struct AABBData
    {
        public Vector3 Min;
        public Vector3 Max;
    }

    #endregion
}
