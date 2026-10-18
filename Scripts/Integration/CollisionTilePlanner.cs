using System;
using System.Collections.Generic;
using UnityEngine;

// A sphere of "needs ground" around some anchor -- decoupled from
// TerrainAnchor so the planner can be driven by tests and by anything else
// (physicsRadius vs navRadius, both read through this same shape).
public struct AnchorSphere { public Vector3 position; public float radius; }

// Decides which 16 m collider-only tiles must be built to cover anchors that
// the render clipmap's LOD0 box does not already ground. Pure function of
// its inputs -- no MCChunkManager, no scene state -- so Task 8's streamer
// can call it off the main thread and tests can drive it without the Editor.
public static class CollisionTilePlanner
{
    public static void Plan(IReadOnlyList<AnchorSphere> anchors, float tileSize, Func<Vector3Int, bool> coveredByClipmap, HashSet<Vector3Int> result)
    {
        result.Clear();
        for (int a = 0; a < anchors.Count; a++)
        {
            Vector3 p = anchors[a].position;
            float r = anchors[a].radius;
            Vector3Int lo = Floor((p - Vector3.one * r) / tileSize);
            Vector3Int hi = Floor((p + Vector3.one * r) / tileSize);
            for (int z = lo.z; z <= hi.z; z++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var c = new Vector3Int(x, y, z);
                if (result.Contains(c)) continue;
                // Closest point on the tile's AABB to the anchor centre -- the
                // standard sphere/AABB test. Coarser than that would over-plan
                // tiles at the sphere's rounded corners; the brief's covered-face
                // vs far-corner test exists specifically to pin this down.
                Vector3 min = (Vector3)c * tileSize;
                Vector3 closest = Vector3.Max(min, Vector3.Min(p, min + Vector3.one * tileSize));
                if ((closest - p).sqrMagnitude > r * r) continue;
                if (coveredByClipmap(c)) continue;
                result.Add(c);
            }
        }
    }

    // Tile coord = floor(pos / tileSize) per axis. Mathf.FloorToInt (not a
    // cast) so negative coordinates floor toward -infinity instead of toward
    // zero -- e.g. -0.1 must land in tile -1, not tile 0.
    static Vector3Int Floor(Vector3 v) => new(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z));
}
