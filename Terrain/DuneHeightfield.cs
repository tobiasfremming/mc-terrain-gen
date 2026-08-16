using UnityEngine;

// Analytic desert dune field, informed by dune geomorphology (Bishop et al.
// 2001 "Modelling Desert Dune Fields"; Paris et al. 2019 "Desertscape
// Simulation"): transverse dunes have a long gentle windward slope, a sharp
// crest brink and a short steep slip face near the angle of repose; crest
// lines are sinuous and run perpendicular to the wind; fields are organized in
// a hierarchy (mega dunes / secondary dunes / ripples) with regional variation
// in sand supply and dune size.
//
// This is a pure function of (x, z) — no simulation — so it stays consistent
// across all LOD levels. The composition was tuned visually in the prototype
// (scratchpad dunes.py); this file is a 1:1 port of it.
[CreateAssetMenu(fileName = "DuneField", menuName = "Marching Cubes/Heightfield (Dunes)")]
public class DuneHeightfield : HeightDensityField
{
    [Header("World")]
    public int seed = 1337;
    [Tooltip("Direction the wind blows toward, degrees from +X toward +Z. Crests run perpendicular to this.")]
    public float windDegrees = 25f;
    public float baseHeight = 0f;

    [Header("Mega dunes (draa)")]
    public float megaWavelength = 320f;
    public float megaHeight = 26f;
    [Tooltip("How much crest lines wander (0 = ruler-straight ridges).")]
    public float megaSinuosity = 0.95f;
    [Range(0f, 1f)] public float megaSegmentation = 0.55f;

    [Header("Secondary dunes")]
    public float secondaryWavelength = 62f;
    public float secondaryHeight = 5.5f;
    [Tooltip("Secondary dunes ride at a slightly different wind angle, degrees.")]
    public float secondaryWindOffset = 9f;
    public float secondarySinuosity = 1.15f;
    [Range(0f, 1f)] public float secondarySegmentation = 0.9f;
    [Tooltip("Scale (m) of the patches where secondary dunes appear.")]
    public float secondaryPatchScale = 260f;

    [Header("Sand ripples")]
    public float rippleWavelength = 7f;
    public float rippleHeight = 0.18f;

    [Header("Regional variation")]
    [Tooltip("Scale (m) of sand-supply variation: how pronounced dunes are region to region.")]
    public float supplyScale = 950f;
    [Tooltip("Scale (m) of dune-size (height) variation.")]
    public float sizeVariationScale = 1500f;
    [Tooltip("Interdune plain: fbm scale (m) and amplitude (m).")]
    public float plainScale = 420f;
    public float plainAmplitude = 3f;

    // Where along one period the crest sits (fraction that is windward slope).
    const float kCrestPos = 0.72f;

    // ------------------------------------------------------------------
    // Dune shaping
    // ------------------------------------------------------------------

    // t in [0,1) along wind through one dune period -> height 0..1.
    // Long concave windward rise (slope 0 at the trough, steepening toward the
    // crest with slope > 0 AT the crest -> sharp brink), then a short
    // smoothstep slip face: steep at the brink, rounded foot.
    static float DuneProfile(float t, float crestPos)
    {
        if (t < crestPos)
        {
            float w = t / crestPos;
            return w * w * (1.6f - 0.6f * w);
        }
        float lee = (t - crestPos) / (1f - crestPos);
        float inv = 1f - lee;
        return inv * inv * (3f - 2f * inv);
    }

    // Quasi-periodic transverse dune ridges: crests run across the wind (along
    // v), warped by low-frequency noise for sinuosity and local wavelength
    // variation, with along-crest swelling/pinching to break ridges up.
    static float DuneSystem(float u, float v, float wavelength, uint seed,
                            float sinuosity, float segmentation, float crestPos)
    {
        float inv = 1f / wavelength;
        float warp = (TerrainNoise.GNoise(u * inv * 0.35f, v * inv * 0.22f, seed + 7u) * 0.55f
                    + TerrainNoise.GNoise(u * inv * 0.11f, v * inv * 0.07f, seed + 13u) * 0.9f) * sinuosity;
        float phase = u * inv + warp;
        float t = phase - Mathf.Floor(phase);
        float prof = DuneProfile(t, crestPos);

        float seg = Mathf.Clamp01(0.5f + 0.5f * TerrainNoise.GNoise(u * inv * 0.18f, v * inv * 0.4f, seed + 29u));
        seg = 1f - segmentation * (1f - seg * seg * (3f - 2f * seg));
        return prof * seg;
    }

    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        // fbm outputs stay within roughly [-1.3, 1.3]; dune terms are >= 0.
        const float fbmMax = 1.3f;
        float duneMax = megaHeight * (0.85f + 0.5f * fbmMax)
                      + secondaryHeight + rippleHeight;
        minH = baseHeight - plainAmplitude * fbmMax - 1f;
        maxH = baseHeight + plainAmplitude * fbmMax + duneMax + 1f;
        return true;
    }

    public override float HeightAt(float wx, float wz)
    {
        uint s = unchecked((uint)seed);
        float wr = windDegrees * Mathf.Deg2Rad;
        float cu = Mathf.Cos(wr), su = Mathf.Sin(wr);
        float u = wx * cu + wz * su;   // along wind
        float v = -wx * su + wz * cu;  // across wind

        // --- regional modulation ---
        float supply = TerrainNoise.Fbm(wx / supplyScale, wz / supplyScale, 3, s + 101u);
        supply = Mathf.Clamp01((supply + 0.55f) / 1.0f);
        supply = 0.25f + 0.75f * (supply * supply * (3f - 2f * supply)); // dunes everywhere, but varying
        float sizeMod = 0.85f + 0.5f * TerrainNoise.Fbm(wx / sizeVariationScale + 31.7f, wz / sizeVariationScale - 11.3f, 2, s + 211u);

        // --- mega dunes (draa) ---
        float mega = DuneSystem(u, v, megaWavelength, s + 1u, megaSinuosity, megaSegmentation, kCrestPos) * megaHeight;

        // --- secondary dunes riding on them, slightly rotated wind ---
        float wr2 = (windDegrees + secondaryWindOffset) * Mathf.Deg2Rad;
        float u2 = wx * Mathf.Cos(wr2) + wz * Mathf.Sin(wr2);
        float v2 = -wx * Mathf.Sin(wr2) + wz * Mathf.Cos(wr2);
        float sec = DuneSystem(u2, v2, secondaryWavelength, s + 2u, secondarySinuosity, secondarySegmentation, 0.68f) * secondaryHeight;
        // patchiness: secondary dunes come in fields, not everywhere
        float patch = Mathf.Clamp01((TerrainNoise.Fbm(wx / secondaryPatchScale - 7.7f, wz / secondaryPatchScale + 3.1f, 2, s + 401u) + 0.45f) / 0.8f);
        patch = patch * patch * (3f - 2f * patch);
        sec *= 0.15f + 0.85f * patch;

        // --- sand ripples hint ---
        float rip = DuneSystem(u, v, rippleWavelength, s + 3u, 1.4f, 0.3f, 0.6f) * rippleHeight;

        float dunes = (mega * sizeMod + sec * (0.45f + 0.55f * supply) + rip * (0.4f + 0.6f * supply)) * supply;

        // --- interdune base: gently rolling deflation plain ---
        float basePlain = TerrainNoise.Fbm(wx / plainScale, wz / plainScale, 3, s + 301u) * plainAmplitude;
        return baseHeight + basePlain + dunes;
    }
}
