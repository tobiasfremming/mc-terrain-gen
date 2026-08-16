using UnityEngine;

// Drop this on a custom player controller to make the terrain's LOD system
// follow it, instead of the auto-spawned demo capsule from PlayerBootstrap.
//
// MCChunkManager only ever reads manager.target.position to decide which
// chunks to load/unload and at what LOD, so pointing it at any transform is
// enough to drive the system.
public class TerrainTarget : MonoBehaviour
{
    [Tooltip("Left empty, the first MCChunkManager found in the scene is used.")]
    public MCChunkManager manager;

    void Awake()
    {
        if (manager == null)
            manager = FindFirstObjectByType<MCChunkManager>();

        if (manager != null)
            manager.target = transform;
        else
            Debug.LogWarning("[TerrainTarget] No MCChunkManager found in the scene.", this);
    }
}
