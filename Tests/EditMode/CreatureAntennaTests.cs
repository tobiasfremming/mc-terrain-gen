using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's Antenna part (T5.6) on the SkyDragon (whiskers and chin barbels): validation, exact strand lengths,
    // the strands trail a flying body by a bounded amount, settle without jitter at rest, and follow Froude size.
    public class CreatureAntennaTests
    {
        const float kDt = 1f / 60f;

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        [Test] public void Antenna_Validate_RejectsBadData()
        {
            var ok = new Antenna { id = "a", at = new PartSocket(0, 1, Vector3.zero), length = 0.3f, segments = 8, stiffnessHz = 1.5f };
            Assert.IsNull(ok.Validate(Ctx(10)));
            Assert.AreEqual(8, ok.BoneCount(Ctx(10)), "one bone per segment");
            var bad = (Antenna)ok.Clone(); bad.segments = 1;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "too few segments");
            bad = (Antenna)ok.Clone(); bad.length = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "no length");
            bad = (Antenna)ok.Clone(); bad.at.joint = 10;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "joint out of range");
            bad = (Antenna)ok.Clone(); bad.direction = Vector3.zero;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "no direction");
        }

        static float MaxLengthError(CreatureSim sim)
        {
            CreatureAntennae an = sim.Antennae;
            float worst = 0f;
            for (int a = 0; a < an.Count; a++)
            {
                AntennaSpec s = an.Spec(a);
                float seg = s.length / s.Segments * sim.SizeScale;
                for (int k = 1; k <= s.Segments; k++)
                {
                    Vector3 d = an.Points[an.FirstPoint(a) + k] - an.Points[an.FirstPoint(a) + k - 1];
                    Assert.IsFalse(float.IsNaN(d.x + d.y + d.z), "finite");
                    worst = Mathf.Max(worst, Mathf.Abs(d.magnitude - seg) / seg);
                }
            }
            return worst;
        }

        // Flying straight and through a barrel roll: lengths exact; the whiskers trail behind their rest curve by a
        // bounded amount (they stream back in the wind, never wrap round the head).
        [Test] public void Antenna_Flight_TrailsBounded_LengthsExact()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            Assert.Greater(sp.AntennaCount, 0, "the dragon has whiskers");
            float s = sp.scale, alt = 6f * s;
            CreatureSim sim = CreatureDragonTwistTests.AirborneDragon(sp, s, alt, out _);
            CreatureAntennae an = sim.Antennae;
            float worstLen = 0f, maxLag = 0f, sumLag = 0f, behind = 0f; int n = 0;
            AirTwist tw = sim.Flight.Twist;
            for (int t = 0; t < 600; t++)
            {
                if (t == 240) tw.Begin(TwistManoeuvre.BarrelRoll, sim.Flight.Model.HorizontalSpeed, sim.Flight.Model.Forward, Vector3.up);
                sim.Flight.SetGoals(alt, sp.flight.cruiseSpeed * sim.TimeScale, 0f);
                sim.Tick(kDt, sim.Joints[0]);
                worstLen = Mathf.Max(worstLen, MaxLengthError(sim));
                float lag = an.TipLag(0, sim.Joints, sim.Rotations) / (an.Spec(0).length * s);
                maxLag = Mathf.Max(maxLag, lag);
                if (t < 240)
                {
                    sumLag += lag; n++;
                    int tip = an.FirstPoint(0) + an.Spec(0).Segments;
                    behind += Vector3.Dot(an.Points[tip] - sim.Joints[0], sim.Rotations[0] * Vector3.forward);
                }
            }
            TestContext.WriteLine($"whisker tip lag: mean {sumLag / n:P1} of its length in straight flight, max {maxLag:P1}; length error {worstLen:P4}");
            Assert.Less(worstLen, 1e-4f, "segment lengths exact");
            Assert.Greater(sumLag / n, 0.02f, "the whisker streams back in the wind");
            Assert.Less(maxLag, 0.5f, "but stays a whisker (bounded: no whip)");
            Assert.Less(behind / n, 0f, "its tip trails behind the head");
        }

        // Standing still on the ground: the strands come to rest on their curves, no jitter.
        [Test] public void Antenna_AtRest_Settles_NoJitter()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            float s = sp.scale;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s);
            CreatureAntennae an = sim.Antennae;
            // a jolt first: walk, then stop
            for (int t = 0; t < 120; t++) sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * (2f * s));
            Vector3 hold = sim.Joints[0];
            // the strands' own motion: each tip's offset from its rest point over the last second (the standing body
            // still creeps a tidy-up step now and then; the strands follow it without ringing)
            float firstLag = 0f, lastLag = 0f, lag = 0f;
            for (int t = 0; t < 600; t++)
            {
                sim.Tick(kDt, hold);
                for (int a = 0; a < an.Count; a++)
                {
                    float l = an.TipLag(a, sim.Joints, sim.Rotations) / (an.Spec(a).length * s);
                    if (t < 60) firstLag = Mathf.Max(firstLag, l);
                    if (t >= 540) lastLag = Mathf.Max(lastLag, l);
                }
            }
            for (int a = 0; a < an.Count; a++) lag = Mathf.Max(lag, an.TipLag(a, sim.Joints, sim.Rotations) / (an.Spec(a).length * s));
            TestContext.WriteLine($"standing: tips at most {firstLag:P2} of their length off their rest curve in the first second, {lastLag:P2} in the tenth, {lag:P2} at the end");
            Assert.Less(lastLag, 0.1f, "no ringing at rest (the standing body still creeps a tidy-up step now and then)");
            Assert.LessOrEqual(lastLag, firstLag + 1e-4f, "the strands calm down, they do not ring up");
            Assert.Less(lag, 0.05f, "back on the rest curve");
        }

        // Froude: at 0.9x and 1.1x, flying at their own (Froude) speeds, the mean tip lag over its length agrees.
        [Test] public void Antenna_Size_Froude()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            var lag = new float[2];
            float[] f = { 0.9f, 1.1f };
            for (int c = 0; c < 2; c++)
            {
                float s = sp.scale * f[c], alt = 6f * s;
                CreatureSim sim = CreatureDragonTwistTests.AirborneDragon(sp, s, alt, out _);
                float sum = 0f; int n = Mathf.RoundToInt(6f * Mathf.Sqrt(s) / kDt);
                for (int t = 0; t < n; t++)
                {
                    sim.Flight.SetGoals(alt, sp.flight.cruiseSpeed * sim.TimeScale, 0f);
                    sim.Tick(kDt * Mathf.Sqrt(f[c]), sim.Joints[0]);
                    sum += sim.Antennae.TipLag(0, sim.Joints, sim.Rotations) / (sim.Antennae.Spec(0).length * s);
                }
                lag[c] = sum / n;
            }
            TestContext.WriteLine($"mean whisker lag / length: {lag[0]:P2} vs {lag[1]:P2}");
            Assert.AreEqual(lag[0], lag[1], 0.15f * lag[0], "the strands trail the same at every size");
        }
    }
}
