using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The creature lab: one enclosure per species on a gentle arc in front of the player's spawn, each with one
// wandering individual. The animals are the roster's (CreatureLabRoster); each gets the habitat its modules ask
// for (CreatureKit.LabHabitat: aviary, climbable or plain enclosure; the grove is OctopusLab's) and the brain its
// behaviour modules ask for (CreatureKit.BrainFactory). Placement runs once the ground answers, so it adapts to wherever the dunes are,
// preferring the flattest of a few nearby spots.
// Scaling (R22): each species' own CreatureSpecies.scale, or a per-enclosure override in scaleOverrides
// (parallel to species; 0 = the species' scale). Changing either in play mode respawns that enclosure's
// creature at the new size and rebuilds its curb; nothing is checked per frame unless something changed.
[RequireComponent(typeof(CreatureWorld))]
public sealed class CreatureLab : MonoBehaviour
{
    [Tooltip("The lab's animals (Config/Creatures/CreatureLabRoster.asset): when set, species and scaleOverrides come from it; each entry's habitat follows its species' modules (CreatureKit.LabHabitat).")]
    public CreatureLabRoster roster;
    public CreatureSpecies[] species;
    [Tooltip("Per-species size override, parallel to species (0 = use the species' own scale).")]
    public float[] scaleOverrides;
    public float spacing = 25f, arcRadius = 30f;
    public int seed = 7;
    public float searchRadius = 15f;   // flat-spot search around each arc slot
    public Material borderMaterial;
    [Tooltip("Optional shade tree placed in every enclosure (a baked plant prototype set); the creature walks around it.")]
    public PlantPrototypeSet enclosurePlant;
    public float plantScale = 1.2f;
    [Tooltip("Distance of the tree from the enclosure centre, as a fraction of the radius.")]
    public float plantOffset = 0.45f;

    [Header("Aviary (flying species)")]
    [Tooltip("A flying species gets an aviary: this radius and height at scale 1 (larger individuals scale it up to 1.5x; past that, or when its flight needs more room, AviarySizing follows its turn radius, length and manoeuvres), trees and rock perches.")]
    public float aviaryRadius = 14f;
    public float aviaryHeight = 10f;
    [Range(0, 3)] public int aviaryTrees = 2;
    [Range(0, 6)] public int aviaryRocks = 3;

    const float kWaitSeconds = 10f;
    const float kProbeWindow = LabLayout.kProbeWindow;
    const float kViewGap = LabLayout.kViewGap;          // the player starts this far outside the nearest curb, facing it
    internal const float kEnclosureGap = LabLayout.kEnclosureGap; // free ground between neighbouring curbs
    const float kSunSide = 35f;         // the player stands this far round from the sun's side (modelling light)

    // The enclosure for a species whose largest individual has size scale maxScale: 2 body lengths of
    // radius (room to wander and turn, small enough to see the animal from the curb), at least 5 m.
    public static float EnclosureRadius(CreatureSpecies sp, float maxScale) => CreatureKit.LabHabitat.EnclosureRadius(sp, maxScale, kAviaryRadius);

    // Design decision 4: the aviary is 14 m x 10 m at scale 1 (the serpent's turn fits: FlightBrain.FitsAviary).
    internal const float kAviaryRadius = 14f, kAviaryHeight = 10f;
    float EnclosureRadiusFor(CreatureSpecies sp, float maxScale) => CreatureKit.LabHabitat.EnclosureRadius(sp, maxScale, aviaryRadius);

    // The size scale of the species' largest possible individual (CreatureWorld draws it from the seed).
    public static float MaxScale(CreatureSpecies sp) => MaxScale(sp, sp.scale);
    public static float MaxScale(CreatureSpecies sp, float scale) => scale * (1f + Mathf.Abs(sp.Compiled.Runtime.look.sizeJitter));

    // One placed enclosure and its creature.
    sealed class Entry
    {
        public CreatureSpecies species;
        public float scale;                // the effective scale it was spawned at
        public CreatureEnclosure enclosure;
        public CreatureSim sim;
        public int seed;
        public EnclosurePlant plant;
        public PerchSpot[] perches;     // flying species: rock tops and tree crowns
        public float aviaryHeight;
    }

    // Climbing (2026-10-17): the shade tree is a climbable obstacle for a species that can climb, up to
    // kPlantClimbShare of the plant's height (and the species' own cap); such a species gets a ClimbingBrain.
    const float kPlantClimbShare = 0.45f;

    internal static EnclosureArea AreaFor(CreatureEnclosure enc, CreatureSpecies sp, float maxScale, EnclosurePlant plant)
    {
        EnclosureArea area = enc.AreaFor(sp, maxScale);
        if (plant != null && sp.climb.canClimb && area.ObstacleCount > 0)
        {
            area.obstacleClimbable = new bool[area.ObstacleCount];
            for (int i = 0; i < area.obstacleClimbable.Length; i++) area.obstacleClimbable[i] = true;
            area.maxHeight = Mathf.Min(sp.climb.MaxHeightBodyLengths * sp.BodyLength * maxScale, kPlantClimbShare * plant.Height);
        }
        return area;
    }

    // The brain from the species' behaviour modules (CreatureKit.BrainFactory: the shipped species keep exactly
    // the pre-kit choice - flyer FlightBrain, climber with a climbable tree ClimbingBrain, else WanderBrain).
    internal static ICreatureBrain BrainFor(EnclosureArea area, CreatureSpecies sp, int seed, PerchSpot[] perches = null)
        => CreatureKit.BrainFactory.Create(sp, area, seed, perches);

    // A flying species' area: the aviary's height on top of the usual disc (its trees and rocks are walked round).
    static EnclosureArea AviaryArea(CreatureEnclosure enc, CreatureSpecies sp, float maxScale, float height)
    {
        EnclosureArea area = enc.AreaFor(sp, maxScale);
        area.maxHeight = height;
        return area;
    }

    readonly List<Entry> entries = new List<Entry>();
    CreatureWorld placedWorld;
    bool scalesDirty;

    // The scale enclosure k uses: its override, else the species' own.
    public float ScaleFor(int k)
    {
        float o = scaleOverrides != null && k < scaleOverrides.Length ? scaleOverrides[k] : 0f;
        return o > 0f ? o : (species[k] != null ? species[k].scale : 1f);
    }

    internal CreatureSim SimFor(int k) => k < entries.Count && entries[k] != null ? entries[k].sim : null;
    internal CreatureEnclosure EnclosureFor(int k) => k < entries.Count && entries[k] != null ? entries[k].enclosure : null;

    // the roster first: OctopusLab's Start reads the species list (it waits for these enclosures)
    void Awake() => UseRoster();
    void OnEnable() => CreatureSpecies.Changed += OnSpeciesChanged;
    void OnDisable() => CreatureSpecies.Changed -= OnSpeciesChanged;
    void OnSpeciesChanged(CreatureSpecies sp) => scalesDirty = true;
    void OnValidate() => scalesDirty = true;

    void Update()
    {
        if (!scalesDirty) return;
        scalesDirty = false;
        ApplyScales();
    }

    // Respawns every enclosure whose effective scale changed: the old creature is despawned, the curb and
    // safe disc rebuilt for the new size around the same centre, and a new individual spawned there.
    internal void ApplyScales()
    {
        if (placedWorld == null) return;
        IGroundProbe ground = placedWorld.Ground;
        for (int k = 0; k < entries.Count; k++)
        {
            Entry e = entries[k];
            if (e == null || e.species == null) continue;
            float scale = ScaleFor(k);
            if (Mathf.Approximately(scale, e.scale)) continue;
            Vector3 face = e.sim != null ? e.sim.Rotations[0] * Vector3.forward : e.enclosure.transform.forward;
            placedWorld.Despawn(e.sim);
            e.scale = scale;
            float maxScale = MaxScale(e.species, scale);
            bool aviary = CreatureKit.LabHabitat.For(e.species) == CreatureKit.Habitat.Aviary;
            if (!aviary) e.enclosure.radius = EnclosureRadius(e.species, maxScale); // an aviary keeps its size
            e.enclosure.Build(ground);
            EnclosureArea area = aviary ? AviaryArea(e.enclosure, e.species, maxScale, e.aviaryHeight) : AreaFor(e.enclosure, e.species, maxScale, e.plant);
            e.sim = placedWorld.Spawn(e.species, area.center, face, BrainFor(area, e.species, e.seed, e.perches), area, e.seed, scale);
        }
    }

    IEnumerator Start()
    {
        CreatureWorld world = GetComponent<CreatureWorld>();
        if (GetComponent<CreatureDebugView>() == null) gameObject.AddComponent<CreatureDebugView>(); // F3 overlay
        Transform player = null;
        Vector3 groundAtPlayer = default;
        float waited = 0f;
        bool ready = false;
        while (waited <= kWaitSeconds)
        {
            MCChunkManager m = MCChunkManager.Instance;
            if (m != null && world.Ground != null)
            {
                if (player == null) player = m.target != null ? m.target : (Camera.main != null ? Camera.main.transform : null);
                if (player != null)
                {
                    Vector3 p = player.position;
                    if (world.Ground.Probe(p, PlanetGravity.UpAt(p), kProbeWindow, out groundAtPlayer, out _)) { ready = true; break; }
                }
            }
            yield return null;
            waited += Time.unscaledDeltaTime;
        }
        if (!ready)
        {
            Debug.LogWarning("[CreatureLab] no ground at the player after " + kWaitSeconds + " s; no enclosures placed.");
            yield break;
        }
        Place(world, player, groundAtPlayer);
    }

    internal void Place(CreatureWorld world, Transform player, Vector3 origin)
    {
        UseRoster();
        if (species == null) return;
        placedWorld = world;
        entries.Clear();
        IGroundProbe ground = world.Ground;
        Vector3 up = PlanetGravity.UpAt(origin);
        Vector3 fwd = Vector3.ProjectOnPlane(player.forward, up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
        fwd.Normalize();

        int n = species.Length;
        // the individual's size is drawn from its seed inside CreatureWorld: size each enclosure and its safe disc
        // for the largest possible individual; big species get big enclosures (LabLayout keeps neighbours apart
        // and the arc outside the player's spot). A grove species is OctopusLab's (no slot here).
        var radius = new float[n];
        for (int k = 0; k < n; k++)
        {
            CreatureSpecies sp = RuntimeOf(k);
            if (sp == null || CreatureKit.LabHabitat.For(sp) == CreatureKit.Habitat.Grove) continue;
            radius[k] = EnclosureRadiusFor(sp, MaxScale(sp, ScaleFor(k)));
        }
        var flatness = new float[n];
        var placed = new List<Vector4>(n); // centre, radius of every enclosure placed so far
        Vector3[] centres = LabLayout.Centres(ground, origin, fwd, up, radius, spacing, arcRadius, searchRadius, flatness, placed);
        var sb = new System.Text.StringBuilder("[CreatureLab] placed:");
        Vector3 nearestCentre = default; float nearestRadius = 0f, nearestDist = float.MaxValue;
        for (int k = 0; k < n; k++)
        {
            CreatureSpecies sp = RuntimeOf(k);
            if (sp == null || !(radius[k] > 0f)) { entries.Add(null); continue; }
            float scale = ScaleFor(k);
            float maxScale = MaxScale(sp, scale);
            float r = radius[k];
            Vector3 center = centres[k];
            Vector3 cUp = PlanetGravity.UpAt(center);
            CreatureKit.Habitat habitat = CreatureKit.LabHabitat.For(sp);

            var go = new GameObject("Enclosure " + sp.name);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(center, Quaternion.FromToRotation(Vector3.up, cUp));
            var enc = go.AddComponent<CreatureEnclosure>();
            enc.radius = r;
            enc.borderMaterial = borderMaterial;
            enc.Build(ground);

            Vector3 face = Vector3.ProjectOnPlane(origin - center, cUp);
            if (face.sqrMagnitude < 1e-6f) face = fwd;
            EnclosurePlant plant = null; PerchSpot[] perches = null; float height = 0f;
            EnclosureArea area;
            if (habitat == CreatureKit.Habitat.Aviary)
            {
                height = AviarySizing.Height(sp, maxScale, aviaryHeight);
                _ = world.Surfaces; // the registry first: the rocks register as they are made
                perches = DressAviary(enc, ground, center, cUp, r, height, face.normalized, k, out plant);
                area = AviaryArea(enc, sp, maxScale, height);
                if (!FlightBrain.FitsAviary(sp, area, maxScale)) Debug.LogWarning($"[CreatureLab] {sp.name} at scale {maxScale:F2} cannot turn inside a {r:F0} m aviary");
            }
            else
            {
                // Plain and Climbable: the shade tree (climbable for a climber: AreaFor)
                plant = enclosurePlant != null ? PlacePlant(enc, ground, center, cUp, r, face.normalized, k) : null;
                area = AreaFor(enc, sp, maxScale, plant);
            }
            CreatureSim sim = world.Spawn(sp, center, face.normalized, BrainFor(area, sp, seed + k, perches), area, seed + k, scale);
            entries.Add(new Entry { species = sp, scale = scale, enclosure = enc, sim = sim, seed = seed + k, plant = plant, perches = perches, aviaryHeight = height });
            sb.Append($" {sp.name} ({habitat}) at {center} (r {r:F1} m, flatness {flatness[k]:F3})");
            float dist = Vector3.ProjectOnPlane(center - origin, up).magnitude - r;
            if (dist < nearestDist) { nearestDist = dist; nearestCentre = center; nearestRadius = r; }
        }
        if (nearestDist < float.MaxValue) FramePlayer(ground, player, origin, nearestCentre, nearestRadius, sb);
        Debug.Log(sb.ToString());
    }

    // Species k as the systems read it (a kit asset: its compiled runtime view; a legacy asset itself).
    CreatureSpecies RuntimeOf(int k) => species[k] != null ? species[k].Compiled.Runtime : null;

    // With a roster (Config/Creatures/CreatureLabRoster.asset) the lab's animals are its entries: species and
    // scale overrides in roster order, the grove species handed to the OctopusLab on this object.
    internal void UseRoster()
    {
        if (roster == null || roster.entries == null || roster.entries.Count == 0) return;
        roster.Split(out List<CreatureLabRoster.Entry> list, out CreatureSpecies grove);
        species = new CreatureSpecies[list.Count];
        scaleOverrides = new float[list.Count];
        for (int k = 0; k < list.Count; k++) { species[k] = list[k].species; scaleOverrides[k] = list[k].scaleOverride; }
        if (grove != null)
        {
            OctopusLab ol = GetComponent<OctopusLab>();
            if (ol != null && ol.octopus == null) ol.octopus = grove;
        }
    }

    // Moves the player to kViewGap outside the nearest curb, facing its centre, on the sun's side (turned
    // kSunSide round from it, so the light models the animal instead of backlighting it); without a sun, on
    // the line toward where the player spawned. The whole enclosure and its animal are in view from the start.
    static void FramePlayer(IGroundProbe ground, Transform player, Vector3 origin, Vector3 centre, float radius, System.Text.StringBuilder sb)
    {
        Vector3 up = PlanetGravity.UpAt(centre);
        Vector3 away = Vector3.ProjectOnPlane(origin - centre, up);
        Light sun = RenderSettings.sun;
        if (sun == null)
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length && sun == null; i++) if (lights[i].type == LightType.Directional) sun = lights[i];
        }
        if (sun != null)
        {
            Vector3 toSun = Vector3.ProjectOnPlane(-sun.transform.forward, up);
            if (toSun.sqrMagnitude > 1e-4f)
            {
                // of the two sides kSunSide off the sun, the one nearer the spawn
                Vector3 a = Quaternion.AngleAxis(kSunSide, up) * toSun, b = Quaternion.AngleAxis(-kSunSide, up) * toSun;
                away = Vector3.Dot(a, away) >= Vector3.Dot(b, away) ? a : b;
            }
        }
        if (away.sqrMagnitude < 1e-6f) away = -Vector3.ProjectOnPlane(player.forward, up);
        away.Normalize();
        Vector3 spot = centre + away * (radius + kViewGap);
        if (!ground.Probe(spot + up * 2f, up, kProbeWindow + 2f, out Vector3 g, out _)) return;
        // the player's pivot height above the ground it stood on
        float lift = Mathf.Max(0f, Vector3.Dot(player.position - origin, PlanetGravity.UpAt(origin)));
        var cc = player.GetComponent<CharacterController>();
        bool ccOn = cc != null && cc.enabled;
        if (ccOn) cc.enabled = false;
        player.SetPositionAndRotation(g + up * (lift + 0.05f), Quaternion.LookRotation(-away, up));
        if (ccOn) cc.enabled = true;
        player.SendMessage("ResetOrientation", SendMessageOptions.DontRequireReceiver); // GravityAligner
        sb.Append($"; player framed at {player.position} facing the enclosure");
    }

    // A flying species' aviary: posts to its roof, aviaryTrees trees (enclosurePlant, at plantScale) and
    // aviaryRocks rock perches round the middle, all walked round on the ground (obstacles); their tops are the
    // brain's perches (a tree's crown as a region the brain searches for a branch top, a rock's top likewise).
    PerchSpot[] DressAviary(CreatureEnclosure enc, IGroundProbe ground, Vector3 center, Vector3 up, float radius, float height,
                            Vector3 towardPlayer, int k, out EnclosurePlant firstPlant)
    {
        firstPlant = null;
        var perches = new List<PerchSpot>();
        enc.obstacles.Clear();
        AviaryDressing.Posts(enc.transform, center, up, radius, height, 10, borderMaterial, ground);
        int trees = enclosurePlant != null ? aviaryTrees : 0;
        for (int t = 0; t < trees; t++)
        {
            Vector3 dir = Quaternion.AngleAxis(70f + 160f * t, up) * towardPlayer;
            Vector3 at = center + dir * (0.4f * radius);
            if (ground.Probe(at + up * 2f, up, kProbeWindow + 2f, out Vector3 g, out _)) at = g;
            var go = new GameObject("Plant " + enclosurePlant.name);
            go.transform.SetParent(enc.transform, true);
            go.transform.SetPositionAndRotation(at - up * (0.1f * plantScale), Quaternion.AngleAxis(137f * (k + t), up) * Quaternion.FromToRotation(Vector3.up, up));
            var plant = go.AddComponent<EnclosurePlant>();
            plant.Init(enclosurePlant, seed + k + 31 * t, plantScale);
            if (firstPlant == null) firstPlant = plant;
            enc.obstacles.Add(new Vector4(at.x, at.y, at.z, plant.TrunkRadius));
            perches.Add(new PerchSpot { point = at + up * (0.95f * plant.Height), radius = Mathf.Max(1f, 0.35f * plant.Height) });
        }
        uint h = (uint)(seed + k) * 2654435761u + 7u;
        for (int r = 0; r < aviaryRocks; r++)
        {
            h ^= h << 13; h ^= h >> 17; h ^= h << 5;
            float jitter = (h & 0xFFFF) / 65535f;
            Vector3 dir = Quaternion.AngleAxis(-40f + 120f * r + 25f * jitter, up) * towardPlayer;
            Vector3 at = center + dir * ((0.25f + 0.3f * jitter) * radius);
            if (ground.Probe(at + up * 2f, up, kProbeWindow + 2f, out Vector3 g, out _)) at = g;
            var size = new Vector3(1.6f + 0.8f * jitter, 0.9f + 0.6f * jitter, 1.4f + 0.6f * (1f - jitter));
            GameObject rock = AviaryDressing.Rock(enc.transform, at, up, size, seed + k + r, borderMaterial, out Mesh mesh);
            rock.AddComponent<OwnedMesh>().mesh = mesh;
            enc.obstacles.Add(new Vector4(at.x, at.y, at.z, 0.5f * Mathf.Max(size.x, size.z)));
            perches.Add(new PerchSpot { point = at + up * size.y, radius = 0.3f * Mathf.Min(size.x, size.z) });
        }
        return perches.ToArray();
    }

    // Destroys a generated mesh with its object (the aviary's rocks).
    sealed class OwnedMesh : MonoBehaviour
    {
        public Mesh mesh;
        void OnDestroy() { if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } }
    }

    // The shade tree: plantOffset x radius from the centre, 70 deg round from the player's side (in view, not
    // in the way), on the probed ground; its trunk becomes the enclosure's obstacle.
    EnclosurePlant PlacePlant(CreatureEnclosure enc, IGroundProbe ground, Vector3 center, Vector3 up, float radius, Vector3 towardPlayer, int k)
    {
        Vector3 dir = Quaternion.AngleAxis(70f, up) * towardPlayer;
        Vector3 at = center + dir * (plantOffset * radius);
        if (ground.Probe(at + up * 2f, up, kProbeWindow + 2f, out Vector3 g, out _)) at = g;
        var go = new GameObject("Plant " + enclosurePlant.name);
        go.transform.SetParent(enc.transform, true);
        // sunk a little so the root flare meets sloping sand
        go.transform.SetPositionAndRotation(at - up * (0.1f * plantScale), Quaternion.AngleAxis(137f * k, up) * Quaternion.FromToRotation(Vector3.up, up));
        var plant = go.AddComponent<EnclosurePlant>();
        plant.Init(enclosurePlant, seed + k, plantScale);
        enc.obstacles.Clear();
        enc.obstacles.Add(new Vector4(at.x, at.y, at.z, plant.TrunkRadius));
        return plant;
    }

    // The lab's layout (LabLayout: pure geometry, also run offline); kept here for OctopusLab and the tests.
    internal static Vector3 ArcSlot(Vector3 origin, Vector3 fwd, int k, int n, float spacing, float arcRadius)
        => LabLayout.ArcSlot(origin, fwd, Vector3.up, k, n, spacing, arcRadius);
    internal static Vector3 ArcSlot(Vector3 origin, Vector3 fwd, Vector3 up, int k, int n, float spacing, float arcRadius)
        => LabLayout.ArcSlot(origin, fwd, up, k, n, spacing, arcRadius);
    internal static Vector3 FlattestSpot(IGroundProbe ground, Vector3 slot, Vector3 up, float radius, float searchRadius,
                                         List<Vector4> placed, out float best)
        => LabLayout.FlattestSpot(ground, slot, up, radius, searchRadius, placed, out best);
}
