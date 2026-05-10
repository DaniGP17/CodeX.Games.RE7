using CodeX.Core.Engine;
using CodeX.Games.RE7.PAK;
using System.IO;

namespace CodeX.Games.RE7
{
    public class RE7Game : Game
    {
        public override string Name => "Resident Evil 7 Biohazard";
        public override string ShortName => "RE7";
        public override string GameFolder { get => GameFolderSetting.GetString(); set => GameFolderSetting.Set(value); }
        public override string GamePathPrefix => "RE7\\";
        public override bool RequiresGameFolder => true;
        public override bool Enabled { get => GameEnabledSetting.GetBool(); set => GameEnabledSetting.Set(value); }
        public override bool EnableMapView => true;
        public override FileTypeIcon Icon => FileTypeIcon.Application;
        public override string HashAlgorithm => "Murmur3";
        public override int DefaultTextureChannelMaskIndex => 1; //RE7 uses the alpha channel for metallic, not transparency — show RGB only by default



        public static Setting GameFolderSetting = Settings.Register("RE7.GameFolder", SettingType.String, "C:\\Program Files (x86)\\Steam\\steamapps\\common\\RESIDENT EVIL 7 biohazard");
        public static Setting GameEnabledSetting = Settings.Register("RE7.Enabled", SettingType.Bool, true);

        public const string ExecutableName = "re7.exe";
        public const string FilesPath = "natives\\stm\\";

        public override bool CheckGameFolder(string folder)
        {
            return Directory.Exists(folder) && File.Exists(Path.Combine(folder, ExecutableName));
        }

        public override bool AutoDetectGameFolder(out string source)
        {
            source = null;
            return false; //TODO: Steam library / registry detection
        }

        public override FileManager CreateFileManager()
        {
            return new RE7FileManager(this);
        }

        public override Level GetMapLevel()
        {
            return new RE7Map(this);
        }

        public override Setting[] GetMapSettings()
        {
            //MainMenuGamesPanel.SelectGame() calls this each time the user clicks RE7 in
            //the games list, so we lazily warm up the FileManager here and refresh the
            //chapter dropdown from the actual PAK contents. The first click pays the
            //archive-index cost (a few seconds); subsequent clicks reuse the cached state.
            var fman = GetFileManager() as RE7FileManager;
            if (fman != null)
            {
                var chapters = fman.EnumerateChapterScenes();
                if (chapters.Length > 0)
                {
                    RE7Map.ChapterSetting.DropDownOptions = chapters;
                }
            }
            return new[] { RE7Map.ChapterSetting, RE7Map.StartPositionSetting };
        }
    }
}
