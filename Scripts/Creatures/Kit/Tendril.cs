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
        public enum Phase { Hang = 0, Reach = 1, Hold = 2 }

        const int kSubSteps = 3, kIterations = 3;
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
            float h = dt / kSubSteps;
            Vector3 vel = (root - lastRoot) / dt;
            Ground(root, out Vector3 g, out Vector3 gn);
            for (int s = 0; s < kSubSteps; s++)
            {
                float f = (s + 1f) / kSubSteps;
                Chain.SetRoot(Vector3.Lerp(lastRoot, root, f), Quaternion.Slerp(lastFrame, frame, f));
                Chain.Integrate(h, vel);
                for (int it = 0; it < kIterations; it++) { Chain.SolveIteration(h); KeepAbove(g, gn); }
                Chain.EndSubStep(h);   // last: the strict pass keeps every length exact (a contact may sink by a hair)
            }
            Chain.UpdateFrames(dt);
            lastRoot = root; lastFrame = frame;
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
