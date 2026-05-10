using CodeX.Core.Engine;
using CodeX.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeX.Games.RE7.Files
{
    public class Mdf2File : FilePack
    {
        public const uint MagicValue = 0x0046444D; //"MDF\0"
        public const ushort SupportedVersion = 1;

        public uint Magic;
        public ushort Version;
        public ushort MaterialCount;

        public Mdf2Material[] Materials = Array.Empty<Mdf2Material>();

        public Mdf2File() : base(null) { }
        public Mdf2File(GameArchiveFileInfo info) : base(info) { }
        public Mdf2File(GameArchiveFileInfo info, byte[] data) : base(info)
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
                ReadMaterialHeaders(br);
                ReadTextureHeaders(br);
                ReadPropertyHeaders(br);
            }
            catch (Exception ex)
            {
                LoadException = ex;
            }
        }

        public override byte[] Save() => null; //TODO: implement writeback
        public override void Read(MetaNodeReader reader) { }
        public override void Write(MetaNodeWriter writer) { }

        private void ReadHeader(BinaryReader br)
        {
            Magic = br.ReadUInt32();
            Version = br.ReadUInt16();
            MaterialCount = br.ReadUInt16();
            br.ReadBytes(8); //pad to 16

            if (Magic != MagicValue) throw new InvalidDataException("Invalid MDF2 magic.");
            if (Version != SupportedVersion)
            {
                throw new InvalidDataException($"Unsupported MDF2 version {Version}. Supported version is {SupportedVersion}.");
            }
        }

        private void ReadMaterialHeaders(BinaryReader br)
        {
            Materials = new Mdf2Material[MaterialCount];
            for (int i = 0; i < MaterialCount; i++)
            {
                var m = new Mdf2Material
                {
                    NameOffset = br.ReadUInt64(),
                    NameHash = br.ReadUInt32(),
                    PropertyDataBlockSize = br.ReadUInt32(),
                    PropertyCount = br.ReadUInt32(),
                    TextureCount = br.ReadUInt32(),
                };
                br.ReadUInt64(); //padding
                m.ShaderType = br.ReadUInt32();
                m.Flags = br.ReadUInt32();
                m.PropertyHeadersOffset = br.ReadUInt64();
                m.TextureHeadersOffset = br.ReadUInt64();
                m.FirstMaterialNameOffset = br.ReadUInt64();
                m.PropertiesDataBlockOffset = br.ReadUInt64();
                m.MasterMaterialFilePathOffset = br.ReadUInt64();

                long pos = br.BaseStream.Position;
                br.BaseStream.Position = (long)m.NameOffset;
                m.Name = ReadWString(br);
                br.BaseStream.Position = (long)m.MasterMaterialFilePathOffset;
                m.MasterMaterialFilePath = ReadWString(br);
                br.BaseStream.Position = pos;

                Materials[i] = m;
            }
        }

        private void ReadTextureHeaders(BinaryReader br)
        {
            for (int i = 0; i < Materials.Length; i++)
            {
                var m = Materials[i];
                m.Textures = new Mdf2Texture[m.TextureCount];
                for (int j = 0; j < m.TextureCount; j++)
                {
                    var t = new Mdf2Texture
                    {
                        TypeOffset = br.ReadUInt64(),
                        TypeUtf16Hash = br.ReadUInt32(),
                        TypeAsciiHash = br.ReadUInt32(),
                        FilePathOffset = br.ReadUInt64(),
                    };
                    br.ReadUInt64(); //padding

                    long pos = br.BaseStream.Position;
                    br.BaseStream.Position = (long)t.TypeOffset;
                    t.Type = ReadWString(br);
                    br.BaseStream.Position = (long)t.FilePathOffset;
                    t.FilePath = ReadWString(br);
                    br.BaseStream.Position = pos;

                    m.Textures[j] = t;
                }
            }
        }

        private void ReadPropertyHeaders(BinaryReader br)
        {
            for (int i = 0; i < Materials.Length; i++)
            {
                var m = Materials[i];
                m.Properties = new Mdf2Property[m.PropertyCount];
                for (int j = 0; j < m.PropertyCount; j++)
                {
                    var p = new Mdf2Property
                    {
                        NameOffset = br.ReadUInt64(),
                        NameUtf16Hash = br.ReadUInt32(),
                        NameAsciiHash = br.ReadUInt32(),
                        DataOffset = br.ReadUInt32(),
                        ParameterCount = br.ReadUInt32(),
                    };

                    long pos = br.BaseStream.Position;
                    br.BaseStream.Position = (long)p.NameOffset;
                    p.Name = ReadWString(br);

                    p.Parameters = new float[p.ParameterCount];
                    br.BaseStream.Position = (long)(m.PropertiesDataBlockOffset + p.DataOffset);
                    for (int k = 0; k < p.ParameterCount; k++)
                    {
                        p.Parameters[k] = br.ReadSingle();
                    }

                    br.BaseStream.Position = pos;
                    m.Properties[j] = p;
                }
            }
        }

        private static string ReadWString(BinaryReader br)
        {
            var sb = new StringBuilder();
            char c;
            while ((c = (char)br.ReadUInt16()) != '\0') sb.Append(c);
            return sb.ToString();
        }

        public Mdf2Material FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var m in Materials)
            {
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }
    }

    public class Mdf2Material
    {
        public ulong NameOffset;
        public uint NameHash;
        public uint PropertyDataBlockSize;
        public uint PropertyCount;
        public uint TextureCount;
        public uint ShaderType;
        public uint Flags;
        public ulong PropertyHeadersOffset;
        public ulong TextureHeadersOffset;
        public ulong FirstMaterialNameOffset;
        public ulong PropertiesDataBlockOffset;
        public ulong MasterMaterialFilePathOffset;

        public string Name;
        public string MasterMaterialFilePath;

        public Mdf2Texture[] Textures = Array.Empty<Mdf2Texture>();
        public Mdf2Property[] Properties = Array.Empty<Mdf2Property>();

        public override string ToString() => Name ?? "(unnamed material)";
    }

    public class Mdf2Texture
    {
        public ulong TypeOffset;
        public uint TypeUtf16Hash;
        public uint TypeAsciiHash;
        public ulong FilePathOffset;

        public string Type;
        public string FilePath;

        public override string ToString() => $"{Type} = {FilePath}";
    }

    public class Mdf2Property
    {
        public ulong NameOffset;
        public uint NameUtf16Hash;
        public uint NameAsciiHash;
        public uint DataOffset;
        public uint ParameterCount;

        public string Name;
        public float[] Parameters = Array.Empty<float>();

        public override string ToString() => $"{Name} = [{string.Join(", ", Parameters)}]";
    }
}
