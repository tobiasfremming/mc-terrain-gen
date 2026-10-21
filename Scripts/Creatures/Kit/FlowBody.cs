// Creature kit (Apex redesign §5.1): FlowBody - one chain from the head to the tail tip, drawn as one continuous
// surface whose cross-section is shaped, not an ellipse. A Body part like SpineBody (ISpineChainBody: the sim runs its
// joints exactly as a SpineBody's - joints from profile keys by the same monotone cubic, spacing, clearances, bend
// limits, a rigid head auto from the profile), but it draws its own mesh (RayBody is the precedent):
//   section   a superellipse of (halfWidth, halfHeight) whose exponent runs from 2 (the trunk's oval) to bladeExponent
//             as `blade` goes 0 -> 1, plus a sharp dorsal hump (hump x max(0, sin phi)^humpSharpness), a keel below
//             (keel x max(0, -sin phi)^keelSharpness; keelForward moves that term's points toward the head, level in the
//             bind, by keelForward x the same weight - the sternum jutting forward) and flank masses (flank x cos^2 phi, sideways). An oval is at most
//             ovalMaxAspect times as tall as it is wide; `blade` stretches the height to the key's full halfHeight, so
//             a ribbon tail (height / width >= 3) needs blade 1 and an oval trunk keeps its authored height.
//             The SpineJoint the sim gets carries the key's halfWidth / halfHeight only: it is NOT the drawn section
//             (no hump, keel, flank, oval cap or tail taper). A part placed on the body's surface queries Section.
//   surface   rings from a small pole headCapLength ahead of joint 0 (the head part covers it: FrontRingU) to a fine
//             pole tailTipLength behind the last joint, ringsPerSegment rings per segment, in SpineBody's ring sequence
//             (SpineBody.RingSequence: the same skinning between the two nearest joints, the same tail tip profile);
//             normals from the surface by finite differences.
//   rest      restAtStance (on by default): the bind lays the joints at their clearances with each segment `spacing`
//             long (CreatureSpecies.restAtSpacing), i.e. as steep as the walking sim holds them. Without it the rest
//             puts the joints one spacing apart along the ground, so a steeply raised neck is bound much flatter than it
//             stands, and the inside of the neck-root bend - the back, under the hump - folds in the standing pose.
//   colour    named kinds: `tailfan` behind tailColourFrom (the teal, striped tail), `dorsalstripe` - a narrow band
//             along the top from the head back to dorsalStripeTo. Both off by default; a kind (and its material slot) is
//             wanted only while its colour is on, and tailKind / dorsalStripeKind can name a kind the species already
//             draws (one slot for both: a species short of kit slots).
// Kit-only, no runtime of its own (the sim and the locomotion modes move the joints). Mesh only; allocation at build.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // A FlowBody profile key: the chain's joint data (as a SpineBody ProfileKey) plus the section's shape channels.
    [Serializable]
    public struct FlowKey
    {
        [Param(ParamUnit.Fraction, "place along the spine, 0 = joint 0 (head) .. 1 = last joint", Min = 0, Max = 1)] public float t;
        [Param(ParamUnit.Metres, "section half width")] public float halfWidth;
        [Param(ParamUnit.Metres, "section half height (an oval is at most 2x as tall as wide; blade stretches it to this)")] public float halfHeight;
        [Param(ParamUnit.Metres, "joint centre above the ground")] public float clearance;
        [Param(ParamUnit.Degrees, "cone half-angle vs the previous segment")] public float bendLimitDeg;
        [Param(ParamUnit.Metres, "dorsal hump: a sharp ridge rising this far above the section's top")] public float hump;
        [Param(ParamUnit.Metres, "keel: the sternum jutting this far below the section's bottom")] public float keel;
        [Param(ParamUnit.Metres, "flank masses: this much extra width at the sides")] public float flank;
        [Param(ParamUnit.Fraction, "0 = the oval .. 1 = the full ribbon (superellipse bladeExponent, the full halfHeight)", Min = 0, Max = 1)] public float blade;
        [Param(ParamUnit.Metres, "keel forward: the keel's lowest point moved this far toward the head, level in the bind (the sternum jutting forward and down; the keel term's share of it, 0 at the flanks); 0 = straight down", Min = 0, Default = "0")] public float keelForward;
        [Param(ParamUnit.Metres, "per-joint spacing: the segment length here (interpolated at each segment's midpoint; varies smoothly, neighbours <= 1.25x apart); 0 = the body's spacing. Every key 0 = one spacing everywhere", Min = 0, Default = "0 (the body's)")] public float spacing;
    }

    [Serializable]
    [Part("FlowBody", PartKind.Body, Bones = "= jointCount", Summary = "One chain from the head to the tail tip (the sim runs it like a SpineBody's) drawn as one flowing surface with a shaped section: superellipse from oval to tall ribbon (blade), a sharp dorsal hump, a keel, flank masses. The neck and the tail are the same chain as the trunk; a head part sits on joint 0.",
          System = "CreatureSim chain (ISpineChainBody) + FlowBody.EmitMesh", MaterialLayers = new[] { 0 }, KitOnly = true,
          UseWhen = "a body whose neck, trunk and tail flow into each other with a section an ellipse cannot draw: humped backs, keeled chests, ribbon or paddle tails")]
    public sealed class FlowBody : CreaturePart, ISpineChainBody, IMaterialKinds
    {
        [Param(ParamUnit.Metres, "joint spacing (fine in the neck for a smooth S)", Example = "fixture 0.1")] public float spacing = 0.1f;
        [Param(ParamUnit.Metres, "the surface closes with a small pole this far ahead of joint 0 (inside the head part)")] public float headCapLength = 0.05f;
        [Param(ParamUnit.Metres, "fine tail tip behind the last joint")] public float tailTipLength = 0.1f;
        [Param(ParamUnit.Count, "joints, head to tail", Min = 3, Max = 128, Example = "fixture 24")] public int jointCount = 24;
        [Param(ParamUnit.Struct, "profile keys (>= 2, t = 0 and t = 1 included, increasing t): monotone cubic to every joint and ring")] public FlowKey[] profile = new FlowKey[0];
        [Param(ParamUnit.Count, "rigid head: the first N joints move as one rigid body; -1 = auto from the profile (FlowBody.AutoHead: the joints before the neck narrows, if it narrows before the body widens), 0 = off", Min = -1, Max = 16, Default = "-1 (auto)")] public int rigidHeadJoints = -1;
        [Param(ParamUnit.Fraction, "hump exponent on max(0, sin phi): higher = a sharper ridge", Min = 1, Max = 64, Default = "8")] public float humpSharpness = 8f;
        [Param(ParamUnit.Fraction, "keel exponent on max(0, -sin phi): higher = a narrower keel", Min = 1, Max = 64, Default = "6")] public float keelSharpness = 6f;
        [Param(ParamUnit.Fraction, "superellipse exponent at blade 1 (2 = ellipse; 4 = a rounded ribbon)", Min = 2, Max = 12, Default = "4")] public float bladeExponent = 4f;
        [Param(ParamUnit.Count, "mesh: vertices around each ring", Min = 8, Max = 64, Default = "24")] public int ringSegments = 24;
        [Param(ParamUnit.Count, "mesh: rings per spine segment", Min = 1, Max = 8, Default = "3")] public int ringsPerSegment = 3;
        [Param(ParamUnit.Fraction, "body fraction (0 front tip .. 1 tail tip) from which the surface is tailKind (default `tailfan`); >= 1 = off", Min = 0, Max = 1, Default = "1 (off)")] public float tailColourFrom = 1f;
        [Param(ParamUnit.Fraction, "body fraction up to which a dorsalStripeKind band (default `dorsalstripe`) runs along the top from the head; 0 = off", Min = 0, Max = 1, Default = "0 (off)")] public float dorsalStripeTo = 0f;
        [Param(ParamUnit.Degrees, "dorsal stripe width around the section (centred on the top)", Min = 0, Max = 180, Default = "30")] public float dorsalStripeWidthDeg = 30f;
        [Param(ParamUnit.Text, "the material kind of the tail behind tailColourFrom: tailfan (a kit slot of its own, styled by a species Layers row naming it), or a kind the species already draws (a FleshWing's wingfan: the tail in the wing's colour, one slot for both)", Default = "tailfan")] public string tailKind = "tailfan";
        [Param(ParamUnit.Text, "the material kind of the dorsal stripe: dorsalstripe (a kit slot of its own), or a kind the species already draws (one slot for both)", Default = "dorsalstripe")] public string dorsalStripeKind = "dorsalstripe";
        [Param(ParamUnit.Fraction, "an oval (blade 0) is at most this many times as tall as wide; blade 1 stretches it to the key's full halfHeight", Min = 1, Max = 8, Default = "2")] public float ovalMaxAspect = 2f;
        [Param(ParamUnit.Flag, "bind the body as it stands: each rest segment spacing long between the joints' clearances (a raised neck bound as steep as it walks); off = joints one spacing apart along the ground (SpineBody's rest)", Default = "on")] public bool restAtStance = true;

        // RED test hook (CreatureFlowBodyTests.Red_Normals_Flipped): the ring normals turned inward.
        internal static bool FlipNormalsForTest;

        public int JointCount => jointCount;
        public float Spacing => spacing;
        public float HeadCapLength => headCapLength;
        public float TailTipLength => tailTipLength;
        public int RigidHeadJoints => rigidHeadJoints >= 0 ? Mathf.Min(rigidHeadJoints, Mathf.Max(0, jointCount - 1)) : AutoHead(BuildJoints());

        // The auto rigid head: SpineBody.AutoHead's rule (the joints before the first one >= 5 % narrower than its
        // predecessor, in the front half; a head of one joint: 0), but the search stops where the body first widens by
        // >= 5 %: a neck that swells into a chest has no head of its own joints in front of it, and the chest narrowing
        // into a ribbon tail (still in the front half) is not a neck - SpineBody's rule made all of joints 0..10 one
        // rigid block there, and every bend of the walk then fell on the chest-tail joint (a sliver of the body folded,
        // KitChecks.MaxBendProblem on CreatureCrestBeakHeadTests' fixture).
        public static int AutoHead(SpineJoint[] js)
        {
            const float kStep = 0.05f;
            int n = js != null ? js.Length : 0;
            for (int i = 1; i <= n / 2; i++)
            {
                if (js[i].halfWidth > (1f + kStep) * js[i - 1].halfWidth) return 0;
                if (js[i].halfWidth < (1f - kStep) * js[i - 1].halfWidth) return i >= 2 ? i : 0;
            }
            return 0;
        }

        float HeadLen => Mathf.Max(headCapLength, 1e-3f);
        float TailLen => Mathf.Max(tailTipLength, 1e-3f);
        float SpineLen
        {
            get
            {
                float[] s = Segs();
                if (s == null) return spacing * (jointCount - 1);
                float a = 0f;
                for (int i = 0; i < s.Length; i++) a += s[i];
                return a;
            }
        }
        public float BodyLength => HeadLen + SpineLen + TailLen;
        // The body fraction where the full rings start (joint 0): ahead of it the surface only closes into a small pole,
        // inside the head part sitting on joint 0.
        public float FrontRingU => HeadLen / BodyLength;
        // The body fraction of joint k (0 head joint .. jointCount - 1; fractional: along its segment).
        public float UAtJoint(float k)
        {
            float[] s = Segs();
            return s == null ? (HeadLen + spacing * k) / BodyLength : (HeadLen + ArcAt(s, k)) / BodyLength;
        }

        // ------------------------------------------------------------ per-joint spacing (the profile's `spacing` channel)

        // True when a profile key sets its own spacing (> 0): the segments then vary (SegmentLengths).
        public bool HasSpacingChannel
        {
            get
            {
                if (profile != null) for (int i = 0; i < profile.Length; i++) if (profile[i].spacing != 0f) return true;
                return false;
            }
        }

        // The segment lengths (scale 1; segment i = joint i to i + 1): the `spacing` channel (a key's 0 = the body's
        // spacing) by the profile's monotone cubic at each segment's midpoint. null without the channel (one spacing:
        // CreatureSpecies.segmentLengths stays empty). A fresh array (CompiledSpecies keeps it).
        public float[] SegmentLengths()
        {
            if (!HasSpacingChannel) return null;
            int n = Mathf.Max(jointCount, 2);
            var r = new float[n - 1];
            var keys = new Keys(profile, ChSpacing, spacing);
            for (int i = 0; i < r.Length; i++) r[i] = MonotoneCubic.Eval(keys, (i + 0.5f) / (n - 1f));
            return r;
        }

        // SegmentLengths for this body's own readers (the arc <-> joint mapping, the mesh): kept while the data it comes
        // from (joint count, spacing, each key's t and spacing) is unchanged; never handed out (nothing writes it).
        [NonSerialized] float[] segCache, segCacheOf;
        float[] Segs()
        {
            if (!HasSpacingChannel) return null;
            int m = 2 + 2 * profile.Length;
            bool same = segCache != null && segCacheOf != null && segCacheOf.Length == m && segCacheOf[0] == jointCount && segCacheOf[1] == spacing;
            for (int i = 0; same && i < profile.Length; i++) same = segCacheOf[2 + 2 * i] == profile[i].t && segCacheOf[3 + 2 * i] == profile[i].spacing;
            if (same) return segCache;
            var of = new float[m];
            of[0] = jointCount; of[1] = spacing;
            for (int i = 0; i < profile.Length; i++) { of[2 + 2 * i] = profile[i].t; of[3 + 2 * i] = profile[i].spacing; }
            segCache = SegmentLengths(); segCacheOf = of;
            return segCache;
        }

        // The arc from joint 0 to (fractional) joint k over segment lengths s.
        static float ArcAt(float[] s, float k)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(k), 0, s.Length - 1);
            float a = 0f;
            for (int j = 0; j < i; j++) a += s[j];
            return a + (k - i) * s[i];
        }

        // The (fractional) joint at arc a from joint 0 over segment lengths s (ArcAt's inverse; past the ends: carried on
        // along the end segments' lengths).
        static float JointAt(float[] s, float a)
        {
            int i = 0;
            while (i < s.Length - 1 && a >= s[i]) { a -= s[i]; i++; }
            return i + a / s[i];
        }

        public override int BoneCount(in SpeciesCtx c) => jointCount;
        public override int MeshOrder => 100;
        protected override void CopyArrays() { profile = (FlowKey[])profile?.Clone(); }

        // IMaterialKinds: the kinds this body draws (data) - a slot only for a colour it uses, so a species short of kit
        // slots can draw the tail and the stripe in a kind it already has (tailKind / dorsalStripeKind).
        public void Kinds(List<string> into)
        {
            if (tailColourFrom < 1f && !string.IsNullOrEmpty(tailKind)) into.Add(tailKind);
            if (dorsalStripeTo > 0f && !string.IsNullOrEmpty(dorsalStripeKind)) into.Add(dorsalStripeKind);
        }

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a FlowBody is a spine body, not with a SoftBody";
            if (jointCount < 3 || jointCount > 128) return $"{id}: jointCount must be 3..128";
            if (!(spacing > 0f)) return $"{id}: spacing must be > 0";
            if (rigidHeadJoints < -1 || rigidHeadJoints > jointCount - 1) return $"{id}: rigidHeadJoints {rigidHeadJoints} out of range -1..{jointCount - 1}";
            if (profile == null || profile.Length < 2) return $"{id}: profile needs >= 2 keys";
            for (int i = 1; i < profile.Length; i++) if (!(profile[i].t > profile[i - 1].t)) return $"{id}: profile keys must be in increasing t";
            if (profile[0].t != 0f || profile[profile.Length - 1].t != 1f) return $"{id}: profile must start at t = 0 and end at t = 1";
            for (int i = 0; i < profile.Length; i++)
            {
                FlowKey k = profile[i];
                if (!(k.halfWidth > 0f) || !(k.halfHeight > 0f)) return $"{id}: profile[{i}] halfWidth and halfHeight must be > 0";
                if (k.hump < 0f || k.keel < 0f || k.flank < 0f) return $"{id}: profile[{i}] hump, keel and flank must be >= 0";
                if (k.blade < 0f || k.blade > 1f) return $"{id}: profile[{i}] blade must be 0..1";
                if (k.keelForward < 0f || k.keelForward > spacing) return $"{id}: profile[{i}] keelForward must be 0..spacing ({spacing})";
            }
            if (humpSharpness < 1f || keelSharpness < 1f) return $"{id}: humpSharpness and keelSharpness must be >= 1";
            if (bladeExponent < 2f || bladeExponent > 12f) return $"{id}: bladeExponent must be 2..12";
            if (ringSegments < 8 || ringSegments > 64) return $"{id}: ringSegments must be 8..64";
            if (ringsPerSegment < 1 || ringsPerSegment > 8) return $"{id}: ringsPerSegment must be 1..8";
            if (ovalMaxAspect < 1f || ovalMaxAspect > 8f) return $"{id}: ovalMaxAspect must be 1..8";
            if (tailColourFrom < 1f && string.IsNullOrEmpty(tailKind)) return $"{id}: tailKind is empty (tailColourFrom < 1 draws the tail in it)";
            if (dorsalStripeTo > 0f && string.IsNullOrEmpty(dorsalStripeKind)) return $"{id}: dorsalStripeKind is empty (dorsalStripeTo > 0 draws the stripe in it)";
            if (restAtStance)
            {
                // CreatureSim.RestRun lays each rest segment `spacing` long between the clearances, its run never shorter
                // than RestRunFloor x spacing: a segment rising steeper than asin(sqrt(1 - floor^2)) (75.5 deg) is
                // stretched in the bind and pops to its length on the first walking tick. Reported once the stretch passes
                // MaxRestStretch (1 %: ~78.1 deg) - the fixtures' steep neck stands at 76 deg, stretched 0.03 %
                // (per-joint spacing: each segment against its own length)
                SpineJoint[] js = BuildJoints();
                float[] segs = Segs();
                float limit = Mathf.Sqrt(MaxRestStretch * MaxRestStretch - RestRunFloor * RestRunFloor) * spacing;
                for (int i = 1; i < js.Length; i++)
                {
                    float L = segs == null ? spacing : segs[i - 1];
                    float lim = segs == null ? limit : Mathf.Sqrt(MaxRestStretch * MaxRestStretch - RestRunFloor * RestRunFloor) * L;
                    float rise = Mathf.Abs(js[i].clearance - js[i - 1].clearance);
                    if (rise > lim) return $"{id}: restAtStance: stance segment {i - 1}-{i} rises {Mathf.Asin(Mathf.Min(1f, rise / L)) * Mathf.Rad2Deg:F0} deg ({rise:F3} m over a {L:F3} m segment; > {MaxStanceSegmentDeg:F1} deg the rest run's floor stretches it > 1 %)";
                }
            }
            return HasSpacingChannel ? ValidateSpacing(c) : null;
        }

        // Per-joint spacing: neighbouring segments at most MaxSpacingStep x apart (no visible kink where the spacing
        // changes), and composed only with parts and modules that honour it. The rest read `spacing` as every segment's
        // length (a mesh part laid out at joint x spacing, the climb, a twisting air chain, a soaring glide) and would
        // silently misdraw or misplace: refused here, naming the part.
        internal const float MaxSpacingStep = 1.25f;
        static readonly HashSet<string> kHonoursSpacing = new HashSet<string> { "FlowBody", "Leg2Bone", "DrakeLeg", "DigitWing", "Eyes", "AttachedChain" };
        static readonly HashSet<string> kModuleRefusesSpacing = new HashSet<string> { "Climb", "ClimbExcursion", "Soar" };

        string ValidateSpacing(in SpeciesCtx c)
        {
            for (int i = 0; i < profile.Length; i++) if (profile[i].spacing < 0f) return $"{id}: profile[{i}] spacing must be >= 0 (0 = the body's spacing)";
            float[] s = Segs();
            for (int i = 0; i < s.Length; i++)
            {
                if (!(s[i] > 0f)) return $"{id}: per-joint spacing: segment {i} is {s[i]} m (must be > 0)";
                if (i > 0 && Mathf.Max(s[i], s[i - 1]) > MaxSpacingStep * Mathf.Min(s[i], s[i - 1]))
                    return $"{id}: per-joint spacing jumps between segments {i - 1} and {i} ({s[i - 1]:F4} -> {s[i]:F4} m; neighbours at most {MaxSpacingStep}x apart): spread the change over more joints";
            }
            for (int i = 0; i < c.Parts.Count; i++)
            {
                CreaturePart p = c.Parts[i];
                if (p != null && !kHonoursSpacing.Contains(p.Meta.Name))
                    return $"{p.id}: a {p.Meta.Name} supports only a uniformly spaced spine; {id} has per-joint spacing (its profile's spacing channel) - leave the channel at 0 or drop {p.id}";
            }
            for (int i = 0; i < c.Modules.Count; i++)
            {
                CreatureModule m = c.Modules[i];
                if (m == null) continue;
                if (kModuleRefusesSpacing.Contains(m.Meta.Name))
                    return $"module {m.Meta.Name} supports only a uniformly spaced spine; {id} has per-joint spacing (its profile's spacing channel)";
                if (m is Fly f && f.twist.Enabled)
                    return $"module Fly: the twisting air chain (twist) supports only a uniformly spaced spine; {id} has per-joint spacing (its profile's spacing channel)";
            }
            return null;
        }

        // CreatureSim.RestRun's floor (the least rest run, x spacing), the stretch Validate allows, and the steepest stance
        // segment bound within it.
        internal const float RestRunFloor = 0.25f, MaxRestStretch = 1.01f;
        internal static float MaxStanceSegmentDeg => Mathf.Asin(Mathf.Min(1f, Mathf.Sqrt(MaxRestStretch * MaxRestStretch - RestRunFloor * RestRunFloor))) * Mathf.Rad2Deg;

        // ------------------------------------------------------------ the chain

        public const int ChHalfWidth = 0, ChHalfHeight = 1, ChClearance = 2, ChBend = 3, ChHump = 4, ChKeel = 5, ChFlank = 6, ChBlade = 7, ChKeelForward = 8, ChSpacing = 9;

        readonly struct Keys : MonotoneCubic.IKeys
        {
            readonly FlowKey[] k; readonly int ch;
            readonly float bodySpacing;   // ChSpacing: a key's 0 reads this
            public Keys(FlowKey[] k, int ch, float bodySpacing = 0f) { this.k = k; this.ch = ch; this.bodySpacing = bodySpacing; }
            public int Count => k.Length;
            public float X(int i) => k[i].t;
            public float Y(int i)
            {
                switch (ch)
                {
                    case ChHalfWidth: return k[i].halfWidth;
                    case ChHalfHeight: return k[i].halfHeight;
                    case ChClearance: return k[i].clearance;
                    case ChBend: return k[i].bendLimitDeg;
                    case ChHump: return k[i].hump;
                    case ChKeel: return k[i].keel;
                    case ChFlank: return k[i].flank;
                    case ChKeelForward: return k[i].keelForward;
                    case ChSpacing: return k[i].spacing > 0f ? k[i].spacing : bodySpacing;
                    default: return k[i].blade;
                }
            }
        }

        // A profile channel at spine place t (0 joint 0 .. 1 last joint): SpineBody's monotone cubic (MonotoneCubic).
        public float Profile(float t, int ch) => MonotoneCubic.Eval(new Keys(profile, ch), t);

        // The sim's joints, from the profile exactly as SpineBody builds them (no rigid neck joints: rigidHeadJoints).
        public SpineJoint[] BuildJoints()
        {
            int n = Mathf.Max(jointCount, 2);
            var r = new SpineJoint[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                r[i] = new SpineJoint { halfWidth = Profile(t, ChHalfWidth), halfHeight = Profile(t, ChHalfHeight), clearance = Profile(t, ChClearance), bendLimitDeg = Profile(t, ChBend) };
            }
            return r;
        }

        // ------------------------------------------------------------ the section

        // The section at body fraction u (0 front tip .. 1 tail tip) and angle phi (-pi/2 belly, 0 right, +pi/2 back):
        // `offset` is the surface point in the joint's section plane (x right, y up; metres at scale 1), the return its
        // distance from the centre line. Ahead of joint 0 the section closes elliptically into the front pole; behind
        // the last joint it eases from the body's taper into the fine tail tip.
        public float Section(float u, float phi, out Vector2 offset)
        {
            float s = Mathf.Clamp01(u) * BodyLength, scale = 1f;
            float lh = HeadLen, tailStart = lh + SpineLen;
            if (s < lh) { float q = (lh - s) / lh; scale = Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
            else if (s > tailStart) scale = SpineBody.TailTipProfile(Mathf.Min(1f, (s - tailStart) / TailLen), TailSlope());
            float t = SpineLen > 0f ? Mathf.Clamp01((s - lh) / SpineLen) : 0f;
            offset = scale * Shape(t, phi);
            return offset.magnitude;
        }

        // keelForward's direction at a ring: the body's forward levelled in the bind (the rest stands on level ground, up
        // +y) - a keel jutting forward under a chest pitched up by its raised neck moves forward, not up the neck.
        static Vector3 Level(Vector3 t)
        {
            var l = new Vector3(t.x, 0f, t.z);
            return l.sqrMagnitude > 1e-8f ? l.normalized : t;
        }

        // The section point with its keel's forward shift (z: metres toward the head along the levelled forward, Level;
        // keelForward - 0 without it).
        public Vector3 Section3(float u, float phi)
        {
            Section(u, phi, out Vector2 o);
            float fwd = 0f;
            if (hasKeelForward)
            {
                // the section's own scale (the front pole and the tail tip shrink it) and spine place, as Section
                float s = Mathf.Clamp01(u) * BodyLength, scale = 1f;
                float lh = HeadLen, tailStart = lh + SpineLen;
                if (s < lh) { float q = (lh - s) / lh; scale = Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
                else if (s > tailStart) scale = SpineBody.TailTipProfile(Mathf.Min(1f, (s - tailStart) / TailLen), TailSlope());
                float t = SpineLen > 0f ? Mathf.Clamp01((s - lh) / SpineLen) : 0f;
                fwd = scale * ShapeParams(t).keelFwd * Mathf.Pow(Mathf.Max(0f, -Mathf.Sin(phi)), keelSharpness);
            }
            return new Vector3(o.x, o.y, fwd);
        }

        bool hasKeelForward
        {
            get
            {
                if (profile != null) for (int i = 0; i < profile.Length; i++) if (profile[i].keelForward != 0f) return true;
                return false;
            }
        }

        // The section's profile values at spine place t (six monotone cubics - most of a mesh build's cost: a ring asks
        // for the same three t for every vertex, so the last few are memoised; the values are exactly the same).
        struct ShapeAt { public float t, a, b, e, hump, keel, flank, keelFwd; }
        [NonSerialized] readonly ShapeAt[] shapeMemo = new ShapeAt[4];
        int shapeMemoCount, shapeMemoNext;

        ShapeAt ShapeParams(float t)
        {
            if (memoOn) for (int i = 0; i < shapeMemoCount; i++) if (shapeMemo[i].t == t) return shapeMemo[i];
            float a = Mathf.Max(PartMeshEmitter.MinRadius, Profile(t, ChHalfWidth));
            float hh = Mathf.Max(PartMeshEmitter.MinRadius, Profile(t, ChHalfHeight));
            float blade = Mathf.Clamp01(Profile(t, ChBlade));
            float n = Mathf.Lerp(2f, bladeExponent, blade);
            var r = new ShapeAt
            {
                t = t, a = a, b = Mathf.Lerp(Mathf.Min(hh, ovalMaxAspect * a), hh, blade), e = 2f / n,
                hump = Mathf.Max(0f, Profile(t, ChHump)), keel = Mathf.Max(0f, Profile(t, ChKeel)), flank = Mathf.Max(0f, Profile(t, ChFlank)),
                keelFwd = hasKeelForward ? Mathf.Max(0f, Profile(t, ChKeelForward)) : 0f,
            };
            if (memoOn) { shapeMemo[shapeMemoNext] = r; shapeMemoNext = (shapeMemoNext + 1) & 3; shapeMemoCount = Mathf.Min(4, shapeMemoCount + 1); }
            return r;
        }

        // The memos live only while this body emits its rings (cleared at the start; a clone shares the arrays, and other
        // callers - tests editing the data, parts asking for the surface - always compute afresh).
        [NonSerialized] bool memoOn;
        void ClearShapeMemo() { shapeMemoCount = 0; shapeMemoNext = 0; }

        // The unscaled section at spine place t.
        Vector2 Shape(float t, float phi)
        {
            ShapeAt q = ShapeParams(t);
            float a = q.a, b = q.b, e = q.e;
            float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
            float x = a * Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), e);
            float y = b * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), e);
            y += q.hump * Mathf.Pow(Mathf.Max(0f, sn), humpSharpness);
            y -= q.keel * Mathf.Pow(Mathf.Max(0f, -sn), keelSharpness);
            x += Mathf.Sign(cs) * q.flank * cs * cs;
            return new Vector2(x, y);
        }

        // The body's taper at the last joint in tail-profile units d(scale)/d(k), k = distance / tailTipLength (half the
        // last segment's change of the mean half-axis, as SpineBody's TaperSlope), clamped to keep the tip monotone.
        float TailSlope()
        {
            int n = jointCount;
            float tPrev = (n - 2f) / (n - 1f);
            float end = Profile(1f, ChHalfWidth) + Profile(1f, ChHalfHeight), prev = Profile(tPrev, ChHalfWidth) + Profile(tPrev, ChHalfHeight);
            float[] segs = Segs();
            float perMetre = 0.5f * (end - prev) / (segs == null ? spacing : segs[segs.Length - 1]);
            return Mathf.Clamp(perMetre * TailLen / Mathf.Max(end, PartMeshEmitter.MinRadius), SpineBody.kTailTipSlope, 0f);
        }

        // ------------------------------------------------------------ the mesh

        // Rings from the front pole to the tail pole, ringSegments + 1 vertices each (seam duplicated), in SpineBody's
        // ring sequence (SpineBody.RingSequence) with this body's sections.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            // the rings only depend on this body's data and the rest pose: built once, then replayed (RestMeshCache)
            string[] kinds = tailColourFrom < 1f && dorsalStripeTo > 0f ? new[] { tailKind, dorsalStripeKind }
                           : tailColourFrom < 1f ? new[] { tailKind } : dorsalStripeTo > 0f ? new[] { dorsalStripeKind } : new string[0];
            ulong key = CacheKey(m);
            if (RestMeshCache.TryReplay(m, key, kinds)) return;
            int firstVertex = m.VertexCount, firstTri = m.Lists.tris.Count;
            ClearShapeMemo();
            frameMemoCount = 0; frameMemoNext = 0; frameMemoOf = m;
            memoOn = true;
            try
            {
            var rings = new Rings
            {
                b = this, m = m,
                kindTail = tailColourFrom < 1f ? m.Slot(tailKind) : -1,
                kindStripe = dorsalStripeTo > 0f ? m.Slot(dorsalStripeKind) : -1,
            };
            SpineBody.RingSequence(m, rings, jointCount, HeadLen, spacing, SpineLen, TailLen, Mathf.Max(1, ringsPerSegment), ringSegments, Segs());
            }
            finally { memoOn = false; frameMemoOf = null; }
            RestMeshCache.Store(m, key, firstVertex, firstTri, kinds);
        }

        ulong CacheKey(PartMeshEmitter m)
        {
            // every serialized field of this body (RestMeshCache.MixData: any edit rebuilds), the test hook, the rest pose
            ulong h = RestMeshCache.MixData(RestMeshCache.Mix(RestMeshCache.Seed, 0xF10B), this);   // FlowBody
            h = RestMeshCache.Mix(h, FlipNormalsForTest);
            return RestMeshCache.MixRest(h, m);
        }

        struct Rings : SpineBody.IBodyRings
        {
            public FlowBody b; public PartMeshEmitter m; public int kindTail, kindStripe;

            public int FrontPole(int segs)
            {
                Frame tip = b.FrameAt(m, 0f);
                return m.Pole(tip.c, tip.t, 0f, PartMeshEmitter.PartBody, 0f, PartMeshEmitter.Rigid(0), segs);
            }

            public int Ring(float s, Vector4 skin, int segs) => b.EmitRing(m, s, skin, segs, kindTail, kindStripe);

            public int TailPole(int segs)
            {
                float L = b.BodyLength;
                Frame end = b.FrameAt(m, L);
                return m.Pole(end.c, -end.t, L, kindTail >= 0 ? kindTail : PartMeshEmitter.PartBody, 0f, PartMeshEmitter.Rigid(b.jointCount - 1), segs);
            }
        }

        struct Frame { public Vector3 c, t, x, y; }

        // The rest centre line and section frame at arc s (metres from the front tip): the rest spine (SpineAt) between
        // joint 0 and the last joint, carried straight on along its end tangents over the head cap and the tail tip.
        [NonSerialized] readonly float[] frameMemoS = new float[4];
        [NonSerialized] readonly Frame[] frameMemo = new Frame[4];
        int frameMemoCount, frameMemoNext;
        [NonSerialized] PartMeshEmitter frameMemoOf;

        // FrameAt, memoised for the last few arcs of one emitter's rest pose (a ring asks for s, s - h and s + h for every
        // vertex; the frames are exactly the same).
        Frame FrameAt(PartMeshEmitter m, float s)
        {
            if (!memoOn || !ReferenceEquals(m, frameMemoOf)) return FrameAtUncached(m, s);
            for (int i = 0; i < frameMemoCount; i++) if (frameMemoS[i] == s) return frameMemo[i];
            Frame f = FrameAtUncached(m, s);
            frameMemoS[frameMemoNext] = s; frameMemo[frameMemoNext] = f;
            frameMemoNext = (frameMemoNext + 1) & 3; frameMemoCount = Mathf.Min(4, frameMemoCount + 1);
            return f;
        }

        Frame FrameAtUncached(PartMeshEmitter m, float s)
        {
            float lh = HeadLen, tailStart = lh + SpineLen;
            SpineSection q;
            Vector3 shift = Vector3.zero;
            if (s < lh) { q = m.SpineAt(0f); shift = q.t * (lh - s); }
            else if (s > tailStart) { q = m.SpineAt(jointCount - 1); shift = -q.t * (s - tailStart); }
            else { float[] segs = Segs(); q = m.SpineAt(segs == null ? (s - lh) / spacing : JointAt(segs, s - lh)); }
            return new Frame { c = q.c + shift, t = q.t, x = q.x, y = q.y };
        }

        Vector3 Point(PartMeshEmitter m, float s, float phi)
        {
            Frame f = FrameAt(m, s);
            Vector3 o = Section3(s / BodyLength, phi);
            Vector3 p = f.c + f.x * o.x + f.y * o.y;
            return o.z != 0f ? p + Level(f.t) * o.z : p;
        }

        // One vertex of the drawn surface at arc s, section angle phi, in the section frame f: the point, its outward
        // normal (finite differences around and along the body) and the unit direction along the body toward the tail.
        // EmitRing's per-vertex surface, shared with the parts riding on it (SurfaceAt).
        Vector3 SurfaceVertex(PartMeshEmitter m, in Frame f, float s, float phi, out Vector3 normal, out Vector3 toTail)
        {
            float L = BodyLength, u = s / L, h = PartMeshEmitter.DiffStep;
            float s0 = Mathf.Max(0f, s - h), s1 = Mathf.Min(L, s + h);
            Vector3 o = Section3(u, phi);
            Vector3 p = f.c + f.x * o.x + f.y * o.y;
            if (o.z != 0f) p += Level(f.t) * o.z;
            // the surface's tangents: around the ring (in the section plane; keelForward: and along it) and along the body
            // (toward the tail)
            Vector3 d = Section3(u, phi + h) - Section3(u, phi - h);
            Vector3 around = f.x * d.x + f.y * d.y;
            if (d.z != 0f) around += Level(f.t) * d.z;
            Vector3 along = Point(m, s1, phi) - Point(m, s0, phi);
            // outward: the ring runs counter-clockwise in (x, y), so cross(around, toward the head) points out
            normal = PartMeshEmitter.Unit(Vector3.Cross(around, -along), f.x * Mathf.Cos(phi) + f.y * Mathf.Sin(phi));
            toTail = PartMeshEmitter.Unit(along, -f.t);
            return p;
        }

        // The drawn surface at body fraction u and section angle phi in the rest pose (a part riding on it: PitRow).
        internal Vector3 SurfaceAt(PartMeshEmitter m, float u, float phi, out Vector3 normal, out Vector3 toTail)
        {
            float s = Mathf.Clamp01(u) * BodyLength;
            return SurfaceVertex(m, FrameAt(m, s), s, phi, out normal, out toTail);
        }

        // The skin the body's rings have at arc s (SpineBody.RingSequence): rigid to joint 0 over the head cap, the
        // segment blend (SpineBody.SegmentSkin) between the joints, rigid to the last joint over the tail tip.
        internal Vector4 SkinAt(float s)
        {
            float lh = HeadLen, tailStart = lh + SpineLen;
            if (s <= lh) return PartMeshEmitter.Rigid(0);
            if (s >= tailStart) return PartMeshEmitter.Rigid(jointCount - 1);
            float[] segs = Segs();
            float k = segs == null ? (s - lh) / spacing : JointAt(segs, s - lh);
            int i = Mathf.Min((int)k, jointCount - 2);
            return SpineBody.SegmentSkin(i, k - i);
        }

        int EmitRing(PartMeshEmitter m, float s, Vector4 skin, int segs, int kindTail, int kindStripe)
        {
            float u = s / BodyLength;
            Frame f = FrameAt(m, s);
            float stripeHalf = 0.5f * dorsalStripeWidthDeg * Mathf.Deg2Rad;
            int first = m.VertexCount;
            for (int k = 0; k <= segs; k++)
            {
                int kk = k == segs ? 0 : k;   // exact seam duplicate
                float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * kk / segs;
                Vector3 p = SurfaceVertex(m, f, s, phi, out Vector3 nn, out _);
                if (FlipNormalsForTest) nn = -nn;
                float sn = Mathf.Sin(phi);
                int part = PartMeshEmitter.PartBody;
                if (kindTail >= 0 && u >= tailColourFrom) part = kindTail;
                else if (kindStripe >= 0 && u <= dorsalStripeTo && Mathf.Abs(phi - 0.5f * Mathf.PI) <= stripeHalf) part = kindStripe;
                m.Vertex(p, nn, s, (float)k / segs, part, sn, skin);
            }
            return first;
        }
    }
}
