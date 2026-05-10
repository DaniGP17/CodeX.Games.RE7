using System.IO;
using System.Text;

namespace CodeX.Games.RE7.Common
{
    internal static class BinaryReaderExtensions
    {
        public static void Align(this BinaryReader reader, int alignment)
        {
            if (alignment <= 1) return;
            var pos = reader.BaseStream.Position;
            var rem = pos % alignment;
            if (rem != 0)
            {
                reader.BaseStream.Seek(alignment - rem, SeekOrigin.Current);
            }
        }

        public static string ReadNullTerminatedString(this BinaryReader reader, long? position = null, Encoding encoding = null)
        {
            encoding ??= Encoding.UTF8;
            var stream = reader.BaseStream;
            var orig = stream.Position;
            if (position.HasValue) stream.Seek(position.Value, SeekOrigin.Begin);

            int charSize = (encoding == Encoding.Unicode || encoding == Encoding.BigEndianUnicode) ? 2 : 1;

            var sb = new System.Collections.Generic.List<byte>();
            while (true)
            {
                var chunk = reader.ReadBytes(charSize);
                if (chunk.Length < charSize) break;

                bool isNull = true;
                for (int i = 0; i < chunk.Length; i++) if (chunk[i] != 0) { isNull = false; break; }
                if (isNull) break;
                sb.AddRange(chunk);
            }

            var result = encoding.GetString(sb.ToArray());
            if (position.HasValue) stream.Seek(orig, SeekOrigin.Begin);
            return result;
        }

        public static string ReadFixedString(this BinaryReader reader, int length, Encoding encoding = null)
        {
            encoding ??= Encoding.Unicode;
            var bytes = reader.ReadBytes(length);
            if (encoding == Encoding.Unicode || encoding == Encoding.BigEndianUnicode)
            {
                for (int i = 0; i < bytes.Length - 1; i += 2)
                {
                    if (bytes[i] == 0 && bytes[i + 1] == 0) return encoding.GetString(bytes, 0, i);
                }
                return encoding.GetString(bytes);
            }
            else
            {
                var nulIdx = System.Array.IndexOf(bytes, (byte)0);
                if (nulIdx >= 0) return encoding.GetString(bytes, 0, nulIdx);
                return encoding.GetString(bytes);
            }
        }
    }
}
