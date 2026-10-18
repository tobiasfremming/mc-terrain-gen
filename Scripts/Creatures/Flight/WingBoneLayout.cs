// Bone order of a winged species (design section 2): spine 0..N-1, then 3 per leg (the wing arms are
// legs: upper = humerus, lower = forearm, foot = wrist/hand), then 6 per wing, then the 2 eyes.
//   wing bone k: 0..2 fingers (leading, middle, trailing), 3..5 membrane ribs (rear to front)
// With no wings this is exactly CreatureSpecies' layout (BoneCount, LegBone, EyeBone). Phase B moves
// CreatureSpecies.EyeBone/BoneCount onto these functions (EyeBone shifts only when WingCount > 0).
public static class WingBoneLayout
{
    public const int BonesPerWing = WingSpec.Fingers + WingSpec.Ribs;
    public const int FirstRib = WingSpec.Fingers;

    public static int BoneCount(int joints, int legs, int wings) => joints + 3 * legs + BonesPerWing * wings + 2;
    public static int LegBone(int joints, int leg, int part) => joints + 3 * leg + part;
    public static int WingBone(int joints, int legs, int wing, int k) => joints + 3 * legs + BonesPerWing * wing + k;
    public static int EyeBone(int joints, int legs, int wings, int eye) => joints + 3 * legs + BonesPerWing * wings + eye;

    public static int BoneCount(CreatureSpecies sp, int wings) => BoneCount(sp.JointCount, LegCount(sp), wings);
    public static int WingBone(CreatureSpecies sp, int wing, int k) => WingBone(sp.JointCount, LegCount(sp), wing, k);
    public static int EyeBone(CreatureSpecies sp, int wings, int eye) => EyeBone(sp.JointCount, LegCount(sp), wings, eye);

    static int LegCount(CreatureSpecies sp) => sp.legs != null ? sp.legs.Length : 0;
}
