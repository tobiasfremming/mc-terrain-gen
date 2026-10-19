using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The winged serpent and trees (user request 2026-10-18: "the wyvern should be able to avoid flying into
    // trees, and it should be able to land on trees (climbing or perched) if it wants to"): analytic trees
    // (WingedTreeSurfaces), every skinned vertex of the posed mesh checked against the bark.
    public class WingedSerpentTreeTests
    {
        const float kDt = 1f / 60f;
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        WingedSerpentTestSpecies.Serpent Serpent()
        {
            var s = WingedSerpentTestSpecies.Create();
            trash.Add(s.species);
            return s;
        }

        // The skinned mesh of a sim, for contact checks: every vertex in world space after WriteBoneWorld.
        internal sealed class Skinned
        {
            readonly Vector3[] v; readonly Vector4[] w; readonly int[] part;
            readonly Matrix4x4[] bind, world, skin;
            public readonly Vector3[] World;
            public int Count => v.Length;
            public int Part(int i) => part[i];

            public Skinned(CreatureSpecies sp, List<Object> trash)
            {
                Mesh m = WingedMeshBuilder.Build(sp, sp.wings);
                trash.Add(m);
                v = m.vertices;
                var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
                m.GetUVs(1, uv1); m.GetUVs(2, uv2);
                w = uv2.ToArray();
                part = new int[v.Length];
                for (int i = 0; i < v.Length; i++) part[i] = Mathf.RoundToInt(uv1[i].x);
                bind = WingedMeshBuilder.BindPoses(sp, sp.wings);
                world = new Matrix4x4[bind.Length]; skin = new Matrix4x4[bind.Length];
                World = new Vector3[v.Length];
            }

            public void Pose(CreatureSim sim)
            {
                sim.WriteBoneWorld(world, 0);
                for (int i = 0; i < skin.Length; i++) skin[i] = world[i] * bind[i];
                for (int i = 0; i < v.Length; i++)
                {
                    Vector4 s = w[i];
                    World[i] = s.z * skin[(int)s.x].MultiplyPoint3x4(v[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(v[i]);
                }
            }
        }

        internal struct Contact { public float depth; public int vertex, part, tick; public Vector3 at; }

        // The deepest skinned vertex inside a tree (depth > 0 = inside), skipping parts in `skipParts`.
        // feet: vertices this near a foot point (feetR) are the foot's contact, not a penetration.
        internal static Contact Deepest(Skinned m, WingedTreeSurfaces trees, int tick, int skipMask = 0, CreatureSim feetOf = null, float feetR = 0f)
        {
            var c = new Contact { depth = float.MinValue, vertex = -1 };
            for (int i = 0; i < m.Count; i++)
            {
                if ((skipMask & (1 << m.Part(i))) != 0) continue;
                if (feetOf != null)
                {
                    bool near = false;
                    foreach (var l in feetOf.Legs) if ((m.World[i] - l.foot).sqrMagnitude < feetR * feetR) { near = true; break; }
                    if (near) continue;
                }
                float d = -trees.TreeDistance(m.World[i], out _, out _);
                if (d > c.depth) { c.depth = d; c.vertex = i; c.part = m.Part(i); c.tick = tick; c.at = m.World[i]; }
            }
            return c;
        }

        // Three trees round the middle of the 14 m aviary (as the lab: 0.4 x radius out), their crowns as perch
        // regions.
        internal static WingedTreeSurfaces TreeAviary(out PerchSpot[] spots, int count = 3)
        {
            var s = new WingedTreeSurfaces();
            var list = new List<PerchSpot>();
            for (int k = 0; k < count; k++)
            {
                float a = (90f + 360f * k / count) * Mathf.Deg2Rad;
                var t = s.AddTree(new Vector3(5.6f * Mathf.Cos(a), 0f, 5.6f * Mathf.Sin(a)), 5.5f, 0.32f, 41 + k, 4, 0.11f, 40f * k);
                list.Add(s.CrownSpot(t));
            }
            spots = list.ToArray();
            return s;
        }

        // The aviary with the trees' trunks as the enclosure's obstacles (as CreatureLab.DressAviary: 1.5 x the
        // trunk radius, walked round on the ground).
        internal static EnclosureArea TreeArea(CreatureSpecies sp, float scale, WingedTreeSurfaces trees)
        {
            EnclosureArea a = WingedSerpentBrainTests.Aviary(sp, scale);
            int n = trees.Trees.Count;
            a.obstacleCenters = new Vector3[n]; a.obstacleRadii = new float[n];
            for (int i = 0; i < n; i++) { a.obstacleCenters[i] = trees.Trees[i].basePoint; a.obstacleRadii[i] = 1.5f * trees.Trees[i].trunkRadius; }
            a.keepOut = EnclosureArea.KeepOutFor(sp, scale);
            return a;
        }

        // Straight at a lone trunk (head-on and a little off), the brain's goal straight on through it: the
        // avoidance turns early and nothing touches the bark.
        [TestCase(1f, 0f)] [TestCase(1f, 0.6f)] [TestCase(1f, -1.2f)] [TestCase(0.9f, 0.3f)] [TestCase(1.1f, -0.3f)]
        public void Avoid_LoneTrunk(float scale, float offset)
        {
            var s = Serpent();
            s.flight.avoidLookahead = 2.4f;
            var trees = new WingedTreeSurfaces();
            trees.AddTree(new Vector3(offset, 0f, 14f), 9f, 0.32f, 41, 0);
            var sim = new CreatureSim(s.species, trees, _ => Vector3.up, Vector3.zero, Vector3.forward, scale);
            var mesh = new Skinned(s.species, trash);
            float alt = 3f * scale;
            Assert.IsTrue(sim.Flight.Launch(alt));
            Contact worst = new Contact { depth = float.MinValue };
            float maxRoll = 0f, prevBank = 0f;
            for (int t = 0; t < Mathf.RoundToInt(10f / kDt); t++)
            {
                if (sim.Flight.Airborne) sim.Flight.SetGoals(alt, s.flight.cruiseSpeed * Mathf.Sqrt(scale), 0f);
                sim.Tick(kDt, sim.Joints[0]);
                float bank = sim.Flight.Model.Bank;
                if (t > 90) maxRoll = Mathf.Max(maxRoll, Mathf.Abs(bank - prevBank) / kDt);
                prevBank = bank;
                if (t % 3 != 0) continue;
                mesh.Pose(sim);
                Contact c = Deepest(mesh, trees, t);
                if (c.depth > worst.depth) worst = c;
            }
            TestContext.WriteLine($"x{scale} offset {offset}: deepest {worst.depth * 100f:F1} cm (part {worst.part}, t={worst.tick * kDt:F1} s), blocked evaluations {sim.Flight.Avoidance.Blocked}/{sim.Flight.Avoidance.Evaluations}, casts {sim.Flight.Avoidance.Casts}, max roll rate {maxRoll * Mathf.Rad2Deg:F0} deg/s, girdle at the end {sim.Joints[sim.Flight.Girdle]}");
            Assert.Less(worst.depth, 0f, "nothing inside the trunk");
            Assert.Greater(sim.Flight.Avoidance.Blocked, 0, "the avoidance saw it");
        }

        // Fly and land on a perch at `point` along `axis` (the landing autopilot flies the approach).
        internal static void FlyAndPerch(CreatureSim sim, Vector3 point, int source, Vector3 axis, float maxSeconds, out int ticks)
        {
            Assert.IsTrue(sim.Flight.Launch(point.y + 3f * sim.SizeScale));
            ticks = 0;
            for (; ticks < 180; ticks++) sim.Tick(kDt, sim.Joints[0]);
            sim.Flight.Land(point, source, axis);
            int max = Mathf.RoundToInt(maxSeconds / kDt);
            for (; ticks < max && sim.Mode != FlightMode.Ground; ticks++) sim.Tick(kDt, sim.Joints[0]);
        }

        internal sealed class PerchStats
        {
            public float footWorst, footMeanTop, deepest, girdleAbove;
            public int deepPart, footLeg, footTick;
            public override string ToString() => $"planted feet off the bark <= {footWorst * 100f:F1} cm (leg {footLeg}, t={footTick / 60f:F1} s), deepest vertex in the bark {deepest * 100f:F1} cm (part {deepPart}), girdle {girdleAbove * 100f:F1} cm above its clearance";
        }

        // Standing on a perch: each foot's distance to the bark, the deepest non-foot vertex inside it.
        internal static PerchStats Perched(CreatureSim sim, Skinned mesh, WingedTreeSurfaces trees, float seconds)
        {
            var st = new PerchStats { deepest = float.MinValue };
            for (int t = 0; t < Mathf.RoundToInt(seconds / kDt); t++)
            {
                sim.Tick(kDt, sim.Joints[0]);
                if (t % 6 != 0) continue;
                for (int l = 0; l < sim.Legs.Length; l++)
                {
                    if (sim.Legs[l].stepping) continue; // a swinging foot is lifted
                    float d = Mathf.Abs(trees.TreeDistance(sim.Legs[l].foot, out _, out _));
                    if (d > st.footWorst) { st.footWorst = d; st.footLeg = l; st.footTick = t; }
                }
                mesh.Pose(sim);
                Contact c = Deepest(mesh, trees, t, 1 << CreatureMeshBuilder.PartFoot, sim, 0.04f * sim.SizeScale);
                if (c.depth > st.deepest) { st.deepest = c.depth; st.deepPart = c.part; }
            }
            int g = sim.Flight.Girdle;
            st.girdleAbove = trees.TreeDistance(sim.Joints[g], out _, out _) - sim.Species.joints[g].clearance * sim.SizeScale;
            return st;
        }

        // A lone horizontal limb: lands along it, stands with every foot on the bark and nothing in it.
        [TestCase(1f)] [TestCase(0.9f)] [TestCase(1.1f)]
        public void Perch_Limb_FeetOnBark(float scale)
        {
            var s = Serpent();
            s.flight.avoidLookahead = 2.4f; s.flight.perchGrip = true;
            var trees = new WingedTreeSurfaces().AddLimb(new Vector3(-3f, 2.4f, 8f), new Vector3(3f, 2.4f, 8f), 0.11f, 31);
            var sim = new CreatureSim(s.species, trees, _ => Vector3.up, Vector3.zero, Vector3.right, scale);
            var mesh = new Skinned(s.species, trash);
            FlyAndPerch(sim, new Vector3(0f, 2.4f + 0.11f, 8f), 31, Vector3.right, 45f, out int ticks);
            Assert.AreEqual(FlightMode.Ground, sim.Mode, "landed");
            Assert.IsTrue(sim.Flight.Perched, "perched");
            PerchStats st = Perched(sim, mesh, trees, 3f);
            TestContext.WriteLine($"x{scale}: landed after {ticks * kDt:F1} s; {st}");
            Assert.Less(st.footWorst, 0.02f * scale, "feet on the bark");
            Assert.Less(st.deepest, 0.005f * scale, "nothing in the bark");
        }

        // One tree in the aviary, its crown the only perch spot: the brain finds a branch top in it (axis,
        // thickness, room, a clear final), lands along it and grips it; nothing goes into the bark on the way.
        [TestCase(1f, 1)] [TestCase(1f, 2)] [TestCase(0.9f, 3)] [TestCase(1.1f, 4)]
        public void Perch_TreeCrown_Brain(float scale, int seed)
        {
            var s = Serpent();
            s.flight.avoidLookahead = 2.4f; s.flight.perchGrip = true;
            var trees = new WingedTreeSurfaces();
            var tree = trees.AddTree(new Vector3(0f, 0f, 5.6f), 5.5f, 0.32f, 41, 4, 0.11f, 20f * seed);
            EnclosureArea area = TreeArea(s.species, scale, trees);
            var sim = new CreatureSim(s.species, trees, _ => Vector3.up, new Vector3(0f, 0f, -3f), Vector3.forward, scale) { Bounds = area };
            var brain = new FlightBrain(area, seed, new[] { trees.CrownSpot(tree) });
            var mesh = new Skinned(s.species, trash);
            Contact worst = new Contact { depth = float.MinValue };
            bool perched = false; int t = 0;
            for (; t < Mathf.RoundToInt(180f / kDt) && !perched; t++)
            {
                sim.Tick(kDt, brain.Target(sim, kDt));
                perched = sim.Mode == FlightMode.Ground && sim.Flight.Perched;
                if (t % 6 != 0 || sim.Mode == FlightMode.Ground) continue;
                mesh.Pose(sim);
                Contact c = Deepest(mesh, trees, t, 1 << CreatureMeshBuilder.PartFoot, sim.Mode == FlightMode.Touchdown ? sim : null, 0.04f * scale);
                if (c.depth > worst.depth) worst = c;
            }
            string stats = $"branch tops {brain.BranchTops}, accepted {brain.BranchesAccepted}, rejected axis/thin/room/approach {string.Join("/", brain.Rejected)}, aborted {brain.LandingsAborted}, flights {brain.Flights}";
            Assert.IsTrue(perched, "perched on the tree within 3 minutes (" + stats + ")");
            PerchStats st = Perched(sim, mesh, trees, 3f);
            TestContext.WriteLine($"x{scale} seed {seed}: perched after {t * kDt:F0} s at {brain.LastPerch.point}; {stats}; deepest on the way {worst.depth * 100f:F1} cm (part {worst.part}, t={worst.tick * kDt:F1} s); {st}");
            Assert.Less(worst.depth, 0.005f * scale, "nothing in the bark on the way");
            Assert.Less(st.footWorst, 0.02f * scale, "planted feet on the bark");
            Assert.Less(st.deepest, 0.005f * scale, "nothing in the bark while perched");
        }

        // Baseline / report: the brain flying in the tree aviary; how deep anything goes into a tree.
        [TestCase(1f, 0f)]
        [TestCase(1f, 2.4f)] [TestCase(0.9f, 2.4f)] [TestCase(1.1f, 2.4f)]
        public void Trees_Report(float scale, float lookahead)
        {
            var s = Serpent();
            s.flight.avoidLookahead = lookahead; s.flight.perchGrip = lookahead > 0f;
            var trees = TreeAviary(out PerchSpot[] spots);
            EnclosureArea area = TreeArea(s.species, scale, trees);
            var sim = new CreatureSim(s.species, trees, _ => Vector3.up, new Vector3(0f, 0f, -2f), Vector3.forward, scale) { Bounds = area };
            var brain = new FlightBrain(area, 3, spots);
            var mesh = new Skinned(s.species, trash);
            int hits = 0, groundHits = 0, landingHits = 0; float air = 0f; Contact worst = new Contact { depth = float.MinValue };
            int lastEval = 0;
            var trace = new System.Collections.Generic.Queue<string>();
            for (int t = 0; t < Mathf.RoundToInt(120f / kDt); t++)
            {
                sim.Tick(kDt, brain.Target(sim, kDt));
                var av = sim.Flight.Avoidance;
                if (av.Evaluations != lastEval)
                {
                    lastEval = av.Evaluations;
                    trace.Enqueue($"    t={t * kDt:F2} {brain.Current} asked {av.LastAsked:F2} clear {av.LastAskedClear:F1} chosen {av.LastChosen:F2} frac {av.ClearFraction:F2} bank {sim.Flight.Model.Bank * Mathf.Rad2Deg:F0} v {sim.Flight.Model.HorizontalSpeed:F1} p {sim.Flight.Model.Position} hit {av.LastHit}");
                    if (trace.Count > 25) trace.Dequeue();
                }
                if (sim.Flight.Airborne) air += kDt;
                if (t % 6 != 0) continue;
                mesh.Pose(sim);
                Contact c = Deepest(mesh, trees, t);
                if (c.depth <= 0.005f * scale) continue;
                if (sim.Mode == FlightMode.Ground) { groundHits++; continue; }
                if (sim.Flight.Landing || sim.Mode == FlightMode.Touchdown) { landingHits++; continue; }
                hits++;
                if (c.depth > worst.depth) worst = c;
                if (hits == 1) foreach (string line in trace) TestContext.WriteLine(line);
                if (hits <= 3) TestContext.WriteLine($"  contact t={t * kDt:F1} depth {c.depth * 100f:F0} cm part {c.part} mode {sim.Mode} landing {sim.Flight.Landing} final {sim.Flight.OnFinal} brain {brain.Current} avoid {sim.Flight.Avoidance.Active} clear {sim.Flight.Avoidance.ClearFraction:F2} speed {sim.Flight.Model.HorizontalSpeed:F1} bank {sim.Flight.Model.Bank * Mathf.Rad2Deg:F0} girdle {sim.Joints[sim.Flight.Girdle]} at {c.at}");
            }
            TestContext.WriteLine($"x{scale} lookahead {lookahead}: evals {sim.Flight.Avoidance.Evaluations}, blocked {sim.Flight.Avoidance.Blocked}, casts {sim.Flight.Avoidance.Casts}; airborne {air:F0} s, landings {sim.Flight.Landings}, perches {brain.Perches} (branch tops {brain.BranchTops}, accepted {brain.BranchesAccepted}, aborted landings {brain.LandingsAborted}, rejected axis/thin/room/approach {string.Join("/", brain.Rejected)}), sampled ticks inside a tree: in free flight {hits}, landing {landingHits}, on the ground {groundHits}, deepest in the air {worst.depth * 100f:F1} cm (part {worst.part}, t={worst.tick * kDt:F1} s at {worst.at})");
        }
    }
}
