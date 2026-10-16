using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One small test scene per biome: the SampleScene's world objects (light,
// volume, player + camera, target, terrain manager, plant scatter, grass)
// copied into a fresh scene and pointed at a single-biome, flat-world
// WorldConfig under Config/Test. Regenerate with the menu item whenever the
// SampleScene setup changes; the scenes are derived, not hand-edited.
//
// Menu: Marching Cubes / Rebuild Biome Test Scenes. Requires the SampleScene
// to be open as the active scene (it is the source of the copied objects).
public static class BiomeTestScenes
{
    const string kSceneFolder = "Assets/Scenes/Biomes";
    const string kConfigFolder = "Assets/TerrainGen/Config/Test";

    // Root objects of the source scene that a biome test needs. Anything
    // else in the SampleScene (old managers, debug cubes, meteors) is left out.
    static readonly string[] kRoots = { "Directional Light", "Global Volume", "Player", "TargetTransform", "Terrain", "PlantScatter", "TerrainGrass" };

    [MenuItem("Marching Cubes/Rebuild Biome Test Scenes")]
    public static void RebuildFromMenu()
    {
        string report = Rebuild();
        Debug.Log(report);
    }

    // Builds one scene per <Name>WorldConfig.asset in Config/Test. Returns a
    // human-readable report (also used by the execute_code driver).
    public static string Rebuild()
    {
        var sb = new System.Text.StringBuilder();
        string err = SampleSceneCopy.FindSources(kRoots, out var sources);
        if (err != null) return err;

        // per-scene render settings of the source, copied verbatim
        var render = SampleSceneCopy.RenderSnapshot.Capture();

        if (!AssetDatabase.IsValidFolder(kSceneFolder))
            AssetDatabase.CreateFolder("Assets/Scenes", "Biomes");

        var configGuids = AssetDatabase.FindAssets("t:WorldConfig", new[] { kConfigFolder });
        int built = 0;
        foreach (var guid in configGuids)
        {
            string cfgPath = AssetDatabase.GUIDToAssetPath(guid);
            var cfg = AssetDatabase.LoadAssetAtPath<WorldConfig>(cfgPath);
            if (cfg == null || !cfg.name.EndsWith("WorldConfig")) continue;
            string biomeName = cfg.name.Substring(0, cfg.name.Length - "WorldConfig".Length);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var copies = SampleSceneCopy.CopyRoots(scene, sources, kRoots);

            // point the world at this biome
            SampleSceneCopy.SetConfig(copies, cfg);

            // spawn over the origin of a flat world; PlayerBootstrap drops the
            // player onto the surface on Play
            SampleSceneCopy.PlacePlayer(copies, new Vector3(0f, 160f, 0f));

            render.ApplyTo(scene, copies);

            string scenePath = kSceneFolder + "/" + biomeName + ".unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.CloseScene(scene, true);
            sb.AppendLine(scenePath + " <- " + cfgPath + " (" + (cfg.defaultDensity != null ? cfg.defaultDensity.name : "null") + ", globe " + cfg.useGlobe + ")");
            built++;
        }
        AssetDatabase.SaveAssets();
        sb.Insert(0, "Built " + built + " biome test scenes under " + kSceneFolder + ":\n");
        return sb.ToString();
    }
}
