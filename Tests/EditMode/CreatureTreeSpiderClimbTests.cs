using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The swinger's prey (design 2026-10-19-two-armed-swinger): a small spider that climbs the grove's trees -
    // ClimbSpec on arthropod legs, through the climbing API on real colliders (MeshColliders: Editor only, so this
    // class is in the harness's UnityOnlyTests; first run in Unity).
    public class CreatureTreeSpiderClimbTests
    {
        const float kDt = 1f / 60f;
        internal const string kPath = "Assets/TerrainGen/Config/Creatures/TreeSpider.asset";
        static readonly Func<Vector3, Vector3> Up = _ => Vector3.up;

        ClimbTestMeshes geo;
        [SetUp] public void SetUp() { geo = new ClimbTestMeshes(); }
        [TearDown] public void TearDown() { geo.Dispose(); }

        static CreatureSpecies Spider()
        {
            var sp = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kPath);
            Assert.IsNotNull(sp, kPath);
            Assert.IsTrue(sp.climb.canClimb, "the tree spider climbs");
            return sp;
        }

        static bool Finite(Vector3 v) => !(float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z));

        // From the ground 1.5 m out, up a grove-sized trunk (r 0.15 m) to a goal 1.5 m up its side: it gets there,
        // nothing goes NaN, no joint sinks into the bark and every planted foot stays on the surface.
        [Test] public void TreeSpider_ClimbsTrunk()
        {
            const float R = 0.15f, goalY = 1.5f;
            geo.Cylinder(new Vector3(0f, -1f, 0f), R, 6f);
            CreatureSpecies sp = Spider();
            var probe = new CompositeSurfaceProbe(new FlatGroundProbe(), new PhysicsSurfaceProbe(geo.Registry));
            var sim = new CreatureSim(sp, probe, Up, new Vector3(0f, 0f, -1.5f), Vector3.forward, sp.scale);
            Vector3 goal = new Vector3(0f, goalY, -R);
            bool arrived = false; float maxY = 0f, worstSink = 0f, worstFoot = 0f;
            for (int t = 0; t < 60 * 60; t++)
            {
                if (!arrived && (sim.Joints[0] - goal).magnitude < 0.15f) arrived = true;
                sim.Tick(kDt, arrived ? sim.Joints[0] : goal);
                foreach (Vector3 j in sim.Joints)
                {
                    Assert.IsTrue(Finite(j), "NaN joint");
                    if (j.y > 0.05f && j.y < 4.9f) worstSink = Mathf.Max(worstSink, R - new Vector2(j.x, j.z).magnitude);
                }
                for (int l = 0; l < sim.Legs.Length; l++)
                {
                    LegState ls = sim.Legs[l];
                    if (ls.stepping || (ls.foot - ls.stepTo).magnitude > 1e-3f) continue;
                    Vector3 f = ls.foot;
                    float d = Mathf.Min(Mathf.Abs(f.y), Mathf.Abs(new Vector2(f.x, f.z).magnitude - R));
                    worstFoot = Mathf.Max(worstFoot, d);
                }
                maxY = Mathf.Max(maxY, sim.Joints[0].y);
            }
            TestContext.Out.WriteLine($"arrived {arrived}, head max y {maxY:F2}, worst sink {worstSink * 100f:F1} cm, worst planted foot {worstFoot * 100f:F1} cm");
            Assert.IsTrue(arrived, "reached the goal on the trunk");
            Assert.Greater(maxY, 1.2f);
            Assert.LessOrEqual(worstSink, 0.02f * sp.scale, "a joint inside the bark");
            Assert.LessOrEqual(worstFoot, 0.02f * sp.scale + 0.01f, "planted feet off the bark");
        }

        // The lab's way: a ClimbingBrain with the trunk as a climbable obstacle starts a climb and gets up it.
        [Test] public void TreeSpider_ClimbingBrain_GoesUp()
        {
            const float R = 0.15f;
            geo.Cylinder(new Vector3(0f, -1f, 0f), R, 6f);
            CreatureSpecies sp = Spider();
            float maxScale = CreatureLab.MaxScale(sp);
            var area = EnclosureArea.For(Vector3.zero, Vector3.up, 4f, sp, maxScale);
            area.obstacleCenters = new[] { Vector3.zero }; area.obstacleRadii = new[] { R };
            area.obstacleClimbable = new[] { true };
            area.keepOut = EnclosureArea.KeepOutFor(sp, maxScale);
            area.maxHeight = 3f;
            var probe = new CompositeSurfaceProbe(new FlatGroundProbe(), new PhysicsSurfaceProbe(geo.Registry));
            var sim = new CreatureSim(sp, probe, Up, new Vector3(-1.2f, 0f, -1.2f), Vector3.forward, sp.scale) { Bounds = area };
            var brain = new ClimbingBrain(area, 5, 1f);
            Assert.IsTrue(brain.StartClimb(sim), "a goal on the trunk");
            float maxY = 0f;
            for (int t = 0; t < 60 * 60; t++)
            {
                sim.Tick(kDt, brain.Target(sim, kDt));
                foreach (Vector3 j in sim.Joints) Assert.IsTrue(Finite(j));
                maxY = Mathf.Max(maxY, sim.Joints[0].y);
            }
            TestContext.Out.WriteLine($"brain {brain.Mode}, climbs {brain.Climbs}, goal {brain.Goal}, head max y {maxY:F2}");
            Assert.Greater(maxY, 0.9f, "climbed the trunk");
        }
    }
}
