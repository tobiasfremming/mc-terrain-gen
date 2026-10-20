using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // PteroWing (creature kit, the Apex): a pterosaur's wing flown by the shipped flight system. Proven on the Apex: it
    // composes as a wing (the flight's bones first, its own after), its bind pose agrees with its mesh, the membrane
    // never turns inside out through a whole flight (launch, beats, glide, landing) and a wing-walk, the membrane's
    // leading edge stays on the four-phalanx finger as it flexes, the tip lags the stroke and the folded finger curls,
    // and the walking membrane clears the ground. RED: a spread bind (bindFold 0) turns the folded membrane inside
    // out; no lag (flexDeg 0) fails the lag test.
    public class CreaturePteroWingTests
    {
        internal const float kDt = 1f / 60f;

        internal static CreatureSpecies Apex() => CreatureKitAssets.Load("Apex");

        // A copy of the Apex with one PteroWing field changed (a fresh compile).
        internal static CreatureSpecies ApexWith(System.Action<PteroWing> edit)
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("Apex");
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = "ApexEdit";
            sp.scale = a.scale; sp.kitVersion = a.kitVersion;
            sp.parts = new List<CreaturePart>(); sp.modules = new List<CreatureModule>();
            foreach (CreaturePart p in a.parts) { CreaturePart c = p.Clone(); if (c is PteroWing w) edit(w); sp.parts.Add(c); }
            foreach (CreatureModule m in a.modules) sp.modules.Add(m.Clone());
            return sp.Compiled.Runtime;
        }

        internal static CreatureSim Sim(CreatureSpecies sp, float s, int seed = 1) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed);

        // The species' skinned mesh: posed vertices and inverted triangles against the bind.
        internal sealed class Skinned
        {
            public readonly Vector3[] V, P; public readonly int[] T;
            public readonly List<Vector2> Uv0 = new List<Vector2>(), Uv1 = new List<Vector2>();
            public readonly List<Vector4> Uv2 = new List<Vector4>();
            readonly Matrix4x4[] bind, world, skin;
            public Skinned(CreatureSpecies sp)
            {
                Mesh mesh = CreatureKitAssets.BuildMesh(sp);
                V = mesh.vertices; T = mesh.triangles;
                mesh.GetUVs(0, Uv0); mesh.GetUVs(1, Uv1); mesh.GetUVs(2, Uv2);
                Object.DestroyImmediate(mesh);
                bind = CreatureRestPose.BindPoses(sp);
                world = new Matrix4x4[sp.BoneCount]; skin = new Matrix4x4[sp.BoneCount];
                P = new Vector3[V.Length];
            }
            public void Pose(CreatureSim sim)
            {
                sim.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < V.Length; i++) { Vector4 s = Uv2[i]; P[i] = s.z * skin[(int)s.x].MultiplyPoint3x4(V[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(V[i]); }
            }
            public int Part(int i) => Mathf.RoundToInt(Uv1[i].x);
            // inverted triangles of part `part` (-1 = any) in the last pose, and the first one described
            public int Inverted(int part, out string first)
            {
                first = null; int n = 0;
                for (int k = 0; k < T.Length; k += 3)
                {
                    int i0 = T[k], i1 = T[k + 1], i2 = T[k + 2];
                    if (part >= 0 && Part(i0) != part) continue;
                    Vector3 rest = Vector3.Cross(V[i1] - V[i0], V[i2] - V[i0]);
                    Vector3 now = Vector3.Cross(P[i1] - P[i0], P[i2] - P[i0]);
                    Vector3 want = skin[(int)Uv2[i0].x].MultiplyVector(rest);
                    if (!(Vector3.Dot(now, want) > 0f)) { n++; if (first == null) first = $"triangle {k / 3} part {Part(i0)} bones {Uv2[i0]}"; }
                }
                return n;
            }
        }

        static PteroWingRuntime Wing(CreatureSim sim, int w = 0)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is PteroWingRuntime p && p.Wing == w) return p;
            return null;
        }

        [Test] public void Compose_FlightBonesFirst_ThenItsOwn()
        {
            CompiledSpecies c = CreatureKitAssets.LoadAsset("Apex").Compiled;
            Assert.IsNull(c.Error, c.Error);
            Assert.AreEqual(2, c.Wings.Count);
            CreatureSpecies sp = c.Runtime;
            Assert.IsTrue(sp.CanFly);
            for (int i = 0; i < c.Parts.Count; i++)
            {
                if (!(c.Parts[i] is PteroWing pw)) continue;
                int w = PteroWing.WingIndex(c.Parts, i);
                Assert.AreEqual(c.Layout.Start(i), sp.WingBone(w, 0), "the flight's bones start the block");
                Assert.AreEqual(WingBoneLayout.Bones(pw.spec) + PteroWing.ExtraBones, c.Layout.Ranges[System.Array.FindIndex(c.Layout.Ranges, r => r.Part == i)].Count);
            }
            var sim = Sim(sp, sp.scale);
            Assert.IsNotNull(Wing(sim, 0)); Assert.IsNotNull(Wing(sim, 1));
        }

        // The bind pose is the mesh's pose: skinned by the rest bones themselves every vertex stays put (the phalanges
        // and the part-folded wing are re-posed on the bind's wings, KitRuntime.AfterSpreadWings).
        [Test] public void Bind_RestBones_ReproduceTheMesh()
        {
            CreatureSpecies sp = Apex();
            var m = new Skinned(sp);
            Matrix4x4[] rest = CreatureRestPose.BoneWorld(sp), bind = CreatureRestPose.BindPoses(sp);
            float worst = 0f;
            for (int i = 0; i < m.V.Length; i++)
            {
                Vector4 s = m.Uv2[i];
                Vector3 p = s.z * (rest[(int)s.x] * bind[(int)s.x]).MultiplyPoint3x4(m.V[i]) + s.w * (rest[(int)s.y] * bind[(int)s.y]).MultiplyPoint3x4(m.V[i]);
                worst = Mathf.Max(worst, (p - m.V[i]).magnitude);
            }
            Assert.Less(worst, 1e-4f);
        }

        // A whole flight and a wing-walk without a membrane (or any) triangle turning inside out, at 0.9x and 1.1x.
        [TestCase(0.9f)] [TestCase(1.1f)]
        public void Flight_NoInvertedTriangle(float k)
        {
            CreatureSpecies sp = Apex();
            float s = sp.scale * k, T = Mathf.Sqrt(s);
            var m = new Skinned(sp);
            var sim = Sim(sp, s);
            string worst = null; int frames = 0;
            void Check(string when)
            {
                m.Pose(sim); frames++;
                if (worst == null && m.Inverted(-1, out string f) > 0) worst = when + ": " + f;
            }
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * s); if (t % 6 == 0) Check($"walk t{t}"); }
            Assert.IsTrue(sim.Flight.Launch(6f * s), "launch");
            for (int t = 0; t < 900; t++)
            {
                float goal = t < 600 ? 6f * s : 1f * s;   // beats, then a glide down
                sim.Flight.SetGoals(goal, sp.flight.cruiseSpeed * T, t > 300 && t < 450 ? 0.5f / T : 0f);
                sim.Tick(kDt, sim.Joints[0]);
                if (t % 3 == 0) Check($"air t{t} mode {sim.Mode}");
            }
            sim.Flight.Land(sim.Flight.Model.Position + sim.Flight.Model.Forward * (8f * s) - Vector3.up * sim.Flight.Model.Altitude);
            for (int t = 0; t < 7200 && sim.Mode != FlightMode.Ground; t++)
            {
                sim.Tick(kDt, sim.Joints[0]);
                if (t % 3 == 0) Check($"landing t{t} mode {sim.Mode}");
            }
            Assert.AreEqual(FlightMode.Ground, sim.Mode, "it lands");
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * s); if (t % 6 == 0) Check($"after t{t}"); }
            TestContext.WriteLine($"x{k}: {frames} frames checked");
            Assert.IsNull(worst, worst);
        }

        // The membrane's leading edge (zone 0, on the wing finger) stays on the drawn finger while it flexes: every
        // vertex of the finger's line within the finger's root radius of the phalanx polyline.
        [Test] public void LeadingEdge_RidesTheFlexingFinger()
        {
            CreatureSpecies sp = Apex();
            float s = sp.scale, T = Mathf.Sqrt(s);
            var m = new Skinned(sp);
            var sim = Sim(sp, s);
            sim.Flight.Launch(6f * s);
            PteroWing pw = null; foreach (CreaturePart q in KitView.Parts(sp)) if (q is PteroWing x) { pw = x; break; }
            PteroWingRuntime rt = Wing(sim, 0);
            var bones = new Matrix4x4[sp.BoneCount];
            float worst = 0f, maxFlex = 0f;
            for (int t = 0; t < 600; t++)
            {
                sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f);
                sim.Tick(kDt, sim.Joints[0]);
                if (t < 120 || t % 4 != 0) continue;
                m.Pose(sim);
                sim.WriteBoneWorld(bones, 0);
                maxFlex = Mathf.Max(maxFlex, Mathf.Abs(rt.Flex));
                // the polyline: knuckle, phalanx roots, tip
                var pts = new List<Vector3> { bones[sp.WingBone(0, 0)].GetColumn(3) };
                for (int k = 1; k < PteroWing.Phalanges; k++) pts.Add(bones[rt.FirstExtraBone + k - 1].GetColumn(3));
                Matrix4x4 last = bones[rt.FirstExtraBone + PteroWing.Phalanges - 2];
                pts.Add(last.MultiplyPoint3x4(new Vector3(0f, 0f, pw.phalanges[3] * pw.spec.fingerLengths[0])));
                for (int i = 0; i < m.V.Length; i++)
                {
                    if (m.Part(i) != PartMeshEmitter.PartMembrane) continue;
                    // zone 0's u = 0 column: uv0.y == 0 exactly (z + u) / Z
                    if (m.Uv0[i].y != 0f) continue;
                    Vector4 sk = m.Uv2[i];
                    bool onFinger = sk.x == sp.WingBone(0, 0) || (sk.x >= rt.FirstExtraBone && sk.x < rt.FirstExtraBone + PteroWing.Phalanges - 1);
                    if (!onFinger) continue;   // the other wing, the propatagium
                    float d = float.MaxValue;
                    for (int k = 0; k + 1 < pts.Count; k++) d = Mathf.Min(d, SegDist(m.P[i], pts[k], pts[k + 1]));
                    worst = Mathf.Max(worst, d);
                }
            }
            TestContext.WriteLine($"leading edge off the finger <= {worst / s * 1000f:F2} mm (scale 1), |flex| <= {maxFlex:F1} deg");
            Assert.Greater(maxFlex, 0.5f * pw.flexDeg, "the finger flexed");
            Assert.Less(worst / s, pw.fingerRoot, "the membrane's leading edge left the finger");
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        // The tip lags the stroke: up (flex > 0) on the downstroke, down on the upstroke; on the ground the folded
        // finger curls by foldCurlDeg.
        [Test] public void Flex_LagsTheStroke_CurlsWhenFolded()
        {
            CreatureSpecies sp = Apex();
            Assert.IsNull(LagProblem(sp), LagProblem(sp));
            var sim = Sim(sp, sp.scale);
            for (int t = 0; t < 120; t++) sim.Tick(kDt, sim.Joints[0]);
            PteroWing pw = null; foreach (CreaturePart q in KitView.Parts(sp)) if (q is PteroWing x) { pw = x; break; }
            Assert.AreEqual(pw.foldCurlDeg, Wing(sim, 0).Curl, 0.05f * Mathf.Abs(pw.foldCurlDeg) + 1e-3f, "curled when folded on the ground");
        }

        internal static string LagProblem(CreatureSpecies sp)
        {
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = Sim(sp, s);
            sim.Flight.Launch(6f * s);
            PteroWingRuntime rt = Wing(sim, 0);
            float down = 0f, up = 0f; int nd = 0, nu = 0;
            for (int t = 0; t < 900; t++)
            {
                sim.Flight.SetGoals(8f * s, sp.flight.cruiseSpeed * T, 0f);   // climbing: beating hard
                sim.Tick(kDt, sim.Joints[0]);
                if (t < 120 || sim.Flight.Model.Gliding) continue;
                if (sim.Flight.Wings.State[0].angleRate < 0f) { down += rt.Flex; nd++; } else { up += rt.Flex; nu++; }
            }
            if (nd == 0 || nu == 0) return "no beats";
            down /= nd; up /= nu;
            TestContext.WriteLine($"mean flex: downstroke {down:F2} deg, upstroke {up:F2} deg");
            return down > 0.5f && up < down - 1f ? null : $"the tip does not lag the stroke (downstroke {down:F2}, upstroke {up:F2})";
        }

        [Test] public void Red_NoFlex_LagFails()
        {
            string p = LagProblem(ApexWith(w => w.flexDeg = 0f));
            TestContext.WriteLine("RED: " + p);
            Assert.IsNotNull(p);
        }

        // RED: the spread bind (bindFold 0, as a Wing) - folded on the ground the membrane's zones close to a seventh and
        // turn inside out.
        [Test] public void Red_SpreadBind_FoldedMembraneInverts()
        {
            CreatureSpecies sp = ApexWith(w => w.bindFold = 0f);
            var m = new Skinned(sp);
            var sim = Sim(sp, sp.scale);
            for (int t = 0; t < 30; t++) sim.Tick(kDt, sim.Joints[0] + Vector3.forward);
            m.Pose(sim);
            int n = m.Inverted(PartMeshEmitter.PartMembrane, out string f);
            TestContext.WriteLine($"RED: {n} membrane triangles inverted ({f})");
            Assert.Greater(n, 0);
        }

        // Walking (straight, then the kit's tight circle) the folded membrane stays off the ground.
        [TestCase(0.9f)] [TestCase(1.1f)]
        public void Walk_MembraneClearsTheGround(float k)
        {
            CreatureSpecies sp = Apex();
            float s = sp.scale * k, T = Mathf.Sqrt(s);
            var m = new Skinned(sp);
            var sim = Sim(sp, s);
            float low = float.MaxValue;
            Vector3 centre = new Vector3(0f, 0f, 0.25f) * s;
            for (int t = 0; t < 900; t++)
            {
                Vector3 target = t < 450 ? sim.Joints[0] + sim.Rotations[0] * Vector3.forward * s
                                         : centre + new Vector3(Mathf.Cos(t / 60f * 3f / T), 0f, Mathf.Sin(t / 60f * 3f / T)) * (0.12f * s);
                sim.Tick(kDt, target);
                if (t % 5 != 0) continue;
                m.Pose(sim);
                for (int i = 0; i < m.P.Length; i++) if (m.Part(i) == PartMeshEmitter.PartMembrane) low = Mathf.Min(low, m.P[i].y);
            }
            TestContext.WriteLine($"x{k}: lowest membrane point {low / s:F3} m (scale 1)");
            Assert.Greater(low, 0.02f * s);
        }
    }
}
