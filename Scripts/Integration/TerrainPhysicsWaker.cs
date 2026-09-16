using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// PhysX does not wake sleeping bodies when a static MeshCollider's mesh is
// swapped, so a crate resting on ground that was just dug away floats. Wake
// everything whose bounds touch a region whose collision changed.
public static class TerrainPhysicsWaker
{
    static readonly Collider[] s_hits = new Collider[256];
    static readonly HashSet<Rigidbody> s_woken = new();

    // Which TerrainModificationSystem OnEdit is currently subscribed to. A
    // scene reload replaces MCChunkManager.Instance (and its Modifications)
    // with a new object; without tracking this, RegionModified subscriptions
    // from Install()'s one-shot RuntimeInitializeOnLoadMethod call go stale
    // and edit-triggered wakes silently stop after the first scene load.
    static TerrainModificationSystem s_bound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        MarchingChunk.ColliderChanged -= OnChunk;
        MarchingChunk.ColliderChanged += OnChunk;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        EnsureBound();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureBound();

    // Re-resolves MCChunkManager and, if its Modifications differs from what
    // OnEdit is currently subscribed to, moves the subscription over. Cheap
    // (a couple of reference compares) once bound, so it is safe to call from
    // every chunk collider change as well as from scene loads.
    static void EnsureBound()
    {
        // Not `MCChunkManager.Instance ?? FindFirstObjectByType<...>()`: `??`
        // uses the plain reference-equality null check, which bypasses
        // Unity's overloaded `== null` (true for a destroyed-but-not-yet-GC'd
        // object). An explicit check keeps the fallback reachable in that case.
        var m = MCChunkManager.Instance;
        if (m == null) m = Object.FindFirstObjectByType<MCChunkManager>();
        if (m == null) return;

        var mods = m.Modifications;
        if (mods == s_bound) return;

        if (s_bound != null) s_bound.RegionModified -= OnEdit;
        if (mods != null) mods.RegionModified += OnEdit;
        s_bound = mods;
    }

    static void OnChunk(MarchingChunk c)
    {
        EnsureBound(); // cheap reference compare; catches a manager swap between scene-load events
        Wake(c.WorldBounds);
    }

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
