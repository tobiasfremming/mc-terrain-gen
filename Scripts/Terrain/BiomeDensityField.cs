using System;
using UnityEngine;

// Blends multiple biome DENSITY FIELDS into one continuous 3D terrain
// function. Members can be heightfields (dunes) or pure SDF-style volume
// fields (canyon, alien rock) — the blend is over densities:
//
//     D(p) = sum_b  w_b(x, z) * D_b(p)
//
// Architecture notes (why this can't crack or band):
//  - The result is a single continuous function of position, sampled
//    identically by every LOD, so all the existing watertightness guarantees
//    (Transvoxel seams, chunk borders) apply automatically. Gaps between
//    biomes are structurally impossible.
//  - Weights come from a SOFTMAX over per-biome low-frequency noise fields:
//    they always sum to 1 (full coverage), vary smoothly (no hard borders),
//    and adding a biome is just adding an entry to the list. `sharpness`
//    controls transition band width.
//  - Biome weights are baked into VERTEX COLORS at meshing time (see
//    GetVertexColor); the terrain shader cross-fades biome palettes/textures
//    with them — no harsh color lines.
//  - Performance: grid sampling goes through AddDensityColumn, so each member
//    caches its per-(x,z) work once per column, whatever kind of field it is.
[CreateAssetMenu(fileName = "BiomeWorld", menuName = "Marching Cubes/Biome World")]
public class BiomeDensityField : DensityField
{
    [Tooltip("Index 0's weight is implicit; weights of indices 1..3 go to vertex color R/G/B for the shader. Each entry is a Biome asset bundling terrain shape + surface material.")]
    public Biome[] biomes = new Biome[0];

    public int seed = 99;
    [Tooltip("Scale (m) of the biome regions.")]
    public float regionScale = 1400f;
    [Tooltip("Higher = narrower biome transition bands.")]
    public float sharpness = 12f;

    const int kMaxBiomes = 8;

    DensityField Field(int i) => biomes[i] != null ? biomes[i].terrain : null;

    // Thread-safe: pure math on stack memory, no shared scratch.
    void ComputeWeights(float wx, float wz, Span<float> w, int n)
    {
        uint s = unchecked((uint)seed);
        float maxA = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            float a = TerrainNoise.Fbm(wx / regionScale + i * 13.7f, wz / regionScale - i * 7.3f,
                                       2, s + (uint)(i * 191)) + (biomes[i] != null ? biomes[i].bias : -10f);
            w[i] = a;
            if (a > maxA) maxA = a;
        }
        float sum = 0f;
        for (int i = 0; i < n; i++)
        {
            w[i] = Mathf.Exp(sharpness * (w[i] - maxA));
            sum += w[i];
        }
        for (int i = 0; i < n; i++) w[i] /= sum;
    }

    public override float Sample(Vector3 p)
    {
        int n = Mathf.Min(biomes.Length, kMaxBiomes);
        if (n == 0) return -p.y;

        Span<float> w = stackalloc float[kMaxBiomes];
        ComputeWeights(p.x, p.z, w, n);

        // Skip negligible biomes and renormalize — still a pure function of
        // position, so all LODs agree exactly.
        float d = 0f, used = 0f;
        for (int i = 0; i < n; i++)
        {
            if (w[i] < 0.004f || Field(i) == null) continue;
            d += w[i] * Field(i).Sample(p);
            used += w[i];
        }
        return used > 0f ? d / used : -p.y;
    }

    // Grid sampling: weights once per column, then each member fills the
    // column through its own cached fast path.
    public override void SampleGrid(Vector3 origin, int countX, int countY, int countZ, float step, float[] dest)
    {
        int n = Mathf.Min(biomes.Length, kMaxBiomes);
        Span<float> w = stackalloc float[kMaxBiomes];

        for (int z = 0; z < countZ; z++)
        {
            float wz = origin.z + z * step;
            for (int x = 0; x < countX; x++)
            {
                float wx = origin.x + x * step;
                int colIdx = z * countY * countX + x;

                float used = 0f;
                if (n > 0)
                {
                    ComputeWeights(wx, wz, w, n);
                    for (int i = 0; i < n; i++)
                        if (w[i] >= 0.004f && Field(i) != null) used += w[i];
                }

                // start from zero (or bare -y when nothing contributes)
                int idx = colIdx;
                for (int y = 0; y < countY; y++)
                {
                    dest[idx] = used > 0f ? 0f : -(origin.y + y * step);
                    idx += countX;
                }

                if (used > 0f)
                    for (int i = 0; i < n; i++)
                        if (w[i] >= 0.004f && Field(i) != null)
                            Field(i).AddDensityColumn(wx, wz, origin.y, step, countY,
                                                      w[i] / used, dest, colIdx, countX);
            }
        }
    }

    // Fast 2D gradient when every active biome is a plain heightfield
    // (d/dy is exactly -1 there); full 3D central differences otherwise.
    // Both formulas agree exactly where the choice flips, so no seams.
    public override Vector3 Gradient(Vector3 p, float eps)
    {
        int n = Mathf.Min(biomes.Length, kMaxBiomes);
        if (n > 0)
        {
            Span<float> w = stackalloc float[kMaxBiomes];
            ComputeWeights(p.x, p.z, w, n);
            bool pure2D = true;
            for (int i = 0; i < n && pure2D; i++)
            {
                if (w[i] < 0.004f || Field(i) == null) continue;
                if (!(Field(i) is HeightDensityField hf) || hf.Has3D) pure2D = false;
            }
            if (pure2D)
            {
                float dx = Sample(new Vector3(p.x + eps, p.y, p.z)) - Sample(new Vector3(p.x - eps, p.y, p.z));
                float dz = Sample(new Vector3(p.x, p.y, p.z + eps)) - Sample(new Vector3(p.x, p.y, p.z - eps));
                return new Vector3(dx / (2f * eps), -1f, dz / (2f * eps));
            }
        }
        return base.Gradient(p, eps); // 6-sample central differences
    }

    // Weighted surface hardness: 0 soft sand .. 1 hard rock, blending smoothly
    // across biome transitions (footprints gradually fade out approaching rock).
    public override float SurfaceHardness(Vector3 p)
    {
        int n = Mathf.Min(biomes.Length, kMaxBiomes);
        if (n == 0) return 0f;
        Span<float> w = stackalloc float[kMaxBiomes];
        ComputeWeights(p.x, p.z, w, n);
        float h = 0f;
        for (int i = 0; i < n; i++)
            if (biomes[i] != null) h += w[i] * biomes[i].hardness;
        return h;
    }

    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        minH = float.MaxValue; maxH = float.MinValue;
        int n = Mathf.Min(biomes.Length, kMaxBiomes);
        if (n == 0) { minH = maxH = 0f; return false; }
        for (int i = 0; i < n; i++)
        {
            if (Field(i) == null || !Field(i).TryGetHeightBounds(out float lo, out float hi))
                return false;
            minH = Mathf.Min(minH, lo);
            maxH = Mathf.Max(maxH, hi);
        }
        // the blend is a convex combination of the member densities
        return true;
    }

    public override bool HasVertexColors => biomes.Length > 1;

    // Vertex color channels R/G/B carry the weights of biomes 1..3 (biome 0 is
    // the implicit remainder). Pure function of position -> LODs agree.
    public override Color GetVertexColor(Vector3 p)
    {
        int n = Mathf.Min(biomes.Length, kMaxBiomes);
        if (n <= 1) return new Color(0, 0, 0, 1);
        Span<float> w = stackalloc float[kMaxBiomes];
        ComputeWeights(p.x, p.z, w, n);
        return new Color(
            n > 1 ? w[1] : 0f,
            n > 2 ? w[2] : 0f,
            n > 3 ? w[3] : 0f,
            1f);
    }
}
