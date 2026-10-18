using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Tree trunks as things bodies bump into and agents walk around.
//
// PlantScatter draws plants as instance matrices and nothing else, so without
// this a Rigidbody rolls straight through a trunk and a NavMeshAgent paths
// through a grove as if it were a lawn. Species opt in with
// PlantSpecies.obstacleRadius/obstacleHeight (0 = no obstacle, the default),
// and each such plant becomes one upright capsule from its base along the
// local up:
//
//   * NavMesh: every time TerrainNavMesh (re)builds a patch it raises
//     CollectExtraSources; this appends one Capsule source per trunk in the
//     patch with area 1 (Not Walkable), so the patch is carved around it.
//     Nothing persists between builds -- the sources describe the plots as
//     they are at that moment.
//   * Physics: every refreshInterval (unscaled) the trunks within each
//     TerrainAnchor's max(physicsRadius, navRadius) are gathered and a pool of child
//     CapsuleColliders, on the terrain layer, is moved onto them. Colliders
//     only where anchors are, because a static collider per plant across a
//     900 m plant radius would be tens of thousands of them.
//
// The gap, deliberately left open: PlantScatter builds plots only around its
// eye (the player). A query near a distant anchor finds no built plots and
// returns nothing, so NPCs far from the player walk through trees nobody is
// looking at. Building plots for anchors too would make placement cost follow
// every NPC.
//
// Keep this component's transform at unit scale: the colliders are its
// children and inherit its lossyScale.
public class FloraObstacles : MonoBehaviour
{
    [Tooltip("The scatter whose built plots are read. Left empty, the first PlantScatter in the scene is found (and looked for again while none exists).")]
    public PlantScatter scatter;

    [Tooltip("Seconds (unscaled) between collider refreshes around the anchors. Trunks never move, so this only has to keep up with anchors walking and plots finishing their build.")]
    public float refreshInterval = 0.5f;

    [Tooltip("Most trunk colliders alive at once, over all anchors. Over the cap the trunks nearest an anchor win.")]
    public int maxColliders = 512;

    struct Candidate
    {
        public Vector3 basePos, up;
        public float radius, height, key;   // key: capsule-surface distance to the nearest anchor
    }

    sealed class ByKey : IComparer<Candidate>
    {
        public int Compare(Candidate a, Candidate b) => a.key.CompareTo(b.key);
    }
    static readonly ByKey s_byKey = new ByKey();

    sealed class Slot
    {
        public GameObject go;
        public CapsuleCollider collider;
        public Vector3 basePos, up;
        public float radius, height;
        public int layer = -1;
    }

    readonly List<(Vector3 basePos, Vector3 up, float radius, float height)> _query = new();
    readonly List<(Vector3 basePos, Vector3 up, float radius, float height)> _navQuery = new();
    readonly List<Candidate> _found = new();
    readonly HashSet<Vector3> _seen = new();
    readonly List<Slot> _pool = new();
    int _active;
    float _nextRefresh;
    float _nextFind;

    void OnEnable()
    {
        TerrainNavMesh.CollectExtraSources += OnCollectSources;
        _nextRefresh = 0f;
    }

    void OnDisable()
    {
        TerrainNavMesh.CollectExtraSources -= OnCollectSources;
        for (int i = 0; i < _pool.Count; i++)
            if (_pool[i].go != null) _pool[i].go.SetActive(false);
        _active = 0;
    }

    void Update()
    {
        float now = Time.unscaledTime;
        if (now < _nextRefresh) return;
        _nextRefresh = now + Mathf.Max(0.02f, refreshInterval);
        RefreshColliders();
    }

    PlantScatter ResolveScatter()
    {
        if (scatter != null) return scatter;
        // FindFirstObjectByType walks the scene; while there is no scatter at
        // all, do not pay for that on every NavMesh build and refresh.
        float now = Time.unscaledTime;
        if (now < _nextFind) return null;
        _nextFind = now + 2f;
        scatter = FindFirstObjectByType<PlantScatter>();
        return scatter;
    }

    // ---- NavMesh ----------------------------------------------------------

    void OnCollectSources(List<NavMeshBuildSource> sources, Bounds world)
    {
        PlantScatter s = ResolveScatter();
        if (s == null || sources == null) return;
        _navQuery.Clear();
        s.GetObstacles(world, _navQuery);
        for (int i = 0; i < _navQuery.Count; i++)
        {
            var o = _navQuery[i];
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Capsule,
                transform = FloraObstacleMath.CapsuleSourceMatrix(o.basePos, o.up, o.radius, o.height),
                size = FloraObstacleMath.CapsuleSourceSize(o.radius, o.height),
                area = 1, // Not Walkable
            });
        }
        _navQuery.Clear();
    }

    // ---- physics ----------------------------------------------------------

    // Trunk colliders reach max(physicsRadius, navRadius) around an anchor:
    // anything that navigates that far can also walk into a trunk that far
    // out, and the player's recommended physicsRadius 0 (its ground comes
    // from the clipmap) must not leave it walking through trees.
    static float TrunkReach(TerrainAnchor a) => Mathf.Max(a.physicsRadius, a.navRadius);

    void RefreshColliders()
    {
        _found.Clear();
        _seen.Clear();
        IReadOnlyList<TerrainAnchor> anchors = TerrainAnchor.All;
        PlantScatter s = ResolveScatter();

        if (s != null)
        {
            // Union of one box query per anchor. A trunk seen from two anchors
            // is the same plant (same instance, bit-identical base), so the
            // base position is the identity.
            for (int a = 0; a < anchors.Count; a++)
            {
                TerrainAnchor anchor = anchors[a];
                float reach = anchor != null ? TrunkReach(anchor) : 0f;
                if (reach <= 0f) continue;
                _query.Clear();
                s.GetObstacles(new Bounds(anchor.Position, Vector3.one * (reach * 2f)), _query);
                for (int i = 0; i < _query.Count; i++)
                {
                    var o = _query[i];
                    if (!_seen.Add(o.basePos)) continue;
                    _found.Add(new Candidate { basePos = o.basePos, up = o.up, radius = o.radius, height = o.height });
                }
            }
            _query.Clear();

            // The boxes' corners reach past the spheres: keep a trunk only if
            // its capsule is within some anchor's reach, ranked by how
            // near the nearest anchor is.
            int kept = 0;
            for (int i = 0; i < _found.Count; i++)
            {
                Candidate c = _found[i];
                float best = float.MaxValue;
                for (int a = 0; a < anchors.Count; a++)
                {
                    TerrainAnchor anchor = anchors[a];
                    float reach = anchor != null ? TrunkReach(anchor) : 0f;
                    if (reach <= 0f) continue;
                    float d = FloraObstacleMath.CapsuleDistance(anchor.Position, c.basePos, c.up, c.radius, c.height);
                    if (d <= reach && d < best) best = d;
                }
                if (best == float.MaxValue) continue;
                c.key = best;
                _found[kept++] = c;
            }
            _found.RemoveRange(kept, _found.Count - kept);
        }

        int cap = Mathf.Max(0, maxColliders);
        if (_found.Count > cap) _found.Sort(s_byKey);
        int use = Mathf.Min(_found.Count, cap);

        MCChunkManager m = MCChunkManager.Instance;
        int layer = 0;
        if (m != null && m.worldConfig != null) layer = m.worldConfig.terrainLayer;

        for (int i = 0; i < use; i++)
        {
            Slot slot = i < _pool.Count ? _pool[i] : CreateSlot();
            if (slot.go == null) { _pool.RemoveAt(i); i--; continue; } // destroyed from outside: drop it, this index is filled again next pass
            Place(slot, _found[i], layer);
        }
        for (int i = use; i < _pool.Count; i++)
            if (_pool[i].go != null && _pool[i].go.activeSelf) _pool[i].go.SetActive(false);
        _active = use;
    }

    Slot CreateSlot()
    {
        var go = new GameObject("FloraObstacle");
        go.transform.SetParent(transform, false);
        var col = go.AddComponent<CapsuleCollider>();
        col.direction = 1; // Y
        col.center = Vector3.zero;
        var slot = new Slot { go = go, collider = col };
        _pool.Add(slot);
        return slot;
    }

    // Moving a static collider rebuilds its broadphase entry, so a slot that
    // already sits on this trunk is left alone -- in steady state (anchors
    // standing in a grove) a refresh touches nothing.
    static void Place(Slot slot, Candidate c, int layer)
    {
        if (!slot.go.activeSelf) slot.go.SetActive(true);
        if (slot.layer != layer) { slot.go.layer = layer; slot.layer = layer; }
        if (slot.basePos == c.basePos && slot.up == c.up && slot.radius == c.radius && slot.height == c.height) return;

        slot.go.transform.SetPositionAndRotation(c.basePos + c.up * (c.height * 0.5f), Quaternion.FromToRotation(Vector3.up, c.up));
        slot.collider.radius = c.radius;
        // A capsule's height includes its caps; it cannot be shorter than its
        // diameter.
        slot.collider.height = Mathf.Max(c.height, c.radius * 2f);
        slot.basePos = c.basePos; slot.up = c.up; slot.radius = c.radius; slot.height = c.height;
    }

    public int ActiveColliders => _active;
}

// The per-plant arithmetic, pure so it can be tested without built plots.
public static class FloraObstacleMath
{
    // Instance matrix -> trunk capsule. PlantScatter builds instance matrices
    // with a uniform scale, so lossyScale.x is the plant's scale. Base is the
    // matrix translation (already sunk into the ground by the species' sink).
    public static bool TryMakeObstacle(Matrix4x4 trs, Vector3 up, float obstacleRadius, float obstacleHeight,
                                       out (Vector3 basePos, Vector3 up, float radius, float height) obstacle)
    {
        obstacle = default;
        if (obstacleRadius <= 0f || obstacleHeight <= 0f) return false;
        float scale = trs.lossyScale.x;
        obstacle = (new Vector3(trs.m03, trs.m13, trs.m23), up, obstacleRadius * scale, obstacleHeight * scale);
        return true;
    }

    // NavMeshBuildSource transform for a Capsule shape: unscaled, turned from
    // world Y onto `up`, centred halfway up the trunk. The capsule's
    // dimensions go in `size` (CapsuleSourceSize), which is the form Unity
    // itself emits when it collects a CapsuleCollider. Scaling the matrix
    // instead is ambiguous: if the builder makes the capsule from `size` first
    // and then applies a non-uniform scale, the caps stretch into a spheroid
    // that tapers to nothing at the trunk base. With this form local
    // y = -height/2 is the base and +height/2 the top.
    public static Matrix4x4 CapsuleSourceMatrix(Vector3 basePos, Vector3 up, float radius, float height)
    {
        return Matrix4x4.TRS(basePos + up * (height * 0.5f), Quaternion.FromToRotation(Vector3.up, up), Vector3.one);
    }

    // NavMeshBuildSource.size for a Capsule: x = z = diameter, y = height.
    public static Vector3 CapsuleSourceSize(float radius, float height) => new Vector3(radius * 2f, height, radius * 2f);

    // Distance from a point to the capsule's surface (0 inside): the axis runs
    // from basePos to basePos + up * height.
    public static float CapsuleDistance(Vector3 point, Vector3 basePos, Vector3 up, float radius, float height)
    {
        float t = Mathf.Clamp(Vector3.Dot(point - basePos, up), 0f, height);
        return Mathf.Max(0f, (point - (basePos + up * t)).magnitude - radius);
    }
}
