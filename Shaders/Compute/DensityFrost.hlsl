#ifndef MC_DENSITY_FROST_INCLUDED
#define MC_DENSITY_FROST_INCLUDED

#include "TerrainNoiseGPU.hlsl"

// GPU port of Scripts/Terrain/FrostVolumeField.cs's HeightAt(wx,wz). Returns
// HEIGHT (not density) -- matches HeightDensityField.Sample's convention of
// `density = HeightAt(x,z) - y`, applied by the caller (EvaluateLeafDensity
// in DensityBiomeBlend.hlsl). Field order/names mirror the C# class's
// ToGpuLeafParams exactly; keep the two in lockstep.
struct FrostParams
{
    float seed;
    float baseHeight;
    float windDegrees;
    float plainScale;
    float plainAmplitude;
    float peakScale;
    float peakOctaves;
    float peakAmp;
    float sastrugiEnabled;
    float sastrugiWavelengthAcross;
    float sastrugiWavelengthAlong;
    float sastrugiOctaves;
    float sastrugiAmp;
    float sastrugiAsymmetry;
    float permafrostEnabled;
    float polygonScale;
    float polygonTroughDepth;
    float polygonEdgeWidth;
    float crevassesEnabled;
    float crevasseScale;
    float crevasseDepth;
    float crevasseEdgeWidth;
    float crevassePatchScale;
    float hummocksEnabled;
    float hummockScale;
    float hummockAmp;
};

float EvaluateFrostHeight(float wx, float wz, FrostParams p)
{
    uint s = (uint)(int)p.seed;
    float h = p.baseHeight;

    // broad rolling base (interdune-plain equivalent)
    h += MC_Fbm(wx / p.plainScale, wz / p.plainScale, 3, s + 11u) * p.plainAmplitude;

    // frozen peaks / hills -- reuses the same ridged-fbm machinery as Canyon's
    // mountains, just at Frost's own scale/amplitude
    h += MC_RidgedFbm(wx / p.peakScale, wz / p.peakScale, (int)p.peakOctaves, s + 23u, 0.5, 2.0) * p.peakAmp;

    // sastrugi: wind-carved ridges. Anisotropic ridged noise -- long
    // wavelength along wind (ridges run in the wind direction), short
    // wavelength across wind (many parallel ridges) -- with a cheap
    // directional-derivative proxy (two single-octave MC_GNoise taps,
    // instead of a second full multi-octave RidgedFbm call) for the
    // sharp-lee/gentle-windward asymmetry real sastrugi has.
    if (p.sastrugiEnabled > 0.5)
    {
        float wr = radians(p.windDegrees);
        float cu = cos(wr), su = sin(wr);
        float u = wx * cu + wz * su;   // along wind
        float v = -wx * su + wz * cu;  // across wind

        int soct = (int)p.sastrugiOctaves;
        float ridge = MC_RidgedFbm(v / p.sastrugiWavelengthAcross, u / p.sastrugiWavelengthAlong, soct, s + 41u, 0.5, 2.0);
        float nHere = MC_GNoise(v / p.sastrugiWavelengthAcross, u / p.sastrugiWavelengthAlong, s + 41u);
        float nAhead = MC_GNoise(v / p.sastrugiWavelengthAcross, (u + p.sastrugiWavelengthAcross * 0.35) / p.sastrugiWavelengthAlong, s + 41u);
        ridge += p.sastrugiAsymmetry * 0.5 * (nHere - nAhead);
        h += ridge * p.sastrugiAmp;
    }

    // permafrost polygons: Worley cell-boundary troughs (patterned ground) --
    // f2-f1 is ~0 exactly on a cell edge, so 1-smoothstep(...) carves a thin
    // trough network along the polygon boundaries.
    if (p.permafrostEnabled > 0.5)
    {
        float f1, f2;
        MC_Worley2(wx / p.polygonScale, wz / p.polygonScale, s + 61u, f1, f2);
        float trough = 1.0 - MC_Smoothstep(0.0, p.polygonEdgeWidth, f2 - f1);
        h -= trough * p.polygonTroughDepth;
    }

    // crevasse fields: same Worley-edge trick as the polygons, but finer and
    // deeper, and gated by a slow patch mask so cracked glacier-like zones
    // appear regionally rather than blanketing the whole biome.
    if (p.crevassesEnabled > 0.5)
    {
        float patch = MC_Fbm(wx / p.crevassePatchScale + 5.2, wz / p.crevassePatchScale - 2.9, 2, s + 71u);
        patch = saturate((patch + 0.4) / 0.7);
        patch = patch * patch * (3.0 - 2.0 * patch);

        if (patch > 0.001)
        {
            float f1, f2;
            MC_Worley2(wx / p.crevasseScale, wz / p.crevasseScale, s + 83u, f1, f2);
            float crack = 1.0 - MC_Smoothstep(0.0, p.crevasseEdgeWidth, f2 - f1);
            h -= crack * p.crevasseDepth * patch;
        }
    }

    // frost heave hummocks: small bumpy ground texture
    if (p.hummocksEnabled > 0.5)
        h += MC_Fbm(wx / p.hummockScale, wz / p.hummockScale, 2, s + 97u) * p.hummockAmp;

    return h;
}

#endif
