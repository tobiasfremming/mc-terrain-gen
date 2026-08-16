using UnityEngine;

// Digs into the voxel terrain: press (or hold) the dig key to raycast from the
// aim transform and carve a physical hole where the ray hits the ground.
//
// Self-contained on purpose: attach it to anything with an aim direction — the
// player for now, a shovel item later. All it needs is MCChunkManager's
// Modifications API (StampSphere with affectsPhysics = true, so the collider
// deforms and you can actually walk down into the hole you dug).
public class TerrainDigger : MonoBehaviour
{
    [Header("Input")]
    public KeyCode digKey = KeyCode.R;
    [Tooltip("Seconds between digs while the key is held.")]
    public float repeatInterval = 0.2f;

    [Header("Aim")]
    [Tooltip("Ray origin/direction. Defaults to the main camera (crosshair digging).")]
    public Transform aimSource;
    public float maxDistance = 4.5f;

    [Header("Dig shape")]
    public float digRadius = 1.1f;
    [Tooltip("Physical digs deform the collider so you can descend into the hole or walk into the tunnel.")]
    public bool affectsPhysics = true;
    [Tooltip("How far past the surface the dig sphere is pushed, as a fraction of the radius. Makes each scoop bite into the ground instead of grazing it.")]
    [Range(0f, 1f)] public float bite = 0.35f;

    MCChunkManager _terrain;
    float _nextDigTime;

    void Start()
    {
        _terrain = FindFirstObjectByType<MCChunkManager>();
        if (!aimSource && Camera.main) aimSource = Camera.main.transform;
    }

    void Update()
    {
        if (_terrain == null || aimSource == null) return;

        bool wantDig = Input.GetKeyDown(digKey) ||
                       (Input.GetKey(digKey) && Time.time >= _nextDigTime);
        if (!wantDig) return;
        _nextDigTime = Time.time + repeatInterval;

        if (!Physics.Raycast(aimSource.position, aimSource.forward, out RaycastHit hit, maxDistance))
            return;
        if (hit.collider.GetComponentInParent<MarchingChunk>() == null)
            return; // only dig terrain

        // Push the sphere slightly into the ground so each scoop removes a
        // solid bite rather than half a sphere of air. CarveSphere is true 3D
        // material removal, so digging sideways bores tunnels with roofs.
        Vector3 center = hit.point + aimSource.forward * (digRadius * bite);
        _terrain.Modifications.CarveSphere(center, digRadius, affectsPhysics);
    }
}
