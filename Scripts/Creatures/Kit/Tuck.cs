// Creature kit: Tuck - a posture part. When the creature is scared it tucks: the walking stops (CreatureSim.Halt),
// the body crouches to the ground (CreatureSim.Crouch), its arthropod legs fold into the pose its LegFold rows give
// (each segment's pitch in a vertical fold plane beside the body - one plane per limb, or one per segment with
// segmentYawDeg, so a leg can fold flat: the merus out under the shell's margin, the rest swept back along the body on
// the ground; the rock crab's legs and claws fold onto the shell's surface, not into it), its EyeStalks swing to their
// tuckDirection (stalks first), and once tucked the whole sim sleeps (CreatureSim.Asleep: no sub-steps, exactly zero
// motion). After the hold (longer for a stronger scare) it peeks - the stalks rise first, the body stays down - then
// unfolds and walks on. Another scare at any point tucks it again (from wherever it is).
//
// The scare is a generic input: CreatureSim.Scare(intensity 0..1) - any behaviour, threat or system may call it; the
// runtime takes the strongest since the last tick. For now (rock crab) a per-individual random timer also scares it
// (randomScareSeconds: uniform 0.25..1.75 x the mean; the first 0.5..1.5, golden-ratio staggered over consecutive seeds; seeded by the individual's seed, Froude-timed, so individuals never
// tuck in sync). 0 = only real scares.
//
// The pose is drawn, not simulated: the sim keeps its planted feet (the gait is untouched), and the runtime - the
// LAST part (Validate), so it writes after every other part - blends each folded leg's segment rotations from the IK
// pose to the fold pose (forward kinematics from the IK's coxa: exact lengths, joints connected) and swings the
// stalks and their eyes about the stalks' roots. At weight 0 it writes nothing (bit-exact pass-through). Froude:
// times x sqrt s, the crouch x s. Allocation-free after construction; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // How one arthropod limb folds: a vertical plane beside the body (yawed toward the tail or the head) and each
    // segment's pitch in it.
    [Serializable]
    public struct LegFold
    {
        [Param(ParamUnit.PartRef, "the ArthropodLeg it folds; a mirrored leg's id (without .L / .R) folds both")] public string leg;
        [Param(ParamUnit.Degrees, "the fold plane: 0 = straight out sideways, + swept toward the tail, - toward the head (mirrored by side)", Min = -90, Max = 90)] public float yawDeg;
        [Param(ParamUnit.Degrees, "each segment's pitch in the fold plane (body out): 0 = out level, 90 = up, 180 = back in level, -90 = down; one per segment")] public float[] pitchDeg;
        [Param(ParamUnit.Fraction, "starts folding this fraction of the curl later (and unfolds that much earlier)", Min = 0, Max = 0.6f)] public float lag;
        [Param(ParamUnit.Degrees, "optional: a fold plane per segment (its yaw, as yawDeg; one per segment), so a limb can fold flat - the merus out, the rest swept back along the body's side; empty = every segment in yawDeg's plane")] public float[] segmentYawDeg;
    }

    [Serializable]
    [Part("Tuck", PartKind.Rigid, Bones = "0 (poses the others' bones)", Summary = "Posture: when scared (CreatureSim.Scare, or a random timer per individual) it stops, crouches, folds its ArthropodLegs (LegFold rows: a pitch per segment in a fold plane) and EyeStalks away, sleeps exactly still, then peeks (stalks first), unfolds and walks on. Rock mimicry, playing dead, a pill bug's roll.",
          System = "TuckRuntime (part runtime, stage Late; the LAST part: blends the other parts' bones) + CreatureSim Halt / Crouch / Asleep", KitOnly = true,
          UseWhen = "a creature that hides by folding up: rock crabs, box crabs, armadillos, pill bugs, tortoises")]
    public sealed class Tuck : CreaturePart
    {
        [Param(ParamUnit.Seconds, "time to reach the tucked pose", Example = "rock crab 0.45")] public float curlSeconds = 0.45f;
        [Param(ParamUnit.Seconds, "time held tucked after a full scare (x 0.5..1 with the intensity, x 0.7..1.3 per tuck)", Example = "4")] public float holdSeconds = 4f;
        [Param(ParamUnit.Seconds, "the peek: the stalks rise and look about while the body stays down")] public float peekSeconds = 1.2f;
        [Param(ParamUnit.Seconds, "time to unfold")] public float uncurlSeconds = 0.6f;
        [Param(ParamUnit.Metres, "how far the body crouches (every joint's clearance lowered)")] public float crouch = 0.05f;
        [Param(ParamUnit.Seconds, "random scares: mean time up and about between them (uniform 0.25..1.75 x this, per individual); 0 = only real scares (CreatureSim.Scare)", Example = "rock crab 14")] public float randomScareSeconds = 0f;
        [Param(ParamUnit.Fraction, "a scare weaker than this is ignored", Min = 0, Max = 1)] public float minScare = 0.25f;
        [Param(ParamUnit.Struct, "how each folded leg folds (legs not listed keep their IK pose)")] public LegFold[] legs = new LegFold[0];

        public override int BoneCount(in SpeciesCtx c) => 0;
        protected override void CopyArrays()
        {
            legs = (LegFold[])legs?.Clone();
            if (legs != null) for (int i = 0; i < legs.Length; i++) { legs[i].pitchDeg = (float[])legs[i].pitchDeg?.Clone(); legs[i].segmentYawDeg = (float[])legs[i].segmentYawDeg?.Clone(); }
        }

        // The limb a fold row names: its own id, or a mirrored limb's base id.
        public static bool Names(string row, string limbId)
            => !string.IsNullOrEmpty(row) && (limbId == row || limbId == row + ".L" || limbId == row + ".R");

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Tuck needs a SpineBody";
            if (c.IndexOf(this) != c.Parts.Count - 1) return $"{id}: Tuck must be the last part (it poses the other parts' bones after they wrote them)";
            for (int i = 0; i < c.Parts.Count; i++) if (c.Parts[i] is Tuck && !ReferenceEquals(c.Parts[i], this)) return $"{id}: only one Tuck part";
            if (!(curlSeconds > 0f) || holdSeconds < 0f || peekSeconds < 0f || !(uncurlSeconds > 0f)) return $"{id}: curlSeconds, uncurlSeconds > 0; holdSeconds, peekSeconds >= 0";
            if (crouch < 0f || randomScareSeconds < 0f || minScare < 0f || minScare > 1f) return $"{id}: crouch >= 0, randomScareSeconds >= 0, minScare 0..1";
            if (legs == null) return null;
            foreach (LegFold f in legs)
            {
                if (f.lag < 0f || f.lag > 0.6f) return $"{id}: fold '{f.leg}': lag must be 0..0.6";
                int found = 0;
                foreach (CreaturePart p in c.Parts)
                {
                    if (!Names(f.leg, p.id)) continue;
                    if (!(p is ArthropodLeg a)) return $"{id}: fold '{f.leg}' names {p.id}, not an ArthropodLeg";
                    int n = a.segments != null ? a.segments.Length : 0;
                    if (f.pitchDeg == null || f.pitchDeg.Length != n) return $"{id}: fold '{f.leg}' needs {n} pitches (one per segment of {p.id})";
                    if (f.segmentYawDeg != null && f.segmentYawDeg.Length != 0 && f.segmentYawDeg.Length != n) return $"{id}: fold '{f.leg}': segmentYawDeg needs none or {n} yaws (one per segment of {p.id})";
                    found++;
                }
                if (found == 0) return $"{id}: fold '{f.leg}' names no limb";
            }
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c) => c.Sim != null ? new TuckRuntime(this, c.Sim) : null;
    }

    public sealed class TuckRuntime : IPartRuntime
    {
        public enum Phase { Active = 0, Curling = 1, Curled = 2, Peeking = 3, Uncurling = 4 }

        const float kSettleSeconds = 0.2f;   // tucked this long (x sqrt s) before it sleeps: the crouch and grounding settle
        const float kStalkLead = 0.6f;       // the stalks fold in this share of the curl time (they go first)
        const float kPeekRise = 0.35f;       // the stalks rise in this share of the peek

        readonly Tuck t;
        readonly CreatureSim sim;
        readonly float L, T;
        // folded legs: leg index, fold row, side, segment lengths (this individual's), first bone
        readonly int[] legIndex, legRow, legSide, legBone;
        readonly float[][] legLength;
        readonly float[][] pitchCos, pitchSin;
        readonly float[][] yawCos, yawSin;   // per segment (all equal without segmentYawDeg)
        readonly float[] baseCos, baseSin;   // the limb's own fold plane (yawDeg): signs the plane normals
        // stalks: their runtime's bone, root offset (joint frame, mirrored), joint, fold (joint frame), eye bone (-1 none)
        readonly int[] stalkBone, stalkJoint, stalkEye;
        readonly Vector3[] stalkOffset;
        readonly Quaternion[] stalkFold;

        float activeClock, nextRandom, u, stalk, hold, settle, peek;   // clocks in Froude seconds (x 1 / sqrt s)
        int scares;

        public Phase State { get; private set; }
        public float Progress => u;                 // 0 standing .. 1 tucked (the curl)
        public float StalkFold => stalk;            // 0 up .. 1 folded
        public int Tucks { get; private set; }      // tucks started
        public float LastIntensity { get; private set; }
        public Tuck Part => t;
        public int FoldedLegs => legIndex.Length;
        public int LegOf(int k) => legIndex[k];

        public TuckRuntime(Tuck tuck, CreatureSim sim)
        {
            t = tuck; this.sim = sim;
            L = sim.SizeScale; T = sim.TimeScale;
            CreatureSpecies sp = sim.Species;
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            int[] ordinal = KitView.Ordinals(parts);
            var li = new List<int>(); var lr = new List<int>(); var ls = new List<int>();
            var sb = new List<int>(); var sj = new List<int>(); var se = new List<int>(); var so = new List<Vector3>(); var sf = new List<Quaternion>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is ArthropodLeg a && tuck.legs != null)
                    for (int r = 0; r < tuck.legs.Length; r++)
                        if (Tuck.Names(tuck.legs[r].leg, a.id)) { li.Add(ordinal[i]); lr.Add(r); ls.Add(a.at.side < 0 ? -1 : 1); break; }
                if (parts[i] is EyeStalk es && es.tuckDirection.sqrMagnitude > 1e-8f)
                {
                    sb.Add(sp.Layout.Start(i)); sj.Add(es.at.joint); so.Add(es.Mirror(es.at.offset)); sf.Add(es.LocalFold());
                    se.Add(es.carriesEye ? sp.EyeBone(es.EyeIndex) : -1);
                }
            }
            legIndex = li.ToArray(); legRow = lr.ToArray(); legSide = ls.ToArray();
            int n = legIndex.Length;
            legBone = new int[n]; legLength = new float[n][]; pitchCos = new float[n][]; pitchSin = new float[n][];
            yawCos = new float[n][]; yawSin = new float[n][]; baseCos = new float[n]; baseSin = new float[n];
            for (int k = 0; k < n; k++)
            {
                LegSpec spec = sp.legs[legIndex[k]];
                LegFold f = tuck.legs[legRow[k]];
                int segs = spec.segments.Length;
                legBone[k] = sp.LegBone(legIndex[k], 0);
                legLength[k] = new float[segs]; pitchCos[k] = new float[segs]; pitchSin[k] = new float[segs];
                yawCos[k] = new float[segs]; yawSin[k] = new float[segs];
                bool perSegment = f.segmentYawDeg != null && f.segmentYawDeg.Length == segs;
                baseCos[k] = Mathf.Cos(f.yawDeg * Mathf.Deg2Rad); baseSin[k] = Mathf.Sin(f.yawDeg * Mathf.Deg2Rad);
                for (int s = 0; s < segs; s++)
                {
                    legLength[k][s] = spec.segments[s].length * L;
                    float p = f.pitchDeg[s] * Mathf.Deg2Rad;
                    pitchCos[k][s] = Mathf.Cos(p); pitchSin[k][s] = Mathf.Sin(p);
                    float y = (perSegment ? f.segmentYawDeg[s] : f.yawDeg) * Mathf.Deg2Rad;
                    yawCos[k][s] = Mathf.Cos(y); yawSin[k][s] = Mathf.Sin(y);
                }
            }
            stalkBone = sb.ToArray(); stalkJoint = sj.ToArray(); stalkEye = se.ToArray(); stalkOffset = so.ToArray(); stalkFold = sf.ToArray();
            nextRandom = RandomInterval();
            State = Phase.Active;
        }

        public PartStage Stage => PartStage.Late;

        // uniform 0.25..1.75 x the mean, from the individual's seed and the count so far (Froude-timed). The first:
        // 0.5..1.5 x the mean (a fresh individual walks a while first) on the golden-ratio sequence of the seed, so a
        // group spawned with consecutive seeds (the lab's) starts out evenly staggered, never two at once.
        float RandomInterval()
        {
            if (!(t.randomScareSeconds > 0f)) return float.PositiveInfinity;
            if (scares == 0)
            {
                double g = (sim.Seed & 0xFFFF) * 0.6180339887498949;
                return t.randomScareSeconds * (0.5f + (float)(g - Math.Floor(g)));
            }
            return t.randomScareSeconds * (0.25f + 1.5f * Hash01(sim.Seed * 7919 + scares * 104729 + 17));
        }

        static float Hash01(int k)
        {
            uint x = (uint)k * 0x9E3779B1u;
            x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        void Scared(float intensity)
        {
            LastIntensity = intensity;
            float h = t.holdSeconds * (0.5f + 0.5f * Mathf.Clamp01(intensity)) * (0.7f + 0.6f * Hash01(sim.Seed * 31 + Tucks * 977 + 5));
            switch (State)
            {
                case Phase.Active: case Phase.Uncurling:
                    State = Phase.Curling; settle = 0f; hold = h; Tucks++;
                    sim.Halt = true; sim.Asleep = false;
                    break;
                case Phase.Curling: case Phase.Curled:
                    hold = Mathf.Max(hold, h);
                    break;
                case Phase.Peeking:
                    State = Phase.Curled; hold = h;   // the stalks snap back down (still asleep)
                    break;
            }
        }

        // Every phase runs on Froude time (dt / sqrt s, seconds at scale 1) and snaps to its end within kEps: a phase
        // length that is a whole number of frames (0.4 s at 60 fps) then ends on the same frame at every size, not
        // on whichever side the rounding of the sum falls (that put a 0.9x and a 1.1x individual a frame apart).
        const float kEps = 1e-4f;

        static float Toward(float x, float to, float step)
        {
            x = Mathf.MoveTowards(x, to, step);
            return Mathf.Abs(x - to) < kEps ? to : x;
        }

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            float h = dt / T;   // Froude time
            float s = sim.TakeScare();
            // the random timer runs only while it is up and about (a tuck never cuts a walk short of its interval)
            if (State == Phase.Active)
            {
                activeClock += h;
                if (activeClock >= nextRandom - kEps) { s = Mathf.Max(s, 1f); scares++; nextRandom = activeClock + RandomInterval(); }
            }
            if (s >= t.minScare && s > 0f) Scared(s);

            switch (State)
            {
                case Phase.Curling:
                    u = Toward(u, 1f, h / t.curlSeconds);
                    stalk = Toward(stalk, 1f, h / (kStalkLead * t.curlSeconds));
                    if (u >= 1f)
                    {
                        settle += h;
                        if (settle >= kSettleSeconds - kEps) { State = Phase.Curled; sim.Asleep = true; }
                    }
                    break;
                case Phase.Curled:
                    stalk = Toward(stalk, 1f, h / (kStalkLead * t.curlSeconds));
                    hold -= h;
                    if (hold <= kEps && stalk >= 1f) { State = Phase.Peeking; peek = 0f; }
                    break;
                case Phase.Peeking:
                    peek += h;
                    stalk = Toward(stalk, 0f, h / (kPeekRise * Mathf.Max(1e-3f, t.peekSeconds)));
                    if (peek >= t.peekSeconds - kEps) { State = Phase.Uncurling; sim.Asleep = false; }
                    break;
                case Phase.Uncurling:
                    stalk = Toward(stalk, 0f, h / (kStalkLead * t.curlSeconds));
                    u = Toward(u, 0f, h / t.uncurlSeconds);
                    if (u <= 0f && stalk <= 0f) { State = Phase.Active; sim.Halt = false; }
                    break;
            }
            // asleep, the crouch is frozen with everything else (it is already at its full value)
            if (!sim.Asleep) sim.Crouch = Ease(u) * t.crouch * L;
        }

        public float LegWeight(int k)
        {
            float lag = t.legs[legRow[k]].lag;
            return Ease((u - lag) / (1f - lag));
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            if (u <= 0f && stalk <= 0f) return;   // standing: the other parts' bones exactly as they wrote them
            float sc = L;
            var scv = new Vector3(sc, sc, sc);
            Vector3[] pts = sim.LegPoints;
            CreatureSpecies sp = sim.Species;
            for (int k = 0; k < legIndex.Length; k++)
            {
                float w = LegWeight(k);
                if (w <= 0f) continue;
                int l = legIndex[k];
                int o = sim.LegPointOffset(l);
                Vector3 nIK = sim.LegPlaneNormal(l);
                Quaternion R = sim.Rotations[sp.legs[l].attachJoint];
                Vector3 up = R * Vector3.up;
                Vector3 baseOut = R * new Vector3(legSide[k] * baseCos[k], 0f, -baseSin[k]);
                float sign = Vector3.Dot(Vector3.Cross(baseOut, up), nIK) < 0f ? -1f : 1f;
                Vector3 p = pts[o];
                float[] len = legLength[k];
                for (int s = 0; s < len.Length; s++)
                {
                    Vector3 outDir = R * new Vector3(legSide[k] * yawCos[k][s], 0f, -yawSin[k][s]);
                    Quaternion qIK = ArthropodLegIK.SegmentRotation(pts[o + s], pts[o + s + 1], nIK);
                    Vector3 d = outDir * pitchCos[k][s] + up * pitchSin[k][s];
                    // the fold plane's normal, one sign for the whole limb (the limb plane's, against the IK plane's), so
                    // segments folded into planes swept far round (segmentYawDeg) never flip about their own axis: a
                    // claw stays the right way up and a fringe of hairs stays underneath
                    Vector3 nT = Vector3.Cross(outDir, up) * sign;
                    Quaternion qT = ArthropodLegIK.SegmentRotation(Vector3.zero, d, nT);
                    Quaternion q = Quaternion.Slerp(qIK, qT, w);
                    dst[offset + legBone[k] + s] = Matrix4x4.TRS(p, q, scv);
                    p += q * Vector3.forward * len[s];
                }
            }
            float ws = Ease(stalk);
            if (ws <= 0f) return;
            for (int k = 0; k < stalkBone.Length; k++)
            {
                int j = stalkJoint[k];
                Quaternion R = sim.Rotations[j];
                Vector3 root = sim.Joints[j] + R * (stalkOffset[k] * sc);
                Quaternion q = R * Quaternion.Slerp(Quaternion.identity, stalkFold[k], ws) * Quaternion.Inverse(R);
                Matrix4x4 pivot = Matrix4x4.TRS(root, q, Vector3.one) * Matrix4x4.TRS(-root, Quaternion.identity, Vector3.one);
                dst[offset + stalkBone[k]] = pivot * dst[offset + stalkBone[k]];
                if (stalkEye[k] >= 0) dst[offset + stalkEye[k]] = pivot * dst[offset + stalkEye[k]];
            }
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }   // folded limbs lie within the body's reach
        public void DrawDebug(ICreatureDebugDraw d) { }
    }
}
