#ifndef MC_DENSITY_DUNE_INCLUDED
#define MC_DENSITY_DUNE_INCLUDED

#include "TerrainNoiseGPU.hlsl"

// GPU port of Scripts/Terrain/DuneHeightfield.cs's HeightAt(wx,wz). Returns
// HEIGHT (not density) -- matches HeightDensityField.Sample's convention of
// `density = HeightAt(x,z) - y`, applied by the caller (EvaluateLeafDensity
// in TerrainDensityDispatch.hlsl). Field order/names mirror the C# class's
// serialized fields exactly; keep the two in lockstep.
struct DuneParams
{
    float seed;
    float windDegrees;
    float baseHeight;
    float megaWavelength;
    float megaHeight;
    float megaSinuosity;
    float megaSegmentation;
    float secondaryWavelength;
    float secondaryHeight;
    float secondaryWindOffset;
    float secondarySinuosity;
    float secondarySegmentation;
    float secondaryPatchScale;
    float rippleWavelength;
    float rippleHeight;
    float supplyScale;
    float sizeVariationScale;
    float plainScale;
    float plainAmplitude;
};

static const float MC_DUNE_CREST_POS = 0.72; // matches DuneHeightfield.kCrestPos

// t in [0,1) along wind through one dune period -> height 0..1.
float MC_DuneProfile(float t, float crestPos)
{
    if (t < crestPos)
    {
        float w = t / crestPos;
        return w * w * (1.6 - 0.6 * w);
    }
    float lee = (t - crestPos) / (1.0 - crestPos);
    float inv = 1.0 - lee;
    return inv * inv * (3.0 - 2.0 * inv);
}

// Quasi-periodic transverse dune ridges (see DuneHeightfield.DuneSystem).
float MC_DuneSystem(float u, float v, float wavelength, uint seed,
                     float sinuosity, float segmentation, float crestPos)
{
    float inv = 1.0 / wavelength;
    float warp = (MC_GNoise(u * inv * 0.35, v * inv * 0.22, seed + 7u) * 0.55
                + MC_GNoise(u * inv * 0.11, v * inv * 0.07, seed + 13u) * 0.9) * sinuosity;
    float phase = u * inv + warp;
    float t = phase - floor(phase);
    float prof = MC_DuneProfile(t, crestPos);

    float seg = saturate(0.5 + 0.5 * MC_GNoise(u * inv * 0.18, v * inv * 0.4, seed + 29u));
    seg = 1.0 - segmentation * (1.0 - seg * seg * (3.0 - 2.0 * seg));
    return prof * seg;
}

float EvaluateDuneHeight(float wx, float wz, DuneParams p)
{
    uint s = (uint)(int)p.seed;
    float wr = radians(p.windDegrees);
    float cu = cos(wr), su = sin(wr);
    float u = wx * cu + wz * su;   // along wind
    float v = -wx * su + wz * cu;  // across wind

    // --- regional modulation ---
    float supply = MC_Fbm(wx / p.supplyScale, wz / p.supplyScale, 3, s + 101u);
    supply = saturate((supply + 0.55) / 1.0);
    supply = 0.25 + 0.75 * (supply * supply * (3.0 - 2.0 * supply));
    float sizeMod = 0.85 + 0.5 * MC_Fbm(wx / p.sizeVariationScale + 31.7, wz / p.sizeVariationScale - 11.3, 2, s + 211u);

    // --- mega dunes (draa) ---
    float mega = MC_DuneSystem(u, v, p.megaWavelength, s + 1u, p.megaSinuosity, p.megaSegmentation, MC_DUNE_CREST_POS) * p.megaHeight;

    // --- secondary dunes riding on them, slightly rotated wind ---
    float wr2 = radians(p.windDegrees + p.secondaryWindOffset);
    float u2 = wx * cos(wr2) + wz * sin(wr2);
    float v2 = -wx * sin(wr2) + wz * cos(wr2);
    float sec = MC_DuneSystem(u2, v2, p.secondaryWavelength, s + 2u, p.secondarySinuosity, p.secondarySegmentation, 0.68) * p.secondaryHeight;
    float patch = saturate((MC_Fbm(wx / p.secondaryPatchScale - 7.7, wz / p.secondaryPatchScale + 3.1, 2, s + 401u) + 0.45) / 0.8);
    patch = patch * patch * (3.0 - 2.0 * patch);
    sec *= 0.15 + 0.85 * patch;

    // --- sand ripples hint ---
    float rip = MC_DuneSystem(u, v, p.rippleWavelength, s + 3u, 1.4, 0.3, 0.6) * p.rippleHeight;

    float dunes = (mega * sizeMod + sec * (0.45 + 0.55 * supply) + rip * (0.4 + 0.6 * supply)) * supply;

    // --- interdune base: gently rolling deflation plain ---
    float basePlain = MC_Fbm(wx / p.plainScale, wz / p.plainScale, 3, s + 301u) * p.plainAmplitude;
    return p.baseHeight + basePlain + dunes;
}

#endif
