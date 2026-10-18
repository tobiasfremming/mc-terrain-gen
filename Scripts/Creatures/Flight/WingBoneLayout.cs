// Bone order of a winged species (design section 2): spine 0..N-1, then 3 per leg (the wing arms are
// legs: upper = humerus, lower = forearm, foot = wrist/hand), then 6 per wing, then the 2 eyes.
//   wing bone k: 0..2 fingers (leading, middle, trailing), 3..4 membrane ribs (trailing, rear flank),
//   5 the arm sector (the front flank rib's slot)
// With no wings this is exactly CreatureSpecies' layout (BoneCount, LegBone, EyeBone). Phase B moves
// CreatureSpecies.EyeBone/BoneCount onto these functions (EyeBone shifts only when WingCount > 0).
public static class WingBoneLayout
{
    public const int BonesPerWing = WingSpec.Fingers + WingSpec.Ribs;
    public const int FirstRib = WingSpec.Fingers;
    // wing bone 5 (in rib 2's slot): the arm sector, an affine frame at the wrist on the rear flank rib and
    // the forearm (CreatureWings.ArmSector): the membrane zone between them, its edge on the forearm
    public const int ArmRib = FirstRib + 2;

    public static int BoneCount(int joints, int legs, int wings) => joints + 3 * legs + BonesPerWing * wings + 2;
    public static int LegBone(int joints, int leg, int part) => joints + 3 * leg + part;
    public static int WingBone(int joints, int legs, int wing, int k) => joints + 3 * legs + BonesPerWing * wing + k;
    public static int EyeBone(int joints, int legs, int wings, int eye) => joints + 3 * legs + BonesPerWing * wings + eye;

    // The int overloads above assume legacy legs (3 bones each), which is what every winged species has. The
    // species overloads count the real leg bones (LegBoneLayout: arthropod legs have one bone per segment).
    public static int BoneCount(CreatureSpecies sp, int wings) => sp.JointCount + LegBoneLayout.LegBones(sp.legs) + BonesPerWing * wings + 2;
    public static int WingBone(CreatureSpecies sp, int wing, int k) => sp.JointCount + LegBoneLayout.LegBones(sp.legs) + BonesPerWing * wing + k;
    public static int EyeBone(CreatureSpecies sp, int wings, int eye) => sp.JointCount + LegBoneLayout.LegBones(sp.legs) + BonesPerWing * wings + eye;
}
