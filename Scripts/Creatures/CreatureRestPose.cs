using UnityEngine;

// The rest pose the creature mesh is authored in: a fresh, unticked CreatureSim on flat ground (y = 0,
// up = +Y) at the origin, facing +Z, sizeScale 1. On flat ground droop is zero and the feet stand on
// their rest points, so the pose is fully determined by the species.
public static class CreatureRestPose
{
    internal static CreatureSim Sim(CreatureSpecies sp) =>
        new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);

    public static Matrix4x4[] BoneWorld(CreatureSpecies sp) => BoneWorld(Sim(sp));

    internal static Matrix4x4[] BoneWorld(CreatureSim sim)
    {
        var m = new Matrix4x4[sim.Species.BoneCount];
        sim.WriteBoneWorld(m, 0);
        return m;
    }

    // Inverses of BoneWorld: skin matrix = boneWorld x bindPose.
    public static Matrix4x4[] BindPoses(CreatureSpecies sp)
    {
        Matrix4x4[] m = BoneWorld(sp);
        for (int i = 0; i < m.Length; i++) m[i] = m[i].inverse;
        return m;
    }
}
