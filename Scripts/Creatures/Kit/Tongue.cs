// Creature kit: Tongue - a long forked tongue on a physics chain (TentacleChain, through its public API, as the
// Tendril), rooted inside the mouth. Every so often it shoots out (outSeconds, eased), flicks - a fast travelling
// wave down the chain in the plane rolled planeDeg from the side-to-side (90 = up and down), plus the chain's own
// follow-through as the head sways and turns - and draws back in (inSeconds) until it is hidden inside the head;
// then it waits intervalMin .. intervalMax and flicks again. The chain is always simulated at its full length; the
// drawn tongue is the chain unrolled from its root by the extension e (0 hidden .. 1 out): bone k at root + e x
// (particle k - root), its length along the chain x e, its girth easing to 0.35 x (a tongue drawn back into its
// sheath), so the forked tip slides out of the mouth and back in. While hidden the chain is not simulated (it is
// laid straight along the mouth when it next shoots out). Froude: lengths x s, times x sqrt s, masses x s^3.
// Mesh: a tapered round tube with two tines past the tip (rigid to the last bone), material layer `layer`.
// Kit-only part: bones from the species layout (Chain block), runtime at stage Late; allocation-free after
// construction; main thread.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Tongue", PartKind.Chain, Bones = "= segments", Summary = "A forked tongue on a physics chain (TentacleChain) rooted in the mouth: shoots out, flicks (a fast travelling wave plus the chain's follow-through as the head sways), draws back until hidden in the head, waits, flicks again.",
          System = "TongueRuntime (part runtime, stage Late) + TentacleChain", MaterialLayers = new[] { 14 }, KitOnly = true,
          UseWhen = "monitor lizards, snakes: a tongue that flicks in and out; any quick extensible feeler that hides in the head")]
    public sealed class Tongue : CreaturePart
    {
        [Param(ParamUnit.Direction, "the direction it shoots out, in the joint frame (along the mouth)")] public Vector3 direction = new Vector3(0f, -0.1f, 1f);
        [Param(ParamUnit.Metres, "length out of the root, extended", Example = "komodo 0.14")] public float length = 0.1f;
        [Param(ParamUnit.Count, "particles (= bones) along it", Min = 4, Max = 16, Default = "8")] public int segments = 8;
        [Param(ParamUnit.Metres, "radius at the root")] public float rootRadius = 0.006f;
        [Param(ParamUnit.Metres, "radius where it forks")] public float tipRadius = 0.0025f;
        [Param(ParamUnit.Fraction, "fork tines: length, fraction of the tongue's", Min = 0, Max = 0.5f)] public float forkLength = 0.18f;
        [Param(ParamUnit.Degrees, "fork tines: spread between them", Min = 0, Max = 90)] public float forkSpreadDeg = 34f;
        [Param(ParamUnit.Degrees, "flick: wave amplitude per joint (bounded by bendLimitDeg)", Min = 0, Max = 40, Example = "komodo 16")] public float waveDeg = 16f;
        [Param(ParamUnit.Hz, "flick: wave frequency", Example = "komodo 7")] public float waveHz = 7f;
        [Param(ParamUnit.Fraction, "flick: waves along the tongue", Min = 0.25f, Max = 4)] public float waves = 1.2f;
        [Param(ParamUnit.Degrees, "flick: plane of the wave rolled from side-to-side (0) toward up-and-down (90)", Min = 0, Max = 90)] public float planeDeg = 70f;
        [Param(ParamUnit.Degrees, "bend cone per joint", Min = 5, Max = 60, Default = "35")] public float bendLimitDeg = 35f;
        [Param(ParamUnit.Fraction, "muscle tone: how straight it holds against gravity", Min = 0.05f, Max = 1)] public float muscle = 0.8f;
        [Param(ParamUnit.Hz, "velocity damping rate relative to the head (1/s)", Example = "6")] public float damping = 6f;
        [Param(ParamUnit.Kilograms, "mass of the tongue", Example = "0.004")] public float mass = 0.004f;
        [Param(ParamUnit.Seconds, "shooting out (eased)", Example = "0.08")] public float outSeconds = 0.08f;
        [Param(ParamUnit.Seconds, "flicking, fully out", Example = "0.5")] public float flickSeconds = 0.5f;
        [Param(ParamUnit.Seconds, "drawing back in (eased)", Example = "0.12")] public float inSeconds = 0.12f;
        [Param(ParamUnit.Seconds, "shortest wait between flicks")] public float intervalMin = 0.8f;
        [Param(ParamUnit.Seconds, "longest wait between flicks")] public float intervalMax = 2.5f;
        [Param(ParamUnit.Count, "material layer (part id): 14 tongue (restyle it with Layers), 1 limb, 0 body", Min = 0, Max = 15, Default = "14")] public int layer = 14;

        public const int MinSegments = 4, MaxSegments = 16;
        public const float HiddenGirth = 0.3f;           // girth when drawn back in, x
        public const float HiddenLength = 0.015f;         // drawn length when hidden, x (never 0: no degenerate triangles)
        public int Segments => Mathf.Clamp(segments, MinSegments, MaxSegments);

        public override int BoneCount(in SpeciesCtx c) => Segments;
        public override int MeshOrder => 100;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Tongue roots on a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (segments < MinSegments || segments > MaxSegments) return $"{id}: segments must be {MinSegments}..{MaxSegments}";
            if (!(length > 0f) || !(rootRadius > 0f) || !(tipRadius > 0f) || tipRadius > rootRadius) return $"{id}: length > 0, 0 < tipRadius <= rootRadius";
            if (forkLength < 0f || forkLength > 0.5f || forkSpreadDeg < 0f || forkSpreadDeg > 90f) return $"{id}: forkLength 0..0.5, forkSpreadDeg 0..90";
            if (waveDeg < 0f || waveDeg > 40f || waveHz < 0f || waves < 0.25f || waves > 4f) return $"{id}: waveDeg 0..40, waveHz >= 0, waves 0.25..4";
            if (!(mass > 0f) || !(muscle > 0f) || muscle > 1f || damping < 0f) return $"{id}: mass > 0, muscle in (0, 1], damping >= 0";
            if (!(outSeconds > 0f) || flickSeconds < 0f || !(inSeconds > 0f) || intervalMin < 0f || intervalMax < intervalMin) return $"{id}: out / in > 0, flick >= 0, 0 <= intervalMin <= intervalMax";
            if (layer < 0 || layer >= PartLayers.MaxParts) return $"{id}: layer must be in 0..{PartLayers.MaxParts - 1}";
            if (direction.sqrMagnitude < 1e-8f) return $"{id}: direction is zero";
            return null;
        }

        // The chain's spec at scale 1: nearly no rest curl (gravity and the wave shape it), the wave as its sinus.
        public TentacleSpec Spec => new TentacleSpec
        {
            segments = Segments, length = length, rootRadius = rootRadius, tipRadius = tipRadius, taper = 1f, flatten = 1f,
            bendLimitDeg = bendLimitDeg, tipCurlDeg = 0.01f, minStretch = 0.95f, maxStretch = 1.05f, maxTwistDeg = 10f,
            muscle = muscle, damping = Mathf.Max(0.01f, damping), massPerArm = mass, sinusAmpDeg = Mathf.Max(0.01f, waveDeg),
            sinusHz = Mathf.Max(0.01f, waveHz), sinusWaves = waves,
        };

        // Root pose in the joint frame: the offset and a frame whose +Z is the direction, rolled by planeDeg about it
        // (the chain's travelling sinus bends in its frame's x-z plane: rolled 90 it waves up and down).
        public void RootLocal(out Vector3 offset, out Quaternion frame)
        {
            offset = at.offset;
            Vector3 d = PartMeshEmitter.Unit(direction, Vector3.forward);
            Vector3 up = Vector3.up - d * Vector3.Dot(Vector3.up, d);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.back - d * Vector3.Dot(Vector3.back, d);
            frame = Quaternion.LookRotation(d, up.normalized) * Quaternion.AngleAxis(planeDeg, Vector3.forward);
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new TongueRuntime(this, c.Sim, c.Species.Layout.Start(c.Index), c.Size, c.Ordinal) : null;

        // ------------------------------------------------------------ mesh

        const int kSides = 8;

        // A tapered tube along the rest chain (bones straight along the direction, extended), a ring at every particle
        // (blended 50/50 between its segments' bones) and one mid-segment; a rounded root inside the mouth; past the last
        // particle two tines (cones rigid to the last bone) spread in the mouth's plane.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            int start = m.Species.Layout.Start(c.Index), n = Segments;
            TentacleSpec spec = Spec;
            float sp = spec.Spacing;
            var q = new System.Collections.Generic.List<PartMeshEmitter.TubeSample>(2 * n + 2);
            Matrix4x4 b0 = m.Bones[start];
            Vector3 p0 = b0.MultiplyPoint3x4(Vector3.zero), t0 = PartMeshEmitter.Unit(b0.MultiplyVector(Vector3.forward), Vector3.forward);
            float r0 = spec.RadiusAt(0f);
            q.Add(PartMeshEmitter.Sample(p0 - t0 * r0, t0, 0f, 0f, 0f, PartMeshEmitter.Rigid(start), true));
            float dr = (spec.TipRadius - spec.RootRadius) / spec.Length;
            Vector3 tip = p0, tipT = t0;
            for (int k = 0; k < n; k++)
            {
                Matrix4x4 b = m.Bones[start + k];
                Vector3 p = b.MultiplyPoint3x4(Vector3.zero);
                Vector3 t = PartMeshEmitter.Unit(b.MultiplyVector(Vector3.forward), t0);
                float s = sp * k, r = spec.RadiusAt(s);
                Vector4 skin = k == 0 ? PartMeshEmitter.Rigid(start) : PartMeshEmitter.Blend(start + k - 1, start + k, 0.5f);
                if (k == n - 1) skin = PartMeshEmitter.Rigid(start + k);
                q.Add(PartMeshEmitter.Sample(p, t, r, dr, s, skin));
                if (k < n - 1)
                {
                    float sm = s + 0.5f * sp;
                    q.Add(PartMeshEmitter.Sample(p + t * (0.5f * sp), t, spec.RadiusAt(sm), dr, sm, PartMeshEmitter.Rigid(start + k)));
                }
                else
                {
                    float rt = Mathf.Max(spec.TipRadius, PartMeshEmitter.MinRadius);
                    q.Add(PartMeshEmitter.Sample(p + t * rt, t, 0f, 0f, s + rt, PartMeshEmitter.Rigid(start + k), true));
                    tip = p; tipT = t;
                }
            }
            Matrix4x4 bl = m.Bones[start + n - 1];
            Vector3 side = PartMeshEmitter.Unit(bl.MultiplyVector(Quaternion.Inverse(Quaternion.AngleAxis(planeDeg, Vector3.forward)) * Vector3.right), Vector3.right);
            m.Tube(q, side, kSides, layer);
            if (forkLength > 0f)
            {
                float fl = forkLength * length, half = 0.5f * forkSpreadDeg;
                Vector3 up = PartMeshEmitter.Unit(Vector3.Cross(tipT, side), Vector3.up);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 d = Quaternion.AngleAxis(s * half, up) * tipT;
                    Quaternion qt = Quaternion.LookRotation(d, up);
                    m.Horn(tip - d * (0.5f * spec.TipRadius), qt, spec.TipRadius * 0.85f, 0f, fl, 0f, start + n - 1, layer, 6, sp * (n - 1), out _, out _);
                }
            }
        }
    }

    // One Tongue of one individual: a TentacleChain at full length, drawn unrolled by the extension.
    public sealed class TongueRuntime : IPartRuntime
    {
        public enum Phase { Hidden = 0, Out = 1, Flick = 2, In = 3 }

        const int kSubSteps = 3, kIterations = 3;
        const float kReachFroude = 4f, kShapePull = 0.05f, kHold = 0.97f;

        readonly Tongue t;
        readonly CreatureSim sim;
        readonly int start, n;
        readonly float scale, T;
        readonly Vector3 rootOffset;
        readonly Quaternion rootFrame;
        public readonly TentacleChain Chain;
        readonly float[] radius;
        Vector3 lastRoot;
        Quaternion lastFrame;
        float phaseTime, wait;
        int flicks;
        bool stepped;
        uint seed;

        public Phase State { get; private set; }
        public float Extension { get; private set; }     // 0 hidden .. 1 out
        public int Flicks => flicks;
        public int FirstBone => start;
        public Tongue Part => t;

        public TongueRuntime(Tongue tongue, CreatureSim sim, int firstBone, SizeCtx size, int ordinal)
        {
            t = tongue; this.sim = sim; start = firstBone; n = tongue.Segments;
            scale = size.L; T = size.T;
            tongue.RootLocal(out rootOffset, out rootFrame);
            RootPose(out Vector3 root, out Quaternion frame);
            Chain = new TentacleChain(tongue.Spec, root, frame, scale);
            Chain.Tone = 1f;
            Chain.ShapeRate = TentacleChain.FastShapeRate;
            // out, the tongue is held straight out along the mouth by the tip-led pull (the curvature muscle alone lets a
            // long thin chain sag): its goal travels at kReachFroude sqrt(g L) (the Tendril's lesson: the chain's default
            // ReachSpeed, tuned for metre-long octopus arms, is far too slow for a short chain; Froude-scaled by the chain)
            Chain.PullSpeed = kReachFroude * Mathf.Sqrt(9.81f * tongue.Spec.Length);
            Chain.SinusAmount = 0f;
            lastRoot = root; lastFrame = frame;
            radius = new float[n];
            TentacleSpec spec = tongue.Spec;
            for (int k = 0; k < n; k++) radius[k] = spec.RadiusAt(spec.Spacing * k) * scale;
            Vector3 p = sim.Joints[0];
            seed = (uint)(Mathf.RoundToInt(p.x * 97f) * 73856093 ^ Mathf.RoundToInt(p.z * 89f) * 19349663 ^ (ordinal + 7) * 83492791);
            State = Phase.Hidden;
            wait = Interval(0) * 0.5f;
        }

        public PartStage Stage => PartStage.Late;

        float Hash01(int k)
        {
            uint x = seed + (uint)k * 0x9E3779B1u;
            x ^= x >> 15; x *= 0x85EBCA77u; x ^= x >> 13; x *= 0xC2B2AE35u; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }

        float Interval(int k) => Mathf.Lerp(t.intervalMin, t.intervalMax, Hash01(k)) * T;

        void RootPose(out Vector3 root, out Quaternion frame)
        {
            int j = t.at.joint;
            Quaternion r = sim.Rotations[j];
            root = sim.Joints[j] + r * (rootOffset * scale);
            frame = r * rootFrame;
        }

        Vector3 Up => sim.Rotations[t.at.joint] * Vector3.up;
        public Vector3 Root { get { RootPose(out Vector3 r, out _); return r; } }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            stepped = true;
            RootPose(out Vector3 root, out Quaternion frame);
            phaseTime += dt;
            switch (State)
            {
                case Phase.Hidden:
                    Extension = 0f;
                    wait -= dt;
                    if (wait <= 0f) { State = Phase.Out; phaseTime = 0f; Lay(root, frame); flicks++; }
                    break;
                case Phase.Out:
                    Extension = Ease(phaseTime / (t.outSeconds * T));
                    if (phaseTime >= t.outSeconds * T) { State = Phase.Flick; phaseTime = 0f; Extension = 1f; }
                    break;
                case Phase.Flick:
                    Extension = 1f;
                    if (phaseTime >= t.flickSeconds * T * (0.7f + 0.6f * Hash01(3 * flicks + 1))) { State = Phase.In; phaseTime = 0f; }
                    break;
                case Phase.In:
                    Extension = 1f - Ease(phaseTime / (t.inSeconds * T));
                    if (phaseTime >= t.inSeconds * T) { State = Phase.Hidden; phaseTime = 0f; Extension = 0f; wait = Interval(3 * flicks + 2); }
                    break;
            }
            // the wave runs while it is out
            bool out_ = State == Phase.Flick || State == Phase.Out;
            Chain.SinusAmount = out_ ? 1f : 0f;
            if (State == Phase.Hidden) { Lay(root, frame); return; }   // hidden: laid straight in the mouth, not simulated
            if (out_)
            {
                Chain.ShapePull = kShapePull;
                Chain.SetTargetShape(root + frame * Vector3.forward * (kHold * Chain.Length), 1f, false);
            }
            else { Chain.ShapePull = 0f; Chain.ClearTargetShape(); }
            Integrate(dt, root, frame);
        }

        // Laid straight along the mouth (it shoots out from there), at rest.
        void Lay(Vector3 root, Quaternion frame)
        {
            Chain.SetRoot(root, frame);
            Vector3 f = frame * Vector3.forward;
            for (int k = 1; k < n; k++) { Chain.X[k] = root + f * (Chain.Spacing * k); Chain.Prev[k] = Chain.X[k]; }
            lastRoot = root; lastFrame = frame;
            Chain.UpdateFrames(0f);
        }

        // Sub-steps with the root eased over the tick and damping relative to the root's own velocity (the Tendril's
        // lesson: a whole-tick root jump flings the free run); every particle kept above the ground under the head.
        void Integrate(float dt, Vector3 root, Quaternion frame)
        {
            dt = Mathf.Min(dt, 0.1f);
            float h = dt / kSubSteps;
            Vector3 vel = (root - lastRoot) / dt;
            Vector3 up = Up.normalized;
            Chain.Gravity = -9.81f * up;
            Vector3 g = sim.Joints[t.at.joint] - up * (sim.Species.joints[t.at.joint].clearance * scale);
            for (int s = 0; s < kSubSteps; s++)
            {
                float f = (s + 1f) / kSubSteps;
                Chain.SetRoot(Vector3.Lerp(lastRoot, root, f), Quaternion.Slerp(lastFrame, frame, f));
                Chain.Integrate(h, vel);
                for (int it = 0; it < kIterations; it++) { Chain.SolveIteration(h); KeepAbove(g, up); }
                Chain.EndSubStep(h);
            }
            Chain.UpdateFrames(dt);
            lastRoot = root; lastFrame = frame;
        }

        void KeepAbove(Vector3 g, Vector3 gn)
        {
            TentacleChain c = Chain;
            for (int k = 1; k < n; k++)
            {
                float d = Vector3.Dot(c.X[k] - g, gn) - radius[k];
                if (d >= 0f) continue;
                c.X[k] -= gn * d;
                Vector3 v = c.X[k] - c.Prev[k];
                float vn = Vector3.Dot(v, gn);
                c.Prev[k] = c.X[k] - (v - gn * vn) * 0.6f - gn * Mathf.Max(0f, vn);
            }
        }

        // The chain's bones (TentacleChain.WriteBones: its drawable display copy), unrolled from the root by the
        // extension: positions pulled toward the root, lengths x e, girth eased toward HiddenGirth. Before the first
        // step (the rest pose: the bind) fully out and straight.
        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            Chain.WriteBones(dst, offset + start);
            float e = stepped ? Extension : 1f;
            if (e >= 1f) return;
            float ez = Mathf.Lerp(Tongue.HiddenLength, 1f, e), er = Mathf.Lerp(Tongue.HiddenGirth, 1f, e);
            Vector3 root = Chain.DisplayX[0];
            for (int k = 0; k < n; k++)
            {
                Matrix4x4 b = dst[offset + start + k];
                Vector3 p = Chain.DisplayX[k];
                Vector3 bx = b.GetColumn(0), by = b.GetColumn(1), bz = b.GetColumn(2);
                Vector3 pos = root + (p - root) * ez;
                b.SetColumn(0, bx * er); b.SetColumn(1, by * er); b.SetColumn(2, bz * ez);
                b.SetColumn(3, new Vector4(pos.x, pos.y, pos.z, 1f));
                dst[offset + start + k] = b;
            }
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            Vector3 a = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), b = -a;
            Chain.Encapsulate(ref a, ref b);
            float r = (t.rootRadius * 1.3f + t.forkLength * t.length) * scale;
            lo = Vector3.Min(lo, a - new Vector3(r, r, r));
            hi = Vector3.Max(hi, b + new Vector3(r, r, r));
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            if (State == Phase.Hidden) return;
            var c = new Color(1f, 0.85f, 0.2f, 1f);
            for (int k = 0; k + 1 < n; k++) d.Line(Chain.X[k], Chain.X[k + 1], c);
        }
    }
}
