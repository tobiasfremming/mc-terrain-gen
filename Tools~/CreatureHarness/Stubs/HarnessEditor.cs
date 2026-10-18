// UnityEditor.AssetDatabase for the offline harness (both modes): the creature tests load species assets
// by their project path, and the kit fixture discovers them with FindAssets. Assets are read straight from
// their Unity YAML (HarnessYaml) - no conversion step, so the harness always sees the committed asset.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace UnityEditor
{
    public static class AssetDatabase
    {
        static readonly Dictionary<string, object> cache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // Like Unity: the same object for the same path until the cache is dropped (tests may mutate it).
        public static T LoadAssetAtPath<T>(string assetPath) where T : class
        {
            string full = HarnessPaths.Full(assetPath);
            if (!File.Exists(full)) return null;
            if (cache.TryGetValue(full, out object o)) return o as T;
            Type t = HarnessYaml.ScriptType(full);
            if (t == null || !typeof(T).IsAssignableFrom(t)) return null;
            o = HarnessYaml.Load(full, t, null);
            cache[full] = o;
            return o as T;
        }

        public static void DropCache() => cache.Clear();

        // "t:TypeName" filters only (all the creature tests use). The returned "GUIDs" are the asset paths
        // themselves; GUIDToAssetPath is the identity, so code written against the real API works unchanged.
        public static string[] FindAssets(string filter, string[] searchInFolders)
        {
            Match m = Regex.Match(filter ?? "", @"t:(\w+)");
            string type = m.Success ? m.Groups[1].Value : null;
            var hits = new List<string>();
            foreach (string folder in searchInFolders ?? new[] { "Assets/TerrainGen" })
            {
                string dir = HarnessPaths.Full(folder);
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir, "*.asset", SearchOption.AllDirectories))
                {
                    Type t = HarnessYaml.ScriptType(f);
                    if (t == null || (type != null && !IsOrDerives(t, type))) continue;
                    hits.Add(HarnessPaths.AssetPath(f));
                }
            }
            hits.Sort(StringComparer.Ordinal);
            return hits.ToArray();
        }

        public static string GUIDToAssetPath(string guid) => guid;

        static bool IsOrDerives(Type t, string name)
        {
            for (; t != null; t = t.BaseType) if (t.Name == name) return true;
            return false;
        }
    }
}

// Maps "Assets/TerrainGen/..." to the submodule (or worktree) this harness lives in.
public static class HarnessPaths
{
    static string tg;

    // The TerrainGen root: $CREATURE_HARNESS_TG, else the folder above the binary that holds Scripts/Creatures.
    public static string TerrainGenRoot
    {
        get
        {
            if (tg != null) return tg;
            string env = Environment.GetEnvironmentVariable("CREATURE_HARNESS_TG");
            if (!string.IsNullOrEmpty(env)) return tg = Path.GetFullPath(env);
            for (string d = AppContext.BaseDirectory; d != null; d = Path.GetDirectoryName(d))
                if (Directory.Exists(Path.Combine(d, "Scripts", "Creatures")) && Directory.Exists(Path.Combine(d, "Config")))
                    return tg = d;
            throw new DirectoryNotFoundException("harness: cannot find the TerrainGen root above " + AppContext.BaseDirectory + " (set CREATURE_HARNESS_TG)");
        }
    }

    const string kPrefix = "Assets/TerrainGen";

    public static string Full(string assetPath)
    {
        string p = assetPath.Replace('\\', '/');
        if (p.StartsWith(kPrefix, StringComparison.OrdinalIgnoreCase)) p = p.Substring(kPrefix.Length).TrimStart('/');
        return Path.GetFullPath(Path.Combine(TerrainGenRoot, p));
    }

    public static string AssetPath(string full)
    {
        string rel = Path.GetRelativePath(TerrainGenRoot, full).Replace('\\', '/');
        return kPrefix + "/" + rel;
    }
}
