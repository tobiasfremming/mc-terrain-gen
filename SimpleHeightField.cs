using UnityEngine;

[CreateAssetMenu(fileName="HF_Simple", menuName="Marching Cubes/Heightfield (Simple)")]
public class SimpleHeightfield : DensityField
{
    [Header("Base")]
    public float baseHeight = 0f;     // world Y offset
    public float amplitude = 8f;      // strength of hills
    public float frequency = 0.08f;   // base Perlin frequency
    [Range(1,8)] public int octaves = 4;
    public float lacunarity = 2f;     // freq multiplier per octave
    public float gain = 0.5f;         // amp multiplier per octave

    [Header("Dunes (optional)")]
    public bool dunes = true;
    public Vector2 windDir = new Vector2(1,0);
    public float duneFreqAlong = 0.02f;   // long along wind
    public float duneFreqAcross = 0.2f;   // tighter across wind
    public float duneAmp = 2f;

    [Header("Mountains (ridged)")]
    public bool mountains = true;
    public float ridgeFreq = 0.05f;
    public float ridgeAmp = 6f;

    float Fbm2D(Vector2 p)
    {
        float sum = 0f, amp = 1f, freq = frequency;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Mathf.PerlinNoise(p.x * freq, p.y * freq);
            freq *= lacunarity;
            amp  *= gain;
        }
        return sum;
    }

    float Ridged2D(Vector2 p, float f)
    {
        float n = Mathf.PerlinNoise(p.x * f, p.y * f);  // 0..1
        float r = 1f - Mathf.Abs(2f * n - 1f);          // ridge shape
        return r * r;
    }

    // The (expensive) 2D terrain height at world (x, z).
    public float HeightAt(float wx, float wz)
    {
        Vector2 xz = new Vector2(wx, wz);

        float h = baseHeight;

        // Base fbm (recenters roughly around 0)
        float fbm = Fbm2D(xz) - 0.5f;
        h += fbm * amplitude;

        if (dunes)
        {
            Vector2 u = windDir.sqrMagnitude > 0.0001f ? windDir.normalized : Vector2.right;
            Vector2 v = new Vector2(-u.y, u.x); // perpendicular
            float du = Vector2.Dot(xz, u) * duneFreqAlong;
            float dv = Vector2.Dot(xz, v) * duneFreqAcross;
            float d = Mathf.PerlinNoise(du, dv) * 0.6f + Mathf.PerlinNoise(du * 2.31f, dv * 2.31f) * 0.4f;
            d = Mathf.SmoothStep(0.2f, 0.9f, d);
            h += (d - 0.5f) * 2f * duneAmp;
        }

        if (mountains)
        {
            float r = Ridged2D(xz, ridgeFreq);
            h += r * ridgeAmp;
        }

        return h;
    }

    public override float Sample(Vector3 w)
    {
        // Signed density for a height surface: positive = below surface (solid),
        // negative = above (air).
        return HeightAt(w.x, w.z) - w.y;
    }

    // Height only depends on (x, z), so evaluate it once per column instead of
    // once per sample: countY x fewer noise evaluations than the default.
    public override void SampleGrid(Vector3 origin, int countX, int countY, int countZ, float step, float[] dest)
    {
        float[] row = new float[countX];
        for (int z = 0; z < countZ; z++)
        {
            float wz = origin.z + z * step;
            for (int x = 0; x < countX; x++)
                row[x] = HeightAt(origin.x + x * step, wz);

            for (int y = 0; y < countY; y++)
            {
                float wy = origin.y + y * step;
                int baseIdx = (z * countY + y) * countX;
                for (int x = 0; x < countX; x++)
                    dest[baseIdx + x] = row[x] - wy;
            }
        }
    }

    // Identical values to the base central-difference gradient (d/dy of h - y is
    // exactly -1, and x/z differences only involve the height), ~3x cheaper.
    public override Vector3 Gradient(Vector3 p, float eps)
    {
        float dx = HeightAt(p.x + eps, p.z) - HeightAt(p.x - eps, p.z);
        float dz = HeightAt(p.x, p.z + eps) - HeightAt(p.x, p.z - eps);
        return new Vector3(dx / (2f * eps), -1f, dz / (2f * eps));
    }
}
