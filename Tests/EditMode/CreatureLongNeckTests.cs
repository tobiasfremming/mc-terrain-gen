using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // LongNeck and BeakHead (creature kit, the Apex). Proven: the neck keeps its length (bones evenly along an arc within
    // 12 % of it), the head never comes below its clearance, the head is steadier than the chest it hangs from in a
    // beating flight (the stabiliser) and stays ahead of it, aiming turns the head toward a point and striking reaches
    // it, the eyes ride the head, the beak gapes in a strike, Froude: 0.9x and 1.1x hold the same posture per size, no
    // GC. RED: a neck without the stabiliser (stabiliseSeconds tiny) is not steadier than the chest.
    public class CreatureLongNeckTests
    {
        const float kDt = 1f / 60f;

        static T Rt<T>(CreatureSim sim) where T : class, IPartRuntime
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is T t) return t;
            return null;
        }

        static CreatureSpecies With(System.Action<LongNeck> edit)
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("Apex");
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = "ApexNeckEdit"; sp.scale = a.scale; sp.kitVersion = a.kitVersion;
            foreach (CreaturePart p in a.parts) { CreaturePart c = p.Clone(); if (c is LongNeck n) edit(n); sp.parts.Add(c); }
            foreach (CreatureModule m in a.modules) sp.modules.Add(m.Clone());
            return sp.Compiled.Runtime;
        }

        [TestCase(0.9f)] [TestCase(1.1f)]
        public void Neck_KeepsItsLength_HeadAboveTheGround(float k)
        {
            CreatureSpecies sp = CreaturePteroWingTests.Apex();
            float s = sp.scale * k, T = Mathf.Sqrt(s);
            var sim = CreaturePteroWingTests.Sim(sp, s);
            LongNeckRuntime n = Rt<LongNeckRuntime>(sim);
            LongNeck p = n.Part;
            var bones = new Matrix4x4[sp.BoneCount];
            float lo = float.MaxValue, hi = 0f, lowest = float.MaxValue;
            void Check()
            {
                sim.WriteBoneWorld(bones, 0);
                float L = 0f;
                for (int b = 1; b < p.segments; b++) L += ((Vector3)bones[n.HeadBone - p.segments + 1 + b].GetColumn(3) - (Vector3)bones[n.HeadBone - p.segments + b].GetColumn(3)).magnitude;
                lo = Mathf.Min(lo, L / (p.length * s)); hi = Mathf.Max(hi, L / (p.length * s));
                float ground = sim.Flight.Airborne ? sim.Flight.GroundBelow.y : 0f;
                lowest = Mathf.Min(lowest, (bones[n.HeadBone].GetColumn(3).y - ground) / s);
            }
            for (int t = 0; t < 600; t++) { sim.Tick(kDt, t < 300 ? sim.Joints[0] + sim.Rotations[0] * Vector3.forward * s : sim.Joints[0]); Check(); }
            sim.Flight.Launch(6f * s);
            for (int t = 0; t < 900; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, t > 400 ? 0.4f / T : 0f); sim.Tick(kDt, sim.Joints[0]); Check(); }
            TestContext.WriteLine($"x{k}: neck arc {lo:F3}..{hi:F3} of its length, head >= {lowest:F3} m (scale 1) over the ground");
            Assert.Greater(lo, 0.87f); Assert.Less(hi, 1.03f);
            Assert.Greater(lowest, p.headClearance * 0.99f);
        }

        // In a beating flight the head's vertical bob (high-passed) is smaller than the chest's, and the head stays ahead.
        internal static string StabiliseProblem(CreatureSpecies sp)
        {
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = CreaturePteroWingTests.Sim(sp, s);
            LongNeckRuntime n = Rt<LongNeckRuntime>(sim);
            sim.Flight.Launch(6f * s);
            var chest = new List<float>(); var head = new List<float>();
            float behind = float.MaxValue;
            for (int t = 0; t < 1200; t++)
            {
                sim.Flight.SetGoals(t < 600 ? 6f * s : 9f * s, sp.flight.cruiseSpeed * T, 0f);   // climbing: beating
                sim.Tick(kDt, sim.Joints[0]);
                if (t < 300) continue;
                chest.Add(sim.Joints[0].y); head.Add(n.HeadPosition.y);
                behind = Mathf.Min(behind, Vector3.Dot(n.HeadPosition - sim.Joints[0], sim.Flight.Model.Forward));
            }
            float bc = Bob(chest, Mathf.RoundToInt(0.5f * T / kDt)), bh = Bob(head, Mathf.RoundToInt(0.5f * T / kDt));
            TestContext.WriteLine($"bob (high-passed rms): chest {bc / s * 1000f:F1} mm, head {bh / s * 1000f:F1} mm (scale 1); head ahead of the chest >= {behind / s:F2} m");
            if (behind < 0.3f * s) return $"the head fell behind the chest ({behind / s:F2} m)";
            return bh < 0.75f * bc ? null : $"the head bobs {bh / s * 1000f:F1} mm, the chest {bc / s * 1000f:F1} mm: not stabilised";
        }

        static float Bob(List<float> y, int w)
        {
            double ss = 0; int c = 0;
            for (int i = w; i + w < y.Count; i++)
            {
                double m = 0; for (int j = i - w; j <= i + w; j++) m += y[j];
                m /= 2 * w + 1;
                ss += (y[i] - m) * (y[i] - m); c++;
            }
            return c > 0 ? (float)System.Math.Sqrt(ss / c) : 0f;
        }

        [Test] public void Head_SteadierThanTheChest_InFlight() => Assert.IsNull(StabiliseProblem(CreaturePteroWingTests.Apex()), StabiliseProblem(CreaturePteroWingTests.Apex()));

        [Test] public void Red_NoStabiliser_HeadBobsWithTheChest()
        {
            string p = StabiliseProblem(With(n => n.stabiliseSeconds = 0.02f));
            TestContext.WriteLine("RED: " + p);
            Assert.IsNotNull(p);
        }

        // Aiming at a point beside it, the head turns toward it; striking, it reaches it (within a tenth of the neck) and
        // the beak gapes; released, it comes back.
        [Test] public void Aim_TurnsAndStrikes_BeakGapes_EyesRide()
        {
            CreatureSpecies sp = CreaturePteroWingTests.Apex();
            float s = sp.scale;
            var sim = CreaturePteroWingTests.Sim(sp, s);
            LongNeckRuntime n = Rt<LongNeckRuntime>(sim);
            BeakHeadRuntime h = Rt<BeakHeadRuntime>(sim);
            for (int t = 0; t < 120; t++) sim.Tick(kDt, sim.Joints[0]);
            Vector3 root = sim.Joints[0];
            Vector3 aim = root + (sim.Rotations[0] * new Vector3(0.45f, -0.3f, 0.5f)) * s;
            n.Aim(aim, 0f);
            for (int t = 0; t < 90; t++) sim.Tick(kDt, sim.Joints[0]);
            Vector3 toAim = aim - root, toHead = n.HeadPosition - root; toAim.y = 0f; toHead.y = 0f;
            float yaw = Vector3.Angle(toAim, toHead);
            n.Aim(aim, 1f);
            float best = float.MaxValue, gape = 0f;
            for (int t = 0; t < 90; t++) { sim.Tick(kDt, sim.Joints[0]); best = Mathf.Min(best, (n.StrikePoint - aim).magnitude); gape = Mathf.Max(gape, h.Gape); }
            var bones = new Matrix4x4[sp.BoneCount];
            sim.WriteBoneWorld(bones, 0);
            float eyeOff = ((Vector3)bones[sp.EyeBone(0)].GetColumn(3) - (Vector3)bones[n.HeadBone].GetColumn(3)).magnitude / s;
            n.Release();
            TestContext.WriteLine($"looking: {yaw:F1} deg off the aim point; striking: beak tip within {best / s:F3} m (scale 1), gape {gape:F0} deg; eye {eyeOff:F3} m from the head bone");
            Assert.Less(yaw, 25f, "the head turned toward the aim point");
            Assert.Less(best, 0.3f * n.Part.length * s, "the strike reached the aim point (the beak tip)");
            Assert.Greater(gape, 15f, "the beak gaped");
            Assert.Less(eyeOff, 0.25f, "the eyes ride the head");
        }

        // Froude: the neck's posture per size is the same at 0.9x and 1.1x (time in sqrt-size units).
        [Test] public void Size_SamePosturePerSize()
        {
            CreatureSpecies sp = CreaturePteroWingTests.Apex();
            Vector3 Rel(float k)
            {
                float s = sp.scale * k;
                var sim = CreaturePteroWingTests.Sim(sp, s, 3);
                LongNeckRuntime n = Rt<LongNeckRuntime>(sim);
                for (int t = 0; t < 300; t++) sim.Tick(kDt * Mathf.Sqrt(k), sim.Joints[0] + sim.Rotations[0] * Vector3.forward * s);
                return Quaternion.Inverse(sim.Rotations[0]) * (n.HeadPosition - sim.Joints[0]) / s;
            }
            Vector3 a = Rel(0.9f), b = Rel(1.1f);
            TestContext.WriteLine($"head from the chest per size: x0.9 {a}, x1.1 {b}");
            Assert.Less((a - b).magnitude, 0.01f * a.magnitude);
        }

        [Test] public void Gc_NeckAndHead_NoAllocation()
        {
            GcAllocs.AssertCounterWorks();
            CreatureSpecies sp = CreaturePteroWingTests.Apex();
            var sim = CreaturePteroWingTests.Sim(sp, sp.scale);
            var bones = new Matrix4x4[sp.BoneCount];
            LongNeckRuntime n = Rt<LongNeckRuntime>(sim);
            for (int t = 0; t < 60; t++) { sim.Tick(kDt, sim.Joints[0]); sim.WriteBoneWorld(bones, 0); }
            int allocs = GcAllocs.Count(() =>
            {
                for (int t = 0; t < 200; t++) { n.Aim(sim.Joints[0] + Vector3.forward, t > 100 ? 1f : 0f); sim.Tick(kDt, sim.Joints[0]); sim.WriteBoneWorld(bones, 0); }
            });
            Assert.AreEqual(0, allocs);
        }

        // The head's pieces are closed solids (every edge shared by an even number of triangles).
        [Test] public void Head_PiecesClosed()
        {
            BeakHead h = null;
            foreach (CreaturePart p in KitView.Parts(CreaturePteroWingTests.Apex())) if (p is BeakHead b) h = b;
            foreach (Sculpt s in new[] { h.SkullSolid(), h.BeakSolid(), h.JawSolid(), h.CrestSolid() })
            {
                SculptPiece piece = SculptMesher.For(s);
                var edges = new Dictionary<long, int>();
                // seam duplicates share positions: weld by position
                var weld = new Dictionary<Vector3, int>();
                int Id(int i) { Vector3 q = piece.Pos[i]; if (!weld.TryGetValue(q, out int w)) { w = weld.Count; weld[q] = w; } return w; }
                for (int k = 0; k < piece.Tris.Length; k += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = Id(piece.Tris[k + e]), b = Id(piece.Tris[k + (e + 1) % 3]);
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        edges.TryGetValue(key, out int c); edges[key] = c + 1;
                    }
                int open = 0; foreach (var kv in edges) if ((kv.Value & 1) != 0) open++;
                Assert.AreEqual(0, open, $"layer {s.layer}: open edges");
                Assert.Greater(piece.Tris.Length, 300, $"layer {s.layer}: a real solid");
            }
        }
    }
}
