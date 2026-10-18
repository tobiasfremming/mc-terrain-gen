// Creature kit T5.1: FinStrip - a membrane strip along the spine (mane, crest, dorsal / anal fin, tail tuft).
// Mesh only, no bones of its own: it is skinned to the spine, its base row on the body and each higher row riding
// the spine `flowJoints` x (row height) joints further back, so the strip trails behind the body like hair in the
// wind - it sweeps out on a bend and leans against a roll travelling down the body (the dragon's twist).
// Membrane shading (part 8): translucent, fin rays at the tufts, a darker tip rim.
using System;
using UnityEngine;

// The runtime fin (CreatureSpecies.fins, filled by the kit's FinStrip part): authored at scale 1.
[Serializable]
public struct FinStripSpec
{
    public int fromJoint, toJoint;        // spine joints the base runs along (fromJoint < toJoint)
    public float heightStart, heightEnd;  // m: strip height at the first and the last joint
    public float leanDeg;                 // the strip sweeps back by this much over its height (curling)
    public int tufts;                     // scallops along the free edge (0 = a straight edge)
    public float tuftDepth;               // 0..1: valley depth between tufts, fraction of the height
    public float flowJoints;              // joints further back the top row rides (the trail)
    public float inset;                   // 0..1: base buried this fraction of the body's radius
    public float aroundDeg;               // around the body: 0 = on the back, 180 = under the belly
    public bool mirror;                   // also at -aroundDeg (a lateral pair)
    public int columnsPerJoint;           // mesh columns per spine segment (0 = 2)

    public int Columns => Mathf.Max(2, (toJoint - fromJoint) * (columnsPerJoint > 0 ? columnsPerJoint : 2));
}

namespace CreatureKit
{
    [Serializable]
    [Part("FinStrip", PartKind.Fin, Bones = "0 (skinned to the spine)", Summary = "Double-sided membrane strip along the spine: mane, crest, dorsal or anal fin, tail tuft. Height profile, swept-back curl, scalloped tufts; its upper rows ride the spine flowJoints further back, so it trails and flows as the body bends and twists.",
          System = "mesh only (FinStrip.EmitMesh via FinStripMesh), skinned to the spine bones", MaterialLayers = new[] { 8 },
          UseWhen = "manes, crests, dorsal / anal fins, tail tufts, frills along a body")]
    public sealed partial class FinStrip : CreaturePart
    {
        [Param(ParamUnit.Joint, "last spine joint of the base (the first is at.joint)")] public int toJoint;
        [Param(ParamUnit.Metres, "height at the first joint", Example = "dragon mane 0.075")] public float heightStart;
        [Param(ParamUnit.Metres, "height at the last joint")] public float heightEnd;
        [Param(ParamUnit.Degrees, "the strip curls back by this much over its height (0 = straight up); past 40 the curl folds onto the body when it bends", Min = 0, Max = 40)] public float leanDeg;
        [Param(ParamUnit.Count, "scallops (tufts, flames) along the free edge, 0 = a straight edge", Min = 0, Max = 200)] public int tufts;
        [Param(ParamUnit.Fraction, "valley depth between tufts, fraction of the height", Min = 0, Max = 0.9f)] public float tuftDepth;
        [Param(ParamUnit.Fraction, "how many joints further back the top row rides (the trail; 0 = rigid on its joint)", Min = 0, Max = 4)] public float flowJoints;
        [Param(ParamUnit.Fraction, "base buried this fraction of the body's radius", Min = 0, Max = 0.6f)] public float inset;
        [Param(ParamUnit.Degrees, "around the body: 0 = on the back, 180 = under the belly (mirror: also at minus this)")] public float aroundDeg;
        [Param(ParamUnit.Count, "mesh columns per spine segment (0 = 2)", Min = 0, Max = 8)] public int columnsPerJoint;

        public override int BoneCount(in SpeciesCtx c) => 0;

        public FinStripSpec ToRuntime() => new FinStripSpec
        {
            fromJoint = at.joint, toJoint = toJoint, heightStart = heightStart, heightEnd = heightEnd, leanDeg = leanDeg,
            tufts = tufts, tuftDepth = tuftDepth, flowJoints = flowJoints, inset = inset, aroundDeg = aroundDeg, mirror = mirror,
            columnsPerJoint = columnsPerJoint,
        };

        public static FinStrip From(in FinStripSpec f, string id) => new FinStrip
        {
            id = id, at = new PartSocket(f.fromJoint, 0, Vector3.zero), mirror = f.mirror, toJoint = f.toJoint, heightStart = f.heightStart,
            heightEnd = f.heightEnd, leanDeg = f.leanDeg, tufts = f.tufts, tuftDepth = f.tuftDepth, flowJoints = f.flowJoints, inset = f.inset,
            aroundDeg = f.aroundDeg, columnsPerJoint = f.columnsPerJoint,
        };

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a FinStrip needs a SpineBody";
            if (at.joint < 0 || toJoint >= c.Joints || toJoint <= at.joint) return $"{id}: joints {at.joint}..{toJoint} must lie in 0..{c.Joints - 1}, first < last";
            if (heightStart < 0f || heightEnd < 0f || heightStart + heightEnd <= 0f) return $"{id}: heights must be >= 0 and not both 0";
            if (tuftDepth < 0f || tuftDepth > 0.9f) return $"{id}: tuftDepth must be in 0..0.9";
            if (flowJoints < 0f || flowJoints > 4f) return $"{id}: flowJoints must be in 0..4";
            if (leanDeg < 0f || leanDeg > 40f) return $"{id}: leanDeg must be in 0..40 (a stronger curl folds onto the body)";
            return null;
        }
    }
}
