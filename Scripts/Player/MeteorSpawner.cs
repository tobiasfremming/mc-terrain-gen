using UnityEngine;

// Debug/test utility: every `cooldownSeconds`, rains a burst of `count`
// meteor prefabs down onto the target from `spawnHeight` directly "above"
// it -- radially outward from the planet center in globe mode, straight up
// in flat mode (see PlanetGravity) -- so PlanetGravityBody's behavior (fall
// toward the surface, land upright) can be watched directly.
//
// Not auto-wired like PlayerBootstrap: add this component to any GameObject
// in the scene and drag your Meteor prefab into meteorPrefab.
public class MeteorSpawner : MonoBehaviour
{
    [Tooltip("Any prefab with (or without -- Rigidbody/PlanetGravityBody are added automatically if missing) physics components.")]
    public GameObject meteorPrefab;
    [Tooltip("Left empty, the first MCChunkManager's target (usually the player) is used.")]
    public Transform target;

    [Header("Burst")]
    public int count = 100;
    public float spawnHeight = 100f;
    [Tooltip("Meteors are scattered across a disc of this radius (perpendicular to 'up') so 100 of them don't all land in exactly the same spot.")]
    public float spreadRadius = 30f;
    public float cooldownSeconds = 20f;
    [Tooltip("Spawn one burst immediately on Start instead of waiting out the first cooldown.")]
    public bool spawnOnStart = true;

    float _nextSpawnTime;

    void Start()
    {
        if (!target)
        {
            var mgr = FindFirstObjectByType<MCChunkManager>();
            if (mgr != null) target = mgr.target;
        }
        _nextSpawnTime = Time.time + (spawnOnStart ? 0f : cooldownSeconds);
    }

    void Update()
    {
        if (!meteorPrefab || !target) return;
        if (Time.time < _nextSpawnTime) return;
        _nextSpawnTime = Time.time + cooldownSeconds;
        SpawnBurst();
    }

    void SpawnBurst()
    {
        Vector3 up = PlanetGravity.UpAt(target.position);

        // Any vector not parallel to `up` to build a tangent basis for the
        // spread disc -- same technique as GravityAligner's degenerate-case
        // fallback.
        Vector3 seed = Mathf.Abs(Vector3.Dot(up, Vector3.forward)) < 0.99f ? Vector3.forward : Vector3.right;
        Vector3 tangentA = Vector3.Cross(up, seed).normalized;
        Vector3 tangentB = Vector3.Cross(up, tangentA);

        for (int i = 0; i < count; i++)
        {
            Vector2 offset = Random.insideUnitCircle * spreadRadius;
            Vector3 pos = target.position + up * spawnHeight + tangentA * offset.x + tangentB * offset.y;
            Quaternion rot = Quaternion.LookRotation(Vector3.Slerp(tangentA, tangentB, Random.value), up);

            var meteor = Instantiate(meteorPrefab, pos, rot);

            // A non-convex MeshCollider (the default for an imported rock
            // model) can never collide with the terrain's own MeshCollider --
            // it's also non-convex (a marching-cubes surface can't be convex),
            // and PhysX only lets non-convex mesh colliders hit primitives or
            // OTHER convex mesh colliders. Without this the meteor falls
            // straight through the ground with no collision at all.
            foreach (var meshCol in meteor.GetComponentsInChildren<MeshCollider>())
                if (!meshCol.convex) meshCol.convex = true;

            if (!meteor.TryGetComponent<Rigidbody>(out var rb))
                rb = meteor.AddComponent<Rigidbody>();
            // Falling fast against a thin static mesh shell -- guard against
            // tunneling through it in one physics step.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (!meteor.TryGetComponent<PlanetGravityBody>(out var body))
                body = meteor.AddComponent<PlanetGravityBody>();
            body.ResetOrientation(); // matches the rotation just set above, not whatever Awake would otherwise capture
            if (!meteor.TryGetComponent<MeteorImpact>(out _))
                meteor.AddComponent<MeteorImpact>();
        }
    }
}
