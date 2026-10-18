using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Phase B: FlightBrain cycles and aviary containment (design sections 4-5), on analytic ground with rock
    // and branch perches.
    public class WingedSerpentBrainTests
    {
        const float kDt = 1f / 60f;
        public const float AviaryRadius = 14f, AviaryHeight = 10f;
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        internal static EnclosureArea Aviary(CreatureSpecies sp, float scale = 1f, float radius = AviaryRadius, float height = AviaryHeight)
        {
            EnclosureArea a = EnclosureArea.For(Vector3.zero, Vector3.up, radius, sp, scale);
            a.maxHeight = height;
            return a;
        }

        // Two rocks and a branch inside the aviary, and their perch spots (tops).
        internal static WingedTestSurfaces Perches(out PerchSpot[] spots)
        {
            var s = new WingedTestSurfaces()
                .AddRock(new Vector3(5f, 0f, 2f), new Vector3(1.8f, 1.2f, 1.8f), 11)
                .AddRock(new Vector3(-4f, 0f, -5f), new Vector3(2.2f, 0.8f, 1.6f), 12)
                .AddBranch(-6f, 1f, 2.5f, 5f, 0.18f, 21);
            spots = new[]
            {
                new PerchSpot { point = new Vector3(5f, 1.2f, 2f), source = 11 },
                new PerchSpot { point = new Vector3(-4f, 0.8f, -5f), source = 12 },
                new PerchSpot { point = new Vector3(-3f, 2.5f + 0.18f, 5f), source = 21 },
            };
            return s;
        }

        WingedSerpentTestSpecies.Serpent Serpent()
        {
            var s = WingedSerpentTestSpecies.Create();
            trash.Add(s.species);
            return s;
        }

        sealed class Stats
        {
            public float maxPlanarGirdle, maxPlanarJoint, maxHeight, minClear = float.MaxValue, worstLen;
            public int launches, landings, perches;
        }

        static Stats Run(CreatureSim sim, FlightBrain brain, EnclosureArea area, float seconds)
        {
            var st = new Stats();
            int g = sim.Flight.Girdle;
            for (int t = 0; t < Mathf.RoundToInt(seconds / kDt); t++)
            {
                sim.Tick(kDt, brain.Target(sim, kDt));
                WingedSerpentModeTests.AssertFinite(sim, "tick " + t);
                st.maxPlanarGirdle = Mathf.Max(st.maxPlanarGirdle, area.PlanarDistance(sim.Joints[g]));
                foreach (var j in sim.Joints)
                {
                    st.maxPlanarJoint = Mathf.Max(st.maxPlanarJoint, area.PlanarDistance(j));
                    st.maxHeight = Mathf.Max(st.maxHeight, j.y);
                }
                st.worstLen = Mathf.Max(st.worstLen, WingedSerpentModeTests.MaxSegmentError(sim));
            }
            st.launches = sim.Flight.Launches; st.landings = sim.Flight.Landings; st.perches = brain.Perches;
            return st;
        }

        [Test] public void Containment_TurnRadiusFits()
        {
            var s = Serpent();
            Assert.IsTrue(FlightBrain.FitsAviary(s.species, Aviary(s.species), 1f), "the serpent at scale 1 turns inside a 14 m aviary");
            Assert.IsFalse(FlightBrain.FitsAviary(s.species, Aviary(s.species, 4f), 4f), "at scale 4 it does not");
            Assert.IsFalse(FlightBrain.FitsAviary(s.species, Aviary(s.species, 1f, 5f), 1f), "nor in a 5 m enclosure");
        }

        // 120 s of the brain: launches and lands again and again, on perches too.
        [Test] public void FlyingBrain_Cycles_120s()
        {
            var s = Serpent();
            var surfaces = Perches(out PerchSpot[] spots);
            EnclosureArea area = Aviary(s.species);
            var sim = new CreatureSim(s.species, surfaces, _ => Vector3.up, new Vector3(0f, 0f, -2f), Vector3.forward) { Bounds = area };
            var brain = new FlightBrain(area, 5, spots);
            Stats st = Run(sim, brain, area, 120f);
            TestContext.WriteLine($"launches {st.launches}, landings {st.landings} (on perches {st.perches}), max girdle radius {st.maxPlanarGirdle:F2} / safe {area.safeRadius:F2}, max joint radius {st.maxPlanarJoint:F2} / {area.radius}, top {st.maxHeight:F2} / {area.maxHeight}, worst segment error {st.worstLen:P3}");
            Assert.GreaterOrEqual(st.launches, 2, "flies repeatedly");
            Assert.GreaterOrEqual(st.landings, 2, "lands repeatedly");
            Assert.GreaterOrEqual(st.perches, 1, "uses a perch");
            Assert.Less(st.worstLen, 0.01f, "segment lengths");
        }

        // A tree crown given only as a region: the brain finds the branch top itself (probe down, up-facing,
        // room along it) and perches there.
        [Test] public void FlyingBrain_FindsBranchInRegion()
        {
            var s = Serpent();
            var surfaces = new WingedTestSurfaces().AddBranch(-6f, 1f, 2.5f, 5f, 0.18f, 21);
            EnclosureArea area = Aviary(s.species);
            var region = new[] { new PerchSpot { point = new Vector3(-2.5f, 3.2f, 5f), radius = 2f } };
            var sim = new CreatureSim(s.species, surfaces, _ => Vector3.up, new Vector3(0f, 0f, -2f), Vector3.forward) { Bounds = area };
            var brain = new FlightBrain(area, 4, region);
            bool onBranch = false;
            for (int t = 0; t < Mathf.RoundToInt(150f / kDt) && !onBranch; t++)
            {
                sim.Tick(kDt, brain.Target(sim, kDt));
                onBranch = sim.Mode == FlightMode.Ground && sim.Flight.Perched && brain.LastPerch.source == 21;
            }
            TestContext.WriteLine($"perched on the branch: {onBranch}, at {brain.LastPerch.point}, flights {brain.Flights}");
            Assert.IsTrue(onBranch, "found and landed on the branch");
            Assert.Greater(sim.Joints[sim.Flight.Girdle].y, 2.3f, "standing on the branch");
        }

        // 120 s of the brain in the aviary, several seeds: the girdle stays in the safe cylinder, the whole
        // body in the aviary, below its roof.
        [Test] public void Containment_Aviary_120s()
        {
            var s = Serpent();
            var surfaces = Perches(out PerchSpot[] spots);
            EnclosureArea area = Aviary(s.species);
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var sim = new CreatureSim(s.species, surfaces, _ => Vector3.up, new Vector3(0f, 0f, -2f), Vector3.forward) { Bounds = area };
                Stats st = Run(sim, new FlightBrain(area, seed, spots), area, 120f);
                TestContext.WriteLine($"seed {seed}: launches {st.launches}, landings {st.landings}, girdle radius {st.maxPlanarGirdle:F2} (safe {area.safeRadius:F2}), body radius {st.maxPlanarJoint:F2} (aviary {area.radius}), top {st.maxHeight:F2} (roof {area.maxHeight})");
                Assert.LessOrEqual(st.maxPlanarGirdle, area.safeRadius - 0.05f, "girdle in the safe cylinder without touching its clamp (the turn-back keeps it off)");
                Assert.LessOrEqual(st.maxPlanarJoint, area.radius, "body inside the aviary");
                Assert.LessOrEqual(st.maxHeight, area.maxHeight + 0.5f, "under the roof");
            }
        }
    }
}
