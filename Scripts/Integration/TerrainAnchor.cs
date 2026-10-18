using System.Collections.Generic;
using UnityEngine;

// "Something that needs ground here." Put it on the player and on every NPC
// that should keep simulating physically/navigating while away from the
// player. Collision tiles are built within max(physicsRadius, navRadius) --
// NavMesh is baked from colliders, so NavMesh out to navRadius needs tiles out
// to navRadius too -- and navRadius puts the anchor into the NavMesh patch
// plan. Trunk colliders (FloraObstacles) use the same max. On the clipmap
// target (the player) physicsRadius 0 is fine: its ground and its NavMesh
// sources come from the LOD0 box, so its tiles use physicsRadius only, and
// navRadius just sizes its NavMesh patch. NPCs without an anchor only have
// ground inside the clipmap's LOD0 box around the player -- pair them with
// TerrainRigidbodyGuard.
public class TerrainAnchor : MonoBehaviour
{
    [Tooltip("Collision tiles are built out to max(physicsRadius, navRadius) around this anchor, wherever the clipmap's LOD0 box does not already cover. On the clipmap target (player) tiles use physicsRadius alone, and 0 is fine: the clipmap grounds it.")]
    public float physicsRadius = 20f;
    [Tooltip("Includes this anchor in the NavMesh patch plan with this radius, and (because NavMesh is baked from colliders) also extends its collision tiles and trunk colliders to this radius. On the clipmap target it only sizes the NavMesh patch (no tiles); <= 0 falls back to 40.")]
    public float navRadius = 40f;

    static readonly List<TerrainAnchor> s_all = new();
    public static IReadOnlyList<TerrainAnchor> All => s_all;

    public Vector3 Position => transform.position;

    // Guard against a double-add: Unity can call OnEnable again after a
    // domain reload without an intervening OnDisable (e.g. reload while the
    // object was already enabled), so a bare Add would duplicate the entry.
    void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
    void OnDisable() => s_all.Remove(this);
}
