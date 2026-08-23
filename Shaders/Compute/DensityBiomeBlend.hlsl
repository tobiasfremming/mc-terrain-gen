#ifndef MC_DENSITY_BIOME_BLEND_INCLUDED
#define MC_DENSITY_BIOME_BLEND_INCLUDED

#include "TerrainNoiseGPU.hlsl"
#include "DensityDune.hlsl"
#include "DensityAlien.hlsl"
#include "DensityCanyon.hlsl"
#include "DensityFrost.hlsl"

// Matches Biome.SurfaceStyle-style dispatch already proven in
// SandTerrain.shader's EVALUATE_CHANNEL macro -- density's analogue. Values
// must match whatever C# writes into _BiomeFieldType (see Biome.cs / the
// leaf DensityField subclasses' GPU-type binding).
#define MC_FIELDTYPE_DUNE   0
#define MC_FIELDTYPE_ALIEN  1
#define MC_FIELDTYPE_CANYON 2
#define MC_FIELDTYPE_FROST  3
#define MC_MAX_BIOMES 8

// Union of all 4 leaf types' params in one struct (nested, not flattened, so
// field names never collide across types). Small in absolute terms (~380
// bytes/slot x 8 slots) -- the unused sub-structs per slot cost negligible
// GPU memory.
struct LeafParams
{
    DuneParams dune;
    AlienParams alien;
    CanyonParams canyon;
    FrostParams frost;
};

struct BiomeBlendParams
{
    float seed;
    float regionScale;
    float sharpness;
    float biomeCount; // n, as float (cast to int at use)
};

// Persistent world/biome-blend configuration -- rebuilt only when
// TerrainTuning fires (mirrors MCChunkManager.BuildBiomeMaterialProps'
// existing dirty-flag pattern), never per-batch/per-dispatch. Bound as
// StructuredBuffers (rather than loose globals) even for the single-element
// ones so every persistent param, scalar or array, sets via the same
// ComputeBuffer.SetData path from C# -- no separate SetFloat-per-field code.
StructuredBuffer<LeafParams> _LeafParams;        // [MC_MAX_BIOMES]
StructuredBuffer<int> _BiomeFieldType;           // [MC_MAX_BIOMES]
StructuredBuffer<float> _BiomeBias;              // [MC_MAX_BIOMES]
StructuredBuffer<BiomeBlendParams> _BiomeBlendBuf; // [1]

// [branch]: fieldType is a runtime value (the compiler can't eliminate the
// other 3 leaf types' code at compile time), so without this hint the
// compiler is free to flatten this into straight-line arithmetic evaluating
// ALL 4 branches unconditionally every call -- 4x the cost, and 4x the code
// size at every one of this function's call sites. [branch] keeps it a real
// runtime branch instead. See EvaluateBiomeBlendWithWeights's [loop] comment
// for why call-site code size matters here specifically.
float EvaluateLeafDensity(int fieldType, float3 worldPos, LeafParams p)
{
    [branch]
    if (fieldType == MC_FIELDTYPE_DUNE)
        return EvaluateDuneHeight(worldPos.x, worldPos.z, p.dune) - worldPos.y;
    if (fieldType == MC_FIELDTYPE_CANYON)
        return EvaluateCanyonDensity(worldPos, p.canyon);
    if (fieldType == MC_FIELDTYPE_ALIEN)
        return EvaluateAlienDensity(worldPos, p.alien);
    if (fieldType == MC_FIELDTYPE_FROST)
        return EvaluateFrostHeight(worldPos.x, worldPos.z, p.frost) - worldPos.y;
    return -worldPos.y; // fallback flat ground, matches BiomeDensityField's n==0 case
}

// Port of BiomeDensityField.ComputeWeights: softmax over per-biome low-freq
// noise + bias, numerically stabilized by subtracting the max before exp.
void MC_ComputeBiomeWeights(float wx, float wz, out float w[MC_MAX_BIOMES], int n)
{
    uint s = (uint)(int)_BiomeBlendBuf[0].seed;
    float maxA = -3.402823e38;
    float raw[MC_MAX_BIOMES];
    [loop] for (int i = 0; i < n; i++)
    {
        float a = MC_Fbm(wx / _BiomeBlendBuf[0].regionScale + i * 13.7, wz / _BiomeBlendBuf[0].regionScale - i * 7.3,
                          2, s + (uint)(i * 191)) + _BiomeBias[i];
        raw[i] = a;
        if (a > maxA) maxA = a;
    }
    float sum = 0.0;
    [loop] for (int j = 0; j < n; j++)
    {
        w[j] = exp(_BiomeBlendBuf[0].sharpness * (raw[j] - maxA));
        sum += w[j];
    }
    [loop] for (int k = 0; k < n; k++) w[k] /= sum;
}

// Density blend using externally supplied weights (e.g. from
// MC_ComputeBiomeWeights3D) instead of computing them from worldPos.xz --
// same skip-and-renormalize logic EvaluateBiomeBlend (below) uses internally.
//
// [loop] is load-bearing, not a style choice: without it the compiler is
// free to fully UNROLL this (small, statically-bounded-by-MC_MAX_BIOMES
// loop), which duplicates a full EvaluateLeafDensity call -- itself already
// containing all 4 leaf types' complete evaluation code (Canyon's
// bridge/spire system, Frost's Worley scans, etc.) -- once per unrolled
// iteration, up to MC_MAX_BIOMES=8 times, at EVERY call site. This function
// has 3 call sites (EvaluatePlanetWrap's triplanar X/Y/Z faces), so unrolled
// this was ~24 full copies of the entire leaf-density branch tree compiled
// into one kernel -- exactly what caused the D3D shader compiler to time
// out entirely. [loop] forces a real runtime loop instead: one compiled
// copy of the loop body per call site, executed up to 8 times, not
// unrolled into 8 copies.
float EvaluateBiomeBlendWithWeights(float3 worldPos, float w[MC_MAX_BIOMES], int n)
{
    if (n <= 0) return -worldPos.y;
    float d = 0.0, used = 0.0;
    [loop]
    for (int i = 0; i < n; i++)
    {
        if (w[i] < 0.004) continue;
        d += w[i] * EvaluateLeafDensity(_BiomeFieldType[i], worldPos, _LeafParams[i]);
        used += w[i];
    }
    return used > 0.0 ? d / used : -worldPos.y;
}

// Port of BiomeDensityField.Sample: skip negligible biomes (same 0.004
// threshold), renormalize over the ones that remain.
float EvaluateBiomeBlend(float3 worldPos)
{
    int n = (int)_BiomeBlendBuf[0].biomeCount;
    if (n <= 0) return -worldPos.y;

    float w[MC_MAX_BIOMES];
    MC_ComputeBiomeWeights(worldPos.x, worldPos.z, w, n);
    return EvaluateBiomeBlendWithWeights(worldPos, w, n);
}

// Port of BiomeDensityField.ComputeWeights3D -- biome SELECTION as a
// function of a single 3D position (a point on the planet's surface),
// instead of 3 independent per-triplanar-face 2D positions. See the C#
// method's comment (BiomeDensityField.cs) for why this must be shared across
// all 3 faces rather than recomputed per face. `pos` divides by regionScale
// exactly like the 2D case, so regionScale keeps the same meaning in both
// flat and globe modes.
void MC_ComputeBiomeWeights3D(float3 pos, out float w[MC_MAX_BIOMES], int n)
{
    uint s = (uint)(int)_BiomeBlendBuf[0].seed;
    float3 q = pos / _BiomeBlendBuf[0].regionScale;
    float maxA = -3.402823e38;
    float raw[MC_MAX_BIOMES];
    [loop] for (int i = 0; i < n; i++)
    {
        float a = MC_Fbm3(q.x + i * 13.7, q.y - i * 7.3, q.z + i * 5.1, 2, s + (uint)(i * 191)) + _BiomeBias[i];
        raw[i] = a;
        if (a > maxA) maxA = a;
    }
    float sum = 0.0;
    [loop] for (int j = 0; j < n; j++)
    {
        w[j] = exp(_BiomeBlendBuf[0].sharpness * (raw[j] - maxA));
        sum += w[j];
    }
    [loop] for (int k = 0; k < n; k++) w[k] /= sum;
}

#endif
