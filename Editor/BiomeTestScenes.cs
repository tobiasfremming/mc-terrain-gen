using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        var src = SceneManager.GetActiveScene();
        var sb = new System.Text.StringBuilder();
        if (!src.IsValid() || !src.path.EndsWith("SampleScene.unity"))
            return "Open Assets/Scenes/SampleScene.unity as the active scene first (it is the source of the copied objects).";

        var sources = new Dictionary<string, GameObject>();
        foreach (var go in src.GetRootGameObjects()) sources[go.name] = go;
        foreach (var name in kRoots)
            if (!sources.ContainsKey(name)) return "SampleScene has no root object named '" + name + "'.";

        // per-scene render settings of the source, copied verbatim
        var skybox = RenderSettings.skybox;
        var ambientMode = RenderSettings.ambientMode;
        var ambientLight = RenderSettings.ambientLight;
        var ambientSky = RenderSettings.ambientSkyColor;
        var ambientEquator = RenderSettings.ambientEquatorColor;
        var ambientGround = RenderSettings.ambientGroundColor;
        float ambientIntensity = RenderSettings.ambientIntensity;
        bool fog = RenderSettings.fog; var fogColor = RenderSettings.fogColor; var fogMode = RenderSettings.fogMode;
        float fogDensity = RenderSettings.fogDensity, fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance;
        var lighting = Lightmapping.lightingSettings;

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
            var map = new Dictionary<Object, Object>(); // source object/component -> copy
            var copies = new List<GameObject>();
            foreach (var name in kRoots)
            {
                var s = sources[name];
                var c = Object.Instantiate(s);
                c.name = s.name;
                SceneManager.MoveGameObjectToScene(c, scene);
                copies.Add(c);
                MapHierarchy(s, c, map);
            }
            // References between the copied objects (Terrain.target ->
            // TargetTransform, scatter viewer -> Player, ...) still point at
            // the SOURCE scene after Instantiate; cross-scene references do
            // not serialise, so remap them onto the copies.
            foreach (var c in copies)
                foreach (var comp in c.GetComponentsInChildren<Component>(true))
                    RemapReferences(comp, map);

            // point the world at this biome
            SetConfig(copies, cfg);

            // spawn over the origin of a flat world; PlayerBootstrap drops the
            // player onto the surface on Play
            var player = copies.Find(g => g.name == "Player");
            var target = copies.Find(g => g.name == "TargetTransform");
            if (player != null) player.transform.position = new Vector3(0f, 160f, 0f);
            if (target != null) target.transform.position = new Vector3(0f, 160f, 0f);

            var prev = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.fog = fog; RenderSettings.fogColor = fogColor; RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity; RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
            var light = copies.Find(g => g.name == "Directional Light");
            if (light != null) RenderSettings.sun = light.GetComponent<Light>();
            if (lighting != null) Lightmapping.SetLightingSettingsForScene(scene, lighting);
            SceneManager.SetActiveScene(prev);

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

    static void MapHierarchy(GameObject s, GameObject c, Dictionary<Object, Object> map)
    {
        map[s] = c;
        var sc = s.GetComponents<Component>();
        var cc = c.GetComponents<Component>();
        for (int i = 0; i < sc.Length && i < cc.Length; i++)
            if (sc[i] != null && cc[i] != null) map[sc[i]] = cc[i];
        for (int i = 0; i < s.transform.childCount && i < c.transform.childCount; i++)
            MapHierarchy(s.transform.GetChild(i).gameObject, c.transform.GetChild(i).gameObject, map);
    }

    static void RemapReferences(Component comp, Dictionary<Object, Object> map)
    {
        if (comp == null) return;
        var so = new SerializedObject(comp);
        var it = so.GetIterator();
        bool changed = false;
        while (it.NextVisible(true))
        {
            if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
            if (map.TryGetValue(it.objectReferenceValue, out var mapped))
            {
                it.objectReferenceValue = mapped;
                changed = true;
            }
        }
        if (changed) so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Every component with a serialized `worldConfig` field (MCChunkManager,
    // PlantScatter, TerrainGrass) gets the biome's config.
    static void SetConfig(List<GameObject> copies, WorldConfig cfg)
    {
        foreach (var c in copies)
            foreach (var comp in c.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                var so = new SerializedObject(comp);
                var p = so.FindProperty("worldConfig");
                if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) continue;
                p.objectReferenceValue = cfg;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
    }
}
