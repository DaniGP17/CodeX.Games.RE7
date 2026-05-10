using System.IO;
using System.Reflection;

namespace CodeX.Games.RE7.RSZ
{
    public static class RszRegistry
    {
        private const string EmbeddedResourceName = "CodeX.Games.RE7.Resources.rszre7.json";

        private static readonly object _lock = new();
        private static RszTypeDb _current;

        public static RszTypeDb Current
        {
            get
            {
                if (_current != null) return _current;
                lock (_lock)
                {
                    if (_current != null) return _current;
                    _current = LoadEmbedded();
                }
                return _current;
            }
            set
            {
                lock (_lock) { _current = value; }
            }
        }

        private static RszTypeDb LoadEmbedded()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new FileNotFoundException(
                    $"Embedded RSZ DB '{EmbeddedResourceName}' not found in {asm.GetName().Name}. " +
                    "Make sure Resources/rszre7.json is present and marked as <EmbeddedResource>.");
            var db = new RszTypeDb();
            db.LoadFromStream(stream);
            return db;
        }
    }
}
