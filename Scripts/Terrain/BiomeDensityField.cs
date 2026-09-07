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
//  - Biome identity is never stored per vertex: the terrain shader and the
//    grass scatter evaluate the same selection from world position
//    (Shaders/Compute/BiomeSelect.hlsl mirrors ComputeWeights/ComputeWeights3D;
//    see BIOME_SHADING.md), so shading, grass and geometry agree on every
//    boundary. Vertex colour .a carries baked AO only.
//  - Performance: grid sampling goes through AddDensityColumn, so each member
//    caches its per-(x,z) work once per column, whatever kind of field it is.
[CreateAssetMenu(fileName = "BiomeWorld", menuName = "Marching Cubes/Biome World")]
public class BiomeDensityField : DensityField
{
    [Tooltip("Up to MaxBiomes (8) entries; slot i is biome i everywhere (selection, shading globals, grass). Each entry is a Biome asset bundling terrain shape + surface material. Order only changes the region map, never which style renders a biome.")]
    public Biome[] biomes = new Biome[0];

    public int seed = 99;
    [Tooltip("Scale (m) of the biome regions.")]
    public float regionScale = 1400f;
    [Tooltip("Higher = narrower biome transition bands.")]
    public float sharpness = 12f;

    [Header("Biome edges")]
    [Tooltip("Metres. The height every biome's relief fades toward at its borders, so neighbours meet without a step. Keep it where the flat biomes sit (dune plain, sand basins, alpine meadow).")]
    public float edgeHeight = 0f;
    [Tooltip("Width of the relief fade in selection-noise units; 0 disables it. A biome has full relief where its selection score leads the runner-up by this much; at the border the lead is 0 and both sides are flat at edgeHeight. Independent of sharpness, so shading can stay crisp while heights ramp. About 60-80 m per side at 0.2 with regionScale 1000.")]
    [Range(0f, 1f)] public float edgeBand = 0.2f;

    [Header("Rendering")]
    [Tooltip("Material every chunk in this world renders with (e.g. SandTerrain.mat). All biomes here are shaded by ONE material/shader, cross-faded by vertex-baked weights -- swap this to point the whole world at a different terrain shader. Leave unset to fall back to whatever material is on the chunk prefab.")]
    public Material terrainMaterial;

    const int kMaxBiomes = 8;
    public const int MaxBiomes = kMaxBiomes;
    public int BiomeCount => Mathf.Min(biomes.Length, kMaxBiomes);

    // The field in slot i, or null. Exposed for the one case where
    // TryBuildGpuLeaves' fixed LeafGpuParams is not the whole story:
    // LSystemGroveField also needs a per-dispatch capsule atlas uploaded, so
    // TerrainGpuSampler has to find the instance, not just its params.
    public DensityField FieldAt(int i) => (uint)i < (uint)BiomeCount ? Field(i) : null;

    bool _nullSlotWarned;

    protected override void OnValidate()
    {
        base.OnValidate();
        // Loud, not silent: the density blend, the shader's selection loop and
        // the published biome table all stop at MaxBiomes. Anything past it is
        // ignored everywhere, so say so here instead of shading it as biome 0.
        if (biomes != null && biomes.Length > kMaxBiomes)
            Debug.LogWarning($"[BiomeDensityField] '{name}': {biomes.Length} biomes assigned but only the first {kMaxBiomes} are used (MaxBiomes).", this);
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

    // Thread-safe: pure math on stack memory, no shared scratch. Public for
    // PlantScatter, which accepts a candidate plant with probability equal
    // to its biome's weight at that spot.
    public void ComputeWeights(float wx, float wz, Span<float> w, int n)
    {
        Span<float> relief = stackalloc float[kMaxBiomes];
        ComputeWeights(wx, wz, w, relief, n);
    }

    // Same, plus each biome's RELIEF factor: 1 where the biome leads the
    // runner-up by edgeBand or more in raw selection score, 0 at and beyond
    // the border. Only the leading biome ever has a positive lead, so at most
    // one entry is non-zero; the blend uses it to fade that biome's terrain
    // toward the flat edgeHeight plane. Mirrored by MC_BiomeWeightsFlatRelief
    // in Shaders/Compute/BiomeSelect.hlsl.
    public void ComputeWeights(float wx, float wz, Span<float> w, Span<float> relief, int n)
    {
        uint s = unchecked((uint)seed);
        for (int i = 0; i < n; i++)
            w[i] = TerrainNoise.Fbm(wx / regionScale + i * 13.7f, wz / regionScale - i * 7.3f,
                                    2, s + (uint)(i * 191)) + (biomes[i] != null ? biomes[i].bias : -10f);
        FinishWeights(w, relief, n);
    }

    // Softmax over the raw scores in w (in place) and the relief factors.
    void FinishWeights(Span<float> w, Span<float> relief, int n)
    {
        float maxA = float.MinValue, secondA = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            float a = w[i];
            if (a > maxA) { secondA = maxA; maxA = a; }
            else if (a > secondA) secondA = a;
        }
        float sum = 0f;
        for (int i = 0; i < n; i++)
        {
            float a = w[i];
            // lead over the best OTHER biome: positive only for the leader
            float lead = a >= maxA ? a - secondA : a - maxA;
            relief[i] = edgeBand > 0f ? TerrainNoise.Smoothstep(0f, edgeBand, lead) : 1f;
            w[i] = Mathf.Exp(sharpness * (a - maxA));
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
        Span<float> relief = stackalloc float[kMaxBiomes];
        ComputeWeights3D(pos, w, relief, n);
    }

    public void ComputeWeights3D(Vector3 pos, Span<float> w, Span<float> relief, int n)
    {
        uint s = unchecked((uint)seed);
        Vector3 q = pos / regionScale;
        for (int i = 0; i < n; i++)
            w[i] = TerrainNoise.Fbm3(q.x + i * 13.7f, q.y - i * 7.3f, q.z + i * 5.1f,
                                     2, s + (uint)(i * 191)) + (biomes[i] != null ? biomes[i].bias : -10f);
        FinishWeights(w, relief, n);
    }

    // Density blend using externally supplied weights (e.g. from
    // ComputeWeights3D) instead of computing them from p.x/p.z -- same
    // skip-and-renormalize logic as Sample().
    public float SampleWithWeights(Vector3 p, ReadOnlySpan<float> w, int n, float fw)
    {
        float d = 0f, used = 0f;
        for (int i = 0; i < n; i++)
        {
            if (w[i] < 0.004f || Field(i) == null) continue;
            d += w[i] * Field(i).Sample(p, fw);
            used += w[i];
        }
        return used > 0f ? d / used : -p.y;
    }

    // The blend proper: each biome's density is first faded toward the flat
    // edgeHeight plane by its relief factor (see ComputeWeights), THEN
    // weighted. At a border both sides have zero relief, so the heights meet
    // exactly; inside a region relief is 1 and this is the plain blend.
    // Mirrored by EvaluateBiomeBlendWithRelief in DensityBiomeBlend.hlsl.
    public float SampleWithWeights(Vector3 p, ReadOnlySpan<float> w, ReadOnlySpan<float> relief, int n, float fw)
    {
        float d = 0f, used = 0f;
        float plane = edgeHeight - p.y;
        for (int i = 0; i < n; i++)
        {
            if (w[i] < 0.004f || Field(i) == null) continue;
            float r = relief[i];
            float di = r > 0f ? Field(i).Sample(p, fw) : 0f;
            d += w[i] * (r * di + (1f - r) * plane);
            used += w[i];
        }
        return used > 0f ? d / used : -p.y;
    }

    // SurfaceHardness using externally supplied weights -- a pure function of
    // the weight vector (no position-dependent per-biome term), so unlike
    // SampleWithWeights it doesn't need a `p`.
    public float SurfaceHardnessWithWeights(ReadOnlySpan<float> w, int n)
    {
        float h = 0f;
        for (int i = 0; i < n; i++)
            if (biomes[i] != null) h += w[i] * biomes[i].hardness;
        return h;
    }

    public override float Sample(Vector3 p, float fw)
    {
        int n = Mathf.Min(biomes.Length, kMaxBiomes);
        if (n == 0) return -p.y;

        Span<float> w = stackalloc float[kMaxBiomes];
        Span<float> relief = stackalloc float[kMaxBiomes];
        ComputeWeights(p.x, p.z, w, relief, n);

        // Skip negligible biomes and renormalize — still a pure function of
        // position, so all LODs agree exactly.
        return SampleWithWeights(p, w, relief, n, fw);
    }

    // Grid sampling: weights once per column, then each member fills the
    // column through its own cached fast path.
    public override void SampleGrid(Vector3 origin, int countX, int countY, int countZ, float step, float[] dest)
    {
        int n = Mathf.Min(biomes.Length, kMaxBiomes);
        Span<float> w = stackalloc float[kMaxBiomes];
        Span<float> relief = stackalloc float[kMaxBiomes];

        for (int z = 0; z < countZ; z++)
        {
            float wz = origin.z + z * step;
            for (int x = 0; x < countX; x++)
            {
                float wx = origin.x + x * step;
                int colIdx = z * countY * countX + x;

                float used = 0f, flat = 0f;
                if (n > 0)
                {
                    ComputeWeights(wx, wz, w, relief, n);
                    for (int i = 0; i < n; i++)
                        if (w[i] >= 0.004f && Field(i) != null)
                        {
                            used += w[i];
                            flat += w[i] * (1f - relief[i]);
                        }
                }

                // start from the faded-out share of the flat edge plane (or
                // bare -y when nothing contributes); same formula as
                // SampleWithWeights per sample
                float c0 = used > 0f ? flat / used : 0f;
                int idx = colIdx;
                for (int y = 0; y < countY; y++)
                {
                    float wy = origin.y + y * step;
                    dest[idx] = used > 0f ? c0 * (edgeHeight - wy) : -wy;
                    idx += countX;
                }

                if (used > 0f)
                    for (int i = 0; i < n; i++)
                        if (w[i] >= 0.004f && relief[i] > 0f && Field(i) != null)
                            Field(i).AddDensityColumn(wx, wz, origin.y, step, countY,
                                                      w[i] * relief[i] / used, dest, colIdx, countX, step);
            }
        }
    }

    // Fast 2D gradient when every active biome is a plain heightfield
    // (d/dy is exactly -1 there); full 3D central differences otherwise.
    // Both formulas agree exactly where the choice flips, so no seams.
    public override Vector3 Gradient(Vector3 p, float eps, float fw)
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
                float dx = Sample(new Vector3(p.x + eps, p.y, p.z), fw) - Sample(new Vector3(p.x - eps, p.y, p.z), fw);
                float dz = Sample(new Vector3(p.x, p.y, p.z + eps), fw) - Sample(new Vector3(p.x, p.y, p.z - eps), fw);
                return new Vector3(dx / (2f * eps), -1f, dz / (2f * eps));
            }
        }
        return base.Gradient(p, eps, fw); // 6-sample central differences
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
        // the blend is a convex combination of the member densities and, near
        // borders, the flat edge plane
        if (edgeBand > 0f)
        {
            minH = Mathf.Min(minH, edgeHeight - 1f);
            maxH = Mathf.Max(maxH, edgeHeight + 1f);
        }
        return true;
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
        blend = new BiomeBlendGpuParams { seed = seed, regionScale = regionScale, sharpness = sharpness, biomeCount = n,
                                          edgeHeight = edgeHeight, edgeBand = edgeBand };
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
