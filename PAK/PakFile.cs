using CodeX.Core.Engine;
using CodeX.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using TC = System.ComponentModel.TypeConverterAttribute;
using EXP = System.ComponentModel.ExpandableObjectConverter;

namespace CodeX.Games.RE7.PAK
{
    [TC(typeof(EXP))] public class PakFile : GameArchive
    {
        public const uint MagicValue = 0x414B504B; //"KPKA"
        public const uint SupportedVersion = 4;

        public uint Magic { get; set; }
        public uint Version { get; set; }
        public uint EntryCount { get; set; }
        public uint CheckSum { get; set; }

        public PakFileList FileList { get; set; }

        public PakFile(string fpath, string relpath, PakFileList fileList)
        {
            var fi = new FileInfo(fpath);
            Name = fi.Name;
            Path = relpath.ToLowerInvariant();
            FilePath = fpath;
            Size = fi.Length;
            FileList = fileList;
        }

        public override void ReadStructure(BinaryReader br)
        {
            Magic = br.ReadUInt32();
            Version = br.ReadUInt32();
            EntryCount = br.ReadUInt32();
            CheckSum = br.ReadUInt32();

            if (Magic != MagicValue)
            {
                throw new Exception("Invalid PAK archive (expected KPKA magic).");
            }
            if (Version != SupportedVersion)
            {
                throw new Exception($"Unsupported PAK version {Version} (expected {SupportedVersion}).");
            }

            AllEntries = new List<GameArchiveEntry>();
            var files = new List<PakFileEntry>((int)EntryCount);

            for (uint i = 0; i < EntryCount; i++)
            {
                var fe = new PakFileEntry();
                fe.Archive = this;
                fe.ReadHeader(br, FileList);
                files.Add(fe);
                AllEntries.Add(fe);
            }

            BuildDirectoryTree(files);
        }

        private void BuildDirectoryTree(List<PakFileEntry> files)
        {
            var root = new PakDirectoryEntry
            {
                Archive = this,
                Name = Name,
                Path = Path,
            };
            Root = root;

            var dirCache = new Dictionary<string, PakDirectoryEntry>();
            dirCache[string.Empty] = root;

            foreach (var file in files)
            {
                var rel = file.Path;
                var lastSep = rel.LastIndexOfAny(new[] { '/', '\\' });
                var dirRel = lastSep >= 0 ? rel.Substring(0, lastSep) : string.Empty;
                var fileName = lastSep >= 0 ? rel.Substring(lastSep + 1) : rel;

                var dir = EnsureDirectory(root, dirCache, dirRel);
                file.Name = fileName;
                file.Path = string.IsNullOrEmpty(dirRel)
                    ? Path + "\\" + fileName
                    : Path + "\\" + dirRel.Replace('/', '\\') + "\\" + fileName;
                file.Parent = dir;
                dir.Files.Add(file);
            }
        }

        private PakDirectoryEntry EnsureDirectory(PakDirectoryEntry root, Dictionary<string, PakDirectoryEntry> cache, string relPath)
        {
            relPath = relPath.Replace('\\', '/');
            if (cache.TryGetValue(relPath, out var existing)) return existing;

            var parts = relPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            var accum = string.Empty;
            foreach (var part in parts)
            {
                accum = string.IsNullOrEmpty(accum) ? part : accum + "/" + part;
                if (!cache.TryGetValue(accum, out var next))
                {
                    next = new PakDirectoryEntry
                    {
                        Archive = this,
                        Parent = current,
                        Name = part,
                        Path = current.Path + "\\" + part,
                    };
                    current.Directories.Add(next);
                    cache[accum] = next;
                }
                current = next;
            }
            return current;
        }

        public override byte[] ExtractFile(GameArchiveFileInfo f, bool compressed = false)
        {
            if (f is not PakFileEntry pf) return null;

            try
            {
                using var fs = File.OpenRead(GetPhysicalFilePath());
                using var br = new BinaryReader(fs);

                fs.Seek(pf.Offset, SeekOrigin.Begin);
                var data = br.ReadBytes((int)pf.CompressedSize);

                if (compressed || !pf.IsCompressed)
                {
                    return data;
                }

                return pf.CompressionType switch
                {
                    PakCompressionType.Uncompressed => data,
                    PakCompressionType.Deflated => DecompressDeflate(data, (int)pf.UncompressedSize),
                    PakCompressionType.ZStandard => DecompressZStandard(data, (int)pf.UncompressedSize),
                    _ => throw new NotSupportedException($"Unsupported PAK compression type: {pf.CompressionType}"),
                };
            }
            catch
            {
                return null;
            }
        }

        public override bool EnsureEditable(Func<string, string, bool> confirm)
        {
            throw new NotImplementedException();
        }

        private static byte[] DecompressDeflate(byte[] compressed, int uncompressedSize)
        {
            using var input = new MemoryStream(compressed);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(uncompressedSize);
            deflate.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] DecompressZStandard(byte[] compressed, int uncompressedSize)
        {
            using var input = new MemoryStream(compressed);
            using var zstd = new ZstandardStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(uncompressedSize);
            zstd.CopyTo(output);
            return output.ToArray();
        }
    }

    public enum PakCompressionType : byte
    {
        Uncompressed = 0,
        Deflated = 1,
        ZStandard = 2,
        Unknown = 0xFF,
    }

    [TC(typeof(EXP))] public class PakDirectoryEntry : GameArchiveDirectory
    {
        public List<GameArchiveDirectory> Directories { get; set; } = new();
        public List<GameArchiveFileInfo> Files { get; set; } = new();
        public GameArchive Archive { get; set; }
        public GameArchiveDirectory Parent { get; set; }
        public long Size { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public string NameLower
        {
            get
            {
                if (_NameLower == null) _NameLower = Name?.ToLowerInvariant();
                return _NameLower;
            }
        }
        public string PathLower
        {
            get
            {
                if (_PathLower == null) _PathLower = Path?.ToLowerInvariant();
                return _PathLower;
            }
        }
        public string Attributes => "";
        private string _NameLower;
        private string _PathLower;

        public override string ToString() => Name ?? Path;
    }

    [TC(typeof(EXP))] public class PakFileEntry : GameArchiveFileInfo
    {
        public bool IsArchive => false;
        public GameArchive Archive { get; set; }
        public GameArchiveDirectory Parent { get; set; }
        public long Size { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public string NameLower
        {
            get
            {
                if (_NameLower == null) _NameLower = Name?.ToLowerInvariant();
                return _NameLower;
            }
        }
        public string PathLower
        {
            get
            {
                if (_PathLower == null) _PathLower = Path?.ToLowerInvariant();
                return _PathLower;
            }
        }
        public string Attributes
        {
            get
            {
                if (!IsCompressed) return "";
                return CompressionType switch
                {
                    PakCompressionType.Deflated => "Deflate",
                    PakCompressionType.ZStandard => "ZStandard",
                    _ => "Compressed",
                };
            }
        }
        private string _NameLower;
        private string _PathLower;

        public uint LowerCaseHash { get; set; }
        public uint UpperCaseHash { get; set; }
        public long Offset { get; set; }
        public long CompressedSize { get; set; }
        public long UncompressedSize { get; set; }
        public byte Flags0 { get; set; }
        public byte Flags1 { get; set; }
        public byte Flags2 { get; set; }
        public byte Flags3 { get; set; }
        public byte Flags4 { get; set; }
        public byte Flags5 { get; set; }
        public byte Flags6 { get; set; }
        public byte Flags7 { get; set; }
        public long Checksum { get; set; }

        public bool IsCompressed => CompressedSize != UncompressedSize;
        public PakCompressionType CompressionType
        {
            get
            {
                var t = Flags0 & 0x0F;
                return t switch
                {
                    0 => PakCompressionType.Uncompressed,
                    1 => PakCompressionType.Deflated,
                    2 => PakCompressionType.ZStandard,
                    _ => PakCompressionType.Unknown,
                };
            }
        }

        public void ReadHeader(BinaryReader br, PakFileList fileList)
        {
            LowerCaseHash = br.ReadUInt32();
            UpperCaseHash = br.ReadUInt32();
            Offset = br.ReadInt64();
            CompressedSize = br.ReadInt64();
            UncompressedSize = br.ReadInt64();
            Flags0 = br.ReadByte();
            Flags1 = br.ReadByte();
            Flags2 = br.ReadByte();
            Flags3 = br.ReadByte();
            Flags4 = br.ReadByte();
            Flags5 = br.ReadByte();
            Flags6 = br.ReadByte();
            Flags7 = br.ReadByte();
            Checksum = br.ReadInt64();

            Size = UncompressedSize;

            if (fileList != null && fileList.TryGetPath(LowerCaseHash, out var resolved))
            {
                Path = resolved;
            }
            else
            {
                Path = $"unknown/{LowerCaseHash:X8}";
            }
        }

        public override string ToString() => Path ?? LowerCaseHash.ToString("X8");
    }
}
