using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // TailFlourish (Apex redesign §5.6): the living tail, a pose drawn over the sim. The fixture: the FleshWing flyer
    // (CreatureFleshWingTests: the FlowBody fixture without its keel - 24 joints at 0.1 m, a ribbon tail from t = 0.55 -
    // a mirrored wing arm and FleshWing, mirrored DrakeLeg hind legs at joint 11, Eyes; Walk + Fly with the stance flags
    // + FlightExcursion) with a TailFlourish last, rooted at joint 12 on the part's defaults. Invariants: the flight and
    // the walk are untouched (the flight model, every sim joint and foot bit-identical with and without the part); in
    // sustained flight the tip rolls through >= 60 deg and the curl travels (the tip lags the root); standing, the tail
    // rises in a loop above the hips and never touches the ground beyond its base; zero strengths draw the plain chain
    // bit for bit; no added bend beyond a joint's limit and no inverted triangle over ground and air; inside the culling
    // bounds; zero GC; Froude-similar. Each has a RED.
    public class CreatureTailFlourishTests
    {
        const float kDt = 1f / 60f;
        internal const int kRoot = 12;
        const int kHips = CreatureFleshWingTests.kHips;

        internal static TailFlourish Tail(System.Action<TailFlourish> edit = null)
        {
            var t = new TailFlourish { id = "tail", fromJoint = kRoot };
            edit?.Invoke(t);
            return t;
        }

        internal static CreatureSpecies Species(bool withTail = true, System.Action<TailFlourish> edit = null, string name = "TailFlourishFixture")
        {
            FlowBody body = CreatureFlowBodyTests.Body(); body.profile[2].keel = 0f;
            var parts = new List<CreaturePart> { body, CreatureFleshWingTests.Arm(), CreatureFleshWingTests.Hind(), CreatureFleshWingTests.Wing(), CreatureKitComposeTests.TwoEyes() };
            if (withTail) parts.Add(Tail(edit));
            var mods = new CreatureModule[] { new Walk { speed = 0.45f, turnRateDeg = 60f, accel = 1.5f, turnSlowdown = 0.5f }, CreatureFleshWingTests.FlyModule(), new FlightExcursion() };
            return FlowKit.Build(name, 1f, parts, mods);
        }

        static void Zero(TailFlourish t) { t.airRollDeg = 0f; t.airCurlDeg = 0f; t.groundLoopDeg = 0f; t.groundDriftDeg = 0f; }

        internal static TailFlourishRuntime Rt(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is TailFlourishRuntime f) return f;
            return null;
        }

        static float TailLength(CreatureSpecies sp, float s)
        {
            FlowBody b = null;
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is FlowBody f) b = f;
            return (b.spacing * (b.jointCount - 1 - kRoot) + b.tailTipLength) * s;
        }

        // flying: goals every tick (altitude 8 x s, cruise, a slow turn either way)
        static void FlyTick(CreatureSim sim, float s, int k)
        {
            float T = Mathf.Sqrt(s);
            sim.Flight.SetGoals(8f * s, sim.Species.flight.cruiseSpeed * T, (k / 600 % 2 == 0 ? 0.3f : -0.3f) / T);
            sim.Tick(kDt * T, sim.Joints[0]);
        }

        static Vector3 Circle(Vector3 c, float s, int k) { float a = k * kDt * 0.8f; return c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (1.5f * s); }

        static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        static bool Same(Matrix4x4 a, Matrix4x4 b) { for (int i = 0; i < 16; i++) if (!(a[i] == b[i]) && !(float.IsNaN(a[i]) && float.IsNaN(b[i]))) return false; return true; }

        // ------------------------------------------------------------ the flight and the walk untouched

        internal static string UntouchedProblem(out int airFrames)
        {
            airFrames = 0;
            CreatureSpecies with = Species(), without = Species(false, null, "TailFlourishFixtureBare");
            var a = FlowKit.Sim(with, 1f); var b = FlowKit.Sim(without, 1f);
            TailFlourishRuntime rt = Rt(a);
            var ba = new Matrix4x4[with.BoneCount]; var bb = new Matrix4x4[without.BoneCount];
            int g = a.Flight.Girdle;
            a.Flight.Launch(6f); b.Flight.Launch(6f);
            for (int k = 0; k < 1800; k++)
            {
                FlyTick(a, 1f, k); FlyTick(b, 1f, k);
                a.WriteBoneWorld(ba, 0); b.WriteBoneWorld(bb, 0);
                Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo; a.EncapsulatePartBounds(ref lo, ref hi);
                if (rt.Weight > 0.99f) airFrames++;
                if (a.Mode != b.Mode) return $"flight frame {k}: mode {a.Mode} vs {b.Mode}";
                if (!Same(a.Joints[g], b.Joints[g])) return $"flight frame {k}: girdle {a.Joints[g]:F6} vs {b.Joints[g]:F6}";
                FlightModel ma = a.Flight.Model, mb = b.Flight.Model;
                if (!Same(ma.Position, mb.Position) || !Same(ma.Velocity, mb.Velocity) || !Same(ma.Forward, mb.Forward) || !Same(ma.Up, mb.Up)
                    || ma.Bank != mb.Bank || ma.RollRate != mb.RollRate || ma.StrokeRate != mb.StrokeRate || ma.Demand != mb.Demand)
                    return $"flight frame {k}: the flight model differs (position {ma.Position:F6} vs {mb.Position:F6})";
                for (int j = 0; j < a.Joints.Length; j++)
                    if (!Same(a.Joints[j], b.Joints[j]) || !Same(a.Rotations[j], b.Rotations[j])) return $"flight frame {k}: sim joint {j} differs";
            }
            a = FlowKit.Sim(with, 1f); b = FlowKit.Sim(without, 1f);
            Vector3 c = a.Joints[0];
            for (int k = 0; k < 1800; k++)
            {
                Vector3 target = Circle(c, 1f, k);
                a.Tick(kDt, target); b.Tick(kDt, target);
                a.WriteBoneWorld(ba, 0); b.WriteBoneWorld(bb, 0);
                for (int j = 0; j < a.Joints.Length; j++)
                    if (!Same(a.Joints[j], b.Joints[j]) || !Same(a.Rotations[j], b.Rotations[j])) return $"walk frame {k}: sim joint {j} differs";
                for (int l = 0; l < a.Legs.Length; l++)
                    if (!Same(a.Legs[l].foot, b.Legs[l].foot)) return $"walk frame {k}: foot {l} differs";
            }
            return null;
        }

        [Test] public void Tail_FlightUntouched()
        {
            string e = UntouchedProblem(out int air);
            TestContext.Out.WriteLine($"{air} of 1800 flight frames with the air pose fully in (weight > 0.99)");
            Assert.IsNull(e, e);
            Assert.Greater(air, 900, "the overlay was in its air pose for most of the flight");
        }

        [Test] public void Red_Tail_WritesSim()
        {
            TailFlourishRuntime.WritesSimForTest = true;
            try
            {
                string e = UntouchedProblem(out _);
                TestContext.Out.WriteLine($"RED: {e}");
                Assert.IsNotNull(e);
            }
            finally { TailFlourishRuntime.WritesSimForTest = false; }
        }

        // ------------------------------------------------------------ air: twist and curl

        // the shift (ticks, 0..maxLag, to a fraction of a tick) by which y best follows x (CreatureFleshWingTests.Lag)
        static float Lag(List<float> x, List<float> y, int maxLag)
        {
            int N = Mathf.Min(x.Count, y.Count);
            double mx = 0, my = 0;
            for (int i = 0; i < N; i++) { mx += x[i]; my += y[i]; }
            mx /= N; my /= N;
            var cs = new double[maxLag + 1];
            int best = 0;
            for (int L = 0; L <= maxLag; L++)
            {
                double c = 0;
                for (int i = 0; i + L < N; i++) c += (x[i] - mx) * (y[i + L] - my);
                cs[L] = c / (N - L);
                if (cs[L] > cs[best]) best = L;
            }
            if (best == 0 || best == maxLag) return best;
            double a = cs[best - 1], b = cs[best], d = cs[best + 1], den = a - 2 * b + d;
            return den < 0 ? best + (float)(0.5 * (a - d) / den) : best;
        }

        internal static string TwistProblem(CreatureSpecies sp, out float rollRange, out float lag)
        {
            rollRange = 0f; lag = 0f;
            var sim = FlowKit.Sim(sp, 1f);
            TailFlourishRuntime rt = Rt(sim);
            TailFlourish p = rt.Part;
            int n = sim.Joints.Length;
            var root = new List<float>(); var tip = new List<float>();
            float lo = float.MaxValue, hi = float.MinValue;
            sim.Flight.Launch(6f);
            for (int k = 0; k < 1500; k++)
            {
                FlyTick(sim, 1f, k);
                if (k < 600) continue;
                if (sim.Mode != FlightMode.Air) return $"frame {k}: not in sustained flight ({sim.Mode})";
                lo = Mathf.Min(lo, rt.TipRollDeg); hi = Mathf.Max(hi, rt.TipRollDeg);
                root.Add(rt.CurlDeg(kRoot)); tip.Add(rt.CurlDeg(n - 2));
            }
            rollRange = hi - lo;
            if (rollRange < 60f) return $"the tip's roll spans only {rollRange:F1} deg (< 60)";
            float period = p.airHz > 0f ? 1f / p.airHz : 0f;
            int maxLag = Mathf.RoundToInt(period / kDt);
            lag = maxLag > 0 ? Lag(root, tip, maxLag) * kDt / period : 0f;
            if (!(lag >= 0.25f)) return $"the curl at the tip lags the root by only {lag:F3} of a cycle (< 0.25)";
            return null;
        }

        [Test] public void Tail_Air_TwistsAndCurls()
        {
            string e = TwistProblem(Species(), out float range, out float lag);
            TestContext.Out.WriteLine($"tip roll range {range:F1} deg, curl lag tip vs root {lag:F3} cycle");
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Tail_Limp()
        {
            string e = TwistProblem(Species(true, Zero), out float range, out float lag);
            TestContext.Out.WriteLine($"RED: {e}");
            Assert.IsNotNull(e);
        }

        // ------------------------------------------------------------ ground: the loop

        internal static string LoopProblem(CreatureSpecies sp, float s, out float rise, out float lowest)
        {
            rise = float.MaxValue; lowest = float.MaxValue;
            var sim = FlowKit.Sim(sp, s);
            TailFlourishRuntime rt = Rt(sim);
            int n = sim.Joints.Length;
            float len = TailLength(sp, s), T = Mathf.Sqrt(s);
            Vector3 stand = sim.Joints[0];
            int ticks = 600, settle = 120;
            for (int k = 0; k < ticks; k++)
            {
                sim.Tick(kDt * T, stand);
                if (k < settle) continue;
                float top = rt.PosedTip.y;
                for (int j = kRoot; j < n; j++) top = Mathf.Max(top, rt.PosedJoint(j).y);
                rise = Mathf.Min(rise, (top - sim.Joints[kHips].y) / len);
                float low = rt.PosedTip.y;
                for (int j = kRoot + 2; j < n; j++) low = Mathf.Min(low, rt.PosedJoint(j).y);
                lowest = Mathf.Min(lowest, low / s);   // flat ground at y = 0
            }
            if (lowest < 0.02f) return $"a posed tail point beyond the base comes within {lowest * 100f:F1} cm x s of the ground (< 2)";
            if (rise < 0.3f) return $"the tail's highest point rises only {rise:F3} tail lengths above the hips (< 0.3)";
            return null;
        }

        [Test] public void Tail_Ground_Loops()
        {
            string e = LoopProblem(Species(), 1f, out float rise, out float lowest);
            TestContext.Out.WriteLine($"standing: the loop's top is at least {rise:F3} tail lengths above the hips; the lowest posed point beyond the base {lowest * 100f:F1} cm x s");
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Tail_NoLoop()
        {
            string e = LoopProblem(Species(true, t => t.groundLoopDeg = 0f), 1f, out _, out _);
            TestContext.Out.WriteLine($"RED: {e}");
            Assert.IsNotNull(e);
        }

        [Test] public void Red_Tail_Droops_TouchesTheGround()
        {
            TailFlourishRuntime.DroopForTest = true;
            try
            {
                string e = LoopProblem(Species(), 1f, out _, out float lowest);
                TestContext.Out.WriteLine($"RED: {e}");
                Assert.IsNotNull(e);
                StringAssert.Contains("ground", e);
            }
            finally { TailFlourishRuntime.DroopForTest = false; }
        }

        // ------------------------------------------------------------ zero strengths: the plain chain

        internal static string ChainProblem(CreatureSpecies with, out int frames)
        {
            frames = 0;
            CreatureSpecies without = Species(false, null, "TailFlourishFixtureBare");
            var a = FlowKit.Sim(with, 1f); var b = FlowKit.Sim(without, 1f);
            var ba = new Matrix4x4[with.BoneCount]; var bb = new Matrix4x4[without.BoneCount];
            Vector3 c = a.Joints[0];
            for (int k = 0; k < 900; k++)
            {
                if (k == 300) { a.Flight.Launch(6f); b.Flight.Launch(6f); }
                if (k < 300) { Vector3 target = Circle(c, 1f, k); a.Tick(kDt, target); b.Tick(kDt, target); }
                else { FlyTick(a, 1f, k); FlyTick(b, 1f, k); }
                a.WriteBoneWorld(ba, 0); b.WriteBoneWorld(bb, 0);
                for (int i = 0; i < bb.Length; i++) if (!Same(ba[i], bb[i])) return $"frame {k} ({a.Mode}): bone {i} differs from the plain sim's";
                frames++;
            }
            return null;
        }

        [Test] public void Tail_Strengths_Zero_IsTheChain()
        {
            string e = ChainProblem(Species(true, Zero), out int frames);
            TestContext.Out.WriteLine($"{frames} frames (walk, launch, flight) bit-equal");
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Tail_Strengths_Default_IsNotTheChain()
        {
            string e = ChainProblem(Species(), out _);
            TestContext.Out.WriteLine($"RED: {e}");
            Assert.IsNotNull(e);
        }

        // ------------------------------------------------------------ no kink

        internal static string KinkProblem(CreatureSpecies sp, out float worstExcess, out float worstAdded, out int inverted, out string where)
        {
            worstExcess = float.MinValue; worstAdded = 0f; inverted = 0; where = null;
            var sim = FlowKit.Sim(sp, 1f);
            TailFlourishRuntime rt = Rt(sim);
            var m = new CreaturePteroWingTests.Skinned(sp);
            int n = sim.Joints.Length;
            Vector3 c = sim.Joints[0];
            string kink = null;
            // 15 s walking a circle, 10 s standing, 30 s flying (a launch from standing; cruise, turns either way). (A
            // launch from standing after the circle folded the plain sim's tail back on itself until the fixture's
            // FlightSpec.launchTailBySegment, task 9b: CreatureFleshWingTests.Launch_From*_TailNeverFolds.)
            Vector3 stand = c;
            for (int k = 0; k < 3300; k++)
            {
                if (k < 900) sim.Tick(kDt, Circle(c, 1f, k));
                else if (k < 1500) { if (k == 900) stand = sim.Joints[0]; sim.Tick(kDt, stand); }
                else
                {
                    if (k == 1500) sim.Flight.Launch(6f);
                    FlyTick(sim, 1f, k);
                }
                Vector3[] J = sim.Joints;
                for (int j = kRoot; j < n - 1; j++)
                {
                    float a = Vector3.Angle(J[j] - J[j - 1], J[j + 1] - J[j]);
                    float b = Vector3.Angle(rt.PosedJoint(j) - rt.PosedJoint(j - 1), rt.PosedJoint(j + 1) - rt.PosedJoint(j));
                    float lim = sp.joints[j].bendLimitDeg;
                    worstAdded = Mathf.Max(worstAdded, rt.AddedBendDeg(j));
                    float ex = Mathf.Max(b - a, rt.AddedBendDeg(j)) - lim;
                    if (ex > worstExcess) worstExcess = ex;
                    if (ex > 1e-2f && kink == null) kink = $"frame {k} ({sim.Mode}): joint {j} added bend {Mathf.Max(b - a, rt.AddedBendDeg(j)):F2} deg > its limit {lim:F1}";
                }
                if (k % 15 == 14)
                {
                    m.Pose(sim);
                    int inv = m.Inverted(-1, out string w);
                    if (inv > 0 && where == null) where = $"frame {k} ({sim.Mode}): {w}";
                    inverted += inv;
                }
            }
            if (kink != null) return kink;
            if (inverted > 0) return $"{inverted} inverted triangles ({where})";
            return null;
        }

        [Test] public void Tail_NoKink()
        {
            string e = KinkProblem(Species(), out float excess, out float added, out int inv, out _);
            TestContext.Out.WriteLine($"55 s ground + air (a launch from standing): largest added bend {added:F2} deg, worst (added - limit) {excess:F2} deg, {inv} inverted triangles");
            Assert.IsNull(e, e);
        }

        // The living tail over a launch straight out of the circling walk: the sim's tail within its limits and no posed
        // triangle inverted (CreatureFleshWingTests.LaunchTailProblem on this fixture; the from-standing launch is in
        // Tail_NoKink).
        [Test] public void Tail_LaunchFromCircling_NoFold()
        {
            string e = CreatureFleshWingTests.LaunchTailProblem(Species(), false, out float ex, out int inv);
            TestContext.Out.WriteLine($"launch out of a circling walk with the TailFlourish: worst tail bend - limit {ex:F2} deg, {inv} inverted triangles");
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Tail_Unclamped_Kinks()
        {
            TailFlourishRuntime.NoClampForTest = true;
            try
            {
                string e = KinkProblem(Species(true, t => { t.groundLoopDeg = 30f; t.airCurlDeg = 40f; }), out _, out _, out _, out _);
                TestContext.Out.WriteLine($"RED: {e}");
                Assert.IsNotNull(e);
            }
            finally { TailFlourishRuntime.NoClampForTest = false; }
        }

        // ------------------------------------------------------------ bounds

        // KitChecks.BoundsProblem (the walk), then the same in flight: every skinned vertex inside the culling bounds
        internal static string AirBoundsProblem(CreatureSpecies sp)
        {
            float s = CreatureLab.MaxScale(sp), T = Mathf.Sqrt(s);
            var r = new CreatureRenderer(sp, null);
            try
            {
                var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s);
                r.Add(sim, new CreatureInstance { sizeScale = s });
                Vector3[] v = r.Mesh.vertices;
                var w = new List<Vector4>(); r.Mesh.GetUVs(2, w);
                int outside = 0, checks = 0; float worst = 0f;
                sim.Flight.Launch(6f * s);
                for (int k = 0; k < 900; k++)
                {
                    FlyTick(sim, s, k);
                    if (k % 20 != 19) continue;
                    r.Render();
                    Bounds b = r.LastBounds;
                    Matrix4x4[] skin = r.SkinForTest;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector4 q = w[i];
                        Vector3 p = q.z * skin[(int)q.x].MultiplyPoint3x4(v[i]) + q.w * skin[(int)q.y].MultiplyPoint3x4(v[i]);
                        if (!b.Contains(p)) { outside++; worst = Mathf.Max(worst, Mathf.Sqrt(b.SqrDistance(p))); }
                        checks++;
                    }
                }
                return outside == 0 ? null : $"flight: {outside}/{checks} skinned vertices outside the culling bounds (worst {worst:F3} m)";
            }
            finally { r.Dispose(); }
        }

        // the body's own bounds (before the renderer's reach margin) hold every posed tail point: standing (the loop
        // rises above every sim joint) and flying
        internal static string PointsProblem(CreatureSpecies sp, out float worst)
        {
            worst = 0f;
            var sim = FlowKit.Sim(sp, 1f);
            TailFlourishRuntime rt = Rt(sim);
            var body = new global::SpineBody(sim);
            int n = sim.Joints.Length, outside = 0;
            Vector3 stand = sim.Joints[0];
            for (int k = 0; k < 1200; k++)
            {
                if (k < 600) sim.Tick(kDt, stand);
                else { if (k == 600) sim.Flight.Launch(6f); FlyTick(sim, 1f, k); }
                if (k % 10 != 9) continue;
                Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
                body.EncapsulateBounds(ref lo, ref hi);
                for (int j = kRoot; j <= n; j++)
                {
                    Vector3 p = j < n ? rt.PosedJoint(j) : rt.PosedTip;
                    Vector3 q = Vector3.Max(lo, Vector3.Min(hi, p));   // the nearest point of the box (exact: lo, hi are min / max of points)
                    if (q == p && p.x >= lo.x && p.y >= lo.y && p.z >= lo.z && p.x <= hi.x && p.y <= hi.y && p.z <= hi.z) continue;
                    outside++; worst = Mathf.Max(worst, (q - p).magnitude);
                }
            }
            return outside == 0 ? null : $"{outside} posed tail points outside the body's own bounds (worst {worst:F3} m)";
        }

        [Test] public void Tail_InsideBounds()
        {
            CreatureSpecies sp = Species();
            string e = PointsProblem(sp, out _) ?? KitChecks.BoundsProblem(sp) ?? AirBoundsProblem(sp);
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Tail_NoBounds_Outside()
        {
            TailFlourishRuntime.NoBoundsForTest = true;
            try
            {
                CreatureSpecies sp = Species();
                string e = PointsProblem(sp, out _) ?? KitChecks.BoundsProblem(sp) ?? AirBoundsProblem(sp);
                TestContext.Out.WriteLine($"RED: {e}");
                Assert.IsNotNull(e);
            }
            finally { TailFlourishRuntime.NoBoundsForTest = false; }
        }

        // ------------------------------------------------------------ GC

        [Test] public void Tail_NoGC()
        {
            CreatureSpecies sp = Species();
            var sim = FlowKit.Sim(sp, 1f);
            var bones = new Matrix4x4[sp.BoneCount];
            Vector3 c = sim.Joints[0];
            void Frame() { sim.WriteBoneWorld(bones, 0); Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo; sim.EncapsulatePartBounds(ref lo, ref hi); }
            for (int k = 0; k < 240; k++) { sim.Tick(kDt, Circle(c, 1f, k)); Frame(); }
            GcAllocs.AssertCounterWorks();
            int ground = GcAllocs.Count(() => { for (int k = 240; k < 360; k++) { sim.Tick(kDt, Circle(c, 1f, k)); Frame(); } });
            sim.Flight.Launch(6f);
            for (int k = 0; k < 300; k++) { FlyTick(sim, 1f, k); Frame(); }
            int air = GcAllocs.Count(() => { for (int k = 300; k < 420; k++) { FlyTick(sim, 1f, k); Frame(); } });
            Assert.AreEqual(0, ground, "ground");
            Assert.AreEqual(0, air, "air");
        }

        // ------------------------------------------------------------ Froude

        const float kFroude = 0.02f;

        // the same Froude time (dt x sqrt k) at 0.9x and 1.1x: standing, the loop's top above the hips / s; flying, the
        // tip's roll and the tip's curl
        static void Curves(CreatureSpecies sp, float k, List<float> rise, List<float> roll, List<float> curl)
        {
            float s = sp.scale * k;
            var sim = FlowKit.Sim(sp, s);
            TailFlourishRuntime rt = Rt(sim);
            int n = sim.Joints.Length;
            Vector3 stand = sim.Joints[0];
            for (int t = 0; t < 300; t++)
            {
                sim.Tick(kDt * Mathf.Sqrt(s), stand);
                rise.Add((rt.PosedTip.y - sim.Joints[kHips].y) / s);
            }
            sim.Flight.Launch(6f * s);
            for (int t = 0; t < 900; t++)
            {
                FlyTick(sim, s, t);
                if (t < 300) continue;
                roll.Add(rt.TipRollDeg); curl.Add(rt.CurlDeg(n - 2));
            }
        }

        internal static string FroudeProblem(CreatureSpecies sp, out float worst)
        {
            worst = 0f;
            var ra = new List<float>(); var la = new List<float>(); var ca = new List<float>();
            var rb = new List<float>(); var lb = new List<float>(); var cb = new List<float>();
            Curves(sp, 0.9f, ra, la, ca); Curves(sp, 1.1f, rb, lb, cb);
            float Range(List<float> l) { float lo = float.MaxValue, hi = float.MinValue; foreach (float x in l) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); } return Mathf.Max(1e-3f, hi - lo); }
            float Worst(List<float> a, List<float> b) { float r = Range(a), w = 0f; for (int i = 0; i < a.Count && i < b.Count; i++) w = Mathf.Max(w, Mathf.Abs(a[i] - b[i]) / r); return w; }
            float wr = Worst(ra, rb), wl = Worst(la, lb), wc = Worst(ca, cb);
            worst = Mathf.Max(wr, Mathf.Max(wl, wc));
            TestContext.Out.WriteLine($"0.9x vs 1.1x in Froude time: loop top differs by {wr * 100f:F2} % of its range ({Range(ra):F3} m/s), tip roll {wl * 100f:F2} % ({Range(la):F1} deg), tip curl {wc * 100f:F2} % ({Range(ca):F1} deg)");
            return worst <= kFroude ? null : $"the 0.9x and 1.1x tails differ by {worst * 100f:F2} % of their range in Froude time";
        }

        [Test] public void Tail_Froude()
        {
            string e = FroudeProblem(Species(), out _);
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Tail_NotFroude()
        {
            TailFlourishRuntime.IgnoreSizeForTest = true;
            try
            {
                string e = FroudeProblem(Species(), out _);
                TestContext.Out.WriteLine($"RED: {e}");
                Assert.IsNotNull(e);
            }
            finally { TailFlourishRuntime.IgnoreSizeForTest = false; }
        }


        // ------------------------------------------------------------ touchdown: eased back to the loop

        // a launch from a straight walk, 8 s of flight, a landing ahead and 4 s on the ground: the blend never snaps
        // (per tick at most the eased ramp's steepest step at the default easeSeconds, 1.5 x dt / 0.6 s - the flight's
        // own air weight steps by up to 0.12 a tick) and the loop is back after it
        internal static string TouchdownProblem(CreatureSpecies sp, out float maxStep, out float rise)
        {
            maxStep = 0f; rise = float.MaxValue;
            var sim = FlowKit.Sim(sp, 1f);
            TailFlourishRuntime rt = Rt(sim);
            float ease = new TailFlourish().easeSeconds, len = TailLength(sp, 1f), prev = rt.Weight, step = 0f;
            int n = sim.Joints.Length;
            void Track() { step = Mathf.Max(step, Mathf.Abs(rt.Weight - prev)); prev = rt.Weight; }
            for (int k = 0; k < 180; k++) { sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * 2f); Track(); }
            if (!sim.Flight.Launch(6f)) return "no launch";
            for (int k = 0; k < 480; k++) { FlyTick(sim, 1f, 0); Track(); }
            if (!(rt.Weight > 0.99f)) return $"in the air the blend is only at {rt.Weight:F3}";
            FlightModel m = sim.Flight.Model;
            sim.Flight.Land(m.Position + m.Forward * 8f - Vector3.up * m.Altitude);
            int t = 0;
            for (; t < 7200 && sim.Mode != FlightMode.Ground; t++) { sim.Tick(kDt, sim.Joints[0]); Track(); }
            if (sim.Mode != FlightMode.Ground) return "it never landed";
            Vector3 stand = sim.Joints[0];
            for (int k = 0; k < 240; k++)
            {
                sim.Tick(kDt, stand); Track();
                if (k < 120) continue;
                float top = rt.PosedTip.y;
                for (int j = kRoot; j < n; j++) top = Mathf.Max(top, rt.PosedJoint(j).y);
                rise = Mathf.Min(rise, (top - sim.Joints[kHips].y) / len);
            }
            maxStep = step;
            float allowed = ease > 0f ? 1.5f * kDt / ease + 1e-4f : 0f;
            if (maxStep > allowed) return $"the ground / air blend jumps by {maxStep:F3} in one tick (> {allowed:F3}: not eased)";
            if (!(rt.Weight < 1e-4f)) return $"4 s after the touchdown the blend is still at {rt.Weight:F3}";
            if (rise < 0.3f) return $"after the touchdown the loop rises only {rise:F3} tail lengths above the hips (< 0.3)";
            return null;
        }

        [Test] public void Tail_Touchdown_EasesBackToTheLoop()
        {
            string e = TouchdownProblem(Species(), out float step, out float rise);
            TestContext.Out.WriteLine($"largest blend step per tick {step:F4}; after the touchdown the loop's top >= {rise:F3} tail lengths above the hips");
            Assert.IsNull(e, e);
        }

        [Test] public void Red_Tail_Snaps()
        {
            string e = TouchdownProblem(Species(true, t => t.easeSeconds = 0f), out _, out _);
            TestContext.Out.WriteLine($"RED: {e}");
            Assert.IsNotNull(e);
        }

        // ------------------------------------------------------------ data

        [Test] public void Tail_Validate()
        {
            Assert.IsNull(Species().Validate());
            CreatureSpecies Try(System.Action<TailFlourish> e, bool last = true)
            {
                var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
                sp.name = "TailValidate"; sp.scale = 1f; sp.kitVersion = 1;
                var parts = new List<CreaturePart> { CreatureFlowBodyTests.Body(), CreatureFlowBodyTests.HindLeg() };
                if (last) { parts.Add(CreatureKitComposeTests.TwoEyes()); parts.Add(Tail(e)); }
                else { parts.Add(Tail(e)); parts.Add(CreatureKitComposeTests.TwoEyes()); }
                sp.parts = parts; sp.modules = new List<CreatureModule> { new Walk() };
                return sp;
            }
            StringAssert.Contains("last part", Try(null, false).Compiled.Error ?? "");
            StringAssert.Contains("fromJoint", Try(t => t.fromJoint = 22).Compiled.Error ?? "");
            StringAssert.Contains("waveLength", Try(t => t.waveLength = 0f).Compiled.Error ?? "");
            StringAssert.Contains(">= 0", Try(t => t.airRollDeg = -1f).Compiled.Error ?? "");
            Assert.IsNull(Try(null).Compiled.Error);
        }
    }
}
