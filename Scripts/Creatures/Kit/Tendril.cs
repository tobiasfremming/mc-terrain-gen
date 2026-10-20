// Creature kit T5.9: Tendril - a muscular tentacle rooted on a spine joint (the socket-rooted Tentacle of the plan,
// without the octopus' soft body): a TentacleChain (position-based particles: gravity, muscle tone toward a rest
// curl, length and bend limits, a travelling writhe) whose root rides its joint. It hangs and writhes while the body
// moves and swings with it; when the body slows to a stop it reaches out, lays its tip on the ground ahead (the
// plane of the planted feet) and holds it there - an eased pin, so it never trembles - feeling about one spot after
// another, and lets go the moment the body walks on or the hold would overstretch it. Froude: lengths x s, times x
// sqrt s, masses x s^3 (TentacleChain's size rules). Mesh: a tapered round tube (2-bone blend along the chain) in
// the material layer `layer` (1 = limb, 9 = the band colour: a tentacle may share the knee bands' colour).
// Kit-only part: bones from the species layout (Chain block), runtime at stage Late (once per tick, its own
// sub-steps). Allocation-free after construction; main thread. The octopus' Tentacle (soft bodies) is unchanged.
// Maw mode (mode 1, 2026-10-20, the Strider Matriarch's face tentacles): never still - it hangs and writhes, then
// probes (its tip reaches for the ground ahead and sweeps it side to side at probeHz), and now and then (lashEvery)
// lashes out: a fast whip ahead and up, then it drops back to hanging. Seeded per tendril, Froude-timed.
// Clearance (clearance > 0, either mode): its particles and segments are kept outside every IBodyObstacle (a
// DimpledOrb) and every ICapsuleSet limb (the BendyLegs), each inflated by the tube's radius plus the clearance.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Tendril", PartKind.Chain, Bones = "= segments", Summary = "Muscular tentacle rooted on a spine joint (TentacleChain physics: gravity, muscle tone toward a rest curl, a travelling writhe, exact length and bend limits). Hangs and swings while the body moves; when it stops, reaches out and holds its tip on the ground ahead (an eased pin: no tremble), feeling about, and lets go when the body walks on.",
          System = "TendrilRuntime (part runtime, stage Late) + TentacleChain", MaterialLayers = new[] { 1, 9 }, KitOnly = true,
          UseWhen = "tentacles, feeding arms, feelers that touch the ground; on a spine body (soft bodies: Tentacle)")]
    public sealed class Tendril : CreaturePart
    {
        [Param(ParamUnit.Direction, "rest direction at the root in the joint frame (x mirrored by side)")] public Vector3 direction = new Vector3(0f, -0.5f, 1f);
        [Param(ParamUnit.Metres, "length, root to tip", Example = "tentacle crab 0.22")] public float length = 0.2f;
        [Param(ParamUnit.Count, "particles (= bones) along it", Min = 4, Max = 24, Default = "12")] public int segments = 12;
        [Param(ParamUnit.Metres, "radius at the root")] public float rootRadius = 0.01f;
        [Param(ParamUnit.Metres, "radius at the tip")] public float tipRadius = 0.002f;
        [Param(ParamUnit.Degrees, "rest curl toward the belly side at the tip (rises toward it)", Min = 0, Max = 120)] public float curlDeg = 50f;
        [Param(ParamUnit.Degrees, "bend cone per joint", Min = 5, Max = 60, Default = "28")] public float bendLimitDeg = 28f;
        [Param(ParamUnit.Fraction, "muscle tone: how firmly it holds its curl against gravity", Min = 0.05f, Max = 1)] public float muscle = 0.4f;
        [Param(ParamUnit.Hz, "velocity damping rate relative to still air (1/s)", Example = "1.5")] public float damping = 1.5f;
        [Param(ParamUnit.Kilograms, "mass of the whole tendril", Example = "0.02")] public float mass = 0.02f;
        [Param(ParamUnit.Degrees, "writhe: amplitude of the wave travelling down it", Min = 0, Max = 30)] public float writheDeg = 8f;
        [Param(ParamUnit.Hz, "writhe frequency", Example = "0.8")] public float writheHz = 0.8f;
        [Param(ParamUnit.Fraction, "reach: how far ahead it lays its tip, fraction of its length (0 = never touches down)", Min = 0, Max = 0.95f)] public float reach = 0.7f;
        [Param(ParamUnit.Seconds, "how long a touch holds before it feels for another spot", Example = "2")] public float holdSeconds = 2f;
        [Param(ParamUnit.Fraction, "the body moves faster than this fraction of its walking speed: let go, hang", Min = 0, Max = 1)] public float releaseSpeed = 0.25f;
        [Param(ParamUnit.Count, "material layer (part id): 1 the limbs', 9 the band colour's", Min = 1, Max = 9, Default = "1")] public int layer = 1;
        [Param(ParamUnit.Count, "behaviour: 0 feels the ground when the body stops; 1 maw: always writhing, probing the ground ahead and lashing", Min = 0, Max = 1)] public int mode;
        [Param(ParamUnit.Hz, "maw: how fast a probing tip sweeps side to side", Min = 0f, Max = 5f, Example = "0.3")] public float probeHz = 0.3f;
        [Param(ParamUnit.Seconds, "maw: mean time between lashes (0 = never)", Min = 0f, Example = "8")] public float lashEvery;
        [Param(ParamUnit.Metres, "kept this far outside the body's obstacles (a DimpledOrb) and the limbs (BendyLegs), plus its radius; 0 = off", Min = 0f)] public float clearance;

        public const int MinSegments = 4, MaxSegments = 24;
        public int Segments => Mathf.Clamp(segments, MinSegments, MaxSegments);
        public int Side => at.side < 0 ? -1 : at.side > 0 ? 1 : 0;

        public override int BoneCount(in SpeciesCtx c) => Segments;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Tendril roots on a SpineBody (a soft body's arms are Tentacle parts)";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (segments < MinSegments || segments > MaxSegments) return $"{id}: segments must be {MinSegments}..{MaxSegments}";
            if (!(length > 0f) || !(rootRadius > 0f) || tipRadius < 0f || tipRadius > rootRadius) return $"{id}: length > 0, rootRadius > 0, 0 <= tipRadius <= rootRadius";
            if (!(mass > 0f) || !(muscle > 0f) || muscle > 1f || damping < 0f) return $"{id}: mass > 0, muscle in (0, 1], damping >= 0";
            if (reach < 0f || reach > 0.95f || holdSeconds < 0f || releaseSpeed < 0f || releaseSpeed > 1f) return $"{id}: reach 0..0.95, holdSeconds >= 0, releaseSpeed 0..1";
            if (layer != 1 && layer != 9) return $"{id}: layer must be 1 (limb) or 9 (band)";
            if (direction.sqrMagnitude < 1e-8f) return $"{id}: direction is zero";
            if (mode < 0 || mode > 1) return $"{id}: mode must be 0 (feeler) or 1 (maw)";
            if (probeHz < 0f || lashEvery < 0f || clearance < 0f) return $"{id}: probeHz, lashEvery, clearance must be >= 0";
            return null;
        }

        // The chain's spec (TentacleSpec, the octopus arm's physics) at scale 1.
        public TentacleSpec Spec => new TentacleSpec
        {
            segments = Segments, length = length, rootRadius = rootRadius, tipRadius = Mathf.Max(1e-4f, tipRadius), taper = 0.9f, flatten = 1f,
            bendLimitDeg = bendLimitDeg, tipCurlDeg = Mathf.Max(0.01f, curlDeg), minStretch = 0.9f, maxStretch = 1.2f, maxTwistDeg = 20f,
            muscle = muscle, damping = Mathf.Max(0.01f, damping), massPerArm = mass, sinusAmpDeg = Mathf.Max(0.01f, writheDeg), sinusHz = Mathf.Max(0.01f, writheHz),
            sinusWaves = 1.2f,
        };

        // Root pose in the joint frame: offset (x mirrored) and a frame whose +Z is the rest direction and +Y the
        // joint's up made perpendicular (the curl bends toward -Y: the belly side).
        public void RootLocal(out Vector3 offset, out Quaternion frame)
        {
            int s = Side == 0 ? 1 : Side;
            offset = at.offset; offset.x *= s;
            Vector3 d = direction; d.x *= s;
            d = PartMeshEmitter.Unit(d, Vector3.forward);
            Vector3 up = Vector3.up - d * Vector3.Dot(Vector3.up, d);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.back - d * Vector3.Dot(Vector3.back, d);
            frame = Quaternion.LookRotation(d, up.normalized);
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new TendrilRuntime(this, c.Sim, c.Species.Layout.Start(c.Index), c.Size, c.Ordinal) : null;

        // ------------------------------------------------------------ mesh

        const int kSides = 8;

        // A tapered round tube along the rest chain (the rest sim's bones: straight along the direction), a ring at
        // every particle (blended 50/50 between its two segments' bones) and one mid-segment (rigid to its segment);
        // a rounded root buried in the body and a rounded tip.
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
            for (int k = 0; k < n; k++)
            {
                Matrix4x4 b = m.Bones[start + k];
                Vector3 p = b.MultiplyPoint3x4(Vector3.zero);
                Vector3 t = PartMeshEmitter.Unit(b.MultiplyVector(Vector3.forward), t0);
                float s = sp * k, r = spec.RadiusAt(s), dr = (spec.RadiusAt(Mathf.Min(spec.Length, s + 1e-3f)) - r) / 1e-3f;
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
                }
            }
            Vector3 up = b0.MultiplyVector(Vector3.up);
            m.Tube(q, PartMeshEmitter.Unit(Vector3.Cross(up, t0), Vector3.right), kSides, layer);
        }
    }

    // One Tendril of one individual: a TentacleChain whose root rides its joint, a hang / reach / hold cycle.
    public sealed class TendrilRuntime : IPartRuntime
    {
        public enum Phase { Hang = 0, Reach = 1, Hold = 2, Probe = 3, Lash = 4 }

        const int kSubSteps = 3, kIterations = 3;
        // maw mode: a free tentacle with no hold to keep still needs fewer solver passes (2 x 2: the perf budget of a
        // creature carrying eight of them; at the matriarch's size the sub-steps are short in Froude time anyway)
        const int kMawSubSteps = 2, kMawIterations = 2;
        const float kSettleSeconds = 0.6f;     // the body must be slow this long (x sqrt s) before a reach
        const float kReachSeconds = 1.6f;      // a reach that has not touched down by then (x sqrt s) gives up
        const float kTouchDist = 0.1f;         // the tip within this fraction of the length of the spot: touch down
        const float kOverstretch = 0.97f;      // a hold whose root is this fraction of the length from its pin lets go
        const float kShapePull = 0.6f;
        // The reach's pull goal travels at this many sqrt(g L) (L the tendril's length at scale 1; TentacleChain scales
        // PullSpeed by sqrt s, so it stays Froude-similar). The chain's default (Spec.ReachSpeed, 1.2 m/s, tuned for
        // the octopus' metre-long arms) is ~0.9 sqrt(gL) here: the FABRIK shape is re-seeded from the arm every step,
        // so a goal that slow trails a shape that trails the arm, and the muscled curl (112 deg, tone 0.7) holds the
        // tip short of its spot until the reach times out (0-1 touches per tendril, none at 1.1x). Touch-down is
        // reliable from ~2.6 sqrt(gL); 4 keeps a 1.5x margin and is still eased (the goal crosses the reach in ~0.03 s,
        // not in one solver pass; the hold's worst tip motion fell from 8-13 to 5-6 mm/frame vs the old instant pull).
        const float kReachFroude = 4f;
        // maw: a lash's pull goal travels at this many sqrt(g L) (a whip, not a reach); phase lengths (x sqrt s)
        const float kLashFroude = 14f, kLashSeconds = 0.55f;
        const float kHangMin = 1.2f, kHangSpan = 2.4f, kProbeMin = 2f, kProbeSpan = 3f;

        readonly Tendril t;
        readonly CreatureSim sim;
        readonly int start, n, ordinal;
        readonly float scale, T, length;
        readonly Vector3 rootOffset;
        readonly Quaternion rootFrame;
        public readonly TentacleChain Chain;
        public Phase State { get; private set; }
        float stillTime, phaseTime, holdTime;
        Vector3 lastRoot;
        Quaternion lastFrame;
        readonly float[] radius;              // per particle, its tube radius (m, this individual)
        const float kGroundFriction = 0.4f;   // share of the tangential velocity a particle on the ground loses per contact
        int touches;
        Vector3 spot, spotNormal;
        // maw and clearance
        int cycles;
        float phaseLen, lashSide;
        IBodyObstacle[] obstacles;
        ICapsuleSet[] limbs;
        Vector3[] obC, obAx; Quaternion[] obR, obRi;   // the obstacles this tick (they do not move in stage Late)
        public int Lashes { get; private set; }
        public int Probes { get; private set; }

        public Tendril Part => t;
        public int FirstBone => start;
        public Vector3 Spot => spot;
        public int Touches => touches;
        public Vector3 Tip => Chain.X[n - 1];
        public Vector3 Root => Chain.X[0];

        public TendrilRuntime(Tendril tendril, CreatureSim sim, int firstBone, SizeCtx size, int ordinal)
        {
            t = tendril; this.sim = sim; start = firstBone; n = tendril.Segments; this.ordinal = ordinal;
            scale = size.L; T = size.T;
            tendril.RootLocal(out rootOffset, out rootFrame);
            RootPose(out Vector3 root, out Quaternion frame);
            Chain = new TentacleChain(tendril.Spec, root, frame, scale);
            lastRoot = root; lastFrame = frame;
            length = Chain.Length;
            radius = new float[n];
            TentacleSpec spec = tendril.Spec;
            for (int k = 0; k < n; k++) radius[k] = spec.RadiusAt(spec.Spacing * k) * scale;
            Chain.Tone = 1f;
            Chain.PullSpeed = kReachFroude * Mathf.Sqrt(9.81f * spec.Length);
            Chain.SinusAmount = 0f;
            State = Phase.Hang;
            phaseLen = (kHangMin + kHangSpan * Hash01(ordinal * 41 + 3)) * T;
        }

        void FindObstacles()
        {
            int o = 0, l = 0;
            foreach (IPartRuntime r in sim.PartRuntimes) { if (r is IBodyObstacle) o++; if (r is ICapsuleSet) l++; }
            obstacles = new IBodyObstacle[o]; limbs = new ICapsuleSet[l];   // once, on the first step
            obC = new Vector3[o]; obAx = new Vector3[o]; obR = new Quaternion[o]; obRi = new Quaternion[o];
            o = 0; l = 0;
            foreach (IPartRuntime r in sim.PartRuntimes) { if (r is IBodyObstacle b) obstacles[o++] = b; if (r is ICapsuleSet c) limbs[l++] = c; }
        }

        public PartStage Stage => PartStage.Late;

        void RootPose(out Vector3 root, out Quaternion frame)
        {
            int j = t.at.joint;
            Quaternion r = sim.Rotations[j];
            root = sim.Joints[j] + r * (rootOffset * scale);
            frame = r * rootFrame;
        }

        Vector3 Up => sim.Rotations[t.at.joint] * Vector3.up;

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            RootPose(out Vector3 root, out Quaternion frame);
            Vector3 up = Up;
            Chain.Gravity = -9.81f * up.normalized;
            float walk = Mathf.Max(1e-4f, sim.Species.speed * T);
            bool moving = sim.Speed > t.releaseSpeed * walk;
            stillTime = moving ? 0f : stillTime + dt;
            phaseTime += dt;
            if (t.clearance > 0f && obstacles == null) FindObstacles();
            if (t.mode == 1) { Maw(root, frame); Integrate(dt, root, frame); return; }
            // always a little writhe; more while hanging
            Chain.SinusAmount = State == Phase.Hold ? 0.15f : 0.6f;
            switch (State)
            {
                case Phase.Hang:
                    if (t.reach > 0f && stillTime > kSettleSeconds * T && phaseTime > 0.5f * T && GroundSpot(root, frame, out spot, out spotNormal))
                    {
                        State = Phase.Reach; phaseTime = 0f;
                        Chain.ShapePull = kShapePull;
                    }
                    break;
                case Phase.Reach:
                    if (moving || phaseTime > kReachSeconds * T) { Hang(); break; }
                    Chain.SetTargetShape(spot, 1f, false);
                    if ((Chain.X[n - 1] - spot).sqrMagnitude < kTouchDist * kTouchDist * length * length)
                    {
                        Chain.Pin(n - 1, spot, spotNormal, 0, true);
                        State = Phase.Hold; phaseTime = 0f; touches++;
                        holdTime = t.holdSeconds * T * (0.75f + 0.5f * Hash01(touches * 7 + ordinal * 13));
                    }
                    break;
                case Phase.Hold:
                    if (moving || (root - spot).sqrMagnitude > kOverstretch * kOverstretch * length * length) { Hang(); break; }
                    if (phaseTime > holdTime)
                    {
                        // feel for another spot nearby, or hang a while
                        Hang();
                        if (GroundSpot(root, frame, out spot, out spotNormal)) { State = Phase.Reach; phaseTime = 0f; Chain.ShapePull = kShapePull; }
                    }
                    break;
            }
            Integrate(dt, root, frame);
        }

        // The chain's sub-steps with its root eased from where it was to where it is (a root that jumped a whole tick
        // at the first sub-step flung the free run about), damped relative to the root's own velocity (a tendril on a
        // walking body is carried along, not dragged through still air).
        void Integrate(float dt, Vector3 root, Quaternion frame)
        {
            dt = Mathf.Min(dt, 0.1f);
            int subSteps = t.mode == 1 ? kMawSubSteps : kSubSteps, iterations = t.mode == 1 ? kMawIterations : kIterations;
            float h = dt / subSteps;
            Vector3 vel = (root - lastRoot) / dt;
            if (t.clearance > 0f)
                for (int o = 0; o < obstacles.Length; o++)
                {
                    obstacles[o].Ellipsoid(out obC[o], out obR[o], out obAx[o]);
                    obRi[o] = Quaternion.Inverse(obR[o]);
                }
            Ground(root, out Vector3 g, out Vector3 gn);
            for (int s = 0; s < subSteps; s++)
            {
                float f = (s + 1f) / subSteps;
                Chain.SetRoot(Vector3.Lerp(lastRoot, root, f), Quaternion.Slerp(lastFrame, frame, f));
                Chain.Integrate(h, vel);
                for (int it = 0; it < iterations; it++)
                {
                    Chain.SolveIteration(h); KeepAbove(g, gn);
                    if (t.clearance > 0f) KeepOut(false);
                }
                Chain.EndSubStep(h);   // last: the strict pass keeps every length exact (a contact may sink by a hair)
                // the strict pass may re-curl a tip into the body: clear it once more (a hair off exact lengths); the
                // limbs, the costly test, once per tick after the last sub-step (they are drawn where they are now)
                if (t.clearance > 0f) KeepOut(s == subSteps - 1);
            }
            Chain.UpdateFrames(dt);
            lastRoot = root; lastFrame = frame;
        }

        // ------------------------------------------------------------ maw

        void Maw(Vector3 root, Quaternion frame)
        {
            switch (State)
            {
                case Phase.Probe:
                    Chain.SinusAmount = 0.35f;
                    Chain.SetTargetShape(ProbeTarget(root, frame), 0.85f, false);
                    if (phaseTime > phaseLen) MawNext();
                    break;
                case Phase.Lash:
                    Chain.SinusAmount = 0f;
                    Chain.SetTargetShape(LashTarget(root), 1f, false);
                    if (phaseTime > phaseLen) MawNext();
                    break;
                default:
                    Chain.SinusAmount = 0.8f;   // hanging: writhing hard
                    if (phaseTime > phaseLen) MawNext();
                    break;
            }
        }

        // The next maw phase: a hang after a probe or a lash; after a hang a probe, or (lashEvery) a lash.
        void MawNext()
        {
            cycles++;
            float h = Hash01(cycles * 17 + ordinal * 29), u = Hash01(cycles * 23 + ordinal * 7 + 1);
            Chain.PullSpeed = kReachFroude * Mathf.Sqrt(9.81f * t.Spec.Length);
            if (State != Phase.Hang)
            {
                Chain.ClearTargetShape(); Chain.ShapePull = 0f;
                State = Phase.Hang; phaseLen = (kHangMin + kHangSpan * u) * T;
            }
            else if (t.lashEvery > 0f && h < Mathf.Clamp01((kHangMin + 0.5f * kHangSpan + kProbeMin + 0.5f * kProbeSpan) / t.lashEvery))
            {
                State = Phase.Lash; phaseLen = kLashSeconds * T; Lashes++;
                lashSide = u < 0.5f ? -1f : 1f;
                Chain.ShapePull = 1f;
                Chain.PullSpeed = kLashFroude * Mathf.Sqrt(9.81f * t.Spec.Length);
            }
            else
            {
                State = Phase.Probe; phaseLen = (kProbeMin + kProbeSpan * u) * T; Probes++;
                Chain.ShapePull = kShapePull;
            }
            phaseTime = 0f;
        }

        // Probing: a point toward the ground ahead of the root at reach x the length, sweeping side to side.
        Vector3 ProbeTarget(Vector3 root, Quaternion frame)
        {
            Vector3 up = Up.normalized;
            Vector3 d = frame * Vector3.forward;
            d -= up * Vector3.Dot(d, up);
            if (d.sqrMagnitude < 1e-8f) d = sim.Rotations[t.at.joint] * Vector3.forward;
            d.Normalize();
            float ph = 2f * Mathf.PI * t.probeHz * Chain.Time / T + 1.7f * ordinal;
            d = Quaternion.AngleAxis(45f * Mathf.Sin(ph), up) * d;
            Ground(root, out Vector3 g, out Vector3 gn);
            float h = Mathf.Max(0f, Vector3.Dot(root - g, gn) - radius[n - 1]);
            float r = Mathf.Max(0.3f, t.reach) * length * (0.85f + 0.15f * Mathf.Sin(0.63f * ph + ordinal));
            float down = Mathf.Clamp01(h / Mathf.Max(1e-6f, r));   // sine of the depression: on the ground when it reaches
            return root + (d * Mathf.Sqrt(1f - down * down) - gn * down) * r;
        }

        // Lashing: out ahead, a little up and to one side, at nearly the full length.
        Vector3 LashTarget(Vector3 root)
        {
            Quaternion body = sim.Rotations[t.at.joint];
            Vector3 dir = body * new Vector3(0.45f * lashSide, 0.25f, 1f).normalized;
            return root + dir * (0.95f * length);
        }

        // ------------------------------------------------------------ clearance

        // Keeps the particles outside every body obstacle (each pass) and the segments off every limb capsule (once a
        // tick: the costly test, with a box reject). A push keeps the particle's velocity minus the
        // part that went inward.
        void KeepOut(bool limbsToo)
        {
            TentacleChain c = Chain;
            float clr = t.clearance * scale;
            for (int o = 0; o < obstacles.Length; o++)
            {
                Vector3 ce = obC[o], h = obAx[o];
                Quaternion r = obR[o], ri = obRi[o];
                for (int k = 1; k < n; k++)
                {
                    if (c.Pinned[k]) continue;
                    float pad = radius[k] + clr;
                    Vector3 ax = new Vector3(h.x + pad, h.y + pad, h.z + pad);
                    Vector3 q = ri * (c.X[k] - ce);
                    Vector3 u = new Vector3(q.x / ax.x, q.y / ax.y, q.z / ax.z);
                    float m = u.magnitude;
                    if (m >= 1f) continue;
                    Vector3 un = m > 1e-6f ? u / m : Vector3.forward;
                    Vector3 to = ce + r * Vector3.Scale(un, ax);
                    Vector3 nrm = (r * new Vector3(un.x / ax.x, un.y / ax.y, un.z / ax.z)).normalized;
                    Move(k, to - c.X[k], nrm);
                }
            }
            if (!limbsToo || limbs.Length == 0) return;
            Vector3 lo = c.X[0], hi = c.X[0];
            for (int k = 1; k < n; k++) { lo = Vector3.Min(lo, c.X[k]); hi = Vector3.Max(hi, c.X[k]); }
            float grow = radius[0] + clr;
            lo -= new Vector3(grow, grow, grow); hi += new Vector3(grow, grow, grow);
            for (int s = 0; s < limbs.Length; s++)
            {
                ICapsuleSet set = limbs[s];
                int cc = set.CapsuleCount;
                for (int i = 0; i < cc; i++)
                {
                    set.Capsule(i, out Vector3 a, out Vector3 b, out float rl);
                    Vector3 clo = Vector3.Min(a, b) - new Vector3(rl, rl, rl), chi = Vector3.Max(a, b) + new Vector3(rl, rl, rl);
                    if (clo.x > hi.x || clo.y > hi.y || clo.z > hi.z || chi.x < lo.x || chi.y < lo.y || chi.z < lo.z) continue;
                    for (int k = 0; k + 1 < n; k++)
                    {
                        ClosestSegments(c.X[k], c.X[k + 1], a, b, out float st, out Vector3 pt, out Vector3 pl);
                        float need = rl + Mathf.Lerp(radius[k], radius[k + 1], st) + clr;
                        Vector3 dv = pt - pl;
                        float d = dv.magnitude;
                        if (d >= need) continue;
                        Vector3 nrm = d > 1e-7f ? dv / d : Vector3.Cross(b - a, Vector3.up).normalized;
                        if (nrm.sqrMagnitude < 0.5f) nrm = Vector3.right;
                        bool m0 = k > 0 && !c.Pinned[k], m1 = !c.Pinned[k + 1];
                        float w0 = m0 ? 1f - st : 0f, w1 = m1 ? st : 0f, ww = w0 * w0 + w1 * w1;
                        if (ww < 1e-3f) continue;
                        Vector3 push = nrm * ((need - d) / ww);
                        if (m0) Move(k, push * w0, nrm);
                        if (m1) Move(k + 1, push * w1, nrm);
                    }
                }
            }
        }

        void Move(int k, Vector3 delta, Vector3 nrm)
        {
            TentacleChain c = Chain;
            Vector3 v = c.X[k] - c.Prev[k];
            float vn = Vector3.Dot(v, nrm);
            if (vn < 0f) v -= nrm * vn;
            c.X[k] += delta;
            c.Prev[k] = c.X[k] - v;
        }

        // Closest points of segments p0-p1 and q0-q1 (Ericson): s along the first, the two points.
        static void ClosestSegments(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1, out float s, out Vector3 cp, out Vector3 cq)
        {
            Vector3 d1 = p1 - p0, d2 = q1 - q0, r = p0 - q0;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float tt;
            if (a <= 1e-12f && e <= 1e-12f) { s = 0f; tt = 0f; }
            else if (a <= 1e-12f) { s = 0f; tt = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-12f) { tt = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-12f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    tt = (b * s + f) / e;
                    if (tt < 0f) { tt = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (tt > 1f) { tt = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            cp = p0 + d1 * s; cq = q0 + d2 * tt;
        }

        void Hang()
        {
            Chain.UnpinAll();
            Chain.ClearTargetShape();
            Chain.ShapePull = 0f;
            State = Phase.Hang; phaseTime = 0f;
        }

        // The ground under the tendril: the plane of the nearest planted foot (its point and normal), else the body's
        // clearance below the root's joint along its up.
        void Ground(Vector3 root, out Vector3 g, out Vector3 gn)
        {
            Vector3 up = Up.normalized;
            g = sim.Joints[t.at.joint] - up * (sim.Species.joints[t.at.joint].clearance * scale);
            gn = up;
            float best = float.PositiveInfinity;
            LegState[] legs = sim.Legs;
            for (int l = 0; l < legs.Length; l++)
            {
                if (legs[l].stepping || sim.Species.legs[l].feeler) continue;
                float dd = (legs[l].foot - root).sqrMagnitude;
                if (dd < best) { best = dd; g = legs[l].foot; gn = legs[l].footNormal.sqrMagnitude > 0.5f ? legs[l].footNormal : up; }
            }
        }

        // Every free particle stays its radius above the ground plane; the part of a step that went into the ground
        // is taken out of its velocity too, and it slides with friction (it lies on the ground, never sinks in).
        void KeepAbove(Vector3 g, Vector3 gn)
        {
            TentacleChain c = Chain;
            for (int k = 1; k < n; k++)
            {
                if (c.Pinned[k]) continue;
                float r = radius[k];
                float d = Vector3.Dot(c.X[k] - g, gn) - r;
                if (d >= 0f) continue;
                c.X[k] -= gn * d;
                Vector3 v = c.X[k] - c.Prev[k];
                float vn = Vector3.Dot(v, gn);
                Vector3 vt = v - gn * vn;
                c.Prev[k] = c.X[k] - vt * (1f - kGroundFriction) - gn * Mathf.Max(0f, vn);
            }
        }

        // A spot on the ground ahead of the root: out along the rest direction (turned a little each touch), at reach x
        // the length from the root, on the plane of the nearest planted foot (or below the body without feet).
        bool GroundSpot(Vector3 root, Quaternion frame, out Vector3 p, out Vector3 normal)
        {
            Vector3 up = Up.normalized;
            Vector3 d = frame * Vector3.forward;
            d -= up * Vector3.Dot(d, up);
            if (d.sqrMagnitude < 1e-8f) d = sim.Rotations[t.at.joint] * Vector3.forward;
            d.Normalize();
            float yaw = (Hash01(touches * 31 + ordinal * 5) - 0.5f) * 50f;
            d = Quaternion.AngleAxis(yaw, up) * d;
            Ground(root, out Vector3 g, out Vector3 gn);
            float h = Vector3.Dot(root - g, gn);
            float horiz2 = t.reach * t.reach * length * length - h * h;
            if (h <= 0f || horiz2 <= 0f) { p = root; normal = gn; return false; }
            Vector3 dg = d - gn * Vector3.Dot(d, gn);
            dg = PartMeshEmitter.Unit(dg, d);
            p = root - gn * h + dg * Mathf.Sqrt(horiz2) + gn * radius[n - 1];
            normal = gn;
            return true;
        }

        static float Hash01(int k)
        {
            uint x = (uint)k * 0x9E3779B1u;
            x ^= x >> 15; x *= 0x85EBCA77u; x ^= x >> 13;
            return (x & 0xFFFFFF) / 16777216f;
        }

        public void WriteBones(Matrix4x4[] dst, int offset) => Chain.WriteBones(dst, offset + start);

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            Vector3 a = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), b = -a;
            Chain.Encapsulate(ref a, ref b);
            float r = t.rootRadius * 1.3f * scale;
            lo = Vector3.Min(lo, a - new Vector3(r, r, r));
            hi = Vector3.Max(hi, b + new Vector3(r, r, r));
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            var c = State == Phase.Hold ? new Color(1f, 0.3f, 0.8f, 1f) : State == Phase.Reach ? new Color(1f, 0.8f, 0.3f, 1f) : new Color(0.6f, 0.4f, 1f, 1f);
            for (int k = 0; k + 1 < n; k++) d.Line(Chain.X[k], Chain.X[k + 1], c);
            if (State != Phase.Hang) d.Point(spot, 0.01f * scale, c);
        }
    }
}
