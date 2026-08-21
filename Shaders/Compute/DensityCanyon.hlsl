#ifndef MC_DENSITY_CANYON_INCLUDED
#define MC_DENSITY_CANYON_INCLUDED

#include "TerrainNoiseGPU.hlsl"

// GPU port of Scripts/Terrain/CanyonVolumeField.cs, ported from Sample(p)
// (the canonical per-point definition -- AddDensityColumn is a CPU-only
// optimized variant of the same math that caches HeightAt/bridge-candidates
// once per column; this port deliberately re-evaluates everything per voxel
// instead, trading that CPU-side optimization for GPU-side parallelism. See
// the compute-shader thread-mapping note in TerrainDensityDispatch.hlsl).
// Field order/names mirror the C# class's serialized fields exactly.
struct CanyonParams
{
    float seed;
    float floorHeight;

    float thScale;
    float thLo;
    float thHi;

    float peakScale;
    float peakBase;
    float peakRange;
    float detailScale;
    float detailAmp;

    float rrScale;
    float rrAmp;

    float disScale;
    float disSquash;
    float disAmp;

    float enableSpires;    // 0/1
    float spireCellSize;
    float spireChance;
    float spireRadius;
    float spireHeight;
    float spireIrregularity;
    float spireMaxBaseHeight;
    float spireBaseFadeBand;

    float enableBridges;   // 0/1
    float bridgeCellSize;
    float bridgeChance;
    float bridgeMaxCenterHeight;
    float bridgeHalfSpanMin;
    float bridgeHalfSpanMax;
    float bridgeTubeRadiusMin;
    float bridgeTubeRadiusMax;
    float bridgeWalkwayHalfWidth;
    float bridgeLegEmbedMargin;
    float bridgeSmoothing;
};

struct CanyonBridgeCandidate
{
    float valid; // 0/1
    float cx, cz, perpX, perpZ, walkX, walkZ, halfSpan, archRise, tubeR, y0;
    float legBottomA, legBottomB;
};

// Forward declare (HeightAt is needed by both GatherBridgeCandidates and Sample).
float MC_CanyonHeightAt(float wx, float wz, CanyonParams p);

float MC_CanyonSpireBoost(float wx, float wz, CanyonParams p)
{
    if (p.enableSpires <= 0.5) return 0.0;
    uint s = (uint)(int)p.seed;
    float boost = 0.0;
    int icx = (int)floor(wx / p.spireCellSize);
    int icz = (int)floor(wz / p.spireCellSize);
    for (int dz = -1; dz <= 1; dz++)
    for (int dx = -1; dx <= 1; dx++)
    {
        int gx = icx + dx, gz = icz + dz;
        uint ux = (uint)gx, uz = (uint)gz;
        float roll = (float)MC_Hash(ux, uz, s + 9001u) * (1.0 / 4294967296.0);
        if (roll >= p.spireChance) continue;
        float jx = (float)MC_Hash(ux, uz, s + 9002u) * (1.0 / 4294967296.0);
        float jz = (float)MC_Hash(ux, uz, s + 9003u) * (1.0 / 4294967296.0);
        float rj = (float)MC_Hash(ux, uz, s + 9004u) * (1.0 / 4294967296.0);
        float hj = (float)MC_Hash(ux, uz, s + 9005u) * (1.0 / 4294967296.0);
        float cx = (gx + 0.15 + 0.7 * jx) * p.spireCellSize;
        float cz = (gz + 0.15 + 0.7 * jz) * p.spireCellSize;
        float radius = p.spireRadius * (0.6 + 0.8 * rj);
        float height = p.spireHeight * (0.55 + 0.9 * hj);
        float wob = 1.0 + p.spireIrregularity * MC_GNoise(wx / (radius * 0.8), wz / (radius * 0.8), s + 9006u);
        float dx2 = wx - cx, dz2 = wz - cz;
        float dist = sqrt(dx2 * dx2 + dz2 * dz2) / max(wob, 0.35);
        float t = 1.0 - MC_Smoothstep(radius * 0.25, radius, dist);
        boost = max(boost, t * height);
    }
    return boost;
}

float MC_CanyonThAt(float wx, float wz, CanyonParams p)
{
    uint s = (uint)(int)p.seed;
    float thn = MC_Fbm3(wx / p.thScale, 0.0, wz / p.thScale, 3, s + 11u) * 1.06;
    return MC_Smoothstep(p.thLo, p.thHi, thn);
}

float MC_CanyonHeightAt(float wx, float wz, CanyonParams p)
{
    uint s = (uint)(int)p.seed;
    float th = MC_CanyonThAt(wx, wz, p);

    float ridge = MC_RidgedFbm(wx / p.peakScale, wz / p.peakScale, 4, s + 501u, 0.5, 2.0);
    float massif = p.peakBase + p.peakRange * ridge;
    float detail = MC_RidgedFbm(wx / p.detailScale, wz / p.detailScale, 3, s + 733u, 0.5, 2.0);
    massif += p.detailAmp * detail;

    float rr = MC_Smoothstep(0.1, 0.5, MC_Fbm(wx / p.rrScale, wz / p.rrScale, 2, s + 21u) * 0.5 + 0.5);

    float hNoSpire = p.floorHeight + massif * (1.0 - th) + p.rrAmp * 0.3 * rr;
    if (p.enableSpires <= 0.5) return hNoSpire;

    float spireRaw = MC_CanyonSpireBoost(wx, wz, p);
    if (spireRaw <= 0.0) return hNoSpire;

    float heightAboveFloor = hNoSpire - p.floorHeight;
    float baseGate = 1.0 - MC_Smoothstep(p.spireMaxBaseHeight - p.spireBaseFadeBand, p.spireMaxBaseHeight, heightAboveFloor);
    float peakCap = p.floorHeight + p.peakBase + p.peakRange + p.detailAmp;
    float spireTop = hNoSpire + spireRaw * baseGate;
    return min(spireTop, peakCap);
}

float MC_CanyonErosion(float wx, float y, float wz, CanyonParams p)
{
    uint s = (uint)(int)p.seed;
    return MC_Fbm3(wx / p.disScale, y * p.disSquash / p.disScale, wz / p.disScale, 3, s + 39323u);
}

void MC_CanyonGatherBridgeCandidates(float wx, float wz, CanyonParams p, out CanyonBridgeCandidate cands[9])
{
    uint s = (uint)(int)p.seed;
    int icx = (int)floor(wx / p.bridgeCellSize);
    int icz = (int)floor(wz / p.bridgeCellSize);
    int idx = 0;
    for (int dz = -1; dz <= 1; dz++)
    for (int dx = -1; dx <= 1; dx++)
    {
        int gx = icx + dx, gz = icz + dz;
        uint ux = (uint)gx, uz = (uint)gz;
        CanyonBridgeCandidate c;
        c.valid = 0.0; c.cx = 0; c.cz = 0; c.perpX = 0; c.perpZ = 0; c.walkX = 0; c.walkZ = 0;
        c.halfSpan = 0; c.archRise = 0; c.tubeR = 0; c.y0 = 0; c.legBottomA = 0; c.legBottomB = 0;

        float roll = (float)MC_Hash(ux, uz, s + 5001u) * (1.0 / 4294967296.0);
        if (roll < p.bridgeChance)
        {
            float jx = (float)MC_Hash(ux, uz, s + 5002u) * (1.0 / 4294967296.0);
            float jz = (float)MC_Hash(ux, uz, s + 5003u) * (1.0 / 4294967296.0);
            float spanJ = (float)MC_Hash(ux, uz, s + 5005u) * (1.0 / 4294967296.0);
            float riseJ = (float)MC_Hash(ux, uz, s + 5006u) * (1.0 / 4294967296.0);
            float tubeJ = (float)MC_Hash(ux, uz, s + 5007u) * (1.0 / 4294967296.0);

            float cx = (gx + 0.2 + 0.6 * jx) * p.bridgeCellSize;
            float cz = (gz + 0.2 + 0.6 * jz) * p.bridgeCellSize;

            float hCenter = MC_CanyonHeightAt(cx, cz, p);
            if (hCenter < p.bridgeMaxCenterHeight)
            {
                float halfSpan = p.bridgeHalfSpanMin + (p.bridgeHalfSpanMax - p.bridgeHalfSpanMin) * spanJ;
                float archRise = halfSpan * (0.5 + 0.4 * riseJ);
                float tubeR = p.bridgeTubeRadiusMin + (p.bridgeTubeRadiusMax - p.bridgeTubeRadiusMin) * tubeJ;
                float y0 = hCenter + 4.0;

                const float gradEps = 12.0;
                float hXp = MC_CanyonHeightAt(cx + gradEps, cz, p), hXm = MC_CanyonHeightAt(cx - gradEps, cz, p);
                float hZp = MC_CanyonHeightAt(cx, cz + gradEps, p), hZm = MC_CanyonHeightAt(cx, cz - gradEps, p);
                float gxv = hXp - hXm, gzv = hZp - hZm;
                float glen = sqrt(gxv * gxv + gzv * gzv);
                float perpX, perpZ;
                if (glen > 0.05)
                {
                    perpX = gxv / glen; perpZ = gzv / glen;
                }
                else
                {
                    float angJ = (float)MC_Hash(ux, uz, s + 5008u) * (1.0 / 4294967296.0);
                    float ang = angJ * (3.14159265359 * 2.0);
                    perpX = cos(ang); perpZ = sin(ang);
                }

                float legAx = cx + perpX * halfSpan, legAz = cz + perpZ * halfSpan;
                float legBx = cx - perpX * halfSpan, legBz = cz - perpZ * halfSpan;
                float hLegA = MC_CanyonHeightAt(legAx, legAz, p);
                float hLegB = MC_CanyonHeightAt(legBx, legBz, p);

                c.valid = 1.0;
                c.cx = cx; c.cz = cz;
                c.perpX = perpX; c.perpZ = perpZ;
                c.walkX = -perpZ; c.walkZ = perpX;
                c.halfSpan = halfSpan; c.archRise = archRise; c.tubeR = tubeR; c.y0 = y0;
                c.legBottomA = min(0.0, (hLegA - y0) - p.bridgeLegEmbedMargin);
                c.legBottomB = min(0.0, (hLegB - y0) - p.bridgeLegEmbedMargin);
            }
        }
        cands[idx] = c;
        idx++;
    }
}

float MC_CanyonSegmentDist(float u, float v, float segU, float vLo, float vHi)
{
    float cv = clamp(v, vLo, vHi);
    float du = u - segU, dv = v - cv;
    return sqrt(du * du + dv * dv);
}

float MC_CanyonBridgeContribution(CanyonBridgeCandidate c, float wx, float y, float wz, CanyonParams p)
{
    float u = (wx - c.cx) * c.perpX + (wz - c.cz) * c.perpZ;
    float w = (wx - c.cx) * c.walkX + (wz - c.cz) * c.walkZ;
    float v = y - c.y0;

    float cY = (c.archRise * c.archRise - c.halfSpan * c.halfSpan) / (2.0 * c.archRise);
    float R = sqrt(c.halfSpan * c.halfSpan + cY * cY);
    float dArc = v > -c.tubeR * 1.2 ? abs(sqrt(u * u + (v - cY) * (v - cY)) - R) : 3.402823e38;
    float dLegA = MC_CanyonSegmentDist(u, v, c.halfSpan, c.legBottomA, 0.0);
    float dLegB = MC_CanyonSegmentDist(u, v, -c.halfSpan, c.legBottomB, 0.0);
    float dShape = min(min(dArc, dLegA), dLegB);

    float wExtra = max(0.0, abs(w) - p.bridgeWalkwayHalfWidth);
    float distExtruded = sqrt(dShape * dShape + wExtra * wExtra);
    return c.tubeR - distExtruded;
}

// NOTE: named distinctly from Alien's smooth-max -- same shape of operation
// (a smooth union), but a DIFFERENT formula (see DensityAlien.hlsl). Do not
// conflate the two.
float MC_SMaxCanyon(float a, float b, float k)
{
    float h = saturate(0.5 - 0.5 * (b - a) / k);
    return a * h + b * (1.0 - h) + k * h * (1.0 - h);
}

float EvaluateCanyonDensity(float3 worldPos, CanyonParams p)
{
    float wx = worldPos.x, wy = worldPos.y, wz = worldPos.z;
    float d = MC_CanyonHeightAt(wx, wz, p) - wy - MC_CanyonErosion(wx, wy, wz, p) * p.disAmp;

    if (p.enableBridges > 0.5)
    {
        CanyonBridgeCandidate cands[9];
        MC_CanyonGatherBridgeCandidates(wx, wz, p, cands);
        for (int k = 0; k < 9; k++)
        {
            if (cands[k].valid > 0.5)
                d = MC_SMaxCanyon(d, MC_CanyonBridgeContribution(cands[k], wx, wy, wz, p), p.bridgeSmoothing);
        }
    }
    return d;
}

#endif
