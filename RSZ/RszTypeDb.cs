using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeX.Games.RE7.RSZ
{
    public record RszField(
        [property: JsonPropertyName("align")] int Align,
        [property: JsonPropertyName("array")] bool Array,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("native")] bool Native,
        [property: JsonPropertyName("original_type")] string OriginalType,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("type")] string Type
    );

    public record RszTypeEntry(
        [property: JsonPropertyName("crc")] string Crc,
        [property: JsonPropertyName("fields")] RszField[] Fields,
        [property: JsonPropertyName("name")] string Name
    );

    public class RszTypeDb
    {
        private readonly Dictionary<uint, RszTypeEntry> _types = new();

        public IReadOnlyDictionary<uint, RszTypeEntry> Types => _types;
        public int Count => _types.Count;

        public void LoadFromStream(Stream stream)
        {
            _types.Clear();
            var raw = JsonSerializer.Deserialize<Dictionary<string, RszTypeEntry>>(stream)
                      ?? throw new InvalidDataException("RSZ JSON is null or invalid.");

            foreach (var (key, entry) in raw)
            {
                if (uint.TryParse(key, System.Globalization.NumberStyles.HexNumber, null, out var hash))
                    _types[hash] = entry;
            }
        }

        public RszTypeEntry GetByHash(uint hash) =>
            _types.TryGetValue(hash, out var entry) ? entry : null;

        public RszField[] GetFieldsByHash(uint hash) =>
            _types.TryGetValue(hash, out var entry) ? entry.Fields : null;

        public RszTypeEntry GetByName(string name) =>
            _types.Values.FirstOrDefault(e => e.Name == name);
    }
}
