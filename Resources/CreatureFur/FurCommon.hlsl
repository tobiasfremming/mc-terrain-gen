// Creature fur, shared by FurSim.compute and the two fur shaders. Plain HLSL (no pipeline includes): include after
// Creature/CreatureSkin.hlsl and Creature/CreaturePattern.hlsl. Every helper is Fur-prefixed (URP's Core.hlsl
// already defines Hash and friends).
//
// Mirrors (C#, Scripts/Creatures/Fur): FurSkinVertex, FurRoot, FurRegionRow (FurBuilder.cs, byte for byte),
// FurSkinning.Root and FurStrandSim.SubStep (FurSim.cs, line for line - the offline tests run those).
#ifndef CREATURE_FUR_COMMON_INCLUDED
#define CREATURE_FUR_COMMON_INCLUDED

struct FurSkinVertex        // 64 bytes
{
    float3 pos;
    float3 nrm;
    float4 skin;            // bone0, bone1, w0, w1
    float2 uv0;             // arc m, angle / 2pi
    float2 uv1;             // part id, dorsal
    float radius;           // rest pattern radius
    float pad;
};

struct FurRoot              // 64 bytes
{
    uint v0, v1, v2;
    float b1, b2;
    float length;           // rest m
    float width;            // rest m
    uint region;
    float3 comb;
    uint seed;
    float3 tipDir;
    float rank;
};

struct FurRegionRow         // 48 bytes
{
    float4 root;            // linear rgb, bodyTint
    float4 tip;             // linear rgb, 0
    float4 dyn;             // Hz (scale 1), damping ratio, droop (x g), kind
};

// One simulated strand (96 bytes; FurSim.compute writes, CreatureFurStrands.shader reads).
struct FurStrand
{
    float4 p0;              // root (world), w = world width at the root
    float4 p1;              // first point, w = world length
    float4 p2;              // tip, w = rank (0..1)
    float4 v1;              // w = the skin normal at the root, octahedral 16:16 (asfloat)
    float4 v2;              // w = 1 once initialised
    uint4 look;             // root rgb, tip rgb as six halves (x = r|g, y = b|tip r, z = tip g|tip b), w = seed
};

#define FUR_GRAVITY 9.81
#define FUR_MAX_STEP (1.0 / 90.0)
#define FUR_MAX_SUBSTEPS 4
#define FUR_MIN_RISE 0.15
#define FUR_AIR_DRAG 0.1

// ---------------------------------------------------------------- the per-species palette (CreatureRenderer's values)
float4 _Dorsal;
float4 _Flank;
float4 _Belly;
float4 _PatternColor;
float _PatternType;
float4 _PatternParams;
float4 _BodyParams;
float4 _FurLook;            // pattern in the fur, root occlusion, highlight, translucency

// ---------------------------------------------------------------- helpers

float3 FurSafeNormalize(float3 v, float3 fallback)
{
    float l2 = dot(v, v);
    return l2 > 1e-24 ? v * rsqrt(l2) : fallback;
}

uint FurHash(uint x)
{
    x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
    return x;
}

float FurHash01(uint x) { return (float)(FurHash(x) >> 8) * (1.0 / 16777216.0); }

float2 FurOctEncode(float3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    float2 o = n.z >= 0.0 ? n.xy : (1.0 - abs(n.yx)) * float2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
    return o;
}

float3 FurOctDecode(float2 o)
{
    float3 n = float3(o.x, o.y, 1.0 - abs(o.x) - abs(o.y));
    float t = saturate(-n.z);
    n.xy += float2(n.x >= 0.0 ? -t : t, n.y >= 0.0 ? -t : t);
    return normalize(n);
}

float FurPackNormal(float3 n)
{
    float2 o = saturate(FurOctEncode(n) * 0.5 + 0.5);
    uint2 q = (uint2)(o * 65535.0 + 0.5);
    return asfloat(q.x | (q.y << 16));
}

float3 FurUnpackNormal(float f)
{
    uint u = asuint(f);
    float2 o = float2(u & 0xFFFFu, u >> 16) * (1.0 / 65535.0);
    return FurOctDecode(o * 2.0 - 1.0);
}

uint4 FurPackLook(float3 rootC, float3 tipC, uint seed)
{
    uint3 r = f32tof16(rootC), t = f32tof16(tipC);
    return uint4(r.x | (r.y << 16), r.z | (t.x << 16), t.y | (t.z << 16), seed);
}

void FurUnpackLook(uint4 l, out float3 rootC, out float3 tipC)
{
    rootC = f16tof32(uint3(l.x & 0xFFFFu, l.x >> 16, l.y & 0xFFFFu));
    tipC = f16tof32(uint3(l.y >> 16, l.z & 0xFFFFu, l.z >> 16));
}

// hue rotation about the grey axis (CreatureLighting.hlsl's CreatureHueShift, in float)
float3 FurHueShift(float3 c, float turns)
{
    float a = turns * 6.2831853;
    float cs = cos(a), sn = sin(a);
    const float3 k = float3(0.57735027, 0.57735027, 0.57735027);
    float3 r = c * cs + cross(k, c) * sn + k * dot(k, c) * (1.0 - cs);
    return max(r, 0.0);
}

// The body's own colour at a skin point (Creature.shader's palette and pattern, without the gradient wobble), and
// the pattern amounts (x dark, y pale) for the fur on top. part 1 (limbs) takes the limb pattern.
float3 FurBodyColour(float2 uv0, float2 uv1, float radius, float seed, out float2 pat)
{
    float dorsal = uv1.y;
    float3 base = lerp(_Belly.rgb, _Flank.rgb, smoothstep(-0.75, -0.15, dorsal));
    base = lerp(base, _Dorsal.rgb, smoothstep(0.05, 0.7, dorsal));
    int part = (int)(uv1.x + 0.5);
    pat = CreaturePatternMode(uv0, uv1, seed, _PatternType, _PatternParams, radius, _BodyParams, part == 1 ? 2 : 1);
    float3 pale = lerp(_Dorsal.rgb, _Belly.rgb, 0.65) * 1.08;
    float3 c = lerp(base, _PatternColor.rgb, pat.x);
    return lerp(c, pale, pat.y);
}

// A region's root and tip colours at a skin point: toward the body's palette by bodyTint, the body's pattern
// through both by _FurLook.x, then the individual's hue and value.
void FurColours(FurRegionRow g, float2 uv0, float2 uv1, float radius, CreatureInstance inst, float jitter,
                out float3 rootC, out float3 tipC)
{
    float2 pat;
    float3 body = FurBodyColour(uv0, uv1, radius, inst.seed, pat);
    rootC = lerp(g.root.rgb, body, g.root.w);
    tipC = lerp(g.tip.rgb, body * 1.15, g.root.w * 0.5);
    float k = _FurLook.x;
    float3 pale = lerp(_Dorsal.rgb, _Belly.rgb, 0.65) * 1.08;
    rootC = lerp(lerp(rootC, _PatternColor.rgb, pat.x * k), pale, pat.y * k * 0.6);
    tipC = lerp(lerp(tipC, _PatternColor.rgb * 1.3, pat.x * k * 0.8), pale * 1.1, pat.y * k * 0.6);
    float v = (1.0 + inst.valueShift) * jitter;
    rootC = FurHueShift(rootC, inst.hueShift) * v;
    tipC = FurHueShift(tipC, inst.hueShift) * v;
}

// ---------------------------------------------------------------- skinning (CreatureSkin.hlsl's spine path)

float4x4 FurSkinMatrix(FurSkinVertex v, uint baseIndex)
{
    uint b0 = (uint)(v.skin.x + 0.5), b1 = (uint)(v.skin.y + 0.5);
    return _Bones[baseIndex + b0] * v.skin.z + _Bones[baseIndex + b1] * v.skin.w;
}

// FurSkinning.Root: the barycentric blend of the three skinned vertices; comb and tip through the blended matrices.
void FurSkinRoot(FurRoot r, FurSkinVertex a, FurSkinVertex b, FurSkinVertex c, uint baseIndex,
                 out float3 p, out float3 n, out float3 comb, out float3 tip)
{
    float l1 = r.b1, l2 = r.b2, l0 = 1.0 - l1 - l2;
    float4x4 ma = FurSkinMatrix(a, baseIndex), mb = FurSkinMatrix(b, baseIndex), mc = FurSkinMatrix(c, baseIndex);
    p = mul(ma, float4(a.pos, 1.0)).xyz * l0 + mul(mb, float4(b.pos, 1.0)).xyz * l1 + mul(mc, float4(c.pos, 1.0)).xyz * l2;
    float3 na = FurSafeNormalize(mul((float3x3)ma, a.nrm), float3(0, 1, 0));
    float3 nb = FurSafeNormalize(mul((float3x3)mb, b.nrm), float3(0, 1, 0));
    float3 nc = FurSafeNormalize(mul((float3x3)mc, c.nrm), float3(0, 1, 0));
    n = FurSafeNormalize(na * l0 + nb * l1 + nc * l2, na);
    comb = FurSafeNormalize(mul((float3x3)ma, r.comb) * l0 + mul((float3x3)mb, r.comb) * l1 + mul((float3x3)mc, r.comb) * l2, n);
    tip = FurSafeNormalize(mul((float3x3)ma, r.tipDir) * l0 + mul((float3x3)mb, r.tipDir) * l1 + mul((float3x3)mc, r.tipDir) * l2, comb);
}

// ---------------------------------------------------------------- the strand spring (FurStrandSim.SubStep)

float3 FurCap(float3 v, float m)
{
    float l = length(v);
    return l > m ? v * (m / l) : v;
}

void FurSubStep(inout FurStrand s, float3 p0, float3 rootVel, float3 n, float3 comb, float3 tip, float len,
                float omega, float zeta, float droop, float h)
{
    float seg = 0.5 * len;
    float k2 = omega * omega, c = 2.0 * zeta * omega, den = 1.0 + c * h + k2 * h * h;
    float3 force = float3(0.0, -FUR_GRAVITY * droop, 0.0) + rootVel * (c * (1.0 - FUR_AIR_DRAG));
    float3 o1 = s.p1.xyz, o2 = s.p2.xyz;
    s.p0.xyz = p0;

    float3 t1 = p0 + comb * seg;
    float3 v1 = (s.v1.xyz + (k2 * (t1 - s.p1.xyz) + force) * h) / den;
    float3 p1 = s.p1.xyz + v1 * h;
    float3 t2 = p1 + tip * seg;
    float3 v2 = (s.v2.xyz + (k2 * (t2 - s.p2.xyz) + force) * h) / den;
    float3 p2 = s.p2.xyz + v2 * h;

    p1 = p0 + FurSafeNormalize(p1 - p0, comb) * seg;
    float r1 = dot(p1 - p0, n);
    if (r1 < FUR_MIN_RISE * seg) { p1 += n * (FUR_MIN_RISE * seg - r1); p1 = p0 + FurSafeNormalize(p1 - p0, comb) * seg; }
    p2 = p1 + FurSafeNormalize(p2 - p1, tip) * seg;
    float r2 = dot(p2 - p0, n);
    if (r2 < FUR_MIN_RISE * seg) { p2 += n * (FUR_MIN_RISE * seg - r2); p2 = p1 + FurSafeNormalize(p2 - p1, tip) * seg; }

    float vmax = seg / h;
    s.p1.xyz = p1; s.p2.xyz = p2;
    s.v1.xyz = FurCap((p1 - o1) / h, vmax);
    s.v2.xyz = FurCap((p2 - o2) / h, vmax);
}

#endif
