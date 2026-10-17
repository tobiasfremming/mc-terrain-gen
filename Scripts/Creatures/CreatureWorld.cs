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
    readonly Stopwatch watch = new Stopwatch();

    static readonly Func<Vector3, Vector3> FlatUp = _ => Vector3.up;

    IGroundProbe ground;
    Func<Vector3, Vector3> upAt;
    TerrainModificationSystem edits;

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
                    ground = new FieldGroundProbe(m.PhysicsField);
                    upAt = PlanetGravity.TryGetCenter(out _, out _) ? (Func<Vector3, Vector3>)PlanetGravity.UpAt : FlatUp;
                    edits = m.Modifications;
                    if (edits != null) edits.RegionModified += OnTerrainEdited;
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
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public CreatureSim Spawn(CreatureSpecies sp, Vector3 position, Vector3 forward, ICreatureBrain brain, EnclosureArea? bounds, int seed)
    {
        if (sp == null) throw new ArgumentNullException(nameof(sp));
        string err = sp.Validate();
        if (err != null) throw new ArgumentException(sp.name + ": " + err, nameof(sp));
        IGroundProbe g = Ground;
        if (g == null) throw new InvalidOperationException("CreatureWorld: no ground (no MCChunkManager in the scene and SetGround not called)");

        CreatureInstance inst = Individual(sp, seed);
        IGroundProbe simGround = SimGround(g, sp, inst.sizeScale);
        if (simGround is CachedGroundProbe cache) caches.Add(cache);
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

    // Per-individual look and size from the seed, within the species' jitter ranges. The speed cap needs
    // no extra scaling: the gait's stride limit derives from the size-scaled leg geometry.
    internal static CreatureInstance Individual(CreatureSpecies sp, int seed)
    {
        CreatureLook look = sp.look;
        uint h = (uint)seed * 2654435761u + 0x7F4A7C15u;
        float a = Rand(ref h), b = Rand(ref h), c = Rand(ref h), d = Rand(ref h);
        return new CreatureInstance
        {
            seed = 1024f * a,
            hueShift = look.hueJitter * (2f * b - 1f),
            valueShift = look.valueJitter * (2f * c - 1f),
            sizeScale = sp.scale * (1f + look.sizeJitter * (2f * d - 1f)),
        };
    }

    static float Rand(ref uint s)
    {
        s ^= s >> 16; s *= 0x7FEB352Du; s ^= s >> 15; s *= 0x846CA68Bu; s ^= s >> 16;
        return (s & 0xFFFFFFu) / 16777216f;
    }

    void Update() => TickAll(Time.deltaTime);

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
        renderers.Clear(); species.Clear(); sims.Clear(); brains.Clear(); caches.Clear();
        if (edits != null) { edits.RegionModified -= OnTerrainEdited; edits = null; }
        if (Instance == this) Instance = null;
    }
}
