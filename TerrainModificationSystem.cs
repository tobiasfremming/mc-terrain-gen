using System;
using UnityEngine;

// Facade for all runtime terrain edits, decoupled from the chunk manager and
// from whoever triggers the edits (player footprints, digging tools,
// explosions...). Edits are voxel-delta stamps in one of two layers:
//
//   VISUAL  (affectsPhysics = false): rendered but ignored by collision.
//           Footprints live here, so walking never re-cooks colliders and the
//           character stands on the smooth base terrain.
//   PHYSICAL (affectsPhysics = true): rendered AND baked into collision.
//           Use for gameplay digging, craters, etc.
//
// Each layer is a TerrainModificationCache with the short-term -> persistent
// two-tier storage. Listeners (the chunk manager) subscribe to RegionModified
// to remesh affected chunks.
public class TerrainModificationSystem
{
    public readonly TerrainModificationCache visual;
    public readonly TerrainModificationCache physical;

    // Maximum accumulated lowering per voxel, meters (stamps only; carving is
    // self-limiting and ignores this).
    public float maxDepth = 2.35f;

    // Base terrain field, needed by CarveSphere to know how much density to
    // remove. Set by the chunk manager.
    public DensityField baseField;

    // (world bounds of the edit, affectsPhysics)
    public event Action<Bounds, bool> RegionModified;

    public TerrainModificationSystem(float voxelSize, float shortTermSeconds)
    {
        visual = new TerrainModificationCache(voxelSize) { shortTermSeconds = shortTermSeconds };
        physical = new TerrainModificationCache(voxelSize) { shortTermSeconds = shortTermSeconds };
    }

    TerrainModificationCache Layer(bool affectsPhysics) => affectsPhysics ? physical : visual;

    // depth > 0 lowers the terrain, depth < 0 raises it.
    public void StampSphere(Vector3 center, float radius, float depth, bool affectsPhysics = false)
    {
        Layer(affectsPhysics).Stamp(center, radius, depth, maxDepth, Time.time);
        Notify(center, center, radius, affectsPhysics);
    }

    // Carve a smooth capsule-shaped groove from a to b (continuous trails).
    public void StampLine(Vector3 a, Vector3 b, float radius, float depth, bool affectsPhysics = false)
    {
        Layer(affectsPhysics).StampSegment(a, b, radius, depth, maxDepth, Time.time);
        Notify(a, b, radius, affectsPhysics);
    }

    // Remove a full sphere of material (true 3D carving: digging, tunnels,
    // caves). One call excavates the whole sphere regardless of rock depth.
    public void CarveSphere(Vector3 center, float radius, bool affectsPhysics = true)
    {
        if (baseField == null)
        {
            Debug.LogWarning("[TerrainModificationSystem] CarveSphere needs baseField set.");
            return;
        }
        var layer = Layer(affectsPhysics);
        layer.CarveSphere(center, radius, baseField, Time.time);
        Notify(center, center, radius + layer.voxelSize * 2f, affectsPhysics);
    }

    void Notify(Vector3 a, Vector3 b, float radius, bool affectsPhysics)
    {
        var bounds = new Bounds((a + b) * 0.5f, Vector3.zero);
        bounds.Encapsulate(a);
        bounds.Encapsulate(b);
        bounds.Expand(radius * 2f);
        RegionModified?.Invoke(bounds, affectsPhysics);
    }

    public void SetShortTermSeconds(float seconds)
    {
        visual.shortTermSeconds = seconds;
        physical.shortTermSeconds = seconds;
    }

    // Consolidate idle short-term entries into the persistent tier.
    public void Flush(float now)
    {
        visual.Flush(now);
        physical.Flush(now);
    }
}
