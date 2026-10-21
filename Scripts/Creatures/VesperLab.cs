using System.Collections.Generic;
using UnityEngine;

// The Vesper lab (Vesper step 1): one Vesper on flat ground (y = 0 under this object) in an aviary of
// VesperLabLayout.AviaryFactor x the Creature Lab's radius, with perchCount rock perches - exactly the CommandBrain tests'
// box (VesperLabLayout.RockGeometry: no rotation, no sink), registered as perchable surfaces like the Creature Lab's
// aviary rocks (CreatureSurfaceMesh: in play mode a proxy collider of this very mesh) - and
// posts marking the edge. Vesper is driven by a CommandBrain that the "M" menu commands (IMGUI; VesperLabMenu holds the
// state). The F3 skeleton view (CreatureDebugView) is on from the start.
// Changing the species asset, scale, seed or perchCount in play mode respawns Vesper (and rebuilds the rocks for the
// new size); the menu stays as it was and the new individual gets the command last picked.
// Scene: Marching Cubes / Build Vesper Lab (Editor/VesperLabScene.cs).
[RequireComponent(typeof(CreatureWorld))]
public sealed class VesperLab : MonoBehaviour
{
    [Tooltip("The species (Config/Creatures/Vesper.asset).")]
    public CreatureSpecies species;
    [Tooltip("Size scale (0 = the asset's own). The aviary and rocks scale with it.")]
    public float scale;
    public int seed = 1;
    [Tooltip("Opens and closes the command menu.")]
    public KeyCode menuKey = KeyCode.M;
    [Range(0, 3)] public int perchCount = 3;
    [Tooltip("The rocks' and posts' material (the Creature Lab's EnclosureBorder sandstone).")]
    public Material rockMaterial;

    readonly VesperLabMenu menu = new VesperLabMenu();
    readonly List<Mesh> meshes = new List<Mesh>();
    CreatureWorld world;
    CreatureSim sim;
    Transform dressing;
    EnclosureArea area;
    bool started, dirty;
    SimplePlayerController player;
    CursorLockMode cursorBefore;
    bool cursorVisibleBefore;
    static readonly List<Vector3> rockVerts = new List<Vector3>();
    static readonly List<int> rockTris = new List<int>();

    static readonly IGroundProbe Flat = new FlatGroundProbe(0f);

    public VesperLabMenu Menu => menu;
    public CreatureSim Sim => sim;

    void Awake()
    {
        world = GetComponent<CreatureWorld>();
        world.SetGround(Flat, _ => Vector3.up);
        var view = GetComponent<CreatureDebugView>();
        if (view == null) view = gameObject.AddComponent<CreatureDebugView>();
        view.show = true;   // the skeleton: Vesper step 1 has no skin
    }

    void OnEnable() => CreatureSpecies.Changed += OnSpeciesChanged;
    void OnDisable() => CreatureSpecies.Changed -= OnSpeciesChanged;
    void OnSpeciesChanged(CreatureSpecies sp) { if (sp != null && sp == species) dirty = true; }
    void OnValidate() => dirty = true;

    void Start()
    {
        player = FindAnyObjectByType<SimplePlayerController>();
        Build();
        started = true;
        if (player != null)
        {
            // stand inside the aviary, facing Vesper
            Vector3 at = VesperLabLayout.PlayerSpot(area);
            var cc = player.GetComponent<CharacterController>();
            bool ccOn = cc != null && cc.enabled;
            if (ccOn) cc.enabled = false;
            float lift = cc != null ? 0.5f * cc.height + 0.1f : 1.1f;
            player.transform.SetPositionAndRotation(at + Vector3.up * lift, Quaternion.LookRotation(area.center - at, Vector3.up));
            if (ccOn) cc.enabled = true;
            player.SendMessage("ResetOrientation", SendMessageOptions.DontRequireReceiver);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(menuKey)) SetMenu(!menu.Open);
        if (menu.Open)
            for (int i = 0; i < VesperLabMenu.Commands.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Pick(VesperLabMenu.Commands[i]);
        if (dirty && started)
        {
            dirty = false;
            Respawn();
        }
    }

    // Opening the menu frees the cursor and stops the player's look and walk (its click would re-lock the cursor);
    // closing it gives both back as they were. Without a player the cursor is left alone.
    void SetMenu(bool open)
    {
        if (open == menu.Open) return;
        menu.Toggle();
        if (player == null) return;
        player.enabled = !open;
        if (open)
        {
            cursorBefore = Cursor.lockState; cursorVisibleBefore = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        else { Cursor.lockState = cursorBefore; Cursor.visible = cursorVisibleBefore; }
    }

    void Pick(CreatureCommand c)
    {
        if (c == CreatureCommand.WalkTo) menu.Pick(c, PlayerGround());
        else menu.Pick(c);
    }

    // "Walk to me": the ground under the player (else the camera)
    Vector3 PlayerGround()
    {
        Transform t = player != null ? player.transform : Camera.main != null ? Camera.main.transform : null;
        Vector3 p = t != null ? t.position : area.center;
        return new Vector3(p.x, transform.position.y, p.z);
    }

    float Scale => scale > 0f ? scale : (species != null ? species.scale : 1f);

    void Build()
    {
        dirty = false;
        if (species == null) { Debug.LogWarning("[VesperLab] no species: assign Config/Creatures/Vesper.asset"); return; }
        CreatureSpecies rt = species.Compiled.Runtime;
        float s = Scale;
        Vector3 center = new Vector3(transform.position.x, 0f, transform.position.z);
        int rocks = Mathf.Clamp(perchCount, 0, 3);
        area = VesperLabLayout.Area(rt, s, center, rocks);

        // the dressing: rock perches (perchable surfaces: the registry first) and the aviary's edge posts
        _ = world.Surfaces;
        dressing = new GameObject("Aviary").transform;
        dressing.SetParent(transform, false);
        Mesh box = RockMesh(VesperLabLayout.RockSizeAt(s));
        meshes.Add(box);
        for (int i = 0; i < rocks; i++)
        {
            // pivot at the base centre, on the ground, unrotated: VesperLabLayout.RockBox(center, i, rocks, s)
            var rock = new GameObject("Rock perch " + i);
            rock.transform.SetParent(dressing, false);
            rock.transform.SetPositionAndRotation(VesperLabLayout.RockAt(center, Vector3.up, i, rocks, s), Quaternion.identity);
            rock.AddComponent<MeshFilter>().sharedMesh = box;
            rock.AddComponent<MeshRenderer>().sharedMaterial = rockMaterial;
            rock.AddComponent<CreatureSurfaceMesh>();
        }
        AviaryDressing.Posts(dressing, center, Vector3.up, area.radius, area.maxHeight, 16, rockMaterial, Flat);

        VesperLabLayout.Start(area, out Vector3 start, out Vector3 fwd);
        var brain = new CommandBrain(area, seed);
        sim = world.Spawn(species, start, fwd, brain, area, seed, s);
        menu.Attach(brain);
        Debug.Log($"[VesperLab] {species.name} at scale {s:F2}: aviary r {area.radius:F1} m (safe {area.safeRadius:F1} m, roof {area.maxHeight:F1} m), {rocks} rock perches; M: menu, F3: skeleton");
    }

    static Mesh RockMesh(Vector3 size)
    {
        VesperLabLayout.RockGeometry(size, rockVerts, rockTris);
        var m = new Mesh { name = "Vesper lab rock" };
        m.SetVertices(rockVerts);
        m.SetTriangles(rockTris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void Teardown()
    {
        if (sim != null && world != null) world.Despawn(sim);
        sim = null;
        if (dressing != null) Destroy(dressing.gameObject);
        dressing = null;
        foreach (Mesh m in meshes) if (m != null) Destroy(m);
        meshes.Clear();
    }

    // A new individual (and rocks for its size); the menu and its command are kept (VesperLabMenu.Attach).
    void Respawn()
    {
        Teardown();
        Build();
    }

    void OnDestroy()
    {
        foreach (Mesh m in meshes) if (m != null) Destroy(m);
        meshes.Clear();
    }

    static readonly Rect kClosedRect = new Rect(10f, 10f, 280f, 46f);
    static readonly Rect kOpenRect = new Rect(10f, 10f, 280f, 92f + 26f * VesperLabMenu.Commands.Length);

    // no strings built per event: the menu caches its texts (rebuilt when the command or Doing changes)
    void OnGUI()
    {
        if (!menu.Open) { GUI.Box(kClosedRect, menu.ClosedText); return; }
        GUILayout.BeginArea(kOpenRect, GUI.skin.box);
        GUILayout.Label(menu.Title);
        for (int i = 0; i < VesperLabMenu.Commands.Length; i++)
            if (GUILayout.Button(menu.ButtonLabel(i))) Pick(VesperLabMenu.Commands[i]);
        GUILayout.Label(menu.Status);
        GUILayout.Label(VesperLabMenu.Hint);
        GUILayout.EndArea();
    }
}
