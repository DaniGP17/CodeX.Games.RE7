using CodeX.Games.RE7.Hashing;
using System.Collections.Generic;
using System.IO;

namespace CodeX.Games.RE7.PAK
{
    public class PakFileList
    {
        private readonly Dictionary<uint, string> _byLowerHash = new();

        public int Count => _byLowerHash.Count;

        public void LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            _byLowerHash.Clear();
            foreach (var raw in File.ReadAllLines(filePath))
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                //RE Engine appends a numeric version (e.g. ".220128762") that participates in the hash,
                //but should be hidden from the displayed file path.
                var hash = Murmur3.HashFilePath(line, lower: true);
                _byLowerHash[hash] = StripVersionSuffix(line);
            }
        }

        private static string StripVersionSuffix(string path)
        {
            var lastDot = path.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == path.Length - 1) return path;
            for (int i = lastDot + 1; i < path.Length; i++)
            {
                if (path[i] < '0' || path[i] > '9') return path;
            }
            return path.Substring(0, lastDot);
        }

        public bool TryGetPath(uint lowerCaseHash, out string path)
        {
            return _byLowerHash.TryGetValue(lowerCaseHash, out path);
        }
    }
}
