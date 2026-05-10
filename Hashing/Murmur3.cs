using System;
using System.Text;

namespace CodeX.Games.RE7.Hashing
{
    public static class Murmur3
    {
        private const uint DefaultSeed = 0xFFFFFFFF;
        private const uint C1 = 0xcc9e2d51;
        private const uint C2 = 0x1b873593;
        private const uint MixConstant = 0xe6546b64;

        public static uint HashFilePath(string filePath, bool lower)
        {
            var path = (filePath ?? string.Empty).Replace('\\', '/');
            path = lower ? path.ToLowerInvariant() : path.ToUpperInvariant();
            var bytes = Encoding.Unicode.GetBytes(path);
            return Hash(bytes, DefaultSeed);
        }

        public static uint Hash(byte[] data, uint seed = DefaultSeed)
        {
            uint hash = seed;
            int blocks = data.Length / 4;
            int index = 0;

            for (int i = 0; i < blocks; i++)
            {
                uint k = BitConverter.ToUInt32(data, index);
                index += 4;
                k = MixKey(k);
                hash = MixHash(hash, k);
            }

            int remaining = data.Length - index;
            if (remaining > 0)
            {
                uint tail = 0;
                for (int i = remaining - 1; i >= 0; i--)
                {
                    tail <<= 8;
                    tail |= data[index + i];
                }
                tail = MixKey(tail);
                hash ^= tail;
            }

            hash ^= (uint)data.Length;
            hash = Fmix32(hash);
            return hash;
        }

        private static uint MixKey(uint k)
        {
            k *= C1;
            k = (k << 15) | (k >> 17);
            k *= C2;
            return k;
        }

        private static uint MixHash(uint hash, uint k)
        {
            hash ^= k;
            hash = (hash << 13) | (hash >> 19);
            return hash * 5 + MixConstant;
        }

        private static uint Fmix32(uint h)
        {
            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae35;
            h ^= h >> 16;
            return h;
        }
    }
}
