using CodeX.Core.Engine;
using CodeX.Core.Numerics;
using CodeX.Games.RE7.Files;
using CodeX.Games.RE7.PAK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace CodeX.Games.RE7
{

    public class RE7Map : Level
    {
        public RE7Game Game;
        public RE7FileManager FileManager;
        public SceneFile RootScene;
        public List<RE7SceneGroup> SceneGroups = new();
        public string LoadedChapter;

        private volatile SceneFile _pendingScene;
        private volatile string _pendingChapter;

        public static Setting ChapterSetting = Settings.Register(
            "RE7Map.Chapter", SettingType.String, "",
            "Master .scn path inside the game files (e.g. level/main02/main02.scn)",
            false, false, Array.Empty<string>());
        public static Setting StartPositionSetting = Settings.Register(
            "RE7Map.StartPosition", SettingType.Vector3, new Vector3(0, 0, 0));


        public RE7Map(RE7Game game) : base("RE7 Map Level")
        {
            Game = game;
            DefaultSpawnPoint = StartPositionSetting.GetVector3();
            BoundingBox = new BoundingBox(new Vector3(-100000.0f), new Vector3(100000.0f));
            IsLoading = true;

            InitRenderData();
        }


        public override void Update(float elapsed)
        {
            var pending = _pendingScene;
            if (pending != null)
            {
                _pendingScene = null;
                InstallScene(_pendingChapter, pending);
                _pendingChapter = null;
                IsLoading = false;
                Commands.TryExecute("Sidebar.ReloadWorldSettings");
            }
            base.Update(elapsed);
        }

        public override void ContentThreadProc()
        {
            // One-shot heavy load on first visit to this level. Mirrors StreamingLevelBase's
            // StreamingInit gate (we don't subclass it because RE7 isn't streamed at runtime
            // - once a chapter is loaded, all its pieces stay resident). The parse runs here
            // on the content thread; the resulting SceneFile is handed to the update thread
            // via _pendingScene to avoid mutating Level.Entities concurrently with iteration
            // in Level.UpdateBounds / Level.Update.
            if (RootScene == null && IsLoading && _pendingScene == null)
            {
                var chapter = ChapterSetting.GetString();
                var parsed = ParseChapterOffThread(chapter);
                if (parsed == null)
                {
                    IsLoading = false;
                }
                else
                {
                    _pendingChapter = chapter;
                    _pendingScene = parsed;
                }
            }
            base.ContentThreadProc();
        }

        private SceneFile ParseChapterOffThread(string chapterPath)
        {
            FileManager ??= Game.GetFileManager() as RE7FileManager;
            if (FileManager == null) return null;
            if (string.IsNullOrEmpty(chapterPath)) return null;

            var entry = FileManager.FindEntryByInnerPath(chapterPath);
            if (entry == null)
            {
                return null;
            }

            var data = entry.Archive?.ExtractFile(entry);
            if (data == null) return null;

            return FileManager.LoadPiecePack(entry, data, true) as SceneFile;
        }

        private void InstallScene(string chapter, SceneFile scene)
        {
            Clear();
            SceneGroups.Clear();
            LoadedChapter = chapter;
            RootScene = scene;
            BuildSceneGroupsAndEntities();
        }

        public void ClearChapter()
        {
            Clear();
            SceneGroups.Clear();
            RootScene = null;
            LoadedChapter = null;
        }


        private void BuildSceneGroupsAndEntities()
        {
            var rules = RE7ChapterGroups.GetGroupsForChapter(LoadedChapter);
            if (rules != null && rules.Length > 0)
            {
                BuildSceneGroupsByRules(rules);
            }
            else
            {
                BuildSceneGroupsPerSourceScene();
            }
        }

        private void BuildSceneGroupsByRules(RE7ChapterGroupRule[] rules)
        {
            var groups = new RE7SceneGroup[rules.Length];
            for (int i = 0; i < rules.Length; i++)
            {
                groups[i] = new RE7SceneGroup(this, rules[i].Name, rules[i].Name);
                SceneGroups.Add(groups[i]);
            }

            if (RootScene.Pieces == null) return;

            var chapterName = Path.GetFileNameWithoutExtension(LoadedChapter ?? "");

            foreach (var kvp in RootScene.Pieces)
            {
                var piece = kvp.Value;
                if (piece == null) continue;

                var ent = new Entity(piece);
                ent.Scale = new Vector3(piece.MetersPerUnit);
                ent.UpdateBounds();
                Add(ent);

                var sourceKey = "";
                RootScene.PieceSourceScenes?.TryGetValue(kvp.Key, out sourceKey);
                var sceneName = string.IsNullOrEmpty(sourceKey)
                    ? chapterName
                    : Path.GetFileNameWithoutExtension(sourceKey);

                RE7SceneGroup target = null;
                for (int i = 0; i < rules.Length; i++)
                {
                    if (rules[i].Matches?.Invoke(sceneName) == true)
                    {
                        target = groups[i];
                        break;
                    }
                }
                target ??= groups[rules.Length - 1];
                target.Entities.Add(ent);
            }

            for (int i = 0; i < groups.Length; i++)
            {
                groups[i].SetEnabled(i == 0);
            }
        }

        private void BuildSceneGroupsPerSourceScene()
        {
            var buckets = new Dictionary<string, RE7SceneGroup>(StringComparer.OrdinalIgnoreCase);

            string DisplayName(string sourceKey)
            {
                if (string.IsNullOrEmpty(sourceKey))
                    return "(master) " + Path.GetFileNameWithoutExtension(LoadedChapter ?? "");
                return Path.GetFileNameWithoutExtension(sourceKey);
            }

            var masterGroup = new RE7SceneGroup(this, "", DisplayName(""));
            buckets[""] = masterGroup;
            SceneGroups.Add(masterGroup);

            if (RootScene.ResolvedExternalScenes != null)
            {
                foreach (var key in RootScene.ResolvedExternalScenes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    var g = new RE7SceneGroup(this, key, DisplayName(key));
                    buckets[key] = g;
                    SceneGroups.Add(g);
                }
            }

            if (RootScene.Pieces != null)
            {
                foreach (var kvp in RootScene.Pieces)
                {
                    var piece = kvp.Value;
                    if (piece == null) continue;

                    var ent = new Entity(piece);
                    ent.Scale = new Vector3(piece.MetersPerUnit);
                    ent.UpdateBounds();
                    Add(ent);

                    var sourceKey = "";
                    RootScene.PieceSourceScenes?.TryGetValue(kvp.Key, out sourceKey);
                    if (sourceKey == null || !buckets.TryGetValue(sourceKey, out var group))
                        group = masterGroup;
                    group.Entities.Add(ent);
                }
            }
        }


        public override BaseField[] GetFields()
        {
            return [
                new BaseField("Chapter", BaseFieldType.String),
                new BaseField("Scenes", BaseFieldType.ObjectArray)
            ];
        }
        public override void GetFieldValue(BaseField field, out BaseValue value)
        {
            switch (field?.Name)
            {
                case "Chapter": value = new(LoadedChapter ?? ""); break;
                case "Scenes": value = new(SceneGroups.ToArray()); break;
                default: value = default; break;
            }
        }
        public override BaseObject[] GetChildObjects()
        {
            return SceneGroups.ToArray();
        }
    }


    public class RE7SceneGroup : BaseObject
    {
        public RE7Map Map;
        public string SourceScenePath;
        public List<Entity> Entities = new();

        public RE7SceneGroup(RE7Map map, string sourceScenePath, string displayName)
        {
            Map = map;
            SourceScenePath = sourceScenePath;
            Name = displayName;
        }

        public override void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            for (int i = 0; i < Entities.Count; i++)
            {
                var e = Entities[i];
                if (e == null) continue;
                e.Enabled = enabled;
            }
        }

        public override BaseObject GetContainer() => Map;

        public override string ToString()
        {
            var count = Entities?.Count ?? 0;
            return $"{Name} ({count} piece{(count == 1 ? "" : "s")})";
        }
    }
}
