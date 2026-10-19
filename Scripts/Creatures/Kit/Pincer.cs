// Creature kit: Pincer - a hinged, jointed, curved appendage on its own 1-4 bones that opens and closes: a
// centipede's forcipules (venom claws) and ultimate legs, mandibles, chelicera fangs, an earwig's forceps, a
// scorpion-like claw finger. Rooted on a spine joint (offset in the joint frame, x mirrored by side); `mirror` makes
// the pair (.L / .R, each with its own bones). The chain lies in the plane of its hinge: every segment turns about
// the same hinge axis, curling toward the closed side over its length (curveDeg), the last `tipLength` tapering to a
// point in the band colour (the dark tip); joint bulbs (knuckle) and spines along the first segment's inner edge.
// Motion: closed at rest; while walking it holds `walkOpen`; once the body has stood still for idleDelay it flexes
// - opens slowly, holds, snaps shut, rests - every flexPeriod (the menacing idle flex of a centipede's forcipules),
// the first segment turning by (1 - distalShare) of the opening and the joints beyond by the rest. Eased, so it
// never pops; deterministic (no random numbers). Froude: lengths x s, times x sqrt s.
// Bones: Rigid block (after every shipped bone); runtime at stage Late (once per tick; it only poses bones).
// Allocation-free after construction; main thread. Material layers 11 (pincer, Claw by default) and 9 (band: tip,
// spines).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Pincer", PartKind.Rigid, Bones = "= segments (1-4)", Summary = "A hinged, jointed, curved appendage on 1-4 bones that opens and closes: curls toward its closed side about one hinge axis, tapers to a sharp tip in the band colour, joint bulbs and inner spines. Closed at rest, walkOpen while walking; at a standstill it flexes slowly (open, hold, snap shut, rest) every flexPeriod. mirror makes the pair.",
          System = "PincerRuntime (part runtime, stage Late) + Pincer.EmitMesh", MaterialLayers = new[] { 11, 9 }, KitOnly = true,
          UseWhen = "forcipules, mandibles, chelicera fangs, pincers, earwig forceps, centipede ultimate legs - any jointed appendage that opens and closes")]
    public sealed class Pincer : CreaturePart
    {
        [Param(ParamUnit.Direction, "rest direction of the first segment in the joint frame (x mirrored by side)")] public Vector3 direction = new Vector3(0f, 0f, 1f);
        [Param(ParamUnit.Direction, "hinge axis in the joint frame (x mirrored): opening turns the chain about it, away from the midline for (0, 1, 0) on a forward-pointing part", Default = "(0, 1, 0)")] public Vector3 hingeAxis = new Vector3(0f, 1f, 0f);
        [Param(ParamUnit.Metres, "length along the chain, root to tip", Example = "scolopendra forcipule 0.011")] public float length;
        [Param(ParamUnit.Count, "segments (one bone each)", Min = 1, Max = 4, Default = "2")] public int segments = 2;
        [Param(ParamUnit.Fraction, "the first segment's share of the length (0 = equal segments)", Min = 0, Max = 0.9f)] public float baseFraction;
        [Param(ParamUnit.Metres, "radius at the root")] public float radiusStart;
        [Param(ParamUnit.Metres, "radius where the tip starts")] public float radiusEnd;
        [Param(ParamUnit.Metres, "the pointed tip at the end (tapers to a point; band colour); <= the last segment")] public float tipLength;
        [Param(ParamUnit.Degrees, "curl toward the closed side over the whole length", Min = -180, Max = 270)] public float curveDeg;
        [Param(ParamUnit.Fraction, "bulb at each joint between segments, x the radius (0 = none)", Min = 0, Max = 1)] public float knuckle;
        [Param(ParamUnit.Count, "spines along the first segment's inner edge (one: a tooth at its end)", Min = 0, Max = 8)] public int spines;
        [Param(ParamUnit.Metres, "spine length")] public float spineLength;
        [Param(ParamUnit.Degrees, "opening at the peak of a flex (about the hinge)", Min = 0, Max = 90, Example = "forcipule 32")] public float openDeg = 30f;
        [Param(ParamUnit.Fraction, "share of the opening taken by the joints past the first (the tip unfolding)", Min = 0, Max = 1)] public float distalShare = 0.3f;
        [Param(ParamUnit.Seconds, "one idle flex: open slowly, hold, snap shut, rest", Default = "3")] public float flexPeriod = 3f;
        [Param(ParamUnit.Seconds, "standstill before the first flex", Default = "0.6")] public float idleDelay = 0.6f;
        [Param(ParamUnit.Fraction, "opening held while walking, x openDeg", Min = 0, Max = 1)] public float walkOpen;
        [Param(ParamUnit.Fraction, "place in the flex cycle at its start (a second pair can lag the first)", Min = 0, Max = 1)] public float phase;
        [Param(ParamUnit.Count, "material layer (part id) of the segments: 11 = pincer; 1 limb, 2 claw, 10 plate", Min = 0, Max = 15, Default = "11")] public int layer = PartMeshEmitter.PartPincer;
        [Param(ParamUnit.Count, "mesh: sides of the tube (0 = 10)", Min = 0, Max = 24)] public int sides;

        public const int MaxSegments = 4;
        public int Segments => Mathf.Clamp(segments, 1, MaxSegments);
        public int Sides => sides > 0 ? Mathf.Clamp(sides, 3, 24) : 10;
        public int Side => at.side < 0 ? -1 : 1;

        public override int BoneCount(in SpeciesCtx c) => Segments;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Pincer needs a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!mirror && at.side != -1 && at.side != 1) return $"{id}: side must be -1 or +1 (or mirror)";
            if (segments < 1 || segments > MaxSegments) return $"{id}: segments must be 1..{MaxSegments}";
            if (!(length > 0f) || !(radiusStart > 0f) || !(radiusEnd > 0f)) return $"{id}: length, radiusStart, radiusEnd must be > 0";
            if (direction.sqrMagnitude < 1e-8f || hingeAxis.sqrMagnitude < 1e-8f) return $"{id}: direction and hingeAxis must not be zero";
            if (Vector3.Cross(direction.normalized, hingeAxis.normalized).sqrMagnitude < 0.25f) return $"{id}: direction must be at least 30 deg off the hinge axis";
            if (tipLength < 0f || tipLength > SegmentLength(Segments - 1)) return $"{id}: tipLength must be in 0..the last segment's length";
            if (baseFraction < 0f || baseFraction > 0.9f) return $"{id}: baseFraction must be in 0..0.9";
            if (openDeg < 0f || openDeg > 90f) return $"{id}: openDeg must be in 0..90";
            if (distalShare < 0f || distalShare > 1f || walkOpen < 0f || walkOpen > 1f || phase < 0f || phase > 1f) return $"{id}: distalShare, walkOpen, phase must be in 0..1";
            if (!(flexPeriod > 0f) || idleDelay < 0f) return $"{id}: flexPeriod > 0, idleDelay >= 0";
            if (spines < 0 || spines > 8 || spineLength < 0f) return $"{id}: spines 0..8, spineLength >= 0";
            if (knuckle < 0f || knuckle > 1f) return $"{id}: knuckle must be in 0..1";
            if (layer < 0 || layer >= PartLayers.MaxParts || (layer >= 4 && layer <= 7)) return $"{id}: layer must be a spine-shader part id (0..3, 8..15)";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new PincerRuntime(this, c.Sim, c.Species.Layout.Start(c.Index), c.Size) : null;

        // ------------------------------------------------------------ the chain (pure: mesh, runtime, tests)

        public float SegmentLength(int k)
        {
            int n = Segments;
            if (n == 1) return length;
            float first = baseFraction > 0f ? baseFraction * length : length / n;
            return k == 0 ? first : (length - first) / (n - 1);
        }

        // Arc length at the start of segment k (k = Segments: the tip).
        public float SegmentStart(int k)
        {
            float s = 0f;
            for (int i = 0; i < k && i < Segments; i++) s += SegmentLength(i);
            return s;
        }

        // The mirrored direction, hinge axis and offset of this side, joint frame (scale 1).
        void Local(out Vector3 off, out Vector3 dir, out Vector3 hinge)
        {
            int side = Side;
            off = at.offset; off.x *= side;
            dir = direction; dir.x *= side; dir = dir.normalized;
            hinge = hingeAxis; hinge.x *= side; hinge = hinge.normalized;
        }

        // The hinge angle (degrees; + opens) at arc length s for opening `open` (degrees): the first segment turns by
        // (1 - distalShare) x open, each joint beyond by its share of the rest; the curl closes it along the length.
        public float AngleAt(float s, float open)
        {
            int n = Segments;
            float a = open * (n > 1 ? 1f - distalShare : 1f);
            for (int k = 1; k < n; k++) if (s >= SegmentStart(k)) a += open * distalShare / (n - 1);
            return a - curveDeg * Mathf.Clamp(s, 0f, length) / length;
        }

        // Joint-frame rotation for hinge angle psi (degrees) on this side (mirrored: the left side turns the other way).
        Quaternion Turn(float psi, Vector3 hinge) => Quaternion.AngleAxis(Side * psi, hinge);

        // The point at arc length t past the start of segment k (t may run a little past either end: the caps), joint
        // frame at scale 1, for opening `open`; tangent out. Each segment is an arc about the hinge axis (closed form).
        public Vector3 PointAt(int k, float t, float open, out Vector3 tangent)
        {
            Local(out Vector3 off, out Vector3 dir, out Vector3 hinge);
            Vector3 dPar = hinge * Vector3.Dot(dir, hinge), dPerp = dir - dPar;
            Vector3 p = off;
            for (int i = 0; i < k; i++)
                p += Arc(SegmentStart(i), SegmentLength(i), i, open, dPar, dPerp, hinge);
            p += Arc(SegmentStart(k), t, k, open, dPar, dPerp, hinge);
            float psi = AngleAt(SegmentStart(k), open) - curveDeg * t / length;
            tangent = Turn(psi, hinge) * dir;
            return p;
        }

        // The chord of an arc of length L from s0 on segment k (its angle at s0 + 0, curling by curve x L / length).
        Vector3 Arc(float s0, float L, int k, float open, Vector3 dPar, Vector3 dPerp, Vector3 hinge)
        {
            float a0 = AngleAt(SegmentStart(k), open) - curveDeg * (s0 - SegmentStart(k)) / length;
            float da = -curveDeg * L / length;
            float half = 0.5f * da * Mathf.Deg2Rad;
            float sinc = Mathf.Abs(half) > 1e-5f ? Mathf.Sin(half) / half : 1f;
            return dPar * L + Turn(a0 + 0.5f * da, hinge) * dPerp * (L * sinc);
        }

        // Bone frames of the chain for opening `open` (degrees), the root joint at (jointPos, jointRot), sizeScale
        // `scale`: bone k sits at the start of segment k, its forward along the segment's start tangent, its up the
        // hinge axis made perpendicular.
        public void Pose(Vector3 jointPos, Quaternion jointRot, float scale, float open, Vector3[] pos, Quaternion[] rot)
        {
            Local(out _, out _, out Vector3 hinge);
            for (int k = 0; k < Segments; k++)
            {
                Vector3 p = PointAt(k, 0f, open, out Vector3 t);
                pos[k] = jointPos + jointRot * (p * scale);
                rot[k] = jointRot * Quaternion.LookRotation(t, hinge);
            }
        }

        // The radius at arc length s: root -> radiusEnd linearly, the tip tapering to a point; joint bulbs.
        public float RadiusAt(float s)
        {
            float tipStart = length - tipLength;
            float r;
            if (tipLength > 0f && s > tipStart)
            {
                float x = Mathf.Clamp01((s - tipStart) / tipLength);
                r = radiusEnd * Mathf.Pow(1f - x, 0.75f);
            }
            else r = Mathf.Lerp(radiusStart, radiusEnd, tipStart > 0f ? Mathf.Clamp01(s / tipStart) : 1f);
            if (knuckle > 0f)
                for (int k = 1; k < Segments; k++)
                {
                    float d = (s - SegmentStart(k)) / Mathf.Max(1e-5f, 1.2f * r);
                    r *= 1f + knuckle * Mathf.Exp(-d * d);
                }
            return r;
        }

        // ------------------------------------------------------------ mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSim rest = m.Rest;
            if (rest == null) return;
            int start = m.Species.Layout.Start(c.Index), n = Segments, sd = Sides;
            Vector3 jp = rest.Joints[at.joint];
            Quaternion jr = rest.Rotations[at.joint];
            Local(out _, out _, out Vector3 hingeL);
            Vector3 hinge = jr * hingeL;
            var q = new List<PartMeshEmitter.TubeSample>(32);
            for (int k = 0; k < n; k++)
            {
                int bone = start + k;
                Vector4 skin = PartMeshEmitter.Rigid(bone);
                float L = SegmentLength(k), s0 = SegmentStart(k);
                bool last = k == n - 1;
                float rA = RadiusAt(s0), rB = RadiusAt(s0 + L);
                float capA = 0.9f * rA, capB = last ? 0f : 0.9f * rB;
                float tipFrom = last && tipLength > 0f ? L - tipLength : L;   // the band starts here (segment-local)
                const int steps = 10;
                q.Clear();
                // rounded start cap (a hemisphere) then the segment, then its end cap (none at the tip: a point)
                for (int i = 0; i <= 3; i++)
                {
                    float x = 1f - i / 3f;   // 1 at the pole .. 0 at the cap's base
                    Sample(q, k, -capA * x, rA * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x)), skin, i == 0, jp, jr);
                }
                bool split = false;
                for (int i = 1; i <= steps; i++)
                {
                    float t = L * i / steps;
                    if (!split && t > tipFrom + 1e-6f)
                    {
                        // close the segment's own colour at the band start and begin the band tube there
                        Sample(q, k, tipFrom, RadiusAt(s0 + tipFrom), skin, false, jp, jr);
                        m.Tube(q, hinge, sd, layer);
                        q.Clear();
                        Sample(q, k, tipFrom, RadiusAt(s0 + tipFrom), skin, false, jp, jr);
                        split = true;
                    }
                    bool pole = last && i == steps;
                    Sample(q, k, t, pole ? 0f : RadiusAt(s0 + t), skin, pole, jp, jr);
                }
                if (!last)
                    for (int i = 1; i <= 3; i++)
                    {
                        float x = i / 3f;
                        Sample(q, k, L + capB * x, rB * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x)), skin, i == 3, jp, jr);
                    }
                m.Tube(q, hinge, sd, split ? PartMeshEmitter.PartBand : layer);
            }
            // spines along the first segment's inner edge (the closed side), pointing inward, down and toward the tip
            for (int i = 0; i < SpineCount; i++)
            {
                Spine(i, 0f, out Vector3 bp, out Vector3 sdir, out Vector3 tan, out float r0);
                Quaternion hq = jr * Quaternion.LookRotation(sdir, -tan);
                m.Horn(jp + jr * bp, hq, r0, 0f, spineLength, 15f, start, PartMeshEmitter.PartBand, 6, 0f, out _, out _);
            }
        }

        public int SpineCount => spineLength > 0f ? Mathf.Clamp(spines, 0, 8) : 0;

        // Spine i (on the first segment) for opening `open`: its base on the segment's inner surface, its direction
        // (inward, alternately a little down, toward the tip), the segment tangent there and its base radius; joint
        // frame, scale 1. The mesh draws a horn of spineLength from it (curling 15 deg toward the tip).
        public void Spine(int i, float open, out Vector3 basePt, out Vector3 dir, out Vector3 tangent, out float baseRadius)
        {
            Local(out _, out _, out Vector3 hingeL);
            int n = SpineCount;
            float L0 = SegmentLength(0);
            float f = n <= 1 ? 0.85f : Mathf.Lerp(0.3f, 0.9f, i / (n - 1f));
            float t = L0 * f;
            Vector3 p = PointAt(0, t, open, out Vector3 tan);
            Vector3 pA = PointAt(0, t, open - 2f, out _), pB = PointAt(0, t, open + 2f, out _);
            Vector3 inward = PartMeshEmitter.Unit(pA - pB, Vector3.Cross(hingeL, tan));   // toward closing
            inward = PartMeshEmitter.Unit(inward - tan * Vector3.Dot(inward, tan), inward);
            Vector3 down = PartMeshEmitter.Unit(Vector3.ProjectOnPlane(Vector3.down, tan), Vector3.down);
            float lean = (i & 1) == 0 ? 0.35f : -0.15f;   // alternate rows: inner-lower and inner
            dir = PartMeshEmitter.Unit(inward + down * lean + tan * 0.55f, inward);
            float r = RadiusAt(t);
            basePt = p + PartMeshEmitter.Unit(inward + down * lean, inward) * (0.8f * r);
            tangent = tan;
            baseRadius = Mathf.Max(0.3f * r, 0.22f * spineLength);
        }

        // One tube sample at segment-local arc t of segment k, rest pose (open 0), rigid to its bone.
        void Sample(List<PartMeshEmitter.TubeSample> q, int k, float t, float r, Vector4 skin, bool pole, Vector3 jp, Quaternion jr)
        {
            Vector3 p = PointAt(k, t, 0f, out Vector3 tan);
            const float e = 1e-4f;
            float dr = (RadiusAt(SegmentStart(k) + t + e) - RadiusAt(SegmentStart(k) + t - e)) / (2f * e);
            q.Add(PartMeshEmitter.Sample(jp + jr * p, jr * tan, r, pole ? 0f : dr, SegmentStart(k) + t, skin, pole));
        }
    }

    // One Pincer of one individual: the eased opening and the idle-flex clock. Stage Late: once per tick;
    // WriteBones reads the sim's joints at draw time.
    public sealed class PincerRuntime : IPartRuntime
    {
        const float kEaseSeconds = 0.09f;    // opening easing time constant at scale 1 (x sqrt s): a snap, not a pop
        const float kMoving = 0.15f;         // walking: speed above this fraction of the cruise speed

        readonly Pincer p;
        readonly CreatureSim sim;
        readonly int start, n;
        readonly float scale, T;
        readonly Vector3[] pos;
        readonly Quaternion[] rot;
        float open;                          // eased opening, degrees (0 at construction: the rest pose)
        float still;                         // seconds the body has stood still

        public Pincer Part => p;
        public float Opening => open;
        public int FirstBone => start;

        public PincerRuntime(Pincer pincer, CreatureSim sim, int firstBone, SizeCtx size)
        {
            p = pincer; this.sim = sim; start = firstBone; n = pincer.Segments;
            scale = size.L; T = size.T;
            pos = new Vector3[n]; rot = new Quaternion[n];
        }

        public PartStage Stage => PartStage.Late;

        // The flex cycle at phase x (0..1): open slowly, hold, snap shut, rest.
        public static float Profile(float x)
        {
            x -= Mathf.Floor(x);
            if (x < 0.35f) return Smooth(x / 0.35f);
            if (x < 0.55f) return 1f;
            if (x < 0.65f) return 1f - Smooth((x - 0.55f) / 0.1f);
            return 0f;
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        public bool Walking => sim.Speed > kMoving * sim.Species.speed * sim.TimeScale;

        // The opening the runtime eases toward now (degrees).
        public float TargetOpening
        {
            get
            {
                float walk = p.walkOpen;
                if (Walking) return walk * p.openDeg;
                float t = still - p.idleDelay * T;
                if (t < 0f) return walk * p.openDeg;
                float x = t / (p.flexPeriod * T) + p.phase;
                return Mathf.Max(walk, Profile(x)) * p.openDeg;
            }
        }

        public void Step(in StepCtx c, float dt)
        {
            if (Walking) still = 0f; else still += dt;
            float k = 1f - Mathf.Exp(-dt / (kEaseSeconds * T));
            open += (TargetOpening - open) * k;
        }

        void PoseNow() => p.Pose(sim.Joints[p.at.joint], sim.Rotations[p.at.joint], scale, open, pos, rot);

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            PoseNow();
            Vector3 s = Vector3.one * scale;
            for (int k = 0; k < n; k++) dst[offset + start + k] = Matrix4x4.TRS(pos[k], rot[k], s);
        }

        // The tip in world space now (tests, bounds).
        public Vector3 Tip()
        {
            Vector3 l = p.PointAt(n - 1, p.SegmentLength(n - 1), open, out _);
            return sim.Joints[p.at.joint] + sim.Rotations[p.at.joint] * (l * scale);
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            PoseNow();
            float r = (Mathf.Max(p.radiusStart, p.radiusEnd) * (1f + p.knuckle) + p.spineLength) * scale;
            for (int k = 0; k < n; k++) Grow(ref lo, ref hi, pos[k], r);
            Grow(ref lo, ref hi, Tip(), r);
        }

        static void Grow(ref Vector3 lo, ref Vector3 hi, Vector3 q, float r)
        {
            lo = Vector3.Min(lo, q - new Vector3(r, r, r));
            hi = Vector3.Max(hi, q + new Vector3(r, r, r));
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            PoseNow();
            var c = new Color(0.9f, 0.2f, 0.2f, 1f);
            for (int k = 0; k + 1 < n; k++) d.Line(pos[k], pos[k + 1], c);
            d.Line(pos[n - 1], Tip(), c);
        }
    }
}
