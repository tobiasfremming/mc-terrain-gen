using UnityEngine;

// Shared seeded gradient noise for all terrain fields. Deterministic across
// machines (integer hash, unlike Mathf.PerlinNoise) and mirrored 1:1 by the
// Python prototypes (scratchpad dunes.py) used to tune the terrain visually.
// Thread-safe: pure static math, called from worker threads during meshing.
public static class TerrainNoise
{
    // 32 precomputed gradient directions instead of per-corner cos/sin.
    static readonly float[] kGradX = new float[32];
    static readonly float[] kGradY = new float[32];
    static TerrainNoise()
    {
        for (int i = 0; i < 32; i++)
        {
            float ang = i * (2f * Mathf.PI / 32f);
            kGradX[i] = Mathf.Cos(ang);
            kGradY[i] = Mathf.Sin(ang);
        }
    }

    public static uint Hash(uint ix, uint iy, uint seed)
    {
        unchecked
        {
            uint h = ix * 374761393u + iy * 668265263u + seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    static float GradDot(long cx, long cy, uint seed, float dx, float dy)
    {
        uint h = Hash(unchecked((uint)cx), unchecked((uint)cy), seed);
        int g = (int)(h >> 27); // top 5 bits -> 32 directions
        return kGradX[g] * dx + kGradY[g] * dy;
    }

    // 2D gradient noise, roughly [-1, 1].
    public static float GNoise(float x, float y, uint seed)
    {
        long ix = (long)Mathf.Floor(x);
        long iy = (long)Mathf.Floor(y);
        float fx = x - ix, fy = y - iy;
        float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f); // quintic fade
        float uy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);

        float n00 = GradDot(ix,     iy,     seed, fx,      fy);
        float n10 = GradDot(ix + 1, iy,     seed, fx - 1f, fy);
        float n01 = GradDot(ix,     iy + 1, seed, fx,      fy - 1f);
        float n11 = GradDot(ix + 1, iy + 1, seed, fx - 1f, fy - 1f);
        float nx0 = n00 + ux * (n10 - n00);
        float nx1 = n01 + ux * (n11 - n01);
        return (nx0 + uy * (nx1 - nx0)) * 1.6f;
    }

    public static float Fbm(float x, float y, int octaves, uint seed)
    {
        float s = 0f, amp = 1f, freq = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            s += amp * GNoise(x * freq + i * 19.19f, y * freq - i * 7.77f, seed + (uint)(i * 131));
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return s / norm;
    }

    public static float Smoothstep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // ---- 3D value noise (IQ-style x + y*157-per-z corner hashing) ----
    static float H3(long ix, long iy, long iz, uint seed)
    {
        return Hash(unchecked((uint)ix), unchecked((uint)(iy + iz * 157)), seed) * (1f / 4294967296f);
    }

    // 3D value noise in [0, 1], cubic-smoothed.
    public static float VNoise3(float x, float y, float z, uint seed)
    {
        long ix = (long)Mathf.Floor(x), iy = (long)Mathf.Floor(y), iz = (long)Mathf.Floor(z);
        float fx = x - ix, fy = y - iy, fz = z - iz;
        float ux = fx * fx * (3f - 2f * fx);
        float uy = fy * fy * (3f - 2f * fy);
        float uz = fz * fz * (3f - 2f * fz);

        float n000 = H3(ix, iy, iz, seed),         n100 = H3(ix + 1, iy, iz, seed);
        float n010 = H3(ix, iy + 1, iz, seed),     n110 = H3(ix + 1, iy + 1, iz, seed);
        float n001 = H3(ix, iy, iz + 1, seed),     n101 = H3(ix + 1, iy, iz + 1, seed);
        float n011 = H3(ix, iy + 1, iz + 1, seed), n111 = H3(ix + 1, iy + 1, iz + 1, seed);

        float nx00 = n000 + ux * (n100 - n000), nx10 = n010 + ux * (n110 - n010);
        float nx01 = n001 + ux * (n101 - n001), nx11 = n011 + ux * (n111 - n011);
        float ny0 = nx00 + uy * (nx10 - nx00), ny1 = nx01 + uy * (nx11 - nx01);
        return ny0 + uz * (ny1 - ny0);
    }

    // Ridged multifractal on top of the 2D gradient noise: sharp jagged
    // ridgelines instead of blobby fbm hills (Musgrave-style ridge transform;
    // each octave weighted by the previous octave's ridge value, so peaks
    // compound into dramatic, contrasty ridgelines while valleys stay smooth).
    public static float RidgedFbm(float x, float y, int octaves, uint seed, float gain = 0.5f, float lac = 2f)
    {
        float s = 0f, amp = 0.5f, freq = 1f, prev = 1f;
        for (int i = 0; i < octaves; i++)
        {
            float n = GNoise(x * freq + i * 19.19f, y * freq - i * 7.77f, seed + (uint)(i * 131));
            float r = 1f - Mathf.Abs(Mathf.Clamp(n, -1f, 1f));
            r *= r;
            s += amp * r * prev;
            prev = r;
            amp *= gain;
            freq *= lac;
        }
        return s;
    }

    // IQ-style fbm: amplitudes 0.5/0.25/0.125..., NOT normalized -> [0, ~0.94].
    // Squash the y coordinate before calling to get horizontally-striated
    // erosion features (the signature of IQ's Canyon displacement).
    public static float Fbm3(float x, float y, float z, int octaves, uint seed)
    {
        float s = 0f, amp = 0.5f, f = 1f;
        for (int o = 0; o < octaves; o++)
        {
            s += amp * VNoise3(x * f + o * 13.1f, y * f + o * 7.7f, z * f - o * 5.3f, seed + (uint)(o * 131));
            amp *= 0.5f;
            f *= 2.02f;
        }
        return s;
    }

    // Worley/cellular noise: distance to the nearest (f1) and second-nearest
    // (f2) of a jittered grid of feature points, one per unit cell. f2-f1 is
    // ~0 exactly on a cell boundary and grows toward each cell's center --
    // thresholding it gives cracked/veined patterns (crevasses, permafrost
    // polygon troughs) that plain fbm/ridged noise can't produce. Same
    // integer-hash jitter as GNoise's gradient lookup, so it's just as
    // deterministic/GPU-portable.
    public static void Worley2(float x, float y, uint seed, out float f1, out float f2)
    {
        int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
        f1 = float.MaxValue; f2 = float.MaxValue;
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                int gx = cx + ox, gy = cy + oy;
                uint h = Hash(unchecked((uint)gx), unchecked((uint)gy), seed);
                float jx = (h & 0xFFFFu) / 65536f;
                float jy = ((h >> 16) & 0xFFFFu) / 65536f;
                float dx = gx + jx - x, dy = gy + jy - y;
                float d2 = dx * dx + dy * dy;
                if (d2 < f1) { f2 = f1; f1 = d2; }
                else if (d2 < f2) { f2 = d2; }
            }
        }
        f1 = Mathf.Sqrt(f1);
        f2 = Mathf.Sqrt(f2);
    }
}
