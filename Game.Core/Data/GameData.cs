using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Math;

namespace Game.Core.Data
{
    /// <summary>The game's numbers and rules-as-data: every *.json under
    /// Game.Core/Data, one tree. A file's path is its key: Data/bots/archetypes.json
    /// is "bots.archetypes", a field in it "bots.archetypes.x". Variants
    /// (Data/variants/&lt;set&gt;/&lt;name&gt;.json) are overrides laid on top; the
    /// gamedesigner compares them on bots and on Nick.</summary>
    public sealed class GameData
    {
        public const string VariantsDir = "variants";
        public JsonValue Root { get; }
        public string Variant { get; }

        public GameData(JsonValue root, string variant = "")
        {
            Root = root;
            Variant = variant;
        }

        /// <summary>Reads a Data folder. Files are taken in ordinal order, so the
        /// tree is the same on every machine.</summary>
        public static GameData Load(string dataDir)
        {
            var root = JsonValue.Object();
            var files = new List<string>(Directory.GetFiles(dataDir, "*.json", SearchOption.AllDirectories));
            files.Sort(StringComparer.Ordinal);
            foreach (var file in files)
            {
                string rel = file.Substring(dataDir.Length).TrimStart('/', '\\');
                string[] parts = rel.Substring(0, rel.Length - ".json".Length).Split('/', '\\');
                if (parts[0] == VariantsDir) continue;
                JsonValue value;
                try { value = Json.Parse(File.ReadAllText(file)); }
                catch (FormatException e) { throw new FormatException($"{rel}: {e.Message}"); }
                var node = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var next = node[parts[i]];
                    if (next == null) { next = JsonValue.Object(); node.Set(parts[i], next); }
                    node = next;
                }
                node.Set(parts[parts.Length - 1], value);
            }
            return new GameData(root);
        }

        /// <summary>Variant names of a set ("sprint-2" → ["a", "b"]), sorted.</summary>
        public static IReadOnlyList<string> VariantNames(string dataDir, string set)
        {
            var dir = Path.Combine(dataDir, VariantsDir, set);
            var names = new List<string>();
            if (!Directory.Exists(dir)) return names;
            foreach (var f in Directory.GetFiles(dir, "*.json")) names.Add(Path.GetFileNameWithoutExtension(f));
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>Variant sets present ("sprint-2", …), sorted; the newest is last.</summary>
        public static IReadOnlyList<string> VariantSets(string dataDir)
        {
            var dir = Path.Combine(dataDir, VariantsDir);
            var sets = new List<string>();
            if (!Directory.Exists(dir)) return sets;
            foreach (var d in Directory.GetDirectories(dir)) sets.Add(Path.GetFileName(d));
            sets.Sort(StringComparer.Ordinal);
            return sets;
        }

        public GameData WithVariant(string dataDir, string set, string name)
        {
            var path = Path.Combine(dataDir, VariantsDir, set, name + ".json");
            var over = Json.Parse(File.ReadAllText(path));
            return new GameData(Root.MergedWith(over), set + "/" + name);
        }

        /// <summary>The same data with the human's owner type set (game.human_owner); the bots' owners stay.</summary>
        public GameData WithHumanOwner(string owner) =>
            new GameData(Root.MergedWith(Json.Parse("{\"game\":{\"human_owner\":\"" + owner + "\"}}")), Variant);

        /// <summary>The same data playing arena <paramref name="name"/>: <c>game.arena</c> and, laid on top, the arena's own
        /// "data" block (Data/arenas/&lt;name&gt;.json → how many players, how long the match and the session run).
        /// Apply it before a variant, so a variant's numbers still win.</summary>
        public GameData WithArena(string name)
        {
            var root = Root.MergedWith(Json.Parse("{\"game\":{\"arena\":\"" + name + "\"}}"));
            var own = Get("arenas." + name)["data"];
            return new GameData(own == null ? root : root.MergedWith(own), Variant);
        }

        public JsonValue Get(string path)
        {
            var node = Root;
            foreach (var part in path.Split('.'))
            {
                var next = node[part];
                if (next == null) throw new KeyNotFoundException($"no '{path}' in Game.Core/Data (missing '{part}')");
                node = next;
            }
            return node;
        }

        public bool Has(string path)
        {
            var node = Root;
            foreach (var part in path.Split('.'))
            {
                var next = node[part];
                if (next == null) return false;
                node = next;
            }
            return true;
        }

        public Fixed Num(string path) => Get(path).AsFixed();
        public int Int(string path) => Get(path).AsInt();
        public string Str(string path) => Get(path).AsString();

        /// <summary>The Data folder of this repository, found by walking up from <paramref name="start"/>.</summary>
        public static string FindDataDir(string start)
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Game.Core", "Data");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException($"no Game.Core/Data above {start}");
        }
    }
}
