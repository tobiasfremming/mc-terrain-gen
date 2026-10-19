// Creature kit: LegFin - spiny fin frills along the back of a two-bone leg (Caraxes' legs; a Microraptor's leg
// wings): one fin on the thigh, one on the shin, each a double-sided ribbed membrane whose spines rake toward the
// foot, rooted inside the leg tube. Each fin rides a bone of its own on its leg bone, posed by LegFinRuntime:
//   on the ground   folded flat: the spines lie back along the leg (the fin's height shrinks to groundFold, the
//                   tips sheared up the leg, away from the foot), a low frill that never reaches the ground;
//   in the air      spread and turned about the leg toward the body's side, level: with the hind legs trailing
//                   behind the body (FlightSpec.legTuck*) the four fins are a rear lift surface.
// The blend follows the flight's air weight at the leg's hip (CreatureFlight.AirWeight: launch, landing and
// touchdown blend smoothly), the ground fold eases in. The fin bone = the leg bone x (a roll about the bone's
// axis) x (the fold: the spread axis scaled, sheared up the leg) - its rest (bind) pose is the leg bone itself
// (the spread fin on the posterior side), so the mesh is authored spread. The fold keeps the fin's plane, so its
// normals stay exact. Froude: lengths x s, the ease time x sqrt s.
// Kit-only part. Mesh: membrane (part 8), rigid to the fin bones. Runtime allocation-free after construction.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("LegFin", PartKind.Fin, Bones = "2 (thigh fin, shin fin)", Summary = "Spiny fin frills along the back of a Leg2Bone's thigh and shin (raked spines, scalloped edge): folded flat along the leg on the ground, spread and turned level toward the side in the air - with the hind legs trailing, a rear lift surface.",
          System = "LegFinRuntime (part runtime, stage Late) + LegFin.EmitMesh", MaterialLayers = new[] { 8 }, KitOnly = true,
          UseWhen = "finned or feathered legs: Caraxes-like leg frills, Microraptor leg wings, any flap that rides a limb and spreads in flight")]
    public sealed class LegFin : CreaturePart
    {
        [Param(ParamUnit.PartRef, "the Leg2Bone it rides on; a mirrored fin names the unmirrored id and takes .L / .R")] public string leg = "";
        [Param(ParamUnit.Metres, "thigh fin: tallest spine", Example = "dragon 0.07")] public float thighHeight;
        [Param(ParamUnit.Metres, "shin fin: tallest spine", Example = "dragon 0.09")] public float shinHeight;
        [Param(ParamUnit.Fraction, "the fins' root along each bone: from this fraction ..", Min = 0, Max = 0.9f, Default = "0.12")] public float rootStart = 0.12f;
        [Param(ParamUnit.Fraction, ".. to this fraction", Min = 0.1f, Max = 1, Default = "0.92")] public float rootEnd = 0.92f;
        [Param(ParamUnit.Count, "spines per fin (scallops between them along the free edge)", Min = 1, Max = 16, Default = "5")] public int spines = 5;
        [Param(ParamUnit.Fraction, "valley depth between spines, fraction of the height", Min = 0, Max = 0.9f, Default = "0.45")] public float spineDepth = 0.45f;
        [Param(ParamUnit.Degrees, "spread: the spines rake toward the foot by this much", Min = 0, Max = 70, Default = "35")] public float rakeDeg = 35f;
        [Param(ParamUnit.Fraction, "on the ground the fin folds to this fraction of its height (the spines laid back along the leg)", Min = 0.1f, Max = 1, Default = "0.3")] public float groundFold = 0.3f;
        [Param(ParamUnit.Degrees, "in the air the fin turns below the level by this much (droop; negative: above)", Min = -45, Max = 45)] public float airDroopDeg;
        [Param(ParamUnit.Seconds, "the ground fold eases in over this time", Min = 0.02f, Max = 2, Default = "0.3")] public float foldSeconds = 0.3f;

        public const int Fins = 2;
        const int kRows = 5, kColumnsPerSpine = 4;
        const float kSheetOffset = 1e-4f;

        public override int BoneCount(in SpeciesCtx c) => Fins;
        public override void CollectReferences(List<string> into) => into.Add(LegId);

        // The leg's id after mirroring (a mirrored fin names the unmirrored leg).
        public string LegId => leg == null ? "" : at.side == 0 || leg.EndsWith(".L") || leg.EndsWith(".R") ? leg : leg + (at.side < 0 ? ".L" : ".R");

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a LegFin needs a SpineBody";
            if (!(c.Find(LegId) is Leg2Bone)) return $"{id}: leg '{LegId}' must name a Leg2Bone";
            if (thighHeight < 0f || shinHeight < 0f || thighHeight + shinHeight <= 0f) return $"{id}: heights must be >= 0 and not both 0";
            if (!(rootStart >= 0f && rootEnd <= 1f && rootEnd - rootStart >= 0.1f)) return $"{id}: root must span at least 0.1 of the bone inside 0..1";
            if (spines < 1 || spines > 16) return $"{id}: spines must be 1..16";
            if (spineDepth < 0f || spineDepth > 0.9f) return $"{id}: spineDepth must be in 0..0.9";
            if (rakeDeg < 0f || rakeDeg > 70f) return $"{id}: rakeDeg must be in 0..70";
            if (!(groundFold >= 0.1f && groundFold <= 1f)) return $"{id}: groundFold must be in 0.1..1";
            if (!(foldSeconds >= 0.02f)) return $"{id}: foldSeconds must be >= 0.02";
            return null;
        }

        // The leg's index among the species' limbs (sp.legs), or -1.
        public int LegIndex(CreatureSpecies runtime)
        {
            IReadOnlyList<CreaturePart> parts = KitView.Parts(runtime);
            int[] ord = KitView.Ordinals(parts);
            string want = LegId;
            for (int i = 0; i < parts.Count; i++) if (parts[i] is Leg2Bone && parts[i].id == want) return ord[i];
            return -1;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
        {
            if (c.Sim == null) return null;
            int l = LegIndex(c.Species);
            return l < 0 ? null : new LegFinRuntime(this, c.Sim, l, c.Species.Layout.Start(c.Index), c.Size);
        }

        // ------------------------------------------------------------ the fin's shape (bone-local, scale 1)

        public float Height(int fin) => fin == 0 ? thighHeight : shinHeight;
        public float BoneLength(LegSpec s, int fin) => fin == 0 ? s.upperLength : s.lowerLength;

        // Height profile along the root (fraction fr): spines with scalloped valleys, rounded ends.
        public float Profile(float fr)
        {
            float tuft = 1f - Mathf.Clamp(spineDepth, 0f, 0.9f) * Sq(Mathf.Sin(Mathf.PI * spines * fr));
            float end = Mathf.Min(Smooth(fr / 0.18f), Smooth((1f - fr) / 0.16f));
            // taller toward the foot end of each fin (the frill sweeps out behind the leg)
            return tuft * (0.25f + 0.75f * end) * (0.7f + 0.3f * fr);
        }

        // A fin point in its bone's local frame (+Z along the bone, +Y the pole: the fin grows toward -Y, the back of
        // the leg, its spines raked toward +Z; +X its normal): root fraction fr, height fraction v.
        public Vector3 Local(LegSpec s, int fin, float fr, float v)
        {
            float len = BoneLength(s, fin);
            float z = Mathf.Lerp(rootStart, rootEnd, fr) * len;
            float h = Height(fin) * Profile(fr);
            float rake = rakeDeg * Mathf.Deg2Rad;
            // the spine curves a little toward the foot as it rises (a raked, swept spine), a slight camber across
            float bend = rake * (0.6f + 0.4f * v);
            return new Vector3(0.04f * h * Mathf.Sin(Mathf.PI * v), -Mathf.Cos(bend) * h * v, z + Mathf.Sin(bend) * h * v);
        }

        static float Sq(float x) => x * x;
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        // ------------------------------------------------------------ mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            CreatureSpecies sp = m.Species;
            int l = LegIndex(sp);
            if (l < 0) return;
            LegSpec s = sp.legs[l];
            int start = sp.Layout.Start(c.Index);
            int cols = Mathf.Max(1, spines) * kColumnsPerSpine, rows = kRows;
            for (int fin = 0; fin < Fins; fin++)
            {
                if (!(Height(fin) > 0f)) continue;
                int finBone = start + fin;
                Matrix4x4 bone = m.Bones[finBone];   // the fin bone's rest (bind) pose: its leg bone's, unfolded
                var p = new Vector3[(cols + 1) * (rows + 1)];
                for (int ci = 0; ci <= cols; ci++)
                    for (int r = 0; r <= rows; r++)
                        p[ci * (rows + 1) + r] = bone.MultiplyPoint3x4(Local(s, fin, ci / (float)cols, r / (float)rows));
                Vector3 side = bone.MultiplyVector(Vector3.right).normalized;
                float arc = 0f;
                for (int sheet = 0; sheet < 2; sheet++)
                {
                    float sign = sheet == 0 ? 1f : -1f;
                    int first = m.VertexCount;
                    arc = 0f;
                    for (int ci = 0; ci <= cols; ci++)
                    {
                        if (ci > 0) arc += (p[ci * (rows + 1)] - p[(ci - 1) * (rows + 1)]).magnitude;
                        for (int r = 0; r <= rows; r++)
                        {
                            int k = ci * (rows + 1) + r;
                            Vector3 dc = p[Mathf.Min(ci + 1, cols) * (rows + 1) + r] - p[Mathf.Max(ci - 1, 0) * (rows + 1) + r];
                            Vector3 dr = p[ci * (rows + 1) + Mathf.Min(r + 1, rows)] - p[ci * (rows + 1) + Mathf.Max(r - 1, 0)];
                            Vector3 nn = PartMeshEmitter.Unit(Vector3.Cross(dc, dr), side);
                            if (Vector3.Dot(nn, side) < 0f) nn = -nn;
                            nn *= sign;
                            float v = r / (float)rows;
                            // uv0 = (arc along the root m, spine coordinate: a vein at each spine), uv1.y = sheet x (0.1 + 0.9 v)
                            m.Vertex(p[k] + nn * kSheetOffset, nn, arc, ci / (float)cols * spines / 5f, PartMeshEmitter.PartMembrane,
                                     sign * (0.1f + 0.9f * v), PartMeshEmitter.Rigid(finBone));
                        }
                    }
                    for (int ci = 0; ci < cols; ci++)
                        for (int r = 0; r < rows; r++)
                        {
                            int i00 = first + ci * (rows + 1) + r, i10 = i00 + rows + 1, i01 = i00 + 1, i11 = i10 + 1;
                            Vector3 fa = m.Position(i00), fb = m.Position(i10), fc = m.Position(i11);
                            Vector3 face = Vector3.Cross(fb - fa, fc - fa);
                            if (face.sqrMagnitude < 1e-20f) { fc = m.Position(i01); face = Vector3.Cross(fc - fa, fb - fa) * -1f; }
                            bool flip = Vector3.Dot(face, sign * side) < 0f;
                            m.Tri(i00, i10, i11, flip);
                            m.Tri(i00, i11, i01, flip);
                        }
                }
            }
        }
    }

    // One LegFin of one individual: eases the ground fold, turns the spread fins toward the side in the air, and
    // writes the two fin bones on top of the leg's (stage Late; WriteBones runs after the legs' and the flight's).
    public sealed class LegFinRuntime : IPartRuntime
    {
        readonly LegFin f;
        readonly CreatureSim sim;
        readonly int leg, start, attach, side;
        readonly int[] legBone = new int[LegFin.Fins];
        readonly float scale, T;
        const float kFoldShear = 1.2f;   // folded: the spines' raked tips (toward the foot) turn to point up the leg
        float fold;          // 0 spread (the bind) .. 1 folded on the ground; eased
        bool stepped;        // the rest pose (no step yet) is the bind: spread, unturned

        public LegFin Part => f;
        public float Fold => fold;
        public int FirstBone => start;

        public LegFinRuntime(LegFin fin, CreatureSim sim, int leg, int firstBone, SizeCtx size)
        {
            f = fin; this.sim = sim; this.leg = leg; start = firstBone;
            scale = size.L; T = size.T;
            CreatureSpecies sp = sim.Species;
            attach = sp.legs[leg].attachJoint;
            side = sp.legs[leg].side < 0 ? -1 : 1;
            for (int k = 0; k < LegFin.Fins; k++) legBone[k] = sp.LegBone(leg, k);
        }

        public PartStage Stage => PartStage.Late;

        // 0 on the ground .. 1 in the air: the flight's own blend at the hip (none without flight).
        public float Air
        {
            get
            {
                CreatureFlight fl = sim.Flight;
                if (fl == null || fl.Mode == FlightMode.Ground) return 0f;
                return Mathf.Clamp01(fl.AirWeight[attach]);
            }
        }

        public void Step(in StepCtx c, float dt)
        {
            stepped = true;
            float target = 1f - Air;
            float k = 1f - Mathf.Exp(-dt / (f.foldSeconds * T));
            fold += (target - fold) * k;
        }

        // The fin bone k from its leg bone's world matrix (the leg's, written before this runtime's).
        public Matrix4x4 FinBone(Matrix4x4 legWorld, int k)
        {
            if (!stepped) return legWorld;
            float air = Air;
            // the roll about the bone axis that turns the fin's spread direction (-Y) toward the side, level
            float roll = 0f;
            if (air > 0f)
            {
                Vector3 axis = legWorld.MultiplyVector(Vector3.forward), yb = legWorld.MultiplyVector(Vector3.up), xb = legWorld.MultiplyVector(Vector3.right);
                Quaternion ar = sim.Rotations[attach];
                Vector3 want = ar * (Quaternion.AngleAxis(side * f.airDroopDeg, Vector3.forward) * new Vector3(side, 0f, 0f));
                float tx = Vector3.Dot(want, xb), ty = Vector3.Dot(want, yb);
                if (tx * tx + ty * ty > 1e-10f * Mathf.Max(1e-12f, axis.sqrMagnitude))
                    roll = air * Mathf.Atan2(tx, -ty);    // the angle from local -Y to (tx, ty) about +Z
            }
            // the fold: the spread axis (local Y) scaled down and the spines laid back up the leg (sheared toward the
            // bone's root, -Z: away from the foot and the ground), in the fin's plane
            float sy = Mathf.Lerp(1f, f.groundFold, fold);
            float shear = fold * kFoldShear;
            var fm = Matrix4x4.identity;
            fm.m11 = sy; fm.m21 = shear;    // z' = z + shear * y (y < 0 behind the leg: the tips move up the leg)
            return legWorld * Matrix4x4.TRS(Vector3.zero, Quaternion.AngleAxis(roll * Mathf.Rad2Deg, Vector3.forward), Vector3.one) * fm;
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            for (int k = 0; k < LegFin.Fins; k++) dst[offset + start + k] = FinBone(dst[offset + legBone[k]], k);
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            ref LegState ls = ref sim.Legs[leg];
            float r = Mathf.Max(f.thighHeight, f.shinHeight) * scale;
            Grow(ref lo, ref hi, ls.shoulder, r); Grow(ref lo, ref hi, ls.elbow, r); Grow(ref lo, ref hi, ls.foot, r);
        }

        static void Grow(ref Vector3 lo, ref Vector3 hi, Vector3 p, float r)
        {
            lo = Vector3.Min(lo, p - new Vector3(r, r, r));
            hi = Vector3.Max(hi, p + new Vector3(r, r, r));
        }

        public void DrawDebug(ICreatureDebugDraw d)
        {
            ref LegState ls = ref sim.Legs[leg];
            var c = new Color(1f, 0.45f, 0.2f, 1f);
            d.Line(ls.shoulder, ls.elbow, c); d.Line(ls.elbow, ls.foot, c);
        }
    }
}
