using System;
using UnityEngine;

// Canyon biome as a PURE 3D density function (SDF-style — no height API).
// Faithful port of the CONSTRUCTION in Inigo Quilez's "Canyon" shadertoy,
// reimplemented on our own noise (his code is license-restricted; techniques
// only):
//
//   massif  = ridged fbm (jagged mountain peaks) + finer ridged detail
//           + sparse rock spires
//   th      = smoothstep window over a large-scale fbm -> where mountain
//             gives way to canyon/valley (a NARROW window = sheer cliffs)
//   h(x,z)  = floorHeight + massif*(1-th) + ripple          <- "terrain()"
//   density = h - y - disAmp * fbm3(p * (1, disSquash, 1) / disScale)  <- dis()
//           smooth-unioned with any natural rock arches nearby
//
// h is deliberately a function of (x, z) only — that is what guarantees
// mountains of ANY height stay attached to the ground instead of detaching
// into floating islands (every "3D layout" attempt that let the macro shape
// itself depend on y produced floaters under 3D flood-fill verification,
// because a column's solid/air crossings stopped being predictable once the
// dominant term swung with height). The ONLY y-dependent terms are the
// bounded erosion/striation displacement and the explicit arch primitive
// below — exactly IQ's own two-stage construction (terrain() then a separate
// small 3D dis()) — which is what carves real overhangs and undercuts into
// the cliffs without ever being large enough to disconnect rock from the
// ground.
[CreateAssetMenu(fileName = "CanyonField", menuName = "Marching Cubes/Volume (Canyon)")]
public class CanyonVolumeField : DensityField
{
    [Header("World")]
    public int seed = 777;
    [Tooltip("Canyon/valley floor height — keep near the desert base so sand collects low.")]
    public float floorHeight = 2f;

    [Header("Mountain / canyon layout")]
    [Tooltip("Scale (m) of the region noise deciding where mountains rise vs. where canyons cut through.")]
    public float thScale = 900f;
    [Tooltip("Narrow window = sheer canyon cliffs; wide window = gentle foothills.")]
    public float thLo = 0.47f;
    public float thHi = 0.52f;

    [Header("Mountain massif (ridged noise -> jagged peaks)")]
    public float peakScale = 420f;
    public float peakBase = 15f;
    public float peakRange = 140f;
    [Tooltip("Finer ridged detail layered on top for jaggedness/spires.")]
    public float detailScale = 90f;
    public float detailAmp = 22f;

    [Header("Canyon floor ripple")]
    public float rrScale = 30f;
    public float rrAmp = 6f;

    [Header("3D erosion displacement (IQ's dis()) — THE eroded-cliff look")]
    public float disScale = 26f;
    [Tooltip("Vertical squash: higher = flatter strata striations.")]
    public float disSquash = 3f;
    public float disAmp = 13f;

    [Header("Rock spires / pillars")]
    [Tooltip("Erosion remnants: spawn near the valley floor / mountain base, never on the peaks, and never above the tallest possible mountain.")]
    public bool enableSpires = true;
    [Tooltip("Average spacing (m) between candidate spire sites.")]
    public float spireCellSize = 130f;
    [Range(0f, 1f)] public float spireChance = 0.30f;
    public float spireRadius = 11f;
    public float spireHeight = 55f;
    [Tooltip("0 = perfectly round pillars; higher = eroded/irregular silhouette.")]
    [Range(0f, 1f)] public float spireIrregularity = 0.35f;
    [Tooltip("Spires only form where the local terrain is within this height above floorHeight (i.e. the valley floor / cliff base) — never on mountain tops.")]
    public float spireMaxBaseHeight = 45f;
    [Tooltip("Width of the smooth fade-out band below spireMaxBaseHeight.")]
    public float spireBaseFadeBand = 15f;

    [Header("Natural rock bridges / arches")]
    [Tooltip("Fixed-cost placement: no per-site search or validation. Legs always extend down to whatever terrain is really there, so a bridge can never float — it just looks more or less dramatic depending on what it happened to land near.")]
    public bool enableBridges = true;
    [Tooltip("Average spacing (m) between candidate bridge sites — bridges are rare landmarks, not every cell qualifies.")]
    public float bridgeCellSize = 260f;
    [Range(0f, 1f)] public float bridgeChance = 0.02f;
    [Tooltip("Site center must be this close to the local valley floor (a cheap one-shot check, not a search — keeps bridges out of solid mountainsides).")]
    public float bridgeMaxCenterHeight = 40f;
    public float bridgeHalfSpanMin = 15f;
    public float bridgeHalfSpanMax = 30f;
    public float bridgeTubeRadiusMin = 2.5f;
    public float bridgeTubeRadiusMax = 4.5f;
    [Tooltip("Half-width of the walkway across the span.")]
    public float bridgeWalkwayHalfWidth = 3.5f;
    [Tooltip("How far below the springline a leg is allowed to dip to reach real ground.")]
    public float bridgeLegEmbedMargin = 3f;
    [Tooltip("Blend radius welding the arch into the terrain — kept tight so the arch reads as a distinct span, not a ballooning overhang.")]
    public float bridgeSmoothing = 3f;

    float SpireBoost(float wx, float wz)
    {
        if (!enableSpires) return 0f;
        uint s = unchecked((uint)seed);
        float boost = 0f;
        long icx = (long)Mathf.Floor(wx / spireCellSize);
        long icz = (long)Mathf.Floor(wz / spireCellSize);
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            long gx = icx + dx, gz = icz + dz;
            uint ux = unchecked((uint)gx), uz = unchecked((uint)gz);
            float roll = TerrainNoise.Hash(ux, uz, s + 9001u) * (1f / 4294967296f);
            if (roll >= spireChance) continue;
            float jx = TerrainNoise.Hash(ux, uz, s + 9002u) * (1f / 4294967296f);
            float jz = TerrainNoise.Hash(ux, uz, s + 9003u) * (1f / 4294967296f);
            float rj = TerrainNoise.Hash(ux, uz, s + 9004u) * (1f / 4294967296f);
            float hj = TerrainNoise.Hash(ux, uz, s + 9005u) * (1f / 4294967296f);
            float cx = (gx + 0.15f + 0.7f * jx) * spireCellSize;
            float cz = (gz + 0.15f + 0.7f * jz) * spireCellSize;
            float radius = spireRadius * (0.6f + 0.8f * rj);
            float height = spireHeight * (0.55f + 0.9f * hj);
            float wob = 1f + spireIrregularity * TerrainNoise.GNoise(wx / (radius * 0.8f), wz / (radius * 0.8f), s + 9006u);
            float dx2 = wx - cx, dz2 = wz - cz;
            float dist = Mathf.Sqrt(dx2 * dx2 + dz2 * dz2) / Mathf.Max(wob, 0.35f);
            float t = 1f - TerrainNoise.Smoothstep(radius * 0.25f, radius, dist);
            boost = Mathf.Max(boost, t * height);
        }
        return boost;
    }

    // th alone: 0 = inside the mountain, 1 = inside the canyon/valley. This is
    // the cheap classifier bridges use to scan for a facing wall pair without
    // paying for the full ridged-noise massif + spire computation below.
    float ThAt(float wx, float wz)
    {
        uint s = unchecked((uint)seed);
        float thn = TerrainNoise.Fbm3(wx / thScale, 0f, wz / thScale, 3, s + 11u) * 1.06f;
        return TerrainNoise.Smoothstep(thLo, thHi, thn);
    }

    float HeightAt(float wx, float wz)
    {
        uint s = unchecked((uint)seed);
        float th = ThAt(wx, wz);   // 1 = canyon/valley, 0 = mountain

        float ridge = TerrainNoise.RidgedFbm(wx / peakScale, wz / peakScale, 4, s + 501u);
        float massif = peakBase + peakRange * ridge;
        float detail = TerrainNoise.RidgedFbm(wx / detailScale, wz / detailScale, 3, s + 733u);
        massif += detailAmp * detail;

        float rr = TerrainNoise.Smoothstep(0.1f, 0.5f,
                       TerrainNoise.Fbm(wx / rrScale, wz / rrScale, 2, s + 21u) * 0.5f + 0.5f);

        float hNoSpire = floorHeight + massif * (1f - th) + rrAmp * 0.3f * rr;
        if (!enableSpires) return hNoSpire;

        // Spires are erosion remnants: they stand where the terrain is
        // already low (valley floor / cliff base), fading out smoothly as
        // the local ground climbs toward spireMaxBaseHeight, and their
        // absolute world height is hard-capped at the tallest possible
        // mountain peak so a pillar can never read as taller than the
        // mountains around it.
        float spireRaw = SpireBoost(wx, wz);
        if (spireRaw <= 0f) return hNoSpire;

        float heightAboveFloor = hNoSpire - floorHeight;
        float baseGate = 1f - TerrainNoise.Smoothstep(spireMaxBaseHeight - spireBaseFadeBand, spireMaxBaseHeight, heightAboveFloor);
        float peakCap = floorHeight + peakBase + peakRange + detailAmp;
        float spireTop = hNoSpire + spireRaw * baseGate;
        return Mathf.Min(spireTop, peakCap);
    }

    float Erosion(float wx, float y, float wz)
    {
        uint s = unchecked((uint)seed);
        return TerrainNoise.Fbm3(wx / disScale, y * disSquash / disScale, wz / disScale, 3, s + 39323u);
    }

    // A candidate natural-bridge site: an explicit arch made of a circular-arc
    // TOP plus two straight LEGS, extruded for walkway width, smooth-unioned
    // onto the terrain. There is no search and no accept/reject validation —
    // placement is a fixed-cost hash lookup (like spires), orientation comes
    // from one cheap directional sample (not a multi-axis search), and each
    // leg's bottom is set directly from the REAL terrain height at that (x,z)
    // — so a leg always extends down to whatever ground is actually there,
    // tall cliff or modest hillock. That is what guarantees the whole
    // structure is always one connected piece with the ground: it doesn't
    // need to find a "good" site, because it always builds down to
    // whatever's really there.
    struct BridgeCandidate
    {
        public bool valid;
        public float cx, cz, perpX, perpZ, walkX, walkZ, halfSpan, archRise, tubeR, y0;
        public float legBottomA, legBottomB; // v-coordinate (relative to y0) each leg extends down to
    }

    void GatherBridgeCandidates(float wx, float wz, Span<BridgeCandidate> outCandidates)
    {
        uint s = unchecked((uint)seed);
        long icx = (long)Mathf.Floor(wx / bridgeCellSize);
        long icz = (long)Mathf.Floor(wz / bridgeCellSize);
        int idx = 0;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            long gx = icx + dx, gz = icz + dz;
            uint ux = unchecked((uint)gx), uz = unchecked((uint)gz);
            BridgeCandidate c = default;

            float roll = TerrainNoise.Hash(ux, uz, s + 5001u) * (1f / 4294967296f);
            if (roll < bridgeChance)
            {
                float jx = TerrainNoise.Hash(ux, uz, s + 5002u) * (1f / 4294967296f);
                float jz = TerrainNoise.Hash(ux, uz, s + 5003u) * (1f / 4294967296f);
                float spanJ = TerrainNoise.Hash(ux, uz, s + 5005u) * (1f / 4294967296f);
                float riseJ = TerrainNoise.Hash(ux, uz, s + 5006u) * (1f / 4294967296f);
                float tubeJ = TerrainNoise.Hash(ux, uz, s + 5007u) * (1f / 4294967296f);

                float cx = (gx + 0.2f + 0.6f * jx) * bridgeCellSize;
                float cz = (gz + 0.2f + 0.6f * jz) * bridgeCellSize;

                // One cheap one-shot check (not a search): don't bother
                // placing a bridge on top of a mountain.
                float hCenter = HeightAt(cx, cz);
                if (hCenter < bridgeMaxCenterHeight)
                {
                    float halfSpan = bridgeHalfSpanMin + (bridgeHalfSpanMax - bridgeHalfSpanMin) * spanJ;
                    float archRise = halfSpan * (0.5f + 0.4f * riseJ);
                    float tubeR = bridgeTubeRadiusMin + (bridgeTubeRadiusMax - bridgeTubeRadiusMin) * tubeJ;
                    float y0 = hCenter + 4f;

                    // ONE cheap directional sample ("is there an incline
                    // here") for orientation. Not validated, not searched:
                    // if it's not quite lined up with a real gap, the bridge
                    // just looks a little less dramatic; it never breaks,
                    // because the legs below always reach down to whatever
                    // is really there regardless of orientation.
                    //
                    // Uses the full HeightAt (not the th classifier alone):
                    // th saturates to a flat, EXACTLY-constant 0 or 1
                    // everywhere outside its narrow transition band, so its
                    // gradient is zero almost everywhere a bridge actually
                    // gets placed (canyon floor, away from the cliff edge) —
                    // that produced a fully degenerate (0,0) direction,
                    // which collapsed u and w to zero for every point in the
                    // world and rendered as an infinite flat slab. HeightAt
                    // keeps varying continuously everywhere from the
                    // ridge/detail/ripple noise, so its gradient is non-zero
                    // in all but pathological cases — still just one cheap
                    // directional sample, not a search.
                    const float gradEps = 12f;
                    float hXp = HeightAt(cx + gradEps, cz), hXm = HeightAt(cx - gradEps, cz);
                    float hZp = HeightAt(cx, cz + gradEps), hZm = HeightAt(cx, cz - gradEps);
                    float gxv = hXp - hXm, gzv = hZp - hZm;
                    float glen = Mathf.Sqrt(gxv * gxv + gzv * gzv);
                    float perpX, perpZ;
                    if (glen > 0.05f)
                    {
                        perpX = gxv / glen; perpZ = gzv / glen;
                    }
                    else
                    {
                        // Truly flat in every direction (rare) -- fall back
                        // to a deterministic hashed angle so orientation is
                        // never degenerate.
                        float angJ = TerrainNoise.Hash(ux, uz, s + 5008u) * (1f / 4294967296f);
                        float ang = angJ * (Mathf.PI * 2f);
                        perpX = Mathf.Cos(ang); perpZ = Mathf.Sin(ang);
                    }

                    float legAx = cx + perpX * halfSpan, legAz = cz + perpZ * halfSpan;
                    float legBx = cx - perpX * halfSpan, legBz = cz - perpZ * halfSpan;
                    float hLegA = HeightAt(legAx, legAz);
                    float hLegB = HeightAt(legBx, legBz);

                    c.valid = true;
                    c.cx = cx; c.cz = cz;
                    c.perpX = perpX; c.perpZ = perpZ;
                    c.walkX = -perpZ; c.walkZ = perpX;
                    c.halfSpan = halfSpan; c.archRise = archRise; c.tubeR = tubeR; c.y0 = y0;
                    // legs always extend down to whatever ground is really there
                    c.legBottomA = Mathf.Min(0f, (hLegA - y0) - bridgeLegEmbedMargin);
                    c.legBottomB = Mathf.Min(0f, (hLegB - y0) - bridgeLegEmbedMargin);
                }
            }
            outCandidates[idx++] = c;
        }
    }

    static float SegmentDist(float u, float v, float segU, float vLo, float vHi)
    {
        float cv = Mathf.Clamp(v, vLo, vHi);
        float du = u - segU, dv = v - cv;
        return Mathf.Sqrt(du * du + dv * dv);
    }

    float BridgeContribution(in BridgeCandidate c, float wx, float y, float wz)
    {
        float u = (wx - c.cx) * c.perpX + (wz - c.cz) * c.perpZ;
        float w = (wx - c.cx) * c.walkX + (wz - c.cz) * c.walkZ;
        float v = y - c.y0;

        float cY = (c.archRise * c.archRise - c.halfSpan * c.halfSpan) / (2f * c.archRise);
        float R = Mathf.Sqrt(c.halfSpan * c.halfSpan + cY * cY);
        float dArc = v > -c.tubeR * 1.2f ? Mathf.Abs(Mathf.Sqrt(u * u + (v - cY) * (v - cY)) - R) : float.PositiveInfinity;
        float dLegA = SegmentDist(u, v, c.halfSpan, c.legBottomA, 0f);
        float dLegB = SegmentDist(u, v, -c.halfSpan, c.legBottomB, 0f);
        float dShape = Mathf.Min(Mathf.Min(dArc, dLegA), dLegB);

        float wExtra = Mathf.Max(0f, Mathf.Abs(w) - bridgeWalkwayHalfWidth);
        float distExtruded = Mathf.Sqrt(dShape * dShape + wExtra * wExtra);
        return c.tubeR - distExtruded;
    }

    static float SMax(float a, float b, float k)
    {
        float h = Mathf.Clamp01(0.5f - 0.5f * (b - a) / k);
        return a * h + b * (1f - h) + k * h * (1f - h);
    }

    public override float Sample(Vector3 p)
    {
        float d = HeightAt(p.x, p.z) - p.y - Erosion(p.x, p.y, p.z) * disAmp;
        if (enableBridges)
        {
            Span<BridgeCandidate> cands = stackalloc BridgeCandidate[9];
            GatherBridgeCandidates(p.x, p.z, cands);
            for (int k = 0; k < cands.Length; k++)
                if (cands[k].valid)
                    d = SMax(d, BridgeContribution(cands[k], p.x, p.y, p.z), bridgeSmoothing);
        }
        return d;
    }

    // Column path: h(x,z) never depends on y, so it's computed once for the
    // whole column, and the (rare) bridge-candidate search is resolved once
    // per column too — only the erosion and arch terms need re-evaluating
    // per sample.
    public override void AddDensityColumn(float wx, float wz, float yStart, float yStep, int count,
                                          float weight, float[] dest, int destIndex, int destStride)
    {
        float h = HeightAt(wx, wz);
        Span<BridgeCandidate> cands = stackalloc BridgeCandidate[9];
        if (enableBridges)
            GatherBridgeCandidates(wx, wz, cands);

        for (int i = 0; i < count; i++)
        {
            float y = yStart + i * yStep;
            float d = h - y - Erosion(wx, y, wz) * disAmp;
            if (enableBridges)
                for (int k = 0; k < cands.Length; k++)
                    if (cands[k].valid)
                        d = SMax(d, BridgeContribution(cands[k], wx, y, wz), bridgeSmoothing);
            dest[destIndex] += weight * d;
            destIndex += destStride;
        }
    }

    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        // Spire tops are hard-clamped to the peak cap in HeightAt, so they
        // never extend the bounds beyond peakBase+peakRange+detailAmp.
        float peakMax = peakBase + peakRange + detailAmp;
        float bridgeMax = enableBridges ? (bridgeHalfSpanMax * 0.9f + bridgeTubeRadiusMax + 4f) : 0f;
        minH = floorHeight - disAmp - 2f;
        maxH = floorHeight + peakMax + rrAmp + disAmp + bridgeMax + 2f;
        return true;
    }
}
