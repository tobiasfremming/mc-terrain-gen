using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Creature kit (design 2026-10-18-creature-kit §3c): the species assets the generic kit runs over, and what
    // it needs to know about them. The same code runs in Unity and in the offline harness
    // (Tools~/CreatureHarness), whose AssetDatabase reads the same .asset YAML.
    public static class CreatureKitAssets
    {
        public const string kFolder = "Assets/TerrainGen/Config/Creatures";
        public const string kSpeciesScript = "Assets/TerrainGen/Scripts/Creatures/CreatureSpecies.cs";

        // Every CreatureSpecies asset under Config/Creatures, by asset name (the fixture argument).
        public static IEnumerable<string> Species()
        {
            var names = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:CreatureSpecies", new[] { kFolder }))
                names.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        public static string PathOf(string name) => kFolder + "/" + name + ".asset";

        // The species as the runtime sees it: a kit asset (kitVersion 1) is compiled and its runtime view returned
        // (the legacy fields every check reads); a legacy asset is its own view. LoadAsset: the asset itself.
        public static CreatureSpecies Load(string name)
        {
            CreatureSpecies a = LoadAsset(name);
            return a != null && a.kitVersion > 0 ? a.Compiled.Runtime : a;
        }

        public static CreatureSpecies LoadAsset(string name) => AssetDatabase.LoadAssetAtPath<CreatureSpecies>(PathOf(name));

        // The species the catalogue lists: every asset, by name (the Editor menu gathers the same list).
        public static List<CreatureSpecies> CatalogueSpecies()
        {
            var list = new List<CreatureSpecies>();
            foreach (string n in Species()) { CreatureSpecies a = LoadAsset(n); if (a != null) list.Add(a); }
            return list;
        }

        // A file of the project on disk (the Editor runs in the project root; the harness maps the path).
        public static string FullPath(string assetPath)
        {
#if CREATURE_HARNESS
            return HarnessPaths.Full(assetPath);
#else
            return Path.GetFullPath(assetPath);
#endif
        }

        // ------------------------------------------------------------ what the kit knows per species

        // The lab habitat a species gets: the runtime's CreatureKit.LabHabitat (from its modules; T3.5). The
        // legacy-fields rule it replaced is LegacyHabitatOf (CreatureKitLabTests.Habitat_Resolves pins them equal).
        public enum Habitat { Plain, Climbable, Aviary, Grove }

        public static Habitat HabitatOf(CreatureSpecies sp) => (Habitat)(int)CreatureKit.LabHabitat.For(sp);

        public static Habitat LegacyHabitatOf(CreatureSpecies sp)
        {
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) return Habitat.Grove;
            if (sp.CanFly) return Habitat.Aviary;
            if (sp.climb.canClimb) return Habitat.Climbable;
            return Habitat.Plain;
        }

        // Test fixtures in Config/Creatures, not lab animals.
        public static bool IsTestFixture(string name) => name.StartsWith("Test", StringComparison.Ordinal) || name == "KitMonitor";

        // Per-frame tick budget (ms, one individual, flat ground, tick + bone write). Legacy assets have no
        // perfBudgetMs field yet (Phase 2): the shipped budget of one creature (monitor 0.1 ms on DesertWorld,
        // spiders 1 <= 0.1 ms) for everyone.
        public const float kDefaultPerfBudgetMs = 0.1f;
        public static float PerfBudgetMs(CreatureSpecies sp) => kDefaultPerfBudgetMs;

        // The generic checks, by name (CreatureKitTests' test names without the species).
        public const string Validate = "Validate", NoMissingTypes = "NoMissingTypes", Layout = "Layout", MeshTime = "MeshTime",
            MaxBend = "MaxBend", Bounds = "Bounds", Wander = "Wander", Gait = "Gait", Size = "Size", Gc = "Gc", Perf = "Perf", Lab = "Lab";

        // Why a check does not apply to a species (null = it runs). Reasons are about the species, never "it
        // fails": a failing check is a finding, fixed in the species or the code, not skipped here.
        public static string SkipReason(CreatureSpecies sp, string check)
        {
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod)
                switch (check)
                {
                    case MaxBend: case Bounds: case Wander: case Gait: case Size: case Gc: case Perf:
                        return "cephalopod: OctopusCreature (body + controller + brain) is not a CreatureSim; its arm, " +
                               "soft-body, cloak and containment tests are Octopus*Tests until the octopus joins the kit (T1.2/T3.3)";
                }
            if (check == Gait && (sp.legs == null || sp.legs.Length == 0)) return "legless: no feet to plant or gait groups to keep";
            return null;
        }

        // ------------------------------------------------------------ the species' renderer mesh and skin

        // The mesh the renderer draws for the species (CreatureRenderer's choice).
        public static Mesh BuildMesh(CreatureSpecies sp)
        {
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) return OctopusMeshBuilder.Build(sp.cephalopod, sp.name + " octopus");
            return sp.WingCount > 0 ? WingedMeshBuilder.Build(sp) : CreatureMeshBuilder.Build(sp);
        }

        // Bones the renderer uploads per individual.
        public static int RendererBones(CreatureSpecies sp)
            => sp.bodyPlan == CreatureBodyPlan.Cephalopod ? OctopusBody.BoneCountFor(sp.cephalopod) : sp.BoneCount;

        // Serialized fields of a type by Unity's rules (public or [SerializeField], not [NonSerialized]).
        public static HashSet<string> SerializedFields(Type t)
        {
            var set = new HashSet<string>();
            for (; t != null && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsInitOnly || f.IsLiteral || f.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                    if (f.IsPublic || f.IsDefined(typeof(SerializeField), false)) set.Add(f.Name);
                }
            return set;
        }

        // The top-level keys of a ScriptableObject asset's YAML (two-space indent under "MonoBehaviour:").
        public static List<string> TopLevelKeys(string assetPath, out string scriptGuid)
        {
            var keys = new List<string>();
            scriptGuid = null;
            foreach (string line in File.ReadAllLines(FullPath(assetPath)))
            {
                if (line.Length < 3 || line[0] != ' ' || line[1] != ' ' || line[2] == ' ' || line[2] == '-') continue;
                int c = line.IndexOf(':');
                if (c < 0) continue;
                string key = line.Substring(2, c - 2);
                if (key == "m_Script")
                {
                    int g = line.IndexOf("guid:", StringComparison.Ordinal);
                    if (g >= 0) scriptGuid = line.Substring(g + 5).Split(',')[0].Trim();
                }
                keys.Add(key);
            }
            return keys;
        }

        // ------------------------------------------------------------ kit format ([SerializeReference] YAML)

        // Problems in an asset's references.RefIds (Unity's [SerializeReference] store, version 2): a type that
        // does not resolve to a kit part or module ("missing type": Unity drops the object), a key no field of
        // its class takes (Unity silently ignores it: a misspelt parameter reads its default), a part/module
        // list entry with no RefIds object. Works on the text, so Unity and the harness check the same thing.
        public static List<string> KitYamlProblems(string[] lines)
        {
            var problems = new List<string>();
            var listed = new List<string>();
            var defined = new HashSet<string>();
            string list = null;
            int i = 0;
            for (; i < lines.Length; i++)
            {
                string l = lines[i].TrimEnd();
                if (l == "  parts:" || l == "  modules:") { list = l.Trim().TrimEnd(':'); continue; }
                if (list != null && l.StartsWith("  - rid: ", StringComparison.Ordinal)) { listed.Add(list + ":" + l.Substring(9).Trim()); continue; }
                list = null;
                if (l == "  references:") break;
            }
            for (; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("    - rid: ", StringComparison.Ordinal)) continue;
                string rid = lines[i].Substring(11).Trim();
                defined.Add(rid);
                Type t = null; string cls = "?";
                int j = i + 1;
                for (; j < lines.Length && !lines[j].StartsWith("    - ", StringComparison.Ordinal) && Indent(lines[j]) > 4; j++)
                {
                    string s = lines[j].Trim();
                    if (s.StartsWith("type:", StringComparison.Ordinal))
                    {
                        cls = Flow(s, "class"); string ns = Flow(s, "ns");
                        t = typeof(CreatureKit.CreaturePart).Assembly.GetType(string.IsNullOrEmpty(ns) ? cls : ns + "." + cls);
                        if (t == null || t.IsAbstract || !(typeof(CreatureKit.CreaturePart).IsAssignableFrom(t) || typeof(CreatureKit.CreatureModule).IsAssignableFrom(t)))
                        { problems.Add($"rid {rid}: missing type {ns}.{cls}"); t = null; }
                    }
                    else if (s == "data:" && t != null) { j = DataKeys(lines, j + 1, t, $"rid {rid} ({cls})", problems) - 1; }
                }
                i = j - 1;
            }
            foreach (string e in listed)
            {
                string rid = e.Substring(e.IndexOf(':') + 1);
                if (rid == "-2") problems.Add(e.Substring(0, e.IndexOf(':')) + ": a null entry (rid -2)");
                else if (!defined.Contains(rid)) problems.Add($"{e}: no object in references.RefIds");
            }
            return problems;
        }

        static int Indent(string l) { int n = 0; while (n < l.Length && l[n] == ' ') n++; return n; }

        static string Flow(string s, string key)
        {
            int k = s.IndexOf(key + ":", StringComparison.Ordinal);
            if (k < 0) return null;
            int e = s.IndexOfAny(new[] { ',', '}' }, k);
            return s.Substring(k + key.Length + 1, (e < 0 ? s.Length : e) - k - key.Length - 1).Trim();
        }

        // The keys of one RefIds data block (indent 8): each must be a serialized field of the type that holds it.
        // Returns the index of the first line after the block.
        static int DataKeys(string[] lines, int start, Type type, string where, List<string> problems)
        {
            var stack = new List<KeyValuePair<int, Type>> { new KeyValuePair<int, Type>(8, type) };
            int i = start;
            for (; i < lines.Length; i++)
            {
                string l = lines[i].TrimEnd();
                int ind = Indent(l);
                if (ind < 8 || l.Trim().Length == 0) break;
                string text = l.Substring(ind);
                if (text.StartsWith("- ", StringComparison.Ordinal)) { text = text.Substring(2); ind += 2; }
                int c = text.IndexOf(':');
                if (c <= 0 || text.StartsWith("{", StringComparison.Ordinal)) continue;   // a scalar list item or a flow value
                string key = text.Substring(0, c), rest = text.Substring(c + 1).Trim();
                while (stack.Count > 1 && stack[stack.Count - 1].Key > ind) stack.RemoveAt(stack.Count - 1);
                Type owner = stack[stack.Count - 1].Value;
                if (owner == null) continue;   // inside a reference ({rid}) - its object is checked as its own entry
                FieldInfo f = null;
                foreach (FieldInfo x in CreatureKit.KitTypes.SerializedFields(owner)) if (x.Name == key) { f = x; break; }
                if (f == null) { problems.Add($"{where}: unknown key '{key}' in {owner.Name}"); continue; }
                if (rest.Length == 0)
                {
                    // nested: map fields at ind + 2; list items "- " at ind (their keys at ind + 2)
                    bool reference = f.IsDefined(typeof(SerializeReference), false);
                    stack.Add(new KeyValuePair<int, Type>(ind + 2, reference ? null : CreatureKit.KitTypes.ElementType(f.FieldType)));
                }
            }
            return i;
        }

        // The GUID in a script's .meta file.
        public static string ScriptGuid(string scriptPath)
        {
            foreach (string line in File.ReadAllLines(FullPath(scriptPath + ".meta")))
                if (line.StartsWith("guid:", StringComparison.Ordinal)) return line.Substring(5).Trim();
            return null;
        }
    }
}
