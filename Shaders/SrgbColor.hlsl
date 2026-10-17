// sRGB-authored colour -> linear, for colours that reach a shader WITHOUT
// Unity converting them. The one copy of the curve in this project's shaders.
//
// Unity converts a material's Color properties (and SetColor / SetGlobalColor)
// to linear in a Linear project. It does NOT convert vertex colours, values
// set with SetVector / SetVectorArray, or colours packed into a buffer. An
// Inspector Color is authored in sRGB, so any of those that carries one must
// be decoded exactly once before it is used as albedo.
//
// Consumers (decode here, never also on the C# side):
//   PlantColor.hlsl   plant vertex colours (Plant, Plant Impostor Bake)
//   Grass.shader      the blade record's RGB565 base/tip colours
// Not here: the terrain's _BiomeShadeFlat/_BiomeShadeSteep globals are float
// vectors, so MCChunkManager.PublishBiomeShadingGlobals converts them once at
// upload instead (no banding to avoid, and no pow per biome per pixel).
//
// Plain HLSL with no pipeline includes, so URP, classic CG and compute code
// can all include it. Exact curve, the same one Color.linear uses; a no-op in
// a Gamma project, where shading happens in sRGB.
#ifndef MC_SRGB_COLOR_INCLUDED
#define MC_SRGB_COLOR_INCLUDED

float3 MC_SrgbToLinear(float3 srgb)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return srgb;
#else
    float3 c = saturate(srgb);
    float3 lo = c / 12.92;
    float3 hi = pow((c + 0.055) / 1.055, 2.4);
    return lerp(hi, lo, step(c, 0.04045));
#endif
}

#endif
