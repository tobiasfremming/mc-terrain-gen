using CreatureKit;
using UnityEngine;

// Builds a species' skinned mesh once, in the rest pose (CreatureRestPose).
//   position, normal: rest space
//   uv0 = (arc length along the part in metres, angle / 2pi)
//   uv1 = (part id, dorsal coordinate: -1 belly .. +1 spine ridge)
//   uv2 = (bone0, bone1, w0, w1), w0 + w1 = 1
// Since the creature kit's Phase 3 every part draws itself (CreaturePart.EmitMesh, Kit/PartMeshes.cs) through
// the part emitter (Kit/PartMeshEmitter.cs), in the shipped layout: body vertices first, as consecutive rings of
// ringSegments + 1 vertices (seam duplicated) from the snout tip to the tail tip (each tip is a ring of
// identical vertices), then the legs (tube, foot pad, toes, per leg), the two eyes, rigid cones, fins, antennae
// and wings. Vertices are shared along a part, so normals are continuous across ring boundaries.
public static class CreatureMeshBuilder
{
    public const int PartBody = PartMeshEmitter.PartBody, PartLeg = PartMeshEmitter.PartLeg, PartFoot = PartMeshEmitter.PartFoot, PartEye = PartMeshEmitter.PartEye;

    public static Mesh Build(CreatureSpecies sp) => PartMeshEmitter.Build(sp);
}
