using UnityEngine;

// Single query point for "which way is up" anywhere in the world: world Y
// normally, radial from the planet center when the active terrain is in
// globe mode (WorldConfig.useGlobe). Any script that wants to respect
// planet gravity can call this instead of hardcoding Vector3.up/Vector3.down
// or needing its own MCChunkManager reference wired up -- in flat mode this
// is exactly Vector3.up everywhere, so code built against it needs zero
// changes to work in either mode.
//
// GravityAligner/SimplePlayerController don't use this -- they already have
// an explicit `terrain` reference (set once by PlayerBootstrap) and query it
// directly, which is equally valid. This exists for everything ELSE: props,
// AI, vehicles, anything that wants planet gravity without that wiring.
public static class PlanetGravity
{
    static MCChunkManager _manager;

    static MCChunkManager Manager()
    {
        if (_manager == null) _manager = Object.FindFirstObjectByType<MCChunkManager>();
        return _manager;
    }

    public static Vector3 UpAt(Vector3 worldPos)
    {
        var m = Manager();
        if (m != null && m.TryGetPlanetCenter(out Vector3 center, out _))
        {
            Vector3 rel = worldPos - center;
            if (rel.sqrMagnitude > 1e-6f) return rel.normalized;
        }
        return Vector3.up;
    }

    public static bool TryGetCenter(out Vector3 center, out float radius)
    {
        var m = Manager();
        if (m != null) return m.TryGetPlanetCenter(out center, out radius);
        center = default;
        radius = 0f;
        return false;
    }
}
