using UnityEngine;

// Shared seeded gradient noise for all terrain fields. Deterministic across
// machines (integer hash, unlike Mathf.PerlinNoise) and mirrored 1:1 by the
// Python prototypes (scratchpad dunes.py) used to tune the terrain visually.
// Thread-safe: pure static math, called from worker threads during meshing.
//
// LOD BAND-LIMITING (`filterWidth`)
// ---------------------------------
// Every octave-stacking function below takes an optional `filterWidth`: the
// spacing between adjacent samples, in the SAME units as that call's x/y/z
// (so a caller doing Fbm(wx / peakScale, ...) passes filterWidth / peakScale).
// Octaves finer than that spacing can resolve are faded out instead of being
// point-sampled into garbage.
//
// This is not cosmetic. A clipmap at cellSize 0.5 and 6 levels samples its
// coarsest chunks at 16 m, while CanyonField's erosion term modulates density
// vertically at ~8.7 m (and its finer octaves at 4.3 m and 2.2 m). Sampling a
// 8.7 m wave at 16 m spacing does not produce a smoother version of it -- it
// produces a completely different, much lower-frequency wave, differing per
// LOD level, at full amplitude. That aliasing is what turns distant terrain
// into smooth monoliths, corduroy banding, floating sheets and checkerboarded
// isosurfaces. Fading those octaves out costs nothing visually (they were
// below the sampling resolution anyway) and is also faster.
//
// Watertightness: the fade is a function of the SAMPLE SPACING, never of the
// LOD level or camera distance, so any two evaluations at the same point and
// the same spacing agree exactly. That is necessary but NOT sufficient, and
// the subtlety cost a round of visible seams: a Transvoxel transition cell
// spans TWO spacings at once. Its full-res corners belong to the fine
// neighbour (spacing s) and its half-res corners to the coarse chunk's own
// grid (spacing 2s), and under band-limiting those are genuinely different
// fields at the same positions -- so each set has to be sampled at its own
// width and welded against its own side. See ChunkMesher.
// GenerateTransitionFace's CoarseCornerSample.
//
// Residual known limitation: Lengyel derives the transition cell's case code
// from the nine full-res corners alone, so the coarse side's TOPOLOGY still
// follows the fine field's signs. Where the two fields disagree in sign at a
// shared corner -- possible only within roughly the faded amplitude of the
// isosurface -- the coarse polygon can still differ from the neighbouring
// regular cell's. Keeping the fade band wide (see kFadeStart/kFadeEnd) is
// what holds that amplitude, and therefore that risk, small.
//
// Passing 0 (the default) disables filtering, which is what single-point
// queries -- gravity probes, terrain edits -- want.
public static class TerrainNoise
{
    // Strict Nyquist is ratio 0.5 (two samples per wavelength).
    //
    // The WIDTH of this band matters as much as where it sits. The ratio
    // doubles with every LOD level, so a narrow band means a feature is at
    // full strength on one level and gone on the next -- which reads as the
    // terrain visibly changing SHAPE at every LOD ring, not just softening.
    // Spanning a factor of four (0.30 -> 1.20) stretches each feature's
    // fade-out across roughly two levels, so detail dissolves gradually
    // instead of stepping.
    //
    // Tuning: raise kFadeStart to keep more detail per level (at the cost of
    // aliasing); widen the gap between the two to trade a little aliasing at
    // the coarse end for smoother level-to-level transitions. Keep these in
    // lockstep with MC_FADE_START / MC_FADE_END in TerrainNoiseGPU.hlsl -- if
    // CPU and GPU disagree here, the CPU-computed normals stop matching the
    // GPU-computed geometry.
    const float kFadeStart = 0.30f; // ~3.3 samples per wavelength: still fully detailed
    const float kFadeEnd = 1.20f;   // below ~0.8 samples per wavelength: nothing left to resolve

    // Master switch, mirrored by MC_BAND_LIMIT in TerrainNoiseGPU.hlsl. Set
    // BOTH to off to make the density field spacing-independent again -- every
    // LOD then evaluates the identical function, which is the assumption
    // Transvoxel is built on. That makes this the clean A/B for LOD-seam
    // cracks: if seams survive with this off, band-limiting is not what opened
    // them and the transition-cell code itself is at fault. Costs nothing to
    // leave here; the aliasing it exists to prevent comes straight back when
    // it is off, so it is a diagnostic, not a shipping option.
    const bool kBandLimit = false;

    // Amplitude weight for a feature of size `featureSize` sampled at
    // `filterWidth` spacing, both in the same units. 1 = fully resolved, 0 =
    // unresolvable (drop it). filterWidth <= 0 means "unfiltered".
    public static float DetailFade(float filterWidth, float featureSize)
    {
        // The suppression has to span the REST of the method, not just the
        // early return: when kBandLimit is const false, it is the two lines
        // below that the compiler proves unreachable, and restoring the
        // warning before them (as this used to) let CS0162 through anyway.
#pragma warning disable CS0162 // unreachable when kBandLimit is off -- deliberate
        if (!kBandLimit) return 1f;
        if (filterWidth <= 0f || featureSize <= 0f) return 1f;
        return 1f - Smoothstep(kFadeStart, kFadeEnd, filterWidth / featureSize);
#pragma warning restore CS0162
    }

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

    public static float Fbm(float x, float y, int octaves, uint seed, float filterWidth = 0f)
    {
        float s = 0f, amp = 1f, freq = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            // Octave i has wavelength 1/freq in these units, so its
            // spacing-to-wavelength ratio is filterWidth * freq.
            //
            // `norm` accumulates UNWEIGHTED on purpose: a faded octave has to
            // actually vanish, leaving a lower-amplitude result. Folding the
            // weight into norm instead would renormalise the surviving
            // octaves back up to full amplitude -- same aliasing energy,
            // just moved into the octaves that are still resolvable.
            norm += amp;
            float w = DetailFade(filterWidth * freq, 1f);
            if (w > 0f)
                s += amp * w * GNoise(x * freq + i * 19.19f, y * freq - i * 7.77f, seed + (uint)(i * 131));
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
    public static float RidgedFbm(float x, float y, int octaves, uint seed, float gain = 0.5f, float lac = 2f,
                                   float filterWidth = 0f)
    {
        float s = 0f, amp = 0.5f, freq = 1f, prev = 1f;
        for (int i = 0; i < octaves; i++)
        {
            // freq only ever grows, so once one octave is unresolvable every
            // later one is too -- bailing here is both correct and the reason
            // coarse LOD chunks get cheaper rather than just less aliased.
            float w = DetailFade(filterWidth * freq, 1f);
            if (w <= 0f) break;

            float n = GNoise(x * freq + i * 19.19f, y * freq - i * 7.77f, seed + (uint)(i * 131));
            float r = 1f - Mathf.Abs(Mathf.Clamp(n, -1f, 1f));
            r *= r;
            s += amp * r * prev * w;
            prev = r;
            amp *= gain;
            freq *= lac;
        }
        return s;
    }

    // IQ-style fbm: amplitudes 0.5/0.25/0.125..., NOT normalized -> [0, ~0.94].
    // Squash the y coordinate before calling to get horizontally-striated
    // erosion features (the signature of IQ's Canyon displacement).
    // `filterWidth` here is the LARGEST spacing across the three axes after
    // whatever per-axis squash the caller applied (Canyon squashes y by
    // disSquash before dividing by disScale, which makes the y features
    // disSquash times FINER than the x/z ones -- and it is exactly those
    // fine vertical strata that alias into the corduroy banding on distant
    // canyon walls, so the tightest axis is the one that has to drive the
    // fade, not an average).
    public static float Fbm3(float x, float y, float z, int octaves, uint seed, float filterWidth = 0f)
    {
        float s = 0f, amp = 0.5f, f = 1f;
        for (int o = 0; o < octaves; o++)
        {
            float w = DetailFade(filterWidth * f, 1f);
            if (w <= 0f) break;
            s += amp * w * VNoise3(x * f + o * 13.1f, y * f + o * 7.7f, z * f - o * 5.3f, seed + (uint)(o * 131));
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
