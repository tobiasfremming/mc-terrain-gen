// Plant vertex colour -> linear albedo. The ONE place this conversion lives.
//
// PlantMeshBuilder writes the profile's Inspector colours (authored in sRGB,
// like every Color field) to the mesh as raw Color32 bytes. Unity converts
// material colours to linear for a Linear project, but never vertex colours,
// so a shader that reads them must decode them itself -- once. Reading them
// as linear made every plant about twice as bright (a pale lavender Baobab).
//
// Kept in the shader rather than at mesh-build time: 8-bit LINEAR vertex
// colours band badly in the darks (a dark bark's blue channel collapses to
// two or three steps), and decoding here needs no re-bake of the meshes.
//
// Used by MarchingCubes/Plant (Forward) and the impostor bake shader. The
// impostor CARD shader must not call it: its atlas is an sRGB texture, which
// the sampler already decodes.
//
// Plain HLSL with no pipeline includes, so both the URP and the classic CG
// shader can include it. Exact sRGB curve, the same one Color.linear uses.
#ifndef MC_PLANT_COLOR_INCLUDED
#define MC_PLANT_COLOR_INCLUDED

float3 PlantVertexAlbedo(float3 srgb)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return srgb;    // gamma project: shading happens in sRGB, nothing to decode
#else
    float3 c = saturate(srgb);
    float3 lo = c / 12.92;
    float3 hi = pow((c + 0.055) / 1.055, 2.4);
    return lerp(hi, lo, step(c, 0.04045));
#endif
}

#endif
