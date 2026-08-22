using UnityEngine;

// Glacier/tundra heightfield: layers wind-carved snow ridges, permafrost
// patterned ground, crevasse fields, and frost-heave hummocks on top of a
// ridged-fbm frozen highland base -- built from the same noise primitives as
// the other biomes (TerrainNoise.Fbm/RidgedFbm), plus Worley/cellular noise
// (TerrainNoise.Worley2) for the cracked/veined features plain fbm can't
// produce. A pure function of (x, z), same as DuneHeightfield -- consistent
// across all LOD levels for free.
[CreateAssetMenu(fileName = "FrostField", menuName = "Marching Cubes/Heightfield (Frost)")]
public class FrostVolumeField : HeightDensityField
{
    [Header("World")]
    public int seed = 2024;
    public float baseHeight = 0f;
    [Tooltip("Direction the wind blows toward, degrees from +X toward +Z -- sastrugi ridges align with this.")]
    public float windDegrees = 40f;

    [Header("Base terrain")]
    public float plainScale = 380f;
    public float plainAmplitude = 2.5f;

    [Header("Frozen peaks (ridged)")]
    public float peakScale = 260f;
    [Range(1, 8)] public int peakOctaves = 3;
    public float peakAmp = 22f;

    [Header("Sastrugi (wind-carved ridges)")]
    public bool sastrugi = true;
    [Tooltip("Ridge spacing across the wind direction, meters.")]
    public float sastrugiWavelengthAcross = 3.5f;
    [Tooltip("How far ridges stay coherent along the wind direction, meters -- bigger = longer streaks.")]
    public float sastrugiWavelengthAlong = 40f;
    [Range(1, 6)] public int sastrugiOctaves = 2;
    public float sastrugiAmp = 0.6f;
    [Tooltip("0 = symmetric ridge, 1 = fully wind-carved (sharp lee face, gentle windward face).")]
    [Range(0f, 1f)] public float sastrugiAsymmetry = 0.5f;

    [Header("Permafrost polygons")]
    public bool permafrostPolygons = true;
    [Tooltip("Cell diameter, meters -- real patterned ground runs roughly 3-30m.")]
    public float polygonScale = 18f;
    public float polygonTroughDepth = 0.35f;
    [Tooltip("Worley edge-distance width the trough fades over. Smaller = thinner, crisper trough lines.")]
    public float polygonEdgeWidth = 0.35f;

    [Header("Crevasse fields")]
    public bool crevasses = true;
    public float crevasseScale = 55f;
    public float crevasseDepth = 6f;
    public float crevasseEdgeWidth = 0.12f;
    [Tooltip("Scale (m) of the regions where crevasse fields appear -- glaciers are patchy, not everywhere.")]
    public float crevassePatchScale = 500f;

    [Header("Frost heave hummocks")]
    public bool hummocks = true;
    public float hummockScale = 6f;
    public float hummockAmp = 0.4f;

    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        float up = plainAmplitude + peakAmp + (sastrugi ? sastrugiAmp : 0f) + (hummocks ? hummockAmp : 0f) + 1f;
        float down = plainAmplitude + (permafrostPolygons ? polygonTroughDepth : 0f) + (crevasses ? crevasseDepth : 0f) + 1f;
        minH = baseHeight - down;
        maxH = baseHeight + up;
        return true;
    }

    public override float HeightAt(float wx, float wz)
    {
        uint s = unchecked((uint)seed);
        float h = baseHeight;

        // broad rolling base (interdune-plain equivalent)
        h += TerrainNoise.Fbm(wx / plainScale, wz / plainScale, 3, s + 11u) * plainAmplitude;

        // frozen peaks / hills
        h += TerrainNoise.RidgedFbm(wx / peakScale, wz / peakScale, peakOctaves, s + 23u) * peakAmp;

        // sastrugi: wind-carved ridges. Anisotropic ridged noise -- long
        // wavelength along wind (ridges run in the wind direction), short
        // wavelength across wind (many parallel ridges) -- with a cheap
        // directional-derivative proxy (two single-octave GNoise taps,
        // instead of a second full multi-octave RidgedFbm call) for the
        // sharp-lee/gentle-windward asymmetry real sastrugi has.
        if (sastrugi)
        {
            float wr = windDegrees * Mathf.Deg2Rad;
            float cu = Mathf.Cos(wr), su = Mathf.Sin(wr);
            float u = wx * cu + wz * su;   // along wind
            float v = -wx * su + wz * cu;  // across wind

            float ridge = TerrainNoise.RidgedFbm(v / sastrugiWavelengthAcross, u / sastrugiWavelengthAlong, sastrugiOctaves, s + 41u);
            float nHere = TerrainNoise.GNoise(v / sastrugiWavelengthAcross, u / sastrugiWavelengthAlong, s + 41u);
            float nAhead = TerrainNoise.GNoise(v / sastrugiWavelengthAcross, (u + sastrugiWavelengthAcross * 0.35f) / sastrugiWavelengthAlong, s + 41u);
            ridge += sastrugiAsymmetry * 0.5f * (nHere - nAhead);
            h += ridge * sastrugiAmp;
        }

        // permafrost polygons: Worley cell-boundary troughs (patterned
        // ground) -- f2-f1 is ~0 exactly on a cell edge, so 1-smoothstep(...)
        // carves a thin trough network along the polygon boundaries.
        if (permafrostPolygons)
        {
            TerrainNoise.Worley2(wx / polygonScale, wz / polygonScale, s + 61u, out float f1, out float f2);
            float trough = 1f - TerrainNoise.Smoothstep(0f, polygonEdgeWidth, f2 - f1);
            h -= trough * polygonTroughDepth;
        }

        // crevasse fields: same Worley-edge trick as the polygons, but finer
        // and deeper, and gated by a slow patch mask so cracked glacier-like
        // zones appear regionally rather than blanketing the whole biome.
        if (crevasses)
        {
            float patch = TerrainNoise.Fbm(wx / crevassePatchScale + 5.2f, wz / crevassePatchScale - 2.9f, 2, s + 71u);
            patch = Mathf.Clamp01((patch + 0.4f) / 0.7f);
            patch = patch * patch * (3f - 2f * patch);

            if (patch > 0.001f)
            {
                TerrainNoise.Worley2(wx / crevasseScale, wz / crevasseScale, s + 83u, out float f1, out float f2);
                float crack = 1f - TerrainNoise.Smoothstep(0f, crevasseEdgeWidth, f2 - f1);
                h -= crack * crevasseDepth * patch;
            }
        }

        // frost heave hummocks: small bumpy ground texture
        if (hummocks)
            h += TerrainNoise.Fbm(wx / hummockScale, wz / hummockScale, 2, s + 97u) * hummockAmp;

        return h;
    }

    // GPU acceleration hook -- see Shaders/Compute/DensityFrost.hlsl's
    // EvaluateFrostHeight, a 1:1 port of HeightAt above. Field order here
    // must match FrostGpuParams / the HLSL FrostParams struct exactly.
    public override GpuFieldType GpuType => GpuFieldType.Frost;

    public override LeafGpuParams ToGpuLeafParams()
    {
        var p = new LeafGpuParams();
        p.frost = new FrostGpuParams
        {
            seed = seed,
            baseHeight = baseHeight,
            windDegrees = windDegrees,
            plainScale = plainScale,
            plainAmplitude = plainAmplitude,
            peakScale = peakScale,
            peakOctaves = peakOctaves,
            peakAmp = peakAmp,
            sastrugiEnabled = sastrugi ? 1f : 0f,
            sastrugiWavelengthAcross = sastrugiWavelengthAcross,
            sastrugiWavelengthAlong = sastrugiWavelengthAlong,
            sastrugiOctaves = sastrugiOctaves,
            sastrugiAmp = sastrugiAmp,
            sastrugiAsymmetry = sastrugiAsymmetry,
            permafrostEnabled = permafrostPolygons ? 1f : 0f,
            polygonScale = polygonScale,
            polygonTroughDepth = polygonTroughDepth,
            polygonEdgeWidth = polygonEdgeWidth,
            crevassesEnabled = crevasses ? 1f : 0f,
            crevasseScale = crevasseScale,
            crevasseDepth = crevasseDepth,
            crevasseEdgeWidth = crevasseEdgeWidth,
            crevassePatchScale = crevassePatchScale,
            hummocksEnabled = hummocks ? 1f : 0f,
            hummockScale = hummockScale,
            hummockAmp = hummockAmp,
        };
        return p;
    }
}
