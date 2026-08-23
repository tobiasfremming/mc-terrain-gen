using System;
using UnityEngine;

// Wraps an existing DensityField so it renders as a sphere instead of a flat
// plane, WITHOUT modifying that field at all. The wrapped field keeps
// treating its input as an ordinary flat "local" position (height along Y,
// horizontal spread along X/Z) -- this class's only job is producing that
// local position for any given world point on/near the sphere.
//
// How: a triplanar blend, the SAME technique SandTerrain.shader already uses
// for texturing. For a world point, build THREE candidate flat local
// positions -- one per world axis, each treating that axis as if it were
// local up -- sample the wrapped field at all three with perfectly ordinary
// (x,z)-style coordinates, then blend by how closely the point's direction
// from the planet center aligns with each axis.
//
// This is the important bit: those blend weights vary CONTINUOUSLY, so
// Sample(p) stays a smooth function of p everywhere. A naive cube-face
// projection (pick ONE dominant axis, discretely) does not have that
// property -- two adjacent faces compute different local coordinates for the
// same physical point, so the wrapped field's noise evaluates to different
// values on either side of a face boundary: an actual crack in the mesh, not
// a cosmetic seam. A continuous blend never does that, which is what keeps
// Transvoxel's LOD seams watertight (see DensityField.Gradient's comment on
// why Sample must behave identically for every caller).
//
// Cost: up to 3x the wrapped field's Sample() per query, though near-zero
// weight axes are skipped -- most points (away from the roughly-cube-edge-
// shaped blend zones) only pay for one or two.
[CreateAssetMenu(fileName = "PlanetField", menuName = "Marching Cubes/Planet Field")]
public class PlanetField : DensityField
{
    [Tooltip("The existing field to wrap -- a BiomeWorld, a single Biome's terrain, anything. Left completely unmodified; it just receives a different position than the raw world position.")]
    public DensityField surface;

    [Tooltip("Sphere radius (m), measured from center to the wrapped field's local height = 0.")]
    public float radius = 1000f;
    public Vector3 center = Vector3.zero;

    [Tooltip("Extra clearance (m) above the highest possible terrain when computing a safe spawn/drop altitude (SafeSpawnRadius).")]
    public float spawnMargin = 40f;

    const float kWeightEpsilon = 0.0005f;

    // Floating-origin precision fix: MCChunkManager rebases the world
    // positions it feeds into density evaluation (job.densityOrigin) through
    // this same offset, so `center` must be rebased identically here -- fed
    // a rebased worldPos against a raw/absolute center would silently
    // compute the wrong `rel` (offset by _densityOriginOffset) as soon as
    // the offset becomes non-zero. Defaults to zero (= today's exact
    // behavior) until MCChunkManager calls RefreshDensityOriginOffset.
    // Deliberately NOT used by TryGetEmptySkip/SafeSpawnRadius below --
    // those receive/report absolute (job.origin-based) positions, not
    // densityOrigin-based ones, so they must keep using the raw `center`.
    Double3 _densityOriginOffset;
    Vector3 CenterRebased => (Vector3)((Double3)center - _densityOriginOffset);

    public void RefreshDensityOriginOffset(Double3 offset) => _densityOriginOffset = offset;

    // A radius comfortably above ANY possible terrain -- for spawning
    // something and letting it fall onto the surface, rather than trying to
    // land exactly on the ground (which risks spawning inside an overhang or
    // a mountain's flank, since the wrapped field isn't necessarily a pure
    // heightfield -- canyon/alien fields carve caves and overhangs). Falls
    // back to a generous multiple of radius if the wrapped field can't
    // report its own height bounds.
    public float SafeSpawnRadius()
    {
        if (surface != null && surface.TryGetHeightBounds(out _, out float maxH))
            return radius + maxH + spawnMargin;
        return radius * 1.5f + spawnMargin;
    }

    public override float Sample(Vector3 p)
    {
        if (surface == null) return radius - Vector3.Distance(p, CenterRebased);

        Vector3 rel = p - CenterRebased;
        float dist = rel.magnitude;
        if (dist < 1e-5f) return radius; // exact center: degenerate, arbitrary but harmless

        float localHeight = dist - radius;
        Vector3 w = BlendWeights(rel);

        if (surface is BiomeDensityField bdf)
        {
            int n = bdf.BiomeCount;
            Span<float> bw = stackalloc float[BiomeDensityField.MaxBiomes];
            // Reuse `rel` directly rather than reconstructing dir*radius from
            // scratch (rel.normalized * radius) -- that reconstruction threw
            // away rel's own precision (whatever consistency the floating-
            // origin fix gives it) and rebuilt a fresh full-magnitude vector
            // via an independent normalize+multiply, which is exactly what
            // caused biome selection to stay broken at large radius even
            // after CenterRebased/densityOrigin were fixed elsewhere. Using
            // rel means biome selection drifts by `localHeight` (tens of
            // meters) instead of being perfectly height-invariant -- negligible
            // against regionScale (thousands of meters).
            bdf.ComputeWeights3D(rel, bw, n);

            float d = 0f;
            if (w.x > kWeightEpsilon) d += w.x * bdf.SampleWithWeights(new Vector3(rel.z, localHeight, rel.y), bw, n);
            if (w.y > kWeightEpsilon) d += w.y * bdf.SampleWithWeights(new Vector3(rel.x, localHeight, rel.z), bw, n);
            if (w.z > kWeightEpsilon) d += w.z * bdf.SampleWithWeights(new Vector3(rel.x, localHeight, rel.y), bw, n);
            return d;
        }

        float d2 = 0f;
        if (w.x > kWeightEpsilon) d2 += w.x * surface.Sample(new Vector3(rel.z, localHeight, rel.y));
        if (w.y > kWeightEpsilon) d2 += w.y * surface.Sample(new Vector3(rel.x, localHeight, rel.z));
        if (w.z > kWeightEpsilon) d2 += w.z * surface.Sample(new Vector3(rel.x, localHeight, rel.y));
        return d2;
    }

    public override bool HasVertexColors => surface != null && surface.HasVertexColors;

    // Biome color/hardness are pure functions of the (now sphere-coherent)
    // biome weight vector, with no separate per-face position term -- unlike
    // Sample, which still needs each biome's own per-face SHAPE -- so these
    // don't need the triplanar face blend at all once weights come from
    // ComputeWeights3D: all 3 faces would agree exactly, since they'd share
    // the same weights.
    public override Color GetVertexColor(Vector3 p)
    {
        if (surface == null) return base.GetVertexColor(p);
        Vector3 rel = p - CenterRebased;

        if (surface is BiomeDensityField bdf)
        {
            int n = bdf.BiomeCount;
            Span<float> bw = stackalloc float[BiomeDensityField.MaxBiomes];
            bdf.ComputeWeights3D(rel, bw, n); // see Sample()'s comment on why rel, not rel.normalized*radius
            return bdf.GetVertexColorWithWeights(bw, n);
        }

        float localHeight = rel.magnitude - radius;
        Vector3 w = BlendWeights(rel);
        Color c = Color.black;
        if (w.x > kWeightEpsilon) c += w.x * surface.GetVertexColor(new Vector3(rel.z, localHeight, rel.y));
        if (w.y > kWeightEpsilon) c += w.y * surface.GetVertexColor(new Vector3(rel.x, localHeight, rel.z));
        if (w.z > kWeightEpsilon) c += w.z * surface.GetVertexColor(new Vector3(rel.x, localHeight, rel.y));
        c.a = 1f;
        return c;
    }

    public override float SurfaceHardness(Vector3 p)
    {
        if (surface == null) return base.SurfaceHardness(p);
        Vector3 rel = p - CenterRebased;

        if (surface is BiomeDensityField bdf)
        {
            int n = bdf.BiomeCount;
            Span<float> bw = stackalloc float[BiomeDensityField.MaxBiomes];
            bdf.ComputeWeights3D(rel, bw, n); // see Sample()'s comment on why rel, not rel.normalized*radius
            return bdf.SurfaceHardnessWithWeights(bw, n);
        }

        float localHeight = rel.magnitude - radius;
        Vector3 w = BlendWeights(rel);
        float h = 0f;
        if (w.x > kWeightEpsilon) h += w.x * surface.SurfaceHardness(new Vector3(rel.z, localHeight, rel.y));
        if (w.y > kWeightEpsilon) h += w.y * surface.SurfaceHardness(new Vector3(rel.x, localHeight, rel.z));
        if (w.z > kWeightEpsilon) h += w.z * surface.SurfaceHardness(new Vector3(rel.x, localHeight, rel.y));
        return h;
    }

    // Same convention as the shader's triplanar blend (SandTerrain.shader):
    // weight each axis by how aligned the point's radial direction is with it.
    static Vector3 BlendWeights(Vector3 rel)
    {
        Vector3 w = new Vector3(Mathf.Abs(rel.x), Mathf.Abs(rel.y), Mathf.Abs(rel.z));
        float sum = w.x + w.y + w.z;
        return sum > 1e-6f ? w / sum : new Vector3(1f, 0f, 0f);
    }

    // The flat-world "min/max world Y" convention doesn't map onto a sphere
    // (there's no single fixed "up"); TryGetEmptySkip below is the real
    // skip test for planets. Left unimplemented so nothing accidentally
    // relies on it as a flat Y range.
    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        minH = maxH = 0f;
        return false;
    }

    // Radial equivalent of the flat-world empty-chunk skip: solid material
    // can only exist within [radius+minH, radius+maxH] of center (minH/maxH
    // being the wrapped field's own height bounds, reused as "how far the
    // surface can sit above/below the nominal sphere"). A chunk whose AABB
    // distance-from-center range doesn't overlap that shell is provably all
    // air (too far) or all solid (too deep) -- skip it without sampling.
    // Without this, EVERY chunk in the clipmap box gets fully marched,
    // including chunks buried deep in the planet's interior or stranded far
    // out in empty space -- the single biggest cost of planet mode.
    public override bool TryGetEmptySkip(Vector3 boxMin, Vector3 boxMax)
    {
        if (surface == null || !surface.TryGetHeightBounds(out float minH, out float maxH)) return false;

        float rLo = Mathf.Max(0f, radius + minH);
        float rHi = radius + maxH;

        float closestSq = ClosestDistanceSq(center, boxMin, boxMax);
        float farthestSq = FarthestDistanceSq(center, boxMin, boxMax);

        if (farthestSq < rLo * rLo) return true; // entire box closer to center than the shell -> all solid
        if (closestSq > rHi * rHi) return true;  // entire box farther from center than the shell -> all air
        return false;
    }

    static float ClosestDistanceSq(Vector3 p, Vector3 boxMin, Vector3 boxMax)
    {
        float dx = Mathf.Max(Mathf.Max(boxMin.x - p.x, 0f), p.x - boxMax.x);
        float dy = Mathf.Max(Mathf.Max(boxMin.y - p.y, 0f), p.y - boxMax.y);
        float dz = Mathf.Max(Mathf.Max(boxMin.z - p.z, 0f), p.z - boxMax.z);
        return dx * dx + dy * dy + dz * dz;
    }

    static float FarthestDistanceSq(Vector3 p, Vector3 boxMin, Vector3 boxMax)
    {
        float dx = Mathf.Max(Mathf.Abs(p.x - boxMin.x), Mathf.Abs(p.x - boxMax.x));
        float dy = Mathf.Max(Mathf.Abs(p.y - boxMin.y), Mathf.Abs(p.y - boxMax.y));
        float dz = Mathf.Max(Mathf.Abs(p.z - boxMin.z), Mathf.Abs(p.z - boxMax.z));
        return dx * dx + dy * dy + dz * dz;
    }

    // GPU acceleration: only supports wrapping a BiomeDensityField directly
    // (the shape SandTerrain's whole GPU dispatch hardcodes -- see the plan's
    // "hardcode the 3-level shape" decision). Any other `surface` type falls
    // back to pure CPU sampling for the whole world.
    public bool TryBuildGpuParams(out PlanetGpuParams planetParams, out BiomeBlendGpuParams blend,
                                   out LeafGpuParams[] leaves, out GpuFieldType[] fieldTypes, out float[] biases)
    {
        planetParams = new PlanetGpuParams { isPlanet = 1f, center = CenterRebased, radius = radius };
        if (surface is BiomeDensityField biomeWorld && biomeWorld.TryBuildGpuLeaves(out blend, out leaves, out fieldTypes, out biases))
            return true;

        blend = default;
        leaves = null;
        fieldTypes = null;
        biases = null;
        return false;
    }
}
