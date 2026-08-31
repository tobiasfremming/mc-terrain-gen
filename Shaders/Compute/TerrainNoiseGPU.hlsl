#ifndef MC_TERRAIN_NOISE_GPU_INCLUDED
#define MC_TERRAIN_NOISE_GPU_INCLUDED

// GPU port of Scripts/TerrainNoise.cs -- keep the two files in lockstep; any
// change to one must be mirrored in the other, or CPU (Sample/Gradient,
// still authoritative for per-vertex normals/AO) and GPU (bulk grid fill)
// will disagree.
//
// Deliberate, harmless deviation from the C# version: TerrainNoise.cs
// precomputes a 32-entry cos/sin gradient table once at class-init (a CPU
// micro-optimization, since HLSL has no static constructors); this port
// computes cos/sin inline per call instead -- GPU trig is cheap, and it's
// the exact same formula at the exact same 32 fixed angles, just not cached.
//
// Porting invariant: TerrainNoise.Hash takes `long` cell coordinates in C#
// and truncates to uint; here we use `int` (32-bit) reinterpreted as uint.
// Identical for any coordinate this project's world extents actually reach
// (world sizes here are in the hundreds/low thousands of meters).

// LOD band-limiting -- 1:1 mirror of TerrainNoise.DetailFade / the
// filterWidth parameters in Scripts/TerrainNoise.cs. Read that file's header
// for why this exists; the two MUST stay in lockstep or GPU-filled and
// CPU-filled chunks disagree and seams crack.
// Keep in lockstep with kFadeStart / kFadeEnd in Scripts/TerrainNoise.cs --
// see that file for what the numbers mean and how to tune them.
static const float MC_FADE_START = 0.30;
static const float MC_FADE_END = 1.20;

// Mirrors kBandLimit in Scripts/TerrainNoise.cs -- flip BOTH together. See
// that comment for what the off state is for.
#define MC_BAND_LIMIT 0

float MC_DetailFade(float filterWidth, float featureSize)
{
#if !MC_BAND_LIMIT
    return 1.0;
#endif
    if (filterWidth <= 0.0 || featureSize <= 0.0) return 1.0;
    float t = saturate((filterWidth / featureSize - MC_FADE_START) / (MC_FADE_END - MC_FADE_START));
    return 1.0 - t * t * (3.0 - 2.0 * t);
}

uint MC_Hash(uint ix, uint iy, uint seed)
{
    uint h = ix * 374761393u + iy * 668265263u + seed * 2246822519u;
    h = (h ^ (h >> 13)) * 1274126177u;
    return h ^ (h >> 16);
}

float2 MC_GradDir(uint h)
{
    uint g = h >> 27; // top 5 bits -> 32 directions
    float ang = (float)g * (6.283185307179586 / 32.0);
    return float2(cos(ang), sin(ang));
}

float MC_GradDot(int cx, int cy, uint seed, float dx, float dy)
{
    float2 g = MC_GradDir(MC_Hash((uint)cx, (uint)cy, seed));
    return g.x * dx + g.y * dy;
}

// 2D gradient noise, roughly [-1, 1].
float MC_GNoise(float x, float y, uint seed)
{
    int ix = (int)floor(x);
    int iy = (int)floor(y);
    float fx = x - ix, fy = y - iy;
    float ux = fx * fx * fx * (fx * (fx * 6.0 - 15.0) + 10.0); // quintic fade
    float uy = fy * fy * fy * (fy * (fy * 6.0 - 15.0) + 10.0);

    float n00 = MC_GradDot(ix,     iy,     seed, fx,       fy);
    float n10 = MC_GradDot(ix + 1, iy,     seed, fx - 1.0, fy);
    float n01 = MC_GradDot(ix,     iy + 1, seed, fx,       fy - 1.0);
    float n11 = MC_GradDot(ix + 1, iy + 1, seed, fx - 1.0, fy - 1.0);
    float nx0 = n00 + ux * (n10 - n00);
    float nx1 = n01 + ux * (n11 - n01);
    return (nx0 + uy * (nx1 - nx0)) * 1.6;
}

// `norm` accumulates unweighted on purpose -- see the C# Fbm's comment.
float MC_Fbm(float x, float y, int octaves, uint seed, float filterWidth)
{
    float s = 0.0, amp = 1.0, freq = 1.0, norm = 0.0;
    [loop] for (int i = 0; i < octaves; i++)
    {
        norm += amp;
        float w = MC_DetailFade(filterWidth * freq, 1.0);
        if (w > 0.0)
            s += amp * w * MC_GNoise(x * freq + i * 19.19, y * freq - i * 7.77, seed + (uint)(i * 131));
        amp *= 0.5;
        freq *= 2.0;
    }
    return s / norm;
}

float MC_Smoothstep(float a, float b, float x)
{
    float t = saturate((x - a) / (b - a));
    return t * t * (3.0 - 2.0 * t);
}

float MC_H3(int ix, int iy, int iz, uint seed)
{
    return (float)MC_Hash((uint)ix, (uint)(iy + iz * 157), seed) * (1.0 / 4294967296.0);
}

// 3D value noise in [0, 1], cubic-smoothed.
float MC_VNoise3(float x, float y, float z, uint seed)
{
    int ix = (int)floor(x), iy = (int)floor(y), iz = (int)floor(z);
    float fx = x - ix, fy = y - iy, fz = z - iz;
    float ux = fx * fx * (3.0 - 2.0 * fx);
    float uy = fy * fy * (3.0 - 2.0 * fy);
    float uz = fz * fz * (3.0 - 2.0 * fz);

    float n000 = MC_H3(ix, iy, iz, seed),         n100 = MC_H3(ix + 1, iy, iz, seed);
    float n010 = MC_H3(ix, iy + 1, iz, seed),     n110 = MC_H3(ix + 1, iy + 1, iz, seed);
    float n001 = MC_H3(ix, iy, iz + 1, seed),     n101 = MC_H3(ix + 1, iy, iz + 1, seed);
    float n011 = MC_H3(ix, iy + 1, iz + 1, seed), n111 = MC_H3(ix + 1, iy + 1, iz + 1, seed);

    float nx00 = n000 + ux * (n100 - n000), nx10 = n010 + ux * (n110 - n010);
    float nx01 = n001 + ux * (n101 - n001), nx11 = n011 + ux * (n111 - n011);
    float ny0 = nx00 + uy * (nx10 - nx00), ny1 = nx01 + uy * (nx11 - nx01);
    return ny0 + uz * (ny1 - ny0);
}

// Ridged multifractal on top of the 2D gradient noise (Musgrave-style ridge
// transform): sharp jagged ridgelines instead of blobby fbm hills. gain/lac
// are always (0.5, 2.0) at every call site in this project (C#'s default
// parameter values), but kept explicit here since HLSL has no defaults.
float MC_RidgedFbm(float x, float y, int octaves, uint seed, float gain, float lac, float filterWidth)
{
    float s = 0.0, amp = 0.5, freq = 1.0, prev = 1.0;
    [loop] for (int i = 0; i < octaves; i++)
    {
        float w = MC_DetailFade(filterWidth * freq, 1.0);
        if (w <= 0.0) break; // freq only grows, so every later octave is out too
        float n = MC_GNoise(x * freq + i * 19.19, y * freq - i * 7.77, seed + (uint)(i * 131));
        float r = 1.0 - abs(clamp(n, -1.0, 1.0));
        r *= r;
        s += amp * r * prev * w;
        prev = r;
        amp *= gain;
        freq *= lac;
    }
    return s;
}

// IQ-style fbm: amplitudes 0.5/0.25/0.125..., NOT normalized -> [0, ~0.94].
float MC_Fbm3(float x, float y, float z, int octaves, uint seed, float filterWidth)
{
    float s = 0.0, amp = 0.5, f = 1.0;
    [loop] for (int o = 0; o < octaves; o++)
    {
        float w = MC_DetailFade(filterWidth * f, 1.0);
        if (w <= 0.0) break;
        s += amp * w * MC_VNoise3(x * f + o * 13.1, y * f + o * 7.7, z * f - o * 5.3, seed + (uint)(o * 131));
        amp *= 0.5;
        f *= 2.02;
    }
    return s;
}

// Worley/cellular noise -- 1:1 port of TerrainNoise.Worley2. See its comment;
// keep the two in lockstep (same MC_Hash jitter, same 3x3 neighborhood scan).
void MC_Worley2(float x, float y, uint seed, out float f1, out float f2)
{
    int cx = (int)floor(x), cy = (int)floor(y);
    f1 = 3.402823e38; f2 = 3.402823e38;
    // Rolled, not unrolled: 9 cells x a hash each, and Frost calls this twice
    // per sample. See MC_RidgedFbm's note on why code size is the constraint.
    [loop] for (int oy = -1; oy <= 1; oy++)
    {
        [loop] for (int ox = -1; ox <= 1; ox++)
        {
            int gx = cx + ox, gy = cy + oy;
            uint h = MC_Hash((uint)gx, (uint)gy, seed);
            float jx = (float)(h & 0xFFFFu) / 65536.0;
            float jy = (float)((h >> 16) & 0xFFFFu) / 65536.0;
            float dx = (float)gx + jx - x, dy = (float)gy + jy - y;
            float d2 = dx * dx + dy * dy;
            if (d2 < f1) { f2 = f1; f1 = d2; }
            else if (d2 < f2) { f2 = d2; }
        }
    }
    f1 = sqrt(f1);
    f2 = sqrt(f2);
}

#endif
