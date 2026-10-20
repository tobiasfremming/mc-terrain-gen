using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // QuadLaunch and SkyHunt (creature kit, the Pterodactyl) in its lab aviary, with the brain the lab gives it (BrainFactory:
    // a ModularBrain over SkyHunt, FlightExcursion and Wander). Proven at 0.9x and 1.1x: the quad launch crouches then
    // vaults into the air; the hunt holds its soaring height (gliding most of the time) inside the aviary; a stoop at a
    // practice mark and at prey on the ground reaches the target with the head and pulls out without any joint or the
    // head touching the ground, then climbs away; between bouts it lands and launches again; no GC. RED: a stoop with
    // no pull-out (pullUpHeight tiny) hits the ground.
    public class CreatureSkyHuntTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Pterodactyl() => CreaturePteroWingTests.Pterodactyl();

        static CreatureSpecies HuntWith(System.Action<SkyHunt> edit)
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("Pterodactyl");
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = "PterodactylHuntEdit"; sp.scale = a.scale; sp.kitVersion = a.kitVersion;
            foreach (CreaturePart p in a.parts) sp.parts.Add(p.Clone());
            foreach (CreatureModule m in a.modules) { CreatureModule c = m.Clone(); if (c is SkyHunt h) edit(h); sp.modules.Add(c); }
            return sp.Compiled.Runtime;
        }

        internal static EnclosureArea Aviary(CreatureSpecies sp, float s)
        {
            EnclosureArea area = EnclosureArea.For(Vector3.zero, Vector3.up, CreatureLab.EnclosureRadius(sp, s), sp, s);
            area.maxHeight = AviarySizing.Height(sp, s, CreatureLab.kAviaryHeight);
            return area;
        }

        sealed class Run
        {
            public CreatureSim Sim; public ModularBrain Brain; public SkyHuntBehaviour Hunt; public EnclosureArea Area;
            public float Lowest = float.MaxValue, LowestHead = float.MaxValue, LowestTip = float.MaxValue, Out;
            public Run(CreatureSpecies sp, float s, int seed)
            {
                Area = Aviary(sp, s);
                Sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Area.center, Vector3.forward, s, seed) { Bounds = Area };
                Brain = BrainFactory.Create(sp, Area, seed) as ModularBrain;
                foreach (IBehaviour b in Brain.Behaviours) if (b is SkyHuntBehaviour h) Hunt = h;
            }
            public void Tick()
            {
                Sim.Tick(kDt, Brain.Target(Sim, kDt));
                if (Sim.Mode == FlightMode.Air || Sim.Mode == FlightMode.Flare)
                {
                    for (int i = 0; i < Sim.Joints.Length; i++) Lowest = Mathf.Min(Lowest, Sim.Joints[i].y);
                    CreatureWings cw = Sim.Flight.Wings;   // and the wing tips: nothing of it touches the ground
                    for (int w = 0; w < cw.Count; w++) LowestTip = Mathf.Min(LowestTip, cw.FingerTip[cw.FingerIndex(w, 0)].y);
                    foreach (IPartRuntime r in Sim.PartRuntimes) if (r is LongNeckRuntime n) LowestHead = Mathf.Min(LowestHead, n.HeadPosition.y);
                }
                for (int i = 0; i < Sim.Joints.Length; i++) Out = Mathf.Max(Out, Area.PlanarDistance(Sim.Joints[i]) - Area.radius);
            }
        }

        [Test] public void Brain_IsModular_WithTheHunt()
        {
            CreatureSpecies sp = Pterodactyl();
            var r = new Run(sp, sp.scale, 1);
            Assert.IsNotNull(r.Brain, "a ModularBrain");
            Assert.IsNotNull(r.Hunt, "with the SkyHunt behaviour");
            Assert.AreEqual(CreatureKitAssets.Habitat.Aviary, CreatureKitAssets.HabitatOf(sp));
            float big = CreatureLab.MaxScale(sp);
            Assert.GreaterOrEqual(AviarySizing.Height(sp, big, CreatureLab.kAviaryHeight), 1.25f * 9f * big * 0.999f, "the aviary is tall enough to soar and stoop");
            Assert.IsTrue(FlightBrain.FitsAviary(sp, KitChecks_LabArea(sp), big));
        }

        static EnclosureArea KitChecks_LabArea(CreatureSpecies sp) => Aviary(sp, CreatureLab.MaxScale(sp));

        // The quad launch: the body sinks on its limbs first, then vaults off the ground and flies.
        [TestCase(0.9f)] [TestCase(1.1f)]
        public void QuadLaunch_CrouchesThenVaults(float k)
        {
            CreatureSpecies sp = Pterodactyl();
            float s = sp.scale * k, T = Mathf.Sqrt(s);
            var sim = CreaturePteroWingTests.Sim(sp, s);
            for (int t = 0; t < 120; t++) sim.Tick(kDt, sim.Joints[0]);
            int g = sim.Flight.Girdle;
            float stand = sim.Joints[g].y, low = stand;
            Assert.IsTrue(sim.Flight.Launch(6f * s));
            Assert.IsTrue(sim.Flight.Crouching, "it crouches first");
            int t2 = 0;
            for (; t2 < 600 && sim.Mode == FlightMode.Ground; t2++) { sim.Tick(kDt, sim.Joints[0]); low = Mathf.Min(low, sim.Joints[g].y); }
            float crouchT = t2 * kDt / T;
            float vault = 0f;
            for (int t = 0; t < 60; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); vault = Mathf.Max(vault, sim.Flight.Model.VerticalSpeed / T); }
            for (int t = 0; t < 900; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); }
            TestContext.WriteLine($"x{k}: crouch {crouchT:F2} s (scale 1), sank {(stand - low) / s:F3} m, vault {vault:F2} m/s, altitude {sim.Flight.Model.Altitude / s:F2} m (scale 1) after 16 s");
            Assert.Greater((stand - low) / s, 0.6f * 0.12f, "sank into the crouch");
            Assert.AreEqual(0.4f, crouchT, 0.05f, "crouched for crouchSeconds (Froude)");
            Assert.Greater(vault, 3f, "vaulted up hard");
            Assert.AreEqual(FlightMode.Air, sim.Mode);
            Assert.Greater(sim.Flight.Model.Altitude / s, 5f, "reached its altitude");
        }

        // Hunting: the soar holds its height band, mostly gliding, inside the aviary.
        [TestCase(0.9f)] [TestCase(1.1f)]
        public void Soar_HoldsAltitude_GlidesMost(float k)
        {
            CreatureSpecies sp = Pterodactyl();
            float s = sp.scale * k, T = Mathf.Sqrt(s);
            var r = new Run(sp, s, 2);
            float soarTime = 0f, glide = 0f, lo = float.MaxValue, hi = 0f;
            bool reached = false;
            for (int t = 0; t < (int)(360f * T / kDt); t++)
            {
                r.Tick();
                bool circling = r.Brain.Behaviours[r.Brain.Current] == r.Hunt && r.Hunt.Current == SkyHuntBehaviour.Phase.Circle;
                float alt = r.Sim.Flight.Model.Altitude / s;
                if (!circling) { reached = false; continue; }
                if (alt > 9f - 1.2f) reached = true;   // in the band (after a launch or a stoop it first climbs to it)
                if (!reached) continue;
                soarTime += kDt; if (r.Sim.Flight.Model.Gliding) glide += kDt;
                lo = Mathf.Min(lo, alt); hi = Mathf.Max(hi, alt);
            }
            TestContext.WriteLine($"x{k}: circling {soarTime:F0} s, gliding {100f * glide / Mathf.Max(1e-3f, soarTime):F0} %, height {lo:F2}..{hi:F2} m (scale 1; soar 9 +- 1.2), stoops {r.Hunt.Stoops}, outside the area by {r.Out:F3} m");
            Assert.Greater(soarTime, 20f, "it soared");
            Assert.Greater(glide / soarTime, 0.4f, "long glides");
            Assert.Greater(lo, 9f - 3.5f, "held its height (a turn's bank can sink it below the band before the beats catch it)"); Assert.Less(hi, 9f + 2.5f, "and stayed near it (the glides trade speed for height: a phugoid over the band)");
            Assert.LessOrEqual(r.Out, 0f, "inside the aviary");
        }

        // A stoop at the practice mark (no prey): the head reaches the mark, nothing touches the ground, it climbs away.
        [TestCase(0.9f, 3)] [TestCase(1.1f, 4)]
        public void Stoop_Practice_ReachesAndPullsOut(float k, int seed)
        {
            HuntPrey.Source = null;
            CreatureSpecies sp = Pterodactyl();
            string p = StoopProblem(sp, sp.scale * k, seed, null);
            Assert.IsNull(p, p);
        }

        // A stoop at prey on the ground: a monitor walking in the aviary.
        [Test] public void Stoop_OnPrey_ReachesItsHead()
        {
            CreatureSpecies sp = Pterodactyl();
            CreatureSpecies mon = CreatureKitAssets.Load("DesertMonitor");
            var prey = new CreatureSim(mon, new FlatGroundProbe(), _ => Vector3.up, new Vector3(4f, 0f, 3f), Vector3.right, mon.scale, 9);
            try { string p = StoopProblem(sp, sp.scale, 5, prey); Assert.IsNull(p, p); }
            finally { HuntPrey.Source = null; }
        }

        // Five stoops in a row: at least three strike (the beak tip within 0.25 neck + 0.2 m of the target at scale 1, about a
        // beak length), and every
        // one pulls out by itself (the girdle stays clear of the flight's safety floor) with nothing touching the ground.
        internal static string StoopProblem(CreatureSpecies sp, float s, int seed, CreatureSim prey, int want = 5)
        {
            float T = Mathf.Sqrt(s);
            var r = new Run(sp, s, seed);
            var list = new List<CreatureSim> { r.Sim };
            if (prey != null) list.Add(prey);
            HuntPrey.Source = list;
            float clear = sp.joints[0].halfHeight * s, floorH = sp.joints[r.Sim.Flight.Girdle].clearance * s;
            float worstLow = float.MaxValue, worstMiss = 0f;
            int seen = 0;
            string why = null;
            var misses = new System.Text.StringBuilder();
            for (int t = 0; t < (int)(1500f * T / kDt) && seen < want; t++)
            {
                r.Tick();
                if (prey != null) prey.Tick(kDt, r.Area.ClampToSafe(prey.Joints[0] + prey.Rotations[0] * Vector3.forward * prey.SizeScale));
                if (r.Hunt.Current == SkyHuntBehaviour.Phase.Stoop && r.Hunt.HasPrey != (prey != null)) { why = "the stoop picked the wrong target"; break; }
                // a stoop has ended (back to circling with a new count)
                if (r.Hunt.Current == SkyHuntBehaviour.Phase.Circle && r.Hunt.Stoops > seen && r.Hunt.LastPull >= 0 && r.Hunt.LastPull != 3)
                {
                    seen = r.Hunt.Stoops;
                    worstLow = Mathf.Min(worstLow, r.Hunt.LastLowest);
                    worstMiss = Mathf.Max(worstMiss, r.Hunt.LastMiss);
                    misses.Append($" {r.Hunt.LastMiss / s:F2}({r.Hunt.LastPull})");
                }
            }
            HuntPrey.Source = null;
            float reach = 0f;
            foreach (IPartRuntime q in r.Sim.PartRuntimes) if (q is LongNeckRuntime n) reach = n.Part.length * s;
            TestContext.WriteLine($"x{s / sp.scale:F2}{(prey != null ? " prey" : " practice")}: {seen} stoops ({r.Hunt.Aborts} broken off), strikes {r.Hunt.Strikes}, misses (m at scale 1, pull reason){misses}; lowest girdle {worstLow / s:F2} m, lowest joint {r.Lowest / s:F3} m, head {r.LowestHead / s:F3} m, wing tip {r.LowestTip / s:F3} m");
            if (why != null) return why;
            if (seen < want) return $"only {seen} stoops";
            if (r.Hunt.Aborts > want) return $"{r.Hunt.Aborts} stoops broken off for {seen} flown";
            if (r.Hunt.Strikes * 5 < want * 3) return $"only {r.Hunt.Strikes} of {seen} stoops struck the target";
            if (r.Lowest < clear) return $"a joint came down to {r.Lowest / s:F3} m (scale 1) over the ground: hit it";
            if (r.LowestHead < 0.02f * s) return $"the head touched the ground ({r.LowestHead / s:F3} m)";
            if (r.LowestTip < 0.02f * s) return $"a wing tip touched the ground ({r.LowestTip / s:F3} m)";
            if (worstLow < floorH + 0.1f * s) return $"a pull-out bottomed on the flight's floor ({worstLow / s:F3} m, scale 1): it would have hit the ground";
            if (r.Out > 0f) return "left the aviary";
            return null;
        }

        static float mon(CreatureSim prey) => prey.SizeScale;

        [Test] public void Red_NoPullOut_HitsTheGround()
        {
            HuntPrey.Source = null;
            CreatureSpecies sp = HuntWith(h => h.pullUpHeight = 0.25f);
            string p = StoopProblem(sp, sp.scale, 3, null);
            TestContext.WriteLine("RED: " + p);
            Assert.IsNotNull(p);
        }

        // Between bouts the flight behaviour lands it and it launches (quad launch) again.
        [Test] public void Bouts_LandAndLaunchAgain()
        {
            HuntPrey.Source = null;
            CreatureSpecies sp = Pterodactyl();
            float s = sp.scale, T = Mathf.Sqrt(s);
            var r = new Run(sp, s, 6);
            for (int t = 0; t < (int)(900f * T / kDt) && !(r.Sim.Flight.Landings >= 1 && r.Sim.Flight.Launches >= 2 && r.Hunt.Stoops >= 1); t++) r.Tick();
            TestContext.WriteLine($"launches {r.Sim.Flight.Launches}, landings {r.Sim.Flight.Landings}, stoops {r.Hunt.Stoops}, switches {r.Brain.Switches}");
            Assert.GreaterOrEqual(r.Hunt.Stoops, 1, "it hunted");
            Assert.GreaterOrEqual(r.Sim.Flight.Landings, 1, "it landed between bouts");
            Assert.GreaterOrEqual(r.Sim.Flight.Launches, 2, "and launched again");
        }

        [Test] public void Gc_HuntingBrain_NoAllocation()
        {
            GcAllocs.AssertCounterWorks();
            HuntPrey.Source = null;
            CreatureSpecies sp = Pterodactyl();
            var r = new Run(sp, sp.scale, 2);
            var bones = new Matrix4x4[sp.BoneCount];
            for (int t = 0; t < 2400; t++) { r.Tick(); r.Sim.WriteBoneWorld(bones, 0); }
            int allocs = GcAllocs.Count(() => { for (int t = 0; t < 1200; t++) { r.Tick(); r.Sim.WriteBoneWorld(bones, 0); } });
            Assert.AreEqual(0, allocs);
        }
    }
}
