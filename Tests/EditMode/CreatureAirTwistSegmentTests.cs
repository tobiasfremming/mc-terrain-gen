using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The twisting air chain (AirTwist) promises exact segment lengths. Its bend cone broke that when the head's path
    // folded back on itself along the aviary wall (a nearly antiparallel direction: the perpendicular was rounding
    // noise), and the error compounded down the tail: Unity's SkyDragon Wander (seed 1, x3.39, t=244.4) flung joint
    // 55 out of the aviary for one tick. Before the fix 13 of these 12 seeds x 2 sizes broke a segment (offline, up
    // to 430 %), seed 1 x3.39 at the very tick Unity failed.
    public class CreatureAirTwistSegmentTests
    {
        [Test] public void Flight_Twist_SegmentLengthsExact()
        {
            var sp = CreatureKitAssets.Load("SkyDragon");
            Assert.IsNotNull(sp);
            EnclosureArea area = KitChecks.LabArea(sp);
            float big = CreatureLab.MaxScale(sp), small = sp.scale * (1f - Mathf.Abs(sp.look.sizeJitter));
            const float dt = 1f / 30f;
            var report = new System.Collections.Generic.List<string>();
            int airborne = 0;
            for (int seed = 1; seed <= 12; seed++)
                foreach (float s in new[] { big, small })
                {
                    var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, s, seed) { Bounds = area };
                    var brain = new FlightBrain(area, seed);
                    float seg = sp.spacing * s, worst = 0f, at = 0f;
                    int joint = 0;
                    for (int t = 0; t < (int)(300f / dt); t++)
                    {
                        sim.Tick(dt, brain.Target(sim, dt));
                        if (sim.Flight == null || !sim.Flight.Airborne) continue;
                        airborne++;
                        for (int i = 1; i < sim.Joints.Length; i++)
                        {
                            float e = Mathf.Abs((sim.Joints[i] - sim.Joints[i - 1]).magnitude / seg - 1f);
                            if (e > worst) { worst = e; at = sim.Time; joint = i; }
                        }
                    }
                    if (worst > 1e-3f) report.Add($"seed {seed} x{s:F2}: segment {joint} off by {worst * 100f:F1} % at t={at:F2}");
                }
            Assert.Greater(airborne, 24 * 3000, "the dragon hardly flew: the check does not bite");
            Assert.IsEmpty(report, string.Join("\n", report));
        }
    }
}
