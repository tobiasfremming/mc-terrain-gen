#ifndef MC_DENSITY_ERODED_INCLUDED
#define MC_DENSITY_ERODED_INCLUDED

#include "TerrainNoiseGPU.hlsl"
#include "ErosionNoiseGPU.hlsl"

// GPU port of Scripts/Terrain/ErodedHeightField.cs's HeightAt(wx, wz).
// Returns HEIGHT (not density); the caller subtracts y. Field order/names
// mirror ErodedGpuParams exactly; keep the two in lockstep.
struct ErodedParams
{
    float seed;
    float baseHeight;
    float baseScale;
    float baseOctaves;
    float baseAmp;
    float fadeRange;
    float heightOffsetX;
    float heightOffsetY;
    float eScale;
    float eStrength;
    float eGullyWeight;
    float eDetail;
    float eRoundingX;
    float eRoundingY;
    float eRoundingZ;
    float eRoundingW;
    float eOnsetX;
    float eOnsetY;
    float eOnsetZ;
    float eOnsetW;
    float eAssumedSlopeX;
    float eAssumedSlopeY;
    float eCellScale;
    float eOctaves;
    float eGain;
    float eLacunarity;
    float eNormalization;
    float sandEnabled;
    float sandLevel;
    float sandScale;
    float sandOctaves;
    float sandAmp;
    float sandBlend;
    // massif base (ErodedHeightField.BaseMode.Massif), appended
    float baseMode;
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
    float ridgeScale;
    float ridgeAmp;
    float spireScale;
    float spireAmp;
    float erosionOnsetRelief;
    float ledgeAmp;
    float ledgeSpacing;
};

// smoothstep and its derivative with respect to t
float MC_ErodedDSmooth(float a, float b, float t, out float dt)
{
    float u = saturate((t - a) / (b - a));
    dt = 6.0 * u * (1.0 - u) / (b - a);
    return u * u * (3.0 - 2.0 * u);
}

// (1 - |fbm|)^2 with its gradient -- mirrors ErodedHeightField.RidgedD
float3 MC_ErodedRidgedD(float x, float y, int octaves, uint seed, float fw)
{
    float3 n = MC_FbmD(x, y, octaves, seed, fw);
    float a = abs(n.x);
    float d = -2.0 * (1.0 - a) * sign(n.x);
    return float3((1.0 - a) * (1.0 - a), d * n.y, d * n.z);
}

// Mirrors ErodedHeightField.MassifBase: x height, yz slope; floor and relief out.
float3 MC_ErodedMassifBase(float wx, float wz, ErodedParams p, float fw, uint s, out float floorH, out float relief)
{
    float3 f1 = MC_FbmD(wx / p.valleyScale, wz / p.valleyScale, 2, s + 3u, fw / p.valleyScale);
    float3 f2 = MC_FbmD(wx / p.meadowScale, wz / p.meadowScale, 4, s + 11u, fw / p.meadowScale);
    floorH = p.baseHeight + f1.x * p.valleyAmp + f2.x * p.meadowAmp;
    float fx = f1.y * p.valleyAmp / p.valleyScale + f2.y * p.meadowAmp / p.meadowScale;
    float fz = f1.z * p.valleyAmp / p.valleyScale + f2.z * p.meadowAmp / p.meadowScale;

    float3 m = MC_FbmD(wx / p.massifScale, wz / p.massifScale, 3, s + 1u, fw / p.massifScale);
    float3 w = MC_FbmD(wx / p.warpScale, wz / p.warpScale, 3, s + 7u, fw / p.warpScale);
    float3 tw = MC_FbmD(wx / p.towerScale, wz / p.towerScale, 2, s + 13u, fw / p.towerScale);
    float t = m.x + p.massifOffset + p.warpAmp * w.x + p.towerAmp * tw.x;
    float tx = m.y / p.massifScale + p.warpAmp * w.y / p.warpScale + p.towerAmp * tw.y / p.towerScale;
    float tz = m.z / p.massifScale + p.warpAmp * w.z / p.warpScale + p.towerAmp * tw.z / p.towerScale;

    float dss, dsw;
    float ss = MC_ErodedDSmooth(p.screeLo, p.wallLo, t, dss);
    float sw = MC_ErodedDSmooth(p.wallLo, p.wallHi, t, dsw);

    float apron = p.screeHeight * pow(ss, p.screeExp);
    float dapron = p.screeHeight * p.screeExp * pow(max(ss, 1e-9), p.screeExp - 1.0) * dss;

    float3 r1 = MC_ErodedRidgedD(wx / p.ridgeScale, wz / p.ridgeScale, 3, s + 23u, fw / p.ridgeScale);
    float3 r2 = MC_ErodedRidgedD(wx / p.spireScale, wz / p.spireScale, 3, s + 29u, fw / p.spireScale);
    float summit = p.ridgeAmp * r1.x + p.spireAmp * r2.x;
    float sx = p.ridgeAmp * r1.y / p.ridgeScale + p.spireAmp * r2.y / p.spireScale;
    float sz = p.ridgeAmp * r1.z / p.ridgeScale + p.spireAmp * r2.z / p.spireScale;

    float top = p.wallHeight + summit;
    float h = floorH + apron + sw * top;
    relief = h - floorH;
    return float3(h,
                  fx + dapron * tx + dsw * tx * top + sw * sx,
                  fz + dapron * tz + dsw * tz * top + sw * sz);
}

float MC_ErodedSmoothMax(float a, float b, float k)
{
    if (k <= 0.0) return max(a, b);
    float h = saturate(0.5 + 0.5 * (a - b) / k);
    return b + (a - b) * h + k * h * (1.0 - h);
}

float EvaluateErodedHeight(float wx, float wz, ErodedParams p, float fw)
{
    uint s = (uint)(int)p.seed;

    MC_ErosionParams e;
    e.scale = p.eScale;
    e.strength = p.eStrength;
    e.gullyWeight = p.eGullyWeight;
    e.detail = p.eDetail;
    e.rounding = float4(p.eRoundingX, p.eRoundingY, p.eRoundingZ, p.eRoundingW);
    e.onset = float4(p.eOnsetX, p.eOnsetY, p.eOnsetZ, p.eOnsetW);
    e.assumedSlope = float2(p.eAssumedSlopeX, p.eAssumedSlopeY);
    e.cellScale = p.eCellScale;
    e.octaves = (int)p.eOctaves;
    e.gain = p.eGain;
    e.lacunarity = p.eLacunarity;
    e.normalization = p.eNormalization;
    // ONE erosion-filter call for both base modes: the filter is the bulk of
    // this leaf's code and every leaf is inlined into the biome blend, whose
    // size is what made FXC time out on TerrainMesh.compute's CSTransition
    // before. Only the (small) base and post-processing branch per mode.
    // Written as one return rather than two so FXC's flow analysis does not
    // flag "potentially uninitialized" up the call chain (see the note on
    // EvaluateLeafDensity in DensityBiomeBlend.hlsl).
    bool massif = p.baseMode > 0.5;
    float3 n;
    float fadeTarget, relief = 0.0, floorH = 0.0;
    [branch]
    if (massif)
    {
        n = MC_ErodedMassifBase(wx, wz, p, fw, s, floorH, relief);
        float top0 = p.screeHeight + p.wallHeight;
        // 0 everywhere (symmetric gullies, no baseline shift), rising to +1
        // over the upper wall and summit for sharp crests
        fadeTarget = MC_Smoothstep(p.screeHeight + 0.5 * p.wallHeight, top0, relief);
    }
    else
    {
        n = MC_FbmD(wx / p.baseScale, wz / p.baseScale, (int)p.baseOctaves, s + 1u, fw / p.baseScale);
        n.x *= p.baseAmp;
        n.yz *= p.baseAmp / p.baseScale;
        fadeTarget = clamp(n.x / (p.baseAmp * p.fadeRange), -1.0, 1.0);
    }

    float ridge;
    float4 d = MC_ErosionFilter(float2(wx, wz), n, fadeTarget, e, s + 101u, fw, ridge);

    float h;
    [branch]
    if (massif)
    {
        // the meadow stays a meadow: erosion fades in across the apron foot
        float emask = MC_Smoothstep(0.0, p.erosionOnsetRelief, relief);
        h = n.x + d.x * emask;
        if (p.ledgeAmp > 0.0)
        {
            float top0 = p.screeHeight + p.wallHeight;
            float u = h / p.ledgeSpacing;
            float saw = (u - floor(u) - 0.5) * 2.0;
            float band = MC_Smoothstep(p.screeHeight * 0.8, p.screeHeight + p.wallHeight * 0.25, relief)
                       * (1.0 - MC_Smoothstep(top0 * 0.9, top0 * 1.1, relief));
            h -= p.ledgeAmp * saw * band;
        }
    }
    else
    {
        float offset = lerp(p.heightOffsetX, -fadeTarget, p.heightOffsetY) * d.w;
        h = p.baseHeight + n.x + d.x + offset;
        if (p.sandEnabled > 0.5)
        {
            float plain = p.sandLevel + MC_Fbm(wx / p.sandScale, wz / p.sandScale, (int)p.sandOctaves, s + 5u, fw / p.sandScale) * p.sandAmp;
            h = MC_ErodedSmoothMax(h, plain, p.sandBlend);
        }
    }
    return h;
}

#endif
