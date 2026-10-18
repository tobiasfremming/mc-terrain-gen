using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The winged serpent's mesh (WingedMeshBuilder) and bone layout (WingBoneLayout).
    public class WingedSerpentMeshTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        sealed class Data
        {
            public Vector3[] v, n; public int[] t;
            public List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
            public List<Vector4> uv2 = new List<Vector4>();
            public long ms;
        }

        WingedSerpentTestSpecies.Serpent Serpent()
        {
            var s = WingedSerpentTestSpecies.Create();
            trash.Add(s.species);
            return s;
        }

        Data Build(WingedSerpentTestSpecies.Serpent s)
        {
            WingedMeshBuilder.Build(s.species, s.wings); // warm-up (JIT)
            var sw = Stopwatch.StartNew();
            Mesh m = WingedMeshBuilder.Build(s.species, s.wings);
            long ms = sw.ElapsedMilliseconds;
            trash.Add(m);
            var d = new Data { v = m.vertices, n = m.normals, t = m.triangles, ms = ms };
            m.GetUVs(0, d.uv0); m.GetUVs(1, d.uv1); m.GetUVs(2, d.uv2);
            return d;
        }

        static int Part(Data d, int i) => Mathf.RoundToInt(d.uv1[i].x);

        static Matrix4x4[] Skin(Matrix4x4[] world, Matrix4x4[] bind)
        {
            var s = new Matrix4x4[world.Length];
            for (int i = 0; i < s.Length; i++) s[i] = world[i] * bind[i];
            return s;
        }

        static Vector3 SkinPos(Data d, Matrix4x4[] skin, int i)
        {
            Vector4 s = d.uv2[i];
            return s.z * skin[(int)s.x].MultiplyPoint3x4(d.v[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(d.v[i]);
        }

        // as CreatureSkin.hlsl: normalize(blended 3x3 x normal)
        static Vector3 SkinNormal(Data d, Matrix4x4[] skin, int i)
        {
            Vector4 s = d.uv2[i];
            return (s.z * skin[(int)s.x].MultiplyVector(d.n[i]) + s.w * skin[(int)s.y].MultiplyVector(d.n[i])).normalized;
        }

        // ------------------------------------------------------------------ layout

        [Test] public void Bones_Layout_MonitorUnchanged()
        {
            var sp = CreatureTestSpecies.Lizardish();
            trash.Add(sp);
            Assert.AreEqual(sp.BoneCount, WingBoneLayout.BoneCount(sp, 0), "bone count without wings");
            for (int e = 0; e < 2; e++) Assert.AreEqual(sp.EyeBone(e), WingBoneLayout.EyeBone(sp, 0, e), "eye bone without wings");
            for (int l = 0; l < sp.legs.Length; l++)
                for (int p = 0; p < 3; p++) Assert.AreEqual(sp.LegBone(l, p), WingBoneLayout.LegBone(sp.JointCount, l, p));
            var s = Serpent();
            Assert.AreEqual(58, WingBoneLayout.BoneCount(s.species, 2), "design: 32 + 3x4 + 6x2 + 2");
            Assert.AreEqual(32 + 12 + 12, WingBoneLayout.EyeBone(s.species, 2, 0));
            Assert.AreEqual(32 + 12 + 6 + 3, WingBoneLayout.WingBone(s.species, 1, WingBoneLayout.FirstRib));
            foreach (var w in s.wings) Assert.IsNull(w.Validate(s.species));
        }

        // CreatureSpecies with wings: its own layout is WingBoneLayout's, it validates, and its lateral reach
        // (enclosures, culling) covers the spread wing.
        [Test] public void Species_Winged_LayoutAndReach()
        {
            var s = Serpent();
            CreatureSpecies sp = s.species;
            Assert.IsNull(sp.Validate());
            Assert.IsTrue(sp.CanFly);
            Assert.AreEqual(2, sp.WingCount);
            Assert.AreEqual(58, sp.BoneCount);
            Assert.AreEqual(WingBoneLayout.EyeBone(sp, 2, 1), sp.EyeBone(1));
            Assert.AreEqual(WingBoneLayout.WingBone(sp, 1, 5), sp.WingBone(1, 5));
            Assert.Greater(sp.MaxLateralReach, 1.7f, "the spread wing's reach");
            Assert.AreEqual(sp.BoneCount, CreatureRestPose.BindPoses(sp).Length);
            s.wings[1].leg = 0;
            Assert.IsNotNull(sp.Validate(), "two wings on one arm");
        }

        // ------------------------------------------------------------------ mesh

        [Test] public void Mesh_Serpent_Builds()
        {
            var s = Serpent();
            var d = Build(s);
            int nv = d.v.Length, bones = WingBoneLayout.BoneCount(s.species, s.wings.Length);
            TestContext.WriteLine($"{nv} vertices, {d.t.Length / 3} triangles, {bones} bones, build {d.ms} ms");
            Assert.AreEqual(nv, d.n.Length); Assert.AreEqual(nv, d.uv0.Count); Assert.AreEqual(nv, d.uv1.Count); Assert.AreEqual(nv, d.uv2.Count);
            Assert.That(nv, Is.InRange(3000, 15000), "vertex budget (~9k in the design)");
            int membrane = 0;
            var used = new bool[bones];
            for (int i = 0; i < nv; i++)
            {
                Vector4 sk = d.uv2[i];
                Assert.AreEqual(1f, sk.z + sk.w, 1e-5f, "weights at " + i);
                foreach (float b in new[] { sk.x, sk.y })
                {
                    Assert.AreEqual(Mathf.Round(b), b, 1e-6f, "bone index not integral at " + i);
                    Assert.That(b, Is.InRange(0f, bones - 1f), "bone index at " + i);
                }
                if (sk.z > 0f) used[(int)sk.x] = true;
                if (sk.w > 0f) used[(int)sk.y] = true;
                Assert.AreEqual(1f, d.n[i].magnitude, 1e-3f, "normal length at " + i);
                if (Part(d, i) == WingedMeshBuilder.PartMembrane) membrane++;
            }
            Assert.AreEqual(s.wings.Length * WingedMeshBuilder.MembraneVerticesPerWing, membrane, "membrane vertices");
            for (int b = 0; b < bones; b++) Assert.IsTrue(used[b], $"bone {b} skins nothing");
            Assert.AreEqual(0, d.t.Length % 3);
            for (int k = 0; k < d.t.Length; k += 3)
            {
                for (int q = 0; q < 3; q++) Assert.That(d.t[k + q], Is.InRange(0, nv - 1));
                Vector3 a = d.v[d.t[k]], b = d.v[d.t[k + 1]], c = d.v[d.t[k + 2]];
                Assert.Greater(0.5f * Vector3.Cross(b - a, c - a).magnitude, 1e-10f, $"degenerate triangle {k / 3} (part {Part(d, d.t[k])})");
                // rest pose: every wing face agrees with its vertex normals (outward tubes, both membrane
                // sheets); the body's own parts are CreatureMeshTests' business
                if (Part(d, d.t[k]) == CreatureMeshBuilder.PartBody || Part(d, d.t[k]) == CreatureMeshBuilder.PartEye) continue;
                Vector3 vn = d.n[d.t[k]] + d.n[d.t[k + 1]] + d.n[d.t[k + 2]];
                Assert.Greater(Vector3.Dot(Vector3.Cross(b - a, c - a), vn), 0f, $"rest triangle {k / 3} (part {Part(d, d.t[k])}, bones {d.uv2[d.t[k]]}) faces against its normals");
            }
            Assert.Less(d.ms, 20, "build time (budget 5 ms in the Editor)");
        }

        // Rest bone world x bind = identity: the mesh is authored in the bind pose.
        [Test] public void Mesh_BindPose_IsRest()
        {
            var s = Serpent();
            var d = Build(s);
            var world = WingedMeshBuilder.RestBoneWorld(s.species, s.wings);
            var skin = Skin(world, WingedMeshBuilder.BindPoses(s.species, s.wings));
            for (int i = 0; i < d.v.Length; i += 7) Assert.Less((SkinPos(d, skin, i) - d.v[i]).magnitude, 1e-4f, "vertex " + i);
        }

        // Counts wing triangles (any vertex on a wing arm, finger or rib bone): none may collapse or flip
        // against its skinned normals. Returns the number checked.
        static int CheckWing(Data d, Matrix4x4[] skin, bool[] wingBone, string at, ref float worst)
        {
            int count = 0;
            for (int k = 0; k < d.t.Length; k += 3)
            {
                int i0 = d.t[k], i1 = d.t[k + 1], i2 = d.t[k + 2];
                Vector4 sk = d.uv2[i0];
                if (!wingBone[(int)sk.x] && !wingBone[(int)sk.y]) continue;
                Vector3 a = SkinPos(d, skin, i0), b = SkinPos(d, skin, i1), c = SkinPos(d, skin, i2);
                Vector3 cr = Vector3.Cross(b - a, c - a);
                float restArea = Vector3.Cross(d.v[i1] - d.v[i0], d.v[i2] - d.v[i0]).magnitude;
                Assert.Greater(cr.magnitude, 0.02f * restArea, $"triangle {k / 3} (part {Part(d, i0)}, bones {sk}) collapsed at {at}");
                Vector3 vn = SkinNormal(d, skin, i0) + SkinNormal(d, skin, i1) + SkinNormal(d, skin, i2);
                float dot = Vector3.Dot(cr.normalized, vn.normalized);
                worst = Mathf.Min(worst, dot);
                Assert.Greater(dot, 0f, $"triangle {k / 3} (part {Part(d, i0)}, bones {sk}) inverted at {at}");
                count++;
            }
            return count;
        }

        static bool[] WingBones(WingedSerpentTestSpecies.Serpent s)
        {
            CreatureSpecies sp = s.species;
            int n = sp.JointCount, legs = sp.legs.Length;
            var wingBone = new bool[WingBoneLayout.BoneCount(sp, s.wings.Length)];
            foreach (var ws in s.wings) for (int p = 0; p < 3; p++) wingBone[WingBoneLayout.LegBone(n, ws.leg, p)] = true;
            for (int b = WingBoneLayout.WingBone(n, legs, 0, 0); b < WingBoneLayout.EyeBone(n, legs, s.wings.Length, 0); b++) wingBone[b] = true;
            return wingBone;
        }

        // Sweep both wings through the whole stroke (band bottom to the upstroke target) x fold 0..1 (air
        // upstroke up to the ground fold) x hand twist (feathered, flat, pronated) x bank (level, 35 deg):
        // no wing triangle flips against its skinned normals or collapses.
        [Test] public void Mesh_WingSweep_NoInvertedTris()
        {
            var s = Serpent();
            var d = Build(s);
            var sp = s.species;
            var bind = WingedMeshBuilder.BindPoses(sp, s.wings);
            var rest = WingedMeshBuilder.RestBoneWorld(sp, s.wings);
            var wingBone = WingBones(s);
            CreatureSim sim = CreatureRestPose.Sim(sp);
            var wings = new CreatureWings(sp, s.wings, sim.Joints, sim.Rotations, 1f);
            var world = (Matrix4x4[])rest.Clone();
            int checkedTris = 0; float worst = 1f;
            float feather = -s.flight.featherDeg * Mathf.Deg2Rad, pron = s.wings[0].pronationDeg * Mathf.Deg2Rad;
            Quaternion[] restRot = (Quaternion[])sim.Rotations.Clone();
            int g = sp.legs[s.wings[0].leg].attachJoint;
            foreach (float bank in new[] { 0f, 35f })
            {
            // a banked girdle over a body that has not rolled yet (the air chain carries the roll back)
            for (int i = 0; i < restRot.Length; i++)
                sim.Rotations[i] = i <= g + 2 ? Quaternion.AngleAxis(bank, Vector3.forward) * restRot[i] : restRot[i];
            foreach (float fold in new[] { 0f, 0.3f, 0.6f, 0.8f, 1f })
                foreach (float tilt in new[] { feather, 0f, pron })
                    for (int ia = 0; ia <= 8; ia++)
                    {
                        for (int w = 0; w < 2; w++)
                        {
                            float ang = Mathf.Lerp(-wings.BandDownAngle(w), wings.BandUpAngle(w) * (1f + s.wings[w].overshootUp), ia / 8f);
                            wings.SetPose(w, ang, fold, tilt);
                        }
                        wings.Pose();
                        wings.WriteBones(world, 0);
                        checkedTris += CheckWing(d, Skin(world, bind), wingBone, $"bank {bank} fold {fold} tilt {tilt:F2} step {ia}", ref worst);
                    }
            }
            TestContext.WriteLine($"{checkedTris} wing triangle poses checked, worst face/normal agreement {worst:F3}");
            Assert.Greater(checkedTris, 1000);
        }

        // Wing-walk (user decision): CreatureLegs walks the arms as front legs, with the wings folded on
        // them. Walking in circles and turning hard: no wing triangle flips or collapses.
        [Test] public void Mesh_WingWalk_FoldedNoInvertedTris()
        {
            var s = Serpent();
            var d = Build(s);
            var sp = s.species;
            var bind = WingedMeshBuilder.BindPoses(sp, s.wings);
            var wingBone = WingBones(s);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            var wings = new CreatureWings(sp, s.wings, sim.Joints, sim.Rotations, 1f);
            var world = new Matrix4x4[WingBoneLayout.BoneCount(sp, s.wings.Length)];
            int checkedTris = 0; float worst = 1f;
            const float dt = 1f / 60f;
            for (int f = 0; f < 360; f++)
            {
                float t = f * dt;
                // a circle of 1.2 m, then a hard turn the other way
                Vector3 target = t < 3f ? new Vector3(1.2f * Mathf.Sin(1.5f * t), 0f, 1.2f * Mathf.Cos(1.5f * t) + 1.5f)
                                        : new Vector3(-2f, 0f, -1f);
                sim.Tick(dt, target);
                for (int w = 0; w < s.wings.Length; w++)
                {
                    LegState l = sim.Legs[s.wings[w].leg];
                    wings.PoseOnArm(w, l.shoulder, l.elbow, l.foot, 1f);
                }
                WingedMeshBuilder.WriteBones(sim, wings, world);
                if (f % 6 == 0) checkedTris += CheckWing(d, Skin(world, bind), wingBone, $"walk frame {f}", ref worst);
            }
            TestContext.WriteLine($"{checkedTris} folded wing triangle poses checked, worst face/normal agreement {worst:F3}");
            Assert.Greater(checkedTris, 1000);
        }

        // The membrane reads as one smooth sheet: where two zones meet on a ray, both zones' border vertices
        // coincide and share one normal (per sheet), in the rest pose and skinned in flight and folded.
        [Test] public void Mesh_Membrane_SmoothAcrossZones()
        {
            var s = Serpent();
            var d = Build(s);
            var sp = s.species;
            var mem = new List<int>();
            for (int i = 0; i < d.v.Length; i++) if (Part(d, i) == WingedMeshBuilder.PartMembrane) mem.Add(i);
            int cols = WingedMeshBuilder.ZoneColumns + 1, block = cols * (WingedMeshBuilder.ZoneRows + 1);
            var bind = WingedMeshBuilder.BindPoses(sp, s.wings);
            var world = WingedMeshBuilder.RestBoneWorld(sp, s.wings);
            CreatureSim sim = CreatureRestPose.Sim(sp);
            var wings = new CreatureWings(sp, s.wings, sim.Joints, sim.Rotations, 1f);
            float worst = 1f; int pairs = 0;
            foreach (float fold in new[] { -1f, 0f, 0.6f, 1f }) // -1: the rest mesh itself
            {
                Matrix4x4[] skin = null;
                if (fold >= 0f)
                {
                    for (int w = 0; w < 2; w++) wings.SetPose(w, -0.5f * wings.BandDownAngle(w), fold);
                    wings.Pose(); wings.WriteBones(world, 0);
                    skin = Skin(world, bind);
                }
                for (int w = 0; w < s.wings.Length; w++)
                    for (int z = 0; z + 1 < WingedMeshBuilder.MembraneZones; z++)
                        for (int sheet = 0; sheet < 2; sheet++)
                            for (int j = 1; j <= WingedMeshBuilder.ZoneRows; j++)
                            {
                                int a = mem[w * WingedMeshBuilder.MembraneVerticesPerWing + (z * 2 + sheet) * block + j * cols + cols - 1];
                                int b = mem[w * WingedMeshBuilder.MembraneVerticesPerWing + ((z + 1) * 2 + sheet) * block + j * cols];
                                Vector3 pa = skin == null ? d.v[a] : SkinPos(d, skin, a), pb = skin == null ? d.v[b] : SkinPos(d, skin, b);
                                Vector3 na = skin == null ? d.n[a] : SkinNormal(d, skin, a), nb = skin == null ? d.n[b] : SkinNormal(d, skin, b);
                                Assert.Less((pa - pb).magnitude, 1e-4f, $"zones {z}/{z + 1} do not meet (wing {w}, row {j}, fold {fold})");
                                float dot = Vector3.Dot(na, nb);
                                worst = Mathf.Min(worst, dot);
                                Assert.Greater(dot, 0.9999f, $"crease between zones {z}/{z + 1} (wing {w}, sheet {sheet}, row {j}, fold {fold})");
                                pairs++;
                            }
            }
            TestContext.WriteLine($"{pairs} border pairs, worst normal agreement {worst:F5}");
        }

        // Ribs stretched or shortened (the flank moving relative to the wrist; CreatureWings scales a rib
        // bone uniformly by length / rest length): every membrane normal stays as it was, and agrees with
        // its skinned faces.
        [Test] public void Mesh_MembraneNormals_ScaledRibs()
        {
            var s = Serpent();
            var d = Build(s);
            var sp = s.species;
            var bind = WingedMeshBuilder.BindPoses(sp, s.wings);
            var rest = WingedMeshBuilder.RestBoneWorld(sp, s.wings);
            int n = sp.JointCount, legs = sp.legs.Length;
            foreach (float stretch in new[] { 0.75f, 1.3f })
            {
                var world = (Matrix4x4[])rest.Clone();
                for (int w = 0; w < s.wings.Length; w++)
                    for (int k = 0; k < WingSpec.Ribs; k++)
                    {
                        int b = WingBoneLayout.WingBone(n, legs, w, WingBoneLayout.FirstRib + k);
                        float sk = k == 1 ? 1f / stretch : stretch; // neighbouring ribs differently
                        world[b] = rest[b] * Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * sk);
                    }
                var skin = Skin(world, bind);
                int count = 0;
                for (int k = 0; k < d.t.Length; k += 3)
                {
                    int i0 = d.t[k], i1 = d.t[k + 1], i2 = d.t[k + 2];
                    if (Part(d, i0) != WingedMeshBuilder.PartMembrane) continue;
                    Vector3 a = SkinPos(d, skin, i0), b = SkinPos(d, skin, i1), c = SkinPos(d, skin, i2);
                    Vector3 fn = Vector3.Cross(b - a, c - a).normalized;
                    foreach (int i in new[] { i0, i1, i2 })
                    {
                        Vector3 sn = SkinNormal(d, skin, i);
                        // smooth normals lean toward the neighbouring zone by at most half the crease between zones
                        Assert.Greater(Vector3.Dot(sn, fn), 0.99f, $"membrane triangle {k / 3}: normal vs face at stretch {stretch}");
                        Assert.Greater(Vector3.Dot(sn, d.n[i]), 0.99999f, $"membrane vertex {i}: normal changed at stretch {stretch}");
                    }
                    count++;
                }
                Assert.Greater(count, 500);
            }
        }
    }
}
