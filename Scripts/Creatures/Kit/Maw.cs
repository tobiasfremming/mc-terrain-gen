// Creature kit (2026-10-20, the Delver): Maw - a big-mouthed head: a lofted skull rigid to its joint (sections keyed
// along the head: half-width, top and palate heights, a squarer lower half; brow ridges over the eyes, nostril bosses
// at the snout's tip, a cheek flare at the hinge) and a hinged lower jaw on a bone of its own (a lofted mandible, its
// floor the mouth's), both lined inside with the mouth layer, and a row of conical teeth along every lip - fangs at the
// front, raked back - that interlock when the jaw shuts. The gape follows CreatureSim.Display (a behaviour or posture
// part asks for a display: a Burrow part's burst out of the ground, a threat) eased at gapeSeconds, plus a slow
// breathing gape (breathDeg) when nothing asks. Rigid: it never deforms or inverts.
// Material layers: `layer` (0 the body's skin), `toothLayer` (13 teeth), `mouthLayer` (14 the mouth's soft tissue).
// Froude: lengths x s, times x sqrt s. Kit-only part: one bone (Rigid block), runtime at stage Late; allocation-free
// after construction.
using System;
using UnityEngine;

namespace CreatureKit
{
    // One section of a Maw's skull or mandible, in the hinge frame (z forward along the head, y up).
    [Serializable]
    public struct MawKey
    {
        [Param(ParamUnit.Metres, "place along the head from the hinge (+ forward)")] public float z;
        [Param(ParamUnit.Metres, "half width")] public float halfWidth;
        [Param(ParamUnit.Metres, "top of the section above the hinge (the skull's roof; a mandible's floor)")] public float top;
        [Param(ParamUnit.Metres, "bottom of the section (the skull's palate; a mandible's chin)")] public float bottom;
    }

    [Serializable]
    [Part("Maw", PartKind.Rigid, Bones = "1 (the lower jaw)", Summary = "A big-mouthed head: a lofted skull rigid to its joint (brow ridges, nostril bosses, cheek flare) and a hinged mandible on its own bone, both lined with the mouth layer, with interlocking raked conical teeth along every lip (fangs at the front). Gapes with CreatureSim.Display (a burst, a threat) and breathes slowly otherwise.",
          System = "MawRuntime (part runtime, stage Late) + Maw.EmitMesh (LimbLoft)", MaterialLayers = new[] { 0, 13, 14 }, KitOnly = true,
          UseWhen = "reptilian and monstrous heads with a huge gape: burrowers bursting out jaws first, crocodilian or theropod-like heads, deep-sea gapers")]
    public sealed class Maw : CreaturePart
    {
        [Param(ParamUnit.Struct, "skull sections, back to front (>= 3)")] public MawKey[] skull = new MawKey[0];
        [Param(ParamUnit.Struct, "mandible sections, back to front (>= 3), closed against the palate")] public MawKey[] mandible = new MawKey[0];
        [Param(ParamUnit.Fraction, "how squared the lower halves are (superellipse exponent)", Min = 2, Max = 6, Default = "3")] public float square = 3f;
        [Param(ParamUnit.Metres, "brow ridge height over the eyes")] public float brow;
        [Param(ParamUnit.Metres, "where along the head the brows peak (from the hinge)")] public float browAt;
        [Param(ParamUnit.Metres, "nostril boss height at the snout's tip")] public float nostril;
        [Param(ParamUnit.Degrees, "the widest gape (CreatureSim.Display 1)", Min = 0, Max = 85, Default = "60")] public float gapeDeg = 60f;
        [Param(ParamUnit.Degrees, "the breathing gape at rest (0 = shut)", Min = 0, Max = 15, Default = "3")] public float breathDeg = 3f;
        [Param(ParamUnit.Seconds, "the breath's period")] public float breathSeconds = 3.5f;
        [Param(ParamUnit.Seconds, "time to swing from shut to the full gape")] public float gapeSeconds = 0.25f;
        [Param(ParamUnit.Count, "teeth along each lip side, upper and lower (0 = none)", Min = 0, Max = 24)] public int teethPerSide = 9;
        [Param(ParamUnit.Metres, "a back tooth's length")] public float toothLength = 0.012f;
        [Param(ParamUnit.Fraction, "the front teeth (fangs) are this much longer", Min = 1, Max = 3, Default = "1.8")] public float fang = 1.8f;
        [Param(ParamUnit.Fraction, "tooth base radius, x its length", Min = 0.1f, Max = 0.6f, Default = "0.28")] public float toothWidth = 0.28f;
        [Param(ParamUnit.Degrees, "teeth rake back from straight down / up", Min = 0, Max = 50, Default = "18")] public float toothRakeDeg = 18f;
        [Param(ParamUnit.Fraction, "teeth start this far along the lip (0 = the hinge .. 1 = the tip)", Min = 0, Max = 1, Default = "0.2")] public float teethFrom = 0.2f;
        [Param(ParamUnit.Metres, "teeth sit this far in from the lip's edge")] public float toothInset = 0.006f;
        [Param(ParamUnit.Count, "material layer of the skull and mandible (0 the body's skin)", Min = 0, Max = 15)] public int layer;
        [Param(ParamUnit.Count, "material layer of the teeth (13 teeth)", Min = 0, Max = 15, Default = "13")] public int toothLayer = 13;
        [Param(ParamUnit.Count, "material layer of the mouth's lining (14 the mouth's soft tissue)", Min = 0, Max = 15, Default = "14")] public int mouthLayer = 14;
        [Param(ParamUnit.Count, "mesh: vertices round a section, 0 = 28", Min = 0, Max = 64)] public int around;

        public override int BoneCount(in SpeciesCtx c) => 1;
        protected override void CopyArrays() { skull = (MawKey[])skull?.Clone() ?? new MawKey[0]; mandible = (MawKey[])mandible?.Clone() ?? new MawKey[0]; }

        public Vector3 Hinge => at.offset;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Maw needs a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (skull == null || skull.Length < 3 || mandible == null || mandible.Length < 3) return $"{id}: skull and mandible need >= 3 sections each";
            foreach (MawKey[] keys in new[] { skull, mandible })
                for (int i = 0; i < keys.Length; i++)
                {
                    if (!(keys[i].halfWidth > 0f) || !(keys[i].top > keys[i].bottom)) return $"{id}: section {i}: halfWidth > 0, top > bottom";
                    if (i > 0 && !(keys[i].z > keys[i - 1].z)) return $"{id}: sections must run back to front (z increasing)";
                }
            if (gapeDeg < 0f || gapeDeg > 85f || breathDeg < 0f || breathDeg > 15f || !(gapeSeconds > 0f) || !(breathSeconds > 0f)) return $"{id}: gapeDeg 0..85, breathDeg 0..15, gapeSeconds, breathSeconds > 0";
            if (teethPerSide < 0 || teethPerSide > 24) return $"{id}: teethPerSide 0..24";
            if (teethPerSide > 0 && (!(toothLength > 0f) || fang < 1f || toothWidth < 0.1f || teethFrom < 0f || teethFrom >= 1f)) return $"{id}: teeth need toothLength > 0, fang >= 1, toothWidth >= 0.1, teethFrom 0..1";
            foreach (int l in new[] { layer, toothLayer, mouthLayer }) if (l < 0 || l >= PartLayers.MaxParts || (l >= 4 && l <= 7)) return $"{id}: layers must be spine-shader part ids (0..3, 8..15)";
            return null;
        }

        // ------------------------------------------------------------ sections

        static float Lerp(MawKey[] k, float z, Func<MawKey, float> f)
        {
            if (z <= k[0].z) return f(k[0]);
            for (int i = 1; i < k.Length; i++)
                if (z <= k[i].z)
                {
                    float t = (z - k[i - 1].z) / Mathf.Max(1e-6f, k[i].z - k[i - 1].z);
                    // Catmull-Rom through the keys (clamped ends): a smooth skull
                    float p0 = f(k[Mathf.Max(0, i - 2)]), p1 = f(k[i - 1]), p2 = f(k[i]), p3 = f(k[Mathf.Min(k.Length - 1, i + 1)]);
                    return PartMeshEmitter.CR(p0, p1, p2, p3, t);
                }
            return f(k[k.Length - 1]);
        }

        public float HalfWidth(MawKey[] k, float z) => Mathf.Max(1e-4f, Lerp(k, z, q => q.halfWidth));
        public float Top(MawKey[] k, float z) => Lerp(k, z, q => q.top);
        public float Bottom(MawKey[] k, float z) => Lerp(k, z, q => q.bottom);

        // The lip line: the skull's palate edge at z (x = half width, y = bottom), in the hinge frame.
        public Vector3 UpperLip(float z, int side) => new Vector3(side * HalfWidth(skull, z), Bottom(skull, z), z);
        public Vector3 LowerLip(float z, int side) => new Vector3(side * HalfWidth(mandible, z), Top(mandible, z), z);

        // The jaw's rotation about the hinge (degrees; + opens: the mandible's front drops).
        public static Quaternion Open(float deg) => Quaternion.AngleAxis(deg, Vector3.right);

        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null ? new MawRuntime(this, c.Sim, c.Species.Layout.Start(c.Index)) : null;

        // ------------------------------------------------------------ mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            CreatureSpecies sp = m.Species;
            int bone = sp.Layout.Start(c.Index);
            int j = Mathf.Clamp(at.joint, 0, m.Joints - 1);
            // the hinge frame in the rest pose: on the head joint (the skull) and on the jaw's bone (closed: the same)
            Matrix4x4 head = Matrix4x4.TRS(m.Rest.Joints[j], m.Rest.Rotations[j], Vector3.one) * Matrix4x4.TRS(Hinge, Quaternion.identity, Vector3.one);
            int ar = around > 0 ? Mathf.Max(12, around) : 28;
            float arc0 = sp.headCapLength + sp.spacing * j;
            EmitLoft(m, skull, head, PartMeshEmitter.Rigid(j), ar, true, arc0);
            EmitLoft(m, mandible, head, PartMeshEmitter.Rigid(bone), ar, false, arc0);
            if (teethPerSide > 0)
            {
                EmitTeeth(m, head, j, true, arc0);
                EmitTeeth(m, head, bone, false, arc0);
            }
        }

        // A closed loft through the keys: superellipse sections between top and bottom (the lower half squarer), the
        // palate (skull) or floor (mandible) in the mouth layer; brows and nostrils raised on the skull.
        void EmitLoft(PartMeshEmitter m, MawKey[] keys, Matrix4x4 frame, Vector4 skin, int ar, bool upper, float arc0)
        {
            float z0 = keys[0].z, z1 = keys[keys.Length - 1].z;
            var loft = new LimbLoft(ar);
            Vector3 X = frame.MultiplyVector(Vector3.right).normalized, Y = frame.MultiplyVector(Vector3.up).normalized;
            const int rings = 22;
            var zs = new float[rings + 1];
            float capB = 0.6f * HalfWidth(keys, z0), capF = 0.5f * HalfWidth(keys, z1);
            loft.Pole(frame.MultiplyPoint3x4(new Vector3(0f, 0.5f * (Top(keys, z0) + Bottom(keys, z0)), z0 - 0.35f * capB)), skin, arc0 + z1 - z0);
            for (int r = 0; r <= rings; r++)
            {
                float z = Mathf.Lerp(z0, z1, 0.5f - 0.5f * Mathf.Cos(Mathf.PI * r / rings));
                zs[r] = z;
                float a = HalfWidth(keys, z), top = Top(keys, z), bot = Bottom(keys, z);
                float yc = 0.5f * (top + bot), b = 0.5f * (top - bot);
                float browH = upper ? brow * Mathf.Exp(-Mathf.Pow((z - browAt) / Mathf.Max(1e-4f, 0.18f * (z1 - z0)), 2f)) : 0f;
                float nos = upper ? nostril * Mathf.Exp(-Mathf.Pow((z - (z1 - 0.12f * (z1 - z0))) / Mathf.Max(1e-4f, 0.08f * (z1 - z0)), 2f)) : 0f;
                // end rings shrink toward the caps
                float end = Mathf.Min(1f, Mathf.Min((z - z0) / Mathf.Max(1e-4f, 0.12f * (z1 - z0)) + 0.55f, (z1 - z) / Mathf.Max(1e-4f, 0.1f * (z1 - z0)) + 0.4f));
                end = Mathf.Sqrt(Mathf.Clamp01(end));
                float e = square;
                loft.Ring(frame.MultiplyPoint3x4(new Vector3(0f, yc, z)), X, Y, 0f, phi =>
                {
                    float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                    // the mouth side is flat (the palate under the skull, the floor on top of the mandible)
                    bool mouthSide = upper ? sn < 0f : sn > 0f;
                    float ex = mouthSide ? Mathf.Max(e, 4f) : e - 0.6f;
                    ex = Mathf.Max(2f, ex);
                    float rr = 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(cs) / a, ex) + Mathf.Pow(Mathf.Abs(sn) / b, ex), 1f / ex);
                    if (upper && sn > 0f)
                    {
                        // brow ridges either side of the midline, nostril bosses at the tip
                        float side = Mathf.Abs(cs);
                        rr += browH * Mathf.Exp(-Mathf.Pow((side - 0.55f) / 0.22f, 2f)) * sn;
                        rr += nos * Mathf.Exp(-Mathf.Pow((side - 0.35f) / 0.2f, 2f)) * sn;
                    }
                    return rr * end;
                }, skin, arc0 + z1 - z);
            }
            loft.Pole(frame.MultiplyPoint3x4(new Vector3(0f, 0.5f * (Top(keys, z1) + Bottom(keys, z1)), z1 + 0.3f * capF)), skin, arc0);
            int mouthPart = mouthLayer, skinPart = layer;
            loft.PartOf = (ring, k) =>
            {
                if (ring == 0 || ring == rings + 2) return skinPart;
                float phi = 2f * Mathf.PI * k / ar, sn = Mathf.Sin(phi);
                float z = zs[Mathf.Clamp(ring - 1, 0, rings)];
                bool inside = upper ? sn < -0.4f : sn > 0.4f;
                bool lips = z > z0 + 0.12f * (z1 - z0) && z < z1 - 0.08f * (z1 - z0);
                return inside && lips ? mouthPart : skinPart;
            };
            loft.Emit(m, layer);
        }

        // Tooth i's place along the lip (0 = the first, nearest the hinge) and its length.
        public float ToothZ(MawKey[] keys, int i)
        {
            float z0 = keys[0].z, z1 = keys[keys.Length - 1].z;
            float f = teethPerSide == 1 ? 0.5f : (float)i / (teethPerSide - 1);
            return Mathf.Lerp(z0 + teethFrom * (z1 - z0), z1 - 0.1f * (z1 - z0), f);
        }

        public float ToothLength(int i)
        {
            float f = teethPerSide == 1 ? 1f : (float)i / (teethPerSide - 1);
            return toothLength * Mathf.Lerp(1f, fang, Mathf.SmoothStep(0.55f, 1f, f));
        }

        void EmitTeeth(PartMeshEmitter m, Matrix4x4 frame, int bone, bool upper, float arc0)
        {
            MawKey[] keys = upper ? skull : mandible;
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < teethPerSide; i++)
                {
                    // upper teeth sit just outside the lower jaw's lip, lower ones just inside the upper's: they interlock
                    float z = ToothZ(keys, i) + (upper ? 0f : 0.5f * (ToothZ(keys, Mathf.Min(teethPerSide - 1, i + 1)) - ToothZ(keys, i)));
                    float hw = HalfWidth(keys, z) - toothInset;
                    float y = upper ? Bottom(keys, z) + 0.15f * toothLength : Top(keys, z) - 0.15f * toothLength;
                    Vector3 basePt = new Vector3(side * hw, y, z);
                    Vector3 dir = Quaternion.AngleAxis(upper ? -toothRakeDeg : toothRakeDeg, Vector3.right) * (upper ? Vector3.down : Vector3.up);
                    Quaternion q = Quaternion.LookRotation(frame.MultiplyVector(dir), frame.MultiplyVector(Vector3.forward));
                    float len = ToothLength(i);
                    m.Horn(frame.MultiplyPoint3x4(basePt), q, toothWidth * len, 0.02f * len, len, upper ? 12f : -12f, bone, toothLayer, 6, arc0, out _, out _);
                }
        }
    }

    public sealed class MawRuntime : IPartRuntime
    {
        readonly Maw maw;
        readonly CreatureSim sim;
        readonly int bone;
        readonly float scale, T;
        float clock;

        public float Angle { get; private set; }   // degrees, 0 = shut
        public int Bone => bone;
        public Maw Part => maw;

        public MawRuntime(Maw maw, CreatureSim sim, int bone)
        {
            this.maw = maw; this.sim = sim; this.bone = bone;
            scale = sim.SizeScale; T = sim.TimeScale;
            clock = (sim.Seed & 1023) * 0.618f;
        }

        public PartStage Stage => PartStage.Late;

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            clock += dt / T;
            float breath = maw.breathDeg * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * clock / maw.breathSeconds));
            float want = Mathf.Max(breath, Mathf.Clamp01(sim.Display) * maw.gapeDeg);
            float rate = maw.gapeDeg / (maw.gapeSeconds * T) * dt;
            Angle = Mathf.MoveTowards(Angle, want, Mathf.Max(rate, 1e-4f));
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            int j = maw.at.joint;
            Quaternion r = sim.Rotations[j];
            Vector3 p = sim.Joints[j] + r * (maw.Hinge * scale);
            dst[offset + bone] = Matrix4x4.TRS(p, r * Maw.Open(Angle), new Vector3(scale, scale, scale));
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            // the open jaw's tip may drop below the body's reach
            int j = maw.at.joint;
            Quaternion r = sim.Rotations[j];
            MawKey[] k = maw.mandible;
            Vector3 tip = new Vector3(0f, k[k.Length - 1].bottom - maw.toothLength * maw.fang, k[k.Length - 1].z);
            Vector3 p = sim.Joints[j] + r * ((maw.Hinge + Maw.Open(Angle) * tip) * scale);
            float pad = maw.toothLength * maw.fang * scale;
            lo = Vector3.Min(lo, p - new Vector3(pad, pad, pad)); hi = Vector3.Max(hi, p + new Vector3(pad, pad, pad));
        }

        public void DrawDebug(ICreatureDebugDraw d) { }
    }
}
