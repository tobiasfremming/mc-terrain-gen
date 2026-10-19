using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The creature lab scene: the SampleScene's world objects (light, volume, player + camera, target, terrain
// manager, grass; no plant scatter) copied into a fresh scene on the flat lab world, plus a Creatures root
// with the CreatureWorld, the CreatureLab that places one enclosure (or aviary) per roster entry at runtime and,
// for every grove animal in the roster (a soft body: the tree octopus, the two-armed swinger), an OctopusLab with
// its own grove (placed one after another).
// The animals are Config/Creatures/CreatureLabRoster.asset (creature kit T3.5: habitats from the species'
// modules, sizes from its scale overrides). Derived, not hand-edited: regenerate with the menu item.
//
// Menu: Marching Cubes / Rebuild Creature Lab. Requires the SampleScene to be open as the active scene.
public static class CreatureLabScene
{
    const string kScenePath = "Assets/Scenes/CreatureLab.unity";
    const string kConfigPath = "Assets/TerrainGen/Config/Creatures/CreatureLabWorldConfig.asset";
    const string kCreatureMaterialPath = "Assets/TerrainGen/Config/Creatures/Creature.mat";
    const string kBorderMaterialPath = "Assets/TerrainGen/Config/Creatures/EnclosureBorder.mat";
    const string kBaobabPath = "Assets/TerrainGen/Config/Flora/Prototypes/BaobabPlant_Prototypes.asset";   // R24 shade tree
    // the octopus grove (the hand-wired lab of 2026-10-18): oaks and trees, the desert beetle as prey
    static readonly string[] kGroveSetPaths =
    {
        "Assets/TerrainGen/Config/Flora/Prototypes/OakPlant_Prototypes.asset",
        "Assets/TerrainGen/Config/Flora/Prototypes/TreePlant_Prototypes.asset",
    };
    const string kGrovePreyPath = "Assets/TerrainGen/Config/Creatures/DesertBeetle.asset";
    // a mouth feeder's grove (the swinger, design 2026-10-19): smaller trees (branches within its 1.4 m arms), several
    // small spiders climbing them
    const string kSpiderPreyPath = "Assets/TerrainGen/Config/Creatures/TreeSpider.asset";

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
        var roster = AssetDatabase.LoadAssetAtPath<CreatureLabRoster>(CreatureLabRoster.AssetPath);
        if (roster == null) return "Missing " + CreatureLabRoster.AssetPath;
        for (int i = 0; i < roster.entries.Count; i++)
            if (roster.entries[i].species == null) return CreatureLabRoster.AssetPath + ": entry " + i + " names no species";
        roster.SplitGroves(out var enclosures, out var groves);

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
        lab.roster = roster;   // species and scale overrides come from it at runtime
        lab.borderMaterial = borderMat;
        lab.plantScale = 2f;   // the Baobab at 2x (creature-module skill: the lab's shade tree)
        root.AddComponent<CreatureDebugView>();
        lab.enclosurePlant = AssetDatabase.LoadAssetAtPath<PlantPrototypeSet>(kBaobabPath);
        foreach (CreatureSpecies grove in groves)
        {
            var octo = root.AddComponent<OctopusLab>();
            octo.octopus = grove;
            var sets = new System.Collections.Generic.List<PlantPrototypeSet>();
            foreach (string path in kGroveSetPaths) { var set = AssetDatabase.LoadAssetAtPath<PlantPrototypeSet>(path); if (set != null) sets.Add(set); }
            octo.groveSets = sets.ToArray();
            octo.treeScale = 1f;
            octo.preySpecies = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kGrovePreyPath);
            if (grove.cephalopod.MouthCount > 0)
            {
                // tuning starting points, not measured in the Editor yet
                octo.treeScale = 0.6f; octo.groveTrees = 5; octo.canopyOverlap = 0.6f;
                octo.preySpecies = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kSpiderPreyPath);
                octo.preyCount = 4; octo.preyClimbs = true; octo.seed = 47;
            }
        }
        copies.Add(root);

        render.ApplyTo(scene, copies);

        EditorSceneManager.SaveScene(scene, kScenePath);
        EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.SaveAssets();
        return "Built " + kScenePath + " <- " + kConfigPath + " (" + (cfg.defaultDensity != null ? cfg.defaultDensity.name : "null")
             + ", globe " + cfg.useGlobe + ", plants " + cfg.enablePlants + "; roster " + enclosures.Count + " enclosures"
             + (groves.Count > 0 ? " + groves (" + string.Join(", ", groves.ConvertAll(g => g.name)) + ")" : "") + ")";
    }
}
