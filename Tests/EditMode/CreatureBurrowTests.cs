using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's Burrow part, the digger behaviours and the shared digging services (2026-10-20, the Delver):
    //   the trip cycle (dig in, dive head first, travel hidden, burst out jaws first, climb out) at 0.9x and 1.1x: every
    //   phase in order, fully out of sight under the ground while Under, the head above its walking height at the burst,
    //   the jaw agape, sim joints and every carve inside the enclosure, no plate or blade clipping at any frame;
    //   the tunnel network: a second trip from near the first's entry to near its exit reuses it (enters at its hole,
    //   carves a fraction of the first trip's volume) and the network stays inside the enclosure;
    //   the carve queue: refuses spheres outside the area, never drains more than its budget a frame, stays bounded;
    //   a group of three with their brains: each spends most of its time on the surface (forages, rests, follows) and
    //   some underground, never all three under at once (maxHiddenShare 0.67), and the trips reuse the tunnels;
    //   the postures (rest curled, scratch) never clip the plates; zero allocation through a whole trip.
    public class CreatureBurrowTests
    {
        const float kDt = 1f / 60f;
        readonly List<Object> trash = new List<Object>();

        [SetUp] public void SetUp() { CreatureDigging.Clear(); TunnelNetwork.ClearAll(); CreatureGroup.Clear(); CreatureDigging.OnRequest = null; }

        [TearDown]
        public void TearDown()
        {
            CreatureDigging.OnRequest = null; CreatureDigging.Clear(); TunnelNetwork.ClearAll(); CreatureGroup.Clear();
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static CreatureSpecies Delver() => CreatureKitAssets.Load("Delver").Compiled.Runtime;
        static CreatureSpecies Asset() => CreatureKitAssets.Load("Delver");

        static BurrowRuntime Rt(CreatureSim s) { foreach (IPartRuntime r in s.PartRuntimes) if (r is BurrowRuntime b) return b; return null; }

        static CreatureSim Sim(CreatureSpecies sp, EnclosureArea area, Vector3 at, float size, int seed)
            => new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, at, Vector3.forward, size, seed) { Bounds = area };

        [Test] public void Burrow_Validate_LastAndAlone()
        {
            var b = new Burrow { id = "b" };
            var other = new Eyes { id = "eyes" };
            Assert.IsNull(b.Validate(new SpeciesCtx(new CreaturePart[] { other, b }, new CreatureModule[0], 20, false)));
            Assert.IsNotNull(b.Validate(new SpeciesCtx(new CreaturePart[] { b, other }, new CreatureModule[0], 20, false)), "not last");
            Assert.IsNotNull(b.Validate(new SpeciesCtx(new CreaturePart[] { new Tuck { id = "t" }, b }, new CreatureModule[0], 20, false)), "two postures");
            var bad = (Burrow)b.Clone(); bad.depth = 0f;
            Assert.IsNotNull(bad.Validate(new SpeciesCtx(new CreaturePart[] { other, bad }, new CreatureModule[0], 20, false)));
            Assert.IsNotNull(new Tunnel().Validate(new SpeciesCtx(new CreaturePart[] { other }, new CreatureModule[] { new Walk() }, 20, false)), "Tunnel without Burrow");
        }

        // ------------------------------------------------------------ the trip

        struct TripLog
        {
            public List<BurrowRuntime.Phase> phases;
            public int underFrames, frames, carves;
            public float maxDisplay, headLift, lowestTop, carveVolume;
            public string problem;
        }

        // Ticks one trip (BeginTrip(goal)) to its end; checks every frame. size: the individual's sizeScale.
        static TripLog RunTrip(CreatureSpecies asset, CreatureSim sim, Vector3 goal, EnclosureArea area, bool meshChecks, int maxFrames = 6000, bool clips = true)
        {
            CreatureSpecies sp = asset.Compiled.Runtime;
            BurrowRuntime rt = Rt(sim);
            var log = new TripLog { phases = new List<BurrowRuntime.Phase>(), lowestTop = float.MaxValue };
            float carveVol = 0f; int carves = 0; string carveProblem = null;
            CreatureDigging.OnRequest = (c, r) =>
            {
                carves++; carveVol += r * r * r;
                if (!CreatureDigging.Inside(area, c, r) && carveProblem == null) carveProblem = $"carve at {c} r {r:F2} outside the enclosure";
            };
            sim.Tick(kDt, sim.Joints[0]);
            Assert.IsTrue(rt.BeginTrip(goal), "trip starts");
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var world = new Matrix4x4[sp.BoneCount];
            Mesh mesh = meshChecks ? CreatureKitAssets.BuildMesh(asset) : null;
            Vector3[] v = mesh != null ? mesh.vertices : null;
            var uv2 = new List<Vector4>(); if (mesh != null) mesh.GetUVs(2, uv2);
            if (mesh != null) Object.DestroyImmediate(mesh);
            float walkHead = sim.Joints[0].y;
            int clod0 = 0, clod1 = 0;
            for (int i = 0; i < asset.Compiled.Parts.Count; i++)
                if (asset.Compiled.Parts[i] is Burrow bp) { clod0 = sp.Layout.Start(i); clod1 = clod0 + bp.debris; }
            for (int f = 0; f < maxFrames && rt.TripActive; f++)
            {
                sim.Tick(kDt, rt.Target);
                log.frames++;
                if (rt.State != BurrowRuntime.Phase.Idle && (log.phases.Count == 0 || log.phases[log.phases.Count - 1] != rt.State)) log.phases.Add(rt.State);
                if (rt.Concealed) log.underFrames++;
                log.maxDisplay = Mathf.Max(log.maxDisplay, sim.Display);
                for (int i = 0; i < sim.Joints.Length; i++)
                    if (area.PlanarDistance(sim.Joints[i]) > area.radius) { log.problem = $"f{f}: joint {i} outside the enclosure"; return log; }
                if (f % 5 != 0) continue;
                sim.WriteBoneWorld(world, 0);
                if (rt.State == BurrowRuntime.Phase.Lunge || rt.State == BurrowRuntime.Phase.Rise)
                    log.headLift = Mathf.Max(log.headLift, world[0].MultiplyPoint3x4(Vector3.zero).y - walkHead);
                if (!meshChecks) continue;
                string clip = clips ? ShellClip.Problem(asset, world, 1e-4f, out _) ?? BladeClip.Problem(asset, world, 1e-4f) : null;
                if (clip != null) { log.problem = $"f{f} {rt.State}: {clip}"; return log; }
                if (rt.Concealed)
                {
                    // every vertex of the drawn body under the ground (y = 0)
                    float top = float.MinValue;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector4 s = uv2[i];
                        if ((int)s.x >= clod0 && (int)s.x < clod1) continue;   // flung clods lie on the surface (on purpose)
                        Vector3 p = s.z * (world[(int)s.x] * bind[(int)s.x]).MultiplyPoint3x4(v[i]) + s.w * (world[(int)s.y] * bind[(int)s.y]).MultiplyPoint3x4(v[i]);
                        top = Mathf.Max(top, p.y);
                    }
                    log.lowestTop = Mathf.Min(log.lowestTop, -top);
                    if (top > 0f) { log.problem = $"f{f}: under, but a vertex is {top * 1000f:F0} mm above the ground"; return log; }
                }
            }
            log.carves = carves; log.carveVolume = carveVol;
            if (rt.TripActive && log.problem == null) log.problem = $"the trip did not end in {maxFrames} frames (phase {rt.State})";
            if (carveProblem != null) log.problem = carveProblem;
            CreatureDigging.OnRequest = null;
            return log;
        }

        [TestCase(0.9f)] [TestCase(1.1f)]
        public void Burrow_Trip_DigsIn_TravelsHidden_BurstsOut_InsideTheEnclosure(float rel)
        {
            CreatureSpecies asset = Asset(), sp = asset.Compiled.Runtime;
            float s = asset.scale * rel;
            EnclosureArea area = KitChecks.LabArea(sp);
            var sim = Sim(sp, area, area.center - Vector3.forward * (0.4f * area.safeRadius), s, 5);
            Vector3 goal = area.center + Vector3.forward * (0.5f * area.safeRadius) + Vector3.right * (0.2f * area.safeRadius);
            TripLog log = RunTrip(asset, sim, goal, area, true);
            Assert.IsNull(log.problem);
            Assert.AreEqual("DigIn Dive Under Lunge Rise", string.Join(" ", log.phases), "phases");
            Assert.Greater(log.underFrames, 60, "travels hidden");
            Assert.Greater(log.maxDisplay, 0.99f, "the jaw opens fully at the burst");
            Assert.Greater(log.headLift, 0.05f * s, "the head bursts above its walking height");
            Assert.Greater(log.carves, 5, "a real tunnel was carved");
            TunnelNetwork net = Rt(sim).Network;
            Assert.AreEqual(2, net.Holes, "an entrance and an exit");
            Assert.AreEqual(1, net.Tunnels, "one tunnel");
            TestContext.Out.WriteLine($"x{rel}: {log.frames} frames, under {log.underFrames}, carves {log.carves}, deepest top {log.lowestTop * 1000f:F0} mm under, head lift {log.headLift:F2} m");
        }

        // RED: a shallow dive leaves the shell above the ground while it is "under".
        [Test] public void Burrow_Red_ShallowDiveIsSeen()
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("Delver");
            Burrow b = null; float d = 0f;
            foreach (CreaturePart p in a.parts) if (p is Burrow q) { b = q; d = q.depth; q.depth = 0.2f; }
            try
            {
                a.InvalidateCompiled();
                CreatureSpecies sp = a.Compiled.Runtime;
                EnclosureArea area = KitChecks.LabArea(sp);
                var sim = Sim(sp, area, area.center - Vector3.forward * (0.4f * area.safeRadius), a.scale, 5);
                TripLog log = RunTrip(a, sim, area.center + Vector3.forward * (0.5f * area.safeRadius), area, true, clips: false);
                Assert.IsNotNull(log.problem);
                StringAssert.Contains("above the ground", log.problem);
            }
            finally { b.depth = d; a.InvalidateCompiled(); }
        }

        [Test] public void Burrow_Network_SecondTripReusesTheTunnel()
        {
            CreatureSpecies asset = Asset(), sp = asset.Compiled.Runtime;
            EnclosureArea area = KitChecks.LabArea(sp);
            Vector3 a = area.center - Vector3.forward * (0.45f * area.safeRadius), b = area.center + Vector3.forward * (0.5f * area.safeRadius);
            var first = Sim(sp, area, a, asset.scale, 1);
            TripLog one = RunTrip(asset, first, b, area, false);
            Assert.IsNull(one.problem);
            TunnelNetwork net = Rt(first).Network;
            int holes = net.Holes;
            // another individual near the first entrance, going near the first exit
            var second = Sim(sp, area, a - Vector3.right * (0.3f * sp.BodyLength * asset.scale), asset.scale, 2);
            TripLog two = RunTrip(asset, second, b + Vector3.right * (0.2f * sp.BodyLength * asset.scale), area, false);
            Assert.IsNull(two.problem);
            BurrowRuntime rt2 = Rt(second);
            Assert.AreEqual(1, rt2.TripsReusing, "the second trip rides the network");
            Assert.IsFalse(rt2.EntryWasNew, "enters at the existing hole");
            Assert.Less(two.carveVolume, 0.25f * one.carveVolume, $"carved volume: second {two.carveVolume:F3} vs first {one.carveVolume:F3}");
            Assert.LessOrEqual(net.Holes, holes + 1, "at most one new hole");
            for (int i = 0; i < net.Holes; i++) Assert.LessOrEqual(area.PlanarDistance(net.Hole(i)), area.radius, $"hole {i} inside");
            for (int t = 0; t < net.Tunnels; t++)
                for (int k = 0; k < net.TunnelPoints(t); k++) Assert.LessOrEqual(area.PlanarDistance(net.TunnelPoint(t, k)), area.radius, $"tunnel {t} point {k} inside");
        }

        // The clods: earth flies at the burst (well above the ground), is thrown back while it digs in, and every clod has
        // crumbled away (parked inside the body) a few seconds after the trip.
        [Test] public void Burrow_Debris_FliesAtTheBurst_ThenSettles()
        {
            CreatureSpecies asset = Asset(), sp = asset.Compiled.Runtime;
            EnclosureArea area = KitChecks.LabArea(sp);
            var sim = Sim(sp, area, area.center - Vector3.forward * (0.4f * area.safeRadius), asset.scale, 5);
            BurrowRuntime rt = Rt(sim);
            Assert.Greater(rt.Clods, 0, "the Delver flings clods");
            sim.Tick(kDt, sim.Joints[0]);
            rt.BeginTrip(area.center + Vector3.forward * (0.5f * area.safeRadius));
            int digging = 0; float burstTop = 0f;
            for (int f = 0; f < 9000 && rt.TripActive; f++)
            {
                sim.Tick(kDt, rt.Target);
                if (rt.State == BurrowRuntime.Phase.DigIn) digging = Mathf.Max(digging, rt.ClodsFlying);
                if (rt.State == BurrowRuntime.Phase.Lunge) for (int i = 0; i < rt.Clods; i++) burstTop = Mathf.Max(burstTop, rt.ClodHeight(i));
            }
            Assert.Greater(digging, 2, "earth thrown while it digs in");
            Assert.Greater(burstTop, 0.3f * asset.scale * sp.joints[0].clearance, "clods fly up at the burst");
            for (int f = 0; f < 300; f++) sim.Tick(kDt, sim.Joints[0]);
            Assert.AreEqual(0, rt.ClodsFlying, "every clod has crumbled away");
        }

        // ------------------------------------------------------------ the carve queue

        [Test] public void Digging_Queue_InsideOnly_BudgetPerFrame_Bounded()
        {
            EnclosureArea area = KitChecks.LabArea(Delver());
            Assert.IsTrue(CreatureDigging.Request(area, area.center, 0.5f));
            Assert.IsFalse(CreatureDigging.Request(area, area.center + Vector3.right * (area.radius - 0.2f), 0.5f), "across the rim");
            Assert.IsFalse(CreatureDigging.Request(area, area.center + Vector3.right * (area.radius + 3f), 0.5f), "outside");
            for (int i = 0; i < 400; i++) CreatureDigging.Request(area, area.center + Vector3.right * 0.01f * i, 0.4f);
            Assert.AreEqual(CreatureDigging.Capacity, CreatureDigging.Pending, "bounded");
            Assert.Greater(CreatureDigging.Dropped, 0, "oldest dropped when full");
            var sink = new Recorder();
            int frames = 0;
            while (CreatureDigging.Pending > 0) { int n = CreatureDigging.Drain(sink, 2); Assert.LessOrEqual(n, 2, "budget"); frames++; }
            Assert.AreEqual(CreatureDigging.Capacity, sink.count);
            Assert.AreEqual((CreatureDigging.Capacity + 1) / 2, frames);
        }

        sealed class Recorder : ICarveSink { public int count; public void Carve(Vector3 c, float r, bool physical) => count++; }

        // ------------------------------------------------------------ a group with its brains

        [Test] public void Burrow_Group_MostlySurface_NeverAllUnder_ReusesTunnels()
        {
            CreatureSpecies asset = Asset(), sp = asset.Compiled.Runtime;
            EnclosureArea area = KitChecks.LabArea(sp);
            const int count = 3;
            var sims = new CreatureSim[count]; var brains = new ICreatureBrain[count];
            for (int i = 0; i < count; i++)
            {
                float ang = 2f * Mathf.PI * i / count;
                Vector3 at = area.center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (0.5f * area.safeRadius);
                sims[i] = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, at, -new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)), asset.scale * (0.92f + 0.08f * i), 100 + i) { Bounds = area };
                brains[i] = BrainFactory.Create(sp, area, 100 + i);
                Assert.IsTrue(brains[i] is ModularBrain, "a ModularBrain");
            }
            const float dt = 1f / 30f;
            int frames = (int)(12f * 60f / dt);
            var under = new int[count]; var scratch = new int[count]; var rest = new int[count];
            int twoUnder = 0, longestTwo = 0, run = 0;
            for (int f = 0; f < frames; f++)
            {
                int hidden = 0;
                for (int i = 0; i < count; i++)
                {
                    sims[i].Tick(dt, brains[i].Target(sims[i], dt));
                    BurrowRuntime rt = Rt(sims[i]);
                    if (rt.Concealed) { under[i]++; hidden++; }
                    if (rt.Activity == DigActivity.Scratch) scratch[i]++;
                    if (rt.Activity == DigActivity.Rest) rest[i]++;
                    for (int j = 0; j < sims[i].Joints.Length; j++)
                        Assert.LessOrEqual(area.PlanarDistance(sims[i].Joints[j]), area.radius, $"individual {i} joint {j} outside at f{f}");
                }
                if (hidden >= 2) { twoUnder++; run++; longestTwo = Mathf.Max(longestTwo, run); } else run = 0;
                Assert.Less(hidden, count, $"all {count} under at once at f{f}");
            }
            int trips = 0, reusing = 0;
            for (int i = 0; i < count; i++)
            {
                BurrowRuntime rt = Rt(sims[i]);
                trips += rt.Trips; reusing += rt.TripsReusing;
                float share = (float)under[i] / frames;
                TestContext.Out.WriteLine($"individual {i}: underground {share:P0}, trips {rt.Trips} ({rt.TripsReusing} reusing), scratching {(float)scratch[i] / frames:P0}, resting {(float)rest[i] / frames:P0}");
                Assert.That(share, Is.InRange(0.03f, 0.4f), $"individual {i}: share of the time underground");
                Assert.Greater(scratch[i], 0, $"individual {i} forages");
                Assert.Greater(rest[i], 0, $"individual {i} rests");
            }
            // maxHiddenShare 0.67 of three: two may be under together, never all three (asserted every frame above)
            TestContext.Out.WriteLine($"two under at once: {twoUnder} frames ({(float)twoUnder / frames:P0}), longest {longestTwo * dt:F0} s");
            Assert.Greater(reusing, 0, $"later trips reuse the tunnels ({reusing} of {trips})");
            TunnelNetwork net = TunnelNetwork.For(area);
            for (int i = 0; i < net.Holes; i++) Assert.LessOrEqual(area.PlanarDistance(net.Hole(i)), area.radius, $"hole {i} inside");
            TestContext.Out.WriteLine($"network: {net.Holes} holes, {net.Tunnels} tunnels; carves requested {CreatureDigging.Requested}, refused {CreatureDigging.Refused}");
        }

        // ------------------------------------------------------------ postures

        [TestCase(0f)] [TestCase(1f)]
        public void Burrow_RestAndScratch_NoPlateClipping(float curl)
        {
            CreatureSpecies asset = Asset(), sp = asset.Compiled.Runtime;
            EnclosureArea area = KitChecks.LabArea(sp);
            var sim = Sim(sp, area, area.center, asset.scale, 9);
            BurrowRuntime rt = Rt(sim);
            var world = new Matrix4x4[sp.BoneCount];
            for (int f = 0; f < 600; f++)
            {
                rt.Request(f < 300 ? DigActivity.Rest : DigActivity.Scratch, curl);
                sim.Tick(kDt, sim.Joints[0]);
                if (f % 5 != 0) continue;
                sim.WriteBoneWorld(world, 0);
                Assert.IsNull(ShellClip.Problem(asset, world, 1e-4f, out _), $"f{f}");
                Assert.IsNull(BladeClip.Problem(asset, world, 1e-4f), $"f{f}");
            }
            Assert.IsTrue(sim.Halt, "halted while it scratches");
        }

        [Test] public void Burrow_Trip_NoAllocation()
        {
            CreatureSpecies sp = Delver();
            EnclosureArea area = KitChecks.LabArea(sp);
            var sim = Sim(sp, area, area.center - Vector3.forward * (0.4f * area.safeRadius), sp.scale, 4);
            ICreatureBrain brain = BrainFactory.Create(sp, area, 4);
            var world = new Matrix4x4[sp.BoneCount];
            BurrowRuntime rt = Rt(sim);
            for (int f = 0; f < 30; f++) { sim.Tick(kDt, brain.Target(sim, kDt)); sim.WriteBoneWorld(world, 0); }
            rt.BeginTrip(area.center + Vector3.forward * (0.5f * area.safeRadius));
            for (int f = 0; f < 30; f++) { sim.Tick(kDt, rt.Target); sim.WriteBoneWorld(world, 0); }
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() =>
            {
                for (int f = 0; f < 900 && rt.TripActive; f++) { sim.Tick(kDt, rt.Target); sim.WriteBoneWorld(world, 0); }
            });
            Assert.AreEqual(0, allocs);
        }
    }
}
