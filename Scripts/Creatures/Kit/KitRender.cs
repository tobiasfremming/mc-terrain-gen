// Creature kit T3.3: what the renderer draws for a species, from the species' body plan in one place - the mesh,
// the bind poses, the bones per individual, the culling reach past the simulated points and _BodyParams. The
// renderer has one construction path (CreatureRenderer reads a KitRender); only the shading family differs:
//   Spine   CreatureMeshBuilder / the part emitter, CreatureRestPose (Creature.shader, 2-bone skin)
//   Soft    OctopusMeshBuilder, OctopusRestPose (CreatureOctopus.shader, 4-bone affine skin)
// The numbers are the pre-kit renderer constructor's, moved here unchanged (CreatureKitRenderTests).
using UnityEngine;

namespace CreatureKit
{
    public enum ShadingFamily { Spine = 0, Soft = 1 }

    public sealed class KitRender
    {
        const float kBoundsMargin = 0.05f;

        public ShadingFamily Family { get; private set; }
        public Mesh Mesh { get; private set; }
        public Matrix4x4[] BindPoses { get; private set; }
        public int Bones { get; private set; }
        // culling bounds grow by Reach x the largest individual's size, past the simulated points
        public float Reach { get; private set; }
        // _BodyParams = (body length, 1 / length, arc length where the tail begins, soft ? 1 : 0)
        public Vector4 BodyParams { get; private set; }
        // the skin product: affine (MulAffine, the soft lattice's shear) or the full matrix product
        public bool AffineSkin => Family == ShadingFamily.Soft;

        KitRender() { }

        public static KitRender For(CreatureSpecies sp)
        {
            sp = sp.Compiled.Runtime;   // the runtime view (a legacy asset is its own)
            var r = new KitRender();
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) r.Soft(sp);
            else r.Spine(sp);
            return r;
        }

        void Soft(CreatureSpecies sp)
        {
            Family = ShadingFamily.Soft;
            CephalopodSpec c = sp.cephalopod;
            Bones = OctopusBody.BoneCountFor(c);
            BindPoses = OctopusRestPose.BindPoses(c);
            Mesh = OctopusMeshBuilder.Build(c, (sp != null ? sp.name : "") + " octopus");
            // past the simulated points: the arm radius (bulging), the head/mantle shell around its inset
            // lattice, the eyes
            Reach = Mathf.Max(c.arm.RootRadius * c.arm.Bulge, 1.5f * Mathf.Max(c.MantleRadius, c.HeadRadius)) + c.EyeRadius + kBoundsMargin;
            float len = c.arm.Length;
            BodyParams = new Vector4(len, 1f / len, c.WebDepth * len, 1f);
        }

        void Spine(CreatureSpecies sp)
        {
            Family = ShadingFamily.Spine;
            Bones = sp.BoneCount;
            BindPoses = CreatureRestPose.BindPoses(sp);
            Mesh = PartMeshEmitter.Build(sp);

            // Everything that sticks out past the spine joints: legs and feet sideways and down, the head
            // cap and tail tip along the body, the eyes.
            float halfHeight = 0f;
            for (int i = 0; i < sp.JointCount; i++) halfHeight = Mathf.Max(halfHeight, sp.joints[i].halfHeight);
            Reach = Mathf.Max(sp.MaxLateralReach, Mathf.Max(sp.headCapLength, sp.tailTipLength), halfHeight) + sp.eyeRadius + kBoundsMargin;

            // _BodyParams = (body length, 1 / length, arc length where the tail begins (behind the hind legs), 0)
            float len = Mathf.Max(sp.BodyLength, 1e-3f);
            int hip = -1;
            for (int l = 0; l < (sp.legs != null ? sp.legs.Length : 0); l++) hip = Mathf.Max(hip, sp.legs[l].attachJoint);
            float tailStart = hip >= 0 ? sp.headCapLength + sp.ArcToJoint(hip) : 0.5f * len;   // the arc to the hips (per-joint spacing; uniform: spacing x hip)
            BodyParams = new Vector4(len, 1f / len, tailStart, 0f);
        }
    }
}
