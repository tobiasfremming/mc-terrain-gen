using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // AttachedChain (Vesper step 2 groundwork, skeleton only): a secondary chain attached onto the mother (spine) chain's
    // joints at one end or both. The fixture is the DigitWing fixture's winged FlowBody (CreatureFleshWingTests: 24 joints
    // at 0.1 m, a narrow raised neck, the wing arm, DrakeLeg hind legs, Eyes; Walk + Fly + FlightExcursion + SkeletonOnly)
    // with a one-ended wattle under the jaw (joint 1), a two-ended pouch from the jaw to the chest (joint 1 -> 6, slack
    // 0.25, keep-out on) and a mirrored pair of one-ended lappets on joint 2. Invariants: the bone layout; a one-ended
    // chain hangs below its joint at rest, lags and swings when the mother moves and settles without jitter; attached
    // ends stay on their mother joints through walking, tight turns and flight; lengths exact; bend cone held; never
    // inside the mother body (keep-out); Froude-similar 0.9x / 1.1x; no GC; Validate; F3 draws every joint. Each has a RED.
    public class CreatureAttachedChainTests
    {
        const float kDt = 1f / 60f;

        internal const int kPouchStart = 1, kPouchEnd = 6;

        internal static AttachedChain Wattle() => new AttachedChain
        {
            id = "wattle", at = new PartSocket(1, 0, new Vector3(0f, -0.05f, 0.02f)), segments = 6, length = 0.2f,
            direction = new Vector3(0f, -1f, 0.15f), curlDeg = 20f, rootRadius = 0.012f, tipRadius = 0.006f,
            stiffnessHz = 1.2f, damping = 3f, maxBendDeg = 30f,
        };

        internal static AttachedChain Pouch() => new AttachedChain
        {
            id = "pouch", at = new PartSocket(kPouchStart, 0, new Vector3(0f, -0.05f, 0.02f)), endJoint = kPouchEnd, endOffset = new Vector3(0f, -0.12f, 0f),
            segments = 10, length = 0f, slack = 0.25f, direction = new Vector3(0f, -1f, -0.2f), rootRadius = 0.02f, tipRadius = 0.04f,
            stiffnessHz = 1.5f, damping = 3f, maxBendDeg = 35f, collideRadius = 0.01f,
        };

        internal static AttachedChain Lappet() => new AttachedChain
        {
            id = "lappet", at = new PartSocket(2, 0, new Vector3(0.04f, 0f, 0f)), mirror = true, segments = 4, length = 0.1f,
            direction = new Vector3(1f, -1f, -0.3f), curlDeg = -15f, rootRadius = 0.01f, tipRadius = 0.004f, stiffnessHz = 2f, damping = 4f,
        };

        internal static CreatureSpecies Species(System.Action<List<CreaturePart>> edit = null, string name = "AttachedChainFixture", bool keel = false)
        {
            FlowBody body = CreatureFlowBodyTests.Body(); if (!keel) body.profile[2].keel = 0f;
            var parts = new List<CreaturePart>
            {
                body, CreatureFleshWingTests.Arm(), CreatureFleshWingTests.Hind(), CreatureDigitWingTests.Wing(), CreatureKitComposeTests.TwoEyes(),
                Wattle(), Pouch(), Lappet(),
            };
            edit?.Invoke(parts);
            var mods = new List<CreatureModule>
            {
                new Walk { speed = 0.45f, turnRateDeg = 60f, accel = 1.5f, turnSlowdown = 0.5f }, CreatureFleshWingTests.FlyModule(), new FlightExcursion(), new SkeletonOnly(),
            };
            return FlowKit.Build(name, 1f, parts, mods);
        }

        internal static AttachedChainRuntime Chain(CreatureSim sim, string id)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is AttachedChainRuntime a && a.Part.id == id) return a;
            return null;
        }

        static Vector3 Ahead(CreatureSim sim, float s) => sim.Joints[0] + sim.Rotations[0] * Vector3.forward * (2f * s);

        // joint k of a chain in its start joint's frame, / size
        static Vector3 Local(CreatureSim sim, AttachedChainRuntime c, int k, float s)
        {
            int j = c.Part.at.joint;
            return Quaternion.Inverse(sim.Rotations[j]) * (c.Joint(k) - sim.Joints[j]) / s;
        }

        // ------------------------------------------------------------ the scenario: walk, tight turns, flight

        // Ticks a sim through walking ahead, tight turns (a target swung from side to side close by), a launch, beating
        // flight with banked turns and a dive fold, calling `each` after every tick. Froude time (dt x sqrt s).
        internal static void Scenario(CreatureSpecies sp, CreatureSim sim, float s, System.Action<string> each, bool fly = true)
        {
            float T = Mathf.Sqrt(s), dt = kDt * T;
            for (int t = 0; t < 150; t++) { sim.Tick(dt, Ahead(sim, s)); each("walk"); }
            for (int t = 0; t < 240; t++)
            {
                float side = (t / 60) % 2 == 0 ? 1f : -1f;
                Vector3 tgt = sim.Joints[0] + sim.Rotations[0] * new Vector3(side * 0.6f, 0f, -0.1f) * s;
                sim.Tick(dt, tgt); each("turns");
            }
            if (!fly) return;
            Assert.IsTrue(sim.Flight.Launch(6f * s), "launch");
            for (int t = 0; t < 360; t++)
            {
                float turn = t < 120 ? 0f : t < 240 ? 60f : -60f;
                sim.Flight.DiveFold = t > 300 ? 0.8f : 0f;
                sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, turn);
                sim.Tick(dt, sim.Joints[0]); each("flight");
            }
        }

        // ------------------------------------------------------------ layout

        [Test] public void AttachedChain_Bones_Layout()
        {
            CreatureSpecies sp = Species();
            Assert.IsNull(sp.Validate());
            Assert.IsTrue(sp.skeletonOnly, "a SkeletonOnly fixture");
            string layout = KitChecks.LayoutProblem(sp);
            Assert.IsNull(layout, layout);
            CompiledSpecies c = sp.Compiled;
            var sim = FlowKit.Sim(sp, 1f);
            int chains = 0;
            for (int i = 0; i < c.Parts.Count; i++)
            {
                if (!(c.Parts[i] is AttachedChain a)) continue;
                chains++;
                AttachedChainRuntime rt = Chain(sim, a.id);
                Assert.IsNotNull(rt, a.id);
                int count = 0;
                foreach (BoneRange br in c.Layout.Ranges) if (br.Id == a.id) count = br.Count;
                Assert.AreEqual(a.segments + 1, count, $"{a.id}: one bone per chain joint");
                Assert.AreEqual(c.Layout.Start(i), rt.FirstBone, $"{a.id}: its range");
            }
            Assert.AreEqual(4, chains, "wattle, pouch, lappet.L, lappet.R");
            FlowKit.Run(sim, 0.5f, Ahead(sim, 1f));
            var bones = new Matrix4x4[sp.BoneCount];
            for (int i = 0; i < bones.Length; i++) bones[i] = Matrix4x4.zero;
            sim.WriteBoneWorld(bones, 0);
            foreach (IPartRuntime r in sim.PartRuntimes)
            {
                if (!(r is AttachedChainRuntime a)) continue;
                for (int k = 0; k < a.JointCount; k++)
                {
                    Matrix4x4 m = bones[a.FirstBone + k];
                    Assert.AreNotEqual(Matrix4x4.zero, m, $"{a.Part.id} bone {k} written");
                    Assert.Less(((Vector3)m.GetColumn(3) - a.Joint(k)).magnitude, 1e-5f, $"{a.Part.id} bone {k} at its joint");
                    Vector3 z = m.MultiplyVector(Vector3.forward).normalized;
                    Vector3 along = (k < a.JointCount - 1 ? a.Joint(k + 1) - a.Joint(k) : a.Joint(k) - a.Joint(k - 1)).normalized;
                    Assert.Greater(Vector3.Dot(z, along), 0.9999f, $"{a.Part.id} bone {k}: +Z along the chain");
                }
            }
            // the mirrored lappets are mirror images at rest (x negated in the joint frame)
            var rest = FlowKit.Sim(sp, 1f);
            AttachedChainRuntime l = Chain(rest, "lappet.L"), rr = Chain(rest, "lappet.R");
            for (int k = 0; k < l.JointCount; k++)
            {
                Vector3 a = Local(rest, l, k, 1f), b = Local(rest, rr, k, 1f);
                Assert.Less((a - new Vector3(-b.x, b.y, b.z)).magnitude, 1e-4f, $"lappet joint {k} mirrored");
            }
            Assert.Less(Local(rest, l, l.JointCount - 1, 1f).x, -0.04f, "lappet.L hangs out to the left");
        }

        // A two-ended chain's ends sit square to their mother joints: its first bone's up is the start joint's (made
        // square to the chain) and its last bone's up the end joint's.
        internal static float EndFrameWorst()
        {
            CreatureSpecies sp = Species(name: "AttachedChainFrames");
            var sim = FlowKit.Sim(sp, 1f);
            AttachedChainRuntime p = Chain(sim, "pouch");
            float worst = 0f;
            Scenario(sp, sim, 1f, _ =>
            {
                for (int e = 0; e < 2; e++)
                {
                    int k = e == 0 ? 0 : p.JointCount - 1, j = e == 0 ? kPouchStart : kPouchEnd;
                    Vector3 t = p.Frame(k) * Vector3.forward, up = sim.Rotations[j] * Vector3.up;
                    up -= t * Vector3.Dot(up, t);
                    if (up.sqrMagnitude < 1e-4f) continue;
                    worst = Mathf.Max(worst, Vector3.Angle(p.Frame(k) * Vector3.up, up.normalized));
                }
            });
            return worst;
        }

        [Test] public void TwoEnded_Frames_SquareToBothJoints()
        {
            float worst = EndFrameWorst();
            TestContext.Out.WriteLine($"pouch end frames vs their joints' up: at most {worst:F3} deg");
            Assert.Less(worst, 0.5f);
        }

        // RED: without the twist spread the end bone is not square to the end joint.
        [Test] public void Red_NoTwistSpread_EndNotSquare()
        {
            AttachedChainRuntime.NoTwistSpreadForTest = true;
            float worst;
            try { worst = EndFrameWorst(); }
            finally { AttachedChainRuntime.NoTwistSpreadForTest = false; }
            TestContext.Out.WriteLine($"no twist spread: the end bone up to {worst:F2} deg off its joint's up");
            Assert.Greater(worst, 0.5f);
        }

        // ------------------------------------------------------------ one-ended: hangs, lags, swings, settles

        [Test] public void OneEnded_HangsBelow_LagsAndSwings_Settles()
        {
            CreatureSpecies sp = Species();
            float s = 1f;
            var sim = FlowKit.Sim(sp, s);
            AttachedChainRuntime w = Chain(sim, "wattle");
            // standing: it hangs below its joint
            sim.Halt = true;
            for (int t = 0; t < 180; t++) sim.Tick(kDt, sim.Joints[0]);
            Vector3 up = sim.Up[1];
            float below = Vector3.Dot(w.StartPoint - w.Joint(w.JointCount - 1), up);
            TestContext.Out.WriteLine($"standing: tip {below * 1000f:F1} mm below its root (length {w.Length * 1000f:F0} mm)");
            Assert.Greater(below, 0.7f * w.Length, "hangs below its joint");
            // walking and turning: the tip lags its carried rest place and swings
            sim.Halt = false;
            float lag = 0f, swing0 = float.MaxValue, swing1 = float.MinValue;
            Scenario(sp, sim, s, _ =>
            {
                int k = w.JointCount - 1;
                lag = Mathf.Max(lag, (w.Joint(k) - w.RestNow(k)).magnitude);
                Vector3 l = Local(sim, w, k, s);
                swing0 = Mathf.Min(swing0, l.z); swing1 = Mathf.Max(swing1, l.z);
            }, fly: false);
            // halt: it settles, no jitter
            sim.Halt = true;
            float lastMove = 0f;
            Vector3 prev = Local(sim, w, w.JointCount - 1, s);
            for (int t = 0; t < 360; t++)
            {
                sim.Tick(kDt, sim.Joints[0]);
                Vector3 l = Local(sim, w, w.JointCount - 1, s);
                if (t >= 300) lastMove = Mathf.Max(lastMove, (l - prev).magnitude);
                prev = l;
            }
            TestContext.Out.WriteLine($"moving: tip up to {lag * 1000f:F1} mm off its carried rest place, swinging {(swing1 - swing0) * 1000f:F1} mm fore-aft; settled: {lastMove * 1000f:F4} mm/frame in the last second");
            Assert.Greater(lag, 0.05f * w.Length, "the free end lags the mother's motion");
            Assert.Greater(swing1 - swing0, 0.1f * w.Length, "and swings");
            Assert.Less(lastMove, kSettled, "settles without jitter");
        }

        internal const float kSettled = 1e-4f;   // m per frame relative to its joint, in the last second of a 6 s halt

        // RED: the settle bound bites - an undamped, unsprung wattle is still swinging after the same halt.
        [Test] public void Red_UndampedChain_DoesNotSettle()
        {
            CreatureSpecies sp = Species(ps => { foreach (CreaturePart p in ps) if (p.id == "wattle") { ((AttachedChain)p).damping = 0f; ((AttachedChain)p).stiffnessHz = 0f; } }, "AttachedChainUndamped");
            var sim = FlowKit.Sim(sp, 1f);
            AttachedChainRuntime w = Chain(sim, "wattle");
            Scenario(sp, sim, 1f, _ => { }, fly: false);
            sim.Halt = true;
            float lastMove = 0f;
            Vector3 prev = Local(sim, w, w.JointCount - 1, 1f);
            for (int t = 0; t < 360; t++)
            {
                sim.Tick(kDt, sim.Joints[0]);
                Vector3 l = Local(sim, w, w.JointCount - 1, 1f);
                if (t >= 300) lastMove = Mathf.Max(lastMove, (l - prev).magnitude);
                prev = l;
            }
            TestContext.Out.WriteLine($"undamped: {lastMove * 1000f:F3} mm/frame in the last second");
            Assert.Greater(lastMove, kSettled);
        }

        // RED: the lag bound bites - a stiff, heavily damped wattle (12 Hz, no gravity) stays near its carried rest place.
        [Test] public void Red_StiffChain_DoesNotLag()
        {
            CreatureSpecies sp = Species(ps => { foreach (CreaturePart p in ps) if (p.id == "wattle") { var a = (AttachedChain)p; a.stiffnessHz = 10f; a.damping = 40f; a.gravity = 0f; } }, "AttachedChainStiff");
            var sim = FlowKit.Sim(sp, 1f);
            AttachedChainRuntime w = Chain(sim, "wattle");
            float lag = 0f;
            Scenario(sp, sim, 1f, _ => lag = Mathf.Max(lag, (w.Joint(w.JointCount - 1) - w.RestNow(w.JointCount - 1)).magnitude), fly: false);
            TestContext.Out.WriteLine($"stiff: tip up to {lag * 1000f:F2} mm off its rest place");
            Assert.Less(lag, 0.05f * w.Length);
        }

        // ------------------------------------------------------------ attached ends, lengths, bends

        internal struct Worst { public float ends, stretch, bendOver, endMove, taut; public string where, stretchWhere; }

        internal static Worst Measure(CreatureSpecies sp, float s)
        {
            var sim = FlowKit.Sim(sp, s);
            var chains = new List<AttachedChainRuntime>();
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is AttachedChainRuntime a) chains.Add(a);
            var w = new Worst { where = "" };
            var lastEnd = new Vector3[chains.Count];
            for (int i = 0; i < chains.Count; i++) lastEnd[i] = chains[i].EndPoint;
            Scenario(sp, sim, s, phase =>
            {
                for (int i = 0; i < chains.Count; i++)
                {
                    AttachedChainRuntime c = chains[i];
                    int n = c.JointCount;
                    float e = (c.Joint(0) - c.StartPoint).magnitude;
                    if (c.TwoEnded)
                    {
                        e = Mathf.Max(e, (c.Joint(n - 1) - c.EndPoint).magnitude);
                        w.endMove = Mathf.Max(w.endMove, (c.EndPoint - lastEnd[i]).magnitude);
                        w.taut = Mathf.Max(w.taut, (c.EndPoint - c.StartPoint).magnitude / c.Length);
                        lastEnd[i] = c.EndPoint;
                    }
                    if (e > w.ends) { w.ends = e; w.where = $"{c.Part.id} {phase}"; }
                    for (int k = 0; k + 1 < n; k++)
                    {
                        float st = Mathf.Abs((c.Joint(k + 1) - c.Joint(k)).magnitude / c.SegmentLength - 1f);
                        if (st > w.stretch) { w.stretch = st; w.stretchWhere = $"{c.Part.id} {phase} seg {k}"; }
                    }
                    for (int k = 1; k + 1 < n; k++)
                        w.bendOver = Mathf.Max(w.bendOver, Vector3.Angle(c.Joint(k) - c.Joint(k - 1), c.Joint(k + 1) - c.Joint(k)) - c.ConeDeg);
                }
            });
            return w;
        }

        [Test] public void AttachedEnds_StayOnTheirJoints_LengthsExact_BendsHeld()
        {
            CreatureSpecies sp = Species();
            Worst w = Measure(sp, 1f);
            TestContext.Out.WriteLine($"ends off their joints at most {w.ends * 1000f:F4} mm ({w.where}); the pouch's end joint moved up to {w.endMove * 1000f:F1} mm/frame; stretch at most {w.stretch * 100f:F4} % ({w.stretchWhere}); chord / length at most {w.taut:F3}; bend over the cone at most {w.bendOver:F3} deg");
            Assert.Less(w.ends, 1e-3f, "attached ends on their mother points (< 1 mm x s)");
            Assert.Less(w.stretch, 0.002f, "lengths exact (< 1 %; the FABRIK residual leaves < 0.2 %)");
            Assert.Less(w.bendOver, 0.5f, "bend cone held");
            Assert.Less(w.taut, 1f / 1.05f, "the two-ended chain never pulled within 5 % of taut (slack >= 0.1)");
        }

        // RED: the bounds bite - the mother end moves far more than 1 mm per frame (an end left behind for one frame
        // fails), and without the strict pass the solver's own chain stretches past 1 %.
        [Test] public void Red_EndsMove_AndNoStrictPassStretches()
        {
            CreatureSpecies sp = Species(name: "AttachedChainRed");
            AttachedChainRuntime.NoStrictForTest = true;
            Worst w;
            try { w = Measure(sp, 1f); }
            finally { AttachedChainRuntime.NoStrictForTest = false; }
            TestContext.Out.WriteLine($"no strict pass: end moved up to {w.endMove * 1000f:F1} mm/frame, stretch {w.stretch * 100f:F2} %, bend over {w.bendOver:F2} deg");
            Assert.Greater(w.endMove, 10f * 1e-3f, "the mother end moves > 10 mm in a frame");
            Assert.Greater(w.stretch, 0.01f, "the plain solve stretches > 1 %");
            Assert.Greater(w.bendOver, 0.5f, "the plain solve breaks the bend cone");
        }

        // ------------------------------------------------------------ keep-out

        // How deep q is inside the FlowBody's drawn surface (FlowBody.Section3, measured here independently of the
        // runtime's table): for each mother segment q lies along (past either end of the chain: the end joint), q's
        // point in the section plane (the joints' right / up lerped) against the section polygon (360 samples of
        // Section3 at that body fraction) along q's own polar angle. Metres; 0 = outside.
        internal static float SectionDepth(CreatureSim sim, FlowBody body, Vector3 q)
        {
            Vector3[] J = sim.Joints;
            float s = sim.SizeScale, worst = 0f;
            for (int j = 0; j + 1 < J.Length; j++)
            {
                Vector3 a = J[j], ab = J[j + 1] - a;
                float l2 = ab.sqrMagnitude;
                float t = Vector3.Dot(q - a, ab) / l2;
                if (t < 0f) { if (j > 0) continue; t = 0f; }
                if (t > 1f) { if (j + 2 < J.Length) continue; t = 1f; }
                Vector3 c = a + ab * t, ax = ab / Mathf.Sqrt(l2);
                Vector3 rt = Vector3.Lerp(sim.Rotations[j] * Vector3.right, sim.Rotations[j + 1] * Vector3.right, t);
                Vector3 up = Vector3.Lerp(sim.Rotations[j] * Vector3.up, sim.Rotations[j + 1] * Vector3.up, t);
                rt -= ax * Vector3.Dot(rt, ax); rt.Normalize();
                up -= ax * Vector3.Dot(up, ax) + rt * Vector3.Dot(up, rt); up.Normalize();
                float x = Vector3.Dot(q - c, rt) / s, y = Vector3.Dot(q - c, up) / s, r = Mathf.Sqrt(x * x + y * y);
                if (r > 0.6f) continue;   // far outside any section of the fixture
                var dir = new Vector2(x, y) / Mathf.Max(r, 1e-9f);
                float u = body.UAtJoint(j + t), surf = 0f;
                Vector3 p0 = body.Section3(u, -Mathf.PI);
                for (int i = 1; i <= 360; i++)
                {
                    Vector3 p1 = body.Section3(u, -Mathf.PI + 2f * Mathf.PI * i / 360f);
                    // the ray from the centre along dir against the polygon edge p0 p1
                    Vector2 e0 = new Vector2(p0.x, p0.y), e = new Vector2(p1.x - p0.x, p1.y - p0.y);
                    float den = dir.x * e.y - dir.y * e.x;
                    if (Mathf.Abs(den) > 1e-12f)
                    {
                        float k = (e0.x * e.y - e0.y * e.x) / den, m = (e0.x * dir.y - e0.y * dir.x) / den;
                        if (k > 0f && m >= -1e-4f && m <= 1f + 1e-4f) surf = Mathf.Max(surf, k);
                    }
                    p0 = p1;
                }
                worst = Mathf.Max(worst, (surf - r) * s);
            }
            return worst;
        }

        internal static FlowBody BodyOf(CreatureSpecies sp)
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is FlowBody f) return f;
            return null;
        }

        // A one-ended chain rooted under the jaw whose rest shape runs up through the neck (it is pulled into the body),
        // and the pouch (two-ended, hanging under the neck to the chest), both kept out: no free joint inside the drawn
        // body (Section3, hump included) through the scenario (every 4th tick).
        internal static float BodyIntrusion(float collideRadius, out float restDepth)
        {
            CreatureSpecies sp = Species(ps =>
            {
                foreach (CreaturePart p in ps)
                {
                    if (p.id == "wattle") { var a = (AttachedChain)p; a.at.offset = new Vector3(0f, -0.06f, 0f); a.direction = new Vector3(0f, 1f, 0.3f); a.curlDeg = 0f; a.collideRadius = collideRadius; }
                    if (p.id == "pouch") ((AttachedChain)p).collideRadius = collideRadius;
                }
            }, "AttachedChainKeepOut" + collideRadius);
            FlowBody body = BodyOf(sp);
            var sim = FlowKit.Sim(sp, 1f);
            AttachedChainRuntime w = Chain(sim, "wattle"), pouch = Chain(sim, "pouch");
            float rd = 0f;
            for (int k = 1; k < w.JointCount; k++) rd = Mathf.Max(rd, SectionDepth(sim, body, w.RestNow(k)));
            restDepth = rd;
            float worst = 0f;
            int tick = 0;
            Scenario(sp, sim, 1f, _ =>
            {
                if (tick++ % 4 != 0) return;
                for (int k = 1; k < w.JointCount; k++) worst = Mathf.Max(worst, SectionDepth(sim, body, w.Joint(k)));
                for (int k = 1; k < pouch.JointCount - 1; k++) worst = Mathf.Max(worst, SectionDepth(sim, body, pouch.Joint(k)));
            });
            return worst;
        }

        [Test] public void KeepOut_NeverInsideTheMotherBody()
        {
            float worst = BodyIntrusion(0.01f, out float restDepth);
            TestContext.Out.WriteLine($"the wattle's rest shape up to {restDepth * 1000f:F1} mm inside the body; kept out: at most {worst * 1000f:F3} mm inside");
            Assert.Greater(restDepth, 0.02f, "the rest shape runs through the neck (the test bites)");
            Assert.Less(worst, 1e-3f, "kept outside the drawn body (< 1 mm)");
        }

        // RED: without the keep-out the same chain sinks into the body.
        [Test] public void Red_NoKeepOut_SinksIntoTheBody()
        {
            float worst = BodyIntrusion(0f, out _);
            TestContext.Out.WriteLine($"no keep-out: up to {worst * 1000f:F1} mm inside");
            Assert.Greater(worst, 1e-2f);
        }

        // Keel ON (the FlowBody fixture's 8 cm chest keel): a pouch hung from under the jaw to the keel's foot, its rest
        // bulging forward and up toward the chest, kept outside the DRAWN section (Section3) - the hump, the keel.
        internal static float KeelIntrusion()
        {
            CreatureSpecies probe = Species(name: "AttachedChainKeelProbe", keel: true);
            FlowBody b = BodyOf(probe);
            float keelFoot = b.Section3(b.UAtJoint(kPouchEnd), -0.5f * Mathf.PI).y;
            CreatureSpecies sp = Species(ps =>
            {
                foreach (CreaturePart p in ps)
                    if (p.id == "pouch")
                    {
                        var a = (AttachedChain)p; a.endOffset = new Vector3(0f, keelFoot - 0.005f, 0f); a.direction = new Vector3(0f, 0.3f, 1f); a.slack = 0.3f;
                        a.stiffnessHz = 3f;
                    }
            }, "AttachedChainKeel" + (AttachedChain.EllipseKeepOutForTest ? "Ellipse" : ""), keel: true);
            FlowBody body = BodyOf(sp);
            var sim = FlowKit.Sim(sp, 1f);
            AttachedChainRuntime pouch = Chain(sim, "pouch");
            float worst = 0f;
            int tick = 0;
            Scenario(sp, sim, 1f, _ =>
            {
                if (tick++ % 4 != 0) return;
                for (int k = 1; k < pouch.JointCount - 1; k++) worst = Mathf.Max(worst, SectionDepth(sim, body, pouch.Joint(k)));
            });
            TestContext.Out.WriteLine($"keel foot at y {keelFoot * 1000f:F0} mm; pouch joints at most {worst * 1000f:F3} mm inside the drawn section");
            return worst;
        }

        [Test] public void KeepOut_KeelOn_OutsideTheDrawnSection()
        {
            Assert.Less(KeelIntrusion(), 1e-3f);
        }

        // RED: the joints' ellipses (no hump, no keel) let the pouch into the keel.
        [Test] public void Red_EllipseKeepOut_EntersTheKeel()
        {
            AttachedChain.EllipseKeepOutForTest = true;
            float worst;
            try { worst = KeelIntrusion(); }
            finally { AttachedChain.EllipseKeepOutForTest = false; }
            Assert.Greater(worst, 5e-3f);
        }

        // ------------------------------------------------------------ rest from the bind, snap on a jump

        // A plane rising `slope` (tan) along +x.
        sealed class SlopeGround : IGroundProbe
        {
            readonly float slope; readonly Vector3 n;
            public SlopeGround(float slope) { this.slope = slope; n = new Vector3(-slope, 1f, 0f).normalized; }
            public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
            {
                point = new Vector3(near.x, slope * near.x, near.z); normal = n; return true;
            }
        }

        // The pouch of a flat-spawned individual vs one spawned uphill on a 0.6 slope: length and rest shape apart (m).
        internal static float SpawnDifference(out float lenFlat, out float lenSlope)
        {
            CreatureSpecies sp = Species(name: "AttachedChainSpawn");
            var flat = FlowKit.Sim(sp, 1f);
            var slope = new CreatureSim(sp, new SlopeGround(0.6f), _ => Vector3.up, new Vector3(1f, 0.6f, 0f), Vector3.right, 1f, 2);
            AttachedChainRuntime a = Chain(flat, "pouch"), b = Chain(slope, "pouch");
            lenFlat = a.Length; lenSlope = b.Length;
            float d = Mathf.Abs(a.Length - b.Length);
            for (int k = 0; k < a.JointCount; k++) d = Mathf.Max(d, (a.RestLocal(k) - b.RestLocal(k)).magnitude);
            return d;
        }

        [Test] public void Rest_FromTheBind_SameForEverySpawn()
        {
            float d = SpawnDifference(out float a, out float b);
            TestContext.Out.WriteLine($"pouch length flat {a * 1000f:F2} mm, uphill {b * 1000f:F2} mm; rest shapes at most {d * 1000f:F4} mm apart");
            Assert.Less(d, 1e-6f);
        }

        // RED: the rest taken from each spawn pose (the old way) differs on a slope.
        [Test] public void Red_SpawnRest_DiffersOnASlope()
        {
            AttachedChain.SpawnRestForTest = true;
            float d;
            try { d = SpawnDifference(out _, out _); }
            finally { AttachedChain.SpawnRestForTest = false; }
            TestContext.Out.WriteLine($"spawn rest: {d * 1000f:F2} mm apart");
            Assert.Greater(d, 1e-3f);
        }

        // A teleport (every mother joint 10 m away in one tick) snaps the chains onto their carried rest shapes, at rest:
        // the worst chain joint's distance from its carried rest place and its speed after the jump (m, m/tick).
        internal static void Teleport(out float off, out float speed, out int snaps)
        {
            CreatureSpecies sp = Species(name: "AttachedChainTeleport");
            var sim = FlowKit.Sim(sp, 1f);
            FlowKit.Run(sim, 0.5f, Ahead(sim, 1f));
            for (int j = 0; j < sim.Joints.Length; j++) sim.Joints[j] += new Vector3(10f, 0f, 0f);
            off = 0f; speed = 0f; snaps = 0;
            foreach (IPartRuntime r in sim.PartRuntimes)
            {
                if (!(r is AttachedChainRuntime c)) continue;
                c.Step(new StepCtx(sim), kDt);
                snaps += c.Snaps;
                for (int k = 0; k < c.JointCount; k++)
                {
                    off = Mathf.Max(off, (c.Joint(k) - c.RestNow(k)).magnitude);
                    speed = Mathf.Max(speed, (c.X[k] - c.Prev[k]).magnitude);
                }
            }
        }

        [Test] public void Teleport_SnapsOntoTheRestShape()
        {
            Teleport(out float off, out float speed, out int snaps);
            TestContext.Out.WriteLine($"after a 10 m jump: {snaps} snaps, joints at most {off * 1000f:F3} mm off their rest places, moving {speed * 1000f:F3} mm/tick");
            Assert.AreEqual(4, snaps, "every chain snapped");
            Assert.Less(off, 0.02f, "on its carried rest shape (the two-ended one made exact: within 2 cm)");
            Assert.Less(speed, 1e-6f, "at rest");
        }

        // RED: without the snap the chain is dragged across the jump (its joints far from their places).
        [Test] public void Red_NoSnap_FlingsTheChain()
        {
            AttachedChainRuntime.NoSnapForTest = true;
            float off, speed;
            try { Teleport(out off, out speed, out _); }
            finally { AttachedChainRuntime.NoSnapForTest = false; }
            TestContext.Out.WriteLine($"no snap: joints up to {off:F2} m off, moving {speed:F2} m/tick");
            Assert.Greater(off, 0.1f);
        }

        // ------------------------------------------------------------ size rule

        // Every chain joint in its start joint's frame, / size, after the walk-and-turn scenario in Froude time.
        internal static Vector3[] Shape(float k)
        {
            CreatureSpecies sp = Species(name: "AttachedChainSize");
            var sim = FlowKit.Sim(sp, k);
            Scenario(sp, sim, k, _ => { }, fly: false);
            var o = new List<Vector3>();
            foreach (IPartRuntime r in sim.PartRuntimes)
                if (r is AttachedChainRuntime c) for (int j = 0; j < c.JointCount; j++) o.Add(Local(sim, c, j, k));
            return o.ToArray();
        }

        internal static float ShapeDiff(Vector3[] a, Vector3[] b)
        {
            float d = 0f;
            for (int i = 0; i < a.Length; i++) d = Mathf.Max(d, (a[i] - b[i]).magnitude);
            return d;
        }

        internal const float kFroudeTol = 0.002f;   // m at scale 1 (1 % of the wattle's length)

        [Test] public void AttachedChain_Size_FroudeSimilar()
        {
            Vector3[] a = Shape(0.9f), b = Shape(1.1f);
            float d = ShapeDiff(a, b);
            TestContext.Out.WriteLine($"0.9x vs 1.1x, every chain joint in its joint's frame / size: at most {d * 1000f:F2} mm apart");
            Assert.Less(d, kFroudeTol);
        }

        // RED: rates not Froude-scaled (stiffness and damping in plain Hz) - the two sizes swing differently.
        [Test] public void Red_NotFroudeScaled_Differs()
        {
            AttachedChainRuntime.NoFroudeForTest = true;
            float d;
            try { d = ShapeDiff(Shape(0.9f), Shape(1.1f)); }
            finally { AttachedChainRuntime.NoFroudeForTest = false; }
            TestContext.Out.WriteLine($"not Froude-scaled: {d * 1000f:F2} mm apart");
            Assert.Greater(d, kFroudeTol);
        }

        // ------------------------------------------------------------ GC, Validate, F3

        sealed class Recorder : ICreatureDebugDraw
        {
            public int lines, points;
            public readonly List<Vector3> at = new List<Vector3>();
            public readonly List<float> radii = new List<float>();
            public bool record;
            public void Line(Vector3 a, Vector3 b, Color c) => lines++;
            public void Point(Vector3 p, float r, Color c) { points++; if (record) { at.Add(p); radii.Add(r); } }
        }

        [Test] public void AttachedChain_NoGC()
        {
            CreatureSpecies sp = Species();
            float s = 1f, T = 1f;
            var sim = FlowKit.Sim(sp, s);
            var bones = new Matrix4x4[sp.BoneCount];
            var rec = new Recorder();
            Vector3 lo = Vector3.zero, hi = Vector3.zero;
            void Frame()
            {
                sim.WriteBoneWorld(bones, 0);
                foreach (IPartRuntime r in sim.PartRuntimes) { r.DrawDebug(rec); r.EncapsulateBounds(ref lo, ref hi); }
            }
            for (int t = 0; t < 60; t++) { sim.Tick(kDt, Ahead(sim, s)); Frame(); }
            GcAllocs.AssertCounterWorks();
            int ground = GcAllocs.Count(() => { for (int t = 0; t < 60; t++) { sim.Tick(kDt, Ahead(sim, s)); Frame(); } });
            Assert.AreEqual(0, ground, "ground");
            Assert.IsTrue(sim.Flight.Launch(6f * s));
            for (int t = 0; t < 120; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f); sim.Tick(kDt, sim.Joints[0]); Frame(); }
            int air = GcAllocs.Count(() => { for (int t = 0; t < 60; t++) { sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 30f); sim.Tick(kDt, sim.Joints[0]); Frame(); } });
            Assert.AreEqual(0, air, "air");
        }

        [Test] public void AttachedChain_Validate_RejectsBadData()
        {
            var ctx = new SpeciesCtx(new CreaturePart[] { Wattle(), Pouch() }, new CreatureModule[] { new SkeletonOnly() }, 24, false);
            Assert.IsNull(Wattle().Validate(ctx));
            Assert.IsNull(Pouch().Validate(ctx));
            AttachedChain Bad(AttachedChain ok, System.Action<AttachedChain> f) { var b = (AttachedChain)ok.Clone(); f(b); return b; }
            // an end before the start is allowed (a chain hung from the chest forward to the jaw)
            Assert.IsNull(Bad(Pouch(), b => { b.at.joint = 6; b.endJoint = 1; }).Validate(ctx), "end before start allowed");
            Assert.IsNull(Bad(Pouch(), b => { b.endJoint = b.at.joint; b.endOffset = b.at.offset + new Vector3(0f, 0f, -0.05f); }).Validate(ctx), "both ends on one joint, apart");
            Assert.IsNotNull(Bad(Wattle(), b => b.at.joint = 24).Validate(ctx), "start joint out of range");
            Assert.IsNotNull(Bad(Wattle(), b => b.at.joint = -1).Validate(ctx), "start joint negative");
            Assert.IsNotNull(Bad(Pouch(), b => b.endJoint = 24).Validate(ctx), "end joint out of range");
            Assert.IsNotNull(Bad(Pouch(), b => b.endJoint = -2).Validate(ctx), "end joint below -1");
            Assert.IsNotNull(Bad(Pouch(), b => { b.endJoint = b.at.joint; b.endOffset = b.at.offset; }).Validate(ctx), "both ends at one point");
            Assert.IsNotNull(Bad(Pouch(), b => b.slack = 0f).Validate(ctx), "slack");
            Assert.IsNotNull(Bad(Pouch(), b => b.slack = 0.05f).Validate(ctx), "slack below the 0.1 floor (taut trap)");
            Assert.IsNotNull(Bad(Pouch(), b => b.length = -1f).Validate(ctx), "length < 0");
            Assert.IsNotNull(Bad(Pouch(), b => b.length = 0.5f).Validate(ctx), "two-ended: length comes from slack, not both");
            Assert.AreEqual(0f, new AttachedChain().length, "length defaults to 0 (slack for two-ended)");
            Assert.IsNotNull(Bad(Wattle(), b => b.length = 0f).Validate(ctx), "one-ended needs a length");
            Assert.IsNotNull(Bad(Wattle(), b => b.segments = 1).Validate(ctx), "segments");
            Assert.IsNotNull(Bad(Wattle(), b => b.segments = 25).Validate(ctx), "segments");
            Assert.IsNotNull(Bad(Wattle(), b => b.radii = new[] { 0.01f, 0.01f }).Validate(ctx), "radii count");
            Assert.IsNull(Bad(Wattle(), b => b.radii = new[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.005f }).Validate(ctx), "radii: segments + 1");
            Assert.IsNotNull(Bad(Wattle(), b => b.rootRadius = 0f).Validate(ctx), "rootRadius");
            Assert.IsNotNull(Bad(Wattle(), b => b.direction = Vector3.zero).Validate(ctx), "direction");
            Assert.IsNotNull(Bad(Wattle(), b => b.stiffnessHz = -1f).Validate(ctx), "stiffness");
            Assert.IsNotNull(Bad(Wattle(), b => b.damping = -1f).Validate(ctx), "damping");
            Assert.IsNotNull(Bad(Wattle(), b => b.gravity = 1.5f).Validate(ctx), "gravity");
            Assert.IsNotNull(Bad(Wattle(), b => b.maxBendDeg = 2f).Validate(ctx), "maxBendDeg");
            Assert.IsNotNull(Bad(Wattle(), b => b.collideRadius = -0.01f).Validate(ctx), "collideRadius");
            var soft = new SpeciesCtx(new CreaturePart[] { Wattle() }, new CreatureModule[0], 0, true);
            Assert.IsNotNull(Wattle().Validate(soft), "a soft body");
            // the problem names the part
            StringAssert.Contains("wattle", Bad(Wattle(), b => b.at.joint = 99).Validate(ctx));
            // Clone copies the radii
            var w = Wattle(); w.radii = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f };
            var cl = (AttachedChain)w.Clone(); cl.radii[0] = 2f;
            Assert.AreEqual(1f, w.radii[0], "CopyArrays clones radii");
        }

        [Test] public void AttachedChain_DrawDebug_DrawsEveryJoint()
        {
            CreatureSpecies sp = Species();
            var sim = FlowKit.Sim(sp, 1.2f);
            FlowKit.Run(sim, 1f, Ahead(sim, 1.2f));
            foreach (IPartRuntime r in sim.PartRuntimes)
            {
                if (!(r is AttachedChainRuntime c)) continue;
                var rec = new Recorder { record = true };
                c.DrawDebug(rec);
                Assert.GreaterOrEqual(rec.lines, c.JointCount - 1, $"{c.Part.id}: a line per segment");
                for (int k = 0; k < c.JointCount; k++)
                {
                    bool hit = false;
                    for (int i = 0; i < rec.at.Count && !hit; i++)
                        hit = (rec.at[i] - c.Joint(k)).magnitude < 1e-6f && Mathf.Abs(rec.radii[i] - c.Radius(k)) < 1e-7f;
                    Assert.IsTrue(hit, $"{c.Part.id} joint {k} drawn at its radius");
                    Assert.AreEqual(c.Part.JointRadius(k) * 1.2f, c.Radius(k), 1e-6f, "radius x size");
                }
            }
        }
    }
}
