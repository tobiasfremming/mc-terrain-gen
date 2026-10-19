// GPU skinning for CreatureRenderer's single instanced draw per species.
//
//   _Bones          skin matrices (boneWorld x bindPose), _BonesPerCreature per instance,
//                   instance i at [i * _BonesPerCreature, (i + 1) * _BonesPerCreature)
//   _BindPoses      the species' bind poses (rest bone world inverses), one set for all instances;
//                   used to measure a vertex's distance from its bone axis (pattern space)
//   _Instances      per-individual shading data, indexed by SV_InstanceID
//
// Vertex layout (CreatureMeshBuilder): uv0 = (arc length m, angle / 2pi),
// uv1 = (part id 0 body / 1 leg / 2 foot / 3 eye, dorsal -1..1), uv2 = (bone0, bone1, w0, w1).
// With CREATURE_FOUR_BONES defined (the octopus shader): uv3 = (bone2, bone3, e2, e3), the skin is
// (1 - e2 - e3)(w0 M0 + w1 M1) + e2 M2 + e3 M3, and normals go through the cofactor of the blended 3x3
// (affine soft bones, non-uniform arm scale); arm tubes flagged in uv3 take the curve skin (CreatureArmCurve).
// Without it (every spine creature) nothing here changed.
#ifndef CREATURE_SKIN_INCLUDED
#define CREATURE_SKIN_INCLUDED

// Must match C# CreatureInstance field-for-field (32 bytes).
struct CreatureInstance
{
    float seed;
    float hueShift;    // turns
    float valueShift;  // albedo *= 1 + valueShift
    float sizeScale;
    float cloak;       // 0 visible .. 1 gone (Forward pass only)
    float chroma;      // chromatophore expansion 0..1
    float flash;       // deimatic flash 0..1
    float cloudPhase;  // passing-cloud bands phase, metres
};

StructuredBuffer<float4x4> _Bones;
StructuredBuffer<float4x4> _BindPoses;
StructuredBuffer<CreatureInstance> _Instances;
uint _BonesPerCreature;

struct CreatureVertexIn
{
    float4 positionOS : POSITION;   // rest space
    float3 normalOS : NORMAL;
    float2 uv0 : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    float4 uv2 : TEXCOORD2;
#if defined(CREATURE_FOUR_BONES)
    float4 uv3 : TEXCOORD3;
#endif
};

struct CreatureSkinned
{
    float3 posWS;
    float3 normalWS;
    float radius;              // rest distance from the (blended) bone axis, metres: pattern space
    CreatureInstance inst;
};

// ---------------------------------------------------------------------------------------------
// CreatureDisplace: THE HOOK for surface detail (scales, scutes, skin folds). Runs after skinning,
// in world space, in every pass (Forward, ShadowCaster, DepthOnly, DepthNormals), so displaced
// geometry shadows and occludes consistently. v1: no-op.
//   posWS  skinned world position (displace this)
//   nrmWS  skinned world normal (unit)
//   uv0    (arc length m, angle / 2pi)   uv1 (part id, dorsal -1..1)
// ---------------------------------------------------------------------------------------------
void CreatureDisplace(inout float3 posWS, float3 nrmWS, float2 uv0, float2 uv1, CreatureInstance inst)
{
}

#if defined(CREATURE_FOUR_BONES)
// OCTOPUS ARM CURVE (OctopusArmSkin.cs mirrors this line by line): an arm tube vertex between consecutive bones
// e (earlier) and l (later), t = its weight on l, sits on a circular arc from the middle of e's segment to the
// middle of l's (constant turning rate); its ring offset is laid out in a frame turning with the curve. Linear
// blending drew a joint as two chords with the ring squeezed between them and turned the inner side of a tight
// wrap inside out. Flag: uv3 = (rest spacing, 0, 0, 0) on an arm vertex (uv1.x == 4), bones one apart.
float4x4 CreatureRigidInverse(float4x4 b)
{
    float3x3 r = transpose((float3x3)b);
    float3 tr = -mul(r, float3(b[0][3], b[1][3], b[2][3]));
    return float4x4(r[0][0], r[0][1], r[0][2], tr.x,
                    r[1][0], r[1][1], r[1][2], tr.y,
                    r[2][0], r[2][1], r[2][2], tr.z,
                    0.0, 0.0, 0.0, 1.0);
}

void CreatureArmCurve(float4x4 skinE, float4x4 skinL, float4x4 bindE, float4x4 bindL, float3 restPos, float3 restN,
                      float t, float spacing, out float3 pos, out float3 nrm)
{
    float4x4 we = mul(skinE, CreatureRigidInverse(bindE)), wl = mul(skinL, CreatureRigidInverse(bindL));
    float3 local = mul(bindL, float4(restPos, 1.0)).xyz, nl = mul((float3x3)bindL, restN);
    float h = 0.5 * spacing, u = 1.0 - t;
    float3 p = float3(wl[0][3], wl[1][3], wl[2][3]);
    float3 m1 = mul(wl, float4(0.0, 0.0, h, 1.0)).xyz, m0 = mul(we, float4(0.0, 0.0, h, 1.0)).xyz;
    float3 l0 = p - m0, l1 = m1 - p;
    float a0 = length(l0), a1 = length(l1);
    float3 d0 = a0 > 1e-12 ? l0 / a0 : l1 / max(1e-12, a1);
    float3 d1 = a1 > 1e-12 ? l1 / a1 : d0;
    float cth = clamp(dot(d0, d1), -1.0, 1.0), th = acos(cth);
    float3 c, tng;
    if (th < 1e-3) { c = m0 + (m1 - m0) * t; tng = d0; }
    else
    {
        float3 nb = d1 - d0 * cth;
        nb /= max(1e-12, length(nb));
        float r = (a0 + a1) * 0.5 / tan(0.5 * th);
        float st = sin(th * t), ct = cos(th * t);
        float3 arc1 = m0 + (d0 * sin(th) + nb * (1.0 - cth)) * r;
        c = m0 + (d0 * st + nb * (1.0 - ct)) * r + (m1 - arc1) * t;
        tng = d0 * ct + nb * st;
    }
    float3 xe = float3(we[0][0], we[1][0], we[2][0]), xl = float3(wl[0][0], wl[1][0], wl[2][0]);
    float3 ye = float3(we[0][1], we[1][1], we[2][1]), yl = float3(wl[0][1], wl[1][1], wl[2][1]);
    float3 ze = float3(we[0][2], we[1][2], we[2][2]), zl = float3(wl[0][2], wl[1][2], wl[2][2]);
    float lxe = length(xe), lxl = length(xl);
    float sx = lxe * u + lxl * t, sy = length(ye) * u + length(yl) * t, sz = length(ze) * u + length(zl) * t;
    float tl = length(tng);
    float3 tn = tl > 1e-12 ? tng / tl : zl / max(1e-12, length(zl));
    float3 xs = xe * (u / max(1e-12, lxe)) + xl * (t / max(1e-12, lxl));
    xs -= tn * dot(xs, tn);
    float xsl = length(xs);
    float3 xd = xsl > 1e-9 ? xs / xsl : xl / max(1e-12, lxl);
    float3 yd = cross(tn, xd);
    float3 n = xd * (nl.x / sx) + yd * (nl.y / sy) + tn * (nl.z / sz);
    float nlen = length(n);
    nrm = nlen > 1e-12 ? n / nlen : yd;
    pos = c + xd * (sx * local.x) + yd * (sy * local.y);
}
#endif

CreatureSkinned CreatureSkin(CreatureVertexIn v, uint instanceID)
{
    uint b0 = (uint)(v.uv2.x + 0.5);
    uint b1 = (uint)(v.uv2.y + 0.5);
    uint baseIndex = instanceID * _BonesPerCreature;
    float4x4 m = _Bones[baseIndex + b0] * v.uv2.z + _Bones[baseIndex + b1] * v.uv2.w;
#if defined(CREATURE_FOUR_BONES)
    float extra = v.uv3.z + v.uv3.w;
    if (extra > 0.0)
    {
        uint b2 = (uint)(v.uv3.x + 0.5), b3 = (uint)(v.uv3.y + 0.5);
        m = m * (1.0 - extra) + _Bones[baseIndex + b2] * v.uv3.z + _Bones[baseIndex + b3] * v.uv3.w;
    }
#endif

    CreatureSkinned o;
    o.posWS = mul(m, float4(v.positionOS.xyz, 1.0)).xyz;
#if defined(CREATURE_FOUR_BONES)
    bool armCurve = abs(v.uv1.x - 4.0) < 0.5 && v.uv3.x > 0.0 && extra == 0.0 && (b0 == b1 + 1 || b1 == b0 + 1);
    if (armCurve)
    {
        uint be = min(b0, b1), bl = max(b0, b1);
        float t = bl == b0 ? v.uv2.z : v.uv2.w;
        float3 cp, cn;
        CreatureArmCurve(_Bones[baseIndex + be], _Bones[baseIndex + bl], _BindPoses[be], _BindPoses[bl],
                         v.positionOS.xyz, v.normalOS, t, v.uv3.x, cp, cn);
        o.posWS = cp;
        o.normalWS = cn;
    }
    else
    {
    // cofactor of the blended 3x3 (columns c0, c1, c2): (c1 x c2, c2 x c0, c0 x c1) - the normal map of an
    // affine / non-uniformly scaled bone; equal to the plain map (up to length) for rotation x uniform scale
    float3 c0 = float3(m[0][0], m[1][0], m[2][0]), c1 = float3(m[0][1], m[1][1], m[2][1]), c2 = float3(m[0][2], m[1][2], m[2][2]);
    float3 nCof = v.normalOS.x * cross(c1, c2) + v.normalOS.y * cross(c2, c0) + v.normalOS.z * cross(c0, c1);
    o.normalWS = normalize(nCof);
    }
#else
    // Bone matrices are rotation x uniform scale, so the blended 3x3 maps normals correctly up to length.
    o.normalWS = normalize(mul((float3x3)m, v.normalOS));
#endif
    // blended like the skin, so the radius is continuous where the heavier bone switches
    float4x4 bind = _BindPoses[b0] * v.uv2.z + _BindPoses[b1] * v.uv2.w;
    float3 local = mul(bind, float4(v.positionOS.xyz, 1.0)).xyz;
    o.radius = length(local.xy);
    o.inst = _Instances[instanceID];
    CreatureDisplace(o.posWS, o.normalWS, v.uv0, v.uv1, o.inst);
    return o;
}

#endif
