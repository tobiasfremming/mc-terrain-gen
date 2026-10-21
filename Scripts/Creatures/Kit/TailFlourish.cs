// Creature kit (Apex redesign §5.6): TailFlourish - the living tail. A pose drawn over the sim, like Tuck's: the
// sim's chain (and so the walk and the flight) is never touched; once per tick (stage Late) the runtime re-poses the
// body's tail bones - the body's own joint bones from fromJoint to the last - by forward kinematics from the tail root:
//   ground   the tail leaves the hips low (the first two tail joints stay as the sim has them) and curls upward in the
//            body's vertical plane, groundLoopDeg per joint, building a big loop that rises above the hips; a slow
//            drift (groundDriftDeg at groundHz, travelling down the tail) keeps it alive while standing.
//   air      eased in by the flight's air weight at the tail root (easeSeconds; out again on touchdown): a lazy curl -
//            an S of airCurlDeg per joint travelling down the tail (waveLength tail lengths) at airHz, about the
//            ribbon's rolled up axis (the flat ribbon bends sideways) - and a travelling roll of each bone about its
//            own long axis growing from 0 at the root to airRollDeg at the tip. Decoration only: it never steers.
// Each joint's added bend (the rotation the overlay adds between its two segments) is clamped to that joint's
// bendLimitDeg. Exact lengths: each posed segment is the sim's segment rotated. At zero strengths the bones are the
// sim's bit for bit (displacements accumulate from zero, an identity rotation is skipped). Per-individual phases from
// sim.Seed; Froude: clocks run in dt / sqrt s (Hz / sqrt s, seconds x sqrt s). The rest pose (never stepped) writes
// nothing, so the bind pose is the plain chain. Must be the LAST part (it writes over the body's bones after every
// other part wrote its own). Allocation-free after construction; main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("TailFlourish", PartKind.Chain, Bones = "0 (re-poses the body's tail bones)", Summary = "The living tail: a pose drawn over the sim's tail joints (the walk and the flight untouched). Standing, the tail rises in a calm loop above the hips and drifts; in the air (eased in by the flight's air weight) a travelling roll about each bone's own axis, growing to the tip, and a lazy curl travelling down it. Added bend clamped to each joint's bend limit.",
          System = "TailFlourishRuntime (part runtime, stage Late; the LAST part: writes over the body's tail bones)", KitOnly = true,
          UseWhen = "a long decorative tail that should look alive without steering anything: ribbon tails, plumes, a dragon's tail in flight")]
    public sealed class TailFlourish : CreaturePart
    {
        [Param(ParamUnit.Joint, "the tail root: the first joint of the tail (it and the one behind it stay as the sim has them on the ground)", Example = "fixture 12")] public int fromJoint = 12;
        [Param(ParamUnit.Degrees, "air: the tip's roll amplitude about its own long axis (grows linearly from 0 at the root)", Min = 0, Max = 180, Default = "70")] public float airRollDeg = 70f;
        [Param(ParamUnit.Degrees, "air: the travelling curl's amplitude per joint (sideways, in the ribbon's rolled frame)", Min = 0, Max = 45, Default = "9")] public float airCurlDeg = 9f;
        [Param(ParamUnit.Hz, "air: the roll and curl frequency", Min = 0, Max = 5, Default = "0.35")] public float airHz = 0.35f;
        [Param(ParamUnit.Fraction, "the travelling wave's length, in tail lengths (the tip lags the root by 1 / waveLength cycles)", Min = 0.05f, Max = 10, Default = "0.6")] public float waveLength = 0.6f;
        [Param(ParamUnit.Degrees, "ground: the upward curl per joint (behind the first two tail joints) building the loop", Min = 0, Max = 45, Default = "7")] public float groundLoopDeg = 7f;
        [Param(ParamUnit.Degrees, "ground: the slow drift's amplitude per joint (up-down on the loop and sideways)", Min = 0, Max = 30, Default = "2")] public float groundDriftDeg = 2f;
        [Param(ParamUnit.Hz, "ground: the drift's frequency", Min = 0, Max = 5, Default = "0.12")] public float groundHz = 0.12f;
        [Param(ParamUnit.Seconds, "time to blend between the ground loop and the air motion (the flight's air weight at the tail root, eased)", Min = 0, Max = 10, Default = "0.6")] public float easeSeconds = 0.6f;

        public override int BoneCount(in SpeciesCtx c) => 0;

        // The body part: the one the tail's bones belong to (the chain from the head to the tail tip).
        internal static int BodyIndex(IReadOnlyList<CreaturePart> parts)
        {
            for (int i = 0; i < parts.Count; i++) if (parts[i] is ISpineChainBody || parts[i] is SpineBody) return i;
            return -1;
        }

        internal static float TailTip(CreaturePart body) =>
            body is ISpineChainBody b ? b.TailTipLength : body is SpineBody s ? s.tailTipLength : 0f;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a TailFlourish needs a spine body (FlowBody, SpineBody)";
            if (BodyIndex(c.Parts) < 0) return $"{id}: a TailFlourish needs a spine body (FlowBody, SpineBody)";
            if (c.IndexOf(this) != c.Parts.Count - 1) return $"{id}: TailFlourish must be the last part (it poses the body's tail bones after every other part wrote its own)";
            for (int i = 0; i < c.Parts.Count; i++) if (c.Parts[i] is TailFlourish && !ReferenceEquals(c.Parts[i], this)) return $"{id}: only one TailFlourish part";
            if (fromJoint < 1 || fromJoint > c.Joints - 3) return $"{id}: fromJoint {fromJoint} out of range 1..{c.Joints - 3} (the tail needs at least two segments behind it)";
            if (airRollDeg < 0f || airCurlDeg < 0f || groundLoopDeg < 0f || groundDriftDeg < 0f) return $"{id}: airRollDeg, airCurlDeg, groundLoopDeg, groundDriftDeg must be >= 0";
            if (airHz < 0f || groundHz < 0f) return $"{id}: airHz, groundHz must be >= 0";
            if (!(waveLength > 0f)) return $"{id}: waveLength must be > 0";
            if (easeSeconds < 0f) return $"{id}: easeSeconds must be >= 0";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c) => c.Sim != null ? new TailFlourishRuntime(this, c.Sim) : null;
    }

    public sealed class TailFlourishRuntime : IPartRuntime
    {
        // RED test hooks (CreatureTailFlourishTests): the pose also written into sim.Joints; the bend clamp off; the
        // bounds not grown; the clocks in plain seconds; the ground loop curling down instead of up.
        internal static bool WritesSimForTest, NoClampForTest, NoBoundsForTest, IgnoreSizeForTest, DroopForTest;

        readonly TailFlourish t;
        readonly CreatureSim sim;
        readonly int root, n, bone0;
        readonly float L, T, tipLength;
        readonly float[] limit;          // per joint: bendLimitDeg (radians)
        readonly float[] along;          // per joint: tail lengths from the root (0 root .. 1 the last joint)
        readonly Vector3[] posed;        // per joint: the posed centre (the sim's ahead of the root)
        readonly Quaternion[] posedRot;  // per joint: the posed bone rotation
        readonly float[] curlDeg, rollDeg, addedDeg;
        Vector3 tip;
        bool stepped;
        float clock, weight;             // Froude seconds; eased air weight 0 ground .. 1 air
        readonly float phaseAir, phaseRoll, phaseGround, phaseSide;

        public TailFlourish Part => t;
        public int Root => root;
        public float Weight => Ease(weight);
        // the tip bone's roll about its own axis (deg) and a joint's air curl (deg, signed), as last posed
        public float TipRollDeg => rollDeg[n - 1];
        public float CurlDeg(int j) => curlDeg[j];
        // the bend the overlay added at joint j (deg, the angle of its added rotation)
        public float AddedBendDeg(int j) => addedDeg[j];
        public Vector3 PosedJoint(int j) => posed[j];
        public Vector3 PosedTip => tip;
        public bool Posed => stepped;

        public TailFlourishRuntime(TailFlourish part, CreatureSim sim)
        {
            t = part; this.sim = sim;
            L = sim.SizeScale; T = sim.TimeScale;
            CreatureSpecies sp = sim.Species;
            n = sim.Joints.Length;
            root = Mathf.Clamp(part.fromJoint, 0, n - 1);
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            int body = TailFlourish.BodyIndex(parts);
            bone0 = body >= 0 ? sp.Layout.Start(body) : 0;
            tipLength = (body >= 0 ? TailFlourish.TailTip(parts[body]) : 0f) * L;
            limit = new float[n]; along = new float[n];
            for (int j = 0; j < n; j++)
            {
                limit[j] = sp.joints[j].bendLimitDeg * Mathf.Deg2Rad;
                along[j] = j <= root ? 0f : (j - root) / (float)Mathf.Max(1, n - 1 - root);
            }
            posed = new Vector3[n]; posedRot = new Quaternion[n];
            curlDeg = new float[n]; rollDeg = new float[n]; addedDeg = new float[n];
            phaseAir = Hash01(sim.Seed * 7919 + 11); phaseRoll = Hash01(sim.Seed * 7919 + 23);
            phaseGround = Hash01(sim.Seed * 7919 + 37); phaseSide = Hash01(sim.Seed * 7919 + 53);
        }

        public PartStage Stage => PartStage.Late;

        static float Hash01(int k)
        {
            uint x = (uint)k * 0x9E3779B1u;
            x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        public void Step(in StepCtx c, float dt)
        {
            float h = !(dt > 0f) ? 0f : IgnoreSizeForTest ? dt : dt / T;   // Froude seconds
            clock += h;
            CreatureFlight f = sim.Flight;
            float target = f != null ? f.AirWeight[root] : 0f;
            weight = t.easeSeconds > 0f ? Mathf.MoveTowards(weight, target, h / t.easeSeconds) : target;
            stepped = true;
            Pose();
            if (WritesSimForTest)
            {
                Vector3[] J = sim.Joints;
                for (int j = root + 1; j < n; j++) J[j] = posed[j];
            }
        }

        // The pose over the sim's current chain (FK from the root; called by Step, WriteBones and EncapsulateBounds, so
        // the bones always follow the chain the sim holds now). Exact lengths: each posed segment is the sim's rotated.
        void Pose()
        {
            Vector3[] J = sim.Joints;
            Quaternion[] R = sim.Rotations;
            for (int j = 0; j <= root; j++) { posed[j] = J[j]; posedRot[j] = R[j]; curlDeg[j] = 0f; rollDeg[j] = 0f; addedDeg[j] = 0f; }
            float w = Ease(weight), g = 1f - w;
            const float TwoPi = 2f * Mathf.PI;
            float pAir = TwoPi * (t.airHz * clock + phaseAir), pRoll = TwoPi * (t.airHz * clock + phaseRoll);
            float pGround = TwoPi * (t.groundHz * clock + phaseGround), pSide = TwoPi * (t.groundHz * clock + phaseSide);
            float k = TwoPi / t.waveLength;
            Quaternion Q = Quaternion.identity;   // the accumulated added rotation (applied to the sim's segments)
            bool turned = false;
            Vector3 shift = Vector3.zero;         // the posed joint - the sim's joint
            for (int i = root + 1; i < n; i++)
            {
                int j = i - 1;                    // the joint whose added bend turns segment j -> i
                Vector3 d = J[i] - J[j];
                // the added bend at joint j, a rotation vector (axis x radians) in the sim's frame at j
                Quaternion Rj = R[j];
                Vector3 fwd = Rj * Vector3.forward, up = Rj * Vector3.up, right = Rj * Vector3.right;
                float x = along[j];
                float roll = w * t.airRollDeg * x * Mathf.Sin(pRoll - k * x);   // joint j's bone roll (0 at the root)
                float curl = w * t.airCurlDeg * Mathf.Sin(pAir - k * x);
                Vector3 bend = Vector3.zero;
                if (curl != 0f)
                {
                    // sideways in the ribbon's rolled frame: about its up axis turned by its roll
                    Vector3 upRolled = roll != 0f ? Quaternion.AngleAxis(roll, fwd) * up : up;
                    bend += upRolled * (curl * Mathf.Deg2Rad);
                }
                if (j >= root + 1 && g > 0f)
                {
                    // the loop: upward in the body's vertical plane (about right: the tail's segment turns toward up),
                    // breathing with the drift, and a slow sideways drift
                    float loop = t.groundLoopDeg + t.groundDriftDeg * Mathf.Sin(pGround - k * x);
                    float side = t.groundDriftDeg * Mathf.Sin(pSide - k * x);
                    if (DroopForTest) loop = -loop;
                    if (loop != 0f) bend += right * (g * loop * Mathf.Deg2Rad);
                    if (side != 0f) bend += up * (g * side * Mathf.Deg2Rad);
                }
                float ang = bend.magnitude;
                if (!NoClampForTest && ang > limit[j]) { bend *= limit[j] / ang; ang = limit[j]; }
                curlDeg[j] = curl; rollDeg[j] = roll; addedDeg[j] = ang * Mathf.Rad2Deg;
                if (ang > 0f) { Q = Q * Quaternion.AngleAxis(ang * Mathf.Rad2Deg, bend / ang); turned = true; }
                if (turned) { Vector3 dq = Q * d; shift += dq - d; }
                posed[i] = J[i] + shift;
                // bone i points from i toward i - 1 (the sim's frame turned with its segment), rolled about its own axis
                float rollI = w * t.airRollDeg * along[i] * Mathf.Sin(pRoll - k * along[i]);
                Quaternion q = turned ? Q * R[i] : R[i];
                if (rollI != 0f) q = Quaternion.AngleAxis(rollI, q * Vector3.forward) * q;
                posedRot[i] = q;
                if (i == n - 1) { rollDeg[i] = rollI; curlDeg[i] = 0f; addedDeg[i] = 0f; }
            }
            tip = posed[n - 1] - posedRot[n - 1] * Vector3.forward * tipLength;
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            if (!stepped) return;   // the rest pose (the bind): the plain chain
            Pose();
            var sc = new Vector3(L, L, L);
            for (int i = root + 1; i < n; i++) dst[offset + bone0 + i] = Matrix4x4.TRS(posed[i], posedRot[i], sc);
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            if (!stepped || NoBoundsForTest) return;
            Pose();
            for (int i = root + 1; i < n; i++) { lo = Vector3.Min(lo, posed[i]); hi = Vector3.Max(hi, posed[i]); }
            lo = Vector3.Min(lo, tip); hi = Vector3.Max(hi, tip);
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            if (!stepped) return;
            var c = new Color(0.12f, 0.75f, 0.7f);
            for (int i = root + 1; i < n; i++) d.Line(posed[i - 1], posed[i], c);
            d.Line(posed[n - 1], tip, c);
        }
    }
}
