using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// Owns every creature: ticks each sim toward its brain's target in Update, draws them in LateUpdate with
// one instanced renderer per species (created on first spawn). Allocation-free per frame.
public sealed class CreatureWorld : MonoBehaviour
{
    public Material creatureMaterial;

    // Where climbing creatures find surfaces other than the terrain. Auto: this project - the terrain is the
    // density field, plants and CreatureSurfaceMesh objects get colliders of their own render meshes in a
    // private physics scene (play mode). Physics: another game - the ground and every surface are the
    // colliders in surfaceMask of the default physics scene (TerrainColliders, MeshColliders, buildings).
    public enum GroundSource { Auto, Physics }
    public GroundSource groundSource = GroundSource.Auto;
    public LayerMask surfaceMask = ~0;
    [Tooltip("Edit mode only (tests): the layer render-only surfaces' colliders are put on.")]
    public int proxyLayer = 0;

    public static CreatureWorld Instance { get; private set; }

    readonly List<CreatureSim> sims = new List<CreatureSim>();
    readonly List<ICreatureBrain> brains = new List<ICreatureBrain>();
    readonly List<CreatureSpecies> species = new List<CreatureSpecies>();
    readonly List<CreatureRenderer> renderers = new List<CreatureRenderer>();
    readonly List<CachedGroundProbe> caches = new List<CachedGroundProbe>();
    readonly List<IGroundProbe> simGrounds = new List<IGroundProbe>(); // per sim, parallel to sims
    readonly Stopwatch watch = new Stopwatch();
    CreatureSurfaces surfaces;
    bool ownsSurfaces;

    static readonly Func<Vector3, Vector3> FlatUp = _ => Vector3.up;

    IGroundProbe ground;
    Func<Vector3, Vector3> upAt;
    TerrainModificationSystem edits;
    FieldGroundProbe terrainProbe; // the probe on the terrain's physics field (null with a SetGround ground)
    DensityField boundField;       // the physics field terrainProbe was last pointed at

    public IReadOnlyList<CreatureSim> Sims => sims;
    internal ICreatureBrain BrainOf(int i) => brains[i];
    internal CreatureSurfaces SurfacesIfAny => surfaces;
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

    // The climbing surfaces (created on first use; see groundSource).
    public CreatureSurfaces Surfaces
    {
        get
        {
            if (surfaces == null)
            {
                if (groundSource == GroundSource.Physics) surfaces = CreatureSurfaces.ForScene(Physics.defaultPhysicsScene, surfaceMask, false);
                else if (Application.isPlaying) surfaces = CreatureSurfaces.CreateLocal();
                else surfaces = CreatureSurfaces.ForScene(Physics.defaultPhysicsScene, surfaceMask, true, proxyLayer);
                ownsSurfaces = true;
                CreatureSurfaces.Current = surfaces;
            }
            return surfaces;
        }
    }

    // Tests: a registry of their own (not disposed by the world).
    public void SetSurfaces(CreatureSurfaces s)
    {
        if (ownsSurfaces && surfaces != null && surfaces != s) surfaces.Dispose();
        surfaces = s; ownsSurfaces = false;
        CreatureSurfaces.Current = s;
    }

    // A new creature's sim ground. Walking species: the terrain (cached when field-backed). Climbing species:
    // the terrain plus the surfaces (CompositeSurfaceProbe). groundSource Physics: the colliders themselves.
    IGroundProbe GroundFor(IGroundProbe g, CreatureSpecies sp, float sizeScale, out CachedGroundProbe cache)
    {
        cache = null;
        if (groundSource == GroundSource.Physics) return new PhysicsSurfaceProbe(Surfaces);
        IGroundProbe t = SimGround(g, sp, sizeScale);
        cache = t as CachedGroundProbe;
        if (!sp.climb.canClimb) return t;
        var surf = new PhysicsSurfaceProbe(Surfaces);
        return terrainProbe != null ? new CompositeSurfaceProbe(t, surf, terrainProbe, upAt)
                                    : new CompositeSurfaceProbe(t, surf, g is FieldGroundProbe fp ? fp.Field : null, upAt);
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
        // the surface registry first, so render-only surfaces (the lab's tree) can register as they enable
        if (Application.isPlaying) _ = Surfaces;
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
        IGroundProbe simGround = GroundFor(g, sp, inst.sizeScale, out CachedGroundProbe cache);
        if (cache != null) caches.Add(cache);
        if (sp.climb.canClimb && Application.isPlaying && GetComponent<ClimbableFlora>() == null && FindAnyObjectByType<PlantScatter>() != null)
            gameObject.AddComponent<ClimbableFlora>(); // scattered trees become climbable
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
        IGroundProbe sg = simGrounds[i];
        if (sg is CompositeSurfaceProbe comp) sg = comp.Terrain;
        if (sg is CachedGroundProbe cache) caches.Remove(cache);
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
        if (surfaces != null) surfaces.SyncIfDirty();
        // the surface probes need back faces (inside-solid tests); one switch for the whole tick
        PhysicsSurfaceProbe.BackfaceScope backfaces = surfaces != null ? PhysicsSurfaceProbe.Backfaces() : default;
        try
        {
            for (int i = 0; i < sims.Count; i++)
            {
                CreatureSim sim = sims[i];
                ICreatureBrain brain = brains[i];
                Vector3 target = brain != null ? brain.Target(sim, dt) : sim.Joints[0];
                sim.Tick(dt, target);
            }
        }
        finally { backfaces.Dispose(); }
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
        if (ownsSurfaces && surfaces != null) surfaces.Dispose();
        surfaces = null;
        if (Instance == this) Instance = null;
    }
}
