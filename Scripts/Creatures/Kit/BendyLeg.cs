// Creature kit (2026-10-20, the GolfballStrider): BendyLeg - a long, thin, springy limb. Its arthropod segments are
// GENERATED from a few numbers instead of written out one by one:
//   length, segmentCount     the whole leg, cut into 3..7 segments (the last one the tarsus, tarsusShare of it)
//   riseDeg, archDeg         the first segment rises at riseDeg; the solved segments then turn down by archDeg in all
//   kneeAt, kneeSpread       where along the leg the turn is concentrated, and how widely (1 = an even curve: a bent
//                            rod; small = one sharp knee)
//   kinkDeg, lengthJitter,   irregular kinks at the joints (zero-sum, so the arch stays archDeg) and uneven segment
//   seed                     lengths, deterministic per seed: no two legs alike
// CreatureLegs walks it unchanged as an arthropod leg (synergy IK: the reach synergy curls the arch, the swing's lift
// synergy curls the root half and opens the distal half). On top, BendyLegSystem (part runtime) lets the drawn leg
// FLEX: every joint lags the IK pose on a damped spring driven by its own acceleration (inertia), sags under gravity,
// and the lifted tip whips; a planted tip is drawn exactly on the planted foot. Mesh: one continuous tapered tube
// rounded through each joint and blend-skinned across it (smooth), optional beads at the joints.
// Material layers: 1 the leg (restyle with Layers, e.g. dark chitin). Kit-only part, off unless a species names it.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("BendyLeg", PartKind.Limb, Bones = "= segmentCount", Summary = "Long thin springy limb generated from a few numbers (length, segment count, rise, arch, knee spread, seeded kinks and length jitter): walks as an arthropod leg (synergy IK), drawn as one continuous tapered tube blended through its joints, and flexes on its own - every joint lags the IK pose on a damped spring (inertia), sags, the lifted tip whips; a planted tip stays exactly on its foot.",
          System = "CreatureLegs (arthropod synergy IK, gait groups) + BendyLegSystem (part runtime, stage AfterLimbs: re-poses the leg's bones) + BendyLeg.EmitMesh", MaterialLayers = new[] { 1 }, KitOnly = true,
          UseWhen = "harvestmen, stilt-legged and wobbly animals, any limb that should look flexible: non-uniform, kinked, bouncing legs")]
    public sealed partial class BendyLeg : LimbPart
    {
        // ---- the shape ----
        [Param(ParamUnit.Count, "segments, coxa to tarsus (one bone each; 3..7 like an ArthropodLeg)", Min = 3, Max = 7, Default = "7")] public int segmentCount = 7;
        [Param(ParamUnit.Metres, "whole leg length, root to tip", Min = 0.01f, Max = 5f, Example = "golfball strider 1.1 .. 1.6")] public float length = 1f;
        [Param(ParamUnit.Fraction, "the tarsus (last segment, laid toward the ground) as a share of the length", Min = 0.03f, Max = 0.5f, Default = "0.14")] public float tarsusShare = 0.14f;
        [Param(ParamUnit.Fraction, "uneven segment lengths: each solved segment x (1 +- this), seeded", Min = 0f, Max = 0.6f)] public float lengthJitter;
        [Param(ParamUnit.Degrees, "elevation of the first segment above the horizontal", Min = -30f, Max = 89f, Default = "60")] public float riseDeg = 60f;
        [Param(ParamUnit.Degrees, "total downward turn of the solved segments (the knee arch)", Min = 0f, Max = 200f, Default = "120")] public float archDeg = 120f;
        [Param(ParamUnit.Fraction, "where along the solved leg the turn is concentrated (0 root .. 1 ankle)", Min = 0f, Max = 1f, Default = "0.35")] public float kneeAt = 0.35f;
        [Param(ParamUnit.Fraction, "how widely the turn spreads (1 = an even curve, small = one sharp knee)", Min = 0f, Max = 1f, Default = "0.3")] public float kneeSpread = 0.3f;
        [Param(ParamUnit.Degrees, "irregular extra bend at each joint (+-, seeded, zero-sum)", Min = 0f, Max = 45f)] public float kinkDeg;
        [Param(ParamUnit.Count, "seed of the kinks and the length jitter")] public int seed;
        [Param(ParamUnit.Degrees, "hinge range around each rest bend", Min = 5f, Max = 120f, Default = "60")] public float rangeDeg = 60f;
        [Param(ParamUnit.Degrees, "tarsus angle to the ground, 0 = 20")] public float contactAngleDeg = 35f;
        [Param(ParamUnit.Degrees, "lift synergy at mid-swing: the root half curls, the distal half opens", Default = "20")] public float swingLiftDeg = 20f;
        [Param(ParamUnit.Fraction, "mid-swing pull of the foot toward the coxa", Min = 0f, Max = 0.5f, Default = "0.1")] public float swingRetract = 0.1f;
        // ---- the look ----
        [Param(ParamUnit.Metres, "radius at the root", Example = "golfball strider 0.007")] public float rootRadius = 0.006f;
        [Param(ParamUnit.Metres, "radius at the tip", Example = "golfball strider 0.0018")] public float tipRadius = 0.002f;
        [Param(ParamUnit.Fraction, "rounding of each joint: the blend zone's half width as a share of the shorter neighbouring segment (0 = rigid segments meeting at the joint)", Min = 0f, Max = 0.9f, Default = "0.5")] public float smooth = 0.5f;
        [Param(ParamUnit.Fraction, "bead at each joint, x the leg's radius there (0 = none)", Min = 0f, Max = 3f)] public float beads;
        [Param(ParamUnit.Count, "mesh: vertices round the tube, 0 = 8", Min = 0, Max = 16)] public int around;
        // ---- the flex ----
        [Param(ParamUnit.Hz, "wobble frequency at mid-leg (the root is stiffer, the tip floppier)", Min = 0.2f, Max = 20f, Default = "2.2")] public float wobbleHz = 2.2f;
        [Param(ParamUnit.Fraction, "wobble damping ratio (0.1 springy .. 1 dead)", Min = 0f, Max = 2f, Default = "0.2")] public float wobbleDamping = 0.2f;
        [Param(ParamUnit.Fraction, "how much the joints follow their own acceleration (inertia; 0 = a rigid leg)", Min = 0f, Max = 3f, Default = "1")] public float inertia = 1f;
        [Param(ParamUnit.Metres, "sag of mid-leg under its own weight", Default = "0.01")] public float sag = 0.01f;
        [Param(ParamUnit.Fraction, "lifted tip whip: how loosely the swinging tip follows its foot (0 = exactly)", Min = 0f, Max = 1f, Default = "0.5")] public float whip = 0.5f;
        [Param(ParamUnit.Fraction, "neighbour coupling of the wobble (a continuous rod), x the spring", Min = 0f, Max = 2f, Default = "0.6")] public float coupling = 0.6f;
        [Param(ParamUnit.Fraction, "largest deflection of any joint, x the leg length", Min = 0f, Max = 0.5f, Default = "0.12")] public float maxDeflect = 0.12f;

        public override int BoneCount(in SpeciesCtx c) => segmentCount;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex

        // ------------------------------------------------------------ the generator

        // A deterministic value in [-1, 1] from (seed, k): an integer hash, identical on every runtime.
        internal static float Hash(int seed, int k)
        {
            uint h = (uint)seed * 0x9E3779B9u ^ (uint)(k + 1) * 0x85EBCA6Bu;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0xFFFFFF * 2f - 1f;
        }

        // Share of the arch at a joint at fraction t of the solved leg (unnormalised).
        float TurnWeight(float t)
        {
            float sigma = 0.04f + 0.6f * Mathf.Clamp01(kneeSpread);
            float x = (t - kneeAt) / sigma;
            return Mathf.Exp(-0.5f * x * x);
        }

        // The segments, coxa to tarsus (runtime form).
        public LegSegment[] Segments()
        {
            int n = Mathf.Clamp(segmentCount, 3, 7), solved = n - 1;
            var seg = new LegSegment[n];
            float ls = length * (1f - tarsusShare);
            var w = new float[solved];
            float sw = 0f;
            for (int k = 0; k < solved; k++) { w[k] = Mathf.Max(0.2f, 1f + lengthJitter * Hash(seed, k)); sw += w[k]; }
            for (int k = 0; k < solved; k++) seg[k].length = ls * w[k] / sw;
            seg[n - 1].length = length * tarsusShare;

            // the arch: joints 1..n-2 (the start joints of the solved segments after the first)
            int jn = solved - 1;
            var g = new float[Mathf.Max(1, jn)];
            var kink = new float[Mathf.Max(1, jn)];
            float sg = 0f, gmax = 0f, mean = 0f, along = 0f;
            for (int i = 0; i < jn; i++)
            {
                along += seg[i].length;
                g[i] = TurnWeight(along / ls); sg += g[i]; gmax = Mathf.Max(gmax, g[i]);
                kink[i] = kinkDeg * Hash(seed, 100 + i); mean += kink[i];
            }
            if (jn > 0) mean /= jn;
            seg[0].restBendDeg = riseDeg;
            along = 0f;
            for (int i = 0; i < jn; i++)
            {
                along += seg[i].length;
                float t = along / ls;
                ref LegSegment s = ref seg[i + 1];
                s.restBendDeg = -archDeg * g[i] / sg + (jn > 1 ? kink[i] - mean : 0f);
                s.liftWeight = -(g[i] / gmax) * (1f - 2f * t);
            }
            // the reach synergy scales every joint's bend in its own direction (kinks too): s < 0 straightens the whole
            // leg toward a line together, s > 0 curls it - so the reach is monotone around the rest shape
            float bmax = 0f;
            for (int i = 1; i < solved; i++) bmax = Mathf.Max(bmax, Mathf.Abs(seg[i].restBendDeg));
            for (int i = 1; i < solved; i++) seg[i].flexWeight = bmax > 0f ? seg[i].restBendDeg / bmax : 0f;
            for (int k = 0; k < solved; k++) { seg[k].minBendDeg = seg[k].restBendDeg - rangeDeg; seg[k].maxBendDeg = seg[k].restBendDeg + rangeDeg; }
            seg[n - 1].minBendDeg = -170f; seg[n - 1].maxBendDeg = 170f;   // the tarsus lies at the contact angle: never a step trigger

            // one continuous taper, root to tip
            float a = 0f;
            for (int k = 0; k < n; k++)
            {
                seg[k].radiusStart = RadiusAt(a);
                a += seg[k].length;
                seg[k].radiusEnd = RadiusAt(a);
            }
            return seg;
        }

        // The tube radius at arc length a from the root (scale 1).
        public float RadiusAt(float a)
        {
            float f = length > 0f ? Mathf.Clamp01(a / length) : 0f;
            return rootRadius + (tipRadius - rootRadius) * Mathf.Pow(f, 0.7f);
        }

        public override LegSpec ToLegSpec()
        {
            LegSpec l = BaseSpec();
            l.segments = Segments();
            l.contactAngleDeg = contactAngleDeg; l.kneeLiftDeg = swingLiftDeg; l.swingRetract = swingRetract;
            l.legSides = around;
            return l;
        }

        // Horizontal coxa-to-foot distance at which the leg stands in its rest shape (the IK's s = 0) when the foot is
        // `drop` below the coxa (scale 1; 0 = the leg cannot reach that far down). Author restOffset from it.
        public float RestReach(float drop)
        {
            LegSpec l = ToLegSpec();
            var m = new ArthropodLegModel(l.segments, l.ModelParams);
            if (m.Error != null) return 0f;
            Vector2 foot = m.RestFootLocal();
            float lt = m.TarsusLength;
            Vector2 e = foot - new Vector2(lt * m.CosC, -lt * m.SinC);   // the ankle at s = 0
            float h = drop - lt * m.SinC;                                   // the ankle's drop below the coxa
            float r2 = e.sqrMagnitude - h * h;
            return r2 > 0f ? lt * m.CosC + Mathf.Sqrt(r2) : 0f;
        }

        // ------------------------------------------------------------ the mesh

        const int kZoneSteps = 6;   // rings through each rounded joint

        // One continuous tube through the rest leg the sim solved: a buried rounded root, straight runs rigid to each
        // segment's bone, every joint rounded by a quadratic curve (half width smooth x the shorter neighbour) whose
        // rings blend from the one bone to the next, a fine tip on the foot; beads (own spheres rigid to the outer
        // segment) where asked.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            int l = c.Ordinal;
            CreatureSpecies sp = m.Species;
            CreatureSim rest = m.Rest;
            int n = sp.legs[l].SegmentCount, o = rest.LegPointOffset(l);
            var P = new Vector3[n + 1];
            for (int k = 0; k <= n; k++) P[k] = rest.LegPoints[o + k];
            var d = new Vector3[n];
            var len = new float[n];
            for (int k = 0; k < n; k++) { len[k] = (P[k + 1] - P[k]).magnitude; d[k] = PartMeshEmitter.Unit(P[k + 1] - P[k], Vector3.down); }
            var bone = new int[n];
            for (int k = 0; k < n; k++) bone[k] = sp.LegBone(l, k);
            var h = new float[n + 1];   // the rounding's half width at joint k (0 at the root and the foot)
            // a rounding at least 3 radii wide (a thin tube blended over less creases on the inside of a bend), never
            // more than 0.45 of the shorter segment
            float along = 0f;
            for (int k = 1; k < n; k++)
            {
                along += len[k - 1];
                float shorter = Mathf.Min(len[k - 1], len[k]);
                h[k] = smooth > 0f ? Mathf.Min(0.45f * shorter, Mathf.Max(smooth * 0.5f * shorter, 3f * RadiusAt(along))) : 0f;
            }

            int sides = around > 0 ? Mathf.Max(3, around) : 8;
            var q = new List<PartMeshEmitter.TubeSample>(n * (kZoneSteps + 4) + 6);
            float arc = 0f;   // along the rest leg (scale 1): the taper and uv0
            void Add(Vector3 c0, Vector3 t, float a, Vector4 skin)
            {
                float r = RadiusAt(a), dr = (RadiusAt(a + 1e-3f) - r) / 1e-3f;
                q.Add(PartMeshEmitter.Sample(c0, t, r, dr, a, skin));
            }
            // the root: buried by its radius, rounded
            float r0 = RadiusAt(0f);
            q.Add(PartMeshEmitter.Sample(P[0] - d[0] * r0, d[0], 0f, 0f, -r0, PartMeshEmitter.Rigid(bone[0]), true));
            q.Add(PartMeshEmitter.Sample(P[0] - d[0] * (0.5f * r0), d[0], 0.87f * r0, 1.7f, -0.5f * r0, PartMeshEmitter.Rigid(bone[0])));
            for (int k = 0; k < n; k++)
            {
                Vector4 rigid = PartMeshEmitter.Rigid(bone[k]);
                // the straight run of segment k, between the roundings
                float s0 = h[k], s1 = len[k] - h[k + 1];
                int runs = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / Mathf.Max(0.02f, 0.25f * len[k])));
                for (int i = 0; i <= runs; i++)
                {
                    if (i == 0 && k > 0) continue;   // the rounding before ended on it
                    float s = s0 + (s1 - s0) * i / runs;
                    Add(P[k] + d[k] * s, d[k], arc + s, rigid);
                }
                arc += len[k];
                if (k + 1 >= n) break;
                // the rounding at joint k + 1: a quadratic curve from the end of this run to the start of the next
                int j = k + 1;
                Vector3 A = P[j] - d[k] * h[j], C = P[j] + d[j] * h[j];
                for (int i = 1; i <= kZoneSteps; i++)
                {
                    float u = (float)i / kZoneSteps;
                    Vector3 p = (1f - u) * (1f - u) * A + 2f * u * (1f - u) * P[j] + u * u * C;
                    Vector3 t = PartMeshEmitter.Unit(2f * (1f - u) * (P[j] - A) + 2f * u * (C - P[j]), d[j]);
                    float w1 = u * u * (3f - 2f * u);
                    Vector4 skin = h[j] > 0f ? PartMeshEmitter.Blend(bone[k], bone[j], w1) : PartMeshEmitter.Rigid(i < kZoneSteps ? bone[k] : bone[j]);
                    if (h[j] <= 0f && i < kZoneSteps) continue;   // rigid segments: the next run starts at the joint
                    Add(p, t, arc - h[j] + 2f * h[j] * u, skin);
                }
            }
            // the tip: a fine rounded point on the foot
            float rt = Mathf.Max(PartMeshEmitter.MinRadius, RadiusAt(length));
            Vector4 tipSkin = PartMeshEmitter.Rigid(bone[n - 1]);
            q.Add(PartMeshEmitter.Sample(P[n] + d[n - 1] * (0.6f * rt), d[n - 1], 0.8f * rt, -1.3f, arc + 0.6f * rt, tipSkin));
            q.Add(PartMeshEmitter.Sample(P[n] + d[n - 1] * rt, d[n - 1], 0f, 0f, arc + rt, tipSkin, true));
            Vector3 x0 = PartMeshEmitter.Unit(Vector3.Cross(d[0], Vector3.up), PartMeshEmitter.Unit(Vector3.Cross(d[0], Vector3.forward), Vector3.right));
            m.Tube(q, x0, sides, PartMeshEmitter.PartLeg);

            // beads: a sphere at each joint, rigid to the outer segment's bone
            if (beads > 1f)
            {
                float a = 0f;
                for (int k = 1; k < n; k++)
                {
                    a += len[k - 1];
                    float rb = beads * RadiusAt(a);
                    Matrix4x4 f = Matrix4x4.TRS(P[k], Quaternion.LookRotation(d[k], Mathf.Abs(d[k].y) < 0.95f ? Vector3.up : Vector3.forward), Vector3.one);
                    m.Ellipsoid(f, Vector3.zero, rb, rb, rb, sides, 5, PartMeshEmitter.PartLeg, bone[k]);
                }
            }
        }

        public override string Validate(in SpeciesCtx c)
        {
            if (segmentCount < 3 || segmentCount > 7) return $"{id}: a BendyLeg needs 3..7 segments";
            if (!(length > 0f)) return $"{id}: length must be > 0";
            if (!(rootRadius > 0f) || tipRadius < 0f) return $"{id}: rootRadius > 0, tipRadius >= 0";
            if (tarsusShare < 0.03f || tarsusShare > 0.5f) return $"{id}: tarsusShare must be in 0.03..0.5";
            if (lengthJitter < 0f || lengthJitter > 0.6f) return $"{id}: lengthJitter must be in 0..0.6";
            if (rangeDeg < 5f) return $"{id}: rangeDeg must be >= 5";
            if (!(wobbleHz > 0f) || wobbleDamping < 0f || inertia < 0f || coupling < 0f) return $"{id}: wobbleHz > 0, wobbleDamping, inertia, coupling >= 0";
            if (whip < 0f || whip > 1f) return $"{id}: whip must be in 0..1";
            if (smooth < 0f || smooth > 0.9f) return $"{id}: smooth must be in 0..0.9";
            if (maxDeflect < 0f || maxDeflect > 0.5f) return $"{id}: maxDeflect must be in 0..0.5";
            if (role == LimbRole.Folded || role == LimbRole.WingArm) return $"{id}: a BendyLeg walks or feels (not a wing's arm)";
            LegSpec l = ToLegSpec();
            var m = new ArthropodLegModel(l.segments, l.ModelParams);
            if (m.Error != null) return $"{id}: {m.Error} (archDeg, kinkDeg, kneeSpread)";
            return base.Validate(c);
        }
    }
}
