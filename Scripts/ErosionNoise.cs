using System;
using UnityEngine;

// Rune Skovbo Johansen's "fast and gorgeous" erosion filter, as a general
// generator any heightfield can run its terrain through.
//
//   https://blog.runevision.com/2026/03/fast-and-gorgeous-erosion-filter.html
//   Advanced Terrain Erosion Filter, (c) 2025 Rune Skovbo Johansen, MPL-2.0
//   (https://www.shadertoy.com/view/wXcfWn); this C# follows Luke Mitchell's
//   Burst translation (https://github.com/lpmitchell/AdvancedTerrainErosion).
//   This Source Code Form is subject to the terms of the Mozilla Public
//   License, v. 2.0: https://mozilla.org/MPL/2.0/.
//
// It is not a simulation. Every point is evaluated on its own from the input
// height and its gradient, so it fits this project's contract (pure function
// of position and spacing, chunked, GPU-mirrored) without any change to the
// meshing pipeline. What it produces: gullies as stripes perpendicular to the
// downhill direction, blended from a jittered grid of cells ("phacelle"
// noise -- a phase per cell), stacked over octaves where each octave's
// gullies fade toward the previous octave's height at ridges and creases, so
// small gullies branch off big ones instead of crossing them.
//
// Units are METRES throughout. A slope is metres per metre; `scale` is the
// wavelength of the largest gullies; `strength` is a fraction of `scale`, so
// the first octave's gully amplitude is strength * scale metres.
//
// Mirrored 1:1 by Shaders/Compute/ErosionNoiseGPU.hlsl. Keep the two in
// lockstep, including loop order: the CPU is still authoritative for normals.
[Serializable]
public struct ErosionParams
{
    [Tooltip("Metres. Wavelength of the largest gullies; every later octave is `lacunarity` times finer.")]
    public float scale;
    [Tooltip("Gully amplitude as a fraction of scale (first octave). 0.15-0.25 is mountainous; the halving per octave comes from gain.")]
    public float strength;
    [Tooltip("Scales the gullies before they are faded toward the fade target. 1 = full; lower gives softer, more rounded erosion.")]
    public float gullyWeight;
    [Tooltip("How much each finer octave is restricted to the steep parts of the previous one (stacked fading exponent). 1 = hardly, 2 = strongly.")]
    public float detail;
    [Tooltip("Rounding of ridges (x) and creases (y), the multiplier applied to the input terrain's rounding (z), and the per-octave multiplier that counters lacunarity (w). Larger y = softer, sediment-filled gully floors.")]
    public Vector4 rounding;
    [Tooltip("How quickly the gully mask reaches full strength with slope: for the input terrain (x), for each octave (y), and the same pair for the ridge map (z, w). Higher = gullies appear on gentler ground.")]
    public Vector4 onset;
    [Tooltip("The slope the gullies are steered by is a mix (y) of the real input slope and a normalised slope of magnitude x. Pretending a constant slope keeps gullies straight where the input is nearly flat.")]
    public Vector2 assumedSlope;
    [Tooltip("Stripes per cell inside the phacelle noise. Keep near 0.7; high values distort.")]
    public float cellScale;
    [Tooltip("Gully octaves. Each is `lacunarity` times finer; the finest one should be a few metres.")]
    public int octaves;
    [Tooltip("Amplitude multiplier per octave.")]
    public float gain;
    [Tooltip("Frequency multiplier per octave.")]
    public float lacunarity;
    [Tooltip("0..1. How far interpolated stripes are renormalised to unit magnitude. 0.5 normalises anything above half magnitude; 1 normalises everything (spiky artefacts where cells disagree).")]
    public float normalization;

    // Rune's reference settings, in metres for a ~1.5 km mountain wavelength.
    public static ErosionParams Mountain => new ErosionParams
    {
        scale = 400f, strength = 0.16f, gullyWeight = 0.5f, detail = 1.5f,
        rounding = new Vector4(0.1f, 0f, 0.1f, 2f), onset = new Vector4(1.25f, 1.25f, 2.8f, 1.5f),
        assumedSlope = new Vector2(0.7f, 1f), cellScale = 0.7f, octaves = 7, gain = 0.5f,
        lacunarity = 2f, normalization = 0.5f,
    };

    // Rounder creases and a slower onset: gullies that read as sand-choked.
    public static ErosionParams DesertMountain => new ErosionParams
    {
        scale = 300f, strength = 0.15f, gullyWeight = 0.55f, detail = 1.2f,
        rounding = new Vector4(0.15f, 0.6f, 0.15f, 1.6f), onset = new Vector4(1.1f, 1.25f, 2.8f, 1.5f),
        assumedSlope = new Vector2(0.6f, 1f), cellScale = 0.7f, octaves = 6, gain = 0.5f,
        lacunarity = 2f, normalization = 0.5f,
    };

    // Conservative bound on |height delta| the filter can add, in metres:
    // every octave's gullies are within [-1, 1] * gullyWeight (or the fade
    // target, also in [-1, 1]) times that octave's strength.
    public float MaxHeightDelta()
    {
        float sum = 0f, s = strength * scale;
        for (int i = 0; i < octaves; i++) { sum += s; s *= gain; }
        return sum * Mathf.Max(1f, gullyWeight);
    }

    public float MagnitudeSum()
    {
        float sum = 0f, s = strength * scale;
        for (int i = 0; i < octaves; i++) { sum += s; s *= gain; }
        return sum;
    }
}

public static class ErosionNoise
{
    const float kTau = 6.2831853071795864f;
    const float kEpsilon = 1e-10f;

    static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    static float PowInv(float t, float power) => 1f - Mathf.Pow(1f - Clamp01(t), power);
    static float EaseOut(float t) { float v = 1f - Clamp01(t); return 1f - v * v; }
    static float SmoothStart(float t, float smoothing)
    {
        if (t >= smoothing) return t - 0.5f * smoothing;
        return 0.5f * t * t / smoothing;
    }

    // Random offset of a cell's point, [-0.5, 0.5] per axis, from the
    // project's integer hash (same jitter split as TerrainNoise.Worley2).
    static void CellJitter(long gx, long gy, uint seed, out float jx, out float jy)
    {
        uint h = TerrainNoise.Hash(unchecked((uint)gx), unchecked((uint)gy), seed);
        jx = (h & 0xFFFFu) / 65536f - 0.5f;
        jy = ((h >> 16) & 0xFFFFu) / 65536f - 0.5f;
    }

    // Phacelle noise: a stripe pattern perpendicular to `dir` (normalised),
    // `freq` stripes per unit cell, blended from the 4x4 cells around p. The
    // stripes are a cosine (x) with its sine (y) alongside, and the side
    // vector (zw) that turns the sine into the cosine's derivative:
    //   d cos / dp = -sin * sideDir.
    // Interpolating (cos, sin) pairs as points on a circle and renormalising
    // keeps the amplitude constant where neighbouring cells' phases disagree.
    public static Vector4 Phacelle(float px, float py, float dirX, float dirY, float freq, float offset,
                                   float normalization, uint seed)
    {
        float sideX = -dirY * freq * kTau;
        float sideY = dirX * freq * kTau;
        offset *= kTau;

        float pIntX = Mathf.Floor(px), pIntY = Mathf.Floor(py);
        float pFracX = px - pIntX, pFracY = py - pIntY;
        long cellX = (long)pIntX, cellY = (long)pIntY;

        float cx = 0f, sy = 0f, weightSum = 0f;
        for (int i = -1; i <= 2; i++)
        {
            for (int j = -1; j <= 2; j++)
            {
                CellJitter(cellX + i, cellY + j, seed, out float rx, out float ry);
                float vx = pFracX - i - rx;
                float vy = pFracY - j - ry;
                float sqrDist = vx * vx + vy * vy;
                // bell weight, exactly 0 at the 1.5-cell reach of the 4x4 grid
                float w = Mathf.Max(0f, Mathf.Exp(-sqrDist * 2f) - 0.01111f);
                weightSum += w;
                float wave = vx * sideX + vy * sideY + offset;
                cx += Mathf.Cos(wave) * w;
                sy += Mathf.Sin(wave) * w;
            }
        }
        float ic = cx / weightSum, isn = sy / weightSum;
        float magnitude = Mathf.Sqrt(ic * ic + isn * isn);
        magnitude = Mathf.Max(1f - normalization, magnitude);
        return new Vector4(ic / magnitude, isn / magnitude, sideX, sideY);
    }

    // The filter. `heightAndSlope` is the input terrain at (wx, wz): height
    // in x, d/dx in y, d/dz in z, all metres. `fadeTarget` is -1 at valley
    // floors and +1 at peaks (overshoot is clamped): it is what the gullies
    // fade toward where the ground is flat, which is what lets peaks stay
    // sharp and valleys stay hollow at the same time.
    //
    // Returns the DELTA to add to heightAndSlope (x height, yz slope) and the
    // summed octave strength in w (for offsetting by "magnitude"); ridgeMap
    // is -1 along creases and +1 along ridges, 0 elsewhere.
    //
    // `filterWidth` band-limits: an octave whose gully wavelength the sample
    // spacing cannot resolve is faded out entirely (gullies AND baseline),
    // and every later octave is finer, so the loop stops there.
    public static Vector4 Filter(float wx, float wz, Vector3 heightAndSlope, float fadeTarget,
                                 in ErosionParams p, uint seed, float filterWidth, out float ridgeMap)
    {
        float strength = p.strength * p.scale;
        fadeTarget = Mathf.Clamp(fadeTarget, -1f, 1f);

        Vector3 input = heightAndSlope;
        float freq = 1f / (p.scale * p.cellScale);
        float wavelength = p.scale;
        float slopeLength = Mathf.Max(Mathf.Sqrt(heightAndSlope.y * heightAndSlope.y + heightAndSlope.z * heightAndSlope.z), kEpsilon);
        float magnitude = 0f;
        float roundingMult = 1f;

        float roundingForInput = Mathf.Lerp(p.rounding.y, p.rounding.x, Clamp01(fadeTarget + 0.5f)) * p.rounding.z;
        float combiMask = EaseOut(SmoothStart(slopeLength * p.onset.x, roundingForInput * p.onset.x));

        float ridgeCombiMask = EaseOut(slopeLength * p.onset.z);
        float ridgeFadeTarget = fadeTarget;

        // the slope the gullies follow: real slope mixed with an assumed one
        float gx = Mathf.Lerp(heightAndSlope.y, heightAndSlope.y / slopeLength * p.assumedSlope.x, p.assumedSlope.y);
        float gz = Mathf.Lerp(heightAndSlope.z, heightAndSlope.z / slopeLength * p.assumedSlope.x, p.assumedSlope.y);

        for (int i = 0; i < p.octaves; i++)
        {
            float w = TerrainNoise.DetailFade(filterWidth, wavelength);
            if (w <= 0f) break;

            float gl = Mathf.Sqrt(gx * gx + gz * gz);
            float nx = gl > kEpsilon ? gx / gl : 0f;
            float nz = gl > kEpsilon ? gz / gl : 0f;
            Vector4 ph = Phacelle(wx * freq, wz * freq, nx, nz, p.cellScale, 0.25f, p.normalization, seed);
            // p was scaled by freq, so the side vector is per metre times
            // freq; negated because slopes here point downhill
            float sideX = ph.z * -freq, sideZ = ph.w * -freq;

            float sloping = Mathf.Abs(ph.y);

            // steer later octaves with the unfaded, sign-straightened slope
            float sg = ph.y > 0f ? 1f : (ph.y < 0f ? -1f : 0f);
            gx += sg * sideX * strength * p.gullyWeight;
            gz += sg * sideZ * strength * p.gullyWeight;

            float gH = ph.x, gX = ph.y * sideX, gZ = ph.y * sideZ;

            float fH = Mathf.Lerp(fadeTarget, gH * p.gullyWeight, combiMask);
            float fX = gX * p.gullyWeight * combiMask;
            float fZ = gZ * p.gullyWeight * combiMask;

            float sw = strength * w;
            heightAndSlope.x += fH * sw;
            heightAndSlope.y += fX * sw;
            heightAndSlope.z += fZ * sw;
            magnitude += sw;

            fadeTarget = fH;

            float roundingForOctave = Mathf.Lerp(p.rounding.y, p.rounding.x, Clamp01(ph.x + 0.5f)) * roundingMult;
            float newMask = EaseOut(SmoothStart(sloping * p.onset.y, roundingForOctave * p.onset.y));
            combiMask = PowInv(combiMask, p.detail) * newMask;

            ridgeFadeTarget = Mathf.Lerp(ridgeFadeTarget, gH, ridgeCombiMask);
            ridgeCombiMask *= EaseOut(sloping * p.onset.w);

            strength *= p.gain;
            freq *= p.lacunarity;
            wavelength /= p.lacunarity;
            roundingMult *= p.rounding.w;
        }

        ridgeMap = ridgeFadeTarget * (1f - ridgeCombiMask);
        Vector3 delta = heightAndSlope - input;
        return new Vector4(delta.x, delta.y, delta.z, magnitude);
    }
}
