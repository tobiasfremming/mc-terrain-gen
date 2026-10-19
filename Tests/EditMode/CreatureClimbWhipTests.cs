using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // User bug 2026-10-18 ("the lizard got messed up"): in the lab the desert monitor climbing on and off its
    // Baobab deformed, whipped its tail round (the whole body spun) and its planted feet slid. Root cause: joints on
    // the trunk carried an up nearly along their own segments (a gravity / trail up on a wall joint at the trunk
    // foot); the pitch limiter lifted such a joint along that up (shortening the segment), the length restore kept
    // the rise and stretched the arbitrary run (shoving the joint sideways, reversing the segment in its plane), and
    // the next chain step's bend cone swung everything behind it back - up to 1.3 m in one tick.
    //
    // The lab's cycle on analytic stand-ins (OctopusTestSurfaces.AnalyticSurfaces: an exact capsule trunk) at the
    // lab's size: walk to a goal on the trunk, rest, come down to a spot on the ground, rest, repeat; on level
    // ground and on a slope with the trunk's foot buried in it (the lab tree stands on ~16 deg sand). Per tick:
    // no joint moves much further than the head did (no whip), planted feet stay put, no joint inside the bark or
    // under the ground. Runs offline (harness) and in the Editor alike.
    public class CreatureClimbWhipTests
    {
        const float kDt = 1f / 60f;
        const float kLabScale = 4.27f;     // the lab monitor's size scale (2026-10-18)
        const float kTrunkR = 1.6f;        // the lab Baobab's trunk at plantScale 2
        const int kTicks = 7200, kSeeds = 6;

        // Terrain: a plane through the origin rising `slope` (tan) along +x.
        sealed class SlopeGround : IGroundProbe
        {
            public readonly float slope;
            readonly Vector3 n;
            public SlopeGround(float slope) { this.slope = slope; n = new Vector3(-slope, 1f, 0f).normalized; }
            public float Height(Vector3 p) => slope * p.x;
            public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
            {
                point = new Vector3(near.x, Height(near), near.z);
                normal = n;
                return true;
            }
        }

        public sealed class Result
        {
            public float worstExcess, worstSlide, worstInside, worstUnder;
            public int climbTicks, goalsReached, whipTicks;
            public override string ToString() =>
                $"worst move beyond the head {worstExcess * 100f:F1} cm ({whipTicks} ticks > 5 cm), planted slide {worstSlide * 1000f:F2} mm, " +
                $"inside bark {worstInside * 100f:F1} cm, under ground {worstUnder * 100f:F1} cm, climbing {climbTicks} ticks, goals {goalsReached}";
        }

        static CreatureSpecies Monitor()
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>("Assets/TerrainGen/Config/Creatures/DesertMonitor.asset");
            Assert.IsNotNull(sp);
            Assert.IsTrue(sp.climb.canClimb);
            return sp;
        }

        // One seed of the climb cycle on the given terrain.
        public static Result Cycle(CreatureSpecies sp, float slope, int seed, int ticks = kTicks, System.Action<int, CreatureSim, Vector3[]> onTick = null)
        {
            var ground = new SlopeGround(slope);
            var surf = new AnalyticSurfaces().AddCapsule(new Vector3(0f, -3f, 0f), new Vector3(0f, 14f, 0f), kTrunkR);
            var comp = new CompositeSurfaceProbe(ground, surf);
            var rng = new System.Random(seed);
            Vector3 start = new Vector3(0f, 0f, -9f); start.y = ground.Height(start);
            var sim = new CreatureSim(sp, comp, _ => Vector3.up, start, Vector3.forward, kLabScale);
            int n = sim.Joints.Length, L = sim.Legs.Length;
            var prev = (Vector3[])sim.Joints.Clone();
            var prevFoot = new Vector3[L]; var prevStep = new bool[L];
            for (int l = 0; l < L; l++) { prevFoot[l] = sim.Legs[l].foot; prevStep[l] = sim.Legs[l].stepping; }
            var r = new Result();
            Vector3 goal = Vector3.zero; int phase = 0, phaseT = 0;
            for (int t = 0; t < ticks; t++)
            {
                // 0: to a goal on the bark, 1: rest, 2: to a spot on the ground, 3: rest
                if (phase == 0 && phaseT == 0)
                {
                    float a = (float)rng.NextDouble() * 2f * Mathf.PI, h = 2f + 3f * (float)rng.NextDouble();
                    goal = new Vector3(Mathf.Cos(a) * kTrunkR, h, Mathf.Sin(a) * kTrunkR);
                }
                if (phase == 2 && phaseT == 0)
                {
                    float a = (float)rng.NextDouble() * 2f * Mathf.PI, d = 5f + 4f * (float)rng.NextDouble();
                    goal = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d); goal.y = ground.Height(goal);
                }
                sim.Tick(kDt, phase == 1 || phase == 3 ? sim.Joints[0] : goal);
                phaseT++;
                if ((phase == 0 || phase == 2) && ((sim.Joints[0] - goal).magnitude < 0.8f || phaseT > 1800))
                {
                    if (phaseT <= 1800) r.goalsReached++;
                    phase++; phaseT = 0;
                }
                else if ((phase == 1 || phase == 3) && phaseT > 90) { phase = (phase + 1) % 4; phaseT = 0; }

                if (sim.Climbing) r.climbTicks++;
                float head = (sim.Joints[0] - prev[0]).magnitude, worst = 0f;
                for (int i = 0; i < n; i++)
                {
                    Vector3 j = sim.Joints[i];
                    Assert.IsFalse(float.IsNaN(j.x + j.y + j.z), "joint " + i + " t=" + t);
                    worst = Mathf.Max(worst, (j - prev[i]).magnitude);
                    r.worstInside = Mathf.Max(r.worstInside, -surf.Distance(j, out _, out _));
                    r.worstUnder = Mathf.Max(r.worstUnder, ground.Height(j) - j.y);
                }
                float excess = worst - head;
                if (excess > 0.05f) r.whipTicks++;
                r.worstExcess = Mathf.Max(r.worstExcess, excess);
                for (int l = 0; l < L; l++)
                {
                    LegState s = sim.Legs[l];
                    if (!s.stepping && !prevStep[l]) r.worstSlide = Mathf.Max(r.worstSlide, (s.foot - prevFoot[l]).magnitude);
                    prevFoot[l] = s.foot; prevStep[l] = s.stepping;
                }
                onTick?.Invoke(t, sim, prev);   // diagnostics: the tick, the sim, last tick's joints
                System.Array.Copy(sim.Joints, prev, n);
            }
            return r;
        }

        static void Check(float slope)
        {
            var sp = Monitor();
            float s = kLabScale;
            for (int seed = 0; seed < kSeeds; seed++)
            {
                Result r = Cycle(sp, slope, seed);
                TestContext.Out.WriteLine($"slope {slope:F2} seed {seed}: {r}");
                string why = $"slope {slope:F2} seed {seed}: {r}";
                Assert.Greater(r.climbTicks, kTicks / 3, "climbs | " + why);
                Assert.Greater(r.goalsReached, 2, "gets round the cycle | " + why);
                // a body follows its head: nothing moves more than ~3 cm x scale further than the head in a tick
                Assert.LessOrEqual(r.worstExcess, 0.03f * s, "no whip | " + why);
                Assert.LessOrEqual(r.worstSlide, 1e-4f, "planted feet stay put | " + why);
                Assert.LessOrEqual(r.worstInside, 0.02f * s, "no joint inside the bark | " + why);
                Assert.LessOrEqual(r.worstUnder, 0.02f * s, "no joint under the ground | " + why);
            }
        }

        [Test] public void LabCycle_LevelGround_NoWhip() => Check(0f);
        [Test] public void LabCycle_SlopedBuriedFoot_NoWhip() => Check(Mathf.Tan(16f * Mathf.Deg2Rad));
    }
}
