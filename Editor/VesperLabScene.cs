using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Vesper lab scene (Vesper step 1): the SampleScene's light, volume and player (its camera) copied into a fresh
// scene, as the Creature Lab builder does (SampleSceneCopy), but no terrain: a flat ground plane at y = 0 (a collider
// the player walks on; the creatures use VesperLab's FlatGroundProbe), and a Creatures root with the CreatureWorld,
// the CreatureDebugView (F3, on) and the VesperLab (Vesper, its rock perches and the M menu, built at runtime).
// The player is wired as PlayerBootstrap would (CharacterController + SimplePlayerController; the bootstrap itself
// needs a terrain manager), and stands inside the aviary facing the middle.
//
// Menu: Marching Cubes / Build Vesper Lab. Requires the SampleScene to be open as the active scene.
public static class VesperLabScene
{
    const string kScenePath = "Assets/Scenes/VesperLab.unity";
    const string kVesperPath = "Assets/TerrainGen/Config/Creatures/Vesper.asset";
    const string kCreatureMaterialPath = "Assets/TerrainGen/Config/Creatures/Creature.mat";
    const string kRockMaterialPath = "Assets/TerrainGen/Config/Creatures/EnclosureBorder.mat";

    static readonly string[] kRoots = { "Directional Light", "Global Volume", "Player" };

    [MenuItem("Marching Cubes/Build Vesper Lab")]
    public static void BuildFromMenu()
    {
        string result = Build();
        if (result.StartsWith("Built ")) Debug.Log(result);
        else Debug.LogError("[Build Vesper Lab] " + result);   // a precondition (SampleScene not active, a missing asset)
    }

    public static string Build()
    {
        string err = SampleSceneCopy.FindSources(kRoots, out var sources);
        if (err != null) return err;

        var vesper = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kVesperPath);
        var creatureMat = AssetDatabase.LoadAssetAtPath<Material>(kCreatureMaterialPath);
        var rockMat = AssetDatabase.LoadAssetAtPath<Material>(kRockMaterialPath);
        if (vesper == null) return "Missing " + kVesperPath;
        if (creatureMat == null) return "Missing " + kCreatureMaterialPath;
        if (rockMat == null) return "Missing " + kRockMaterialPath;

        // the aviary at the asset's scale (VesperLab rebuilds it at runtime for its own scale)
        CreatureSpecies rt = vesper.Compiled.Runtime;
        EnclosureArea area = VesperLabLayout.Area(rt, vesper.scale, Vector3.zero, 3);

        var render = SampleSceneCopy.RenderSnapshot.Capture();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var copies = SampleSceneCopy.CopyRoots(scene, sources, kRoots);

        // the player: PlayerBootstrap's wiring (it steps aside when a SimplePlayerController exists)
        GameObject player = copies.Find(g => g.name == "Player");
        string playerNote = WirePlayer(player, area);

        // the flat ground: a plane 3 aviary radii across (a 10 m plane scaled), with its collider for the player
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ground, scene);
        float span = 3f * area.radius;
        ground.transform.localScale = new Vector3(span / 10f, 1f, span / 10f);
        copies.Add(ground);

        var root = new GameObject("Creatures");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var world = root.AddComponent<CreatureWorld>();
        world.creatureMaterial = creatureMat;
        var view = root.AddComponent<CreatureDebugView>();
        view.show = true;
        var lab = root.AddComponent<VesperLab>();
        lab.species = vesper;
        lab.rockMaterial = rockMat;
        lab.perchCount = 3;
        lab.menuKey = KeyCode.M;
        copies.Add(root);

        render.ApplyTo(scene, copies);

        EditorSceneManager.SaveScene(scene, kScenePath);
        EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.SaveAssets();
        return $"Built {kScenePath}: {vesper.name} at scale {vesper.scale:F2}, aviary r {area.radius:F1} m (safe {area.safeRadius:F1} m, roof {area.maxHeight:F1} m), 3 rock perches, ground {span:F0} m; {playerNote}";
    }

    static string WirePlayer(GameObject player, EnclosureArea area)
    {
        if (player == null) return "no Player copied";
        foreach (var col in player.GetComponents<Collider>())
            if (!(col is CharacterController)) col.enabled = false;
        var rend = player.GetComponent<MeshRenderer>();
        if (rend != null) rend.enabled = false;   // first person
        var cc = player.GetComponent<CharacterController>();
        if (cc == null) cc = player.AddComponent<CharacterController>();
        cc.height = 2f; cc.radius = 0.45f; cc.center = Vector3.zero; cc.slopeLimit = 50f; cc.stepOffset = 0.4f;
        Camera cam = player.GetComponentInChildren<Camera>();
        if (cam != null)
        {
            cam.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, 0.1f);
        }
        var ctrl = player.GetComponent<SimplePlayerController>();
        if (ctrl == null) ctrl = player.AddComponent<SimplePlayerController>();   // adds GravityAligner + CharacterGravity
        ctrl.cameraTransform = cam != null ? cam.transform : null;
        Vector3 at = VesperLabLayout.PlayerSpot(area);
        player.transform.SetPositionAndRotation(at + Vector3.up * 1.1f, Quaternion.LookRotation(area.center - at, Vector3.up));
        return cam != null ? "player + camera wired" : "player wired, NO camera under it";
    }
}
