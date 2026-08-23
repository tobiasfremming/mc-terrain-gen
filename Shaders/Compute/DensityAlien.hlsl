#ifndef MC_DENSITY_ALIEN_INCLUDED
#define MC_DENSITY_ALIEN_INCLUDED

#include "TerrainNoiseGPU.hlsl"

// GPU port of Scripts/Terrain/AlienVolumeField.cs's DensityAt(wx,wy,wz).
// Pure 3D SDF-style field -- returns density directly (no height/y split).
// Field order/names mirror the C# class's serialized fields exactly.
struct AlienParams
{
    float seed;
    float latticeFreq;
    float lattice1Amp;
    float lattice2Amp;
    float hillAmp;
    float wallDetailFreq;
    float enableTunnel;    // 0/1 (C# bool)
    float pathAmpA;
    float pathFreqA;
    float pathAmpB;
    float pathFreqB;
    float pathDriftAmp;
    float pathDriftFreq;
    float tunnelRadiusX;
    float tunnelRadiusY;
    float tunnelBlend;
    float pebbleScale;
    float pebbleAmp;
    float tnlPebbleOffset;
    float tunnelReinforce;
};

// NOTE: named distinctly from Canyon's smooth-max -- same shape of operation
// (a smooth union), but a DIFFERENT formula (see DensityCanyon.hlsl). Do not
// conflate the two.
float MC_SMaxAlien(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (a - b) / k);
    return b * (1.0 - h) + a * h + h * (1.0 - h) * k;
}

void MC_AlienPath(float z, AlienParams p, out float px, out float py)
{
    px = p.pathAmpA * sin(z * p.pathFreqA);
    py = p.pathAmpB * cos(z * p.pathFreqB) + p.pathDriftAmp * (sin(z * p.pathDriftFreq) - 1.0);
}

float EvaluateAlienDensity(float3 worldPos, AlienParams p)
{
    float wx = worldPos.x, wy = worldPos.y, wz = worldPos.z;
    uint s = (uint)(int)p.seed;

    float qx = wx * p.latticeFreq, qy = wy * p.latticeFreq, qz = wz * p.latticeFreq;
    float l1 = sin(qx) * cos(qy) + sin(qy) * cos(qz) + sin(qz) * cos(qx);
    float l2 = sin(qx * 1.5) * cos(qy * 1.5) + sin(qy * 1.5) * cos(qz * 1.5) + sin(qz * 1.5) * cos(qx * 1.5);
    float h = p.lattice1Amp * l1 + p.lattice2Amp * l2;

    float tx = MC_VNoise3(wx / p.pebbleScale, wy / p.pebbleScale, wz / p.pebbleScale, s + 8801u);

    float d = wy + h * p.hillAmp;

    float raw;
    if (p.enableTunnel > 0.5)
    {
        float qx2 = sin(wx * p.wallDetailFreq + h);
        float qy2 = sin(wy * p.wallDetailFreq + h);
        float qz2 = sin(wz * p.wallDetailFreq + h);
        float h2 = qx2 * qy2 * qz2;

        float px, py;
        MC_AlienPath(wz, p, px, py);
        float lx = wx - px, ly = wy - py;
        float dist = sqrt((lx / p.tunnelRadiusX) * (lx / p.tunnelRadiusX) + (ly / p.tunnelRadiusY) * (ly / p.tunnelRadiusY));
        float tnl = 1.5 - dist * 1.5 + h2 + (1.0 - tx) * p.pebbleAmp;

        raw = MC_SMaxAlien(d, tnl, p.tunnelBlend) - tx * p.tnlPebbleOffset + tnl * p.tunnelReinforce;
    }
    else
    {
        raw = d - tx * p.tnlPebbleOffset;
    }
    return -raw; // flip Shane's positive-outside convention to ours (positive = solid)
}

#endif
