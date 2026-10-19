// Creature kit: Jaw - a hinged jaw on a bone of its own: a lofted solid (RigidLoft's superellipse sections) with a
// row of teeth along each lip, hinged about the joint frame's x axis at at.offset, that opens and closes on its own.
// The idle gape: once the body has stood still for restSeconds it may open the jaw (openSeconds, eased), hold it
// open (holdSeconds, jittered), close it (closeSeconds) and wait about intervalSeconds before the next gape; the
// moment the body walks on (faster than releaseSpeed of its walking speed) the jaw closes. A crocodile basking with
// its jaws agape (opensUp: the snout and upper jaw lift, the lower jaw stays on the ground), a yawn, a threat gape.
// Teeth: cones rigid to the jaw bone on its lip edge, pointing to the closing side (an upper jaw's down), in
// material layer toothLayer; the jaw itself in `layer` (0 the body's skin). Rigid: it never deforms or inverts.
// Froude: lengths x s, times x sqrt s. Kit-only part: one bone (Rigid block), runtime at stage Late; allocation-free
// after construction; main thread.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Jaw", PartKind.Rigid, Bones = "1", Summary = "A hinged jaw on its own bone: a lofted solid (RigidLoft sections) with a tooth row along each lip, hinged at the socket about the joint's x axis. Idle gape: after the body stands still a while it opens, holds and closes (eased), and closes at once when the body walks on.",
          System = "JawRuntime (part runtime, stage Late) + Jaw.EmitMesh", MaterialLayers = new[] { 0, 2, 13 }, KitOnly = true,
          UseWhen = "a mouth that opens: a crocodile basking agape (opensUp), a yawn, a threat gape, a snapping beak")]
    public sealed class Jaw : CreaturePart
    {
        [Param(ParamUnit.Direction, "the jaw's axis from the hinge in the joint frame (along the snout)")] public Vector3 direction = new Vector3(0f, 0f, 1f);
        [Param(ParamUnit.Metres, "the loft starts this far behind the hinge (along -direction)")] public float back;
        [Param(ParamUnit.Metres, "loft length along the axis (from its start)", Example = "crocodile snout 0.26")] public float length = 0.1f;
        [Param(ParamUnit.Struct, "cross-sections along the loft (RigidLoft's LoftKey); >= 2, t = 0 and t = 1 included")] public LoftKey[] keys = new LoftKey[0];
        [Param(ParamUnit.Degrees, "the axis curls down (+) or up (-) over the length")] public float curveDeg;
        [Param(ParamUnit.Flag, "opens upward (an upper jaw / snout that lifts); off: a lower jaw that drops")] public bool opensUp;
        [Param(ParamUnit.Degrees, "widest gape (hinge angle)", Min = 0, Max = 80, Example = "crocodile 38")] public float gapeDeg = 30f;
        [Param(ParamUnit.Seconds, "the body stands still this long before a gape")] public float restSeconds = 2f;
        [Param(ParamUnit.Seconds, "mean pause between gapes (jittered x 0.5 .. 1.5)")] public float intervalSeconds = 6f;
        [Param(ParamUnit.Seconds, "opening time (eased)")] public float openSeconds = 1.2f;
        [Param(ParamUnit.Seconds, "how long it holds the jaw open (jittered x 0.6 .. 1.4)")] public float holdSeconds = 4f;
        [Param(ParamUnit.Seconds, "closing time (eased)")] public float closeSeconds = 0.8f;
        [Param(ParamUnit.Fraction, "the body moves faster than this fraction of its walking speed: close", Min = 0, Max = 1)] public float releaseSpeed = 0.15f;
        [Param(ParamUnit.Count, "teeth per side (0 = none)", Min = 0, Max = 24)] public int teethPerSide;
        [Param(ParamUnit.Metres, "tooth length (the first and last teeth 0.6 x)")] public float toothLength = 0.01f;
        [Param(ParamUnit.Metres, "tooth base radius")] public float toothRadius = 0.003f;
        [Param(ParamUnit.Fraction, "teeth from this place along the loft ..", Min = 0, Max = 1)] public float teethFrom = 0.15f;
        [Param(ParamUnit.Fraction, ".. to this place", Min = 0, Max = 1)] public float teethTo = 0.95f;
        [Param(ParamUnit.Fraction, "teeth sit this fraction of the half width in from the lip edge", Min = 0, Max = 0.5f)] public float toothInset = 0.08f;
        [Param(ParamUnit.Degrees, "teeth splay outward by this much", Min = -30, Max = 45)] public float toothSplayDeg = 10f;
        [Param(ParamUnit.Count, "material layer of the jaw: 0 the body's skin, 2 claw / horn (a beak)", Min = 0, Max = 15)] public int layer;
        [Param(ParamUnit.Count, "material layer of the teeth: 2 claw (default), 13 teeth (restyle it with Layers)", Min = 0, Max = 15, Default = "2")] public int toothLayer = 2;
        [Param(ParamUnit.Count, "mesh: vertices around each section (0 = 20)", Min = 0, Max = 48)] public int around;
        [Param(ParamUnit.Count, "mesh: sections along the length (0 = 16)", Min = 0, Max = 64)] public int rings;

        public override int BoneCount(in SpeciesCtx c) => 1;
        public override int MeshOrder => 100;
        protected override void CopyArrays() { keys = (LoftKey[])keys?.Clone(); }
        public int Around => around > 0 ? around : 20;
        public int Rings => rings > 0 ? rings : 16;
        public float Sign => opensUp ? -1f : 1f;   // the hinge angle's sign about +x (+: the tip goes down)

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Jaw needs a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!(length > 0f) || back < 0f || back >= length) return $"{id}: length > 0, 0 <= back < length";
            if (direction.sqrMagnitude < 1e-8f) return $"{id}: direction must not be zero";
            if (!(gapeDeg >= 0f && gapeDeg <= 80f)) return $"{id}: gapeDeg must be in 0..80";
            if (restSeconds < 0f || intervalSeconds < 0f || !(openSeconds > 0f) || holdSeconds < 0f || !(closeSeconds > 0f)) return $"{id}: times >= 0, open / close > 0";
            if (releaseSpeed < 0f || releaseSpeed > 1f) return $"{id}: releaseSpeed must be in 0..1";
            if (teethPerSide < 0 || teethPerSide > 24) return $"{id}: teethPerSide must be in 0..24";
            if (teethPerSide > 0 && (!(toothLength > 0f) || !(toothRadius > 0f) || !(teethTo > teethFrom) || teethFrom < 0f || teethTo > 1f)) return $"{id}: teeth need length, radius > 0 and 0 <= teethFrom < teethTo <= 1";
            if (layer < 0 || layer >= PartLayers.MaxParts || toothLayer < 0 || toothLayer >= PartLayers.MaxParts) return $"{id}: layers must be in 0..{PartLayers.MaxParts - 1}";
            return Shape().Validate(c) is string p ? $"{id}: {p}" : null;
        }

        // The jaw's shape as a RigidLoft in the hinge frame (origin at the hinge, the loft starting `back` behind it).
        public RigidLoft Shape()
        {
            Vector3 d = PartMeshEmitter.Unit(direction, Vector3.forward);
            return new RigidLoft
            {
                id = id, at = new PartSocket { joint = at.joint, side = 0, offset = -d * back }, direction = d, length = length,
                keys = keys, curveDeg = curveDeg, around = Around, rings = Rings,
            };
        }

        // The hinge offset in the joint frame (at scale 1).
        public Vector3 Hinge => at.offset;

        // The jaw's rotation about the hinge for a gape angle (degrees, 0 = closed) in the joint frame.
        public Quaternion Open(float deg) => Quaternion.AngleAxis(Sign * deg, Vector3.right);

        // A point of the closed jaw (hinge frame, scale 1) at the gape angle, in the joint frame.
        public Vector3 JointPoint(Vector3 hingeLocal, float deg) => Hinge + Open(deg) * hingeLocal;

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new JawRuntime(this, c.Sim, c.Species.Layout.Start(c.Index), c.Size, c.Ordinal) : null;

        // ------------------------------------------------------------ mesh

        // The loft (in the hinge frame, posed on the jaw bone in the rest pose: the hinge on the joint, closed) and the
        // teeth, all rigid to the jaw's bone.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            CreatureSpecies sp = m.Species;
            int bone = sp.Layout.Start(c.Index);
            Matrix4x4 bw = m.Bones[bone];
            Vector4 skin = PartMeshEmitter.Rigid(bone);
            RigidLoft L = Shape();
            int na = L.Around, nr = L.Rings;
            int j = Mathf.Clamp(at.joint, 0, m.Joints - 1);
            float uBase = sp.headCapLength + sp.spacing * j;
            Vector3 jF = m.Rest.Rotations[j] * Vector3.forward, jUp = m.Rest.Rotations[j] * Vector3.up, jp = m.Rest.Joints[j];
            const float e = 1e-3f;
            float U(Vector3 wp) => Mathf.Max(0f, uBase - Vector3.Dot(wp - jp, jF));

            int Pole(float t, Vector3 nLocal)
            {
                L.Centre(t, out Vector3 cc, out _, out _);
                Vector3 p = bw.MultiplyPoint3x4(cc), nn = PartMeshEmitter.Unit(bw.MultiplyVector(nLocal), jUp);
                return m.Pole(p, nn, U(p), layer, Vector3.Dot(nn, jUp), skin, na);
            }
            L.LocalFrame(out _, out Vector3 f0, out _, out _);
            var starts = new System.Collections.Generic.List<int>(nr + 3);
            starts.Add(Pole(0f, -f0));
            Vector3 c1 = Vector3.zero;
            for (int r = 0; r <= nr; r++)
            {
                float t = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * r / nr);
                RigidLoft.Section q = L.SectionAt(t), qA = L.SectionAt(Mathf.Max(0f, t - e)), qB = L.SectionAt(Mathf.Min(1f, t + e));
                if (r == 1) c1 = bw.MultiplyPoint3x4(q.c);
                starts.Add(m.VertexCount);
                for (int k = 0; k <= na; k++)
                {
                    float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * (k == na ? 0 : k) / na;
                    Vector3 p = RigidLoft.Point(q, phi);
                    Vector3 nl = Vector3.Cross(RigidLoft.Point(q, phi + e) - RigidLoft.Point(q, phi - e), RigidLoft.Point(qB, phi) - RigidLoft.Point(qA, phi));
                    if (nl.sqrMagnitude < 1e-14f) nl = p - q.c;
                    if (Vector3.Dot(nl, p - q.c) < 0f) nl = -nl;
                    Vector3 wp = bw.MultiplyPoint3x4(p), wn = PartMeshEmitter.Unit(bw.MultiplyVector(nl), PartMeshEmitter.Unit(bw.MultiplyVector(p - q.c), jUp));
                    m.Vertex(wp, wn, U(wp), (float)k / na, layer, Vector3.Dot(wn, jUp), skin);
                }
            }
            L.Centre(1f, out _, out Vector3 u1, out Vector3 r1);
            starts.Add(Pole(1f, Vector3.Cross(r1, u1)));
            int s1 = starts[2], s2 = starts[3];
            Vector3 q0 = m.Position(s1), face = Vector3.Cross(m.Position(s1 + 1) - q0, m.Position(s2) - q0);
            bool flip = Vector3.Dot(face, q0 - c1) < 0f;
            for (int i = 0; i + 1 < starts.Count; i++)
                m.Stitch(starts[i], starts[i + 1], na, i == 0, i + 2 == starts.Count, flip);

            // teeth: on each lip edge (phi = 0 right, pi left), toward the closing side, splayed outward
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < teethPerSide; i++)
                {
                    float f = teethPerSide == 1 ? 0.5f : i / (float)(teethPerSide - 1);
                    float t = Mathf.Lerp(teethFrom, teethTo, f);
                    ToothLocal(t, side, out Vector3 basePt, out Vector3 dir);
                    float lenScale = 0.6f + 0.4f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(0.15f + 0.7f * f));
                    Vector3 wb = bw.MultiplyPoint3x4(basePt), wd = PartMeshEmitter.Unit(bw.MultiplyVector(dir), -jUp);
                    Vector3 fw = bw.MultiplyVector(Vector3.forward);   // the tooth curls back (toward -forward)
                    Quaternion qt = Quaternion.LookRotation(wd, PartMeshEmitter.Unit(fw - wd * Vector3.Dot(fw, wd), jUp));
                    m.Horn(wb - wd * (0.3f * toothLength), qt, toothRadius, toothRadius * 0.15f, toothLength * lenScale * 1.3f, 8f, bone, toothLayer, 6, U(wb), out _, out _);
                }
        }

        // A tooth's base point and direction in the hinge frame at loft place t on side -1 / +1.
        public void ToothLocal(float t, int side, out Vector3 basePt, out Vector3 dir)
        {
            RigidLoft L = Shape();
            RigidLoft.Section q = L.SectionAt(t);
            // on the lip: the closing half's surface (an upper jaw's underside, a lower jaw's top) toothInset in from the edge
            Vector3 down = opensUp ? -q.up : q.up;   // toward the closing side
            float xf = Mathf.Clamp(1f - toothInset, 0f, 0.98f), e = opensUp ? q.eBottom : q.eTop;   // the section's exponent 2 / p
            float yf = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(xf, 2f / e)), 0.5f * e);
            basePt = q.c + q.right * (side * xf * q.a) + down * (yf * q.b);
            float a = toothSplayDeg * Mathf.Deg2Rad;
            dir = PartMeshEmitter.Unit(down * Mathf.Cos(a) + q.right * (side * Mathf.Sin(a)), down);
        }
    }

    // One Jaw of one individual: closed / opening / open / closing on a stillness timer.
    public sealed class JawRuntime : IPartRuntime
    {
        public enum Phase { Closed = 0, Opening = 1, Open = 2, Closing = 3 }

        readonly Jaw jaw;
        readonly CreatureSim sim;
        readonly int bone, ordinal;
        readonly float scale, T;
        readonly Vector3 tipLocal, topLocal, bottomLocal;   // hinge-frame points for the bounds
        readonly float pad;
        float still, phaseTime, wait, hold, from;
        int gapes;
        uint seed;

        public Phase State { get; private set; }
        public float Angle { get; private set; }            // degrees, 0 = closed .. gapeDeg
        public int Gapes => gapes;
        public int Bone => bone;
        public Jaw Part => jaw;

        public JawRuntime(Jaw jaw, CreatureSim sim, int bone, SizeCtx size, int ordinal)
        {
            this.jaw = jaw; this.sim = sim; this.bone = bone; this.ordinal = ordinal;
            scale = size.L; T = size.T;
            RigidLoft L = jaw.Shape();
            tipLocal = L.Surface(1f, 0.5f * Mathf.PI);
            topLocal = L.Surface(0.5f, 0.5f * Mathf.PI);
            bottomLocal = L.Surface(0.5f, -0.5f * Mathf.PI);
            float w = 0f;
            foreach (LoftKey k in jaw.keys) w = Mathf.Max(w, Mathf.Max(k.halfWidth, k.halfHeight) + Mathf.Abs(k.rise));
            pad = (w + jaw.toothLength) * scale;
            // an individual's own rhythm: from where it was spawned (deterministic, no allocation)
            Vector3 p = sim.Joints[0];
            seed = (uint)(Mathf.RoundToInt(p.x * 97f) * 73856093 ^ Mathf.RoundToInt(p.z * 89f) * 19349663 ^ (ordinal + 1) * 83492791);
            wait = jaw.intervalSeconds * T * (0.25f + 0.5f * Hash01(0));
        }

        public PartStage Stage => PartStage.Late;

        float Hash01(int k)
        {
            uint x = seed + (uint)k * 0x9E3779B1u;
            x ^= x >> 15; x *= 0x85EBCA77u; x ^= x >> 13; x *= 0xC2B2AE35u; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            float walk = Mathf.Max(1e-4f, sim.Species.speed * T);
            bool moving = sim.Speed > jaw.releaseSpeed * walk;
            still = moving ? 0f : still + dt;
            phaseTime += dt;
            float g = jaw.gapeDeg;
            switch (State)
            {
                case Phase.Closed:
                    Angle = 0f;
                    if (wait > 0f) wait -= dt;
                    if (!moving && still > jaw.restSeconds * T && wait <= 0f && g > 0f) { State = Phase.Opening; phaseTime = 0f; gapes++; }
                    break;
                case Phase.Opening:
                    Angle = g * Ease(phaseTime / (jaw.openSeconds * T));
                    if (moving) Close();
                    else if (phaseTime >= jaw.openSeconds * T) { State = Phase.Open; phaseTime = 0f; hold = jaw.holdSeconds * T * (0.6f + 0.8f * Hash01(2 * gapes + 1)); }
                    break;
                case Phase.Open:
                    Angle = g;
                    if (moving || phaseTime >= hold) Close();
                    break;
                case Phase.Closing:
                    Angle = from * (1f - Ease(phaseTime / (jaw.closeSeconds * T)));
                    if (phaseTime >= jaw.closeSeconds * T)
                    {
                        Angle = 0f; State = Phase.Closed; phaseTime = 0f;
                        wait = jaw.intervalSeconds * T * (0.5f + Hash01(2 * gapes + 2));
                    }
                    break;
            }
            Angle = Mathf.Clamp(Angle, 0f, g);
        }

        void Close() { from = Angle; State = Phase.Closing; phaseTime = 0f; }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            int j = jaw.at.joint;
            Quaternion r = sim.Rotations[j];
            Vector3 p = sim.Joints[j] + r * (jaw.Hinge * scale);
            dst[offset + bone] = Matrix4x4.TRS(p, r * jaw.Open(Angle), new Vector3(scale, scale, scale));
        }

        Vector3 World(Vector3 hingeLocal)
        {
            int j = jaw.at.joint;
            return sim.Joints[j] + sim.Rotations[j] * (jaw.JointPoint(hingeLocal, Angle) * scale);
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            Vector3 d = new Vector3(pad, pad, pad);
            Vector3 a = World(tipLocal), b = World(topLocal), e = World(bottomLocal);
            lo = Vector3.Min(lo, Vector3.Min(a, Vector3.Min(b, e)) - d);
            hi = Vector3.Max(hi, Vector3.Max(a, Vector3.Max(b, e)) + d);
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            var col = State == Phase.Closed ? new Color(0.6f, 0.6f, 0.6f, 1f) : new Color(1f, 0.5f, 0.2f, 1f);
            d.Line(World(Vector3.zero), World(tipLocal), col);
        }
    }
}
