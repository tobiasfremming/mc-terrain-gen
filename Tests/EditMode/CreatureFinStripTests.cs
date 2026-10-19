using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's FinStrip part (T5.1), on the SkyDragon (mane, crest, tail tuft): validation, a double-sided strip
    // standing on the back, skinned to the spine with its upper rows trailing, Froude-sized, and no triangle that
    // flips while the body bends hard or twists through every manoeuvre (CreatureKitTests' MaxBend covers the
    // tight ground circle; this covers the air).
    public class CreatureFinStripTests
    {
        const float kDt = 1f / 60f;
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        [Test] public void FinStrip_Validate_RejectsBadData()
        {
            var ok = new FinStrip { id = "f", at = new PartSocket(2, 0, Vector3.zero), toJoint = 10, heightStart = 0.05f, heightEnd = 0.02f, leanDeg = 30f, tufts = 4, tuftDepth = 0.4f, flowJoints = 1f };
            Assert.IsNull(ok.Validate(Ctx(20)));
            var bad = (FinStrip)ok.Clone(); bad.toJoint = 2;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "empty joint range");
            bad = (FinStrip)ok.Clone(); bad.toJoint = 20;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "past the last joint");
            bad = (FinStrip)ok.Clone(); bad.heightStart = 0f; bad.heightEnd = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "no height");
            bad = (FinStrip)ok.Clone(); bad.tuftDepth = 1f;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "tufts cut to the base");
            bad = (FinStrip)ok.Clone(); bad.flowJoints = 6f;
            Assert.IsNotNull(bad.Validate(Ctx(20)), "flow too far");
            Assert.AreEqual(0, ok.BoneCount(Ctx(20)), "mesh only: no bones of its own");
        }

        sealed class MeshData
        {
            public Vector3[] v; public int[] t;
            public List<Vector2> uv1 = new List<Vector2>(); public List<Vector4> uv2 = new List<Vector4>();
        }

        MeshData Mesh(CreatureSpecies sp)
        {
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            trash.Add(m);
            var d = new MeshData { v = m.vertices, t = m.triangles };
            m.GetUVs(1, d.uv1); m.GetUVs(2, d.uv2);
            return d;
        }

        // The dragon's fins: membrane vertices standing on its back along the joints they name, both sheets, the
        // upper rows skinned further back than the base.
        [Test] public void FinStrip_OnTheBack_DoubleSided_Trailing()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            Assert.Greater(sp.fins.Length, 0, "the dragon has fins");
            MeshData d = Mesh(sp);
            CreatureSim rest = CreatureRestPose.Sim(sp);
            int membrane = 0, above = 0, trailing = 0, front = 0, back = 0;
            float wingReach = 0f;
            foreach (WingSpec w in sp.wings) wingReach = Mathf.Max(wingReach, Mathf.Abs(w.restTip.x));
            for (int i = 0; i < d.v.Length; i++)
            {
                if (Mathf.RoundToInt(d.uv1[i].x) != WingedMeshBuilder.PartMembrane) continue;
                Vector4 s = d.uv2[i];
                if (s.x >= sp.JointCount || s.y >= sp.JointCount) continue;   // a wing's membrane (its rib bones)
                membrane++;
                int j = (int)s.x;
                if (d.v[i].y > rest.Joints[j].y) above++;
                if (d.uv1[i].y > 0f) front++; else back++;
                if (Mathf.Abs(d.uv1[i].y) > 0.9f && Mathf.Max(s.x, s.y) > 0f) trailing++;
            }
            int expect = 0, onBack = 0;
            foreach (FinStripSpec f in sp.fins)
            {
                int c = (f.Columns + 1) * (FinStripMesh.Rows + 1) * 2 * (f.mirror && f.aroundDeg != 0f ? 2 : 1);
                expect += c;
                if (Mathf.Abs(f.aroundDeg) < 90f) onBack += c;   // the cheek frills (105) and the beard (180) hang below
            }
            TestContext.WriteLine($"{membrane} fin vertices ({front} front, {back} back), {above} above their joint ({onBack} on back fins)");
            Assert.AreEqual(expect, membrane, "every fin column and row, two sheets");
            Assert.AreEqual(front, back, "double-sided");
            Assert.Greater(above, 0.9f * onBack, "the back fins stand on the back");
            Assert.Less(above, onBack + 0.1f * (membrane - onBack) + 1, "and the cheek frills and the beard hang below");
            // the top row rides joints further back than the base (flowJoints > 0)
            foreach (FinStripSpec f in sp.fins)
            {
                Vector4 b0 = FinStripMesh.SpineSkin(f.fromJoint + 1f, sp.JointCount), b1 = FinStripMesh.SpineSkin(f.fromJoint + 1f + f.flowJoints, sp.JointCount);
                float u0 = b0.x * b0.z + b0.y * b0.w, u1 = b1.x * b1.z + b1.y * b1.w;
                if (f.flowJoints > 0f) Assert.Greater(u1, u0, "the top trails the base");
            }
        }

        // Size: the skinned fin's height above the spine follows sizeScale exactly.
        [Test] public void FinStrip_HeightScalesWithSize()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            MeshData d = Mesh(sp);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var h = new float[2];
            float[] scales = { 0.9f * sp.scale, 1.1f * sp.scale };
            for (int c = 0; c < 2; c++)
            {
                var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, scales[c]);
                var world = new Matrix4x4[sp.BoneCount];
                sim.WriteBoneWorld(world, 0);
                float top = 0f;
                for (int i = 0; i < d.v.Length; i++)
                {
                    if (Mathf.RoundToInt(d.uv1[i].x) != WingedMeshBuilder.PartMembrane) continue;
                    Vector4 s = d.uv2[i];
                    if (s.x >= sp.JointCount || s.y >= sp.JointCount) continue;
                    Vector3 p = s.z * (world[(int)s.x] * bind[(int)s.x]).MultiplyPoint3x4(d.v[i]) + s.w * (world[(int)s.y] * bind[(int)s.y]).MultiplyPoint3x4(d.v[i]);
                    top = Mathf.Max(top, p.y);
                }
                h[c] = top / scales[c];
            }
            TestContext.WriteLine($"fin top / s: {h[0]:F5} vs {h[1]:F5}");
            Assert.AreEqual(h[0], h[1], 1e-3f * h[0], "the fins scale with the body");
        }

        // Every triangle of the dragon (body, limbs, wings, fins) keeps its orientation through each twisting
        // manoeuvre in the air: no membrane or mane inverts, nothing folds through itself.
        [TestCase(TwistManoeuvre.Corkscrew)]
        [TestCase(TwistManoeuvre.BarrelRoll)]
        [TestCase(TwistManoeuvre.Loop)]
        [TestCase(TwistManoeuvre.SlowRoll)]
        public void Twist_NoInvertedTriangles(TwistManoeuvre m)
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            MeshData d = Mesh(sp);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            float s = sp.scale, alt = 6f * s;
            CreatureSim sim = CreatureDragonTwistTests.AirborneDragon(sp, s, alt, out _);
            AirTwist tw = sim.Flight.Twist;
            Assert.IsTrue(tw.Begin(m, sim.Flight.Model.HorizontalSpeed, sim.Flight.Model.Forward, Vector3.up));
            var world = new Matrix4x4[sp.BoneCount]; var skin = new Matrix4x4[sp.BoneCount];
            var pos = new Vector3[d.v.Length];
            int frames = 0, checkedTris = 0;
            for (int t = 0; tw.Busy && t < 3000; t++)
            {
                sim.Flight.SetGoals(alt, sp.flight.cruiseSpeed * sim.TimeScale, 0f);
                sim.Tick(kDt, sim.Joints[0]);
                if (t % 4 != 0) continue;
                sim.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector4 q = d.uv2[i];
                    pos[i] = q.z * skin[(int)q.x].MultiplyPoint3x4(d.v[i]) + q.w * skin[(int)q.y].MultiplyPoint3x4(d.v[i]);
                }
                for (int k = 0; k < d.t.Length; k += 3)
                {
                    int i0 = d.t[k], i1 = d.t[k + 1], i2 = d.t[k + 2];
                    Vector3 rest = Vector3.Cross(d.v[i1] - d.v[i0], d.v[i2] - d.v[i0]);
                    if (rest.sqrMagnitude < 1e-24f) continue;   // a pole's degenerate fan triangle
                    // a limb's shoulder ring and blob, half body half limb, lie inside the body (the tucked legs
                    // fold them there; nothing of it is visible)
                    Vector4 q0 = d.uv2[i0];
                    if (Mathf.RoundToInt(d.uv1[i0].x) == CreatureMeshBuilder.PartLeg && Mathf.Min(q0.x, q0.y) < sp.JointCount && Mathf.Max(q0.x, q0.y) >= sp.JointCount && q0.w > 0.4f) continue;
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)d.uv2[i0].x].MultiplyVector(rest);
                    Assert.Greater(Vector3.Dot(now, want), 0f, $"{m} p={tw.Progress:F2}: triangle {k / 3} (part {Mathf.RoundToInt(d.uv1[i0].x)}, bones {d.uv2[i0]}) inverted");
                    checkedTris++;
                }
                frames++;
            }
            TestContext.WriteLine($"{m}: {frames} frames, {checkedTris} triangle poses");
            Assert.Greater(frames, 20);
        }
    }
}
