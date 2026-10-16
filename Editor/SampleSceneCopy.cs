using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Shared by the derived-scene builders (BiomeTestScenes, CreatureLabScene): copies root objects of the
// active SampleScene into a fresh scene, remaps references between the copies, points every `worldConfig`
// field at a config and carries the source scene's render settings over.
internal static class SampleSceneCopy
{
    // null when the active scene is the SampleScene and has every root, else the problem.
    internal static string FindSources(string[] roots, out Dictionary<string, GameObject> sources)
    {
        sources = new Dictionary<string, GameObject>();
        var src = SceneManager.GetActiveScene();
        if (!src.IsValid() || !src.path.EndsWith("SampleScene.unity"))
            return "Open Assets/Scenes/SampleScene.unity as the active scene first (it is the source of the copied objects).";
        foreach (var go in src.GetRootGameObjects()) sources[go.name] = go;
        foreach (var name in roots)
            if (!sources.ContainsKey(name)) return "SampleScene has no root object named '" + name + "'.";
        return null;
    }

    // Instantiates each root into `scene` and remaps references between the copies.
    internal static List<GameObject> CopyRoots(Scene scene, Dictionary<string, GameObject> sources, string[] roots)
    {
        var map = new Dictionary<Object, Object>(); // source object/component -> copy
        var copies = new List<GameObject>();
        foreach (var name in roots)
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
        return copies;
    }

    // Spawn over a point of a flat world; PlayerBootstrap drops the player onto the surface on Play.
    internal static void PlacePlayer(List<GameObject> copies, Vector3 position)
    {
        var player = copies.Find(g => g.name == "Player");
        var target = copies.Find(g => g.name == "TargetTransform");
        if (player != null) player.transform.position = position;
        if (target != null) target.transform.position = position;
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
    // PlantScatter, TerrainGrass) gets the config.
    internal static void SetConfig(List<GameObject> copies, WorldConfig cfg)
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

    // Per-scene render settings of the source scene, captured while it is active and applied verbatim.
    internal sealed class RenderSnapshot
    {
        Material skybox;
        UnityEngine.Rendering.AmbientMode ambientMode;
        Color ambientLight, ambientSky, ambientEquator, ambientGround, fogColor;
        float ambientIntensity, fogDensity, fogStart, fogEnd;
        bool fog;
        FogMode fogMode;
        LightingSettings lighting;

        internal static RenderSnapshot Capture() => new RenderSnapshot
        {
            skybox = RenderSettings.skybox,
            ambientMode = RenderSettings.ambientMode,
            ambientLight = RenderSettings.ambientLight,
            ambientSky = RenderSettings.ambientSkyColor,
            ambientEquator = RenderSettings.ambientEquatorColor,
            ambientGround = RenderSettings.ambientGroundColor,
            ambientIntensity = RenderSettings.ambientIntensity,
            fog = RenderSettings.fog, fogColor = RenderSettings.fogColor, fogMode = RenderSettings.fogMode,
            fogDensity = RenderSettings.fogDensity, fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance,
            lighting = Lightmapping.lightingSettings,
        };

        internal void ApplyTo(Scene scene, List<GameObject> copies)
        {
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
        }
    }
}
