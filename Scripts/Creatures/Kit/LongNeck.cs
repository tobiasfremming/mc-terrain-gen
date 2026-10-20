// Creature kit: LongNeck - a long neck carried off the trunk on bones of its own, its last bone the head's: an
// azhdarchid's, a giraffe's, a sauropod's, a swan's, a plesiosaur's. The spine body's chain ends at the chest (its
// joint `at.joint`); the neck is not dragged along the walking path but carried by the chest as a chain (root to tip):
//   posture   an S from the chest, measured in the chest's own frame (its heading, its pitch): the base rises baseDeg over
//             the chest's line on the ground, flightBaseDeg in the air (following basePitchFollow of the chest's pitch:
//             raised over a nose-down body; a folded-wing dive stretches it along the body), never more than maxBaseDeg
//             over the chest's line (no neck leaning back); the head arrives at headDeg (ground) or near level in the
//             air (headPitchFollow of the pitch, within headLevelDeg); a call raises it (callDeg, Calling);
//   head      a critically damped follower of that posture's head point (stabiliseSeconds): the head trails the
//             chest's turns by at most maxLagDeg (follow-through), its height filtered against the wingbeat's bob; it
//             glances about when idle (glanceDeg) and turns toward an aim point (IHeadAim: a hunting behaviour); in the
//             air the neck points at it within strikeConeDeg of its forward and a strike stretches the head along the
//             neck (strikeReach) - never under or behind the neck; never below headClearance over the ground;
//   shape     a cubic Hermite from the root (along the base direction, riding the chest's pitch) to the head (along the
//             head direction), its tangents those of a circular arc (no loop, no bump); bones at equal arc fractions,
//             the last at the head along the head direction.
// Mesh: elliptical rings (pale throat: the belly shading) tapering from the root (buried in the chest, rigid to the
// first bone: a ball socket - the base swings ~60 deg between the stance and flight) to the head (closed inside it),
// each ring between its two bones. Allocation-free after construction; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // A part that can turn the head toward a point and reach for it (a hunting behaviour drives it).
    public interface IHeadAim
    {
        // aim at `point` (world) with this strike reach (0 = only look, 1 = stretch the head onto it); clear with Release
        void Aim(Vector3 point, float strike);
        void Release();
        Vector3 HeadPosition { get; }
        Vector3 StrikePoint { get; }   // what strikes: the head part's tip (LongNeck.tipReach along the head)
    }

    [Serializable]
    [Part("LongNeck", PartKind.Chain, Bones = "= segments (the last is the head's bone)",
          Summary = "A long neck on bones of its own from a spine joint (the chest), held in a posture (raised on the ground, stretched forward in the air) instead of dragged along the path: a stabilised head that glides while the body bobs, glances, calls, turns toward an aim point and strikes. Its last bone carries a head part (BeakHead).",
          System = "LongNeckRuntime (part runtime, stage Late: posture, head follower, Hermite neck) + LongNeck.EmitMesh (rings)",
          MaterialLayers = new[] { 0 }, KitOnly = true,
          UseWhen = "long necks that are carried, not dragged: pterosaurs, giraffes, sauropods, swans, plesiosaurs; any head held up off the body")]
    public sealed partial class LongNeck : CreaturePart
    {
        [Param(ParamUnit.Count, "bones: neck segments + the head's (the last)", Min = 4, Max = 24, Example = "pterodactyl 11")] public int segments = 10;
        [Param(ParamUnit.Metres, "neck length, root to the head bone", Example = "pterodactyl 0.9")] public float length = 0.8f;
        [Param(ParamUnit.Metres, "half width at the root (it flares into the chest)")] public float rootRadius = 0.06f;
        [Param(ParamUnit.Metres, "half width at the head")] public float headRadius = 0.035f;
        [Param(ParamUnit.Fraction, "cross-section height / width", Min = 0.6f, Max = 1.6f)] public float depth = 1.1f;
        [Param(ParamUnit.Degrees, "on the ground: the base's elevation above the heading", Min = -30, Max = 90)] public float baseDeg = 55f;
        [Param(ParamUnit.Degrees, "on the ground: the head's elevation (its forward axis) above the heading", Min = -60, Max = 60)] public float headDeg = 5f;
        [Param(ParamUnit.Degrees, "in the air: the base's elevation (stretched forward)", Min = -30, Max = 60)] public float flightBaseDeg = 8f;
        [Param(ParamUnit.Degrees, "in the air: the head's elevation", Min = -60, Max = 60)] public float flightHeadDeg = -4f;
        [Param(ParamUnit.Degrees, "the bind (mesh) pose's base elevation: between the stance and the flight pose", Min = -30, Max = 90)] public float bindBaseDeg = 30f;
        [Param(ParamUnit.Degrees, "the bind pose's head elevation", Min = -60, Max = 60)] public float bindHeadDeg = 5f;
        [Param(ParamUnit.Fraction, "the head point's distance from the root, x the neck's chord for its bend (< 1 leaves the neck slack)", Min = 0.7f, Max = 1)] public float reach = 0.96f;
        [Param(ParamUnit.Seconds, "the head follower's time constant (larger: steadier head, more lag)", Min = 0.02f, Max = 2)] public float stabiliseSeconds = 0.18f;
        [Param(ParamUnit.Degrees, "idle glances: yaw / pitch amplitude (0 = none)", Min = 0, Max = 80)] public float glanceDeg = 25f;
        [Param(ParamUnit.Seconds, "idle glances: mean interval")] public float glanceSeconds = 2.5f;
        [Param(ParamUnit.Degrees, "a call (on the ground, standing): the base and head rise by this much", Min = 0, Max = 60)] public float callDeg = 25f;
        [Param(ParamUnit.Seconds, "a call's length (0 = it never calls)")] public float callSeconds = 1.2f;
        [Param(ParamUnit.Seconds, "mean interval between calls while standing")] public float callInterval = 12f;
        [Param(ParamUnit.Degrees, "the most the head turns toward an aim point off the heading", Min = 0, Max = 120)] public float aimYawDeg = 70f;
        [Param(ParamUnit.Fraction, "striking: the head's reach toward the aim point, x the neck length", Min = 0.5f, Max = 1)] public float strikeReach = 0.97f;
        [Param(ParamUnit.Metres, "the head never comes lower than this over the ground under the chest")] public float headClearance = 0.08f;
        [Param(ParamUnit.Degrees, "the base's highest elevation above the chest's line (a call or a glance never leans the neck back past this)", Min = 30, Max = 89)] public float maxBaseDeg = 80f;
        [Param(ParamUnit.Fraction, "in the air: how much of the chest's pitch the neck's base follows (1 = it continues the chest's line; less raises it over a nose-down body)", Min = 0, Max = 1)] public float basePitchFollow = 0.6f;
        [Param(ParamUnit.Fraction, "in the air: how much of the chest's pitch the head follows (0 = always level)", Min = 0, Max = 1)] public float headPitchFollow = 0.3f;
        [Param(ParamUnit.Degrees, "in the air: the head's elevation stays within this of level (aiming and striking excepted)", Min = 5, Max = 90)] public float headLevelDeg = 22f;
        [Param(ParamUnit.Degrees, "in the air, aiming and striking: the neck, the reach and the head's turn stay within this cone round the neck's forward (the head never swings below or behind the neck); on the ground it pecks anywhere within aimYawDeg", Min = 10, Max = 180)] public float strikeConeDeg = 40f;
        [Param(ParamUnit.Degrees, "follow-through: the most the head lags its posture (seen from the neck's root)", Min = 5, Max = 180)] public float maxLagDeg = 15f;
        [Param(ParamUnit.Metres, "the head part's reach past the head bone along the head (a beak's tip): a strike lands this point on the aim point", Example = "pterodactyl 0.6")] public float tipReach = 0f;
        [Param(ParamUnit.Count, "mesh: vertices round each ring", Min = 8, Max = 32)] public int ringSides = 16;
        [Param(ParamUnit.Count, "mesh: rings per neck segment", Min = 1, Max = 8)] public int ringsPerSegment = 4;

        public override int BoneCount(in SpeciesCtx c) => segments;
        public override int MeshOrder => 100;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a LongNeck roots on a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (segments < 4 || segments > 24) return $"{id}: segments 4..24";
            if (!(length > 0f) || !(rootRadius > 0f) || !(headRadius > 0f)) return $"{id}: length and radii > 0";
            if (!(stabiliseSeconds > 0f)) return $"{id}: stabiliseSeconds > 0";
            if (reach < 0.7f || reach > 1f || strikeReach < 0.5f || strikeReach > 1f) return $"{id}: reach 0.7..1, strikeReach 0.5..1";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new LongNeckRuntime(this, c.Sim, c.Species.Layout.Start(c.Index)) : null;

        // ------------------------------------------------------------ mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            int b0 = m.Species.Layout.Start(c.Index), K = segments;
            var P = new Vector3[K]; var X = new Vector3[K]; var Y = new Vector3[K]; var Z = new Vector3[K];
            for (int k = 0; k < K; k++)
            {
                Matrix4x4 b = m.Bones[b0 + k];
                P[k] = b.GetColumn(3);
                X[k] = PartMeshEmitter.Unit(b.GetColumn(0), Vector3.right);
                Y[k] = PartMeshEmitter.Unit(b.GetColumn(1), Vector3.up);
                Z[k] = PartMeshEmitter.Unit(b.GetColumn(2), Vector3.forward);
            }
            int sides = ringSides;
            float total = 0f;
            for (int k = 1; k < K; k++) total += (P[k] - P[k - 1]).magnitude;
            // the root: buried in the chest, rigid to the first bone
            int prev = -1; bool prevPole = false;
            void RingAt(Vector3 c0, Vector3 x, Vector3 y, Vector3 t, float w, float dw, float u, Vector4 skin)
            {
                float hgt = w * depth;
                int r = m.Ring(c0, x, y, t, w, hgt, 0f, dw, dw * depth, 0f, u, PartMeshEmitter.PartBody, skin, sides, true);
                if (prev >= 0) m.Stitch(prev, r, sides, prevPole, false, false);
                prev = r; prevPole = false;
            }
            // the root: a rounded cap buried in the chest, rigid to the first bone (a ball socket)
            Vector4 rigid0 = PartMeshEmitter.Rigid(b0);
            float rr = Radius(0f);
            prev = m.Pole(P[0] - Z[0] * rr, -Z[0], 0f, PartMeshEmitter.PartBody, 0f, rigid0, sides);
            prevPole = true;
            for (int k = 1; k <= 2; k++)
            {
                float phi = 30f * k * Mathf.Deg2Rad;
                RingAt(P[0] - Z[0] * (rr * Mathf.Cos(phi)), X[0], Y[0], Z[0], rr * Mathf.Sin(phi), Mathf.Cos(phi) / Mathf.Sin(phi), rr * (1f - Mathf.Cos(phi)), rigid0);
            }
            float arc = 0f;
            for (int k = 0; k < K - 1; k++)
            {
                Vector3 a0 = P[k], a1 = P[k + 1];
                Vector3 m0 = Z[k] * (a1 - a0).magnitude, m1 = Z[k + 1] * (a1 - a0).magnitude;
                float seg = (a1 - a0).magnitude;
                int rings = ringsPerSegment;
                for (int i = (k == 0 ? 0 : 1); i <= rings; i++)
                {
                    float f = (float)i / rings;
                    Vector3 c0 = Herm(a0, m0, a1, m1, f), t = PartMeshEmitter.Unit(HermD(a0, m0, a1, m1, f), Z[k]);
                    Vector3 y = Vector3.Slerp(Y[k], Y[k + 1], f); y = PartMeshEmitter.Unit(y - t * Vector3.Dot(y, t), Y[k]);
                    Vector3 x = Vector3.Cross(y, t);
                    float g = total > 0f ? (arc + f * seg) / total : 0f;
                    float w = Radius(g), dw = (Radius(Mathf.Min(1f, g + 0.01f)) - Radius(Mathf.Max(0f, g - 0.01f))) / (0.02f * Mathf.Max(1e-4f, total));
                    float wf = f * f * (3f - 2f * f);
                    RingAt(c0, x, y, t, w, dw, arc + f * seg, PartMeshEmitter.Blend(b0 + k, b0 + k + 1, wf));
                }
                arc += seg;
            }
            // closed inside the head
            int tip = m.Pole(P[K - 1] + Z[K - 1] * (0.8f * headRadius), Z[K - 1], arc, PartMeshEmitter.PartBody, 0f, PartMeshEmitter.Rigid(b0 + K - 1), sides);
            m.Stitch(prev, tip, sides, false, true, false);
        }

        // half width along the neck, g = 0 root .. 1 head: a flared base
        float Radius(float g)
        {
            float w = Mathf.Lerp(rootRadius, headRadius, Mathf.Pow(Mathf.Clamp01(g), 0.8f));
            float flare = Mathf.Clamp01(1f - g / 0.18f);
            return w * (1f + 0.25f * flare * flare);
        }

        internal static Vector3 Herm(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * m0 + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * m1;
        }

        internal static Vector3 HermD(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
        {
            float t2 = t * t;
            return (6f * t2 - 6f * t) * p0 + (3f * t2 - 4f * t + 1f) * m0 + (-6f * t2 + 6f * t) * p1 + (3f * t2 - 2f * t) * m1;
        }
    }

    // One neck of one individual (stage Late): posture -> head follower -> Hermite neck -> bones.
    public sealed class LongNeckRuntime : IPartRuntime, IHeadAim
    {
        const int kSamples = 24;
        const float kMaxTurn = 80f * Mathf.Deg2Rad, kMaxLead = 32f * Mathf.Deg2Rad;
        // the chest's pitch the neck follows (a stoop dives it, a pull-out raises it), the base's highest elevation
        // over the level (never past vertical), the stabiliser's most offset (x the neck's length)
        const float kPitchMin = -75f, kPitchMax = 45f, kMaxBaseWorld = 85f, kMaxBob = 0.25f, kPitchSeconds = 0.25f, kOffChest = 70f;

        readonly LongNeck p;
        readonly CreatureSim sim;
        readonly int bone, K, joint;
        readonly Vector3 offset;
        readonly Vector3[] bonePos, boneDir, boneUp;
        readonly Vector3[] samp = new Vector3[kSamples + 1];
        readonly float[] cum = new float[kSamples + 1];
        bool stepped;
        Vector3 head, headDir;
        // the follower works on the head relative to the root, its height stabilised (yS: the beat's bob filtered out)
        Vector3 rel, relVel;
        float yS, yV, yA, yPrev, baseEl, pitchS, pitchV, pitchNow, airNow, follow, dive;
        float glanceYaw, glancePitch, glanceYawT, glancePitchT, glanceIn;
        float callIn, callLeft;
        uint rng;
        // aim (IHeadAim)
        bool aiming; Vector3 aimPoint; float strikeT, strike;

        public float Calling { get; private set; }      // 0..1: the call's raise (a head part opens its mouth with it)
        public float Strike => strike;                  // 0..1: the strike's reach (eased)
        public Vector3 HeadPosition => head;
        public Vector3 StrikePoint => head + headDir * (p.tipReach * sim.SizeScale);
        public Vector3 HeadForward => headDir;
        public int HeadBone => bone + K - 1;
        public LongNeck Part => p;

        public LongNeckRuntime(LongNeck part, CreatureSim sim, int firstBone)
        {
            p = part; this.sim = sim; bone = firstBone; K = part.segments; joint = part.at.joint;
            offset = part.at.offset;
            bonePos = new Vector3[K]; boneDir = new Vector3[K]; boneUp = new Vector3[K];
            rng = (uint)(sim.Seed * 2654435761u + 0x9E3779B9u + (uint)firstBone * 97u);
            if (rng == 0) rng = 1;
            callIn = part.callInterval * sim.TimeScale * (0.3f + 0.7f * Rand());
            glanceIn = part.glanceSeconds * sim.TimeScale * Rand();
        }

        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

        public PartStage Stage => PartStage.Late;

        public void Aim(Vector3 point, float strikeAmount) { aiming = true; aimPoint = point; strikeT = Mathf.Clamp01(strikeAmount); }
        public void Release() { aiming = false; strikeT = 0f; }

        // The root, the level heading and the up the posture's elevations are measured from, the chest's pitch in that
        // frame and the air weight. The neck is carried by the chest: the heading is the chest's own (in the air, with
        // the flight's horizontal heading mixed in, so a chest pitched near vertical still has one), its base rises from
        // the chest's pitch. Never the flight path low-passed: in the aviary's tight circles a path lagged by seconds
        // pointed the neck backward over the shoulders, the head hanging behind and below the body.
        void Frame(out Vector3 root, out Vector3 h, out Vector3 up, out float pitch, out float air)
        {
            float s = sim.SizeScale;
            Quaternion r = sim.Rotations[joint];
            root = sim.Joints[joint] + r * (offset * s);
            CreatureFlight fl = sim.Flight;
            air = fl != null ? fl.AirWeight[joint] : 0f;
            up = fl != null && fl.Mode != FlightMode.Ground ? fl.Model.Up : sim.Up[joint];
            if (up.sqrMagnitude < 0.5f) up = Vector3.up;
            Vector3 f = r * Vector3.forward;
            h = f - up * Vector3.Dot(f, up);
            if (air > 0f) h += fl.Model.Forward * (0.25f * air);
            if (h.sqrMagnitude < 1e-8f) h = sim.Heading;
            h = PartMeshEmitter.Unit(h, Vector3.forward);
            pitch = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(f, up), Vector3.Dot(f, h)) * Mathf.Rad2Deg, kPitchMin, kPitchMax);
        }

        static Vector3 Dir(Vector3 h, Vector3 up, float elevDeg)
        {
            float e = elevDeg * Mathf.Deg2Rad;
            return h * Mathf.Cos(e) + up * Mathf.Sin(e);
        }

        // `dir` kept within `maxRad` of `axis` (both unit)
        static Vector3 Cone(Vector3 dir, Vector3 axis, float maxRad)
        {
            float a = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, axis), -1f, 1f));
            return a <= maxRad ? dir : Vector3.RotateTowards(axis, dir, maxRad, 0f).normalized;
        }

        // the posture's head point and head direction
        void Posture(Vector3 root, Vector3 h, Vector3 up, float baseDeg, float headDeg, float reachK, out Vector3 point, out Vector3 dir)
        {
            float d = (baseDeg - headDeg) * Mathf.Deg2Rad;
            float chordK = Mathf.Abs(d) > 1e-3f ? Mathf.Sin(0.5f * d) / (0.5f * d) : 1f;
            Vector3 c = Dir(h, up, 0.5f * (baseDeg + headDeg));
            point = root + c * (p.length * sim.SizeScale * chordK * reachK);
            dir = Dir(h, up, headDeg);
        }

        // the base's and the head's elevations (over the level heading) for this pitch of the chest and air weight:
        // the base rises from the chest's line (never more than maxBaseDeg above it, nor past vertical); the head is
        // level-referenced, following a share of the chest's pitch in the air and held within headLevelDeg of level there
        void Elevations(float pitch, float air, out float baseEl, out float headEl)
        {
            float callUp = p.callDeg * Calling;
            baseEl = pitch * Mathf.Lerp(1f, follow, air) + Mathf.Lerp(p.baseDeg, p.flightBaseDeg, air) + callUp + glancePitch;
            baseEl = Mathf.Min(baseEl, Mathf.Min(pitch + p.maxBaseDeg, kMaxBaseWorld));
            headEl = Mathf.Lerp(p.headDeg + 1.4f * callUp + glancePitch, p.flightHeadDeg + Mathf.Lerp(p.headPitchFollow, 1f, dive) * pitch, air);
            float lim = Mathf.Lerp(90f, Mathf.Lerp(p.headLevelDeg, 90f, dive), air);
            headEl = Mathf.Clamp(headEl, -lim, lim);
        }

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            float s = sim.SizeScale, T = sim.TimeScale;
            Frame(out Vector3 root, out Vector3 h, out Vector3 up, out float pitch, out float air);
            bool ground = air < 0.5f;
            // glances (idle, on the ground)
            glanceIn -= dt;
            if (glanceIn <= 0f)
            {
                glanceIn = p.glanceSeconds * T * (0.5f + Rand());
                glanceYawT = ground && !aiming ? p.glanceDeg * (2f * Rand() - 1f) : 0f;
                glancePitchT = ground && !aiming ? 0.4f * p.glanceDeg * (2f * Rand() - 1f) : 0f;
            }
            float kg = 1f - Mathf.Exp(-dt / (0.5f * T));
            glanceYaw += (glanceYawT - glanceYaw) * kg; glancePitch += (glancePitchT - glancePitch) * kg;
            // calls: standing on the ground, now and then
            bool still = ground && sim.Speed < 0.2f * sim.Species.speed * T && !aiming;
            if (p.callSeconds > 0f && still)
            {
                if (callLeft > 0f) callLeft -= dt;
                else { callIn -= dt; if (callIn <= 0f) { callLeft = p.callSeconds * T; callIn = p.callInterval * T * (0.6f + 0.8f * Rand()); } }
            }
            else callLeft = 0f;
            float callT = callLeft > 0f ? 1f : 0f;
            Calling += (callT - Calling) * (1f - Mathf.Exp(-dt / (0.15f * T)));
            strike += (strikeT - strike) * (1f - Mathf.Exp(-dt / (0.12f * T)));

            // the chest's pitch through a critically damped low-pass: the wingbeat rocks the chest, the head's posture
            // follows only its mean (the base follows the chest itself: WriteBones bends the neck to absorb the rock)
            if (!stepped) { pitchS = pitch; pitchV = 0f; }
            {
                float wp = 1f / (kPitchSeconds * T);
                int np = Mathf.Max(1, Mathf.CeilToInt(dt * wp / 0.2f));
                float hp = dt / np;
                for (int i = 0; i < np; i++) { pitchV += (wp * wp * (pitch - pitchS) - 2f * wp * pitchV) * hp; pitchS += pitchV * hp; }
            }
            pitchNow = pitch; airNow = air;
            // a folded-wing dive (a stoop) stretches the neck along the diving body, toward what it dives at
            dive = sim.Flight != null ? Mathf.Clamp01(sim.Flight.DiveFold / 0.3f) : 0f;
            follow = Mathf.Lerp(p.basePitchFollow, 1f, dive);
            Elevations(pitchS, air, out baseEl, out float headEl);
            Vector3 hy = Quaternion.AngleAxis(glanceYaw, up) * h;
            Posture(root, hy, up, baseEl, headEl, p.reach, out Vector3 want, out Vector3 wantDir);
            if (aiming)
            {
                // on the ground: the head turns toward the aim point (clamped off the heading) and a strike pecks at it.
                // In the air the neck itself points at it, the reach and the head's turn inside strikeConeDeg round the
                // neck's forward (the posture's chord): a mark below or behind (passed in a pull-out) never swings the
                // head under or behind the neck, a strike stretches the head along the neck
                Vector3 to = aimPoint - root;
                Vector3 toH = to - up * Vector3.Dot(to, up);
                float yaw = toH.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(h, toH, up) : 0f;
                float yawMax = p.aimYawDeg * (1f - air);
                yaw = Mathf.Clamp(yaw, -yawMax, yawMax);
                Vector3 ha = Quaternion.AngleAxis(yaw, up) * h;
                Posture(root, ha, up, baseEl, headEl, p.reach, out Vector3 look, out Vector3 lookDir);
                float cone = Mathf.Lerp(Mathf.PI, p.strikeConeDeg * Mathf.Deg2Rad, air);
                Vector3 fwd = PartMeshEmitter.Unit(look - root, ha);
                Vector3 axis = Cone(PartMeshEmitter.Unit(to, fwd), fwd, cone);
                look = Vector3.Lerp(look, root + axis * (look - root).magnitude, air);
                float L = p.length * s * p.strikeReach;
                // the head bone stops a tip short of the point (the beak's tip lands on it)
                Vector3 reachPt = root + axis * Mathf.Min(L, Mathf.Max(0f, to.magnitude - p.tipReach * s));
                want = Vector3.Lerp(look, reachPt, strike);
                Vector3 aimDir = Cone(PartMeshEmitter.Unit(to, lookDir), lookDir, cone);
                wantDir = Vector3.Slerp(lookDir, aimDir, Mathf.Max(strike, 0.5f)).normalized;
            }
            // in the air the head's point stays within kOffChest of the chest's line seen from the root (a stall's
            // lurch pitches the chest under a head the stabiliser holds up: the neck stood straight up off its back)
            Vector3 cf = sim.Rotations[joint] * Vector3.forward;
            float offChest = Mathf.Lerp(180f, kOffChest, air) * Mathf.Deg2Rad;
            if (air > 0f)
            {
                Vector3 rw = want - root;
                float m = rw.magnitude;
                if (m > 1e-6f) want = root + Cone(rw / m, cf, offChest) * m;
            }
            // never below the head clearance over the ground under the chest
            float floorH = GroundHeight(root, up) + p.headClearance * s;
            float wh = Vector3.Dot(want, up);
            if (wh < floorH) want += up * (floorH - wh);
            float y = Vector3.Dot(root, up);
            if (!stepped) { rel = want - root; relVel = Vector3.zero; yS = yPrev = y; yV = yA = 0f; headDir = wantDir; stepped = true; }
            float w0 = 1f / Mathf.Max(1e-3f, p.stabiliseSeconds * T * (1f - 0.8f * strike));
            float hk = Mathf.Min(dt, 0.2f / w0);
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / hk));
            float sh = dt / n;
            float vy = (y - yPrev) / dt; yPrev = y;
            Vector3 relWant = want - root;
            for (int i = 0; i < n; i++)
            {
                // the stabiliser: the root's height through a critically damped low-pass of its vertical velocity
                // (the wingbeat's bob filtered out, a climb followed without lag) - only the height: a low-passed
                // horizontal velocity lagged the root by metres round a turn
                yA += (w0 * w0 * (vy - yV) - 2f * w0 * yA) * sh;
                yV += yA * sh;
                yS += yV * sh;
                // the head's offset from the root, critically damped toward the posture's (the follow-through)
                Vector3 acc = w0 * w0 * (relWant - rel) - 2f * w0 * relVel;
                relVel += acc * sh;
                rel += relVel * sh;
            }
            yS += (y - yS) * Mathf.Min(1f, 2f / T * dt);
            float bob = kMaxBob * p.length * s;
            yS = Mathf.Clamp(yS, y - bob, y + bob);
            // the follow-through never lags the posture by more than maxLagDeg seen from the root (the chain: the
            // head trails the neck, never swings round behind or under it)
            float lagMax = p.maxLagDeg * Mathf.Deg2Rad * (1f - 0.7f * strike);   // a strike is a snap: little follow-through
            if (relWant.sqrMagnitude > 1e-10f && rel.sqrMagnitude > 1e-10f)
            {
                Vector3 rw = relWant.normalized, rn = rel.normalized;
                if (Mathf.Acos(Mathf.Clamp(Vector3.Dot(rw, rn), -1f, 1f)) > lagMax) rel = Vector3.RotateTowards(rw, rn, lagMax, 0f).normalized * rel.magnitude;
            }
            head = root + up * ((yS - y) * (1f - strike)) + rel;   // a strike is aimed: no bob filter on it
            headDir = Vector3.Slerp(headDir, wantDir, 1f - Mathf.Exp(-dt * w0)).normalized;
            headDir = Cone(headDir, wantDir, lagMax);
            // the head stays within the neck's reach of the root (and, in the air, the follow-through and the bob filter
            // keep it within kOffChest of the chest's line too)
            Vector3 rh = head - root;
            if (air > 0f && rh.sqrMagnitude > 1e-10f) { float m = rh.magnitude; rh = Cone(rh / m, cf, offChest) * m; head = root + rh; }
            float dist = rh.magnitude, Lm = p.length * s;
            if (dist > 0.995f * Lm) head = root + rh * (0.995f * Lm / dist);
            else if (dist < 0.4f * Lm && dist > 1e-6f) head = root + rh * (0.4f * Lm / dist);
            float hh = Vector3.Dot(head, up);
            if (hh < floorH) head += up * (floorH - hh);
        }

        float GroundHeight(Vector3 root, Vector3 up)
        {
            CreatureFlight fl = sim.Flight;
            if (fl != null && fl.Airborne) return Vector3.Dot(fl.GroundBelow, up);
            // the ground under the chest: its clearance below the root joint
            return Vector3.Dot(sim.Joints[joint], up) - sim.Species.joints[joint].clearance * sim.SizeScale;
        }

        public void WriteBones(Matrix4x4[] dst, int offsetIndex)
        {
            float s = sim.SizeScale;
            Frame(out Vector3 root, out Vector3 h, out Vector3 up, out _, out _);
            Vector3 H, hd;
            if (stepped) { H = head; hd = headDir; }
            else Posture(root, h, up, p.bindBaseDeg, p.bindHeadDeg, p.reach, out H, out hd);   // the rest / bind pose
            // the base leaves along the posture's base direction toward the head (its yaw follows the head's halfway)
            Vector3 rh = H - root;
            Vector3 rhH = rh - up * Vector3.Dot(rh, up);
            Vector3 hb = rhH.sqrMagnitude > 1e-8f ? Vector3.Slerp(h, rhH.normalized, 0.5f).normalized : h;
            // the base rides the chest's own pitch (its rock about the low-passed pitch the posture follows)
            float baseDeg = stepped ? Mathf.Min(baseEl + (pitchNow - pitchS) * Mathf.Lerp(1f, follow, airNow), Mathf.Min(pitchNow + p.maxBaseDeg, kMaxBaseWorld)) : p.bindBaseDeg;
            Vector3 t0 = Dir(hb, up, baseDeg);
            // tangent scale of a circular arc from the base direction to the head direction (Hermite tangent = 3 x the
            // Bezier control length (4/3) r tan(d/4), chord 2 r sin(d/2)): never a loop or a bump; the arc is the neck's
            // length when the head sits on its posture's chord (a pulled-in head shortens it a little)
            // the base leaves within kMaxLead of the line to the head (a head pulled round behind or below the base hooked
            // the neck's first segment), and the head arrives within it too
            Vector3 cdir = PartMeshEmitter.Unit(H - root, t0);
            float lead0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot(t0, cdir), -1f, 1f));
            if (lead0 > kMaxLead) t0 = Vector3.Slerp(cdir, t0, kMaxLead / lead0).normalized;
            float lead1 = Mathf.Acos(Mathf.Clamp(Vector3.Dot(hd, cdir), -1f, 1f));
            if (lead1 > kMaxLead) hd = Vector3.Slerp(cdir, hd, kMaxLead / lead1).normalized;
            float d = Mathf.Acos(Mathf.Clamp(Vector3.Dot(t0, hd), -1f, 1f));
            // never more than kMaxTurn along the neck (a flare pitching the chest under a level head bent it past a fold)
            if (d > kMaxTurn) { t0 = Vector3.Slerp(hd, t0, kMaxTurn / d).normalized; d = kMaxTurn; }
            // the head kept where the arc stays within 12 % of the neck's length (a lagging head pulled in close
            // compressed the neck to half and folded its rings)
            float circle = p.length * s * (d > 1e-3f ? Mathf.Sin(0.5f * d) / (0.5f * d) : 1f);
            float chord0 = rh.magnitude;
            float chord = Mathf.Clamp(chord0, 0.88f * circle, 1.02f * circle);
            if (chord0 > 1e-6f && chord != chord0) { H = root + rh * (chord / chord0); rh = H - root; }
            chord = Mathf.Max(1e-4f, chord);
            float a1 = chord * (d > 1e-3f ? Mathf.Min(1.5f, 2f * Mathf.Tan(0.25f * d) / Mathf.Sin(0.5f * d)) : 1f);
            Arc(root, t0, H, hd, a1);   // fills the samples for a1
            Vector3 m0 = t0 * a1, m1 = hd * a1;
            Vector3 side = PartMeshEmitter.Unit(Vector3.Cross(up, hb), Vector3.right);
            Vector3 prevDir = t0, prevUp = up;
            float total = cum[kSamples];
            for (int k = 0; k < K; k++)
            {
                Vector3 pos, dir;
                if (k == K - 1) { pos = H; dir = hd; }
                else
                {
                    float target = total * k / (K - 1);
                    int j = 1;
                    while (j < kSamples && cum[j] < target) j++;
                    float f = (target - cum[j - 1]) / Mathf.Max(1e-9f, cum[j] - cum[j - 1]);
                    float u = (j - 1 + f) / kSamples;
                    pos = LongNeck.Herm(root, m0, H, m1, u);
                    dir = PartMeshEmitter.Unit(LongNeck.HermD(root, m0, H, m1, u), t0);
                }
                // up: at the base dir x side (side = the heading's right: continuous through a vertical base - a launch
                // pitches the raised base past vertical, where up projected off the world's flipped), then carried
                // bone to bone by the turn between their directions (parallel transport: a head swung out sideways put
                // the neck along `side`, where dir x side flipped mid-neck)
                Vector3 bu;
                if (k == 0) { bu = Vector3.Cross(dir, side); bu = bu.sqrMagnitude > 1e-8f ? bu.normalized : up; }
                else
                {
                    bu = Quaternion.FromToRotation(prevDir, dir) * prevUp;
                    bu -= dir * Vector3.Dot(bu, dir);
                    bu = bu.sqrMagnitude > 1e-8f ? bu.normalized : prevUp;
                }
                prevDir = dir; prevUp = bu;
                bonePos[k] = pos; boneDir[k] = dir; boneUp[k] = bu;
                dst[offsetIndex + bone + k] = Matrix4x4.TRS(pos, Quaternion.LookRotation(dir, bu), new Vector3(s, s, s));
            }
        }

        // arc length of the Hermite with tangent scale a (fills samp / cum)
        float Arc(Vector3 p0, Vector3 t0, Vector3 p1, Vector3 t1, float a)
        {
            Vector3 m0 = t0 * a, m1 = t1 * a;
            samp[0] = p0; cum[0] = 0f;
            for (int i = 1; i <= kSamples; i++)
            {
                samp[i] = LongNeck.Herm(p0, m0, p1, m1, (float)i / kSamples);
                cum[i] = cum[i - 1] + (samp[i] - samp[i - 1]).magnitude;
            }
            return cum[kSamples];
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            float pad = 0.8f * p.length * sim.SizeScale;   // the head part beyond the head bone
            Vector3 a = head - new Vector3(pad, pad, pad), b = head + new Vector3(pad, pad, pad);
            lo = Vector3.Min(lo, a); hi = Vector3.Max(hi, b);
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            for (int k = 1; k < K; k++) d.Line(bonePos[k - 1], bonePos[k], new Color(0.9f, 0.5f, 0.6f));
            d.Point(head, 0.02f * sim.SizeScale, new Color(1f, 0.3f, 0.3f));
            if (aiming) d.Line(head, aimPoint, new Color(1f, 0.1f, 0.1f));
        }
    }
}
