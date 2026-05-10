using CodeX.Core.Engine;
using System;
using System.IO;

namespace CodeX.Games.RE7.Files
{
    public class TexFile : TexturePack
    {
        public const uint MagicValue = 0x00584554; //"TEX\0"
        public const uint SupportedVersion = 35; //RE7

        public uint Magic { get; set; }
        public uint Version { get; set; }
        public ushort Width { get; set; }
        public ushort Height { get; set; }
        public ushort DepthAndType { get; set; }
        public ushort MipInfo { get; set; }
        public uint Format { get; set; }
        public ulong TextureLayoutFlags { get; set; }
        public uint StreamingFlags { get; set; }
        public uint DataSizeTotal { get; set; }
        public ushort TileMode { get; set; }
        public ushort Alignment { get; set; }
        public TexMipHeader[] Mips { get; set; } = Array.Empty<TexMipHeader>();

        public int NumImages => MipInfo & 0x0FFF;
        public int MipsPerImage => MipInfo >> 12;
        public Texture TextureObject { get; set; }

        public TexFile() : base() { }
        public TexFile(GameArchiveFileInfo info) : base(info) { }
        public TexFile(GameArchiveFileInfo info, byte[] data) : base(info)
        {
            Load(data);
        }

        public override void Load(byte[] data)
        {
            try
            {
                ReadHeader(data);
                ReadMipHeaders(data);
                BuildTexture(data);
            }
            catch (Exception ex)
            {
                LoadException = ex;
            }
        }

        private void ReadHeader(byte[] data)
        {
            if (data.Length < 0x28) throw new InvalidDataException("Texture data too small for TEX header.");

            Magic = BitConverter.ToUInt32(data, 0x00);
            Version = BitConverter.ToUInt32(data, 0x04);
            Width = BitConverter.ToUInt16(data, 0x08);
            Height = BitConverter.ToUInt16(data, 0x0A);
            DepthAndType = BitConverter.ToUInt16(data, 0x0C);
            MipInfo = BitConverter.ToUInt16(data, 0x0E);
            Format = BitConverter.ToUInt32(data, 0x10);
            TextureLayoutFlags = BitConverter.ToUInt64(data, 0x14);
            StreamingFlags = BitConverter.ToUInt32(data, 0x1C);
            DataSizeTotal = BitConverter.ToUInt32(data, 0x20);
            TileMode = BitConverter.ToUInt16(data, 0x24);
            Alignment = BitConverter.ToUInt16(data, 0x26);

            if (Magic != MagicValue) throw new InvalidDataException("Invalid TEX magic.");
            if (Version != SupportedVersion) throw new InvalidDataException($"Unsupported TEX version {Version} (expected {SupportedVersion}).");
            if (Width == 0 || Height == 0 || Width > 16384 || Height > 16384) throw new InvalidDataException($"Invalid texture dimensions {Width}x{Height}.");
            if (MipsPerImage == 0 || MipsPerImage > 16) throw new InvalidDataException($"Invalid mip count {MipsPerImage}.");
        }

        private void ReadMipHeaders(byte[] data)
        {
            int total = NumImages * MipsPerImage;
            int headersBase = 0x28;
            int headersEnd = headersBase + total * 16;
            if (data.Length < headersEnd) throw new InvalidDataException("Texture data too small for mip headers.");

            Mips = new TexMipHeader[total];
            for (int i = 0; i < total; i++)
            {
                int off = headersBase + (i * 16);
                Mips[i] = new TexMipHeader
                {
                    Offset = BitConverter.ToUInt64(data, off),
                    Padding = BitConverter.ToUInt32(data, off + 8),
                    Size = BitConverter.ToUInt32(data, off + 12),
                };
            }
        }

        private void BuildTexture(byte[] data)
        {
            if (Mips.Length == 0) return;

            var mip0 = Mips[0];
            if (mip0.Offset + mip0.Size > (ulong)data.Length)
            {
                throw new InvalidDataException("Mip 0 data out of bounds.");
            }

            var pixels = new byte[mip0.Size];
            Buffer.BlockCopy(data, (int)mip0.Offset, pixels, 0, (int)mip0.Size);

            var dxgi = (TextureFormat)Format;
            TextureFormats.ComputePitch(TextureFormats.GetDXGIFormat(dxgi), Width, Height, out var rowPitch, out _, 0);

            var tex = new Texture
            {
                Name = FileInfo?.Name ?? FilePath ?? "RE7 Texture",
                Format = dxgi,
                Width = Width,
                Height = Height,
                Depth = 1,
                Stride = (ushort)rowPitch,
                MipLevels = 1,
                Data = pixels,
                Sampler = TextureSampler.AnisotropicWrap,
                FilePack = this,
            };

            Textures = new() { { tex.Name.ToLowerInvariant(), tex } };
            TextureObject = tex;
        }

        public override byte[] Save()
        {
            //TODO: implement TEX writeback
            return null;
        }
    }

    public struct TexMipHeader
    {
        public ulong Offset;
        public uint Padding;
        public uint Size;
    }
}
