using UnityEngine;

// Dolomites: isolated pale limestone massifs standing in rolling alpine
// meadow -- sheer terraced walls, a concave scree apron at the wall foot, and
// serrated ridgelines and pinnacles along the summits.
//
// A pure 2D heightfield, deliberately. Every term that carries real relief
// (walls, summits) is a function of (x, z) only, so the surface can never
// fold over itself and there is nothing to flood-fill for. The walls get
// their vertical faces from a NARROW smoothstep on a warped massif mask (the
// same trick CanyonVolumeField uses for its cliffs), and their horizontal
// limestone ledges from TERRACING that smoothstep -- steps in the mask
// domain become steps up the wall face, entirely in 2D.
//
// Prototyped and viewed as a hillshade in Python first (scratchpad
// dolomite/dolomite.py, 2026-09-06); the parameter defaults below are the
// ones that read as the Dolomites there: ~9% of the area massif, walls at
// 80-88 degrees, 130 m of wall over a 48 m apron with 50-80 m of ridge on top.
[CreateAssetMenu(fileName = "DolomiteField", menuName = "Marching Cubes/Heightfield (Dolomite)")]
public class DolomiteVolumeField : HeightDensityField
{
    [Header("World")]
    public int seed = 7331;
    public float baseHeight = 0f;

    [Header("Valley floor (alpine meadow)")]
    [Tooltip("Broad valley undulation, metres per wave.")]
    public float valleyScale = 900f;
    public float valleyAmp = 30f;
    [Tooltip("Rolling meadow detail.")]
    public float meadowScale = 200f;
    public float meadowAmp = 7f;
    [Tooltip("Shading only: flat ground above this height (over baseHeight) is bare rock and scree instead of meadow, so massif tops and high shoulders do not turn green. MCChunkManager pushes it to the terrain material as _DoloRockLine.")]
    public float meadowLine = 40f;
    [Tooltip("Metres over which meadow fades into rock at the meadow line.")]
    public float meadowLineBlend = 20f;

    [Header("Massif layout")]
    [Tooltip("Size of one massif group. The mask is fbm at this scale; a massif is wherever it exceeds the wall threshold.")]
    public float massifScale = 950f;
    [Tooltip("Shifts the mask up or down: positive = more of the world is massif.")]
    public float massifOffset = 0.06f;
    [Tooltip("Mid-scale warp of the mask that crenellates the cliff line into buttresses and bays.")]
    public float warpScale = 170f;
    [Range(0f, 0.6f)] public float warpAmp = 0.22f;
    [Tooltip("Fine warp that breaks the cliff line into detached towers and notches.")]
    public float towerScale = 70f;
    [Range(0f, 0.4f)] public float towerAmp = 0.08f;

    [Header("Wall and scree")]
    [Tooltip("Mask value where the scree apron starts rising.")]
    public float screeLo = 0.27f;
    [Tooltip("Mask value at the wall foot. The apron rises from screeLo to here.")]
    public float wallLo = 0.40f;
    [Tooltip("Mask value at the wall top. wallHi - wallLo is the cliff's width in mask units: keep it narrow (0.04-0.06) for vertical walls.")]
    public float wallHi = 0.47f;
    public float screeHeight = 48f;
    [Tooltip("Apron profile exponent: 1 = straight ramp, 2 = concave talus fan.")]
    [Range(1f, 4f)] public float screeExp = 2f;
    public float wallHeight = 130f;
    [Tooltip("Horizontal limestone ledges up the wall. 0 disables terracing.")]
    [Range(0, 40)] public int ledgeCount = 11;
    [Tooltip("Fraction of each step that is flat ledge rather than riser.")]
    [Range(0f, 0.95f)] public float ledgeSharp = 0.4f;
    [Tooltip("Wanders the ledge lines with noise so strata undulate instead of ringing each buttress like stacked plates. In fractions of a step.")]
    [Range(0f, 1f)] public float ledgeJitter = 0.5f;
    public float ledgeJitterScale = 35f;

    [Header("Summits")]
    [Tooltip("Long serrated crest lines across the massif top (ridged fbm).")]
    public float ridgeScale = 120f;
    [Range(1, 8)] public int ridgeOctaves = 4;
    public float ridgeAmp = 70f;
    [Tooltip("Pinnacles and teeth on top of the crests.")]
    public float spireScale = 48f;
    [Range(1, 6)] public int spireOctaves = 3;
    public float spireAmp = 30f;

    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        // Fbm is in [-1, 1] after normalisation, RidgedFbm in [0, ~1].
        float floorSwing = valleyAmp + meadowAmp;
        minH = baseHeight - floorSwing - 1f;
        maxH = baseHeight + floorSwing + screeHeight + wallHeight + ridgeAmp + spireAmp + 1f;
        return true;
    }

    // A staircase with soft risers: `sharp` of each step is flat, the rest
    // climbs. Identity when n == 0.
    static float Terrace(float u, int n, float sharp)
    {
        if (n <= 0) return u;
        float v = u * n;
        float k = Mathf.Floor(v);
        float f = v - k;
        float riser = TerrainNoise.Smoothstep(sharp, 1f, f);
        return (k + riser) / n;
    }

    public override float HeightAt(float wx, float wz, float fw)
    {
        uint s = unchecked((uint)seed);

        // valley floor
        float h = baseHeight
                + TerrainNoise.Fbm(wx / valleyScale, wz / valleyScale, 2, s + 3u, fw / valleyScale) * valleyAmp
                + TerrainNoise.Fbm(wx / meadowScale, wz / meadowScale, 4, s + 11u, fw / meadowScale) * meadowAmp;

        // massif mask: broad layout, warped at two scales so the cliff line
        // is buttressed and notched rather than a smooth contour
        float t = TerrainNoise.Fbm(wx / massifScale, wz / massifScale, 3, s + 1u, fw / massifScale) + massifOffset;
        t += warpAmp * TerrainNoise.Fbm(wx / warpScale, wz / warpScale, 3, s + 7u, fw / warpScale);
        t += towerAmp * TerrainNoise.Fbm(wx / towerScale, wz / towerScale, 2, s + 13u, fw / towerScale);

        float ss = TerrainNoise.Smoothstep(screeLo, wallLo, t);   // apron 0..1
        float sw = TerrainNoise.Smoothstep(wallLo, wallHi, t);    // wall 0..1, narrow => vertical

        // scree apron: concave talus fan up to the wall foot
        h += screeHeight * Mathf.Pow(ss, screeExp);

        // wall: terraced so the face carries horizontal ledges. The ledges
        // are the finest horizontal feature here (a couple of metres per
        // step at the wall's slope), so they fade toward the plain smoothstep
        // as the sample spacing grows -- same rule as every other octave.
        // Jitter the step phase with noise, zero at both ends of the wall so
        // the foot and the summit stay where the smoothstep puts them.
        float jitter = ledgeCount > 0 && ledgeJitter > 0f
            ? ledgeJitter / ledgeCount * TerrainNoise.Fbm(wx / ledgeJitterScale, wz / ledgeJitterScale, 2, s + 17u, fw / ledgeJitterScale)
              * 4f * sw * (1f - sw)
            : 0f;
        float terraced = Terrace(sw + jitter, ledgeCount, ledgeSharp);
        float ledgeFade = ledgeCount > 0 ? TerrainNoise.DetailFade(fw, wallHeight / ledgeCount * 0.2f) : 0f;
        h += wallHeight * Mathf.Lerp(sw, terraced, ledgeFade);

        // summits: only on the massif top (weighted by the wall mask)
        if (sw > 0f)
        {
            float summit = TerrainNoise.RidgedFbm(wx / ridgeScale, wz / ridgeScale, ridgeOctaves, s + 23u,
                                                  filterWidth: fw / ridgeScale) * ridgeAmp
                         + TerrainNoise.RidgedFbm(wx / spireScale, wz / spireScale, spireOctaves, s + 29u,
                                                  filterWidth: fw / spireScale) * spireAmp;
            h += sw * summit;
        }
        return h;
    }

    // GPU twin: Shaders/Compute/DensityDolomite.hlsl, EvaluateDolomiteHeight.
    // Field order must match DolomiteGpuParams / the HLSL DolomiteParams
    // struct exactly.
    public override GpuFieldType GpuType => GpuFieldType.Dolomite;

    public override LeafGpuParams ToGpuLeafParams()
    {
        var p = new LeafGpuParams();
        p.dolomite = new DolomiteGpuParams
        {
            seed = seed,
            baseHeight = baseHeight,
            valleyScale = valleyScale,
            valleyAmp = valleyAmp,
            meadowScale = meadowScale,
            meadowAmp = meadowAmp,
            massifScale = massifScale,
            massifOffset = massifOffset,
            warpScale = warpScale,
            warpAmp = warpAmp,
            towerScale = towerScale,
            towerAmp = towerAmp,
            screeLo = screeLo,
            wallLo = wallLo,
            wallHi = wallHi,
            screeHeight = screeHeight,
            screeExp = screeExp,
            wallHeight = wallHeight,
            ledgeCount = ledgeCount,
            ledgeSharp = ledgeSharp,
            ledgeJitter = ledgeJitter,
            ledgeJitterScale = ledgeJitterScale,
            ridgeScale = ridgeScale,
            ridgeOctaves = ridgeOctaves,
            ridgeAmp = ridgeAmp,
            spireScale = spireScale,
            spireOctaves = spireOctaves,
            spireAmp = spireAmp,
        };
        return p;
    }
}
