using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Creature kit T3.3: one renderer path. KitRender (mesh, bind poses, bones, reach, body params from the body
    // plan) must give every species exactly what the pre-kit renderer constructor computed (its formulas frozen
    // below), and the renderer's CPU side (skin, culling bounds) must be the pre-kit one for every spine species.
    [TestFixtureSource(typeof(CreatureKitAssets), nameof(CreatureKitAssets.Species))]
    public class CreatureKitRenderTests
    {
        readonly string name;
        public CreatureKitRenderTests(string species) { name = species; }

        // the pre-kit CreatureRenderer constructor's reach and _BodyParams
        static void LegacyNumbers(CreatureSpecies sp, out float reach, out Vector4 bodyParams, out int bones)
        {
            const float kBoundsMargin = 0.05f;
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod)
            {
                CephalopodSpec c = sp.cephalopod;
                bones = OctopusBody.BoneCountFor(c);
                reach = Mathf.Max(c.arm.RootRadius * c.arm.Bulge, 1.5f * Mathf.Max(c.MantleRadius, c.HeadRadius)) + c.EyeRadius + kBoundsMargin;
                float l = c.arm.Length;
                bodyParams = new Vector4(l, 1f / l, c.WebDepth * l, 1f);
                return;
            }
            bones = sp.BoneCount;
            float halfHeight = 0f;
            for (int i = 0; i < sp.JointCount; i++) halfHeight = Mathf.Max(halfHeight, sp.joints[i].halfHeight);
            reach = Mathf.Max(sp.MaxLateralReach, Mathf.Max(sp.headCapLength, sp.tailTipLength), halfHeight) + sp.eyeRadius + kBoundsMargin;
            float len = Mathf.Max(sp.BodyLength, 1e-3f);
            int hip = -1;
            for (int l = 0; l < (sp.legs != null ? sp.legs.Length : 0); l++) hip = Mathf.Max(hip, sp.legs[l].attachJoint);
            float tailStart = hip >= 0 ? sp.headCapLength + sp.spacing * hip : 0.5f * len;
            bodyParams = new Vector4(len, 1f / len, tailStart, 0f);
        }

        [Test] public void Render_EqualsLegacyRenderer()
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            string why = CreatureKitAssets.KitOnlyReason(sp);
            if (why != null) Assert.Ignore(why);
            KitRender rd = KitRender.For(sp);
            try
            {
                LegacyNumbers(sp, out float reach, out Vector4 bp, out int bones);
                Assert.AreEqual(bones, rd.Bones, "bones per individual");
                Assert.AreEqual(reach, rd.Reach, 0f, "reach");
                Assert.AreEqual(bp, rd.BodyParams, "_BodyParams");
                Assert.AreEqual(sp.bodyPlan == CreatureBodyPlan.Cephalopod, rd.AffineSkin, "skin product");
                if (sp.bodyPlan == CreatureBodyPlan.Cephalopod)
                {
                    Assert.IsNull(MeshBytes.Compare(MeshBytes.Of(OctopusMeshBuilder.Build(sp.cephalopod, sp.name + " octopus")), MeshBytes.Of(rd.Mesh)));
                    Assert.IsNull(MeshBytes.CompareMatrices(OctopusRestPose.BindPoses(sp.cephalopod), rd.BindPoses));
                }
                else
                {
                    Assert.IsNull(MeshBytes.Compare(MeshBytes.Reference(sp), MeshBytes.Of(rd.Mesh)), "mesh");
                    Matrix4x4[] bind = MeshBytes.ReferenceBoneWorld(sp);
                    for (int i = 0; i < bind.Length; i++) bind[i] = bind[i].inverse;
                    Assert.IsNull(MeshBytes.CompareMatrices(bind, rd.BindPoses), "bind poses");
                }
            }
            finally { if (rd.Mesh != null) Object.DestroyImmediate(rd.Mesh); }
        }

        // The renderer's CPU side on a walking individual: skin = bone world x the legacy bind pose, bit for bit;
        // the culling bounds are the pre-kit ones (spine joints, feet, elbows, + 2 x reach x size) grown only by
        // part runtimes' points (antenna strands).
        static float Ax(Vector3 v, int a) => a == 0 ? v.x : a == 1 ? v.y : v.z;

        [Test] public void Renderer_Skin_And_Bounds_EqualLegacy()
        {
            CreatureSpecies sp = CreatureKitAssets.Load(name);
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) Assert.Ignore("cephalopod: OctopusCreature is a Unity-side body (CreatureOctopusWorldTests)");
            string why = CreatureKitAssets.KitOnlyReason(sp);
            if (why != null) Assert.Ignore(why);
            var r = new CreatureRenderer(sp, null);
            try
            {
                float s = sp.scale;
                var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s);
                for (int t = 0; t < 60; t++) sim.Tick(1f / 60f, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * (2f * s));
                r.Add(sim, new CreatureInstance { sizeScale = s });
                r.Render();
                Matrix4x4[] bind = MeshBytes.ReferenceBoneWorld(sp);
                for (int i = 0; i < bind.Length; i++) bind[i] = bind[i].inverse;
                var world = new Matrix4x4[sp.BoneCount];
                sim.WriteBoneWorld(world, 0);
                var skin = new Matrix4x4[world.Length];
                for (int b = 0; b < world.Length; b++) skin[b] = world[b] * bind[b];
                var got = new Matrix4x4[world.Length];
                System.Array.Copy(r.SkinForTest, got, got.Length);
                Assert.IsNull(MeshBytes.CompareMatrices(skin, got), "skin matrices");

                LegacyNumbers(sp, out float reach, out _, out _);
                Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
                foreach (Vector3 j in sim.Joints) { lo = Vector3.Min(lo, j); hi = Vector3.Max(hi, j); }
                foreach (LegState l in sim.Legs) { lo = Vector3.Min(lo, l.foot); hi = Vector3.Max(hi, l.foot); lo = Vector3.Min(lo, l.elbow); hi = Vector3.Max(hi, l.elbow); }
                var legacy = new Bounds();
                legacy.SetMinMax(lo, hi);
                legacy.Expand(2f * reach * s);
                Bounds b2 = r.LastBounds;
                if (sp.AntennaCount == 0)
                {
                    Assert.AreEqual(legacy.min, b2.min, "bounds min");
                    Assert.AreEqual(legacy.max, b2.max, "bounds max");
                }
                else
                {
                    // Bounds keeps centre + extents: a side the strands did not move can still round by an ulp when
                    // the opposite side moved (SkyDragon's min z, -13.195978 vs -13.195979), so compare per side within it
                    for (int a = 0; a < 3; a++)
                    {
                        float tol = 1e-6f * (1f + Mathf.Abs(Ax(legacy.min, a)) + Mathf.Abs(Ax(legacy.max, a)));
                        Assert.LessOrEqual(Ax(b2.min, a), Ax(legacy.min, a) + tol, "the strands only grow the bounds (min, axis " + a + ")");
                        Assert.GreaterOrEqual(Ax(b2.max, a), Ax(legacy.max, a) - tol, "the strands only grow the bounds (max, axis " + a + ")");
                    }
                    for (int i = 0; i < sim.Antennae.Points.Length; i++) Assert.IsTrue(b2.Contains(sim.Antennae.Points[i]), "strand point " + i);
                }
            }
            finally { r.Dispose(); }
        }
    }
}
