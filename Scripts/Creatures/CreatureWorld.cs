using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// Owns every creature: ticks each sim toward its brain's target in Update, draws them in LateUpdate with
// one instanced renderer per species (created on first spawn). Allocation-free per frame.
public sealed class CreatureWorld : MonoBehaviour
{
    public Material creatureMaterial;

    public static CreatureWorld Instance { get; private set; }

    readonly List<CreatureSim> sims = new List<CreatureSim>();
    readonly List<ICreatureBrain> brains = new List<ICreatureBrain>();
    readonly List<CreatureSpecies> species = new List<CreatureSpecies>();
    readonly List<CreatureRenderer> renderers = new List<CreatureRenderer>();
    readonly List<CachedGroundProbe> caches = new List<CachedGroundProbe>();
    readonly List<IGroundProbe> simGrounds = new List<IGroundProbe>(); // per sim, parallel to sims
    readonly Stopwatch watch = new Stopwatch();

    static readonly Func<Vector3, Vector3> FlatUp = _ => Vector3.up;

    IGroundProbe ground;
    Func<Vector3, Vector3> upAt;
    TerrainModificationSystem edits;
    FieldGroundProbe terrainProbe; // the probe on the terrain's physics field (null with a SetGround ground)
    DensityField boundField;       // the physics field terrainProbe was last pointed at

    public IReadOnlyList<CreatureSim> Sims => sims;
    public double LastSimMs { get; private set; }

    // The field-backed probe on the terrain's physics field (terrain edits are respected), unless a
    // test supplied its own with SetGround. In a flat world up is the constant +Y (PlanetGravity.UpAt
    // costs a manager lookup per call, and the sim asks it ~150 times a frame).
    public IGroundProbe Ground
    {
        get
        {
            if (ground == null)
            {
                var m = MCChunkManager.Instance;
                if (m != null)
                {
                    upAt = PlanetGravity.TryGetCenter(out _, out _) ? (Func<Vector3, Vector3>)PlanetGravity.UpAt : FlatUp;
                    BindTerrain(m.PhysicsField, m.Modifications);
                }
            }
            return ground;
        }
    }

    // The ground a new creature's sim probes: a field probe gets the creature's own lattice cache (see
    // CachedGroundProbe); analytic probes (tests) are cheap already and are used as they are.
    public static IGroundProbe SimGround(IGroundProbe shared, CreatureSpecies sp, float sizeScale)
        => shared is FieldGroundProbe ? CachedGroundProbe.ForCreature(shared, sp, sizeScale) : shared;

    // Physical edits (digging) change the field the creatures stand on: drop the cached ground there.
    // Visual-only edits (footprints) are not in the physics field.
    void OnTerrainEdited(Bounds b, bool affectsPhysics)
    {
        if (!affectsPhysics) return;
        for (int i = 0; i < caches.Count; i++) caches[i].Invalidate(b);
    }

    public void SetGround(IGroundProbe g, Func<Vector3, Vector3> upAt)
    {
        ground = g; this.upAt = upAt ?? PlanetGravity.UpAt;
        terrainProbe = null; boundField = null;
    }

    // Points the terrain-backed ground at the terrain's current physics field and edit system. The chunk
    // manager rebuilds both as new objects (EnsureField after a domain reload or a world change; a scene
    // reload replaces the manager): the shared probe is re-pointed, every creature's cached ground is
    // dropped (it was read from the old field), and the RegionModified subscription moves over.
    internal void BindTerrain(DensityField physics, TerrainModificationSystem mods)
    {
        if (upAt == null) upAt = FlatUp;
        if (terrainProbe == null)
        {
            terrainProbe = new FieldGroundProbe(physics);
            ground = terrainProbe;
        }
        else if (!ReferenceEquals(physics, boundField))
        {
            terrainProbe.Field = physics;
            for (int i = 0; i < caches.Count; i++) caches[i].Clear();
        }
        boundField = physics;
        if (!ReferenceEquals(mods, edits))
        {
            if (edits != null) edits.RegionModified -= OnTerrainEdited;
            if (mods != null) mods.RegionModified += OnTerrainEdited;
            edits = mods;
        }
    }

    // Per frame: two reference compares once bound (PhysicsField runs the manager's own staleness check).
    void RebindIfStale()
    {
        if (terrainProbe == null) return;
        var m = MCChunkManager.Instance;
        if (m == null) return;
        DensityField f = m.PhysicsField;
        TerrainModificationSystem mods = m.Modifications;
        if (!ReferenceEquals(f, boundField) || !ReferenceEquals(mods, edits)) BindTerrain(f, mods);
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    // scale > 0 overrides the species' own scale for this individual (the lab's per-enclosure override).
    public CreatureSim Spawn(CreatureSpecies sp, Vector3 position, Vector3 forward, ICreatureBrain brain, EnclosureArea? bounds, int seed, float scale = 0f)
    {
        if (sp == null) throw new ArgumentNullException(nameof(sp));
        string err = sp.Validate();
        if (err != null) throw new ArgumentException(sp.name + ": " + err, nameof(sp));
        IGroundProbe g = Ground;
        if (g == null) throw new InvalidOperationException("CreatureWorld: no ground (no MCChunkManager in the scene and SetGround not called)");

        CreatureInstance inst = Individual(sp, seed, scale > 0f ? scale : sp.scale);
        IGroundProbe simGround = SimGround(g, sp, inst.sizeScale);
        if (simGround is CachedGroundProbe cache) caches.Add(cache);
        simGrounds.Add(simGround);
        var sim = new CreatureSim(sp, simGround, upAt, position, forward, inst.sizeScale, seed) { Bounds = bounds };

        int r = species.IndexOf(sp);
        if (r < 0)
        {
            species.Add(sp);
            renderers.Add(new CreatureRenderer(sp, creatureMaterial));
            r = renderers.Count - 1;
        }
        renderers[r].Add(sim, inst);
        sims.Add(sim);
        brains.Add(brain);
        return sim;
    }

    // Removes a creature: no longer ticked or drawn. False if it is not in this world.
    public bool Despawn(CreatureSim sim)
    {
        int i = sims.IndexOf(sim);
        if (i < 0) return false;
        int r = species.IndexOf(sim.Species);
        if (r >= 0) renderers[r].Remove(sim);
        if (simGrounds[i] is CachedGroundProbe cache) caches.Remove(cache);
        sims.RemoveAt(i); brains.RemoveAt(i); simGrounds.RemoveAt(i);
        return true;
    }

    // Per-individual look and size from the seed, within the species' jitter ranges. The speed cap needs
    // no extra scaling: the gait's stride limit derives from the size-scaled leg geometry.
    internal static CreatureInstance Individual(CreatureSpecies sp, int seed) => Individual(sp, seed, sp.scale);

    internal static CreatureInstance Individual(CreatureSpecies sp, int seed, float scale)
    {
        CreatureLook look = sp.look;
        uint h = (uint)seed * 2654435761u + 0x7F4A7C15u;
        float a = Rand(ref h), b = Rand(ref h), c = Rand(ref h), d = Rand(ref h);
        return new CreatureInstance
        {
            seed = 1024f * a,
            hueShift = look.hueJitter * (2f * b - 1f),
            valueShift = look.valueJitter * (2f * c - 1f),
            sizeScale = scale * (1f + look.sizeJitter * (2f * d - 1f)),
        };
    }

    static float Rand(ref uint s)
    {
        s ^= s >> 16; s *= 0x7FEB352Du; s ^= s >> 15; s *= 0x846CA68Bu; s ^= s >> 16;
        return (s & 0xFFFFFFu) / 16777216f;
    }

    void Update()
    {
        RebindIfStale();
        TickAll(Time.deltaTime);
    }

    internal void TickAll(float dt)
    {
        watch.Restart();
        for (int i = 0; i < sims.Count; i++)
        {
            CreatureSim sim = sims[i];
            ICreatureBrain brain = brains[i];
            Vector3 target = brain != null ? brain.Target(sim, dt) : sim.Joints[0];
            sim.Tick(dt, target);
        }
        watch.Stop();
        LastSimMs = watch.Elapsed.TotalMilliseconds;
    }

    void LateUpdate()
    {
        for (int i = 0; i < renderers.Count; i++) renderers[i].Render();
    }

    void OnDestroy()
    {
        for (int i = 0; i < renderers.Count; i++) renderers[i].Dispose();
        renderers.Clear(); species.Clear(); sims.Clear(); brains.Clear(); caches.Clear(); simGrounds.Clear();
        if (edits != null) { edits.RegionModified -= OnTerrainEdited; edits = null; }
        if (Instance == this) Instance = null;
    }
}
