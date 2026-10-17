using System;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Tree octopus phase A (design 2026-10-17-tree-octopus-design.md, tasks 2 and 3): the arm chain and the
    // soft body. Engine-call free (OctoMath instead of Quaternion.Euler etc.), so they also run off-Editor.
    public class OctopusSimTests
    {
        const float Dt = 1f / 60f;

        static TentacleChain NewChain(out TentacleSpec spec)
        {
            spec = TentacleSpec.Default;
            return new TentacleChain(spec, Vector3.zero, Quaternion.identity);
        }

        // A deterministic, deliberately rough root motion: drift, sway and occasional jerks.
        static void ShakeRoot(TentacleChain c, int f)
        {
            float t = f * Dt;
            Vector3 p = new Vector3(0.25f * Mathf.Sin(1.3f * t), 0.15f * Mathf.Sin(2.1f * t + 1f), 0.2f * Mathf.Sin(0.7f * t));
            if (f % 97 < 3) p += new Vector3(0.08f, -0.05f, 0.04f) * (f % 97); // jerk
            float yaw = 0.8f * Mathf.Sin(0.9f * t), pitch = 0.4f * Mathf.Sin(1.7f * t);
            Quaternion q = OctoMath.Mul(OctoMath.AxisAngle(Vector3.up, yaw), OctoMath.AxisAngle(Vector3.right, pitch));
            c.SetRoot(p, q);
        }

        static float SegStretch(TentacleChain c, int k) => (c.X[k + 1] - c.X[k]).magnitude / c.Spacing;

        // Angle (deg) at joint j: segment j-1 (Back -> root for j = 0) to segment j.
        static float JointBend(TentacleChain c, int j)
        {
            Vector3 a = j == 0 ? c.Back : c.X[j - 1];
            return Vector3.Angle(c.X[j] - a, c.X[j + 1] - c.X[j]);
        }

        static void AssertFinite(TentacleChain c, string msg)
        {
            for (int k = 0; k < c.N; k++) Assert.IsTrue(OctoMath.Finite(c.X[k]), $"{msg}: X[{k}] = {c.X[k]}");
            for (int k = 0; k < c.N; k++)
            {
                Quaternion q = c.Frames[k];
                Assert.IsFalse(float.IsNaN(q.x + q.y + q.z + q.w), $"{msg}: frame {k}");
            }
        }

        // ------------------------------------------------------------------ task 2: TentacleChain

        // Segment lengths stay in [minStretch, maxStretch] x spacing through rough root motion, muscle tone
        // changes, a travelling sinus and an out-of-reach strike target (which asks for maximum stretch).
        [Test] public void Tentacle_LengthsWithinStretch()
        {
            var c = NewChain(out var spec);
            float lo = float.MaxValue, hi = 0f;
            for (int f = 0; f < 60 * 12; f++)
            {
                ShakeRoot(c, f);
                if (f == 120) { c.SinusAmount = 1f; c.SetTargetShape(new Vector3(0.6f, -0.4f, 1.2f), 1f, false); }
                if (f == 300) c.SetTargetShape(new Vector3(0f, 0.5f, 3f), 1f, true); // beyond reach: s -> max
                if (f == 420) { c.ClearTargetShape(); c.SinusAmount = 0f; }
                if (f == 540) c.SetLimp(true);
                c.Tick(Dt);
                for (int k = 0; k < c.N - 1; k++) { float s = SegStretch(c, k); lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s); }
            }
            TestContext.Out.WriteLine($"segment stretch range [{lo:F4}, {hi:F4}]");
            Assert.GreaterOrEqual(lo, spec.MinStretch - 1e-3f, "segment shorter than minStretch");
            Assert.LessOrEqual(hi, spec.MaxStretch + 1e-3f, "segment longer than maxStretch");
            Assert.Greater(hi, 1.2f, "the strike target never stretched the arm: test does not bite");
        }

        // No joint bends past the cone (28 deg) on a free arm, even when muscles, gravity and a whipping
        // root all push it there.
        [Test] public void Tentacle_BendCone()
        {
            var c = NewChain(out var spec);
            float worst = 0f;
            for (int f = 0; f < 60 * 10; f++)
            {
                ShakeRoot(c, f);
                if (f == 200) c.SinusAmount = 1f;
                if (f == 400) c.SetLimp(true);
                c.Tick(Dt);
                for (int j = 0; j < c.N - 1; j++) worst = Mathf.Max(worst, JointBend(c, j));
            }
            TestContext.Out.WriteLine($"worst joint bend {worst:F3} deg (limit {spec.BendLimitDeg})");
            Assert.LessOrEqual(worst, spec.BendLimitDeg + 0.05f);
            Assert.Greater(worst, 0.8f * spec.BendLimitDeg, "the chain never neared its bend limit: test does not bite");
        }

        // The free tail past a pin also keeps the cone exactly (the strict tail pass starts at the last pin).
        [Test] public void Tentacle_BendCone_TailPastPin()
        {
            var c = NewChain(out var spec);
            c.SetLimp(true);
            c.Pin(12, c.X[12], Vector3.up);
            float worst = 0f;
            for (int f = 0; f < 60 * 6; f++)
            {
                c.Tick(Dt);
                for (int j = 12; j < c.N - 1; j++) worst = Mathf.Max(worst, JointBend(c, j));
            }
            Assert.LessOrEqual(worst, spec.BendLimitDeg + 0.05f);
            for (int k = 12; k < c.N - 1; k++) Assert.That(SegStretch(c, k), Is.InRange(spec.MinStretch - 1e-3f, spec.MaxStretch + 1e-3f));
        }

        // Twist between consecutive frames about the segment never exceeds 20 deg, while a pin asks for the
        // suckers to turn all the way around (pin normal opposite to the untwisted dorsal side) and the free
        // particles twist toward a body axis. The pinned frame does reach its target.
        [Test] public void Tentacle_TwistPerSegment()
        {
            var c = NewChain(out var spec);
            c.AxisOrigin = new Vector3(0f, 0.3f, 0f); c.AxisDir = Vector3.forward; // free: dorsal away from this axis
            int pin = 14;
            c.Pin(pin, c.X[pin], Vector3.down); // suckers up: 180 deg from the rest frame
            float worst = 0f;
            for (int f = 0; f < 60 * 6; f++)
            {
                ShakeRoot(c, f);
                c.Tick(Dt);
                for (int k = 1; k < c.N; k++) worst = Mathf.Max(worst, Mathf.Abs(RelativeTwistDeg(c.Frames[k - 1], c.Frames[k])));
                worst = Mathf.Max(worst, Mathf.Abs(RelativeTwistDeg(c.RootFrame, c.Frames[0])));
            }
            TestContext.Out.WriteLine($"worst twist between frames {worst:F3} deg");
            Assert.LessOrEqual(worst, spec.MaxTwistDeg + 0.5f, "twist per segment over the limit");
            // the twist turns the dorsal side onto the pin normal as far as the segment allows: compare with the
            // normal made perpendicular to the segment
            Vector3 dorsal = OctoMath.Rotate(c.Frames[pin], Vector3.up), along = OctoMath.Rotate(c.Frames[pin], Vector3.forward);
            Vector3 want = Vector3.ProjectOnPlane(Vector3.down, along);
            Assert.Less(Vector3.Angle(dorsal, want), 3f, "the pinned segment's dorsal side does not face away from the surface");
            Assert.Greater(worst, 0.5f * spec.MaxTwistDeg, "twist never neared its limit: test does not bite");
        }

        // Twist (deg) of b relative to a about b's own +Z (swing-twist decomposition of a^-1 b).
        internal static float RelativeTwistDeg(Quaternion a, Quaternion b)
        {
            Quaternion r = OctoMath.Mul(OctoMath.Conj(a), b);
            Vector3 z = OctoMath.Rotate(r, Vector3.forward);
            // remove the swing (shortest rotation taking +Z to z), what remains is the twist about +Z
            Vector3 axis = OctoMath.Cross(Vector3.forward, z);
            float s = axis.magnitude, cs = Mathf.Clamp(z.z, -1f, 1f);
            Quaternion swing = s > 1e-7f ? OctoMath.AxisAngle(axis / s, Mathf.Atan2(s, cs)) : Quaternion.identity;
            Quaternion twist = OctoMath.Mul(OctoMath.Conj(swing), r);
            float ang = 2f * Mathf.Atan2(twist.z, twist.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, ang);
        }

        [Test] public void Tentacle_Deterministic()
        {
            var a = NewChain(out _); var b = NewChain(out _);
            for (int f = 0; f < 60 * 4; f++)
            {
                ShakeRoot(a, f); ShakeRoot(b, f);
                if (f == 60) { a.SinusAmount = b.SinusAmount = 1f; a.SetTargetShape(Vector3.one, 0.7f, true); b.SetTargetShape(Vector3.one, 0.7f, true); }
                a.Tick(Dt); b.Tick(Dt);
            }
            for (int k = 0; k < a.N; k++)
            {
                Assert.AreEqual(a.X[k].x, b.X[k].x); Assert.AreEqual(a.X[k].y, b.X[k].y); Assert.AreEqual(a.X[k].z, b.X[k].z);
                Assert.AreEqual(a.Twist[k], b.Twist[k]);
            }
        }

        // A minute of abuse: frame hitches (0.25 s), zero and NaN dt, teleports of the root, pins appearing and
        // vanishing. Nothing becomes NaN, the bounds still hold, and the bones stay finite.
        [Test] public void Tentacle_LongRun_NoNaN()
        {
            var c = NewChain(out var spec);
            var bones = new Matrix4x4[c.N];
            var rng = new System.Random(7);
            for (int f = 0; f < 60 * 60; f++)
            {
                ShakeRoot(c, f);
                if (f % 600 == 300) c.SetRoot(c.X[0] + new Vector3(3f, -2f, 1f), c.RootFrame); // teleport
                if (f % 450 == 100) c.Pin(5 + rng.Next(15), c.X[10], new Vector3(0.3f, 1f, 0.1f));
                if (f % 450 == 300) c.UnpinAll();
                if (f % 200 == 50) c.SetTargetShape(c.X[0] + new Vector3((float)rng.NextDouble() * 4f - 2f, 1f, 2f), (float)rng.NextDouble(), true);
                float dt = f % 500 == 499 ? 0.25f : f % 333 == 0 ? 0f : f % 777 == 0 ? float.NaN : Dt;
                c.SinusAmount = (f / 300) % 2;
                c.Tick(dt);
                AssertFinite(c, "frame " + f);
                c.WriteBones(bones, 0);
                for (int b = 0; b < bones.Length; b++)
                    Assert.IsFalse(float.IsNaN(bones[b].m00 + bones[b].m11 + bones[b].m22 + bones[b].m03), $"bone {b} frame {f}");
            }
            int last = 0;
            for (int k = c.N - 1; k > 0; k--) if (c.Pinned[k]) { last = k; break; }
            for (int k = last; k < c.N - 1; k++) Assert.That(SegStretch(c, k), Is.InRange(spec.MinStretch - 1e-3f, spec.MaxStretch + 1e-3f));
        }

        [Test] public void Tentacle_PinsHoldExactly()
        {
            var c = NewChain(out _);
            Vector3 p = c.X[16] + new Vector3(0f, -0.1f, 0f);
            c.Pin(16, p, Vector3.up);
            for (int f = 0; f < 240; f++) { ShakeRoot(c, f); c.Tick(Dt); Assert.AreEqual(0f, (c.X[16] - p).magnitude, 1e-6f); }
        }

        // FABRIK gives the intent: with the muscles on and no gravity, the tip goes to a reachable target.
        [Test] public void Tentacle_TargetShape_TipReaches()
        {
            var c = NewChain(out var spec);
            c.Gravity = Vector3.zero;
            Vector3 target = new Vector3(0.7f, 0.5f, 1.0f);
            for (int f = 0; f < 60 * 4; f++) { c.SetTargetShape(target, 1f, false); c.Tick(Dt); }
            float miss = (c.X[c.N - 1] - target).magnitude;
            TestContext.Out.WriteLine($"tip miss {miss:F3} m");
            Assert.Less(miss, 0.15f * spec.Length);
        }

        // Bone k: +Z along segment k, scale (sigma, sigma, s) with sigma^2 s = squeeze^2 (volume-preserving stretch).
        [Test] public void Tentacle_BonesFollowSegments_VolumePreservingScale()
        {
            var c = NewChain(out _);
            for (int f = 0; f < 120; f++) { c.SetTargetShape(new Vector3(0f, 0f, 3f), 1f, true); c.Tick(Dt); }
            var m = new Matrix4x4[c.N];
            c.WriteBones(m, 0);
            for (int k = 0; k < c.N - 1; k++)
            {
                Vector3 z = new Vector3(m[k].m02, m[k].m12, m[k].m22);
                Vector3 seg = c.X[k + 1] - c.X[k];
                Assert.Less(Vector3.Angle(z, seg), 0.5f, "bone " + k);
                Assert.AreEqual(seg.magnitude / c.Spacing, z.magnitude, 1e-3f, "bone z scale = stretch, bone " + k);
                Vector3 x = new Vector3(m[k].m00, m[k].m10, m[k].m20), y = new Vector3(m[k].m01, m[k].m11, m[k].m21);
                float det = Vector3.Dot(Vector3.Cross(x, y), z);
                Assert.AreEqual(c.Squeeze[k] * c.Squeeze[k], det, 2e-3f, "det = squeeze^2, bone " + k);
            }
        }

        // ------------------------------------------------------------------ task 3: SoftLattice + OctopusBody

        static CephalopodSpec NoBreath() { var sp = CephalopodSpec.Default; sp.breathAmp = -1f; return sp; }

        static void Settle(OctopusBody b, float seconds)
        {
            b.ExtraDamping = 4f;
            for (int f = 0; f < (int)(60 * seconds); f++) b.Tick(Dt);
            b.ExtraDamping = 0f;
        }

        // Limp arms, no contact: the whole animal free-falls at g (damping is relative to the body and
        // cannot slow the fall).
        [Test] public void Body_DrivenByChains_LimpFreeFall()
        {
            var b = new OctopusBody(CephalopodSpec.Default, new Vector3(0f, 20f, 0f), Quaternion.identity);
            b.SetAllLimp(true);
            Vector3 c0 = b.CentreOfMass();
            int frames = 30;
            for (int f = 0; f < frames; f++) b.Tick(Dt);
            float t = frames * Dt, dy = b.CentreOfMass().y - c0.y, a = -2f * dy / (t * t);
            TestContext.Out.WriteLine($"effective acceleration {a:F3} m/s^2 over {t:F2} s");
            Assert.AreEqual(9.81f, a, 0.03f * 9.81f);
        }

        // One arm pinned at its tip, the others limp: the body hangs below the pin and swings like the
        // compound pendulum its mass distribution predicts (T = 2 pi sqrt(I / (M g d))), within 25 %.
        [Test] public void Body_DrivenByChains_PendulumPeriod()
        {
            var b = new OctopusBody(NoBreath(), Vector3.zero, Quaternion.identity);
            b.SetAllLimp(true);
            TentacleChain arm = b.Arms[0];
            int tip = arm.N - 1;
            Vector3 pivot = arm.X[tip];
            arm.Pin(tip, pivot, Vector3.up);
            Settle(b, 25f);

            Vector3 com = b.CentreOfMass();
            Assert.Less(com.y, pivot.y - 0.8f * arm.Length, "the body does not hang from the pinned arm");
            double I = 0, M = 0;
            void Acc(Vector3 p, float m) { double dx = p.x - pivot.x, dy = p.y - pivot.y; I += m * (dx * dx + dy * dy); M += m; }
            for (int i = 0; i < SoftLattice.Count; i++) Acc(b.Lattice.X[i], b.Lattice.Mass[i]);
            foreach (TentacleChain ch in b.Arms) for (int k = 1; k < ch.N; k++) Acc(ch.X[k], ch.Mass[k]);
            double d = Math.Sqrt((com.x - pivot.x) * (com.x - pivot.x) + (com.y - pivot.y) * (com.y - pivot.y));
            double predicted = 2 * Math.PI * Math.Sqrt(I / (M * 9.81 * d));

            // swing it by 8 degrees about the pivot (about +Z) and time the zero crossings of the COM
            Quaternion r = OctoMath.AxisAngle(Vector3.forward, 8f * Mathf.Deg2Rad);
            void Rot(Vector3[] a) { for (int i = 0; i < a.Length; i++) a[i] = pivot + OctoMath.Rotate(r, a[i] - pivot); }
            Rot(b.Lattice.X); Rot(b.Lattice.Prev);
            foreach (TentacleChain ch in b.Arms) { Rot(ch.X); Rot(ch.Prev); }
            arm.Pin(tip, pivot, Vector3.up);
            float prev = b.CentreOfMass().x - pivot.x, first = -1f, last = -1f;
            int crossings = 0;
            for (int f = 1; f <= 60 * 12; f++)
            {
                b.Tick(Dt);
                float x = b.CentreOfMass().x - pivot.x;
                if ((prev > 0f && x <= 0f) || (prev < 0f && x >= 0f)) { float t = f * Dt; if (first < 0f) first = t; last = t; crossings++; }
                prev = x;
            }
            Assert.GreaterOrEqual(crossings, 3, "no swing");
            double measured = 2.0 * (last - first) / (crossings - 1);
            TestContext.Out.WriteLine($"period measured {measured:F3} s, compound pendulum {predicted:F3} s (d = {d:F3} m)");
            Assert.AreEqual(1.0, measured / predicted, 0.25);
        }

        // Shape matching normalises every cluster transform to det 1 and the hull volume constraint keeps the
        // body incompressible: under a hard pull (one arm contracting fast to minimum length with the whole
        // body hanging on it) and with two arms holding it in a V, the soft-bone determinants stay in
        // [0.97, 1.03] and the lattice volume within 5 %.
        [Test] public void Body_VolumePreserved()
        {
            var sp = NoBreath();
            // hard pull
            var b = new OctopusBody(sp, Vector3.zero, Quaternion.identity);
            b.SetAllLimp(true);
            TentacleChain arm = b.Arms[0];
            arm.Pin(arm.N - 1, arm.X[arm.N - 1], Vector3.up);
            Settle(b, 15f);
            CheckVolume(b, 150, f => { float s = Mathf.Lerp(1f, sp.arm.MinStretch, Mathf.Clamp01(f / 9f)); for (int j = 0; j < arm.N - 1; j++) arm.LengthTarget[j] = s; }, "hard pull");

            // V: two side arms pinned up and out, the body slung between them
            b = new OctopusBody(sp, Vector3.zero, Quaternion.identity);
            b.SetAllLimp(true);
            foreach (int a in new[] { 2, 6 })
            {
                TentacleChain ch = b.Arms[a];
                ch.Pin(ch.N - 1, OctopusShape.ArmRoot(sp, a) + OctopusShape.ArmDir(sp, a) * (0.7f * sp.arm.Length) + Vector3.up * 0.4f, Vector3.up);
            }
            CheckVolume(b, 360, null, "V hang");
        }

        static void CheckVolume(OctopusBody b, int frames, Action<int> each, string what)
        {
            float v0 = b.Lattice.RestVolume, vmin = float.MaxValue, vmax = 0f, dmin = float.MaxValue, dmax = 0f;
            var m = new Matrix4x4[b.BoneCount];
            for (int f = 0; f < frames; f++)
            {
                each?.Invoke(f);
                b.Tick(Dt);
                float v = b.Lattice.Volume() / v0;
                vmin = Mathf.Min(vmin, v); vmax = Mathf.Max(vmax, v);
                b.WriteBoneWorld(m, 0);
                float s3 = b.SizeScale * b.SizeScale * b.SizeScale;
                for (int c = 0; c < OctopusBody.SoftBoneCount; c++)
                {
                    float d = Det3(m[b.SoftBone(c)]) / s3;
                    dmin = Mathf.Min(dmin, d); dmax = Mathf.Max(dmax, d);
                }
            }
            TestContext.Out.WriteLine($"{what}: volume ratio [{vmin:F4}, {vmax:F4}], soft bone det [{dmin:F4}, {dmax:F4}]");
            Assert.That(vmin, Is.GreaterThanOrEqualTo(0.95f), what + ": volume lost");
            Assert.That(vmax, Is.LessThanOrEqualTo(1.05f), what + ": volume gained");
            Assert.That(dmin, Is.GreaterThanOrEqualTo(0.97f), what + ": soft bone det");
            Assert.That(dmax, Is.LessThanOrEqualTo(1.03f), what + ": soft bone det");
        }

        // Squishiness is a binding user requirement. A hard pull (the anchored arm contracting to minimum length
        // in 0.15 s, the whole body hanging on it) visibly stretches the mantle, which then squashes past its
        // rest shape and jiggles for 2-4 visible oscillations before settling. Both ends of every range are
        // pinned, so the body can neither silently go stiff nor turn to jelly.
        [Test] public void Body_HardPull_SquashStretchJiggle_InRange()
        {
            var sp = NoBreath();
            var b = new OctopusBody(sp, Vector3.zero, Quaternion.identity);
            b.SetAllLimp(true);
            TentacleChain arm = b.Arms[0];
            arm.Pin(arm.N - 1, arm.X[arm.N - 1], Vector3.up);
            Settle(b, 15f);
            SquishRun(b, sp, out float rise, out float max, out float minAfter, out int halfOscillations, out float final);
            TestContext.Out.WriteLine($"rise {rise:F3} m; stretch max {max:F4}, squash min {minAfter:F4}, visible half-oscillations {halfOscillations}, final {final:F4}");
            Assert.Greater(rise, 0.1f, "the contracting arm did not pull the body up");
            Assert.That(max, Is.InRange(1.12f, 1.20f), "mantle stretch under a hard pull (12-20 %)");
            Assert.That(minAfter, Is.InRange(0.85f, 0.95f), "squash / overshoot after the pull");
            Assert.That(halfOscillations, Is.InRange(4, 8), "visible jiggle: 2-4 oscillations beyond +-1.5 %");
            Assert.AreEqual(1f, final, 0.01f, "does not settle back to its shape");
        }

        // The hard-pull run shared with the mesh test: per frame, the head-front to apex extent / its hanging value.
        internal static void SquishRun(OctopusBody b, CephalopodSpec sp, out float rise, out float max, out float minAfter,
                                       out int halfOscillations, out float final, Action<int, float> each = null)
        {
            TentacleChain arm = b.Arms[0];
            float rest = Extent(b), y0 = b.CentreOfMass().y;
            max = 0f; minAfter = float.MaxValue; halfOscillations = 0;
            int tMax = -1, side = 0;
            for (int f = 1; f <= 300; f++)
            {
                float s = Mathf.Lerp(1f, sp.arm.MinStretch, Mathf.Clamp01(f / 9f));
                for (int j = 0; j < arm.N - 1; j++) arm.LengthTarget[j] = s;
                b.Tick(Dt);
                float e = Extent(b) / rest;
                if (e > max) { max = e; tMax = f; }
                else if (tMax > 0) minAfter = Mathf.Min(minAfter, e);
                int sd = e > 1.015f ? 1 : e < 0.985f ? -1 : 0;
                if (sd != 0 && sd != side) { if (side != 0) halfOscillations++; side = sd; }
                each?.Invoke(f, e);
            }
            rise = b.CentreOfMass().y - y0;
            final = Extent(b) / rest;
        }

        internal static float Det3(Matrix4x4 m) =>
            m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20);

        static float Extent(OctopusBody b) => (b.Lattice.X[OctopusShape.HeadFront] - b.Lattice.X[OctopusShape.Apex]).magnitude;

        // ------------------------------------------------------------------ sleep (resting cost)

        static OctopusBody RestingBody()
        {
            var b = new OctopusBody(CephalopodSpec.Default, Vector3.zero, Quaternion.identity);
            foreach (TentacleChain ch in b.Arms) ch.Pin(ch.N - 1, ch.X[ch.N - 1], Vector3.up);
            return b;
        }

        static bool TickUntilAsleep(OctopusBody b, float maxSeconds)
        {
            for (int f = 0; f < (int)(60 * maxSeconds) && !b.Asleep; f++) b.Tick(Dt);
            return b.Asleep;
        }

        // A settled resting octopus (every arm anchored, muscles on, breathing) falls asleep: its particles
        // stop exactly, the soft bones keep breathing, and a tick costs a small fraction of a simulated one.
        [Test] public void Body_Sleep_SettledRestFallsAsleep_StillBreathes()
        {
            OctopusBody b = RestingBody();
            Assert.IsTrue(TickUntilAsleep(b, 40f), "a settled resting body never fell asleep");
            TestContext.Out.WriteLine($"asleep after {b.Time:F1} s");
            var x0 = (Vector3[])b.Lattice.X.Clone();
            var tip0 = (Vector3[])b.Arms[2].X.Clone();
            var m = new Matrix4x4[b.BoneCount];
            float s3 = b.SizeScale * b.SizeScale * b.SizeScale, lo = float.MaxValue, hi = float.MinValue;
            int mid = b.SoftBone(OctopusShape.ClusterMid);
            for (int f = 0; f < 150; f++)
            {
                b.Tick(Dt);
                Assert.IsTrue(b.Asleep, "woke up on its own at frame " + f);
                b.WriteBoneWorld(m, 0);
                lo = Mathf.Min(lo, m[mid].m00); hi = Mathf.Max(hi, m[mid].m00);
                Assert.AreEqual(1f, Det3(m[mid]) / s3, 1e-3f, "breathing changed the volume");
            }
            for (int i = 0; i < x0.Length; i++) Assert.AreEqual(x0[i], b.Lattice.X[i], "lattice particle " + i + " moved while asleep");
            for (int k = 0; k < tip0.Length; k++) Assert.AreEqual(tip0[k], b.Arms[2].X[k], "arm particle " + k + " moved while asleep");
            Assert.Greater(hi - lo, 1e-3f, "the mantle did not breathe while asleep");

            // cost: asleep vs the same body simulated
            var sw = Stopwatch.StartNew();
            for (int f = 0; f < 120; f++) { b.Tick(Dt); b.WriteBoneWorld(m, 0); }
            double asleep = sw.Elapsed.TotalMilliseconds;
            b.AllowSleep = false;
            sw.Restart();
            for (int f = 0; f < 120; f++) { b.Tick(Dt); b.WriteBoneWorld(m, 0); }
            double awake = sw.Elapsed.TotalMilliseconds;
            TestContext.Out.WriteLine($"tick + bones: asleep {asleep / 120 * 1000:F1} us, awake {awake / 120 * 1000:F1} us");
            Assert.Less(asleep, 0.3 * awake, "sleeping is not cheaper");
        }

        // Anything that could move the body wakes it on the next tick: a muscle target, the sinus, tone, a
        // particle moved from outside, gravity, a pin released or added.
        [Test] public void Body_Sleep_WakesOnAnyChange()
        {
            OctopusBody b = RestingBody();
            TentacleChain arm = b.Arms[3];
            var changes = new (string what, Action apply, Action undo)[]
            {
                ("length target", () => arm.LengthTarget[5] += 1e-3f, () => arm.LengthTarget[5] -= 1e-3f),
                ("bend target", () => arm.SetTargetShape(arm.X[arm.N - 1] + Vector3.up, 0.5f, false), () => arm.ClearTargetShape()),
                ("sinus", () => arm.SinusAmount = 1f, () => arm.SinusAmount = 0f),
                ("tone", () => arm.Tone = 0.5f, () => arm.Tone = 1f),
                ("lattice particle moved", () => b.Lattice.X[OctopusShape.Apex] += new Vector3(0f, 0f, 1e-4f), null),
                ("arm particle moved", () => arm.X[10] += new Vector3(1e-4f, 0f, 0f), null),
                ("gravity", () => b.Gravity = new Vector3(0.5f, -9.8f, 0f), () => b.Gravity = new Vector3(0f, -9.81f, 0f)),
                ("pin added", () => arm.Pin(12, arm.X[12], Vector3.up), () => arm.Unpin(12)),
                ("pin released", () => arm.Unpin(arm.N - 1), null),
            };
            foreach (var c in changes)
            {
                Assert.IsTrue(TickUntilAsleep(b, 60f), "did not (re)settle before: " + c.what);
                c.apply();
                b.Tick(Dt);
                Assert.IsFalse(b.Asleep, "still asleep after: " + c.what);
                c.undo?.Invoke();
            }
        }

        // Never asleep while moving: falling, swinging, or with the travelling sinus on.
        [Test] public void Body_Sleep_NotWhileMoving()
        {
            var fall = new OctopusBody(CephalopodSpec.Default, new Vector3(0f, 50f, 0f), Quaternion.identity);
            for (int f = 0; f < 120; f++) { fall.Tick(Dt); Assert.IsFalse(fall.Asleep, "asleep in free fall"); }
            OctopusBody b = RestingBody();
            b.Arms[1].SinusAmount = 1f;
            for (int f = 0; f < 60 * 40; f++) { b.Tick(Dt); Assert.IsFalse(b.Asleep, "asleep with the sinus on"); }
        }

        // An impossible grip (two opposite arms pinned beyond their stretched reach) lets go instead of
        // tearing the body apart.
        [Test] public void Body_InfeasiblePull_ReleasesGrip()
        {
            var sp = NoBreath();
            var b = new OctopusBody(sp, Vector3.zero, Quaternion.identity);
            b.SetAllLimp(true);
            foreach (int a in new[] { 0, 4 })
            {
                TentacleChain ch = b.Arms[a];
                ch.Pin(ch.N - 1, ch.X[ch.N - 1] + OctopusShape.ArmDir(sp, a) * 0.8f, Vector3.up);
            }
            for (int f = 0; f < 120; f++) b.Tick(Dt);
            Assert.IsFalse(b.Arms[0].Pinned[b.Arms[0].N - 1] && b.Arms[4].Pinned[b.Arms[4].N - 1], "neither grip let go");
            float span = (b.Lattice.X[0] - b.Lattice.X[2]).magnitude / (b.Lattice.Rest[0] - b.Lattice.Rest[2]).magnitude;
            Assert.AreEqual(1f, span, 0.1f, "the web ring did not recover");
        }

        // The arm roots ride the body: the embedded roots match the web ring after a long, rough run, the
        // body stays finite, and two bodies with the same inputs agree bit for bit.
        [Test] public void Body_LongRun_FiniteAndDeterministic()
        {
            OctopusBody Run()
            {
                var b = new OctopusBody(CephalopodSpec.Default, Vector3.zero, Quaternion.identity, 1.5f);
                TentacleChain a0 = b.Arms[0], a3 = b.Arms[3];
                for (int f = 0; f < 60 * 20; f++)
                {
                    if (f == 60) a0.Pin(a0.N - 1, a0.X[a0.N - 1], Vector3.up);
                    if (f == 300) a3.Pin(a3.N - 4, a3.X[a3.N - 4], Vector3.up);
                    if (f == 700) a0.UnpinAll();
                    b.Arms[f / 120 % 8].SinusAmount = 1f;
                    b.Tick(f % 400 == 399 ? 0.25f : Dt);
                }
                return b;
            }
            OctopusBody x = Run(), y = Run();
            for (int i = 0; i < SoftLattice.Count; i++)
            {
                Assert.IsTrue(OctoMath.Finite(x.Lattice.X[i]), "lattice " + i);
                Assert.AreEqual(x.Lattice.X[i].x, y.Lattice.X[i].x); Assert.AreEqual(x.Lattice.X[i].y, y.Lattice.X[i].y);
            }
            foreach (TentacleChain ch in x.Arms) AssertFinite(ch, "arm");
            var m = new Matrix4x4[x.BoneCount];
            x.WriteBoneWorld(m, 0);
            for (int i = 0; i < m.Length; i++) Assert.IsFalse(float.IsNaN(m[i].m00 + m[i].m13 + m[i].m22), "bone " + i);
        }
    }
}
