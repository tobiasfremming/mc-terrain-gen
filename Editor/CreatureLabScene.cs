using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The creature lab scene: the SampleScene's world objects (light, volume, player + camera, target, terrain
// manager, grass; no plant scatter) copied into a fresh scene on the flat lab world, plus a Creatures root
// with the CreatureWorld and the CreatureLab that places one enclosure per species at runtime.
// Derived, not hand-edited: regenerate with the menu item.
//
// Menu: Marching Cubes / Rebuild Creature Lab. Requires the SampleScene to be open as the active scene.
public static class CreatureLabScene
{
    const string kScenePath = "Assets/Scenes/CreatureLab.unity";
    const string kConfigPath = "Assets/TerrainGen/Config/Creatures/CreatureLabWorldConfig.asset";
    const string kCreatureMaterialPath = "Assets/TerrainGen/Config/Creatures/Creature.mat";
    const string kBorderMaterialPath = "Assets/TerrainGen/Config/Creatures/EnclosureBorder.mat";
    static readonly string[] kSpeciesPaths = { "Assets/TerrainGen/Config/Creatures/DesertMonitor.asset" };

    static readonly string[] kRoots = { "Directional Light", "Global Volume", "Player", "TargetTransform", "Terrain", "TerrainGrass" };

    [MenuItem("Marching Cubes/Rebuild Creature Lab")]
    public static void RebuildFromMenu()
    {
        Debug.Log(Rebuild());
    }

    public static string Rebuild()
    {
        string err = SampleSceneCopy.FindSources(kRoots, out var sources);
        if (err != null) return err;

        var cfg = AssetDatabase.LoadAssetAtPath<WorldConfig>(kConfigPath);
        var creatureMat = AssetDatabase.LoadAssetAtPath<Material>(kCreatureMaterialPath);
        var borderMat = AssetDatabase.LoadAssetAtPath<Material>(kBorderMaterialPath);
        if (cfg == null) return "Missing " + kConfigPath;
        if (creatureMat == null) return "Missing " + kCreatureMaterialPath;
        if (borderMat == null) return "Missing " + kBorderMaterialPath;
        var species = new CreatureSpecies[kSpeciesPaths.Length];
        for (int i = 0; i < species.Length; i++)
        {
            species[i] = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kSpeciesPaths[i]);
            if (species[i] == null) return "Missing " + kSpeciesPaths[i];
        }

        var render = SampleSceneCopy.RenderSnapshot.Capture();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var copies = SampleSceneCopy.CopyRoots(scene, sources, kRoots);
        SampleSceneCopy.SetConfig(copies, cfg);
        SampleSceneCopy.PlacePlayer(copies, new Vector3(0f, 160f, 0f));

        var root = new GameObject("Creatures");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var world = root.AddComponent<CreatureWorld>();
        world.creatureMaterial = creatureMat;
        var lab = root.AddComponent<CreatureLab>();
        lab.species = species;
        lab.borderMaterial = borderMat;
        copies.Add(root);

        render.ApplyTo(scene, copies);

        EditorSceneManager.SaveScene(scene, kScenePath);
        EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.SaveAssets();
        return "Built " + kScenePath + " <- " + kConfigPath + " (" + (cfg.defaultDensity != null ? cfg.defaultDensity.name : "null")
             + ", globe " + cfg.useGlobe + ", plants " + cfg.enablePlants + "; species " + species.Length + ")";
    }
}
