// Creature kit T5.2: Flapper - a small fin or flap on its own 1-3 bones that BEATS: pectoral fins, ear flaps,
// frills, paddles. Rooted on a spine joint (offset in the joint frame, x mirrored by side), swept back and drooped
// at rest; each bone adds its share of the beat, a little later than the one before (waveLag), so the fin ripples
// from root to tip. The beat follows the body wave: its frequency is the species' undulation (Slither) x
// beatsPerWave (idleHz without one), its phase locked to the head's wave (CreatureSim.Time), so both fins of a
// pair beat in phase with each other and with the body. The amplitude fades with travel speed like the
// undulation does (idleAmplitude at a standstill), eased so it never pops. Froude: lengths x s, Hz / sqrt s.
// Mesh: a double-sided veined membrane paddle (part 8, the Membrane shading model), skinned along its span.
// Kit-only part (no legacy CreatureSpecies field): bones from the species layout (Fin block), runtime at stage
// Late (once per tick; it only poses bones). Allocation-free after construction; main thread.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Flapper", PartKind.Fin, Bones = "= segments (1-3)", Summary = "A fin or flap on 1-3 bones that beats with the body wave: frequency = the undulation x beatsPerWave (idleHz without Slither), phase locked to the head's wave (a pair beats in phase), a root-to-tip ripple (waveLag), amplitude fading with speed to idleAmplitude. Double-sided veined membrane paddle.",
          System = "FlapperRuntime (part runtime, stage Late) + Flapper.EmitMesh", MaterialLayers = new[] { 8 },
          UseWhen = "pectoral fins, ear flaps, frills, small paddles that beat or flutter (not wings that fly: Wing)")]
    public sealed class Flapper : CreaturePart
    {
        [Param(ParamUnit.Metres, "span, root to tip", Example = "sand eel pectoral 0.045")] public float length;
        [Param(ParamUnit.Metres, "chord (fore-aft width) at the root")] public float rootChord;
        [Param(ParamUnit.Metres, "chord at the tip (the tip itself is rounded)")] public float tipChord;
        [Param(ParamUnit.Count, "bones along the span", Min = 1, Max = 3, Default = "2")] public int segments = 2;
        [Param(ParamUnit.Degrees, "swept back from straight out sideways", Min = 0, Max = 80)] public float sweepDeg = 35f;
        [Param(ParamUnit.Degrees, "rest droop below the joint's horizontal (negative: raised)", Min = -45, Max = 60)] public float droopDeg = 10f;
        [Param(ParamUnit.Degrees, "beat amplitude at the tip (shared over the bones)", Min = 0, Max = 80, Example = "sand eel 28")] public float beatDeg = 25f;
        [Param(ParamUnit.Fraction, "beats per undulation cycle (the beat follows the body wave)", Min = 0.25f, Max = 8, Default = "1")] public float beatsPerWave = 1f;
        [Param(ParamUnit.Hz, "beat frequency when the species does not undulate (no Slither)", Example = "1.5")] public float idleHz = 1.5f;
        [Param(ParamUnit.Fraction, "amplitude at a standstill, fraction of beatDeg (the idle flutter)", Min = 0, Max = 1)] public float idleAmplitude = 0.25f;
        [Param(ParamUnit.Fraction, "phase lag root -> tip in cycles over the span (the ripple)", Min = 0, Max = 0.5f)] public float waveLag = 0.15f;
        [Param(ParamUnit.Degrees, "phase of the beat against the head's wave")] public float phaseDeg;
        [Param(ParamUnit.Count, "fin rays (main veins) across the chord", Min = 1, Max = 12, Default = "5")] public int rays = 5;

        public const int MaxSegments = 3;
        public int Segments => Mathf.Clamp(segments, 1, MaxSegments);

        public override int BoneCount(in SpeciesCtx c) => Segments;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Flapper needs a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!mirror && at.side != -1 && at.side != 1) return $"{id}: side must be -1 or +1 (or mirror)";
            if (segments < 1 || segments > MaxSegments) return $"{id}: segments must be 1..{MaxSegments}";
            if (!(length > 0f) || !(rootChord > 0f) || tipChord < 0f) return $"{id}: length and rootChord > 0, tipChord >= 0";
            if (beatDeg < 0f || beatDeg > 80f) return $"{id}: beatDeg must be in 0..80";
            if (!(beatsPerWave > 0f) || idleHz < 0f) return $"{id}: beatsPerWave > 0, idleHz >= 0";
            if (idleAmplitude < 0f || idleAmplitude > 1f || waveLag < 0f || waveLag > 0.5f) return $"{id}: idleAmplitude in 0..1, waveLag in 0..0.5";
            if (rays < 1 || rays > 12) return $"{id}: rays must be 1..12";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new FlapperRuntime(this, c.Sim, c.Species.Layout.Start(c.Index), c.Size) : null;

        // ------------------------------------------------------------ pose (shared by the mesh and the runtime)

        // The side the fin sits on (+1 right, -1 left; an unmirrored part at side 0 counts as right).
        public int Side => at.side < 0 ? -1 : 1;

        // Bone k's world position and rotation for beat angles `angle` (degrees per bone, cumulative along the span),
        // the root joint at (jointPos, jointRot), sizeScale `scale`. Fin frame: +x the span (mirrored), +z the chord.
        public void Pose(Vector3 jointPos, Quaternion jointRot, float scale, float[] angle, Vector3[] pos, Quaternion[] rot)
        {
            int side = Side, n = Segments;
            Vector3 off = at.offset; off.x *= side;
            Quaternion q0 = jointRot * Quaternion.AngleAxis(side * sweepDeg, Vector3.up) * Quaternion.AngleAxis(-side * droopDeg, Vector3.forward);
            Vector3 p = jointPos + jointRot * off * scale;
            float seg = length / n * scale, cum = 0f;
            for (int k = 0; k < n; k++)
            {
                cum += angle != null ? angle[k] : 0f;
                Quaternion q = q0 * Quaternion.AngleAxis(side * cum, Vector3.forward);
                pos[k] = p; rot[k] = q;
                p += q * new Vector3(side * seg, 0f, 0f);
            }
        }

        // The fin's chord at span fraction t (rounded off over the outer third, so the tip is a curve, not a corner).
        public float ChordAt(float t)
        {
            float c = Mathf.Lerp(rootChord, tipChord, t);
            float r = Mathf.Clamp01((t - 0.65f) / 0.35f);
            return c * Mathf.Sqrt(Mathf.Max(0f, 1f - r * r * 0.85f));
        }

        // ------------------------------------------------------------ mesh

        const float kSheetOffset = 1e-4f;   // the two sheets 0.1 mm apart (no depth fight)
        const int kSpanSteps = 5, kChordSteps = 4;

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSim rest = m.Rest;
            if (rest == null) return;
            int start = m.Species.Layout.Start(c.Index), n = Segments, side = Side;
            var pos = new Vector3[n]; var rot = new Quaternion[n];
            Pose(rest.Joints[at.joint], rest.Rotations[at.joint], 1f, null, pos, rot);
            Quaternion q = rot[0];
            Vector3 span = q * new Vector3(side, 0f, 0f), chord = q * Vector3.forward, normal = q * Vector3.up;
            int cols = kSpanSteps * n, rows = kChordSteps;
            var p = new Vector3[(cols + 1) * (rows + 1)];
            var skin = new Vector4[p.Length];
            for (int i = 0; i <= cols; i++)
            {
                float t = i / (float)cols;
                float ch = ChordAt(t);
                // a gentle camber: the paddle's middle bulges a little toward its upper side
                for (int j = 0; j <= rows; j++)
                {
                    float v = j / (float)rows;                       // 0 trailing .. 1 leading edge
                    float camber = 0.06f * ch * Mathf.Sin(Mathf.PI * v);
                    p[i * (rows + 1) + j] = pos[0] + span * (t * length) + chord * ((v - 0.5f) * ch) + normal * camber;
                    // linear blend along the span between the bones' centres (bone k covers [k/n, (k+1)/n])
                    float u = t * n - 0.5f;
                    int b0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, n - 1), b1 = Mathf.Min(b0 + 1, n - 1);
                    float w1 = b1 == b0 ? 0f : Mathf.Clamp01(u - b0);
                    if (u < 0f) w1 = 0f;
                    skin[i * (rows + 1) + j] = PartMeshEmitter.Blend(start + b0, start + b1, w1);
                }
            }
            for (int sheet = 0; sheet < 2; sheet++)
            {
                float sign = sheet == 0 ? 1f : -1f;
                int first = m.VertexCount;
                for (int i = 0; i <= cols; i++)
                    for (int j = 0; j <= rows; j++)
                    {
                        int k = i * (rows + 1) + j;
                        Vector3 di = p[Mathf.Min(i + 1, cols) * (rows + 1) + j] - p[Mathf.Max(i - 1, 0) * (rows + 1) + j];
                        Vector3 dj = p[i * (rows + 1) + Mathf.Min(j + 1, rows)] - p[i * (rows + 1) + Mathf.Max(j - 1, 0)];
                        Vector3 nn = Vector3.Cross(dj, di);
                        nn = nn.sqrMagnitude > 1e-20f ? nn.normalized : normal;
                        if (Vector3.Dot(nn, normal) < 0f) nn = -nn;          // +n = the paddle's upper side
                        nn *= sign;
                        float t = i / (float)cols, v = j / (float)rows;
                        // uv0 = (span m, rays fanning across the chord: a main vein every 1/rays), uv1.y = sheet sign x
                        // (0.1 + 0.9 x the fraction to the rim: the tip and the trailing edge darken)
                        float edge = Mathf.Max(t, 1f - v);
                        m.Vertex(p[k] + nn * kSheetOffset, nn, t * length, v * rays / 5f, PartMeshEmitter.PartMembrane, sign * (0.1f + 0.9f * edge), skin[k]);
                    }
                for (int i = 0; i < cols; i++)
                    for (int j = 0; j < rows; j++)
                    {
                        int i00 = first + i * (rows + 1) + j, i10 = i00 + rows + 1, i01 = i00 + 1, i11 = i10 + 1;
                        Quad(m, i00, i10, i11, i01, sign > 0f ? normal : -normal);
                    }
            }
        }

        // two triangles wound so their face normal points along `facing`
        static void Quad(PartMeshEmitter m, int a, int b, int c, int d, Vector3 facing)
        {
            Vector3 fa = m.Position(a), fb = m.Position(b), fc = m.Position(c);
            bool flip = Vector3.Dot(Vector3.Cross(fb - fa, fc - fa), facing) < 0f;
            m.Tri(a, b, c, flip);
            m.Tri(a, c, d, flip);
        }
    }

    // One Flapper of one individual: poses its bones from the root joint and the beat. Stage Late: the beat eases
    // its amplitude once per tick; WriteBones reads the sim's joints at draw time.
    public sealed class FlapperRuntime : IPartRuntime
    {
        const float kEaseSeconds = 0.35f;    // amplitude easing time constant at scale 1 (x sqrt s)

        readonly Flapper f;
        readonly CreatureSim sim;
        readonly int start, n;
        readonly float scale, T, omega, lag, phase0;
        readonly float[] angle;
        readonly Vector3[] pos;
        readonly Quaternion[] rot;
        float amp;                           // eased beat amplitude, degrees at the tip (0 at construction: the rest pose)

        public Flapper Part => f;
        public float Amplitude => amp;
        public int FirstBone => start;
        // beat frequency in Hz for this individual (Froude: / sqrt s)
        public float BeatHz => omega / (2f * Mathf.PI);

        public FlapperRuntime(Flapper flapper, CreatureSim sim, int firstBone, SizeCtx size)
        {
            f = flapper; this.sim = sim; start = firstBone; n = flapper.Segments;
            scale = size.L; T = size.T;
            CreatureSpecies sp = sim.Species;
            float hz = sp.undulationHz > 0f && sp.undulationDeg != 0f ? sp.undulationHz * flapper.beatsPerWave : flapper.idleHz;
            omega = 2f * Mathf.PI * hz / T;
            lag = 2f * Mathf.PI * flapper.waveLag;
            phase0 = flapper.phaseDeg * Mathf.Deg2Rad;
            angle = new float[n]; pos = new Vector3[n]; rot = new Quaternion[n];
        }

        public PartStage Stage => PartStage.Late;

        // The amplitude the beat eases toward: idle at a standstill, full once the body travels at 30 % of its speed
        // (the undulation's own fade-in).
        public float TargetAmplitude
        {
            get
            {
                float maxSpeed = sim.Species.speed * sim.TimeScale;
                float travel = maxSpeed > 1e-4f ? Mathf.Clamp01(sim.Speed / (0.3f * maxSpeed)) : 1f;
                return f.beatDeg * Mathf.Lerp(f.idleAmplitude, 1f, travel);
            }
        }

        public void Step(in StepCtx c, float dt)
        {
            float k = 1f - Mathf.Exp(-dt / (kEaseSeconds * T));
            amp += (TargetAmplitude - amp) * k;
        }

        // The beat angle of bone k now (degrees; its share of the tip amplitude).
        public float AngleOf(int k) => amp / n * Mathf.Sin(omega * sim.Time + phase0 - lag * k);

        void PoseNow()
        {
            for (int k = 0; k < n; k++) angle[k] = AngleOf(k);
            f.Pose(sim.Joints[f.at.joint], sim.Rotations[f.at.joint], scale, angle, pos, rot);
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            PoseNow();
            Vector3 s = Vector3.one * scale;
            for (int k = 0; k < n; k++) dst[offset + start + k] = Matrix4x4.TRS(pos[k], rot[k], s);
        }

        // The tip, the root and the chord ends (the fin is short: these bound it).
        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            PoseNow();
            float ch = 0.5f * Mathf.Max(f.rootChord, f.tipChord) * scale;
            int side = f.Side;
            Vector3 tip = pos[n - 1] + rot[n - 1] * new Vector3(side * f.length / n * scale, 0f, 0f);
            Grow(ref lo, ref hi, pos[0], ch); Grow(ref lo, ref hi, tip, ch);
        }

        static void Grow(ref Vector3 lo, ref Vector3 hi, Vector3 p, float r)
        {
            lo = Vector3.Min(lo, p - new Vector3(r, r, r));
            hi = Vector3.Max(hi, p + new Vector3(r, r, r));
        }

        // Tip position now (tests, the debug view).
        public Vector3 Tip()
        {
            PoseNow();
            return pos[n - 1] + rot[n - 1] * new Vector3(f.Side * f.length / n * scale, 0f, 0f);
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            PoseNow();
            var c = new Color(1f, 0.6f, 0.2f, 1f);
            for (int k = 0; k < n; k++)
            {
                Vector3 e = pos[k] + rot[k] * new Vector3(f.Side * f.length / n * scale, 0f, 0f);
                d.Line(pos[k], e, c);
            }
        }
    }
}
