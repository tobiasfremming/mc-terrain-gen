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

        public static CreatureSpecies Load(string name) => AssetDatabase.LoadAssetAtPath<CreatureSpecies>(PathOf(name));

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

        // The lab habitat a species gets from its legacy fields (CreatureLab.BrainFor / OctopusLab today; the
        // roster's Habitat.For replaces this in T3.5).
        public enum Habitat { Plain, Climbable, Aviary, Grove }

        public static Habitat HabitatOf(CreatureSpecies sp)
        {
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) return Habitat.Grove;
            if (sp.CanFly) return Habitat.Aviary;
            if (sp.climb.canClimb) return Habitat.Climbable;
            return Habitat.Plain;
        }

        // Test fixtures in Config/Creatures, not lab animals.
        public static bool IsTestFixture(string name) => name.StartsWith("Test", StringComparison.Ordinal);

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

        // The GUID in a script's .meta file.
        public static string ScriptGuid(string scriptPath)
        {
            foreach (string line in File.ReadAllLines(FullPath(scriptPath + ".meta")))
                if (line.StartsWith("guid:", StringComparison.Ordinal)) return line.Substring(5).Trim();
            return null;
        }
    }
}
