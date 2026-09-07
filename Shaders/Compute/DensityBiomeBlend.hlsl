#ifndef MC_DENSITY_BIOME_BLEND_INCLUDED
#define MC_DENSITY_BIOME_BLEND_INCLUDED

#include "TerrainNoiseGPU.hlsl"
#include "BiomeSelect.hlsl"
#include "DensityDune.hlsl"
#include "DensityAlien.hlsl"
#include "DensityCanyon.hlsl"
#include "DensityFrost.hlsl"
#include "DensityGrove.hlsl"
#include "DensityDolomite.hlsl"
#include "DensityEroded.hlsl"

// Matches Biome.SurfaceStyle-style dispatch already proven in
// SandTerrain.shader's EVALUATE_CHANNEL macro -- density's analogue. Values
// must match whatever C# writes into _BiomeFieldType (see Biome.cs / the
// leaf DensityField subclasses' GPU-type binding).
#define MC_FIELDTYPE_DUNE   0
#define MC_FIELDTYPE_ALIEN  1
#define MC_FIELDTYPE_CANYON 2
#define MC_FIELDTYPE_FROST  3
#define MC_FIELDTYPE_GROVE  4
#define MC_FIELDTYPE_DOLOMITE 5
#define MC_FIELDTYPE_ERODED 6
// MC_MAX_BIOMES comes from BiomeSelect.hlsl

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
    GroveParams grove;
    DolomiteParams dolomite;
    ErodedParams eroded;     // appended LAST -- mirrors LeafGpuParams
};

struct BiomeBlendParams
{
    float seed;
    float regionScale;
    float sharpness;
    float biomeCount; // n, as float (cast to int at use)
    float edgeHeight; // BiomeDensityField.edgeHeight / edgeBand: border relief fade
    float edgeBand;
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
// `fw` is the sample spacing this evaluation belongs to -- see
// TerrainNoise.cs's header (LOD BAND-LIMITING) and DensityField.Sample.
// Written as one initialised local and one return, rather than four early
// returns: with [branch] in front of them FXC's flow analysis cannot prove the
// return value is always assigned and warns "use of potentially uninitialized
// variable" on every kernel that includes this header. The warning was always
// spurious -- the final fallback covered every path -- but this shape produces
// the same branch chain without it.
float EvaluateLeafDensity(int fieldType, float3 worldPos, LeafParams p, float fw)
{
    float d = -worldPos.y; // fallback flat ground, matches BiomeDensityField's n==0 case
    [branch]
    if (fieldType == MC_FIELDTYPE_DUNE)
        d = EvaluateDuneHeight(worldPos.x, worldPos.z, p.dune, fw) - worldPos.y;
    else if (fieldType == MC_FIELDTYPE_CANYON)
        d = EvaluateCanyonDensity(worldPos, p.canyon, fw);
    else if (fieldType == MC_FIELDTYPE_ALIEN)
        d = EvaluateAlienDensity(worldPos, p.alien, fw);
    else if (fieldType == MC_FIELDTYPE_FROST)
        d = EvaluateFrostHeight(worldPos.x, worldPos.z, p.frost, fw) - worldPos.y;
    else if (fieldType == MC_FIELDTYPE_GROVE)
        d = EvaluateGroveDensity(worldPos, p.grove, fw); // volumetric, not a heightfield
    else if (fieldType == MC_FIELDTYPE_DOLOMITE)
        d = EvaluateDolomiteHeight(worldPos.x, worldPos.z, p.dolomite, fw) - worldPos.y;
    else if (fieldType == MC_FIELDTYPE_ERODED)
        d = EvaluateErodedHeight(worldPos.x, worldPos.z, p.eroded, fw) - worldPos.y;
    return d;
}

// Biome selection lives in BiomeSelect.hlsl (shared with the terrain shader
// and the grass scatter, so all three agree on every boundary). These two
// keep the historical names and read their parameters from this pipeline's
// StructuredBuffers.
BiomeSelectParams MC_BiomeSelectFromBuffers(int n)
{
    BiomeSelectParams P;
    P.seed = _BiomeBlendBuf[0].seed;
    P.regionScale = _BiomeBlendBuf[0].regionScale;
    P.sharpness = _BiomeBlendBuf[0].sharpness;
    P.count = n;
    P.isPlanet = 0.0;      // callers pass the already-projected position
    P.center = 0.0;
    P.radius = 0.0;
    P.edgeBand = _BiomeBlendBuf[0].edgeBand;
    [unroll] for (int i = 0; i < MC_MAX_BIOMES; i++) P.bias[i] = _BiomeBias[i];
    return P;
}

void MC_ComputeBiomeWeights(float wx, float wz, out float w[MC_MAX_BIOMES], int n)
{
    MC_BiomeWeightsFlat(wx, wz, MC_BiomeSelectFromBuffers(n), w);
}

// With the relief factors (see BiomeDensityField.ComputeWeights): what the
// density blend itself uses.
void MC_ComputeBiomeWeightsRelief(float wx, float wz, out float w[MC_MAX_BIOMES], out float r[MC_MAX_BIOMES], int n)
{
    MC_BiomeWeightsFlatRelief(wx, wz, MC_BiomeSelectFromBuffers(n), w, r);
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
float EvaluateBiomeBlendWithRelief(float3 worldPos, float w[MC_MAX_BIOMES], float r[MC_MAX_BIOMES], int n, float fw)
{
    if (n <= 0) return -worldPos.y;
    float d = 0.0, used = 0.0;
    float plane = _BiomeBlendBuf[0].edgeHeight - worldPos.y;
    [loop]
    for (int i = 0; i < n; i++)
    {
        if (w[i] < 0.004) continue;
        // Port of BiomeDensityField.SampleWithWeights(p, w, relief, n, fw):
        // each biome fades toward the flat edge plane by its relief factor
        // before weighting, so neighbours meet at edgeHeight at the border.
        float ri = r[i];
        float di = ri > 0.0 ? EvaluateLeafDensity(_BiomeFieldType[i], worldPos, _LeafParams[i], fw) : 0.0;
        d += w[i] * (ri * di + (1.0 - ri) * plane);
        used += w[i];
    }
    return used > 0.0 ? d / used : -worldPos.y;
}

// Plain weighted blend (relief 1 everywhere) -- kept for callers that do
// not fade borders.
float EvaluateBiomeBlendWithWeights(float3 worldPos, float w[MC_MAX_BIOMES], int n, float fw)
{
    float r[MC_MAX_BIOMES];
    [unroll] for (int i = 0; i < MC_MAX_BIOMES; i++) r[i] = 1.0;
    return EvaluateBiomeBlendWithRelief(worldPos, w, r, n, fw);
}

// Port of BiomeDensityField.Sample: skip negligible biomes (same 0.004
// threshold), renormalize over the ones that remain.
float EvaluateBiomeBlend(float3 worldPos, float fw)
{
    int n = (int)_BiomeBlendBuf[0].biomeCount;
    if (n <= 0) return -worldPos.y;

    float w[MC_MAX_BIOMES];
    float r[MC_MAX_BIOMES];
    MC_ComputeBiomeWeightsRelief(worldPos.x, worldPos.z, w, r, n);
    return EvaluateBiomeBlendWithRelief(worldPos, w, r, n, fw);
}

// Sphere-coherent selection for PlanetField: `pos` is normalize(rel) * radius,
// the same point for all three triplanar faces (see BiomeDensityField.cs).
void MC_ComputeBiomeWeights3D(float3 pos, out float w[MC_MAX_BIOMES], int n)
{
    MC_BiomeWeightsSphere(pos, MC_BiomeSelectFromBuffers(n), w);
}

void MC_ComputeBiomeWeightsRelief3D(float3 pos, out float w[MC_MAX_BIOMES], out float r[MC_MAX_BIOMES], int n)
{
    MC_BiomeWeightsSphereRelief(pos, MC_BiomeSelectFromBuffers(n), w, r);
}

#endif
