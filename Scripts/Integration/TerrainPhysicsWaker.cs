using System.Collections.Generic;
using UnityEngine;

// PhysX does not wake sleeping bodies when a static MeshCollider's mesh is
// swapped, so a crate resting on ground that was just dug away floats. Wake
// everything whose bounds touch a region whose collision changed.
public static class TerrainPhysicsWaker
{
    static readonly Collider[] s_hits = new Collider[256];
    static readonly HashSet<Rigidbody> s_woken = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        MarchingChunk.ColliderChanged -= OnChunk;
        MarchingChunk.ColliderChanged += OnChunk;

        // Not `MCChunkManager.Instance ?? FindFirstObjectByType<...>()`: `??`
        // uses the plain reference-equality null check, which bypasses
        // Unity's overloaded `== null` (true for a destroyed-but-not-yet-GC'd
        // object). An explicit check keeps the fallback reachable in that case.
        var m = MCChunkManager.Instance;
        if (m == null) m = Object.FindFirstObjectByType<MCChunkManager>();
        if (m != null)
        {
            m.Modifications.RegionModified -= OnEdit;
            m.Modifications.RegionModified += OnEdit;
        }
    }

    static void OnChunk(MarchingChunk c) => Wake(c.WorldBounds);
    static void OnEdit(Bounds b, bool affectsPhysics) { if (affectsPhysics) Wake(b); }

    public static void Wake(Bounds b)
    {
        b.Expand(1f);
        int n = Physics.OverlapBoxNonAlloc(b.center, b.extents, s_hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        s_woken.Clear();
        for (int i = 0; i < n; i++)
        {
            var rb = s_hits[i].attachedRigidbody;
            if (rb != null && !rb.isKinematic && rb.IsSleeping() && s_woken.Add(rb)) rb.WakeUp();
        }
    }
}
