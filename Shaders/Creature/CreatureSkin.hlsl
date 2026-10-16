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
#ifndef CREATURE_SKIN_INCLUDED
#define CREATURE_SKIN_INCLUDED

// Must match C# CreatureInstance field-for-field (16 bytes).
struct CreatureInstance
{
    float seed;
    float hueShift;    // turns
    float valueShift;  // albedo *= 1 + valueShift
    float sizeScale;
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

    CreatureSkinned o;
    o.posWS = mul(m, float4(v.positionOS.xyz, 1.0)).xyz;
    // Bone matrices are rotation x uniform scale, so the blended 3x3 maps normals correctly up to length.
    o.normalWS = normalize(mul((float3x3)m, v.normalOS));
    // blended like the skin, so the radius is continuous where the heavier bone switches
    float4x4 bind = _BindPoses[b0] * v.uv2.z + _BindPoses[b1] * v.uv2.w;
    float3 local = mul(bind, float4(v.positionOS.xyz, 1.0)).xyz;
    o.radius = length(local.xy);
    o.inst = _Instances[instanceID];
    CreatureDisplace(o.posWS, o.normalWS, v.uv0, v.uv1, o.inst);
    return o;
}

#endif
