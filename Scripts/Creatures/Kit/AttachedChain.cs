// Creature kit (Vesper step 2 groundwork): AttachedChain - a secondary chain connected directly onto the joints of the
// creature's main spine chain (the "mother chain"), at ONE end (a free end that lags, swings and droops) or at BOTH ends
// (a hanging strand between two mother joints: the skin pouch under a jaw, a flap from the chest to the hips), so each
// extra feature chain follows the mother chain's motion.
//   ends      the start is the part's socket (`at`: joint + offset in its frame, x mirrored by side); the end is
//             endJoint + endOffset (-1 = a free end). An attached end sits on its mother point EXACTLY at the end of
//             every tick, and is eased over the chain's own sub-steps from where it was (Tendril's lesson: a root that
//             jumped a whole tick at the first sub-step flung the free run). An end that jumped more than the chain's
//             length in one tick (a respawn, a teleport) snaps the whole chain onto its carried rest shape instead.
//   rest      one-ended: it leaves the start along `direction` (start joint frame) curling by curlDeg toward the
//             direction frame's -Y, `length` long. Two-ended: a circular arc from the start point to the end point,
//             bulging toward `direction` (made square to the chord), the rest chord x (1 + slack) long - so it hangs.
//             The rest is built ONCE per species (and part data) from the bind's rest (CreatureRestPose.BindSim, via
//             PartMeshEmitter.ForBind, as Dewlap does), at scale 1, and stored in the start joint's frame and in the end
//             joint's: every individual gets the same length and shape, whatever ground it spawned on. A chain joint
//             wants to be at the blend of the two carried copies (weight = its place along the chain).
//   physics   TentacleChain-style position-based dynamics on flat arrays (Verlet, a few sub-steps x Gauss-Seidel
//             passes): gravity (x gravity share, along the start joint's world up), a spring pull toward the carried rest
//             shape (stiffnessHz / sqrt s), velocity damped relative to that shape's own motion (damping / sqrt s: a
//             chain on a walking body is carried along, not dragged through still air), segment lengths, a bend cone
//             (a minimum chord across each interior joint), optional keep-out from the mother body (collideRadius > 0).
//             Every sub-step ends with a strict pass: one-ended, root to tip, every length exact and every interior
//             joint inside its cone; two-ended, FABRIK between the two pinned ends (forward and backward) with the cone,
//             the last residual spread evenly over the segments (micrometres).
//   keep-out  each free joint stays collideRadius outside the mother body's surface, swept between joints: on a
//             FlowBody its drawn section (FlowBody.Section3: hump, keel, flanks, blade tail), else the joints'
//             halfWidth x halfHeight ellipses. The surface is tabulated once per species (4 stations per segment x
//             96 polar bins, each bin the largest radius the section reaches in it: conservative) at scale 1.
//   bones     one per chain joint (segments + 1, contiguous; block Chain): origin the joint, +Z along the chain (the
//             last joint: the last segment's direction), +Y transported along the chain (rotation-minimising,
//             SurfaceMath.Transport) from the start joint's up; two-ended, the twist left at the end versus the end
//             joint's up (unwrapped against the previous frame's) is spread along the chain, so both ends sit square to
//             their mother joints (a skin lofted on it later does not twist). Scale = sizeScale (as the spine's).
//   radii     per joint (`radii`, or rootRadius .. tipRadius linearly): the skin later; F3 draws them as circles.
// No mesh in this step (EmitMesh emits nothing): it works on a SkeletonOnly species. Froude: lengths x s, times x sqrt s
// (the spring and damping rates / sqrt s, gravity size-free). Stage Late (once per tick, after every sub-step of every
// mode, so the mother joints are final for the tick; its own sub-steps ease the ends across the tick). Allocation-free
// after construction; main thread. Tendril / WhipTail / Antenna / Dewlap are unchanged.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("AttachedChain", PartKind.Chain, Bones = "= segments + 1",
          Summary = "A secondary chain attached directly onto the mother (spine) chain's joints: at one end (start socket; a free end that lags, swings and droops) or at both ends (start socket + endJoint/endOffset: a strand that hangs between two mother joints, its length the bind's chord x (1 + slack)). Each attached end follows its mother joint exactly; the rest shape (built once per species from the bind) is carried by the mother joints and pulled toward by a spring (stiffnessHz), damped relative to the mother's motion, gravity, exact lengths, a bend cone, optional keep-out from the mother body's drawn surface (collideRadius; a FlowBody's section). One bone per chain joint, frames transported from the mother joint's up; per-joint radii (the skin later), drawn by F3. Skeleton only for now (no mesh).",
          System = "AttachedChainRuntime (part runtime, stage Late: its own PBD sub-steps)", MaterialLayers = new int[0], KitOnly = true,
          UseWhen = "extra feature chains on a spine body that must follow it: skin pouches and flaps hung from jaw/neck joints to the chest (two-ended), wattles, streamers, lappets and free flaps (one-ended); the skeleton the skin is lofted on later")]
    public sealed class AttachedChain : CreaturePart
    {
        [Param(ParamUnit.Joint, "the mother joint the far end is attached to; -1 = a free end (one-ended). May be before or after the start joint", Min = -1, Max = 127, Default = "-1")]
        public int endJoint = -1;
        [Param(ParamUnit.Metres, "the far end's attachment in endJoint's frame (x mirrored by side)")]
        public Vector3 endOffset;
        [Param(ParamUnit.Count, "segments along the chain; bones = segments + 1 (one per chain joint)", Min = 2, Max = 24, Default = "8")]
        public int segments = 8;
        [Param(ParamUnit.Metres, "one-ended: the rest length along the chain (> 0). Two-ended: must be 0 - the length is the bind's chord x (1 + slack)", Min = 0, Default = "0", Example = "wattle 0.2; pouch 0")]
        public float length;
        [Param(ParamUnit.Fraction, "two-ended: extra length over the bind's distance between the ends, so it hangs (>= 0.1: a spine bending or straightening must never pull it taut)", Min = 0.1f, Max = 2f, Default = "0.25")]
        public float slack = 0.25f;
        [Param(ParamUnit.Metres, "joint radius at the start (the skin later)", Default = "0.02")]
        public float rootRadius = 0.02f;
        [Param(ParamUnit.Metres, "joint radius at the end", Default = "0.01")]
        public float tipRadius = 0.01f;
        [Param(ParamUnit.Metres, "per-joint radii, start first (segments + 1 values); empty = rootRadius .. tipRadius linearly")]
        public float[] radii = new float[0];
        [Param(ParamUnit.Direction, "rest: the direction it leaves the start joint (one-ended) or bulges toward (two-ended), start joint frame, x mirrored by side", Default = "(0, -1, 0)")]
        public Vector3 direction = new Vector3(0f, -1f, 0f);
        [Param(ParamUnit.Degrees, "one-ended rest curl, spread over the interior joints, toward the direction frame's -Y (+Y = the joint's up made square to the direction; a vertical direction: the joint's back)", Min = -180, Max = 180, Default = "0")]
        public float curlDeg;
        [Param(ParamUnit.Hz, "stiffness: natural frequency of the pull toward the rest shape carried by the mother joints (0 = limp: it only hangs)", Min = 0, Max = 10, Default = "1.5")]
        public float stiffnessHz = 1.5f;
        [Param(ParamUnit.Hz, "damping rate (1/s) of its velocity relative to the rest shape's own motion", Min = 0, Max = 40, Default = "3")]
        public float damping = 3f;
        [Param(ParamUnit.Fraction, "share of gravity it feels (1 = hangs fully, 0 = weightless)", Min = 0, Max = 1, Default = "1")]
        public float gravity = 1f;
        [Param(ParamUnit.Degrees, "bend cone per interior chain joint (two-ended: raised to the rest arc's bend if that is sharper)", Min = 5, Max = 90, Default = "35")]
        public float maxBendDeg = 35f;
        [Param(ParamUnit.Metres, "keep the free joints this far outside the mother body's surface (a FlowBody's drawn section, else the joints' halfWidth x halfHeight ellipses; swept between joints); 0 = off. Attach the ends on or just under that surface", Min = 0, Default = "0")]
        public float collideRadius;

        public const int MinSegments = 2, MaxSegments = 24;
        public const float MinSlack = 0.1f, MaxSlack = 2f;
        public int Segments => Mathf.Clamp(segments, MinSegments, MaxSegments);
        public int JointCount => Segments + 1;
        public bool TwoEnded => endJoint >= 0;
        public int Side => at.side < 0 ? -1 : at.side > 0 ? 1 : 0;

        public override int BoneCount(in SpeciesCtx c) => JointCount;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex (it emits none)

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: an AttachedChain hangs on a spine body's joints";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: start joint {at.joint} outside 0..{c.Joints - 1}";
            if (endJoint < -1 || endJoint >= c.Joints) return $"{id}: endJoint {endJoint} outside -1 (free end) .. {c.Joints - 1}";
            if (segments < MinSegments || segments > MaxSegments) return $"{id}: segments must be {MinSegments}..{MaxSegments}";
            if (TwoEnded)
            {
                if (endJoint == at.joint && (endOffset - at.offset).sqrMagnitude < 1e-8f) return $"{id}: both ends at the same point";
                if (length != 0f) return $"{id}: a two-ended chain's length is the bind's chord x (1 + slack): length must be 0";
                if (!(slack >= MinSlack) || slack > MaxSlack) return $"{id}: slack must be {MinSlack}..{MaxSlack} (less is pulled taut by a bending spine)";
            }
            else if (!(length > 0f)) return $"{id}: a one-ended chain needs length > 0";
            if (!(rootRadius > 0f) || tipRadius < 0f) return $"{id}: rootRadius > 0, tipRadius >= 0";
            if (radii != null && radii.Length != 0)
            {
                if (radii.Length != JointCount) return $"{id}: radii needs segments + 1 = {JointCount} values (or none)";
                for (int i = 0; i < radii.Length; i++) if (!(radii[i] >= 0f)) return $"{id}: radii[{i}] must be >= 0";
            }
            if (direction.sqrMagnitude < 1e-8f) return $"{id}: direction is zero";
            if (stiffnessHz < 0f || stiffnessHz > 10f) return $"{id}: stiffnessHz must be 0..10";
            if (damping < 0f || damping > 40f) return $"{id}: damping must be 0..40";
            if (gravity < 0f || gravity > 1f) return $"{id}: gravity must be 0..1";
            if (maxBendDeg < 5f || maxBendDeg > 90f) return $"{id}: maxBendDeg must be 5..90";
            if (collideRadius < 0f) return $"{id}: collideRadius must be >= 0";
            return null;
        }

        // joint k's radius at scale 1
        public float JointRadius(int k)
        {
            if (radii != null && radii.Length == JointCount) return radii[Mathf.Clamp(k, 0, JointCount - 1)];
            return Mathf.Lerp(rootRadius, tipRadius, k / (float)Segments);
        }

        protected override void CopyArrays() { radii = radii != null ? (float[])radii.Clone() : new float[0]; }

        // ------------------------------------------------------------ rest geometry (once per species)

        // test hooks (RED proofs): the rest from each individual's spawn pose (the old way); the keep-out on the joints'
        // ellipses even on a FlowBody
        internal static bool SpawnRestForTest, EllipseKeepOutForTest;

        // The rest at scale 1: chain joints relative to the start / end joint (its frame), the end's carry weight, the
        // segment length, the rest bend and the cone; the body surface table for the keep-out (null when off).
        internal sealed class RestGeo
        {
            public Vector3[] restA, restB;
            public float[] wB;
            public float seg, restBendDeg, coneDeg;
            public float[] surface;   // [station * SurfaceBins + bin], radius in the section plane (m, scale 1)
            public int stations;
        }

        public const int StationsPerSegment = 4, SurfaceBins = 96;

        [NonSerialized] RestGeo geo;
        [NonSerialized] CreatureSpecies geoFor;
        [NonSerialized] int geoKey;
        [ThreadStatic] static bool buildingRest;

        int GeoKey()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + at.joint; h = h * 31 + Side; h = h * 31 + at.offset.GetHashCode(); h = h * 31 + endJoint; h = h * 31 + endOffset.GetHashCode();
                h = h * 31 + segments; h = h * 31 + length.GetHashCode(); h = h * 31 + slack.GetHashCode(); h = h * 31 + direction.GetHashCode();
                h = h * 31 + curlDeg.GetHashCode(); h = h * 31 + maxBendDeg.GetHashCode(); h = h * 31 + (collideRadius > 0f ? 1 : 0);
                h = h * 31 + (EllipseKeepOutForTest ? 1 : 0);
                return h;
            }
        }

        // The species' rest (cached by species and part data), from the bind's rest sim.
        internal RestGeo Geometry(CreatureSpecies sp)
        {
            int key = GeoKey();
            if (geo != null && geoFor == sp && geoKey == key) return geo;
            PartMeshEmitter m;
            buildingRest = true;   // the bind rest sim's own AttachedChain runtimes are not needed (and would recurse)
            try { m = PartMeshEmitter.ForBind(sp); }
            finally { buildingRest = false; }
            RestGeo g = BuildGeo(sp, m.Rest.Joints, m.Rest.Rotations, 1f);
            geo = g; geoFor = sp; geoKey = key;
            return g;
        }

        internal RestGeo BuildGeo(CreatureSpecies sp, Vector3[] J, Quaternion[] R, float scale)
        {
            int n = JointCount, side = Side == 0 ? 1 : Side;
            var g = new RestGeo { restA = new Vector3[n], restB = new Vector3[n], wB = new float[n] };
            Vector3 offA = at.offset; offA.x *= side;
            Vector3 d = direction; d.x *= side;
            d = PartMeshEmitter.Unit(d, Vector3.down);
            int ja = at.joint;
            Quaternion RA = R[ja], ia = Quaternion.Inverse(RA);
            if (!TwoEnded)
            {
                g.seg = length / (n - 1);
                Vector3 up = Vector3.up - d * Vector3.Dot(Vector3.up, d);
                if (up.sqrMagnitude < 1e-6f) up = Vector3.back - d * Vector3.Dot(Vector3.back, d);
                Quaternion f = Quaternion.LookRotation(d, up.normalized);
                float per = n > 2 ? curlDeg / (n - 2) : 0f;
                g.restBendDeg = Mathf.Abs(per);
                Vector3 q = offA;
                for (int k = 0; k < n; k++)
                {
                    g.restA[k] = q;
                    float a = per * k * Mathf.Deg2Rad;
                    q += f * new Vector3(0f, -Mathf.Sin(a), Mathf.Cos(a)) * g.seg;
                }
            }
            else
            {
                int jb = endJoint;
                Vector3 offB = endOffset; offB.x *= side;
                Quaternion RB = R[jb], ib = Quaternion.Inverse(RB);
                Vector3 A = J[ja] + RA * (offA * scale), B = J[jb] + RB * (offB * scale);
                Vector3 chord = B - A;
                float D = chord.magnitude;
                Vector3 e = D > 1e-7f ? chord / D : RA * Vector3.forward;
                float L = D * (1f + Mathf.Max(MinSlack, slack));
                g.seg = L / (n - 1) / scale;
                Vector3 u = RA * d;
                u -= e * Vector3.Dot(u, e);
                if (u.sqrMagnitude < 1e-8f) { u = RA * Vector3.down; u -= e * Vector3.Dot(u, e); }
                if (u.sqrMagnitude < 1e-8f) { u = RA * Vector3.right; u -= e * Vector3.Dot(u, e); }
                u.Normalize();
                // the arc of length L on the chord D: sin(x) / x = D / L, x = half the arc's angle (bisection on 0..pi)
                float ratio = D / L, lo = 1e-6f, hi = Mathf.PI - 1e-6f;
                for (int it = 0; it < 60; it++) { float mid = 0.5f * (lo + hi); if (Mathf.Sin(mid) / mid > ratio) lo = mid; else hi = mid; }
                float half = 0.5f * (lo + hi), theta = 2f * half, Rr = L / theta;
                Vector3 centre = 0.5f * (A + B) - u * (Rr * Mathf.Cos(half));
                for (int k = 0; k < n; k++)
                {
                    float phi = -half + theta * k / (n - 1);
                    Vector3 w = k == 0 ? A : k == n - 1 ? B : centre + Rr * (Mathf.Sin(phi) * e + Mathf.Cos(phi) * u);
                    g.restA[k] = ia * (w - J[ja]) / scale;
                    g.restB[k] = ib * (w - J[jb]) / scale;
                    g.wB[k] = k / (float)(n - 1);
                }
                g.restBendDeg = theta * Mathf.Rad2Deg / (n - 1);
            }
            g.coneDeg = Mathf.Min(170f, Mathf.Max(maxBendDeg, g.restBendDeg * 1.1f + 0.5f));
            if (collideRadius > 0f) BuildSurface(sp, J.Length, g);
            return g;
        }

        // The mother body's surface at every station (4 per segment), in polar bins of the section plane: the largest
        // radius the section reaches within each bin (and across each bin a section edge spans), scale 1.
        void BuildSurface(CreatureSpecies sp, int mj, RestGeo g)
        {
            FlowBody body = null;
            if (!EllipseKeepOutForTest)
                foreach (CreaturePart q in KitView.Parts(sp)) if (q is FlowBody f) { body = f; break; }
            const int K = StationsPerSegment, B = SurfaceBins, kSamples = 512;
            int st = (mj - 1) * K + 1;
            g.stations = st;
            g.surface = new float[st * B];
            for (int s = 0; s < st; s++)
            {
                float place = s / (float)K;
                int j0 = Mathf.Min((int)place, mj - 1), j1 = Mathf.Min(j0 + 1, mj - 1);
                float t = place - j0;
                float hw = Mathf.Lerp(sp.joints[j0].halfWidth, sp.joints[j1].halfWidth, t), hh = Mathf.Lerp(sp.joints[j0].halfHeight, sp.joints[j1].halfHeight, t);
                float u = body != null ? body.UAtJoint(place) : 0f;
                Vector2 Sample(float phi)
                {
                    if (body != null) { Vector3 o = body.Section3(u, phi); return new Vector2(o.x, o.y); }
                    return new Vector2(hw * Mathf.Cos(phi), hh * Mathf.Sin(phi));
                }
                Vector2 p0 = Sample(-Mathf.PI);
                for (int i = 1; i <= kSamples; i++)
                {
                    Vector2 p1 = Sample(-Mathf.PI + 2f * Mathf.PI * i / kSamples);
                    int b0 = Bin(p0), b1 = Bin(p1);
                    float r = Mathf.Max(p0.magnitude, p1.magnitude);
                    // every bin from b0 to b1 the shorter way round
                    int span = ((b1 - b0) % B + B) % B, dir = 1;
                    if (span > B / 2) { span = B - span; dir = -1; }
                    for (int k = 0; k <= span; k++)
                    {
                        int b = ((b0 + dir * k) % B + B) % B;
                        if (r > g.surface[s * B + b]) g.surface[s * B + b] = r;
                    }
                    p0 = p1;
                }
            }
        }

        public static int Bin(Vector2 p) => Bin(Mathf.Atan2(p.y, p.x));
        public static int Bin(float theta)
        {
            int b = (int)((theta + Mathf.PI) / (2f * Mathf.PI) * SurfaceBins);
            return b < 0 ? 0 : b >= SurfaceBins ? SurfaceBins - 1 : b;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
        {
            if (c.Sim == null || buildingRest) return null;
            RestGeo g = SpawnRestForTest ? BuildGeo(c.Species, c.Sim.Joints, c.Sim.Rotations, c.Size.L) : Geometry(c.Species);
            return new AttachedChainRuntime(this, c.Sim, g, c.Species.Layout.Start(c.Index), c.Size);
        }
    }

    // One AttachedChain of one individual.
    public sealed class AttachedChainRuntime : IPartRuntime
    {
        public const int SubSteps = 4, Iterations = 4, FabrikPasses = 24;
        const float kMaxSpeedFroude = 40f;   // m/s at scale 1 (x sqrt s): a hitch cannot explode the chain
        const float kMaxDt = 0.1f;           // s at scale 1 (x sqrt s)

        readonly AttachedChain p;
        readonly AttachedChain.RestGeo geo;
        readonly CreatureSim sim;
        readonly int start, n, ja, jb;
        readonly bool two;
        readonly float scale, T, seg, omega2, dampRate, cosCone, minChord, maxSpeed, pad;
        readonly Vector3 offA, offB;          // attachment offsets in their joint frames (x mirrored, at scale 1)
        readonly float[] radius;              // per joint (m, this individual)
        public readonly Vector3[] X, Prev;
        readonly Vector3[] tgt, prevTgt;
        readonly Quaternion[] frames;
        readonly Vector3[] mRight, mUp;       // the mother joints' world right / up (this tick)
        Vector3 lastA, lastB;
        Quaternion lastRA, lastRB;
        float lastTwist;

        // test hooks (RED proofs): skip the strict pass; rates not Froude-scaled; no snap on a jump; no end-twist spread
        internal static bool NoStrictForTest, NoFroudeForTest, NoSnapForTest, NoTwistSpreadForTest;

        public AttachedChain Part => p;
        public int FirstBone => start;
        public int JointCount => n;
        public bool TwoEnded => two;
        public float SegmentLength => seg;
        public float Length => seg * (n - 1);
        // the cone the solver holds (degrees): maxBendDeg, or the rest arc's per-joint bend if that is sharper
        public float ConeDeg => geo.coneDeg;
        public float RestBendDeg => geo.restBendDeg;
        public int Snaps { get; private set; }
        public Vector3 Joint(int k) => X[k];
        public float Radius(int k) => radius[k];
        public Quaternion Frame(int k) => frames[k];
        // joint k's rest relative to the start joint, in its frame, at scale 1 (the same for every individual)
        public Vector3 RestLocal(int k) => geo.restA[k];
        public Vector3 StartPoint { get { Attach(ja, offA, out Vector3 q, out _); return q; } }
        public Vector3 EndPoint { get { if (!two) return X[n - 1]; Attach(jb, offB, out Vector3 q, out _); return q; } }
        // where joint k's rest shape is now (carried by the mother joints)
        public Vector3 RestNow(int k) => Carried(k, sim.Joints[ja], sim.Rotations[ja], two ? sim.Joints[jb] : default, two ? sim.Rotations[jb] : Quaternion.identity);

        Vector3 Carried(int k, Vector3 oa, Quaternion ra, Vector3 ob, Quaternion rb)
        {
            Vector3 q = oa + ra * (geo.restA[k] * scale);
            return two ? Vector3.Lerp(q, ob + rb * (geo.restB[k] * scale), geo.wB[k]) : q;
        }

        internal AttachedChainRuntime(AttachedChain part, CreatureSim sim, AttachedChain.RestGeo geo, int firstBone, SizeCtx size)
        {
            p = part; this.geo = geo; this.sim = sim; start = firstBone;
            n = part.JointCount; ja = part.at.joint; jb = part.endJoint; two = part.TwoEnded;
            int side = part.Side == 0 ? 1 : part.Side;
            scale = size.L; T = size.T;
            offA = part.at.offset; offA.x *= side;
            offB = part.endOffset; offB.x *= side;
            X = new Vector3[n]; Prev = new Vector3[n]; tgt = new Vector3[n]; prevTgt = new Vector3[n];
            radius = new float[n]; frames = new Quaternion[n];
            int mj = sim.Joints.Length;
            mRight = new Vector3[mj]; mUp = new Vector3[mj];
            for (int k = 0; k < n; k++) radius[k] = part.JointRadius(k) * scale;
            seg = geo.seg * scale;
            cosCone = Mathf.Cos(geo.coneDeg * Mathf.Deg2Rad);
            minChord = 2f * seg * Mathf.Cos(0.5f * geo.coneDeg * Mathf.Deg2Rad);
            float rateT = NoFroudeForTest ? 1f : T;
            float w = 2f * Mathf.PI * part.stiffnessHz / rateT;
            omega2 = w * w;
            dampRate = part.damping / rateT;
            maxSpeed = kMaxSpeedFroude * T;
            pad = part.collideRadius * scale;
            Attach(ja, offA, out lastA, out lastRA);
            if (two) Attach(jb, offB, out lastB, out lastRB);
            Snap();
        }

        public PartStage Stage => PartStage.Late;

        // mother joint j's attachment point for an offset (scale 1, mirrored) and the joint's rotation
        void Attach(int j, Vector3 off, out Vector3 q, out Quaternion r)
        {
            r = sim.Rotations[j];
            q = sim.Joints[j] + r * (off * scale);
        }

        // Onto the carried rest shape (lengths made exact), at rest: construction, and an end that jumped (respawn).
        void Snap()
        {
            for (int j = 0; j < mRight.Length; j++) { mRight[j] = sim.Rotations[j] * Vector3.right; mUp[j] = sim.Rotations[j] * Vector3.up; }
            for (int k = 0; k < n; k++) X[k] = RestNow(k);
            X[0] = lastA;
            if (two) { X[n - 1] = lastB; StrictTwoEnded(false); }
            else StrictOneEnded(false);
            for (int k = 0; k < n; k++) { Prev[k] = X[k]; tgt[k] = RestNow(k); prevTgt[k] = tgt[k]; }
            lastTwist = 0f;   // the principal twist
            UpdateFrames();
        }

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            dt = Mathf.Min(dt, kMaxDt * T);
            Attach(ja, offA, out Vector3 A, out Quaternion RA);
            Vector3 B = default; Quaternion RB = Quaternion.identity;
            if (two) Attach(jb, offB, out B, out RB);
            float L = Length;
            if (!NoSnapForTest && ((A - lastA).sqrMagnitude > L * L || (two && (B - lastB).sqrMagnitude > L * L)))
            {
                lastA = A; lastRA = RA; lastB = B; lastRB = RB;
                Snap(); Snaps++;
                return;
            }
            Vector3 g = sim.Up[ja];
            g = -9.81f * p.gravity * (g.sqrMagnitude > 1e-8f ? g.normalized : Vector3.up);
            bool collide = pad > 0f && geo.surface != null;
            if (collide)
                for (int j = 0; j < mRight.Length; j++) { mRight[j] = sim.Rotations[j] * Vector3.right; mUp[j] = sim.Rotations[j] * Vector3.up; }
            float h = dt / AttachedChainRuntime.SubSteps;
            float decay = Mathf.Exp(-dampRate * h), vmax = maxSpeed * h;
            for (int s = 0; s < SubSteps; s++)
            {
                float f = (s + 1f) / SubSteps;
                Quaternion ra = Quaternion.Slerp(lastRA, RA, f);
                Vector3 pa = Vector3.Lerp(lastA, A, f);
                Vector3 oa = pa - ra * (offA * scale);   // the start joint's origin this sub-step
                Quaternion rb = Quaternion.identity; Vector3 pb = default, ob = default;
                if (two) { rb = Quaternion.Slerp(lastRB, RB, f); pb = Vector3.Lerp(lastB, B, f); ob = pb - rb * (offB * scale); }
                // the carried rest shape, this sub-step
                for (int k = 0; k < n; k++) { prevTgt[k] = tgt[k]; tgt[k] = Carried(k, oa, ra, ob, rb); }
                // Verlet: gravity + the spring toward the carried shape, velocity damped relative to the shape's motion
                int last = two ? n - 1 : n;
                for (int k = 1; k < last; k++)
                {
                    Vector3 v = X[k] - Prev[k];
                    Vector3 refV = tgt[k] - prevTgt[k];
                    v = refV + (v - refV) * decay;
                    Vector3 rel = v - refV;
                    float rm = rel.magnitude;
                    if (rm > vmax) v = refV + rel * (vmax / rm);
                    Vector3 a = g + omega2 * (tgt[k] - X[k]);
                    Prev[k] = X[k];
                    X[k] += v + a * (h * h);
                }
                X[0] = pa; Prev[0] = pa;
                if (two) { X[n - 1] = pb; Prev[n - 1] = pb; }
                for (int it = 0; it < Iterations; it++)
                {
                    SolveDistances((it & 1) == 1 && two);
                    SolveCones();
                    if (collide) for (int k = 1; k < last; k++) X[k] = KeepOut(X[k]);
                }
                if (NoStrictForTest) continue;
                if (two) StrictTwoEnded(collide); else StrictOneEnded(collide);
            }
            lastA = A; lastRA = RA;
            if (two) { lastB = B; lastRB = RB; }
            UpdateFrames();
        }

        float W(int k) => k == 0 || (two && k == n - 1) ? 0f : 1f;

        void SolveDistances(bool backward)
        {
            for (int i = 0; i < n - 1; i++)
            {
                int k = backward ? n - 2 - i : i;
                float w0 = W(k), w1 = W(k + 1), ws = w0 + w1;
                if (ws <= 0f) continue;
                Vector3 dv = X[k + 1] - X[k];
                float d = dv.magnitude;
                if (d < 1e-9f) continue;
                Vector3 corr = dv * ((d - seg) / (d * ws));
                X[k] += corr * w0;
                X[k + 1] -= corr * w1;
            }
        }

        // the bend cone as a minimum chord across each interior joint (law of cosines on the rest lengths)
        void SolveCones()
        {
            for (int k = 1; k < n - 1; k++)
            {
                float w0 = W(k - 1), w1 = W(k + 1), ws = w0 + w1;
                if (ws <= 0f) continue;
                Vector3 dv = X[k + 1] - X[k - 1];
                float d = dv.magnitude;
                if (d >= minChord || d < 1e-9f) continue;
                Vector3 corr = dv * ((minChord - d) / (d * ws));
                X[k - 1] -= corr * w0;
                X[k + 1] += corr * w1;
            }
        }

        // Places X[to] exactly seg from X[from], inside the cone at `from` (whose previous joint is `back`, or -1).
        void Place(int back, int from, int to, bool collide)
        {
            Vector3 cand = X[to];
            if (collide) cand = KeepOut(cand);
            Vector3 dir = cand - X[from];
            float m = dir.magnitude;
            Vector3 prevDir = back >= 0 ? X[from] - X[back] : Vector3.zero;
            float pm = prevDir.magnitude;
            if (m < 1e-9f) dir = pm > 1e-9f ? prevDir / pm : Vector3.down;
            else dir /= m;
            if (pm > 1e-9f)
            {
                prevDir /= pm;
                float cs = Vector3.Dot(prevDir, dir);
                if (cs < cosCone)
                {
                    // onto the cone: keep the plane of the bend, the angle at the limit
                    Vector3 perp = dir - prevDir * cs;
                    float pl = perp.magnitude;
                    perp = pl > 1e-7f ? perp / pl : AnyPerp(prevDir);
                    float sn = Mathf.Sqrt(Mathf.Max(0f, 1f - cosCone * cosCone));
                    dir = prevDir * cosCone + perp * sn;
                }
            }
            X[to] = X[from] + dir * seg;
        }

        static Vector3 AnyPerp(Vector3 v)
        {
            Vector3 a = Mathf.Abs(v.y) < 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Cross(v, a).normalized;
        }

        // root to tip: every length exact, every interior joint inside its cone
        void StrictOneEnded(bool collide)
        {
            for (int k = 0; k < n - 1; k++) Place(k - 1, k, k + 1, collide);
        }

        // FABRIK between the two pinned ends (with the cone), then the residual at the end spread over every segment
        void StrictTwoEnded(bool collide)
        {
            Vector3 A = X[0], B = X[n - 1];
            for (int pass = 0; pass < FabrikPasses; pass++)
            {
                // backward: from the end
                X[n - 1] = B;
                for (int k = n - 1; k > 0; k--) Place(k + 1 < n ? k + 1 : -1, k, k - 1, collide && k - 1 > 0);
                // forward: from the start
                X[0] = A;
                for (int k = 0; k < n - 1; k++) Place(k - 1, k, k + 1, collide && k + 1 < n - 1);
                if ((X[n - 1] - B).sqrMagnitude < 1e-10f * seg * seg) break;
            }
            Vector3 r = B - X[n - 1];
            for (int k = 1; k < n; k++) X[k] += r * (k / (float)(n - 1));
            X[n - 1] = B; X[0] = A;
        }

        // Where q is against the mother body: the mother segment j it lies along (t in 0..1; past either end of the
        // mother chain the end joint), its point in that station's section plane (x right, y up) and the surface's
        // radius in its polar bin (m, this individual, not inflated). False when no segment is near enough.
        bool Against(Vector3 q, int j, out float t, out Vector3 cpt, out Vector3 rt, out Vector3 up, out float x, out float y, out float surf)
        {
            Vector3[] J = sim.Joints;
            int mj = J.Length;
            t = 0f; cpt = default; rt = default; up = default; x = 0f; y = 0f; surf = 0f;
            Vector3 a = J[j], ab = J[j + 1] - a;
            float l2 = ab.sqrMagnitude;
            if (l2 < 1e-12f) return false;
            t = Vector3.Dot(q - a, ab) / l2;
            if (t < 0f) { if (j > 0) return false; t = 0f; }
            if (t > 1f) { if (j + 2 < mj) return false; t = 1f; }
            cpt = a + ab * t;
            Vector3 ax = ab / Mathf.Sqrt(l2);
            rt = Vector3.Lerp(mRight[j], mRight[j + 1], t); up = Vector3.Lerp(mUp[j], mUp[j + 1], t);
            rt -= ax * Vector3.Dot(rt, ax); rt.Normalize();
            up -= ax * Vector3.Dot(up, ax) + rt * Vector3.Dot(up, rt); up.Normalize();
            Vector3 dq = q - cpt;
            x = Vector3.Dot(dq, rt); y = Vector3.Dot(dq, up);
            // the station: the larger of the two bracketing ones (conservative)
            const int K = AttachedChain.StationsPerSegment, B = AttachedChain.SurfaceBins;
            float place = (j + t) * K;
            int s0 = Mathf.Min((int)place, geo.stations - 1), s1 = Mathf.Min(s0 + 1, geo.stations - 1);
            int bin = (x * x + y * y) > 1e-14f ? AttachedChain.Bin(Mathf.Atan2(y, x)) : AttachedChain.Bin(-0.5f * Mathf.PI);
            surf = Mathf.Max(geo.surface[s0 * B + bin], geo.surface[s1 * B + bin]) * scale;
            return true;
        }

        // Pushes a point out of the mother body, radially in each near station's section plane, to the surface + pad.
        Vector3 KeepOut(Vector3 q)
        {
            int mj = sim.Joints.Length;
            for (int j = 0; j + 1 < mj; j++)
            {
                if (!Against(q, j, out _, out Vector3 cpt, out Vector3 rt, out Vector3 up, out float x, out float y, out float surf)) continue;
                float need = surf + pad, r2 = x * x + y * y;
                if (r2 >= need * need) continue;
                float r = Mathf.Sqrt(r2);
                if (r < 1e-7f) { q = cpt - up * need; continue; }   // on the axis: out below
                q = cpt + (rt * x + up * y) * (need / r) + (q - cpt - rt * x - up * y);   // keep the along-axis part
            }
            return q;
        }

        // How deep q is inside the mother body's tabulated surface (inflated by `extra` metres): the keep-out's own model.
        public float BodyDepth(Vector3 q, float extra)
        {
            if (geo.surface == null) return 0f;
            for (int j = 0; j < mRight.Length; j++) { mRight[j] = sim.Rotations[j] * Vector3.right; mUp[j] = sim.Rotations[j] * Vector3.up; }
            float worst = 0f;
            for (int j = 0; j + 1 < sim.Joints.Length; j++)
            {
                if (!Against(q, j, out _, out _, out _, out _, out float x, out float y, out float surf)) continue;
                worst = Mathf.Max(worst, surf + extra - Mathf.Sqrt(x * x + y * y));
            }
            return worst;
        }

        // The twist (deg about the last tangent) from the transported up at the end to the end joint's up, unwrapped
        // against the last one (a twist through +-180 does not flip the spread).
        float TwistAngle()
        {
            Vector3 tl = Tangent(n - 1);
            Vector3 want = sim.Rotations[jb] * Vector3.up;
            want -= tl * Vector3.Dot(want, tl);
            if (want.sqrMagnitude < 1e-8f) return lastTwist;
            want.Normalize();
            Vector3 have = frames[n - 1] * Vector3.up;
            float ang = Mathf.Atan2(Vector3.Dot(Vector3.Cross(have, want), tl), Vector3.Dot(have, want)) * Mathf.Rad2Deg;
            float d = ang - lastTwist;
            d -= 360f * Mathf.Round(d / 360f);
            return lastTwist + d;
        }

        // Frames: +Z along the chain, +Y transported (rotation-minimising) from the start joint's up; two-ended, the
        // twist left at the end against the end joint's up is spread along the chain.
        void UpdateFrames()
        {
            Vector3 t0 = Tangent(0);
            Vector3 up = sim.Rotations[ja] * Vector3.up;
            up -= t0 * Vector3.Dot(up, t0);
            if (up.sqrMagnitude < 1e-8f) { up = sim.Rotations[ja] * Vector3.back; up -= t0 * Vector3.Dot(up, t0); }
            up.Normalize();
            Vector3 tp = t0;
            for (int k = 0; k < n; k++)
            {
                Vector3 t = Tangent(k);
                if (k > 0)
                {
                    up = SurfaceMath.Transport(up, tp, t);
                    up -= t * Vector3.Dot(up, t);
                    float m = up.magnitude;
                    up = m > 1e-7f ? up / m : AnyPerp(t);
                }
                frames[k] = Quaternion.LookRotation(t, up);
                tp = t;
            }
            if (!two || NoTwistSpreadForTest) return;
            float ang = TwistAngle();
            lastTwist = ang;
            for (int k = 1; k < n; k++)
                frames[k] = Quaternion.AngleAxis(ang * k / (n - 1), Tangent(k)) * frames[k];
        }

        Vector3 Tangent(int k)
        {
            Vector3 d = k < n - 1 ? X[k + 1] - X[k] : X[n - 1] - X[n - 2];
            float m = d.magnitude;
            return m > 1e-9f ? d / m : Vector3.down;
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            Vector3 s = new Vector3(scale, scale, scale);
            for (int k = 0; k < n; k++) dst[offset + start + k] = Matrix4x4.TRS(X[k], frames[k], s);
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            for (int k = 0; k < n; k++)
            {
                float r = radius[k];
                Vector3 e = new Vector3(r, r, r);
                lo = Vector3.Min(lo, X[k] - e);
                hi = Vector3.Max(hi, X[k] + e);
            }
        }

        static readonly Color kLine = new Color(1f, 0.55f, 0.75f, 1f), kJoint = new Color(1f, 0.85f, 0.9f, 1f), kEnd = new Color(0.4f, 1f, 0.6f, 1f);

        // F3: the chain, a circle of its radius at every joint, the attached ends marked
        public void DrawDebug(ICreatureDebugDraw d)
        {
            for (int k = 0; k + 1 < n; k++) d.Line(X[k], X[k + 1], kLine);
            for (int k = 0; k < n; k++) d.Point(X[k], radius[k], (k == 0 || (two && k == n - 1)) ? kEnd : kJoint);
        }
    }
}
