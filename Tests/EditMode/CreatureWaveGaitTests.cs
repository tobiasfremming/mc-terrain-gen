using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The wave gait (creature kit T5.8): Walk.pattern Wave lays the legs out in waveGroups groups by pair (left pair p
    // in group p % G, right pair half a wave later) and CreatureLegs.StartWave steps the groups strictly in turn, so
    // the lift-offs run down the body as a metachronal wave (head to tail: a centipede's retrograde wave). Proven on
    // the GiantCentipede and the Scolopendra: the lift-off order, the duty factor, the support (never two neighbouring
    // feet of one side in the air), and the size rule. RED: the same legs with the event-driven gait do not keep the order.
    public class CreatureWaveGaitTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Centipede(string name = "GiantCentipede") => CreatureKitAssets.Load(name).Compiled.Runtime;

        static CreatureSim Walker(CreatureSpecies sp, float s) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, 1);

        static bool Air(LegState st) => st.stepping && st.stepT >= 0f;

        static int Groups(CreatureSpecies sp)
        {
            var g = new HashSet<int>();
            foreach (LegSpec l in sp.legs) if (!l.feeler) g.Add(l.gaitGroup);
            return g.Count;
        }

        [Test] public void Wave_Compose_LaysGroupsByPair()
        {
            var parts = new List<CreaturePart>
            {
                new CreatureKit.SpineBody { id = "body", spacing = 0.01f, joints = new JointDef[24] },
                new Eyes { id = "eyes", at = new PartSocket(0, 0, Vector3.zero) },
            };
            for (int k = 0; k < 7; k++)
                parts.Add(new ArthropodLeg
                {
                    id = "leg" + k, mirror = true, at = new PartSocket(2 + 3 * k, 0, new Vector3(0.01f, 0f, 0f)),
                    segments = new[] { new SegmentDef { length = 0.01f }, new SegmentDef { length = 0.02f, flexWeight = 1f }, new SegmentDef { length = 0.02f, flexWeight = -1f } },
                });
            for (int i = 0; i < 24; i++) ((CreatureKit.SpineBody)parts[0]).joints[i] = new JointDef { halfWidth = 0.01f, halfHeight = 0.01f, clearance = 0.01f, bendLimitDeg = 10f };
            foreach (bool tailToHead in new[] { false, true })
            {
                CompiledSpecies c = CompiledSpecies.Compose("wave", parts, new CreatureModule[] { new Walk { pattern = GaitPattern.Wave, waveGroups = 5, waveTailToHead = tailToHead } });
                Assert.IsNull(c.Error, c.Error);
                for (int k = 0; k < 7; k++)
                {
                    int left = k % 5, right = (k + 2) % 5;
                    if (tailToHead) { left = (5 - left) % 5; right = (5 - right) % 5; }
                    Assert.AreEqual(left, c.Limbs[2 * k].gaitGroup, $"pair {k} left (tailToHead {tailToHead})");
                    Assert.AreEqual(right, c.Limbs[2 * k + 1].gaitGroup, $"pair {k} right (tailToHead {tailToHead})");
                }
                var rt = ScriptableObject.CreateInstance<CreatureSpecies>();
                c.WriteLegacyFields(rt);
                Assert.IsTrue(rt.waveGait, "a Wave pattern turns the wave scheduler on");
                Object.DestroyImmediate(rt);
            }
            CompiledSpecies d = CompiledSpecies.Compose("diag", parts, new CreatureModule[] { new Walk { pattern = GaitPattern.Diagonal } });
            var rd = ScriptableObject.CreateInstance<CreatureSpecies>();
            d.WriteLegacyFields(rd);
            Assert.IsFalse(rd.waveGait, "every other pattern keeps the event-driven gait");
            Object.DestroyImmediate(rd);
        }

        // Each new group to lift off over a straight walk (a group whose feet start a step while the last group's
        // have landed): the share that is the previous one + 1 in the order.
        static float InOrder(CreatureSpecies sp, float s, out int transitions)
        {
            int g = Groups(sp);
            var sim = Walker(sp, s);
            Vector3 far = new Vector3(0f, 0f, 1000f * s);
            int last = -1, ok = 0; transitions = 0;
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, far);
                int lifted = -1;
                for (int l = 0; l < sim.Legs.Length; l++)
                    if (sim.Legs[l].stepping && sp.legs[l].gaitGroup != last) lifted = sp.legs[l].gaitGroup;
                if (lifted < 0 || t < 60) continue;
                if (last >= 0) { transitions++; if (lifted == (last + 1) % g) ok++; }
                last = lifted;
            }
            return transitions > 0 ? ok / (float)transitions : 0f;
        }

        [TestCase("GiantCentipede")] [TestCase("Scolopendra")]
        public void Wave_LiftOffs_RunHeadToTail_InOrder(string species)
        {
            CreatureSpecies sp = Centipede(species);
            Assert.IsTrue(sp.waveGait, "the centipede walks with the wave gait");
            float f = InOrder(sp, sp.scale, out int n);
            TestContext.Out.WriteLine($"{n} group lift-offs, {f:P1} in wave order");
            Assert.Greater(n, 100, "the wave keeps stepping");
            Assert.AreEqual(1f, f, 1e-6f, "every group lifts after the one before it");
        }

        [Test] public void Red_EventDrivenGait_LosesTheOrder()
        {
            CreatureSpecies sp = Centipede();
            sp.waveGait = false;
            try
            {
                float f = InOrder(sp, sp.scale, out int n);
                TestContext.Out.WriteLine($"RED: event-driven gait: {n} group lift-offs, {f:P1} in wave order");
                Assert.Less(f, 0.9f, "the order test bites: the event-driven gait picks whichever group is eligible");
            }
            finally { sp.waveGait = true; }
        }

        // Each foot is in the air about 1/G of the time at a steady walk (duty factor 1 - 1/G), and every foot steps.
        [TestCase("GiantCentipede", 0.9f)] [TestCase("GiantCentipede", 1f)] [TestCase("GiantCentipede", 1.1f)]
        [TestCase("Scolopendra", 0.9f)] [TestCase("Scolopendra", 1f)] [TestCase("Scolopendra", 1.1f)]
        public void Wave_DutyFactor_Holds(string species, float k)
        {
            CreatureSpecies sp = Centipede(species);
            int g = Groups(sp), n = sp.legs.Length;
            float s = sp.scale * k;
            var sim = Walker(sp, s);
            Vector3 far = new Vector3(0f, 0f, 1000f * s);
            var air = new int[n]; int ticks = 0;
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt * Mathf.Sqrt(k), far);
                if (t < 120) continue;
                ticks++;
                for (int l = 0; l < n; l++) if (Air(sim.Legs[l])) air[l]++;
            }
            float lo = 1f, hi = 0f;
            for (int l = 0; l < n; l++) { float f = air[l] / (float)ticks; lo = Mathf.Min(lo, f); hi = Mathf.Max(hi, f); }
            TestContext.Out.WriteLine($"x{k:F1}: G {g}, air share per foot {lo:F3}..{hi:F3} (1/G = {1f / g:F3}), duty factor {1f - hi:F3}..{1f - lo:F3}");
            Assert.Greater(lo, 0.6f / g, "every foot takes its turn in the air");
            Assert.Less(hi, 1.2f / g, "no foot is in the air for more than its share: duty factor >= 1 - 1.2/G");
        }

        // The support: never two neighbouring feet of one side in the air at once, and at most one foot in G of each
        // side - walking straight and weaving.
        [TestCase("GiantCentipede")] [TestCase("Scolopendra")]
        public void Wave_SupportPolygon_NeverEmpty(string species)
        {
            CreatureSpecies sp = Centipede(species);
            int g = Groups(sp), n = sp.legs.Length;
            foreach (bool weave in new[] { false, true })
            {
                var sim = Walker(sp, sp.scale);
                int worstSide = 0;
                for (int t = 0; t < 900; t++)
                {
                    Vector3 fwd = sim.Rotations[0] * Vector3.forward;
                    Vector3 goal = weave ? sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / 50f), Vector3.up) * fwd * (2f * sim.SizeScale)
                                         : new Vector3(0f, 0f, 1000f * sp.scale);
                    sim.Tick(kDt, goal);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int up = 0, total = 0; bool prevAir = false;
                        for (int l = 0; l < n; l++)
                        {
                            if (sp.legs[l].side != side || sp.legs[l].feeler) continue;
                            bool a = Air(sim.Legs[l]);
                            total++;
                            if (a) up++;
                            Assert.IsFalse(a && prevAir, $"weave {weave} t={sim.Time:F2}: neighbouring feet of side {side} in the air (leg {l})");
                            prevAir = a;
                        }
                        worstSide = Mathf.Max(worstSide, up);
                        Assert.LessOrEqual(up, (total + g - 1) / g, $"weave {weave} t={sim.Time:F2}: {up} of {total} feet of side {side} in the air");
                    }
                }
                TestContext.Out.WriteLine($"weave {weave}: at most {worstSide} feet of a side in the air");
            }
        }
    }
}
