// Creature kit (sky manta): RayWing - a THICK pectoral wing that is itself a chain, in two directions: a lattice of
// bones, `stations` along the span (root -> tip) by `chordRows` along the chord (leading -> trailing edge).
//   Span chain    the leading edge is a chain of segments hinged about the body's long axis. The root segment is
//                 driven (the stroke of the locomotion mode, IFlapDriver: phase, amplitude, turn asymmetry; the glide
//                 pose when it does not beat); every further segment FOLLOWS the one before it - a damped spring on
//                 its absolute dihedral (spanHz, spanDamping) - so a beat travels out along the wing to the tip with a
//                 growing lag and the tip whips over at the stroke's turn (the winged serpent's chain rule: "when the
//                 wing flaps downward the shoulders go up, and the chain follows"). The body's own heave (opposite
//                 the downstroke) reaches the wing as inertia: the outer wing lags it (inertiaDeg).
//   Chord chains  at every station the rows behind the leading edge hang off it: the first follows a feathering
//                 pitch set by the leading edge's beat speed (featherDeg: trailing edge up on the downstroke), each
//                 next row follows the one before it (chordHz, chordDamping) - a wave from the leading to the
//                 trailing edge.
// Mesh: the RayShape surface (the body's edge continues into it: same samples, same normals), thick at the root and
// thin at the tip, a round leading edge, a fine trailing edge; vertices sit on the chord rows (rigid in the chord,
// the nose cluster on row 0) and blend two stations along the span (skinning is two bones). The root column is
// skinned to the spine like the body's edge. Bind origin of every lattice bone: the attach joint (on the body's
// axis), so the pattern's surface space is the disc's plan (RayBody.EmitDiscVertex).
// Kit-only. Runtime stage AfterLimbs (every sub-step); allocation-free after construction.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("RayWing", PartKind.Fin, Bones = "= stations x chordRows", Summary = "A thick pectoral wing that is a 2D chain: a lattice of stations (span) x chordRows (chord). The root segment is driven by the locomotion mode's stroke (IFlapDriver), every outer segment follows the one before it (a damped spring: the beat travels to a whipping tip), every chord row follows the row ahead (feathering wave to the trailing edge); the body's heave reaches it as inertia. Mesh: thick airfoil sections continuing the RayBody's edge (RayShape), round leading edge, falcate pointed tip.",
          System = "RayWingRuntime (part runtime, stage AfterLimbs) + RayWing.EmitMesh", MaterialLayers = new[] { 15 }, KitOnly = true,
          UseWhen = "manta / eagle ray / skate pectoral fins; any thick wing or fin that bends along its span AND its chord (a whale fluke, a cuttlefish fin, a soft-bodied flyer)")]
    public sealed class RayWing : CreaturePart
    {
        [Param(ParamUnit.Metres, "span from the body's edge to the tip", Example = "manta 1.35")] public float span = 1.35f;
        [Param(ParamUnit.Degrees, "leading edge sweep from straight out sideways", Min = 0, Max = 60)] public float sweepDeg = 20f;
        [Param(ParamUnit.Metres, "extra sweep of the tip (x u^3: a falcate, swept-back tip)")] public float tipSweep = 0.25f;
        [Param(ParamUnit.Metres, "chord at the tip")] public float tipChord = 0.03f;
        [Param(ParamUnit.Fraction, "chord falls as (1 - u)^this (> 1: a concave trailing edge)", Min = 0.5f, Max = 3f)] public float chordExponent = 1.2f;
        [Param(ParamUnit.Metres, "full thickness at the tip")] public float tipThickness = 0.006f;
        [Param(ParamUnit.Fraction, "how fast the thick root thins out along the span (1 = the plain cubic)", Min = 1, Max = 4)] public float thicknessFalloff = 1.5f;
        [Param(ParamUnit.Count, "span stations (bones along the span)", Min = 3, Max = 12, Default = "7")] public int stations = 7;
        [Param(ParamUnit.Count, "chord rows (bones along the chord)", Min = 3, Max = 8, Default = "6")] public int chordRows = 6;
        [Param(ParamUnit.Count, "mesh columns per station interval", Min = 1, Max = 6, Default = "3")] public int subdivisions = 3;

        [Param(ParamUnit.Degrees, "root stroke amplitude (the driven segment's dihedral, + up)", Min = 0, Max = 60, Example = "manta 30")] public float flapDeg = 30f;
        [Param(ParamUnit.Degrees, "root dihedral while gliding (+ up)", Min = -30, Max = 30)] public float glideDeg = 3f;
        [Param(ParamUnit.Degrees, "gliding: extra bend of the outer wing, growing to the tip (+ tips up, - drooped)", Min = -40, Max = 40)] public float glideTipDeg = 10f;
        [Param(ParamUnit.Hz, "span chain: natural frequency of a segment following the one before", Example = "manta 2.4")] public float spanHz = 2.4f;
        [Param(ParamUnit.Fraction, "span chain: damping ratio (< 0.7: the tip overshoots - the whip)", Min = 0.1f, Max = 2)] public float spanDamping = 0.45f;
        [Param(ParamUnit.Hz, "chord chain: natural frequency of a row following the row ahead", Example = "manta 3")] public float chordHz = 3f;
        [Param(ParamUnit.Fraction, "chord chain: damping ratio", Min = 0.1f, Max = 2)] public float chordDamping = 0.55f;
        [Param(ParamUnit.Degrees, "feathering: trailing rows' pitch per m/s of the leading edge's beat speed (scale 1)", Min = 0, Max = 40)] public float featherDeg = 14f;
        [Param(ParamUnit.Degrees, "inertia: outer wing's lag per m/s^2 of the body's vertical acceleration", Min = 0, Max = 10)] public float inertiaDeg = 1.5f;
        [Param(ParamUnit.Degrees, "largest bend between neighbouring span segments or chord rows", Min = 5, Max = 60)] public float maxBendDeg = 38f;
        [Param(ParamUnit.Fraction, "turns: the outer wing's stroke grows / shrinks by this x the turn's asymmetry", Min = 0, Max = 1)] public float asymmetry = 0.3f;

        public int Stations => Mathf.Clamp(stations, 3, 12);
        public int Rows => Mathf.Clamp(chordRows, 3, 8);
        public int Side => at.side < 0 ? -1 : 1;

        public override int BoneCount(in SpeciesCtx c) => Stations * Rows;
        public override int MeshOrder => 100;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a RayWing needs a RayBody";
            RayBody body = null;
            foreach (CreaturePart p in c.Parts) if (p is RayBody rb) body = rb;
            if (body == null) return $"{id}: a RayWing continues a RayBody's edge (no RayBody)";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!mirror && at.side != -1 && at.side != 1) return $"{id}: side must be -1 or +1 (or mirror)";
            if (!(span > 0f) || tipChord < 0f || tipThickness < 0f) return $"{id}: span > 0, tipChord and tipThickness >= 0";
            if (stations < 3 || stations > 12 || chordRows < 3 || chordRows > 8 || subdivisions < 1 || subdivisions > 6) return $"{id}: stations 3..12, chordRows 3..8, subdivisions 1..6";
            if (!(spanHz > 0f) || !(chordHz > 0f)) return $"{id}: spanHz and chordHz must be > 0";
            if (flapDeg < 0f || flapDeg > 60f || maxBendDeg < 5f || maxBendDeg > 60f) return $"{id}: flapDeg 0..60, maxBendDeg 5..60";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new RayWingRuntime(this, c.Sim, c.Species.Layout.Start(c.Index), c.Size) : null;

        // ------------------------------------------------------------ rest lattice (shared by the mesh and the runtime)

        // Span fraction of station k (0 = the body's edge, Stations = the tip).
        public float StationU(int k) => (float)k / Stations;

        // Rest node (k = 0..Stations, r = 0..Rows-1) in the frame of rest joint 0 (signed x: this wing's side).
        public Vector3 RestNode(RayShape shape, int k, int r)
        {
            float x = Side * (shape.HalfWidth + span * StationU(k));
            return shape.Mid(x, (float)r / (Rows - 1));
        }

        public int Bone(int start, int k, int r) => start + (k - 1) * Rows + r;

        // ------------------------------------------------------------ mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSim rest = m.Rest;
            if (rest == null) return;
            RayShape shape = RayShape.For(KitView.Parts(m.Species));
            if (shape == null) return;
            RayBody body = shape.Body;
            int start = m.Species.Layout.Start(c.Index), K = Stations, side = Side, L = shape.LoopCount, sub = Mathf.Clamp(subdivisions, 1, 6);
            Matrix4x4 frame = Matrix4x4.TRS(rest.Joints[0], rest.Rotations[0], Vector3.one);
            int cols = K * sub;
            int first = m.VertexCount;
            for (int j = 0; j <= cols; j++)
            {
                int k = Mathf.Min(j / sub, K - 1);
                float t = (j - k * sub) / (float)sub;   // 0..1 within [station k, k + 1]
                float ax = shape.HalfWidth + span * (j / (float)cols);
                float x = side * ax;
                for (int l = 0; l < L; l++)
                {
                    shape.Loop(l, out float v, out _);
                    int r = shape.RowOf(v);
                    Vector4 skin;
                    if (k == 0 && t == 0f) skin = body.SpineSkin(shape.LoopPoint(x, l).z);
                    else
                    {
                        int b0 = k == 0 ? body.SpineBone(shape.LoopPoint(x, l).z) : Bone(start, k, r);
                        skin = PartMeshEmitter.Blend(b0, Bone(start, k + 1, r), t);
                    }
                    RayBody.EmitDiscVertex(m, shape, frame, x, l, skin);
                }
            }
            RayBody.DiscQuads(m, first, cols, L);
            // the tip: a point just past the last column, fanned to it
            float tipX = side * (shape.HalfSpan + Mathf.Max(0.5f * tipChord, 0.01f));
            Vector3 tipMid = shape.Mid(side * shape.HalfSpan, 0.5f);
            Vector3 tip = new Vector3(tipX, tipMid.y, tipMid.z);
            Vector3 tipN = new Vector3(side, 0f, 0f);
            int pole = m.VertexCount;
            m.Vertex(frame.MultiplyPoint3x4(tip), frame.MultiplyVector(tipN), body.headCapLength - tip.z, 0f, RayBody.PartRay, 0f,
                     PartMeshEmitter.Rigid(Bone(start, K, shape.RowOf(0.5f))));
            int last = first + cols * L;
            for (int l = 0; l < L - 1; l++) Fan(m, last + l, last + l + 1, pole);
            Fan(m, last + L - 1, last, pole);
        }

        static void Fan(PartMeshEmitter m, int a, int b, int pole)
        {
            Vector3 fa = m.Position(a), fb = m.Position(b), fp = m.Position(pole);
            Vector3 facing = m.Normal(a) + m.Normal(b) + m.Normal(pole);
            bool flip = Vector3.Dot(Vector3.Cross(fb - fa, fp - fa), facing) < 0f;
            m.Tri(a, b, pole, flip);
        }
    }

    // One RayWing of one individual: the span and chord chains (degrees), their poses and bones.
    public sealed class RayWingRuntime : IPartRuntime
    {
        readonly RayWing w;
        readonly CreatureSim sim;
        readonly IFlapDriver drive;
        readonly int start, K, R, side, joint;
        readonly float scale, T, wSpan, wChord, zSpan, zChord;
        readonly Vector3[,] rest;        // rest nodes, attach-joint frame, scale 1 (k = 0..K)
        readonly Vector3[,] node;        // posed nodes, same frame
        readonly Quaternion[,] frame;    // posed lattice rotations (root frame)
        readonly float[] phi, phiV;      // span segment s = 1..K: absolute dihedral (deg) and its rate
        readonly float[,] psi, psiV;     // chord segment (station k, row r = 1..R-1): absolute pitch (deg) and rate
        readonly float[] segLen;         // span segment lengths (scale 1)
        readonly float[] tipShare;       // glide bend share per span segment (0 at the root .. 1 at the tip)
        float prevRoot;
        bool started;
        public float Time { get; private set; }

        public RayWing Part => w;
        public int FirstBone => start;
        public int Stations => K;
        public int Rows => R;

        public RayWingRuntime(RayWing wing, CreatureSim sim, int firstBone, SizeCtx size)
        {
            w = wing; this.sim = sim; start = firstBone;
            K = wing.Stations; R = wing.Rows; side = wing.Side; joint = wing.at.joint;
            scale = size.L; T = size.T;
            drive = sim.LocomotionMode<IFlapDriver>();
            wSpan = 2f * Mathf.PI * wing.spanHz / T; zSpan = wing.spanDamping;
            wChord = 2f * Mathf.PI * wing.chordHz / T; zChord = wing.chordDamping;
            RayShape shape = RayShape.For(KitView.Parts(sim.Species));
            rest = new Vector3[K + 1, R]; node = new Vector3[K + 1, R]; frame = new Quaternion[K + 1, R];
            Vector3 att = new Vector3(0f, 0f, -joint * shape.Body.spacing);
            for (int k = 0; k <= K; k++)
                for (int r = 0; r < R; r++) { rest[k, r] = wing.RestNode(shape, k, r) - att; node[k, r] = rest[k, r]; frame[k, r] = Quaternion.identity; }
            phi = new float[K + 1]; phiV = new float[K + 1];
            psi = new float[K + 1, R]; psiV = new float[K + 1, R];
            segLen = new float[K + 1]; tipShare = new float[K + 1];
            for (int s = 1; s <= K; s++)
            {
                Vector3 d = rest[s, 0] - rest[s - 1, 0];
                segLen[s] = new Vector2(d.x, d.y).magnitude;
                float u = (s - 0.5f) / K;
                tipShare[s] = u * u;
            }
        }

        public PartStage Stage => PartStage.AfterLimbs;

        // The root segment's dihedral the stroke asks for now (degrees, + up).
        public float RootTarget()
        {
            if (drive == null) return w.glideDeg;
            float a = drive.StrokeAmplitude;
            float amp = w.flapDeg * (1f + side * w.asymmetry * drive.Asymmetry);
            return a * amp * Mathf.Sin(drive.StrokePhase) + (1f - a) * w.glideDeg;
        }

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            Time += dt;
            float a = drive != null ? drive.StrokeAmplitude : 0f;
            // the root is driven
            float root = RootTarget();
            if (!started) { prevRoot = root; started = true; for (int s = 1; s <= K; s++) phi[s] = root; }
            phi[1] = root; phiV[1] = (root - prevRoot) / dt; prevRoot = root;
            // the body's vertical acceleration (along its up) as the outer wing's inertia; Froude: accelerations are size-free
            float up = 0f;
            if (drive != null)
            {
                Vector3 bodyUp = sim.Rotations[joint] * Vector3.up;
                up = Vector3.Dot(drive.BodyAcceleration, bodyUp);
            }
            float glideBend = (1f - a) * w.glideTipDeg;
            float maxBend = w.maxBendDeg;
            // the followers' own sub-steps: semi-implicit Euler stays accurate (and stable) for omega x h <= 0.5
            int sub = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(wSpan, wChord) * dt / 0.5f), 1, 64);
            float h = dt / sub;
            for (int it = 0; it < sub; it++) Follow(h, up, glideBend, maxBend);
        }

        void Follow(float dt, float up, float glideBend, float maxBend)
        {
            // span chain: each segment follows the one before it (absolute dihedral), semi-implicit
            for (int s = 2; s <= K; s++)
            {
                float target = phi[s - 1] + glideBend * (tipShare[s] - tipShare[s - 1]) - w.inertiaDeg * up * tipShare[s];
                float acc = wSpan * wSpan * (target - phi[s]) - 2f * zSpan * wSpan * phiV[s];
                phiV[s] += acc * dt;
                phi[s] += phiV[s] * dt;
                float rel = phi[s] - phi[s - 1];
                if (rel > maxBend) { phi[s] = phi[s - 1] + maxBend; phiV[s] = Mathf.Min(phiV[s], phiV[s - 1]); }
                else if (rel < -maxBend) { phi[s] = phi[s - 1] - maxBend; phiV[s] = Mathf.Max(phiV[s], phiV[s - 1]); }
            }
            // chord chains: row 1 feathers with the leading edge's beat speed, each next row follows the row ahead
            float lever = 0f;
            for (int k = 1; k <= K; k++)
            {
                lever += phiV[k] * Mathf.Deg2Rad * segLen[k];   // m/s at scale 1 per unit of size (Froude-normalised below)
                float beat = lever * scale / T;                 // the leading edge's beat speed, Froude-normalised (m/s at scale 1)
                float feather = Mathf.Clamp(-w.featherDeg * beat, -maxBend, maxBend);
                for (int r = 1; r < R; r++)
                {
                    float target = r == 1 ? feather : psi[k, r - 1];
                    float acc = wChord * wChord * (target - psi[k, r]) - 2f * zChord * wChord * psiV[k, r];
                    psiV[k, r] += acc * dt;
                    psi[k, r] += psiV[k, r] * dt;
                    float prev = r == 1 ? 0f : psi[k, r - 1];
                    float rel = psi[k, r] - prev;
                    if (rel > maxBend) psi[k, r] = prev + maxBend;
                    else if (rel < -maxBend) psi[k, r] = prev - maxBend;
                }
            }
        }

        // The lattice's pose (root frame, scale 1) from the chain angles.
        void Pose()
        {
            for (int r = 0; r < R; r++) { node[0, r] = rest[0, r]; frame[0, r] = Quaternion.identity; }
            for (int k = 1; k <= K; k++)
            {
                Quaternion seg = Quaternion.AngleAxis(side * phi[k], Vector3.forward);
                node[k, 0] = node[k - 1, 0] + seg * (rest[k, 0] - rest[k - 1, 0]);
                float th = k < K ? 0.5f * (phi[k] + phi[k + 1]) : phi[k];
                Quaternion st = Quaternion.AngleAxis(side * th, Vector3.forward);
                for (int r = 0; r < R; r++)
                {
                    if (r > 0) node[k, r] = node[k, r - 1] + st * (Quaternion.AngleAxis(psi[k, r], Vector3.right) * (rest[k, r] - rest[k, r - 1]));
                    float pin = r > 0 ? psi[k, r] : 0f, pout = r + 1 < R ? psi[k, r + 1] : pin;
                    if (r == 0) pin = pout;
                    frame[k, r] = st * Quaternion.AngleAxis(0.5f * (pin + pout), Vector3.right);
                }
            }
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            Pose();
            Vector3 J = sim.Joints[joint];
            Quaternion Ra = sim.Rotations[joint];
            Vector3 s = Vector3.one * scale;
            for (int k = 1; k <= K; k++)
                for (int r = 0; r < R; r++)
                {
                    Quaternion L = frame[k, r];
                    Vector3 local = node[k, r] - L * rest[k, r];   // bind origin: the attach joint
                    dst[offset + start + (k - 1) * R + r] = Matrix4x4.TRS(J + Ra * (local * scale), Ra * L, s);
                }
        }

        // Every node, grown by the root's thickness (the nodes sit mid-thickness).
        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            Pose();
            Vector3 J = sim.Joints[joint];
            Quaternion Ra = sim.Rotations[joint];
            float pad = 0.15f * scale;
            for (int k = 1; k <= K; k++)
                for (int r = 0; r < R; r++)
                {
                    Vector3 p = J + Ra * (node[k, r] * scale);
                    lo = Vector3.Min(lo, p - new Vector3(pad, pad, pad));
                    hi = Vector3.Max(hi, p + new Vector3(pad, pad, pad));
                }
        }

        // ---- tests and the debug view
        public float Dihedral(int s) => phi[s];
        public float Pitch(int k, int r) => psi[k, r];

        // Node (k, r) in world space now.
        public Vector3 Node(int k, int r)
        {
            Pose();
            return sim.Joints[joint] + sim.Rotations[joint] * (node[k, r] * scale);
        }

        // Node (k, r) height above its rest place, along the attach joint's up, in this individual's metres.
        public float NodeLift(int k, int r)
        {
            Pose();
            return (node[k, r].y - rest[k, r].y) * scale;
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            Pose();
            var c = new Color(0.3f, 0.8f, 1f, 1f);
            Vector3 J = sim.Joints[joint];
            Quaternion Ra = sim.Rotations[joint];
            for (int k = 1; k <= K; k++)
                for (int r = 0; r < R; r++)
                {
                    Vector3 p = J + Ra * (node[k, r] * scale);
                    d.Line(J + Ra * (node[k - 1, r] * scale), p, c);
                    if (r > 0) d.Line(J + Ra * (node[k, r - 1] * scale), p, c);
                }
        }
    }
}
