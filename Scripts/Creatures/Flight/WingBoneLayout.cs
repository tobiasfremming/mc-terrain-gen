using UnityEngine;

// Bone order of a winged species (design section 2): spine 0..N-1, then 3 per leg (the wing arms are
// legs: upper = humerus, lower = forearm, foot = wrist/hand), then Bones(spec) per wing (7 with the one
// humerus patch), then the 2 eyes.
//   wing bone k: 0..F-1 fingers (leading .. trailing; F = WingSpec.FingerCount, 3 for the winged serpent),
//   F..F+1 membrane ribs (trailing, rear flank), F+2 the arm sector (the front flank rib's slot), F+3.. the flank
//   patches (F+3 alone: the humerus patch; WingSpec.flankPatches splits it along the flank), then
//   (WingSpec.groundTallStance) the fan-side rear rib. The constants below are the 3-finger wing's (F = 3); the
//   spec overloads (FirstRibOf, ArmRibOf, HumerusPatchOf) count the wing's own fingers.
// With no wings this is exactly CreatureSpecies' layout (BoneCount, LegBone, EyeBone). Phase B moves
// CreatureSpecies.EyeBone/BoneCount onto these functions (EyeBone shifts only when WingCount > 0).
public static class WingBoneLayout
{
    // The membrane between the humerus and the flank is FlankPatchCount(spec) affine patches side by side, each
    // an elbow-centred triangle on two neighbouring nodes of the flank edge (CreatureWings.FlankNode: the rear
    // flank attach, points on the flank between, the shoulder). One patch (2026-10-18) spans the whole attach
    // with a straight edge, which leaves a gap where the body bends (user feedback); WingSpec.flankPatches = 6
    // (the winged serpent) follows the body's curve.
    public const int MaxFlankPatches = 8;
    public static int FlankPatchCount(WingSpec ws) => ws != null && ws.flankPatches > 1 ? Mathf.Min(ws.flankPatches, MaxFlankPatches) : 1;
    public static int FingerCount(WingSpec ws) => ws != null ? ws.FingerCount : WingSpec.Fingers;
    public static int Bones(WingSpec ws) => FingerCount(ws) + WingSpec.Ribs + FlankPatchCount(ws) + (ws != null && ws.groundTallStance ? 1 : 0);
    public static int FirstRibOf(WingSpec ws) => FingerCount(ws);
    public static int ArmRibOf(WingSpec ws) => FirstRibOf(ws) + 2;
    public static int HumerusPatchOf(WingSpec ws) => FirstRibOf(ws) + WingSpec.Ribs;
    // A tall stance's extra bone after the flank patches: the rear flank rib as the fan beside it sees it
    // (CreatureWings.FanRearUp; -1 without groundTallStance). The zone between the trailing rib and the rear rib
    // rides it, the arm zone the rear rib itself: in the air the two are the same frame (one smooth sheet), on
    // the ground the lifted fan and the arm membrane meet in a crease along the rib.
    public static int FanRearRib(WingSpec ws) => ws != null && ws.groundTallStance ? HumerusPatchOf(ws) + FlankPatchCount(ws) : -1;
    // the bones of a wing with the one humerus patch (the int overloads below assume it)
    public const int BonesPerWing = WingSpec.Fingers + WingSpec.Ribs + 1;
    public const int FirstRib = WingSpec.Fingers;
    // wing bone 5 (in rib 2's slot): the arm sector, an affine frame at the wrist on the rear flank rib and
    // the forearm (CreatureWings.ArmSector): the membrane zone between them, its edge on the forearm
    public const int ArmRib = FirstRib + 2;
    // wing bone 6: the first flank patch (the humerus patch), an affine frame at the elbow on the rear flank
    // attach and the next flank node (one patch: the shoulder)
    // (CreatureWings.HumerusPatch): the membrane between the humerus and the flank. Two-bone skinning between
    // the arm sector and the humerus folded it inside out whenever a wing-walk swung the humerus past the
    // sector's crease (2026-10-18, CreatureKitTests MaxBend).
    public const int HumerusPatch = FirstRib + WingSpec.Ribs;

    public static int BoneCount(int joints, int legs, int wings) => joints + 3 * legs + BonesPerWing * wings + 2;
    public static int LegBone(int joints, int leg, int part) => joints + 3 * leg + part;
    public static int WingBone(int joints, int legs, int wing, int k) => joints + 3 * legs + BonesPerWing * wing + k;
    public static int EyeBone(int joints, int legs, int wings, int eye) => joints + 3 * legs + BonesPerWing * wings + eye;

    // The int overloads above assume legacy legs (3 bones each) and one humerus patch per wing. The species
    // overloads count the real leg bones (LegBoneLayout: arthropod legs have one bone per segment) and the first
    // `wings` of sp.wings' own bone counts (Bones(spec); a wing past sp.wings counts BonesPerWing).
    public static int BoneCount(CreatureSpecies sp, int wings) => sp.JointCount + LegBoneLayout.LegBones(sp.legs) + WingBones(sp, wings) + 2;
    public static int WingBone(CreatureSpecies sp, int wing, int k) => sp.JointCount + LegBoneLayout.LegBones(sp.legs) + WingBones(sp, wing) + k;
    public static int EyeBone(CreatureSpecies sp, int wings, int eye) => sp.JointCount + LegBoneLayout.LegBones(sp.legs) + WingBones(sp, wings) + eye;
    // the bones of sp's first `wings` wings
    public static int WingBones(CreatureSpecies sp, int wings)
    {
        int c = 0;
        for (int w = 0; w < wings; w++) c += sp.wings != null && w < sp.wings.Length ? Bones(sp.wings[w]) : BonesPerWing;
        return c;
    }
}
