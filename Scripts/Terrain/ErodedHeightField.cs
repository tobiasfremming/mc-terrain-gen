using UnityEngine;

// Eroded mountains: a smooth fbm base run through ErosionNoise.Filter (Rune
// Johansen's erosion filter), so ridgelines get dendritic gully systems and
// valley floors stay hollow, with an optional sand plain that buries the
// lower slopes. One class, several biomes: the Mountain and Desert Mountain
// biomes are two assets of this field with different ErosionParams, base
// relief and sand fill (Config/MountainField.asset, DesertMountainField.asset).
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

    [Header("Base relief (before erosion)")]
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
    [Tooltip("Metres over baseHeight. Mountain style: grass gives way to bare rock and scree above this (_MtnGrassLine).")]
    public float grassLine = 110f;
    public float grassLineBlend = 60f;

    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        float e = erosion.MaxHeightDelta() + erosion.MagnitudeSum() * Mathf.Max(1f, Mathf.Abs(heightOffset.x));
        minH = baseHeight - baseAmp - e - 1f;
        maxH = baseHeight + baseAmp + e + 1f;
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

    public override float HeightAt(float wx, float wz, float fw)
    {
        uint s = unchecked((uint)seed);

        // base relief with its gradient, in metres and metres per metre
        Vector3 n = TerrainNoise.FbmD(wx / baseScale, wz / baseScale, baseOctaves, s + 1u, fw / baseScale);
        n.x *= baseAmp;
        n.y *= baseAmp / baseScale;
        n.z *= baseAmp / baseScale;

        // -1 at valley floors, +1 at peaks: where flat ground fades to
        float fadeTarget = Mathf.Clamp(n.x / (baseAmp * fadeRange), -1f, 1f);

        Vector4 e = ErosionNoise.Filter(wx, wz, n, fadeTarget, erosion, s + 101u, fw, out _);
        float offset = Mathf.Lerp(heightOffset.x, -fadeTarget, heightOffset.y) * e.w;
        float h = baseHeight + n.x + e.x + offset;

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
        };
        return p;
    }
}
