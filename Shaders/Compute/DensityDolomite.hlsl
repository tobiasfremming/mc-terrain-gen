#ifndef MC_DENSITY_DOLOMITE_INCLUDED
#define MC_DENSITY_DOLOMITE_INCLUDED

#include "TerrainNoiseGPU.hlsl"

// GPU port of Scripts/Terrain/DolomiteVolumeField.cs's HeightAt(wx,wz).
// Returns HEIGHT (not density); the caller subtracts y. Field order/names
// mirror the C# class's ToGpuLeafParams exactly; keep the two in lockstep.
struct DolomiteParams
{
    float seed;
    float baseHeight;
    float valleyScale;
    float valleyAmp;
    float meadowScale;
    float meadowAmp;
    float massifScale;
    float massifOffset;
    float warpScale;
    float warpAmp;
    float towerScale;
    float towerAmp;
    float screeLo;
    float wallLo;
    float wallHi;
    float screeHeight;
    float screeExp;
    float wallHeight;
    float ledgeCount;
    float ledgeSharp;
    float ledgeJitter;
    float ledgeJitterScale;
    float ridgeScale;
    float ridgeOctaves;
    float ridgeAmp;
    float spireScale;
    float spireOctaves;
    float spireAmp;
};

float MC_DolomiteTerrace(float u, int n, float sharp)
{
    if (n <= 0) return u;
    float v = u * n;
    float k = floor(v);
    float f = v - k;
    float riser = MC_Smoothstep(sharp, 1.0, f);
    return (k + riser) / n;
}

float EvaluateDolomiteHeight(float wx, float wz, DolomiteParams p, float fw)
{
    uint s = (uint)(int)p.seed;

    // valley floor
    float h = p.baseHeight
            + MC_Fbm(wx / p.valleyScale, wz / p.valleyScale, 2, s + 3u, fw / p.valleyScale) * p.valleyAmp
            + MC_Fbm(wx / p.meadowScale, wz / p.meadowScale, 4, s + 11u, fw / p.meadowScale) * p.meadowAmp;

    // massif mask, warped at two scales
    float t = MC_Fbm(wx / p.massifScale, wz / p.massifScale, 3, s + 1u, fw / p.massifScale) + p.massifOffset;
    t += p.warpAmp * MC_Fbm(wx / p.warpScale, wz / p.warpScale, 3, s + 7u, fw / p.warpScale);
    t += p.towerAmp * MC_Fbm(wx / p.towerScale, wz / p.towerScale, 2, s + 13u, fw / p.towerScale);

    float ss = MC_Smoothstep(p.screeLo, p.wallLo, t);
    float sw = MC_Smoothstep(p.wallLo, p.wallHi, t);

    // scree apron
    h += p.screeHeight * pow(ss, p.screeExp);

    // terraced wall
    int ledges = (int)p.ledgeCount;
    float jitter = (ledges > 0 && p.ledgeJitter > 0.0)
        ? p.ledgeJitter / ledges * MC_Fbm(wx / p.ledgeJitterScale, wz / p.ledgeJitterScale, 2, s + 17u, fw / p.ledgeJitterScale)
          * 4.0 * sw * (1.0 - sw)
        : 0.0;
    float terraced = MC_DolomiteTerrace(sw + jitter, ledges, p.ledgeSharp);
    float ledgeFade = ledges > 0 ? MC_DetailFade(fw, p.wallHeight / ledges * 0.2) : 0.0;
    h += p.wallHeight * lerp(sw, terraced, ledgeFade);

    // summits
    if (sw > 0.0)
    {
        float summit = MC_RidgedFbm(wx / p.ridgeScale, wz / p.ridgeScale, (int)p.ridgeOctaves, s + 23u, 0.5, 2.0, fw / p.ridgeScale) * p.ridgeAmp
                     + MC_RidgedFbm(wx / p.spireScale, wz / p.spireScale, (int)p.spireOctaves, s + 29u, 0.5, 2.0, fw / p.spireScale) * p.spireAmp;
        h += sw * summit;
    }
    return h;
}

#endif
