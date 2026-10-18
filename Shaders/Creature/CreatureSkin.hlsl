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
// (affine soft bones, non-uniform arm scale). Without it (every spine creature) nothing here changed.
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
    // cofactor of the blended 3x3 (columns c0, c1, c2): (c1 x c2, c2 x c0, c0 x c1) - the normal map of an
    // affine / non-uniformly scaled bone; equal to the plain map (up to length) for rotation x uniform scale
    float3 c0 = float3(m[0][0], m[1][0], m[2][0]), c1 = float3(m[0][1], m[1][1], m[2][1]), c2 = float3(m[0][2], m[1][2], m[2][2]);
    float3 nCof = v.normalOS.x * cross(c1, c2) + v.normalOS.y * cross(c2, c0) + v.normalOS.z * cross(c0, c1);
    o.normalWS = normalize(nCof);
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
