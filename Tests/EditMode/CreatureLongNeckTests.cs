using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // LongNeck and BeakHead (creature kit, the Pterodactyl). Proven: the neck keeps its length (bones evenly along an arc within
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
            CreatureSpecies a = CreatureKitAssets.LoadAsset("Pterodactyl");
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = "PterodactylNeckEdit"; sp.scale = a.scale; sp.kitVersion = a.kitVersion;
            foreach (CreaturePart p in a.parts) { CreaturePart c = p.Clone(); if (c is LongNeck n) edit(n); sp.parts.Add(c); }
            foreach (CreatureModule m in a.modules) sp.modules.Add(m.Clone());
            return sp.Compiled.Runtime;
        }

        [TestCase(0.9f)] [TestCase(1.1f)]
        public void Neck_KeepsItsLength_HeadAboveTheGround(float k)
        {
            CreatureSpecies sp = CreaturePteroWingTests.Pterodactyl();
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

        [Test] public void Head_SteadierThanTheChest_InFlight() => Assert.IsNull(StabiliseProblem(CreaturePteroWingTests.Pterodactyl()), StabiliseProblem(CreaturePteroWingTests.Pterodactyl()));

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
            CreatureSpecies sp = CreaturePteroWingTests.Pterodactyl();
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
            CreatureSpecies sp = CreaturePteroWingTests.Pterodactyl();
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

        // ---------------------------------------------------------------- the neck carried by the body, every phase
        // A whole lab life in the aviary (the lab's brain: walks, stands, quad launches, beats, glides, stalks, stoops,
        // strikes, pulls out, flares, touches down). The neck is a chain driven from the chest: in the chest's frame its
        // base never leans back past vertical nor folds under, the head stays ahead of the neck base and never hangs
        // off the neck's line (curling down off its base), no bone turns more than kSegBend from the last or back along the neck, the head never
        // faces backward, and in level flight (beats, glides) the head is held within kLevel of level and not below the
        // base. Every phase must be seen (else the check proves nothing). Returns null or the first problem.
        const float kSegBend = 20f, kLevel = 30f;

        internal static string Phase(CreatureSim sim, ModularBrain brain, SkyHuntBehaviour hunt, LongNeckRuntime n)
        {
            switch (sim.Mode)
            {
                case FlightMode.Ground: return sim.Flight.Crouching ? "crouch" : sim.Speed > 0.1f * sim.TimeScale ? "walk" : "stand";
                case FlightMode.Launch: return "launch";
                case FlightMode.Flare: return "flare";
                case FlightMode.Touchdown: return "touchdown";
            }
            bool hunting = hunt != null && brain.Behaviours[brain.Current] == hunt;
            if (hunting && (hunt.Current == SkyHuntBehaviour.Phase.Stoop || hunt.Current == SkyHuntBehaviour.Phase.PullUp))
                return n.Strike > 0.3f ? "strike" : hunt.Current == SkyHuntBehaviour.Phase.Stoop ? "stoop" : "pullup";
            if (hunting && hunt.Current == SkyHuntBehaviour.Phase.Stalk) return "stalk";
            return sim.Flight.Model.Gliding ? "glide" : "beat";
        }

        // The neck's pose in the chest's frame: [0] the base's elevation over the chest's line, [1] the head ahead of the
        // root along the chest (x L), [2] the head over the root in the chest's frame (deg: 0 ahead, 90 straight up off
        // its back), [3] the curl (the chord's elevation under the base direction's: a head hanging off a raised base
        // -90), [4] the head's pitch (world), [5] the head's angle off the chest's forward, [6] the largest bone-to-bone
        // turn, [7] the least bone direction along the chord, [8] the head over the root (world, x L).
        internal static float[] PoseMetrics(Matrix4x4[] bones, int b0, int K, Quaternion chest, float L)
        {
            Vector3 cf = chest * Vector3.forward, cu = chest * Vector3.up;
            Vector3 root = bones[b0].GetColumn(3), head = bones[b0 + K - 1].GetColumn(3), rh = head - root;
            Vector3 d0 = ((Vector3)bones[b0 + 1].GetColumn(3) - root).normalized;
            Vector3 hd = ((Vector3)bones[b0 + K - 1].GetColumn(2)).normalized;
            float bend = 0f, along = 1f;
            Vector3 cd = rh.normalized;
            for (int k = 0; k < K; k++)
            {
                Vector3 dk = ((Vector3)bones[b0 + k].GetColumn(2)).normalized;
                along = Mathf.Min(along, Vector3.Dot(dk, cd));
                if (k > 0) bend = Mathf.Max(bend, Vector3.Angle(bones[b0 + k - 1].GetColumn(2), dk));
            }
            return new[]
            {
                Mathf.Atan2(Vector3.Dot(d0, cu), Vector3.Dot(d0, cf)) * Mathf.Rad2Deg,
                Vector3.Dot(rh, cf) / L,
                Mathf.Atan2(Vector3.Dot(rh, cu), Vector3.Dot(rh, cf)) * Mathf.Rad2Deg,
                (Mathf.Asin(Mathf.Clamp(rh.y / rh.magnitude, -1f, 1f)) - Mathf.Asin(Mathf.Clamp(d0.y, -1f, 1f))) * Mathf.Rad2Deg,
                Mathf.Asin(Mathf.Clamp(hd.y, -1f, 1f)) * Mathf.Rad2Deg,
                Vector3.Angle(hd, cf),
                bend, along, rh.y / L,
            };
        }

        // null, or what is wrong with the pose (`level`: in level flight - beats, glides)
        internal static string PoseProblem(float[] m, bool level)
        {
            if (m[0] >= 90f) return $"the neck's base leans back past vertical ({m[0]:F0} deg off the chest)";
            if (m[0] < -60f) return $"the neck's base folds under the chest ({m[0]:F0} deg)";
            if (m[2] >= 90f || m[2] < -70f) return $"the head is not ahead of the neck base ({m[2]:F0} deg over the chest's line, {m[1]:F2} L along it)";
            if (m[3] < -45f) return $"the head hangs below the neck's line (the neck curls {-m[3]:F0} deg down off its base)";
            if (m[6] > kSegBend || m[7] < 0.7f) return $"the neck kinks or turns back (bone to bone {m[6]:F0} deg, along the chord >= {m[7]:F2})";
            if (m[5] > 100f) return $"the head faces backward ({m[5]:F0} deg off the chest's forward)";
            if (level && Mathf.Abs(m[4]) > kLevel) return $"the head is not held level in flight (pitch {m[4]:F0} deg)";
            if (level && m[8] < -0.5f) return $"the head dangles below the neck base in flight ({m[8]:F2} L)";
            return null;
        }

        internal static string PostureProblem(CreatureSpecies sp, float s, int seed, out string table)
        {
            float T = Mathf.Sqrt(s);
            EnclosureArea area = CreatureSkyHuntTests.Aviary(sp, s);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, s, seed) { Bounds = area };
            var brain = BrainFactory.Create(sp, area, seed) as ModularBrain;
            SkyHuntBehaviour hunt = null;
            foreach (IBehaviour b in brain.Behaviours) if (b is SkyHuntBehaviour h) hunt = h;
            LongNeckRuntime n = Rt<LongNeckRuntime>(sim);
            int K = n.Part.segments, b0 = n.HeadBone - K + 1;
            float L = n.Part.length * s;
            var bones = new Matrix4x4[sp.BoneCount];
            var seen = new SortedDictionary<string, float[]>();   // phase -> base min/max, ahead min, curl min, pitch min/max, bend max, ticks
            string problem = null;
            for (int t = 0; t < (int)(420f * T / kDt); t++)
            {
                sim.Tick(kDt, brain.Target(sim, kDt));
                sim.WriteBoneWorld(bones, 0);
                string ph = Phase(sim, brain, hunt, n);
                float[] m = PoseMetrics(bones, b0, K, sim.Rotations[n.Part.at.joint], L);
                if (!seen.TryGetValue(ph, out float[] a)) seen[ph] = a = new[] { 999f, -999f, 999f, 999f, 999f, -999f, 0f, 0f };
                a[0] = Mathf.Min(a[0], m[0]); a[1] = Mathf.Max(a[1], m[0]); a[2] = Mathf.Min(a[2], m[1]); a[3] = Mathf.Min(a[3], m[3]);
                a[4] = Mathf.Min(a[4], m[4]); a[5] = Mathf.Max(a[5], m[4]); a[6] = Mathf.Max(a[6], m[6]); a[7]++;
                if (problem != null) continue;
                string q = PoseProblem(m, ph == "beat" || ph == "glide");
                if (q != null) problem = $"{ph} at {t * kDt:F1} s: {q}";
            }
            var sb = new System.Text.StringBuilder();
            foreach (var kv in seen)
                sb.Append($"{kv.Key,-10} {kv.Value[7] * kDt,6:F1} s  base {kv.Value[0],4:F0}..{kv.Value[1],3:F0} deg  head ahead >= {kv.Value[2]:F2} L, curl >= {kv.Value[3],4:F0} deg  pitch {kv.Value[4],4:F0}..{kv.Value[5],3:F0}  bend <= {kv.Value[6]:F0}\n");
            table = sb.ToString();
            foreach (string must in new[] { "walk", "launch", "beat", "glide", "stoop", "strike", "flare", "touchdown" })
                if (!seen.ContainsKey(must)) return problem ?? $"the run never showed the phase '{must}'";
            return problem;
        }

        [TestCase(0.9f, 1)] [TestCase(1.1f, 2)]
        public void Neck_CarriedByTheBody_EveryPhase(float k, int seed)
        {
            CreatureSpecies sp = CreaturePteroWingTests.Pterodactyl();
            string p = PostureProblem(sp, sp.scale * k, seed, out string table);
            TestContext.WriteLine($"x{k}:\n{table}");
            Assert.IsNull(p, p);
        }

        // RED: the checker catches the reported break. A good gliding pose, then the neck swung back over the shoulders
        // (the head craned back past vertical) and swung down under the chest (the head hanging): both are problems.
        [Test] public void Red_CranedBackOrHangingNeck_IsCaught()
        {
            CreatureSpecies sp = CreaturePteroWingTests.Pterodactyl();
            float s = sp.scale, T = Mathf.Sqrt(s);
            var sim = CreaturePteroWingTests.Sim(sp, s);
            LongNeckRuntime n = Rt<LongNeckRuntime>(sim);
            sim.Flight.Launch(6f * s);
            for (int t = 0; t < 900; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); }
            int K = n.Part.segments, b0 = n.HeadBone - K + 1;
            float L = n.Part.length * s;
            var bones = new Matrix4x4[sp.BoneCount];
            sim.WriteBoneWorld(bones, 0);
            Quaternion chest = sim.Rotations[n.Part.at.joint];
            string good = PoseProblem(PoseMetrics(bones, b0, K, chest, L), true);
            Assert.IsNull(good, "the flown pose is good: " + good);
            Vector3 root = bones[b0].GetColumn(3), right = chest * Vector3.right;
            string Swung(float deg)
            {
                var b = (Matrix4x4[])bones.Clone();
                Matrix4x4 rot = Matrix4x4.TRS(root, Quaternion.AngleAxis(deg, right), Vector3.one) * Matrix4x4.TRS(-root, Quaternion.identity, Vector3.one);
                for (int k = 0; k < K; k++) b[b0 + k] = rot * b[b0 + k];
                return PoseProblem(PoseMetrics(b, b0, K, chest, L), true);
            }
            string back = Swung(-120f), down = Swung(80f);
            TestContext.WriteLine($"RED craned back: {back}; RED hanging: {down}");
            Assert.IsNotNull(back); Assert.IsNotNull(down);
        }

        // RED: a head that follows the chest's whole pitch with no level hold is caught by the level-flight check.
        [Test] public void Red_HeadFollowsTheChest_NotLevel()
        {
            string p = PostureProblem(With(nk => { nk.headPitchFollow = 1f; nk.headLevelDeg = 90f; nk.basePitchFollow = 1f; nk.flightBaseDeg = 0f; nk.flightHeadDeg = 0f; }), 3.6f, 1, out string table);
            TestContext.WriteLine("RED: " + p + "\n" + table);
            Assert.IsNotNull(p);
        }

        [Test] public void Gc_NeckAndHead_NoAllocation()
        {
            GcAllocs.AssertCounterWorks();
            CreatureSpecies sp = CreaturePteroWingTests.Pterodactyl();
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
            foreach (CreaturePart p in KitView.Parts(CreaturePteroWingTests.Pterodactyl())) if (p is BeakHead b) h = b;
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
