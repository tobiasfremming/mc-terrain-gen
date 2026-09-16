using System.Collections.Generic;
using UnityEngine;

// "Something that needs ground here." Put it on the player and on every NPC
// that should keep simulating physically/navigating while away from the
// player. Collision tiles are built within physicsRadius; NavMesh within
// navRadius. NPCs without an anchor only have ground inside the clipmap's
// LOD0 box around the player -- pair them with TerrainRigidbodyGuard.
public class TerrainAnchor : MonoBehaviour
{
    public float physicsRadius = 20f;
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
