using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The SkyDragon's twisting flight (AirTwist, Fly.twist): the body follows the head's path, so a swim or a
    // corkscrew of the head is a wave travelling head -> tail at the path speed and a roll travels the same
    // way; frames stay continuous through every manoeuvre (no per-tick jump, no flip), lengths exact, the
    // bend cones hold, no NaN, the wings stay on the girdle; the winged serpent never twists.
    public class CreatureDragonTwistTests
    {
        const float kDt = 1f / 60f;
        public const string kDragonPath = "Assets/TerrainGen/Config/Creatures/SkyDragon.asset";
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        // The dragon's runtime view (kit asset compiled).
        internal static CreatureSpecies Dragon()
        {
            var a = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>(kDragonPath);
            Assert.IsNotNull(a, kDragonPath);
            Assert.IsNull(a.Validate(), "SkyDragon validates");
            return a.Compiled.Runtime;
        }

        // A dragon flying straight and level along +Z on the synthetic flight line (an AirChain driven by a constant
        // girdle velocity): only the twist moves the body. spec: the twist to use (the asset's, edited).
        sealed class Line
        {
            public AirChain chain;
            public Vector3[] joints;
            public Vector3 girdle, vel;
            public float h, seg;
            public void Step()
            {
                girdle += vel * h;
                chain.Step(h, joints, girdle, vel, Vector3.up, Vector3.up);
            }
        }

        static Line FlyLine(CreatureSpecies sp, AirTwistSpec twist, float scale, float speed)
        {
            FlightSpec f = CreatureKit.KitTypes.Memberwise(sp.flight);   // a copy: the species stays as it is
            f.twist = twist;
            int g = sp.legs[sp.wings[0].leg].attachJoint;
            var chain = new AirChain(sp, f, g, scale);
            Assert.IsNotNull(chain.Twist, "twist on");
            float seg = sp.spacing * scale;
            var joints = new Vector3[sp.JointCount];
            Vector3 girdle = new Vector3(0f, 8f * scale, 0f);
            for (int i = 0; i < joints.Length; i++) joints[i] = girdle + Vector3.forward * (seg * (g - i));
            var vel = Vector3.forward * speed;
            chain.Reset(girdle, vel);
            return new Line { chain = chain, joints = joints, girdle = girdle, vel = vel, h = kDt / Mathf.Max(1, sp.subSteps), seg = seg };
        }

        static AirTwistSpec Only(AirTwistSpec t, bool swim, bool manoeuvres)
        {
            if (!swim) t.swimAmplitude = 0f;
            if (!manoeuvres) { t.corkscrewAmplitude = 0f; t.barrelRadius = 0f; t.loopRadius = 0f; t.slowRollTime = 0f; }
            return t;
        }

        // Signed roll of a frame about its forward: from the upright (world up made perpendicular) to its up.
        static float RollOf(Vector3 f, Vector3 u)
        {
            Vector3 r = Vector3.ProjectOnPlane(Vector3.up, f);
            if (r.sqrMagnitude < 1e-8f) return 0f;
            r.Normalize();
            return Mathf.Atan2(Vector3.Dot(f, Vector3.Cross(r, u)), Vector3.Dot(r, u));
        }

        // The time lag (s) that best aligns b(t) with a(t - lag), searched in sub-step units.
        static float BestLag(List<float> a, List<float> b, float h, int minLag, int maxLag)
        {
            int best = 0; float bestErr = float.MaxValue;
            for (int lag = Mathf.Max(0, minLag); lag <= maxLag; lag++)
            {
                double e = 0; int c = 0;
                for (int t = lag; t < b.Count; t++) { double d = b[t] - a[t - lag]; e += d * d; c++; }
                if (c > 0 && e / c < bestErr) { bestErr = (float)(e / c); best = lag; }
            }
            return best * h;
        }

        // ------------------------------------------------------------------ data

        [Test] public void Twist_Off_ForTheSerpent()
        {
            var serpent = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>("Assets/TerrainGen/Config/Creatures/WingedSerpent.asset");
            Assert.IsFalse(serpent.Compiled.Runtime.flight.twist.Enabled, "legacy flyer: no twist");
            var chain = new AirChain(serpent, serpent.flight, 5, 1f);
            Assert.IsNull(chain.Twist, "the serpent's air chain is the trailing pull chain");
            var s = WingedSerpentTestSpecies.Create(); trash.Add(s.species);
            var sim = new CreatureSim(s.species, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward);
            Assert.IsNull(sim.Flight.Twist);
            CreatureSpecies d = Dragon();
            Assert.IsTrue(d.flight.twist.Enabled, "the dragon twists");
            Assert.IsTrue(d.flight.twist.CanCorkscrew && d.flight.twist.CanBarrel && d.flight.twist.CanLoop && d.flight.twist.CanSlowRoll);
        }

        // ------------------------------------------------------------------ the travelling wave

        // Swimming in straight flight: joint k's sideways offset is the head's, k segments of path later.
        [Test] public void Swim_WaveTravelsHeadToTail_AtPathSpeed()
        {
            CreatureSpecies sp = Dragon();
            float s = sp.scale, v = sp.flight.cruiseSpeed * Mathf.Sqrt(s);
            var line = FlyLine(sp, Only(sp.flight.twist, true, false), s, v);
            for (int t = 0; t < 1200; t++) line.Step();
            var head = new List<float>(); var mid = new List<float>(); var tail = new List<float>();
            int km = 30, kt = sp.JointCount - 3;
            float arc = 0f; Vector3 prev = line.joints[0];
            for (int t = 0; t < 1600; t++)
            {
                line.Step();
                head.Add(line.joints[0].x); mid.Add(line.joints[km].x); tail.Add(line.joints[kt].x);
                arc += (line.joints[0] - prev).magnitude; prev = line.joints[0];
            }
            float pathSpeed = arc / (1600 * line.h);
            float wantM = km * line.seg / pathSpeed, wantT = kt * line.seg / pathSpeed;
            // the swim repeats every period: search within half a period of the expected lag
            float half = 0.5f * Mathf.Sqrt(s) / sp.flight.twist.swimHz;
            float lagM = BestLag(head, mid, line.h, (int)((wantM - half) / line.h), (int)((wantM + half) / line.h));
            float lagT = BestLag(head, tail, line.h, (int)((wantT - half) / line.h), (int)((wantT + half) / line.h));
            float amp = 0f; foreach (float x in head) amp = Mathf.Max(amp, Mathf.Abs(x));
            TestContext.WriteLine($"path speed {pathSpeed:F2} m/s (flight {v:F2}), head sway {amp:F2} m, lag mid {lagM:F3} s (want {wantM:F3}), tail {lagT:F3} s (want {wantT:F3}), wavelength {pathSpeed / (sp.flight.twist.swimHz / Mathf.Sqrt(s)):F2} m");
            Assert.Greater(amp, 0.5f * sp.flight.twist.swimAmplitude * s, "the head swims");
            Assert.AreEqual(wantM, lagM, 0.04f * wantM + 2f * line.h, "the wave reaches mid-body one path delay later");
            Assert.AreEqual(wantT, lagT, 0.04f * wantT + 2f * line.h, "and the tail");
            float tailAmp = 0f; foreach (float x in tail) tailAmp = Mathf.Max(tailAmp, Mathf.Abs(x));
            Assert.AreEqual(amp, tailAmp, 0.08f * amp, "the wave keeps its amplitude down the body (no pull-chain decay)");
        }

        // A corkscrew: the head's roll reaches joint k one path delay later (the twist travels head to tail).
        [Test] public void Corkscrew_RollTravelsHeadToTail()
        {
            CreatureSpecies sp = Dragon();
            float s = sp.scale, v = sp.flight.cruiseSpeed * Mathf.Sqrt(s);
            var line = FlyLine(sp, Only(sp.flight.twist, false, true), s, v);
            for (int t = 0; t < 300; t++) line.Step();
            AirTwist tw = line.chain.Twist;
            Assert.IsTrue(tw.Begin(TwistManoeuvre.Corkscrew, v, Vector3.forward, Vector3.up));
            int k = 40;
            var head = new List<float>(); var body = new List<float>();
            float uh = 0f, ub = 0f, ph = 0f, pb = 0f, arc = 0f; Vector3 prev = line.joints[0];
            int steps = Mathf.CeilToInt((tw.Duration(TwistManoeuvre.Corkscrew, v) + 3f) / line.h);
            for (int t = 0; t < steps; t++)
            {
                line.Step();
                float rh = RollOf(line.chain.Forward[0], line.chain.Up[0]), rb = RollOf(line.chain.Forward[k], line.chain.Up[k]);
                uh += Mathf.DeltaAngle(ph * Mathf.Rad2Deg, rh * Mathf.Rad2Deg) * Mathf.Deg2Rad; ph = rh;
                ub += Mathf.DeltaAngle(pb * Mathf.Rad2Deg, rb * Mathf.Rad2Deg) * Mathf.Deg2Rad; pb = rb;
                head.Add(uh); body.Add(ub);
                if (tw.Busy) arc += (line.joints[0] - prev).magnitude;
                prev = line.joints[0];
            }
            float busy = tw.Duration(TwistManoeuvre.Corkscrew, v);
            float pathSpeed = arc / busy;
            float want = k * line.seg / pathSpeed, lag = BestLag(head, body, line.h, 0, (int)(3f * want / line.h));
            TestContext.WriteLine($"head rolled {uh * Mathf.Rad2Deg:F0} deg, joint {k} {ub * Mathf.Rad2Deg:F0} deg; lag {lag:F3} s, want {want:F3} s (path {pathSpeed:F2} m/s)");
            Assert.AreEqual(360f * sp.flight.twist.corkscrewRolls, Mathf.Abs(uh) * Mathf.Rad2Deg, 15f, "the head rolls the configured turns");
            Assert.AreEqual(uh, ub, 0.2f, "the body follows it round");
            Assert.AreEqual(want, lag, 0.1f * want + 3f * line.h, "the roll travels down the body at the path speed");
        }

        // ------------------------------------------------------------------ continuity through every manoeuvre

        internal sealed class FlightRun
        {
            public CreatureSim sim;
            public float maxFrameJump, maxNeighbour, maxLenErr, maxBendOver, maxShoulderGap;
            public int ticks;
        }

        internal static CreatureSim AirborneDragon(CreatureSpecies sp, float scale, float altitude, out EnclosureArea area)
        {
            area = EnclosureArea.For(Vector3.zero, Vector3.up, 300f, sp, scale);
            area.maxHeight = 200f;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, new Vector3(0f, 0f, -150f), Vector3.forward, scale) { Bounds = area };
            Assert.IsTrue(sim.Flight.Launch(altitude));
            for (int t = 0; t < 900; t++) { sim.Flight.SetGoals(altitude, sp.flight.cruiseSpeed * sim.TimeScale, 0f); sim.Tick(kDt, sim.Joints[0]); }
            Assert.AreEqual(FlightMode.Air, sim.Mode, "airborne");
            return sim;
        }

        // Ticks a flying sim straight and level, tracking the frame checks.
        internal static void Track(FlightRun r, int ticks, float altitude)
        {
            CreatureSim sim = r.sim;
            CreatureSpecies sp = sim.Species;
            int n = sp.JointCount;
            var prevRot = (Quaternion[])sim.Rotations.Clone();
            var bones = new Matrix4x4[sp.BoneCount];
            float seg = sp.spacing * sim.SizeScale;
            int g = sim.Flight.Girdle;
            for (int t = 0; t < ticks; t++)
            {
                sim.Flight.SetGoals(altitude, sp.flight.cruiseSpeed * sim.TimeScale, 0f);
                sim.Tick(kDt, sim.Joints[0]);
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = sim.Joints[i];
                    Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x), $"joint {i} finite at tick {t}");
                    r.maxFrameJump = Mathf.Max(r.maxFrameJump, Quaternion.Angle(prevRot[i], sim.Rotations[i]));
                    if (i > 0)
                    {
                        r.maxNeighbour = Mathf.Max(r.maxNeighbour, Vector3.Angle(sim.Rotations[i - 1] * Vector3.up, sim.Rotations[i] * Vector3.up));
                        r.maxLenErr = Mathf.Max(r.maxLenErr, Mathf.Abs((sim.Joints[i] - sim.Joints[i - 1]).magnitude - seg) / seg);
                    }
                    if (i > 0 && i + 1 < n)
                    {
                        float lim = sp.joints[i].rigid ? 0f : sp.joints[i].bendLimitDeg;
                        float bend = Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1]);
                        r.maxBendOver = Mathf.Max(r.maxBendOver, bend - lim);
                    }
                }
                Array.Copy(sim.Rotations, prevRot, n);
                // the wings stay attached: each wing arm's upper bone at its shoulder offset from the girdle joint
                sim.WriteBoneWorld(bones, 0);
                for (int k = 0; k < sp.WingCount; k++)
                {
                    LegSpec arm = sp.legs[sp.wings[k].leg];
                    Vector3 so = arm.shoulderOffset; so.x *= arm.side;
                    Vector3 want = sim.Joints[g] + sim.Rotations[g] * (so * sim.SizeScale);
                    Vector3 got = bones[sp.LegBone(sp.wings[k].leg, 0)].GetColumn(3);
                    r.maxShoulderGap = Mathf.Max(r.maxShoulderGap, (got - want).magnitude);
                }
                r.ticks++;
            }
        }

        [TestCase(TwistManoeuvre.Corkscrew)]
        [TestCase(TwistManoeuvre.BarrelRoll)]
        [TestCase(TwistManoeuvre.Loop)]
        [TestCase(TwistManoeuvre.SlowRoll)]
        [TestCase(TwistManoeuvre.None)]
        public void Manoeuvre_FramesContinuous_LengthsExact(TwistManoeuvre m)
        {
            CreatureSpecies sp = Dragon();
            float s = sp.scale;
            float alt = 6f * s;
            var sim = AirborneDragon(sp, s, alt, out _);
            var r = new FlightRun { sim = sim };
            Track(r, 240, alt);
            AirTwist tw = sim.Flight.Twist;
            if (m != TwistManoeuvre.None)
                Assert.IsTrue(tw.Begin(m, sim.Flight.Model.HorizontalSpeed, sim.Flight.Model.Forward, Vector3.up), "begins");
            float dur = m != TwistManoeuvre.None ? tw.Duration(m, sim.Flight.Model.HorizontalSpeed) : 4f;
            Track(r, Mathf.CeilToInt((dur + 2.5f) / kDt), alt);
            Assert.IsFalse(tw.Busy, "finished");
            TestContext.WriteLine($"{m}: {r.ticks} ticks, max frame turn per tick {r.maxFrameJump:F1} deg, neighbour ups {r.maxNeighbour:F1} deg, " +
                                  $"segment error {r.maxLenErr:P3}, bend over limit {r.maxBendOver:F2} deg, shoulder gap {r.maxShoulderGap * 1000f:F2} mm");
            Assert.Less(r.maxFrameJump, 20f, "no frame jumps or flips between ticks");
            Assert.Less(r.maxNeighbour, 20f, "neighbouring frames agree (no twist kink)");
            Assert.Less(r.maxLenErr, 1e-3f, "segment lengths exact");
            Assert.Less(r.maxBendOver, 0.5f, "bend cones hold");
            Assert.Less(r.maxShoulderGap, 1e-3f * s, "the wings stay on the girdle");
        }

        // A loop is a real loop: the head goes over the top upside down and comes back level.
        [Test] public void Loop_GoesOverTheTop_Inverted()
        {
            CreatureSpecies sp = Dragon();
            float s = sp.scale;
            float alt = 6f * s;
            var sim = AirborneDragon(sp, s, alt, out _);
            var r = new FlightRun { sim = sim };
            Track(r, 240, alt);
            AirTwist tw = sim.Flight.Twist;
            float y0 = sim.Joints[0].y;
            Assert.IsTrue(tw.Begin(TwistManoeuvre.Loop, sim.Flight.Model.HorizontalSpeed, sim.Flight.Model.Forward, Vector3.up));
            float top = float.MinValue, minUp = 1f, minFwdZ = 1f;
            while (tw.Busy)
            {
                Track(r, 1, alt);
                top = Mathf.Max(top, sim.Joints[0].y - y0);
                minUp = Mathf.Min(minUp, (sim.Rotations[0] * Vector3.up).y);
                minFwdZ = Mathf.Min(minFwdZ, Vector3.Dot(sim.Rotations[0] * Vector3.forward, sim.Flight.Model.Forward));
            }
            Track(r, 180, alt);
            float upEnd = (sim.Rotations[0] * Vector3.up).y;
            TestContext.WriteLine($"loop: top {top:F2} m over {2f * sp.flight.twist.loopRadius * s:F2}, head up.y min {minUp:F2}, forward min {minFwdZ:F2}, up.y after {upEnd:F2}");
            Assert.Greater(top, 1.6f * sp.flight.twist.loopRadius * s, "over the top");
            Assert.Less(minUp, -0.8f, "upside down at the top");
            Assert.Less(minFwdZ, -0.8f, "heading back at the top");
            Assert.Greater(upEnd, 0.8f, "upright again afterwards");
        }

        // Froude similarity of the twist: at 0.9x and 1.1x the head's sway / s and the swim period / sqrt(s) agree.
        [Test] public void Twist_Size_Froude()
        {
            CreatureSpecies sp = Dragon();
            var amp = new float[2]; var period = new float[2];
            float[] f = { 0.9f, 1.1f };
            for (int c = 0; c < 2; c++)
            {
                float s = sp.scale * f[c], v = sp.flight.cruiseSpeed * Mathf.Sqrt(s);
                var line = FlyLine(sp, Only(sp.flight.twist, true, false), s, v);
                int warm = Mathf.RoundToInt(10f * Mathf.Sqrt(s) / line.h), run = Mathf.RoundToInt(12f * Mathf.Sqrt(s) / line.h);
                for (int t = 0; t < warm; t++) line.Step();
                float a = 0f; int crossings = 0; float first = -1f, last = -1f; float px = line.joints[0].x;
                for (int t = 0; t < run; t++)
                {
                    line.Step();
                    float x = line.joints[0].x;
                    a = Mathf.Max(a, Mathf.Abs(x));
                    if (px < 0f && x >= 0f) { float tc = t * line.h; if (first < 0f) first = tc; last = tc; crossings++; }
                    px = x;
                }
                amp[c] = a / s;
                period[c] = (last - first) / Mathf.Max(1, crossings - 1) / Mathf.Sqrt(s);
            }
            TestContext.WriteLine($"sway/s {amp[0]:F4} vs {amp[1]:F4}, period/sqrt(s) {period[0]:F4} vs {period[1]:F4}");
            Assert.AreEqual(amp[0], amp[1], 0.01f * amp[0], "sway follows the size");
            Assert.AreEqual(period[0], period[1], 0.01f * period[0], "the swim period follows sqrt(size)");
        }

        // FlightBrain flies manoeuvres in the lab aviary sized for the dragon (AviarySizing): several kinds, inside
        // the safe cylinder and under the roof for three minutes, two seeds, smallest and largest individuals.
        [TestCase(1, 0.94f)]
        [TestCase(2, 1.06f)]
        public void Brain_Manoeuvres_InTheAviary(int seed, float sizeFactor)
        {
            CreatureSpecies sp = Dragon();
            float s = sp.scale * sizeFactor;
            float big = CreatureLab.MaxScale(sp);
            float radius = AviarySizing.Radius(sp, big, CreatureLab.kAviaryRadius), height = AviarySizing.Height(sp, big, CreatureLab.kAviaryHeight);
            EnclosureArea area = EnclosureArea.For(Vector3.zero, Vector3.up, radius, sp, big);
            area.maxHeight = height;
            Assert.IsTrue(FlightBrain.FitsAviary(sp, area, big), $"fits the {radius:F1} m x {height:F1} m aviary");
            var brain = new FlightBrain(area, seed);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed) { Bounds = area };
            var kinds = new HashSet<TwistManoeuvre>();
            float worstPlanar = 0f, worstHeight = 0f, minY = float.MaxValue;
            int flights = 0;
            for (int t = 0; t < Mathf.RoundToInt(180f / kDt); t++)
            {
                sim.Tick(kDt, brain.Target(sim, kDt));
                if (sim.Flight.Twist.Busy) kinds.Add(sim.Flight.Twist.Current);
                for (int i = 0; i < sim.Joints.Length; i++)
                {
                    Vector3 p = sim.Joints[i];
                    Assert.IsFalse(float.IsNaN(p.x + p.y + p.z), "finite");
                    worstPlanar = Mathf.Max(worstPlanar, area.PlanarDistance(p));
                    worstHeight = Mathf.Max(worstHeight, p.y);
                    minY = Mathf.Min(minY, p.y);
                }
                flights = brain.Flights;
            }
            TestContext.WriteLine($"seed {seed} x{sizeFactor}: aviary {radius:F1} x {height:F1} m, {flights} flights, {sim.Flight.Twist.Manoeuvres} manoeuvres ({string.Join(", ", kinds)}), " +
                                  $"max planar {worstPlanar:F2} m, max height {worstHeight:F2} m, min y {minY:F3}");
            Assert.GreaterOrEqual(flights, 1, "it flies");
            Assert.GreaterOrEqual(sim.Flight.Twist.Manoeuvres, 3, "manoeuvres every so often");
            Assert.GreaterOrEqual(kinds.Count, 2, "more than one kind");
            Assert.LessOrEqual(worstPlanar, area.radius, "inside the aviary");
            Assert.LessOrEqual(worstHeight, height + 0.5f * s, "under the roof");
            Assert.Greater(minY, -0.05f * s, "never below the ground");
        }
    }
}
