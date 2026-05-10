using System;
using System.Collections.Generic;

namespace CodeX.Games.RE7
{

    public class RE7ChapterGroupRule
    {
        public string Name;
        public Func<string, bool> Matches;

        public RE7ChapterGroupRule(string name, Func<string, bool> matches)
        {
            Name = name;
            Matches = matches;
        }
    }

    public static class RE7ChapterGroups
    {

        private static readonly RE7ChapterGroupRule[] Chapter1Rules =
        {
            new("House",
                s => s.StartsWith("c01_", StringComparison.OrdinalIgnoreCase)
                  && !s.Contains("outside", StringComparison.OrdinalIgnoreCase)),
            new("Outside",
                s => s.Contains("outside", StringComparison.OrdinalIgnoreCase)),
            new("Other",
                _ => true),
        };

        private static readonly RE7ChapterGroupRule[] Chapter3Rules =
        {
            new("MainHouse",
                s => s.Contains("c03_mainhouse", StringComparison.OrdinalIgnoreCase)),
            new("Boat",
                s => s.Contains("c03_boat", StringComparison.OrdinalIgnoreCase)),
            new("Cow",
                s => s.Contains("c03_cow", StringComparison.OrdinalIgnoreCase)),
            new("Garden",
                s => s.Contains("c03_garden", StringComparison.OrdinalIgnoreCase)),
            new("Gh1",
                s => s.Contains("c03_gh1", StringComparison.OrdinalIgnoreCase)),
            new("Gh2",
                s => s.Contains("c03_gh2", StringComparison.OrdinalIgnoreCase)),
            new("GhOther",
                s => s.StartsWith("c03_gh", StringComparison.OrdinalIgnoreCase)
                  && !s.Contains("c03_gh1", StringComparison.OrdinalIgnoreCase)
                  && !s.Contains("c03_gh2", StringComparison.OrdinalIgnoreCase)),
            new("Leftarea",
                s => s.Contains("c03_leftarea", StringComparison.OrdinalIgnoreCase)),
            new("Rightarea",
                s => s.Contains("c03_rightarea", StringComparison.OrdinalIgnoreCase)),
            new("OldHouse",
                s => s.Contains("c03_oldhouse", StringComparison.OrdinalIgnoreCase)),
            new("Outside",
                s => s.Contains("outside", StringComparison.OrdinalIgnoreCase)),
            new("Other",
                _ => true),
        };

        private static readonly Dictionary<string, RE7ChapterGroupRule[]> ByChapterPath =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["scenes/chapter/chapter1.scn"] = Chapter1Rules,
            ["scenes/chapter/chapter3.scn"] = Chapter3Rules,
        };


        public static RE7ChapterGroupRule[] GetGroupsForChapter(string chapterPath)
        {
            var key = NormalisePath(chapterPath);
            if (string.IsNullOrEmpty(key)) return null;
            ByChapterPath.TryGetValue(key, out var rules);
            return rules;
        }

        private static string NormalisePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            var p = path.Replace('\\', '/').ToLowerInvariant().TrimStart('/');

            const string prefix = "natives/stm/";
            if (p.StartsWith(prefix)) p = p[prefix.Length..];

            var lastDot = p.LastIndexOf('.');
            if (lastDot > 0 && lastDot < p.Length - 1)
            {
                bool allDigits = true;
                for (int i = lastDot + 1; i < p.Length; i++)
                {
                    if (p[i] < '0' || p[i] > '9') { allDigits = false; break; }
                }
                if (allDigits) p = p[..lastDot];
            }

            return p;
        }
    }
}
