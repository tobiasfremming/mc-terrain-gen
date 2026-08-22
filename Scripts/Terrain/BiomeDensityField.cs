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

    [Header("Rendering")]
    [Tooltip("Material every chunk in this world renders with (e.g. SandTerrain.mat). All biomes here are shaded by ONE material/shader, cross-faded by vertex-baked weights -- swap this to point the whole world at a different terrain shader. Leave unset to fall back to whatever material is on the chunk prefab.")]
    public Material terrainMaterial;

    const int kMaxBiomes = 8;
    public const int MaxBiomes = kMaxBiomes;
    public int BiomeCount => Mathf.Min(biomes.Length, kMaxBiomes);

    bool _nullSlotWarned;

    protected override void OnValidate()
    {
        base.OnValidate();
        _nullSlotWarned = false; // biomes[] may have just been fixed -- allow a fresh warning if not
    }

    // An unassigned slot here doesn't just skip that biome -- it silently
    // takes TryGetHeightBounds/TryGetEmptySkip/TryBuildGpuLeaves down with it
    // for the WHOLE world (see their own comments), degrading spawn-altitude
    // accuracy, chunk-skip perf, and GPU acceleration all at once with no
    // other diagnostic anywhere. Warn once so a resized-but-not-yet-filled
    // biomes[] array doesn't look like three unrelated mystery slowdowns.
    DensityField Field(int i)
    {
        if (biomes[i] == null)
        {
            if (!_nullSlotWarned)
            {
                _nullSlotWarned = true;
                Debug.LogWarning($"[BiomeDensityField] '{name}': biomes[{i}] is unassigned -- " +
                    "falling back to slow CPU-only generation (no GPU acceleration, no chunk-skip, " +
                    "conservative spawn altitude) until every slot has a Biome assigned.", this);
            }
            return null;
        }
        return biomes[i].terrain;
    }

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

    // Sphere-coherent counterpart of ComputeWeights, for PlanetField: biome
    // SELECTION as a function of a single 3D position (e.g. a point on the
    // planet's surface, dir.normalized * radius), instead of a 2D (x,z)
    // position. PlanetField's triplanar wrap evaluates each biome's own
    // terrain SHAPE via 3 different per-face flat projections (needed for
    // watertightness -- see PlanetField.cs), but if biome selection also
    // went through those same 3 independent per-face (x,z) projections, the
    // result would be 3 UNRELATED biome patterns stitched together, visibly
    // mismatched near the triplanar transition bands. Using one 3D noise
    // field for selection instead means every face agrees on which biome is
    // where -- only each biome's shape still varies per face, as intended.
    // `pos` divides by regionScale exactly like the 2D case's wx/wz, so
    // regionScale keeps the same "meters per biome region" meaning in both
    // flat and globe modes.
    public void ComputeWeights3D(Vector3 pos, Span<float> w, int n)
    {
        uint s = unchecked((uint)seed);
        Vector3 q = pos / regionScale;
        float maxA = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            float a = TerrainNoise.Fbm3(q.x + i * 13.7f, q.y - i * 7.3f, q.z + i * 5.1f,
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

    // Density blend using externally supplied weights (e.g. from
    // ComputeWeights3D) instead of computing them from p.x/p.z -- same
    // skip-and-renormalize logic as Sample().
    public float SampleWithWeights(Vector3 p, ReadOnlySpan<float> w, int n)
    {
        float d = 0f, used = 0f;
        for (int i = 0; i < n; i++)
        {
            if (w[i] < 0.004f || Field(i) == null) continue;
            d += w[i] * Field(i).Sample(p);
            used += w[i];
        }
        return used > 0f ? d / used : -p.y;
    }

    // GetVertexColor/SurfaceHardness using externally supplied weights --
    // both are pure functions of the weight vector (no position-dependent
    // per-biome term), so unlike SampleWithWeights they don't need a `p`.
    public Color GetVertexColorWithWeights(ReadOnlySpan<float> w, int n)
    {
        if (n <= 1) return new Color(0, 0, 0, 1);
        return new Color(n > 1 ? w[1] : 0f, n > 2 ? w[2] : 0f, n > 3 ? w[3] : 0f, 1f);
    }

    public float SurfaceHardnessWithWeights(ReadOnlySpan<float> w, int n)
    {
        float h = 0f;
        for (int i = 0; i < n; i++)
            if (biomes[i] != null) h += w[i] * biomes[i].hardness;
        return h;
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

    // GPU acceleration: resolves every active biome's terrain field to a
    // known leaf GPU type and packs everything needed to reproduce
    // ComputeWeights + the per-biome blend on the GPU (see
    // Shaders/Compute/DensityBiomeBlend.hlsl's EvaluateBiomeBlend). Returns
    // false (and leaves the out params unusable) if ANY active biome's field
    // isn't GPU-capable -- per the plan, this world must fall back to
    // pure-CPU sampling entirely rather than mixing GPU/CPU per biome, which
    // risks ULP-level cracks at chunk boundaries between differently-shaded
    // biome regions.
    public bool TryBuildGpuLeaves(out BiomeBlendGpuParams blend, out LeafGpuParams[] leaves,
                                   out GpuFieldType[] fieldTypes, out float[] biases)
    {
        int n = Mathf.Min(biomes.Length, kMaxBiomes);
        blend = new BiomeBlendGpuParams { seed = seed, regionScale = regionScale, sharpness = sharpness, biomeCount = n };
        leaves = new LeafGpuParams[kMaxBiomes];
        fieldTypes = new GpuFieldType[kMaxBiomes];
        biases = new float[kMaxBiomes];
        for (int i = 0; i < kMaxBiomes; i++) fieldTypes[i] = GpuFieldType.None;

        for (int i = 0; i < n; i++)
        {
            var field = Field(i);
            if (field == null || field.GpuType == GpuFieldType.None) return false;
            leaves[i] = field.ToGpuLeafParams();
            fieldTypes[i] = field.GpuType;
            biases[i] = biomes[i] != null ? biomes[i].bias : -10f;
        }
        return true;
    }
}
