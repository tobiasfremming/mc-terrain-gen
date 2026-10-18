using System.Collections.Generic;
using UnityEngine;

// Scattered plants as climbing surfaces. Every `interval` seconds the PlantScatter instances of climbable
// species (obstacleRadius > 0) near climbing creatures are gathered, and each gets a MeshCollider of its own
// bark: the variant's LOD0 render mesh, submesh 0 (SurfaceMeshCache.BarkOnly), at the instance's transform, in
// the creatures' surface registry (a local physics scene in play mode). Colliders are pooled: a plant that is
// still wanted keeps its collider, one no longer near any creature gives it back. Cooking a new bark mesh
// costs 1-3 ms, so at most one is cooked per frame; the rest wait a frame.
[RequireComponent(typeof(CreatureWorld))]
public sealed class ClimbableFlora : MonoBehaviour
{
    [Tooltip("The scatter whose plants are climbable. Empty: the first PlantScatter in the scene.")]
    public PlantScatter scatter;
    [Tooltip("Seconds between gathers.")]
    public float interval = 0.25f;
    [Tooltip("Metres around each climbing creature (times its size) within which plants get colliders.")]
    public float reach = 3f;

    sealed class Slot
    {
        public MeshCollider collider;
        public Vector3 key;
        public Mesh lod0;
        public bool active, wanted;
    }

    CreatureWorld world;
    readonly List<PlantScatter.Climbable> found = new List<PlantScatter.Climbable>(64);
    readonly List<Slot> pool = new List<Slot>();
    readonly HashSet<Vector3> seen = new HashSet<Vector3>();
    float timer;

    public int PoolSize => pool.Count;
    public int ActiveCount { get; private set; }
    public int Gathers { get; private set; }

    void Awake() => world = GetComponent<CreatureWorld>();

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = interval;
        Gather();
    }

    internal void Gather()
    {
        if (world == null) world = GetComponent<CreatureWorld>();
        CreatureSurfaces surfaces = CreatureSurfaces.Current;
        if (world == null || surfaces == null) return;
        if (scatter == null) scatter = FindAnyObjectByType<PlantScatter>();
        if (scatter == null) return;
        Gathers++;

        found.Clear();
        var sims = world.Sims;
        for (int s = 0; s < sims.Count; s++)
        {
            CreatureSim sim = sims[s];
            if (!sim.CanClimb) continue;
            Vector3 lo = sim.Joints[0], hi = lo;
            for (int i = 1; i < sim.Joints.Length; i++) { lo = Vector3.Min(lo, sim.Joints[i]); hi = Vector3.Max(hi, sim.Joints[i]); }
            var b = new Bounds(0.5f * (lo + hi), hi - lo);
            b.Expand(2f * reach * sim.SizeScale);
            scatter.GetClimbables(b, found);
        }
        // self-ticking bodies (the tree octopus): their simulated points, grown by their own reach
        var bodies = world.Bodies;
        for (int s = 0; s < bodies.Count; s++)
        {
            ICreatureBody body = bodies[s];
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            body.EncapsulateBounds(ref lo, ref hi);
            var b = new Bounds(0.5f * (lo + hi), hi - lo);
            b.Expand(2f * reach * body.SizeScale);
            scatter.GetClimbables(b, found);
        }

        for (int i = 0; i < pool.Count; i++) pool[i].wanted = false;
        seen.Clear();
        // keep the plants that already have a collider
        for (int f = 0; f < found.Count; f++)
        {
            Vector3 key = found[f].pos;
            for (int i = 0; i < pool.Count; i++)
                if (pool[i].active && pool[i].key == key && pool[i].lod0 == found[f].lod0) { pool[i].wanted = true; seen.Add(key); break; }
        }
        // give up the colliders of plants no longer near
        for (int i = 0; i < pool.Count; i++)
        {
            Slot sl = pool[i];
            if (!sl.active || sl.wanted) continue;
            surfaces.Unregister(sl.collider);
            sl.collider.enabled = false;
            sl.active = false;
        }
        // place the new ones, cooking at most one new mesh
        bool cooked = false;
        for (int f = 0; f < found.Count; f++)
        {
            PlantScatter.Climbable c = found[f];
            if (!seen.Add(c.pos)) continue;
            SurfaceMeshCache.Entry e = SurfaceMeshCache.BarkOnly(c.lod0);
            if (e == null) continue;
            if (!e.baked)
            {
                if (cooked) { timer = 0f; continue; } // next frame
                cooked = true;
            }
            Slot sl = FreeSlot();
            Vector3 pos = c.trs.GetColumn(3);
            Quaternion rot = c.trs.rotation;
            Vector3 scl = c.trs.lossyScale;
            if (sl.collider == null)
            {
                sl.collider = surfaces.CreateProxy("Climbable " + c.lod0.name, e, pos, rot, scl);
                if (sl.collider == null) continue;
            }
            else
            {
                sl.collider.enabled = true;
                surfaces.MoveProxy(sl.collider, e, pos, rot, scl);
                surfaces.Register(sl.collider, e);
            }
            sl.key = c.pos; sl.lod0 = c.lod0; sl.active = true; sl.wanted = true;
        }
        int active = 0;
        for (int i = 0; i < pool.Count; i++) if (pool[i].active) active++;
        ActiveCount = active;
        surfaces.SyncIfDirty();
    }

    Slot FreeSlot()
    {
        for (int i = 0; i < pool.Count; i++) if (!pool[i].active && !pool[i].wanted) return pool[i];
        var s = new Slot();
        pool.Add(s);
        return s;
    }

    // Surface AABBs of the pooled colliders (debug view).
    internal int ActiveBounds(List<Bounds> result)
    {
        int k = 0;
        for (int i = 0; i < pool.Count; i++)
            if (pool[i].active && pool[i].collider != null) { result?.Add(pool[i].collider.bounds); k++; }
        return k;
    }

    void OnDestroy()
    {
        CreatureSurfaces surfaces = CreatureSurfaces.Current;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i].collider == null) continue;
            if (surfaces != null) surfaces.DestroyProxy(pool[i].collider);
            else if (Application.isPlaying) Destroy(pool[i].collider.gameObject);
            else DestroyImmediate(pool[i].collider.gameObject);
        }
        pool.Clear();
    }
}
