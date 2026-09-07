#ifndef MC_BIOME_SELECT_GLOBALS_INCLUDED
#define MC_BIOME_SELECT_GLOBALS_INCLUDED

#include "../Compute/BiomeSelect.hlsl"

// Shader GLOBALS published by MCChunkManager.PublishBiomeShadingGlobals
// whenever the biome world changes (TerrainTuning). Globals, not material
// properties: the terrain shader, the grass shaders and anything else that
// needs "which biome is here" read the same values, and none of it is
// per-material data (so the SRP Batcher is unaffected). Read BIOME_SHADING.md.
//
// Selection (feeds BiomeSelectParams):
float4 _BiomeSelParams;   // x seed, y regionScale, z sharpness, w biome count
float4 _BiomeSelPlanet;   // xyz planet centre, w radius
float4 _BiomeSelFlags;    // x 1 = planet mode, 0 = flat
float4 _BiomeSelBias0;    // biases of biome slots 0..3
float4 _BiomeSelBias1;    // biases of biome slots 4..7

// Per-biome shading table, one entry per slot (Biome asset at that index):
float4 _BiomeShadeA[MC_MAX_BIOMES];      // x Biome.SurfaceStyle, y Biome.blendSharpness, z Biome.hardness
float4 _BiomeShadeFlat[MC_MAX_BIOMES];   // Biome.colorFlat
float4 _BiomeShadeSteep[MC_MAX_BIOMES];  // Biome.colorSteep
float4 _BiomeShadeLines[MC_MAX_BIOMES];  // style-specific height lines, planet radius already added:
                                         //   Dolomite:       x rock line, y rock line blend
                                         //   Mountain:       x snow line, y snow blend, z grass line, w grass blend
                                         //   DesertMountain: x sand line, y sand blend

BiomeSelectParams MC_BiomeSelectFromGlobals()
{
    BiomeSelectParams P;
    P.seed = _BiomeSelParams.x;
    P.regionScale = _BiomeSelParams.y;
    P.sharpness = _BiomeSelParams.z;
    P.count = (int)_BiomeSelParams.w;
    P.isPlanet = _BiomeSelFlags.x;
    P.center = _BiomeSelPlanet.xyz;
    P.radius = _BiomeSelPlanet.w;
    P.bias[0] = _BiomeSelBias0.x; P.bias[1] = _BiomeSelBias0.y; P.bias[2] = _BiomeSelBias0.z; P.bias[3] = _BiomeSelBias0.w;
    P.bias[4] = _BiomeSelBias1.x; P.bias[5] = _BiomeSelBias1.y; P.bias[6] = _BiomeSelBias1.z; P.bias[7] = _BiomeSelBias1.w;
    return P;
}

// Weights at a world position, sharpened per biome (Biome.blendSharpness)
// and renormalised -- what the terrain shader blends its styles with. Grass
// or other consumers that want the RAW partition of unity call
// MC_BiomeWeights(worldPos, MC_BiomeSelectFromGlobals(), w) instead.
void MC_BiomeShadeWeights(float3 worldPos, out float w[MC_MAX_BIOMES], out int count)
{
    BiomeSelectParams P = MC_BiomeSelectFromGlobals();
    count = P.count;
    MC_BiomeWeights(worldPos, P, w);
    float sum = 0.0;
    [loop] for (int i = 0; i < count; i++)
    {
        w[i] = pow(max(w[i], 0.0001), 1.0 / max(_BiomeShadeA[i].y, 0.01));
        sum += w[i];
    }
    sum = max(sum, 1e-4);
    [loop] for (int j = 0; j < count; j++) w[j] /= sum;
}

#endif
