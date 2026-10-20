using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The Apex (creature kit): the apex predator of the sky - a huge flyer that must not read as a pterosaur. Its look parts
    // are its own types (SailWing, SerpentNeck, CasqueHead) on the Pterodactyl's systems (Fly + the PteroWing chains,
    // LongNeck's carriage, BeakHead's jaw, QuadLaunch, SkyHunt). Proven: it composes from its own look parts; it is bigger
    // and built heavier than the Pterodactyl (span, chest, neck) with a short toothed head, not a beak; the folded blades
    // stand high over its back when it walks; the neck is carried by the chest in every phase of a lab life and steadies
    // the head in flight (the Pterodactyl's neck tests); the sail is a chain (tip lags root to tip, trailing edge lags the
    // leading edge); its bind reproduces the mesh; the walking sail clears the ground; the head's solids are closed; the
    // mouth is held open; the planted claw stays put in the picture (WingSpec.thumbPlant); it lands under its own brain
    // (SkyHunt.handBack: a bout that ends in a held glide frees the wings). REDs: no lift (the blades lie low), a shut mouth,
    // the claw not planted, the hunt not handing the wings back.
    public class CreatureApexTests
    {
        const float kDt = 1f / 60f;

        internal static CreatureSpecies Apex() => CreatureKitAssets.Load("Apex");

        // A copy of the Apex with its parts and modules edited (a fresh compile).
        internal static CreatureSpecies ApexWith(System.Action<CreaturePart> part = null, System.Action<CreatureModule> module = null)
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("Apex");
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = "ApexEdit"; sp.scale = a.scale; sp.kitVersion = a.kitVersion;
            sp.parts = new List<CreaturePart>(); sp.modules = new List<CreatureModule>();
            foreach (CreaturePart p in a.parts) { CreaturePart c = p.Clone(); part?.Invoke(c); sp.parts.Add(c); }
            foreach (CreatureModule m in a.modules) { CreatureModule c = m.Clone(); module?.Invoke(c); sp.modules.Add(c); }
            return sp.Compiled.Runtime;
        }

        static T Part<T>(CreatureSpecies sp) where T : CreaturePart
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is T t) return t;
            return null;
        }

        static T Rt<T>(CreatureSim sim) where T : class, IPartRuntime
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is T t) return t;
            return null;
        }

        // ---------------------------------------------------------------- its own parts on the Pterodactyl's systems

        [Test] public void Compose_OwnLookParts_OnThePterodactylsSystems()
        {
            CompiledSpecies c = CreatureKitAssets.LoadAsset("Apex").Compiled;
            Assert.IsNull(c.Error, c.Error);
            int sails = 0, necks = 0, heads = 0;
            foreach (CreaturePart p in c.Parts)
            {
                Assert.AreNotEqual(typeof(PteroWing), p.GetType(), "no pterosaur wing");
                Assert.AreNotEqual(typeof(LongNeck), p.GetType(), "no pterosaur neck");
                Assert.AreNotEqual(typeof(BeakHead), p.GetType(), "no beaked head");
                if (p is SailWing) sails++;
                if (p is SerpentNeck) necks++;
                if (p is CasqueHead) heads++;
            }
            Assert.AreEqual(2, sails); Assert.AreEqual(1, necks); Assert.AreEqual(1, heads);
            CreatureSpecies sp = c.Runtime;
            Assert.IsTrue(sp.CanFly);
            var sim = CreaturePteroWingTests.Sim(sp, sp.scale);
            int wings = 0;
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is PteroWingRuntime) wings++;
            Assert.AreEqual(2, wings, "the sail wings run the PteroWing's chains");
            Assert.IsNotNull(Rt<LongNeckRuntime>(sim), "the neck is carried by LongNeck's runtime");
            Assert.IsNotNull(Rt<BeakHeadRuntime>(sim), "the head's jaw and eyes by BeakHead's runtime");
            Assert.IsTrue(sp.flight.vaultSpeed > 0f, "QuadLaunch");
        }

        // ---------------------------------------------------------------- not a pterosaur: bigger, heavier, a toothed head

        static Bounds RestBounds(CreatureSpecies sp)
        {
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = m.vertices;
            Object.DestroyImmediate(m);
            var b = new Bounds(v[0], Vector3.zero);
            for (int i = 1; i < v.Length; i++) b.Encapsulate(v[i]);
            return b;
        }

        static float ChestSection(CreatureSpecies sp)
        {
            float best = 0f;
            for (int i = 0; i < sp.JointCount; i++) best = Mathf.Max(best, sp.joints[i].halfWidth * sp.joints[i].halfHeight);
            return best;
        }

        [Test] public void Bigger_AndBuiltHeavier_ThanThePterodactyl()
        {
            CreatureSpecies a = Apex(), p = CreaturePteroWingTests.Pterodactyl();
            float spanA = RestBounds(a).size.x * a.scale, spanP = RestBounds(p).size.x * p.scale;
            float lenA = (a.JointCount - 1) * a.spacing * a.scale, lenP = (p.JointCount - 1) * p.spacing * p.scale;
            // the chest's section against the span (a heavy body under its wings) and the neck's girth against its length
            float chestA = ChestSection(a) / (spanA / a.scale * spanA / a.scale), chestP = ChestSection(p) / (spanP / p.scale * spanP / p.scale);
            SerpentNeck n = Part<SerpentNeck>(a); LongNeck np = Part<LongNeck>(p);
            float girthA = n.Width(0.1f) / n.length, girthP = np.rootRadius / np.length;
            CasqueHead h = Part<CasqueHead>(a);
            TestContext.WriteLine($"span {spanA:F1} m vs {spanP:F1} m; body {lenA:F1} m vs {lenP:F1} m; chest/span^2 {chestA:E2} vs {chestP:E2}; neck girth/length {girthA:F3} vs {girthP:F3}; snout/skull {h.beakLength / h.skullLength:F2}");
            Assert.Greater(spanA, 1.3f * spanP, "a bigger span");
            Assert.Greater(lenA, 1.5f * lenP, "a longer body");
            Assert.Greater(chestA, 2f * chestP, "a heavier chest for its wings");
            Assert.Greater(girthA, 1.4f * girthP, "a thicker neck");
            Assert.Less(h.beakLength, 1.5f * h.skullLength, "a short snout, not a long beak");
            Assert.Greater(h.teeth, 0, "teeth");
        }

        // Walking, the folded blades stand high over its back: each spar's tip above the chest by more than kRise x the
        // chest's own height over the ground (the reference image: the tips at ~1.6 x the chest's height - sails taller
        // than the body), and behind the shoulder.
        const float kRise = 0.6f;
        internal static float BladeRise(CreatureSpecies sp, out float behind)
        {
            float s = sp.scale;
            var sim = CreaturePteroWingTests.Sim(sp, s);
            var bones = new Matrix4x4[sp.BoneCount];
            float rise = float.MaxValue; behind = float.MaxValue;
            SailWing sw = Part<SailWing>(sp);
            for (int t = 0; t < 240; t++)
            {
                sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * s);
                if (t < 60) continue;
                sim.WriteBoneWorld(bones, 0);
                foreach (IPartRuntime r in sim.PartRuntimes)
                {
                    if (!(r is PteroWingRuntime pw)) continue;
                    Matrix4x4 last = bones[pw.FirstExtraBone + PteroWing.Phalanges - 2];
                    Vector3 tip = last.MultiplyPoint3x4(new Vector3(0f, 0f, sw.phalanges[3] * sw.spec.fingerLengths[0]));
                    float chest = sim.Joints[0].y;
                    rise = Mathf.Min(rise, (tip.y - chest) / chest);
                    behind = Mathf.Min(behind, Vector3.Dot(sim.Joints[1] - tip, sim.Rotations[1] * Vector3.forward) / s);
                }
            }
            return rise;
        }

        [Test] public void Walking_TheFoldedBladesStandTallerThanTheBody()
        {
            float rise = BladeRise(Apex(), out float behind);
            TestContext.WriteLine($"blade tips above the chest by >= {rise:F2} x the chest's height; behind the shoulder >= {behind:F2} m (scale 1)");
            Assert.Greater(rise, kRise);
            Assert.Greater(behind, 0f);
        }

        [Test] public void Red_NoGroundLift_TheBladesLieLow()
        {
            float rise = BladeRise(ApexWith(p => { if (p is SailWing w) w.spec.groundLiftDeg = 0f; }), out _);
            TestContext.WriteLine($"RED: no lift, blade tips above the chest by {rise:F2} x its height");
            Assert.Less(rise, kRise);
        }

        // ---------------------------------------------------------------- the neck carried by the chest (the Pterodactyl's tests)

        [TestCase(0.9f, 1)] [TestCase(1.1f, 2)]
        public void Neck_CarriedByTheBody_EveryPhase(float k, int seed)
        {
            CreatureSpecies sp = Apex();
            string p = CreatureLongNeckTests.PostureProblem(sp, sp.scale * k, seed, out string table);
            TestContext.WriteLine($"x{k}:\n{table}");
            Assert.IsNull(p, p);
        }

        [Test] public void Head_SteadierThanTheChest_InFlight()
        {
            string p = CreatureLongNeckTests.StabiliseProblem(Apex());
            Assert.IsNull(p, p);
        }

        // ---------------------------------------------------------------- the sail as chains

        [TestCase(0.9f)] [TestCase(1.1f)]
        public void Sail_IsAChain_TipLagsRootToTip_TrailingEdgeLagsLeading(float k)
        {
            CreatureSpecies sp = Apex();
            string p = CreaturePteroWingTests.ChainProblem(sp, sp.scale * k, out string report);
            TestContext.WriteLine($"x{k}: {report}");
            Assert.IsNull(p, p);
        }

        // ---------------------------------------------------------------- the mesh

        [Test] public void Bind_RestBones_ReproduceTheMesh()
        {
            CreatureSpecies sp = Apex();
            var m = new CreaturePteroWingTests.Skinned(sp);
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

        [TestCase(0.9f)] [TestCase(1.1f)]
        public void Walk_SailClearsTheGround(float k)
        {
            CreatureSpecies sp = Apex();
            float s = sp.scale * k, T = Mathf.Sqrt(s);
            var m = new CreaturePteroWingTests.Skinned(sp);
            var sim = CreaturePteroWingTests.Sim(sp, s);
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
            TestContext.WriteLine($"x{k}: lowest sail point {low / s:F3} m (scale 1)");
            Assert.Greater(low, 0.02f * s);
        }

        static int OpenEdges(Sculpt s, out int tris)
        {
            SculptPiece piece = SculptMesher.For(s);
            var edges = new Dictionary<long, int>();
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
            tris = piece.Tris.Length / 3;
            return open;
        }

        [Test] public void Head_SolidsClosed()
        {
            CasqueHead h = Part<CasqueHead>(Apex());
            foreach (Sculpt s in new[] { h.CasqueSkull(), h.CasqueJaw(), h.Casque() })
            {
                int open = OpenEdges(s, out int tris);
                Assert.AreEqual(0, open, $"layer {s.layer}: open edges");
                Assert.Greater(tris, 200, $"layer {s.layer}: a real solid");
            }
        }

        // The launch (the sail opening off the folded stance) under its own brain: no sail triangle turns inside out. The fan-side
        // rear rib rolls with the sail (WingSpec.fanRearRollsWithFan): the shipped halfway roll into the arm sector flips the
        // narrow zone beside it in every first launch (seeds 0-2: the Pterodactyl's PoseSweep finding).
        static List<string> LaunchDefects(CreatureSpecies sp, int seed)
        {
            KitPoseSweep.Result r = KitPoseSweep.Run(sp, sp.scale, seed, 12f, false);
            Assert.IsTrue(r.Seen.Contains("flight=launch"), "the run launches");
            return r.Defects;
        }

        [Test] public void Launch_SailNeverInverts()
        {
            List<string> d = LaunchDefects(Apex(), 1);
            Assert.AreEqual(0, d.Count, d.Count > 0 ? d[0] : "");
        }

        [Test] public void Red_FanRearRibHalfwayRoll_SailInvertsInTheLaunch()
        {
            List<string> d = LaunchDefects(ApexWith(p => { if (p is SailWing w) w.spec.fanRearRollsWithFan = false; }), 1);
            TestContext.WriteLine("RED: " + (d.Count > 0 ? d[0] : "clean"));
            Assert.Greater(d.Count, 0);
        }

        // ---------------------------------------------------------------- the open mouth

        static float RestGape(CreatureSpecies sp)
        {
            var sim = CreaturePteroWingTests.Sim(sp, sp.scale);
            BeakHeadRuntime h = Rt<BeakHeadRuntime>(sim);
            float least = float.MaxValue;
            for (int t = 0; t < 600; t++) { sim.Tick(kDt, sim.Joints[0]); if (t > 120) least = Mathf.Min(least, h.Gape); }
            return least;
        }

        [Test] public void Mouth_HeldOpen()
        {
            CreatureSpecies sp = Apex();
            float least = RestGape(sp), want = Part<CasqueHead>(sp).restGapeDeg;
            TestContext.WriteLine($"standing 8 s: the jaw never closes past {least:F1} deg (rest gape {want})");
            Assert.Greater(least, 0.9f * want);
        }

        [Test] public void Red_NoRestGape_MouthShuts()
        {
            float least = RestGape(ApexWith(p => { if (p is CasqueHead h) h.restGapeDeg = 0f; }));
            TestContext.WriteLine($"RED: rest gape 0, the jaw closes to {least:F2} deg");
            Assert.Less(least, 1f);
        }

        // ---------------------------------------------------------------- the planted claw (WingSpec.thumbPlant)

        [Test] public void Gait_ClawPlanted_InThePicture()
        {
            string p = KitChecks.DrawnFeetProblem(Apex(), out float drift, out float offset);
            TestContext.WriteLine($"drawn claw drift {drift * 100f:F2} %, offset {offset * 100f:F2} % of the arm");
            Assert.IsNull(p, p);
            Assert.Less(offset, 0.002f, "the claw's tip sits on the foot point");
        }

        [Test] public void Red_ClawNotPlanted_Skates()
        {
            KitChecks.DrawnFeetProblem(ApexWith(p => { if (p is SailWing w) w.spec.thumbPlant = false; }), out float drift, out float offset);
            TestContext.WriteLine($"RED: thumbPlant off, the claw is {offset * 100f:F2} % of the arm off its foot point");
            Assert.Greater(offset, 0.005f);
        }

        // ---------------------------------------------------------------- it lands (SkyHunt.handBack)

        internal static int Touchdowns(CreatureSpecies sp, float s, int seed, float froudeSeconds)
        {
            int n = 0; FlightMode last = FlightMode.Ground;
            KitPoseSweep.Run(sp, s, seed, froudeSeconds, false, (sim, label, bones) =>
            {
                if (sim.Mode == FlightMode.Touchdown && last != FlightMode.Touchdown) n++;
                last = sim.Mode;
            });
            return n;
        }

        [TestCase(1f, 1)] [TestCase(0.9f, 2)] [TestCase(1.1f, 3)]
        public void Lands_UnderItsOwnBrain(float k, int seed)
        {
            CreatureSpecies sp = Apex();
            int n = Touchdowns(sp, sp.scale * k, seed, 200f);
            TestContext.WriteLine($"x{k} seed {seed}: {n} touchdowns in 200 s (Froude)");
            Assert.GreaterOrEqual(n, 1);
        }

        // The hunt's hand-back itself: a bout that ends while the hunt holds the wings in a glide (Model.DemandCap 0) frees
        // them (SkyHunt.handBack); without it the cap stays and the next landing glides round its point at floor height
        // without a beat (what kept the Apex from ever landing). Returns the cap after the bout, or NaN if the bout never
        // held a glide.
        internal static float CapAfterBout(CreatureSpecies sp)
        {
            float s = sp.scale, T = Mathf.Sqrt(s);
            EnclosureArea area = CreatureSkyHuntTests.Aviary(sp, s);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, s, 1) { Bounds = area };
            SkyHunt mod = null;
            foreach (CreatureModule m in KitView.Modules(sp)) if (m is SkyHunt h) mod = h;
            var hunt = new SkyHuntBehaviour(mod, 1);
            Assert.IsTrue(sim.Flight.Launch(6f * s), "launch");
            float time = 0f;
            for (int t = 0; t < 600 || !sim.Flight.Airborne; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); time += kDt; }
            Assert.Greater(hunt.Utility(new BrainCtx(sim, area, time)), 0f, "the bout starts");
            hunt.Enter(new BrainCtx(sim, area, time));
            bool held = false;
            for (int t = 0; t < (int)(60f * T / kDt) && !held; t++)
            {
                var c = new BrainCtx(sim, area, time);
                hunt.Utility(c);
                sim.Tick(kDt, hunt.Tick(c, kDt).target);
                time += kDt;
                held = sim.Flight.Model.DemandCap == 0f && hunt.Current == SkyHuntBehaviour.Phase.Circle;
            }
            if (!held) return float.NaN;
            float u = hunt.Utility(new BrainCtx(sim, area, time + mod.boutSeconds * T + 1f));   // the bout is over
            Assert.Less(u, 0f, "the bout ended");
            return sim.Flight.Model.DemandCap;
        }

        [Test] public void Hunt_HandsTheWingsBack_WhenTheBoutEnds()
        {
            float cap = CapAfterBout(Apex());
            TestContext.WriteLine($"after the bout the wings' demand cap is {cap}");
            Assert.AreEqual(float.MaxValue, cap);
        }

        [Test] public void Red_HuntDoesNotHandBack_WingsStayHeld()
        {
            float cap = CapAfterBout(ApexWith(module: m => { if (m is SkyHunt h) h.handBack = false; }));
            TestContext.WriteLine($"RED: handBack off, after the bout the wings' demand cap is {cap}");
            Assert.AreEqual(0f, cap);
        }
    }
}
