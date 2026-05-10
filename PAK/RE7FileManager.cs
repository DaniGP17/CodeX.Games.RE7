using CodeX.Core.Engine;
using CodeX.Core.Utilities;
using CodeX.Games.RE7.Files;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeX.Games.RE7.PAK
{
    public class RE7FileManager : FileManager
    {
        public override string ArchiveTypeName => "KPKA";
        public override string ArchiveExtension => ".pak";

        public PakFileList FileList { get; private set; }

        private readonly Dictionary<string, PakFileEntry> _innerPathDict = [];

        public RE7FileManager(RE7Game game) : base(game)
        {
        }

        public override void InitFileTypes()
        {
            InitGenericFileTypes();
            InitFileType(".pak", "RE Engine Package", FileTypeIcon.Archive);
            InitFileType(".tex", "RE Engine Texture", FileTypeIcon.Image, FileTypeAction.ViewTextures);
            InitFileType(".mesh", "RE Engine Mesh", FileTypeIcon.Piece, FileTypeAction.ViewModels);
            InitFileType(".mdf2", "RE Engine Materials Definition", FileTypeIcon.SystemFile);
            InitFileType(".scn", "RE Engine Scene", FileTypeIcon.Level, FileTypeAction.ViewModels);
            InitFileType(".pfb", "RE Engine Prefab", FileTypeIcon.Process);
            InitFileType(".user", "RE Engine User Data", FileTypeIcon.SystemFile);
            InitFileType(".motlist", "Motion List", FileTypeIcon.Animation);
            InitFileType(".motfsm", "Motion FSM", FileTypeIcon.Animation);
            InitFileType(".aimap", "AI Map", FileTypeIcon.Collisions);
            InitFileType(".bnk", "Wwise Sound Bank", FileTypeIcon.Audio);
            InitFileType(".mmtr", "Master Material", FileTypeIcon.SystemFile);
            InitFileType(".efx", "Effect", FileTypeIcon.Animation);
        }

        public override void InitCreateInfos()
        {
        }

        public override bool Init()
        {
            FileList = new PakFileList();
            var listPath = StartupUtil.GetFilePath("CodeX.Games.RE7.filelist.txt");
            if (File.Exists(listPath))
            {
                FileList.LoadFromFile(listPath);
            }
            return true;
        }

        public override void InitArchives(string[] files)
        {
            foreach (var path in files)
            {
                var relpath = path.Replace(Folder + "\\", "");
                var filepathl = path.ToLowerInvariant();
                var isFile = File.Exists(path);

                if (!isFile) continue;
                if (!IsArchive(filepathl)) continue;

                var archive = GetArchive(path, relpath);
                if (archive?.AllEntries == null) continue;

                RootArchives.Add(archive);

                var queue = new Queue<GameArchive>();
                queue.Enqueue(archive);
                while (queue.Count > 0)
                {
                    var a = queue.Dequeue();
                    if (a.Children != null)
                    {
                        foreach (var ca in a.Children) queue.Enqueue(ca);
                    }
                    AllArchives.Add(a);
                }
            }
        }

        public override void InitArchivesComplete()
        {
            _innerPathDict.Clear();
            foreach (var archive in AllArchives)
            {
                if (archive.AllEntries == null) continue;
                ArchiveDict[archive.Path] = archive;
                foreach (var entry in archive.AllEntries)
                {
                    if (entry is PakFileEntry fe)
                    {
                        EntryDict[fe.PathLower] = fe;

                        var inner = ExtractInnerPath(fe.PathLower);
                        if (inner != null) _innerPathDict[inner] = fe;
                    }
                }
            }
        }

        private static string ExtractInnerPath(string fullPathLower)
        {
            var idx = fullPathLower.IndexOf(RE7Game.FilesPath);
            if (idx < 0) return null;
            return fullPathLower[(idx + RE7Game.FilesPath.Length)..].Replace('\\', '/');
        }

        public PakFileEntry FindEntryByInnerPath(string innerPath)
        {
            if (string.IsNullOrEmpty(innerPath)) return null;
            var key = innerPath.ToLowerInvariant().Replace('\\', '/').TrimStart('/');

            key = StripNumericSuffix(key);

            if (_innerPathDict.TryGetValue(key, out var entry)) return entry;

            foreach (var kvp in _innerPathDict)
            {
                if (kvp.Key.EndsWith(key)) return kvp.Value;
                if (Path.GetExtension(key).Length == 0 && kvp.Key.EndsWith(key + ".tex")) return kvp.Value;
            }

            return null;
        }

        public string[] EnumerateChapterScenes(int maxResults = 500)
        {
            if (_innerPathDict.Count == 0) return Array.Empty<string>();

            var chapterFolder = new List<string>();
            var masters = new List<string>();
            var folderRoots = new List<string>();
            var allScenes = new List<string>();

            foreach (var key in _innerPathDict.Keys)
            {
                if (!key.EndsWith(".scn")) continue;
                allScenes.Add(key);

                if (key.StartsWith("scenes/chapter/", StringComparison.OrdinalIgnoreCase))
                {
                    chapterFolder.Add(key);
                    continue;
                }

                var fname = Path.GetFileNameWithoutExtension(key);
                if (string.Equals(fname, "master", StringComparison.OrdinalIgnoreCase))
                {
                    masters.Add(key);
                    continue;
                }

                var dir = Path.GetDirectoryName(key);
                if (!string.IsNullOrEmpty(dir))
                {
                    var parentName = Path.GetFileName(dir);
                    if (string.Equals(parentName, fname, StringComparison.OrdinalIgnoreCase))
                    {
                        folderRoots.Add(key);
                    }
                }
            }

            List<string> picked;
            if (chapterFolder.Count > 0) picked = chapterFolder;
            else if (masters.Count > 0) picked = masters;
            else if (folderRoots.Count > 0) picked = folderRoots;
            else picked = allScenes;

            picked.Sort(NaturalCompare);
            if (picked.Count > maxResults) picked.RemoveRange(maxResults, picked.Count - maxResults);
            return picked.ToArray();
        }

        private static int NaturalCompare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                char ca = a[i], cb = b[j];
                if (char.IsDigit(ca) && char.IsDigit(cb))
                {
                    long na = 0, nb = 0;
                    while (i < a.Length && char.IsDigit(a[i])) { na = na * 10 + (a[i] - '0'); i++; }
                    while (j < b.Length && char.IsDigit(b[j])) { nb = nb * 10 + (b[j] - '0'); j++; }
                    if (na != nb) return na < nb ? -1 : 1;
                }
                else
                {
                    var lc = char.ToLowerInvariant(ca);
                    var rc = char.ToLowerInvariant(cb);
                    if (lc != rc) return lc < rc ? -1 : 1;
                    i++; j++;
                }
            }
            return a.Length.CompareTo(b.Length);
        }

        private static string StripNumericSuffix(string path)
        {
            var lastDot = path.LastIndexOf('.');
            if (lastDot <= 0 || lastDot >= path.Length - 1) return path;
            for (int i = lastDot + 1; i < path.Length; i++)
            {
                if (path[i] < '0' || path[i] > '9') return path;
            }
            return path[..lastDot];
        }

        public override void SaveStartupCache()
        {
        }

        public override void LoadStartupCache()
        {
        }

        public override bool IsArchive(string filename)
        {
            return filename.EndsWith(".pak");
        }

        public override GameArchive GetArchive(string path, string relpath)
        {
            var pak = new PakFile(path, relpath, FileList);
            pak.ReadStructure();
            return pak;
        }

        public override GameArchive CreateArchive(string gamefolder, string relpath) => throw new NotImplementedException();
        public override GameArchive CreateArchive(GameArchiveDirectory dir, string name) => throw new NotImplementedException();
        public override GameArchiveFileInfo CreateFile(GameArchiveDirectory dir, string name, byte[] data, bool overwrite = true) => throw new NotImplementedException();
        public override GameArchiveDirectory CreateDirectory(GameArchiveDirectory dir, string name) => throw new NotImplementedException();
        public override GameArchiveFileInfo CreateFileEntry(string name, string path, ref byte[] data)
        {
            var e = new PakFileEntry
            {
                Name = name,
                Path = path,
                Size = data?.Length ?? 0,
            };
            return e;
        }
        public override void RenameArchive(GameArchive file, string newname) => throw new NotImplementedException();
        public override void RenameEntry(GameArchiveEntry entry, string newname) => throw new NotImplementedException();
        public override void DeleteEntry(GameArchiveEntry entry) => throw new NotImplementedException();
        public override void Defragment(GameArchive file, Action<string, float> progress = null, bool recursive = true) => throw new NotImplementedException();

        public override string ConvertToXml(GameArchiveFileInfo file, byte[] data, out string newfilename, out object infoObject, string folder = "")
        {
            infoObject = null;
            newfilename = file.Name + ".xml";
            return string.Empty;
        }
        public override byte[] ConvertFromXml(string xml, string filename, string folder = "") => null;
        public override string GetXmlFormatName(string filename, out int trimlength)
        {
            trimlength = 4;
            return "RE7 XML";
        }

        public override string ConvertToText(GameArchiveFileInfo file, byte[] data, out string newfilename)
        {
            newfilename = file.Name;
            return TextUtil.GetUTF8Text(data);
        }
        public override byte[] ConvertFromText(string text, string filename) => Encoding.UTF8.GetBytes(text);

        public override TexturePack LoadTexturePack(GameArchiveFileInfo file, byte[] data = null)
        {
            data = EnsureFileData(file, data);
            if (data == null) return null;

            var ext = Path.GetExtension(file.NameLower);
            return ext switch
            {
                ".tex" => new TexFile(file, data),
                _ => null,
            };
        }
        public override PiecePack LoadPiecePack(GameArchiveFileInfo file, byte[] data = null, bool loadDependencies = false)
        {
            data = EnsureFileData(file, data);
            if (data == null)
            {
                return null;
            }

            var stripped = StripNumericSuffix(file.NameLower);
            var ext = Path.GetExtension(stripped);

            if (ext == ".mesh") return LoadMeshFile(file, data, loadDependencies);
            if (ext == ".scn")  return LoadSceneFile(file, data, loadDependencies);

            return null;
        }

        private MeshFile LoadMeshFile(GameArchiveFileInfo file, byte[] data, bool loadDependencies)
        {
            var meshFile = new MeshFile(file, data);

            if (loadDependencies && meshFile.Piece != null)
            {
                var mdf2 = TryLoadSiblingMdf2(file);
                if (mdf2 != null)
                {
                    meshFile.ApplyMaterials(mdf2, this);
                }
            }

            return meshFile;
        }

        private SceneFile LoadSceneFile(GameArchiveFileInfo file, byte[] data, bool loadDependencies)
        {
            var scn = new SceneFile(file, data);
            if (scn.LoadException != null)
            {
                return scn;
            }

            if (loadDependencies)
            {
                scn.LoadExternalScenes(this);
                scn.MergeExternalHierarchies();
                scn.BuildPieces(this);
            }
            return scn;
        }

        private Mdf2File TryLoadSiblingMdf2(GameArchiveFileInfo meshEntry)
        {
            var mdf2Path = meshEntry.PathLower;
            if (!mdf2Path.EndsWith(".mesh"))
            {
                return null;
            }
            mdf2Path = mdf2Path[..^5] + ".mdf2";


            if (!EntryDict.TryGetValue(mdf2Path, out var mdf2Entry))
            {
                return null;
            }
            if (mdf2Entry is not PakFileEntry pf)
            {
                return null;
            }

            var mdf2Data = pf.Archive?.ExtractFile(pf);
            if (mdf2Data == null)
            {
                return null;
            }

            var mdf2 = new Mdf2File(pf, mdf2Data);
            if (mdf2.LoadException != null)
            {
                return null;
            }
            return mdf2;
        }
        public override AudioPack LoadAudioPack(GameArchiveFileInfo file, byte[] data = null) => null;
        public override DataBagPack LoadDataBagPack(GameArchiveFileInfo file, byte[] data = null) => null;
        public override T LoadMetaNode<T>(GameArchiveFileInfo file, byte[] data = null) => default;
    }
}
