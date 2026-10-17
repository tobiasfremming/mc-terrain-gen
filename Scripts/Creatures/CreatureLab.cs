using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The creature lab: one enclosure per species on a gentle arc in front of the player's spawn, each with one
// wandering individual. Placement runs once the ground answers, so it adapts to wherever the dunes are,
// preferring the flattest of a few nearby spots.
// Scaling (R22): each species' own CreatureSpecies.scale, or a per-enclosure override in scaleOverrides
// (parallel to species; 0 = the species' scale). Changing either in play mode respawns that enclosure's
// creature at the new size and rebuilds its curb; nothing is checked per frame unless something changed.
[RequireComponent(typeof(CreatureWorld))]
public sealed class CreatureLab : MonoBehaviour
{
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

    const float kWaitSeconds = 10f;
    const int kCandidates = 16, kRimProbes = 8;
    const float kProbeWindow = 6f;
    const float kGoldenAngle = 2.39996323f;
    const float kViewGap = 4f;          // the player starts this far outside the nearest curb, facing it
    internal const float kEnclosureGap = 4f; // free ground between neighbouring curbs
    const float kSunSide = 35f;         // the player stands this far round from the sun's side (modelling light)

    // The enclosure for a species whose largest individual has size scale maxScale: 2 body lengths of
    // radius (room to wander and turn, small enough to see the animal from the curb), at least 5 m.
    public static float EnclosureRadius(CreatureSpecies sp, float maxScale)
        => Mathf.Max(5f, 2f * sp.BodyLength * maxScale);

    // The size scale of the species' largest possible individual (CreatureWorld draws it from the seed).
    public static float MaxScale(CreatureSpecies sp) => MaxScale(sp, sp.scale);
    public static float MaxScale(CreatureSpecies sp, float scale) => scale * (1f + Mathf.Abs(sp.look.sizeJitter));

    // One placed enclosure and its creature.
    sealed class Entry
    {
        public CreatureSpecies species;
        public float scale;                // the effective scale it was spawned at
        public CreatureEnclosure enclosure;
        public CreatureSim sim;
        public int seed;
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
            e.enclosure.radius = EnclosureRadius(e.species, maxScale);
            e.enclosure.Build(ground);
            EnclosureArea area = e.enclosure.AreaFor(e.species, maxScale);
            e.sim = placedWorld.Spawn(e.species, area.center, face, new WanderBrain(area, e.seed), area, e.seed, scale);
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
        if (species == null) return;
        placedWorld = world;
        entries.Clear();
        IGroundProbe ground = world.Ground;
        Vector3 up = PlanetGravity.UpAt(origin);
        Vector3 fwd = Vector3.ProjectOnPlane(player.forward, up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
        fwd.Normalize();

        int n = species.Length;
        // big species get big enclosures: keep neighbours apart and the arc outside the player's spot
        float maxR = 0f;
        for (int k = 0; k < n; k++) if (species[k] != null) maxR = Mathf.Max(maxR, EnclosureRadius(species[k], MaxScale(species[k], ScaleFor(k))));
        float slotSpacing = Mathf.Max(spacing, 2f * maxR + kEnclosureGap);
        float slotRadius = Mathf.Max(arcRadius, maxR + kViewGap + searchRadius);
        var sb = new System.Text.StringBuilder("[CreatureLab] placed:");
        var placed = new List<Vector4>(n); // centre, radius of every enclosure placed so far
        Vector3 nearestCentre = default; float nearestRadius = 0f, nearestDist = float.MaxValue;
        for (int k = 0; k < n; k++)
        {
            CreatureSpecies sp = species[k];
            if (sp == null) { entries.Add(null); continue; }
            // the individual's size is drawn from its seed inside CreatureWorld: size the enclosure and its
            // safe disc for the largest possible individual
            float scale = ScaleFor(k);
            float maxScale = MaxScale(sp, scale);
            float radius = EnclosureRadius(sp, maxScale);

            Vector3 slot = ArcSlot(origin, fwd, up, k, n, slotSpacing, slotRadius);
            Vector3 center = FlattestSpot(ground, slot, up, radius, searchRadius, placed, out float flatness);
            placed.Add(new Vector4(center.x, center.y, center.z, radius));
            Vector3 cUp = PlanetGravity.UpAt(center);

            var go = new GameObject("Enclosure " + sp.name);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(center, Quaternion.FromToRotation(Vector3.up, cUp));
            var enc = go.AddComponent<CreatureEnclosure>();
            enc.radius = radius;
            enc.borderMaterial = borderMaterial;
            enc.Build(ground);

            Vector3 face = Vector3.ProjectOnPlane(origin - center, cUp);
            if (face.sqrMagnitude < 1e-6f) face = fwd;
            if (enclosurePlant != null) PlacePlant(enc, ground, center, cUp, radius, face.normalized, k);
            EnclosureArea area = enc.AreaFor(sp, maxScale);
            CreatureSim sim = world.Spawn(sp, center, face.normalized, new WanderBrain(area, seed + k), area, seed + k, scale);
            entries.Add(new Entry { species = sp, scale = scale, enclosure = enc, sim = sim, seed = seed + k });
            sb.Append($" {sp.name} at {center} (r {radius:F1} m, flatness {flatness:F3})");
            float dist = Vector3.ProjectOnPlane(center - origin, up).magnitude - radius;
            if (dist < nearestDist) { nearestDist = dist; nearestCentre = center; nearestRadius = radius; }
        }
        if (nearestDist < float.MaxValue) FramePlayer(ground, player, origin, nearestCentre, nearestRadius, sb);
        Debug.Log(sb.ToString());
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

    // The shade tree: plantOffset x radius from the centre, 70 deg round from the player's side (in view, not
    // in the way), on the probed ground; its trunk becomes the enclosure's obstacle.
    void PlacePlant(CreatureEnclosure enc, IGroundProbe ground, Vector3 center, Vector3 up, float radius, Vector3 towardPlayer, int k)
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
    }

    // The k-th of n slots on an arc of radius arcRadius around origin, centred on fwd; adjacent slots are
    // `spacing` apart (chord length).
    internal static Vector3 ArcSlot(Vector3 origin, Vector3 fwd, int k, int n, float spacing, float arcRadius)
        => ArcSlot(origin, fwd, Vector3.up, k, n, spacing, arcRadius);

    internal static Vector3 ArcSlot(Vector3 origin, Vector3 fwd, Vector3 up, int k, int n, float spacing, float arcRadius)
    {
        float step = 2f * Mathf.Asin(Mathf.Clamp(spacing / (2f * arcRadius), -1f, 1f));
        float angle = (k - 0.5f * (n - 1)) * step;
        Vector3 dir = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, up) * Vector3.ProjectOnPlane(fwd, up).normalized;
        return origin + dir * arcRadius;
    }

    // The flattest of kCandidates spots (sunflower pattern) within searchRadius of slot: flatness is the
    // lowest dot(normal, up) over kRimProbes probes on the enclosure rim. Candidates that would bring the curb
    // within kEnclosureGap of an enclosure already placed (xyz centre, w radius) are skipped: neighbouring
    // slots are closer than two search radii, so two enclosures drawn to the same flat patch overlapped. If
    // every candidate is too close, the slot is pushed out of its neighbours. Returns the spot's ground point.
    internal static Vector3 FlattestSpot(IGroundProbe ground, Vector3 slot, Vector3 up, float radius, float searchRadius,
                                         List<Vector4> placed, out float best)
    {
        Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(up, t0);
        best = -2f;
        Vector3 bestP = slot;
        bool found = false;
        for (int i = 0; i < kCandidates; i++)
        {
            float r = searchRadius * Mathf.Sqrt(i / (float)kCandidates), a = i * kGoldenAngle;
            Vector3 c = slot + t0 * (r * Mathf.Cos(a)) + t1 * (r * Mathf.Sin(a));
            if (Crowds(c, up, radius, placed) >= 0) continue;
            if (!ground.Probe(c, up, kProbeWindow, out Vector3 cg, out _)) continue;
            float flat = 2f;
            for (int j = 0; j < kRimProbes; j++)
            {
                float b = j * 2f * Mathf.PI / kRimProbes;
                Vector3 q = cg + (t0 * Mathf.Cos(b) + t1 * Mathf.Sin(b)) * radius;
                flat = ground.Probe(q, up, kProbeWindow, out _, out Vector3 nrm) ? Mathf.Min(flat, Vector3.Dot(nrm, up)) : -1f;
                if (flat < best) break;
            }
            if (flat > best) { best = flat; bestP = cg; found = true; }
        }
        if (found) return bestP;
        // no free candidate answered: push the slot clear of every enclosure it crowds, then ground it
        Vector3 p = slot;
        for (int pass = 0; pass < 4; pass++)
        {
            int k = Crowds(p, up, radius, placed);
            if (k < 0) break;
            Vector3 c = placed[k], d = Vector3.ProjectOnPlane(p - c, up);
            if (d.sqrMagnitude < 1e-6f) d = t0;
            p = c + Vector3.Project(p - c, up) + d.normalized * (placed[k].w + radius + kEnclosureGap + 0.01f);
        }
        if (ground.Probe(p, up, kProbeWindow, out Vector3 pg, out Vector3 pn)) { p = pg; best = Vector3.Dot(pn, up); }
        return p;
    }

    // Index of the first placed enclosure a curb of the given radius centred at c would come within
    // kEnclosureGap of (planar), or -1.
    static int Crowds(Vector3 c, Vector3 up, float radius, List<Vector4> placed)
    {
        if (placed == null) return -1;
        for (int k = 0; k < placed.Count; k++)
        {
            Vector4 e = placed[k];
            Vector3 d = Vector3.ProjectOnPlane(c - (Vector3)e, up);
            float need = e.w + radius + kEnclosureGap;
            if (d.sqrMagnitude < need * need) return k;
        }
        return -1;
    }
}
