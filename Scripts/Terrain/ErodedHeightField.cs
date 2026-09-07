using UnityEngine;

// Eroded mountains: a base relief run through ErosionNoise.Filter (Rune
// Johansen's erosion filter), so ridgelines get dendritic gully systems and
// valley floors stay hollow, with an optional sand plain that buries the
// lower slopes. One class, several biomes, all assets of this field with
// different ErosionParams and base:
//   Fbm base    -- Mountain, Desert Mountain (Config/MountainField.asset,
//                  DesertMountainField.asset)
//   Massif base -- the Dolomites (Config/DolomiteErodedField.asset): meadow
//                  floor, concave scree apron, sheer warped wall and summit
//                  crests, built with analytic derivatives so the filter can
//                  flute the walls with couloirs and cut gullies into the scree.
//
// A pure 2D heightfield: every metre of relief is a function of (x, z), so
// nothing can fold over and there is nothing to flood-fill for. The base
// keeps few octaves on purpose -- the erosion supplies the detail, and a
// bumpy base only gives it noise to amplify.
//
// Prototyped as a hillshade in Python first (scratchpad erosion/
// erosion_proto.py, 2026-09-07); those runs fixed the defaults below.
[CreateAssetMenu(fileName = "ErodedField", menuName = "Marching Cubes/Heightfield (Eroded)")]
public class ErodedHeightField : HeightDensityField
{
    [Header("World")]
    public int seed = 4242;
    [Tooltip("Metres. Mean height of the base relief.")]
    public float baseHeight = 0f;

    public enum BaseMode { Fbm, Massif }

    [Header("Base relief (before erosion)")]
    [Tooltip("Fbm: rolling mountain layout (Mountain, Desert Mountain). Massif: meadow floor with isolated sheer-walled massifs (Dolomites); uses the Massif section below and ignores the Fbm fields.")]
    public BaseMode baseMode = BaseMode.Fbm;
    [Tooltip("Metres per wave of the mountain layout. Keep it at or below a biome region's width, or a patch holds one slope instead of a range.")]
    public float baseScale = 1600f;
    [Tooltip("Octaves of the base. 3 is right: the erosion adds the detail.")]
    [Range(1, 8)] public int baseOctaves = 3;
    [Tooltip("Metres. Amplitude of the base (fbm is roughly -1..1, mostly within -0.7..0.7).")]
    public float baseAmp = 240f;
    [Tooltip("Fraction of baseAmp at which the erosion's fade target saturates: heights above +fadeRange*baseAmp count as peaks, below -fadeRange*baseAmp as valley floors.")]
    [Range(0.1f, 1f)] public float fadeRange = 0.6f;
    [Tooltip("Height offset in units of the summed octave strength: x is added everywhere, y mixes toward -fadeTarget (lowers peaks, lifts floors). Leave (0, 0) so the biome's mean height stays where baseHeight puts it.")]
    public Vector2 heightOffset = Vector2.zero;

    [Header("Erosion")]
    public ErosionParams erosion = ErosionParams.Mountain;

    [Header("Massif base (Dolomites)")]
    [Tooltip("Broad valley undulation of the meadow floor, metres per wave.")]
    public float valleyScale = 900f;
    public float valleyAmp = 30f;
    public float meadowScale = 200f;
    public float meadowAmp = 7f;
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
    [Tooltip("Mask value where the scree apron starts rising.")]
    public float screeLo = 0.27f;
    [Tooltip("Mask value at the wall foot.")]
    public float wallLo = 0.40f;
    [Tooltip("Mask value at the wall top. Keep wallHi - wallLo narrow (0.04-0.08) for vertical walls; the erosion flutes them.")]
    public float wallHi = 0.47f;
    public float screeHeight = 48f;
    [Range(1f, 4f)] public float screeExp = 2f;
    public float wallHeight = 130f;
    [Tooltip("Serrated crests across the massif top: (1 - |fbm|)^2 at this scale.")]
    public float ridgeScale = 120f;
    public float ridgeAmp = 70f;
    [Tooltip("Pinnacles on the crests.")]
    public float spireScale = 48f;
    public float spireAmp = 30f;
    [Tooltip("Metres of relief above the meadow over which the erosion fades in, so the meadow itself stays untouched.")]
    public float erosionOnsetRelief = 12f;
    [Tooltip("Horizontal limestone ledges cut into the wall after erosion (sawtooth of the eroded height). 0 = off.")]
    public float ledgeAmp = 0f;
    public float ledgeSpacing = 12f;

    [Header("Sand fill")]
    [Tooltip("Bury everything below a gently undulating sand plain (smooth max), for ranges rising out of desert basins.")]
    public bool sandEnabled = false;
    [Tooltip("Metres, absolute. Mean height of the sand plain.")]
    public float sandLevel = 0f;
    public float sandScale = 420f;
    [Range(1, 6)] public int sandOctaves = 3;
    public float sandAmp = 12f;
    [Tooltip("Metres over which rock and sand merge at the plain's edge (pediment width).")]
    public float sandBlend = 25f;

    [Header("Shading only")]
    [Tooltip("Metres over baseHeight. Mountain style: snow above this. Pushed to the terrain material as _MtnSnowLine (+ planet radius in globe mode).")]
    public float snowLine = 140f;
    public float snowBlend = 35f;
    [Tooltip("Metres over baseHeight. Mountain style: grass gives way to bare rock and scree above this; Dolomite style: the meadow/rock line. Grass stops here too.")]
    public float grassLine = 110f;
    public float grassLineBlend = 60f;

    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        float e = erosion.MaxHeightDelta() + erosion.MagnitudeSum() * Mathf.Max(1f, Mathf.Abs(heightOffset.x));
        if (baseMode == BaseMode.Massif)
        {
            float floorSwing = valleyAmp + meadowAmp;
            minH = baseHeight - floorSwing - e - ledgeAmp - 1f;
            maxH = baseHeight + floorSwing + screeHeight + wallHeight + ridgeAmp + spireAmp + e + ledgeAmp + 1f;
        }
        else
        {
            minH = baseHeight - baseAmp - e - 1f;
            maxH = baseHeight + baseAmp + e + 1f;
        }
        if (sandEnabled)
        {
            // smooth max can overshoot max(a, b) by at most sandBlend / 4
            minH = Mathf.Min(minH, sandLevel - sandAmp - 1f);
            maxH = Mathf.Max(maxH, sandLevel + sandAmp + 1f) + Mathf.Max(0f, sandBlend) * 0.25f;
        }
        return true;
    }

    // Polynomial smooth max (Quilez), k in metres; plain max when k <= 0.
    static float SmoothMax(float a, float b, float k)
    {
        if (k <= 0f) return Mathf.Max(a, b);
        float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
        return b + (a - b) * h + k * h * (1f - h);
    }

    // smoothstep and its derivative with respect to t
    static float DSmooth(float a, float b, float t, out float dt)
    {
        float u = Mathf.Clamp01((t - a) / (b - a));
        dt = 6f * u * (1f - u) / (b - a);
        return u * u * (3f - 2f * u);
    }

    // (1 - |fbm|)^2 with its gradient: sharp crest lines, differentiable
    // almost everywhere (the crest itself has a kink, as a real one does).
    static Vector3 RidgedD(float x, float y, int octaves, uint seed, float fw)
    {
        Vector3 n = TerrainNoise.FbmD(x, y, octaves, seed, fw);
        float a = Mathf.Abs(n.x);
        float sg = n.x > 0f ? 1f : (n.x < 0f ? -1f : 0f);
        float d = -2f * (1f - a) * sg;
        return new Vector3((1f - a) * (1f - a), d * n.y, d * n.z);
    }

    // Massif base with analytic gradient (x height, yz slope). Also returns
    // the meadow floor and the relief above it. Mirrors MC_ErodedMassifBase.
    Vector3 MassifBase(float wx, float wz, float fw, uint s, out float floor, out float relief)
    {
        Vector3 f1 = TerrainNoise.FbmD(wx / valleyScale, wz / valleyScale, 2, s + 3u, fw / valleyScale);
        Vector3 f2 = TerrainNoise.FbmD(wx / meadowScale, wz / meadowScale, 4, s + 11u, fw / meadowScale);
        floor = baseHeight + f1.x * valleyAmp + f2.x * meadowAmp;
        float fx = f1.y * valleyAmp / valleyScale + f2.y * meadowAmp / meadowScale;
        float fz = f1.z * valleyAmp / valleyScale + f2.z * meadowAmp / meadowScale;

        // massif mask, warped at two scales so the cliff line is buttressed
        // and notched rather than a smooth contour
        Vector3 m = TerrainNoise.FbmD(wx / massifScale, wz / massifScale, 3, s + 1u, fw / massifScale);
        Vector3 w = TerrainNoise.FbmD(wx / warpScale, wz / warpScale, 3, s + 7u, fw / warpScale);
        Vector3 tw = TerrainNoise.FbmD(wx / towerScale, wz / towerScale, 2, s + 13u, fw / towerScale);
        float t = m.x + massifOffset + warpAmp * w.x + towerAmp * tw.x;
        float tx = m.y / massifScale + warpAmp * w.y / warpScale + towerAmp * tw.y / towerScale;
        float tz = m.z / massifScale + warpAmp * w.z / warpScale + towerAmp * tw.z / towerScale;

        float ss = DSmooth(screeLo, wallLo, t, out float dss); // apron 0..1
        float sw = DSmooth(wallLo, wallHi, t, out float dsw);  // wall 0..1, narrow => vertical

        float apron = screeHeight * Mathf.Pow(ss, screeExp);
        float dapron = screeHeight * screeExp * Mathf.Pow(Mathf.Max(ss, 1e-9f), screeExp - 1f) * dss;

        Vector3 r1 = RidgedD(wx / ridgeScale, wz / ridgeScale, 3, s + 23u, fw / ridgeScale);
        Vector3 r2 = RidgedD(wx / spireScale, wz / spireScale, 3, s + 29u, fw / spireScale);
        float summit = ridgeAmp * r1.x + spireAmp * r2.x;
        float sx = ridgeAmp * r1.y / ridgeScale + spireAmp * r2.y / spireScale;
        float sz = ridgeAmp * r1.z / ridgeScale + spireAmp * r2.z / spireScale;

        float top = wallHeight + summit;
        float h = floor + apron + sw * top;
        relief = h - floor;
        return new Vector3(h,
                           fx + dapron * tx + dsw * tx * top + sw * sx,
                           fz + dapron * tz + dsw * tz * top + sw * sz);
    }

    public override float HeightAt(float wx, float wz, float fw)
    {
        uint s = unchecked((uint)seed);
        float h;

        if (baseMode == BaseMode.Massif)
        {
            Vector3 n = MassifBase(wx, wz, fw, s, out float floor, out float relief);
            float top0 = screeHeight + wallHeight;
            // fade target 0 everywhere (symmetric gullies, no baseline shift)
            // rising to +1 over the upper wall and summit: sharp crests
            float fadeTarget = TerrainNoise.Smoothstep(screeHeight + 0.5f * wallHeight, top0, relief);
            Vector4 e = ErosionNoise.Filter(wx, wz, n, fadeTarget, erosion, s + 101u, fw, out _);
            // the meadow stays a meadow: erosion fades in across the apron foot
            float emask = TerrainNoise.Smoothstep(0f, erosionOnsetRelief, relief);
            h = n.x + e.x * emask;
            if (ledgeAmp > 0f)
            {
                float u = h / ledgeSpacing;
                float saw = (u - Mathf.Floor(u) - 0.5f) * 2f;
                float band = TerrainNoise.Smoothstep(screeHeight * 0.8f, screeHeight + wallHeight * 0.25f, relief)
                           * (1f - TerrainNoise.Smoothstep(top0 * 0.9f, top0 * 1.1f, relief));
                h -= ledgeAmp * saw * band;
            }
            return h;
        }

        // base relief with its gradient, in metres and metres per metre
        Vector3 nb = TerrainNoise.FbmD(wx / baseScale, wz / baseScale, baseOctaves, s + 1u, fw / baseScale);
        nb.x *= baseAmp;
        nb.y *= baseAmp / baseScale;
        nb.z *= baseAmp / baseScale;

        // -1 at valley floors, +1 at peaks: where flat ground fades to
        float ft = Mathf.Clamp(nb.x / (baseAmp * fadeRange), -1f, 1f);

        Vector4 eb = ErosionNoise.Filter(wx, wz, nb, ft, erosion, s + 101u, fw, out _);
        float offset = Mathf.Lerp(heightOffset.x, -ft, heightOffset.y) * eb.w;
        h = baseHeight + nb.x + eb.x + offset;

        if (sandEnabled)
        {
            float plain = sandLevel + TerrainNoise.Fbm(wx / sandScale, wz / sandScale, sandOctaves, s + 5u, fw / sandScale) * sandAmp;
            h = SmoothMax(h, plain, sandBlend);
        }
        return h;
    }

    // GPU twin: Shaders/Compute/DensityEroded.hlsl, EvaluateErodedHeight.
    // Field order must match ErodedGpuParams / the HLSL ErodedParams struct.
    public override GpuFieldType GpuType => GpuFieldType.Eroded;

    public override LeafGpuParams ToGpuLeafParams()
    {
        var p = new LeafGpuParams();
        p.eroded = new ErodedGpuParams
        {
            seed = seed,
            baseHeight = baseHeight,
            baseScale = baseScale,
            baseOctaves = baseOctaves,
            baseAmp = baseAmp,
            fadeRange = fadeRange,
            heightOffsetX = heightOffset.x,
            heightOffsetY = heightOffset.y,
            eScale = erosion.scale,
            eStrength = erosion.strength,
            eGullyWeight = erosion.gullyWeight,
            eDetail = erosion.detail,
            eRoundingX = erosion.rounding.x,
            eRoundingY = erosion.rounding.y,
            eRoundingZ = erosion.rounding.z,
            eRoundingW = erosion.rounding.w,
            eOnsetX = erosion.onset.x,
            eOnsetY = erosion.onset.y,
            eOnsetZ = erosion.onset.z,
            eOnsetW = erosion.onset.w,
            eAssumedSlopeX = erosion.assumedSlope.x,
            eAssumedSlopeY = erosion.assumedSlope.y,
            eCellScale = erosion.cellScale,
            eOctaves = erosion.octaves,
            eGain = erosion.gain,
            eLacunarity = erosion.lacunarity,
            eNormalization = erosion.normalization,
            sandEnabled = sandEnabled ? 1f : 0f,
            sandLevel = sandLevel,
            sandScale = sandScale,
            sandOctaves = sandOctaves,
            sandAmp = sandAmp,
            sandBlend = sandBlend,
            baseMode = (float)baseMode,
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
            ridgeScale = ridgeScale,
            ridgeAmp = ridgeAmp,
            spireScale = spireScale,
            spireAmp = spireAmp,
            erosionOnsetRelief = erosionOnsetRelief,
            ledgeAmp = ledgeAmp,
            ledgeSpacing = ledgeSpacing,
        };
        return p;
    }
}
