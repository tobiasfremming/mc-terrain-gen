#ifndef MC_BIOME_SELECT_INCLUDED
#define MC_BIOME_SELECT_INCLUDED

#include "TerrainNoiseGPU.hlsl"

// Biome SELECTION: which biome is where, as a pure function of world
// position. This is the single GPU copy of BiomeDensityField.ComputeWeights
// (flat worlds) and ComputeWeights3D (planets); the density compute, the
// terrain shader and the grass scatter all call it, so geometry, shading and
// grass can never disagree about a boundary. Read BIOME_SHADING.md.
//
// Nothing here is per-vertex or per-material. Callers fill BiomeSelectParams
// from wherever their parameters live (StructuredBuffers in the density
// pipeline, shader globals in surface shaders -- see BiomeSelectGlobals.hlsl)
// and get the partition-of-unity weights back.

#define MC_MAX_BIOMES 8

struct BiomeSelectParams
{
    float seed;
    float regionScale;
    float sharpness;
    int   count;      // biomes in use, <= MC_MAX_BIOMES
    float isPlanet;   // 1: select on the sphere (normalize(p - center) * radius); 0: on (x, z)
    float3 center;
    float radius;
    float edgeBand;   // relief fade width in score units (BiomeDensityField.edgeBand); 0 = off
    float bias[MC_MAX_BIOMES];
};

// Softmax over raw scores plus each biome's RELIEF factor: 1 where it leads
// the runner-up by edgeBand or more, 0 at and beyond the border (only the
// leader can be non-zero). Mirrors BiomeDensityField.FinishWeights.
void MC_BiomeFinishWeights(float raw[MC_MAX_BIOMES], BiomeSelectParams P, out float w[MC_MAX_BIOMES], out float r[MC_MAX_BIOMES])
{
    int n = P.count;
    // Fully initialise the out arrays up front: the loops below are bounded
    // by a runtime count, and FXC otherwise reports "potentially
    // uninitialized" all the way up the call chain.
    [unroll] for (int z = 0; z < MC_MAX_BIOMES; z++) { w[z] = 0.0; r[z] = 0.0; }
    float maxA = -3.402823e38, secondA = -3.402823e38;
    [loop] for (int i = 0; i < n; i++)
    {
        float a = raw[i];
        if (a > maxA) { secondA = maxA; maxA = a; }
        else if (a > secondA) secondA = a;
    }
    float sum = 0.0;
    [loop] for (int j = 0; j < n; j++)
    {
        float a = raw[j];
        float lead = a >= maxA ? a - secondA : a - maxA;
        r[j] = P.edgeBand > 0.0 ? MC_Smoothstep(0.0, P.edgeBand, lead) : 1.0;
        w[j] = exp(P.sharpness * (a - maxA));
        sum += w[j];
    }
    [loop] for (int k = 0; k < n; k++) w[k] /= sum;
}

// Flat worlds. Mirrors BiomeDensityField.ComputeWeights exactly: per-biome
// two-octave fbm on (x, z) with per-index offsets, plus bias, softmax with
// the max subtracted first. The selection is deliberately NOT band-limited
// (see the C# comment): regionScale is kilometres and a fade would move the
// boundaries themselves with LOD.
void MC_BiomeWeightsFlatRelief(float wx, float wz, BiomeSelectParams P, out float w[MC_MAX_BIOMES], out float r[MC_MAX_BIOMES])
{
    uint s = (uint)(int)P.seed;
    int n = P.count;
    float raw[MC_MAX_BIOMES];
    [unroll] for (int z = 0; z < MC_MAX_BIOMES; z++) raw[z] = -3.402823e38;
    [loop] for (int i = 0; i < n; i++)
        raw[i] = MC_Fbm(wx / P.regionScale + i * 13.7, wz / P.regionScale - i * 7.3,
                        2, s + (uint)(i * 191), 0.0) + P.bias[i];
    MC_BiomeFinishWeights(raw, P, w, r);
}

void MC_BiomeWeightsFlat(float wx, float wz, BiomeSelectParams P, out float w[MC_MAX_BIOMES])
{
    float r[MC_MAX_BIOMES];
    MC_BiomeWeightsFlatRelief(wx, wz, P, w, r);
}

// Planets. `pos` is the point on the sphere: normalize(worldPos - center) *
// radius, NOT the raw offset -- every triplanar face and every consumer must
// select from the same point or the biome map differs between them. Mirrors
// BiomeDensityField.ComputeWeights3D.
void MC_BiomeWeightsSphereRelief(float3 pos, BiomeSelectParams P, out float w[MC_MAX_BIOMES], out float r[MC_MAX_BIOMES])
{
    uint s = (uint)(int)P.seed;
    int n = P.count;
    float3 q = pos / P.regionScale;
    float raw[MC_MAX_BIOMES];
    [unroll] for (int z = 0; z < MC_MAX_BIOMES; z++) raw[z] = -3.402823e38;
    [loop] for (int i = 0; i < n; i++)
        raw[i] = MC_Fbm3(q.x + i * 13.7, q.y - i * 7.3, q.z + i * 5.1, 2, s + (uint)(i * 191), 0.0) + P.bias[i];
    MC_BiomeFinishWeights(raw, P, w, r);
}

void MC_BiomeWeightsSphere(float3 pos, BiomeSelectParams P, out float w[MC_MAX_BIOMES])
{
    float r[MC_MAX_BIOMES];
    MC_BiomeWeightsSphereRelief(pos, P, w, r);
}

// The one entry point most callers want: weights at an absolute world
// position, in whichever mode the world is in.
void MC_BiomeWeights(float3 worldPos, BiomeSelectParams P, out float w[MC_MAX_BIOMES])
{
    if (P.isPlanet > 0.5)
    {
        float3 rel = worldPos - P.center;
        MC_BiomeWeightsSphere(normalize(rel) * P.radius, P, w);
    }
    else
    {
        MC_BiomeWeightsFlat(worldPos.x, worldPos.z, P, w);
    }
}

#endif
