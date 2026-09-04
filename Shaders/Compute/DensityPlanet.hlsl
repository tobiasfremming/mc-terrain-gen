#ifndef MC_DENSITY_PLANET_INCLUDED
#define MC_DENSITY_PLANET_INCLUDED

#include "DensityBiomeBlend.hlsl"

struct PlanetParams
{
    float isPlanet;   // 0/1 -- a DISPATCH-TIME switch (see EvaluateWorldDensity), not a per-thread branch: under a planet wrap, column-caching isn't applicable to begin with (PlanetField.Sample's localHeight depends on full 3D distance from center, not a fixed horizontal pair), matching the existing CPU-side cliff PlanetField.TryGetEmptySkip's own comment calls out.
    float3 center;
    float radius;
};

StructuredBuffer<PlanetParams> _PlanetBuf; // [1] -- see DensityBiomeBlend.hlsl's _BiomeBlendBuf comment

// Port of PlanetField.Sample's triplanar blend -- the same technique
// SandTerrain.shader uses for texturing. For a world point, evaluate the
// wrapped (flat-convention) biome blend at up to 3 permuted "local"
// positions -- one per world axis treated as local up -- weighted by how
// aligned the point's radial direction is with each axis. Continuous
// weights (no discrete face-switch) keep this crack-free across chunk/LOD
// boundaries; see PlanetField.cs's header comment for the full rationale.
//
// Biome SELECTION is computed once from `rel` (MC_ComputeBiomeWeights3D,
// sphere-coherent) and reused for all 3 faces, rather than each face
// recomputing its own 2D-projected weights -- see BiomeDensityField.
// ComputeWeights3D's comment for why per-face weights would visibly
// mismatch at the triplanar transition bands. Only each biome's own terrain
// SHAPE still varies per face (via EvaluateBiomeBlendWithWeights's worldPos).
float EvaluatePlanetWrap(float3 worldPos, float fw)
{
    float3 rel = worldPos - _PlanetBuf[0].center;
    float dist = length(rel);
    if (dist < 1e-5) return _PlanetBuf[0].radius; // exact center: degenerate, arbitrary but harmless

    float localHeight = dist - _PlanetBuf[0].radius;
    float3 aw = abs(rel);
    float sum = aw.x + aw.y + aw.z;
    float3 w = sum > 1e-6 ? aw / sum : float3(1.0, 0.0, 0.0);

    int n = (int)_BiomeBlendBuf[0].biomeCount;
    float bw[MC_MAX_BIOMES];
    // normalize(rel)*radius, NOT rel -- biome selection must be a function of
    // WHERE ON THE SPHERE you are, not how high above it. See PlanetField.
    // Sample's comment for why feeding `rel` directly stacks one biome on top
    // of another in the same column at blendSharpness ~200.
    MC_ComputeBiomeWeights3D(normalize(rel) * _PlanetBuf[0].radius, bw, n);

    const float eps = 0.0005;
    float d = 0.0;
    // The triplanar permutation is a rigid relabelling of axes, so the sample
    // spacing survives it unchanged -- fw passes straight through.
    if (w.x > eps) d += w.x * EvaluateBiomeBlendWithWeights(float3(rel.z, localHeight, rel.y), bw, n, fw);
    if (w.y > eps) d += w.y * EvaluateBiomeBlendWithWeights(float3(rel.x, localHeight, rel.z), bw, n, fw);
    if (w.z > eps) d += w.z * EvaluateBiomeBlendWithWeights(float3(rel.x, localHeight, rel.y), bw, n, fw);
    return d;
}

// Top-level entry point: the ONE function the compute kernel calls per
// sample.
float EvaluateWorldDensity(float3 worldPos, float fw)
{
    if (_PlanetBuf[0].isPlanet > 0.5) return EvaluatePlanetWrap(worldPos, fw);
    return EvaluateBiomeBlend(worldPos, fw);
}

#endif
