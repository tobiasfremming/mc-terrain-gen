using System.Collections.Generic;
using UnityEngine;

// Sparse voxel-delta field for runtime terrain edits (footprints, digging...).
// The delta is stored per voxel (base-resolution grid corner) and sampled with
// trilinear interpolation, so every LOD level sees the same continuous field.
//
// Two tiers, as designed:
//  - SHORT-TERM: voxels touched recently. Hot, written every stamp; repeated
//    edits to the same spot (walking back and forth) just update one entry.
//  - PERSISTENT: voxels not touched for `shortTermSeconds` are consolidated
//    here and last for the rest of the run. Read-mostly.
// The sampled value is always shortTerm + persistent, so flushing never changes
// the terrain — it's purely a storage transition.
//
// THREAD SAFETY: chunk meshing runs on worker threads and reads this cache
// while the main thread stamps/flushes. All access goes through _sync; the
// empty check uses a lock-free fast path so untouched terrain pays nothing.
public class TerrainModificationCache
{
    public readonly float voxelSize;
    public float shortTermSeconds = 20f;

    struct ShortEntry { public float delta; public float lastTouch; }

    readonly Dictionary<Vector3Int, ShortEntry> _shortTerm = new();
    readonly Dictionary<Vector3Int, float> _persistent = new();

    // Occupancy at coarse "region" granularity (16^3 voxels) for cheap
    // "is there anything near p?" rejection.
    readonly HashSet<Vector3Int> _regions = new();
    readonly List<Vector3Int> _regionScratch = new();
    readonly List<Vector3Int> _flushScratch = new();
    readonly HashSet<int> _appliedScratch = new();
    const int RegionShift = 4; // 16 voxels per region axis

    readonly object _sync = new object();
    volatile int _regionCountFast;       // lock-free emptiness check
    float _minDelta, _maxDelta;          // conservative bounds of all deltas

    public TerrainModificationCache(float voxelSize)
    {
        this.voxelSize = Mathf.Max(1e-4f, voxelSize);
    }

    public bool IsEmpty => _regionCountFast == 0;
    public int ShortTermCount { get { lock (_sync) return _shortTerm.Count; } }
    public int PersistentCount { get { lock (_sync) return _persistent.Count; } }

    // Conservative delta range over the whole field (0 when empty). Used to
    // widen height bounds for the empty-chunk skip.
    public float MinDelta { get { lock (_sync) return _minDelta; } }
    public float MaxDelta { get { lock (_sync) return _maxDelta; } }

    static Vector3Int RegionOf(Vector3Int v) =>
        new(v.x >> RegionShift, v.y >> RegionShift, v.z >> RegionShift);

    float VoxelDelta(Vector3Int v)
    {
        float d = 0f;
        if (_shortTerm.TryGetValue(v, out var e)) d += e.delta;
        if (_persistent.TryGetValue(v, out var p)) d += p;
        return d;
    }

    // Lower (depth > 0, raise if < 0) the terrain in a smooth spherical dent.
    // The total accumulated lowering per voxel is clamped to maxAccumulated.
    public void Stamp(Vector3 center, float radius, float depth, float maxAccumulated, float now)
    {
        StampSegment(center, center, radius, depth, maxAccumulated, now);
    }

    // Same as Stamp but along the capsule from a to b: used to carve smooth,
    // continuous trails instead of chains of separate dents.
    public void StampSegment(Vector3 a, Vector3 b, float radius, float depth, float maxAccumulated, float now)
    {
        Vector3 lo = Vector3.Min(a, b), hi = Vector3.Max(a, b);
        int x0 = Mathf.FloorToInt((lo.x - radius) / voxelSize), x1 = Mathf.CeilToInt((hi.x + radius) / voxelSize);
        int y0 = Mathf.FloorToInt((lo.y - radius) / voxelSize), y1 = Mathf.CeilToInt((hi.y + radius) / voxelSize);
        int z0 = Mathf.FloorToInt((lo.z - radius) / voxelSize), z1 = Mathf.CeilToInt((hi.z + radius) / voxelSize);

        Vector3 ab = b - a;
        float abLenSq = ab.sqrMagnitude;

        lock (_sync)
        {
            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var v = new Vector3Int(x, y, z);
                        Vector3 p = (Vector3)v * voxelSize;

                        float t = abLenSq > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / abLenSq) : 0f;
                        float dist = Vector3.Distance(p, a + ab * t);
                        if (dist >= radius) continue;

                        float f = 1f - dist / radius;
                        f = f * f * (3f - 2f * f); // smoothstep falloff

                        _shortTerm.TryGetValue(v, out var e);
                        float persist = _persistent.TryGetValue(v, out var pv) ? pv : 0f;

                        // deltas are negative for lowered terrain (positive density = solid)
                        float total = Mathf.Max(e.delta + persist - depth * f, -maxAccumulated);
                        e.delta = total - persist;
                        e.lastTouch = now;
                        _shortTerm[v] = e;
                        _regions.Add(RegionOf(v));

                        if (total < _minDelta) _minDelta = total;
                        if (total > _maxDelta) _maxDelta = total;
                    }
            _regionCountFast = _regions.Count;
        }
    }

    // True 3D material removal: forces the total density (base + deltas) at
    // every voxel in the sphere down to the sphere's signed distance, so ONE
    // scoop carves a clean cavity regardless of how deep the rock is — this is
    // what makes tunnels and caves possible. Never adds material (only ever
    // lowers density), and is self-limiting (bounded by the sphere SDF), so it
    // is not subject to the accumulated-depth clamp.
    public void CarveSphere(Vector3 center, float radius, DensityField baseField, float now)
    {
        float pad = voxelSize * 1.5f; // small skirt so trilinear blending stays smooth
        int x0 = Mathf.FloorToInt((center.x - radius - pad) / voxelSize), x1 = Mathf.CeilToInt((center.x + radius + pad) / voxelSize);
        int y0 = Mathf.FloorToInt((center.y - radius - pad) / voxelSize), y1 = Mathf.CeilToInt((center.y + radius + pad) / voxelSize);
        int z0 = Mathf.FloorToInt((center.z - radius - pad) / voxelSize), z1 = Mathf.CeilToInt((center.z + radius + pad) / voxelSize);

        lock (_sync)
        {
            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var v = new Vector3Int(x, y, z);
                        Vector3 p = (Vector3)v * voxelSize;
                        float dist = Vector3.Distance(p, center);
                        if (dist >= radius + pad) continue;

                        // Desired TOTAL delta so that base + delta == sphere SDF
                        // (negative inside the sphere -> air).
                        float target = (dist - radius) - baseField.Sample(p);

                        _shortTerm.TryGetValue(v, out var e);
                        float persist = _persistent.TryGetValue(v, out var pv) ? pv : 0f;
                        float total = e.delta + persist;
                        if (target >= total) continue; // never add material back

                        e.delta = target - persist;
                        e.lastTouch = now;
                        _shortTerm[v] = e;
                        _regions.Add(RegionOf(v));
                        if (target < _minDelta) _minDelta = target;
                    }
            _regionCountFast = _regions.Count;
        }
    }

    // Consolidate short-term entries that haven't been touched recently into
    // the persistent store. Call every few seconds.
    public void Flush(float now)
    {
        lock (_sync)
        {
            if (_shortTerm.Count == 0) return;

            _flushScratch.Clear();
            foreach (var kv in _shortTerm)
                if (now - kv.Value.lastTouch > shortTermSeconds)
                    _flushScratch.Add(kv.Key);
            if (_flushScratch.Count == 0) return;

            foreach (var v in _flushScratch)
            {
                float d = _shortTerm[v].delta;
                _shortTerm.Remove(v);
                if (d == 0f) continue;
                _persistent.TryGetValue(v, out float p);
                float np = p + d;
                if (np != 0f) _persistent[v] = np;
                else _persistent.Remove(v);
            }

            // _regions only ever grew via Stamp/CarveSphere; a region's last
            // remaining voxel can disappear right here (delta nets back to
            // exactly 0) with nothing else to remove it from _regions. Left
            // unchecked, OverlapsBounds/SampleDelta/ApplyToGrid's region-
            // rejection loops keep scanning zones with nothing left in them
            // for the rest of the session. Flush() runs on a timer, not per
            // frame, so an O(occupied voxels) rebuild here is cheap relative
            // to how rarely this actually runs.
            _regions.Clear();
            foreach (var v in _shortTerm.Keys) _regions.Add(RegionOf(v));
            foreach (var v in _persistent.Keys) _regions.Add(RegionOf(v));
            _regionCountFast = _regions.Count;
        }
    }

    // Does any edit's influence region overlap the world AABB? (Used to decide
    // whether a collider can just share the render mesh.)
    public bool OverlapsBounds(Vector3 min, Vector3 max)
    {
        if (_regionCountFast == 0) return false;
        float regionWorld = voxelSize * (1 << RegionShift);
        lock (_sync)
        {
            foreach (var rc in _regions)
            {
                Vector3 rMin = (Vector3)rc * regionWorld;
                Vector3 rMax = rMin + Vector3.one * regionWorld;
                if (rMax.x + voxelSize < min.x || rMin.x - voxelSize > max.x ||
                    rMax.y + voxelSize < min.y || rMin.y - voxelSize > max.y ||
                    rMax.z + voxelSize < min.z || rMin.z - voxelSize > max.z)
                    continue;
                return true;
            }
        }
        return false;
    }

    // Trilinear delta at an arbitrary world position, with cheap rejection when
    // nothing has been modified nearby.
    public float SampleDelta(Vector3 p)
    {
        if (_regionCountFast == 0) return 0f;

        float fx = p.x / voxelSize, fy = p.y / voxelSize, fz = p.z / voxelSize;
        int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy), z0 = Mathf.FloorToInt(fz);

        lock (_sync)
        {
            // region rejection: the 8 corners span at most 2 region coords per axis
            var rA = RegionOf(new Vector3Int(x0, y0, z0));
            var rB = RegionOf(new Vector3Int(x0 + 1, y0 + 1, z0 + 1));
            bool near = false;
            for (int i = 0; i < 8 && !near; i++)
            {
                var rc = new Vector3Int(
                    (i & 1) == 0 ? rA.x : rB.x,
                    (i & 2) == 0 ? rA.y : rB.y,
                    (i & 4) == 0 ? rA.z : rB.z);
                near = _regions.Contains(rc);
            }
            if (!near) return 0f;

            return TrilinearUnchecked(fx, fy, fz, x0, y0, z0);
        }
    }

    // Caller must hold _sync.
    float TrilinearUnchecked(float fx, float fy, float fz, int x0, int y0, int z0)
    {
        float tx = fx - x0, ty = fy - y0, tz = fz - z0;
        float d000 = VoxelDelta(new Vector3Int(x0, y0, z0));
        float d100 = VoxelDelta(new Vector3Int(x0 + 1, y0, z0));
        float d010 = VoxelDelta(new Vector3Int(x0, y0 + 1, z0));
        float d110 = VoxelDelta(new Vector3Int(x0 + 1, y0 + 1, z0));
        float d001 = VoxelDelta(new Vector3Int(x0, y0, z0 + 1));
        float d101 = VoxelDelta(new Vector3Int(x0 + 1, y0, z0 + 1));
        float d011 = VoxelDelta(new Vector3Int(x0, y0 + 1, z0 + 1));
        float d111 = VoxelDelta(new Vector3Int(x0 + 1, y0 + 1, z0 + 1));
        float c00 = Mathf.Lerp(d000, d100, tx);
        float c10 = Mathf.Lerp(d010, d110, tx);
        float c01 = Mathf.Lerp(d001, d101, tx);
        float c11 = Mathf.Lerp(d011, d111, tx);
        return Mathf.Lerp(Mathf.Lerp(c00, c10, ty), Mathf.Lerp(c01, c11, ty), tz);
    }

    // Finite-difference gradient of the delta field (matches how the base
    // field's gradient is combined for vertex normals).
    public Vector3 GradientDelta(Vector3 p, float eps)
    {
        if (_regionCountFast == 0) return Vector3.zero;
        float dx = SampleDelta(p + new Vector3(eps, 0, 0)) - SampleDelta(p - new Vector3(eps, 0, 0));
        float dy = SampleDelta(p + new Vector3(0, eps, 0)) - SampleDelta(p - new Vector3(0, eps, 0));
        float dz = SampleDelta(p + new Vector3(0, 0, eps)) - SampleDelta(p - new Vector3(0, 0, eps));
        return new Vector3(dx, dy, dz) / (2f * eps);
    }

    // Add the delta field into a pre-filled sample grid (fast path for chunk
    // meshing). Iterates only the occupied regions, so unmodified chunks pay a
    // single overlap test and touched chunks only pay for samples near edits.
    public void ApplyToGrid(Vector3 origin, int countX, int countY, int countZ, float step, float[] dest)
    {
        if (_regionCountFast == 0) return;

        Vector3 gridMin = origin;
        Vector3 gridMax = origin + new Vector3(countX - 1, countY - 1, countZ - 1) * step;
        float regionWorld = voxelSize * (1 << RegionShift);

        lock (_sync)
        {
            _regionScratch.Clear();
            foreach (var rc in _regions)
            {
                Vector3 rMin = (Vector3)rc * regionWorld;
                Vector3 rMax = rMin + Vector3.one * regionWorld;
                // expand by one voxel for trilinear support
                if (rMax.x + voxelSize < gridMin.x || rMin.x - voxelSize > gridMax.x ||
                    rMax.y + voxelSize < gridMin.y || rMin.y - voxelSize > gridMax.y ||
                    rMax.z + voxelSize < gridMin.z || rMin.z - voxelSize > gridMax.z)
                    continue;
                _regionScratch.Add(rc);
            }
            if (_regionScratch.Count == 0) return;

            _appliedScratch.Clear();
            foreach (var rc in _regionScratch)
            {
                Vector3 rMin = (Vector3)rc * regionWorld - Vector3.one * voxelSize;
                Vector3 rMax = (Vector3)rc * regionWorld + Vector3.one * (regionWorld + voxelSize);

                int ix0 = Mathf.Max(0, Mathf.CeilToInt((rMin.x - origin.x) / step));
                int iy0 = Mathf.Max(0, Mathf.CeilToInt((rMin.y - origin.y) / step));
                int iz0 = Mathf.Max(0, Mathf.CeilToInt((rMin.z - origin.z) / step));
                int ix1 = Mathf.Min(countX - 1, Mathf.FloorToInt((rMax.x - origin.x) / step));
                int iy1 = Mathf.Min(countY - 1, Mathf.FloorToInt((rMax.y - origin.y) / step));
                int iz1 = Mathf.Min(countZ - 1, Mathf.FloorToInt((rMax.z - origin.z) / step));

                for (int z = iz0; z <= iz1; z++)
                    for (int y = iy0; y <= iy1; y++)
                        for (int x = ix0; x <= ix1; x++)
                        {
                            int idx = (z * countY + y) * countX + x;
                            if (!_appliedScratch.Add(idx)) continue; // regions can overlap after expansion

                            Vector3 p = origin + new Vector3(x, y, z) * step;
                            float fx = p.x / voxelSize, fy = p.y / voxelSize, fz = p.z / voxelSize;
                            dest[idx] += TrilinearUnchecked(fx, fy, fz,
                                Mathf.FloorToInt(fx), Mathf.FloorToInt(fy), Mathf.FloorToInt(fz));
                        }
            }
        }
    }
}
