// Procedural creature skin patterns, evaluated in the mesh's own surface coordinates so they stick
// to the body as it bends. Kept apart from the lighting so hand-painted textures can replace it
// later behind a keyword.
//
// Surface space: the part's arc length s (uv0.x, rest metres) and its angle (uv0.y, 0..1 starting
// at the belly) are embedded on a cylinder of the vertex's rest radius r around its bone axis:
//     P = (s, r cos(2 pi a), r sin(2 pi a))
// so noise is isotropic in metres on the skin, seamless around the ring, and shrinks with the
// taper of the tail. All loops have constant bounds.
#ifndef CREATURE_PATTERN_INCLUDED
#define CREATURE_PATTERN_INCLUDED

#define CREATURE_PART_BODY 0
#define CREATURE_PART_LEG  1
#define CREATURE_PART_FOOT 2
#define CREATURE_PART_EYE  3
#define CREATURE_PART_BAND 9   // arthropod knee bands (ArthropodLegMesh.PartBand; the octopus uses 4..7, the wyvern 8)

// CreatureSpecies.cs enum CreaturePattern
#define CREATURE_PATTERN_NONE         0
#define CREATURE_PATTERN_BANDS        1
#define CREATURE_PATTERN_SPOTS        2
#define CREATURE_PATTERN_STRIPES      3
#define CREATURE_PATTERN_BANDED_SPOTS 4

// ---------------------------------------------------------------- noise

uint3 CreaturePcg3(uint3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    return v;
}

float CreatureHash01(int3 c, uint salt)
{
    return (float)(CreaturePcg3(asuint(c) ^ salt).x >> 8) * (1.0 / 16777216.0);
}

// Trilinear value noise, smoothstep-faded, 0..1.
float CreatureValueNoise(float3 p, uint salt)
{
    float3 i = floor(p);
    float3 f = p - i;
    float3 u = f * f * (3.0 - 2.0 * f);
    int3 c = (int3)i;
    float n000 = CreatureHash01(c + int3(0, 0, 0), salt);
    float n100 = CreatureHash01(c + int3(1, 0, 0), salt);
    float n010 = CreatureHash01(c + int3(0, 1, 0), salt);
    float n110 = CreatureHash01(c + int3(1, 1, 0), salt);
    float n001 = CreatureHash01(c + int3(0, 0, 1), salt);
    float n101 = CreatureHash01(c + int3(1, 0, 1), salt);
    float n011 = CreatureHash01(c + int3(0, 1, 1), salt);
    float n111 = CreatureHash01(c + int3(1, 1, 1), salt);
    float x00 = lerp(n000, n100, u.x), x10 = lerp(n010, n110, u.x);
    float x01 = lerp(n001, n101, u.x), x11 = lerp(n011, n111, u.x);
    return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
}

// Two octaves (constant loop), 0..1.
float CreatureFbm(float3 p, uint salt)
{
    float sum = 0.0, amp = 0.65, norm = 0.0;
    [unroll] for (int o = 0; o < 2; o++)
    {
        sum += amp * CreatureValueNoise(p, salt + (uint)o * 0x9E3779B9u);
        norm += amp;
        p = p * 2.31 + float3(17.3, -9.1, 5.7);
        amp *= 0.5;
    }
    return sum / norm;
}

// ---------------------------------------------------------------- pattern

// Surface point for a fragment (see header); the per-individual seed shifts the whole pattern.
float3 CreatureSurfacePoint(float2 uv0, float2 uv1, float radius, float seed)
{
    uint3 h = CreaturePcg3(uint3(asuint(seed), 0x68E31DA4u, 0xB5297A4Du));
    float3 offset = float3(h & 0xFFFFu) * (61.0 / 65536.0);
    float phi = uv0.y * 6.2831853;
    float part = floor(uv1.x + 0.5);
    return float3(uv0.x + part * 7.31, radius * cos(phi), radius * sin(phi)) + offset;
}

// Pattern masks for one fragment, contrast applied:
//   x  dark pattern amount (lerp the base colour toward the pattern colour)
//   y  pale spot amount (lerp toward a pale tone)
// params = (scale: bands per metre / feature frequency, contrast, softness: edge width as a fraction
// of a period, spotScale: spot size relative to the band period).
// body = _BodyParams (body length, 1 / length, arc length where the tail begins, 0).
// mode (creature kit T4.1, the part's layer row: CreatureLayerPatternMode): 0 none, 1 the body's pattern, 2 the
// limbs' (mottle / the species' spots). CreaturePattern below is the part-id form (legacy ids).
float2 CreaturePatternMode(float2 uv0, float2 uv1, float seed, float patternType, float4 params,
                           float radius, float4 body, int mode)
{
    int type = (int)(patternType + 0.5);
    if (type == CREATURE_PATTERN_NONE || mode == 0) return 0.0;

    float s = uv0.x;
    float dorsal = uv1.y;
    float scale = max(params.x, 0.01);
    float contrast = saturate(params.y);
    float soft = clamp(params.z, 0.02, 1.0);
    float spotFreq = scale / clamp(params.w, 0.02, 4.0);
    float3 P = CreatureSurfacePoint(uv0, uv1, radius, seed);

    // spots: thresholded value noise, rounder than fbm alone, with a little fine breakup
    float spotN = 0.8 * CreatureValueNoise(P * spotFreq, 0xA511E9B3u)
                + 0.2 * CreatureValueNoise(P * (spotFreq * 2.7), 0x63D83595u);
    float spots = smoothstep(0.64 - 0.12 * soft, 0.64 + 0.12 * soft, spotN);

    if (mode == 2)
    {
        // limbs: mottled, with the species' spots where it has them
        float mottle = smoothstep(0.52, 0.78, CreatureFbm(P * (scale * 1.6), 0x1B873593u));
        float pale = (type == CREATURE_PATTERN_SPOTS) ? 0.0 : (type == CREATURE_PATTERN_BANDED_SPOTS ? 0.7 * spots : 0.0);
        float dark = (type == CREATURE_PATTERN_SPOTS) ? spots : 0.55 * mottle;
        return float2(dark, pale) * contrast;
    }

    // ---- body
    float len = max(body.x, 1e-3);
    float tailK = saturate((s - body.z) / max(len - body.z, 1e-3));   // 0 at the hips .. 1 at the tail tip
    float headFade = smoothstep(0.04 * len, 0.14 * len, s);          // plainer head and throat
    float warp = CreatureFbm(P * (scale * 0.8), 0xCC9E2D51u) - 0.5;
    float edgeN = CreatureValueNoise(P * (scale * 3.0), 0xE6546B64u) - 0.5;

    float dark = 0.0, pale = 0.0;
    if (type == CREATURE_PATTERN_BANDS || type == CREATURE_PATTERN_BANDED_SPOTS)
    {
        // cross-bands, swept back slightly on the flanks, wobbling and ragged at the edges
        float b = P.x * scale + 0.9 * warp - 0.25 * (1.0 - dorsal);   // P.x = s + the seed's offset
        float d = abs(frac(b) - 0.5) * 2.0;           // 0 at a band's centre, 1 midway between bands
        d += 0.35 * edgeN;
        float band = 1.0 - smoothstep(0.45 - 0.5 * soft, 0.45 + 0.5 * soft, d);
        // stronger along the tail, where the bands also close into rings under it
        float strength = lerp(0.7, 1.0, smoothstep(0.0, 0.5, tailK)) * lerp(0.2, 1.0, headFade);
        float bellyEdge = lerp(-0.3, -1.3, smoothstep(0.2, 0.75, tailK));
        float side = smoothstep(bellyEdge - 0.25, bellyEdge + 0.25, dorsal);
        dark = band * strength * side;
        if (type == CREATURE_PATTERN_BANDED_SPOTS)
        {
            // small pale spots break the bands up; sparser on the ground colour between them
            float sp = spots * side * lerp(0.4, 1.0, band) * lerp(0.5, 1.0, headFade);
            pale = sp;
            dark *= 1.0 - sp;
        }
    }
    else if (type == CREATURE_PATTERN_SPOTS)
    {
        dark = spots * smoothstep(-0.65, -0.2, dorsal);
    }
    else if (type == CREATURE_PATTERN_STRIPES)
    {
        // stripes along the body: an integer count around keeps the belly seam closed
        float count = max(2.0, floor(scale * 0.5 + 0.5));
        float b = (uv0.y - 0.5) * count + 0.3 * warp;
        float d = abs(b - floor(b + 0.5)) * 2.0;
        d += 0.25 * edgeN;
        float stripe = 1.0 - smoothstep(0.4 - 0.5 * soft, 0.4 + 0.5 * soft, d);
        dark = stripe * smoothstep(-0.7, -0.3, dorsal);
    }
    return float2(dark, pale) * contrast;
}

// The part-id form: eyes and feet plain, limbs the limb pattern, every other id the body's.
float2 CreaturePattern(float2 uv0, float2 uv1, float seed, float patternType, float4 params,
                       float radius, float4 body)
{
    int part = (int)(uv1.x + 0.5);
    int mode = (part == CREATURE_PART_EYE || part == CREATURE_PART_FOOT) ? 0 : (part == CREATURE_PART_LEG ? 2 : 1);
    return CreaturePatternMode(uv0, uv1, seed, patternType, params, radius, body, mode);
}

// ---------------------------------------------------------------- scale grain

// A grid of small scales in (arc length, angle) space: rows every rowSpacing metres, a constant
// number of columns around each ring (so scales shrink with the taper, as on a real tail), every
// other row offset by half a scale (smaller on the limbs). Returns x = per-scale tint (-1..1), y = height (0 in the
// grooves .. 1 on a scale's crown), z = visibility (fades out once a scale is under ~2 pixels).
// fine (creature kit T4.1, the layer row's FineGrain): the limbs' smaller scales. CreatureScaleGrain below is
// the part-id form.
float3 CreatureScaleGrainFine(float2 uv0, float2 uv1, float rowSpacing, float columns, bool fine)
{
    // limbs and feet carry smaller scales; the column count stays an integer (closed seam)
    int part = (int)(uv1.x + 0.5);
    if (fine) { rowSpacing *= 0.6; columns *= 0.4; }
    columns = max(floor(columns + 0.5), 4.0);
    float2 g = float2(uv0.x / max(rowSpacing, 1e-4), uv0.y * columns);
    float2 w = fwidth(g);                             // before the row offset: no spikes at row edges
    float row = floor(g.x);
    g.y += 0.5 * (row - 2.0 * floor(row * 0.5));
    float2 cell = floor(g);
    float2 f = g - cell;
    cell.y -= columns * floor(cell.y / columns);      // the scale cut by the seam keeps one tint
    float tint = CreatureHash01(int3((int2)cell, part), 0x2545F491u) * 2.0 - 1.0;
    // rounded crown, flat-ish top, narrow grooves
    float2 e = min(f, 1.0 - f) * 2.0;                 // 0 at the edges .. 1 at the centre
    float height = smoothstep(0.0, 0.55, e.x) * smoothstep(0.0, 0.45, e.y);
    float vis = saturate(1.6 - 2.2 * max(w.x, w.y));
    return float3(tint, height, vis);
}

float3 CreatureScaleGrain(float2 uv0, float2 uv1, float rowSpacing, float columns)
{
    int part = (int)(uv1.x + 0.5);
    return CreatureScaleGrainFine(uv0, uv1, rowSpacing, columns, part == CREATURE_PART_LEG || part == CREATURE_PART_FOOT);
}

#endif
