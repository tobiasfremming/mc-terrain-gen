// Creature kit: LegFin - leg wings along the back of a two-bone leg (a Microraptor's hind wings, Caraxes' leg
// membranes): one wing on the thigh, one on the shin, each a fan of finger-like ribs rooted along the bone and raked
// toward the foot, a double-sided membrane between neighbouring ribs with a scalloped trailing edge (the main bat
// wings' look: membrane shading, a vein at each rib, a darker rim). Each wing rides a bone of its own on its leg bone,
// posed by LegFinRuntime:
//   on the ground   folded flat along the leg: an affine fold in the wing's plane presses the spread toward the leg
//                   (groundFold) and lays the ribs back up it (foldShear), the whole folded wing kept inside the bone's
//                   span (foldSpan), so it never hangs below the leg or reaches the ground;
//   in the air      spread and turned about the leg toward the body's side: with the hind legs trailing behind and
//                   out (FlightSpec.legTuck*) the two pairs are a rear wing. Their angle eases between a droop in
//                   powered flight (airDroopDeg) and a glide's (glideDroopDeg), the two sides tilt against each other
//                   in a bank (bankDeg), and they flex with the wingbeat on a lagging spring (flapDeg, flutterHz).
// The blend follows the flight's air weight at the leg's hip (CreatureFlight.AirWeight: launch, landing and
// touchdown blend smoothly), the ground fold eases in. The wing bone = the leg bone x (a roll about the bone's axis)
// x (the fold, affine in the bone's Y-Z plane) - its rest (bind) pose is the leg bone itself (the spread wing on the
// posterior side), so the mesh is authored spread. The fold leaves the membrane's plane (and +X, its normal) alone and
// keeps its determinant positive: nothing inverts, and the shader's cofactor skinning keeps the normals exact.
// Froude: lengths x s, times x sqrt s.
// Kit-only part. Mesh: membrane (part 8) and ribs (part 1), rigid to the wing bones. Runtime allocation-free after
// construction.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("LegFin", PartKind.Fin, Bones = "2 (thigh wing, shin wing)", Summary = "Leg wings along the back of a Leg2Bone's thigh and shin: a fan of finger-like ribs raked toward the foot with a scalloped bat-wing membrane between them - folded flat along the leg on the ground, spread toward the side in the air (with the hind legs trailing, a rear wing pair), flexing with the wingbeat and tilting in glides and turns.",
          System = "LegFinRuntime (part runtime, stage Late) + LegFin.EmitMesh", MaterialLayers = new[] { 1, 8 }, KitOnly = true,
          UseWhen = "winged or finned legs: Microraptor hind wings, Caraxes-like leg membranes, any ribbed flap that rides a limb and spreads in flight")]
    public sealed class LegFin : CreaturePart
    {
        [Param(ParamUnit.PartRef, "the Leg2Bone it rides on; a mirrored fin names the unmirrored id and takes .L / .R")] public string leg = "";
        [Param(ParamUnit.Metres, "thigh wing: its longest rib", Example = "dragon 0.3")] public float thighHeight;
        [Param(ParamUnit.Metres, "shin wing: its longest rib", Example = "dragon 0.42")] public float shinHeight;
        [Param(ParamUnit.Fraction, "the ribs' roots along each bone: from this fraction ..", Min = 0, Max = 0.9f, Default = "0.12")] public float rootStart = 0.12f;
        [Param(ParamUnit.Fraction, ".. to this fraction", Min = 0.1f, Max = 1, Default = "0.92")] public float rootEnd = 0.92f;
        [Param(ParamUnit.Count, "ribs per wing (a membrane zone between each pair)", Min = 2, Max = 8, Default = "5")] public int spines = 5;
        [Param(ParamUnit.Fraction, "the trailing edge arcs in between rib tips by this fraction of their spacing (the bat-wing scallop)", Min = 0, Max = 0.6f, Default = "0.3")] public float scallop = 0.3f;
        [Param(ParamUnit.Fraction, "the first rib's length, fraction of the longest (the ribs lengthen toward the foot)", Min = 0.2f, Max = 1, Default = "0.55")] public float firstRib = 0.55f;
        [Param(ParamUnit.Degrees, "the ribs' mean rake toward the foot", Min = 0, Max = 70, Default = "35")] public float rakeDeg = 35f;
        [Param(ParamUnit.Degrees, "the fan: the first rib rakes this much less than the last", Min = 0, Max = 60, Default = "30")] public float fanDeg = 30f;
        [Param(ParamUnit.Degrees, "each rib curves toward the foot by this much from root to tip", Min = 0, Max = 40, Default = "12")] public float ribCurveDeg = 12f;
        [Param(ParamUnit.Metres, "rib radius at the root (finger-like, tapering, knuckled); 0 = no rib tubes", Min = 0, Example = "dragon 0.006")] public float ribRadius;
        [Param(ParamUnit.Fraction, "the membrane bellies between the ribs by this fraction of their spacing", Min = 0, Max = 0.2f, Default = "0")] public float billow;
        [Param(ParamUnit.Fraction, "on the ground the spread presses to this fraction toward the leg", Min = 0.05f, Max = 1, Default = "0.3")] public float groundFold = 0.3f;
        [Param(ParamUnit.Fraction, "folded: the ribs lay back up the leg by this much per unit of spread", Min = 0, Max = 3, Default = "1.2")] public float foldShear = 1.2f;
        [Param(ParamUnit.Fraction, "folded: the whole wing fits inside this fraction of the bone's span (centred on the roots)", Min = 0.2f, Max = 1.5f, Default = "1")] public float foldSpan = 1f;
        [Param(ParamUnit.Degrees, "powered flight: the wings turn below the level by this much (droop; negative: above)", Min = -45, Max = 45)] public float airDroopDeg;
        [Param(ParamUnit.Degrees, "gliding: the same, eased in while the flight glides", Min = -45, Max = 45)] public float glideDroopDeg;
        [Param(ParamUnit.Degrees, "flex with the wingbeat: the shin wing follows the main wings' stroke (up with it, lagging) by up to this much (the thigh 0.6x)", Min = 0, Max = 30)] public float flapDeg;
        [Param(ParamUnit.Hz, "the flex's spring: its natural frequency (x 1/sqrt s), lightly damped - the lag and the flutter", Min = 0.5f, Max = 20, Default = "4")] public float flutterHz = 4f;
        [Param(ParamUnit.Degrees, "in a full bank the two sides tilt against each other by this much (the inner wing down)", Min = 0, Max = 30)] public float bankDeg;
        [Param(ParamUnit.Seconds, "the ground fold and the glide / bank angles ease over this time", Min = 0.02f, Max = 2, Default = "0.3")] public float foldSeconds = 0.3f;

        public const int Fins = 2;
        const int kRows = 6, kColumnsPerZone = 4, kRibSegments = 6;
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
            if (spines < 2 || spines > 8) return $"{id}: spines (ribs) must be 2..8";
            if (!(scallop >= 0f && scallop <= 0.6f)) return $"{id}: scallop must be in 0..0.6";
            if (!(firstRib >= 0.2f && firstRib <= 1f)) return $"{id}: firstRib must be in 0.2..1";
            if (rakeDeg < 0f || rakeDeg > 70f) return $"{id}: rakeDeg must be in 0..70";
            if (fanDeg < 0f || fanDeg > 60f || rakeDeg - 0.5f * fanDeg < -10f || rakeDeg + 0.5f * fanDeg + ribCurveDeg > 85f) return $"{id}: the fan (rakeDeg +- fanDeg / 2, + ribCurveDeg) must stay within -10..85 deg";
            if (ribRadius < 0f) return $"{id}: ribRadius must be >= 0";
            if (!(billow >= 0f && billow <= 0.2f)) return $"{id}: billow must be in 0..0.2";
            if (!(groundFold >= 0.05f && groundFold <= 1f)) return $"{id}: groundFold must be in 0.05..1";
            if (!(foldShear >= 0f && foldShear <= 3f)) return $"{id}: foldShear must be in 0..3";
            if (!(foldSpan >= 0.2f && foldSpan <= 1.5f)) return $"{id}: foldSpan must be in 0.2..1.5";
            if (!(flutterHz >= 0.5f)) return $"{id}: flutterHz must be >= 0.5";
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

        // ------------------------------------------------------------ the wing's shape (bone-local, scale 1)

        public float Height(int fin) => fin == 0 ? thighHeight : shinHeight;
        public float BoneLength(LegSpec s, int fin) => fin == 0 ? s.upperLength : s.lowerLength;
        public int Ribs => Mathf.Clamp(spines, 2, 8);

        // Rib i's root on the bone (+Z along it, from the joint toward the foot).
        public Vector3 RibRoot(LegSpec s, int fin, int i) => new Vector3(0f, 0f, Mathf.Lerp(rootStart, rootEnd, i / (Ribs - 1f)) * BoneLength(s, fin));

        // A point on rib i at v (0 root .. 1 tip) in its bone's local frame (+Y the pole: the ribs grow toward -Y, the
        // back of the leg, raked toward +Z, the foot; +X the wing's normal): a curved finger, its rake growing along it.
        public Vector3 RibPoint(LegSpec s, int fin, int i, float v)
        {
            float t = i / (Ribs - 1f);
            float len = Height(fin) * Mathf.Lerp(firstRib, 1f, Mathf.Pow(t, 0.8f));
            float r0 = (rakeDeg + fanDeg * (t - 0.5f)) * Mathf.Deg2Rad, c = ribCurveDeg * Mathf.Deg2Rad;
            // the arc of constant curvature from angle r0 to r0 + c v (closed form; straight when c = 0)
            float y, z;
            if (c < 1e-4f) { y = -Mathf.Cos(r0) * len * v; z = Mathf.Sin(r0) * len * v; }
            else
            {
                float a = r0 + c * v;
                y = -(Mathf.Sin(a) - Mathf.Sin(r0)) * len / c;
                z = -(Mathf.Cos(a) - Mathf.Cos(r0)) * len / c;
            }
            return RibRoot(s, fin, i) + new Vector3(0f, y, z);
        }

        // Rib i's tangent at v (unit).
        public Vector3 RibTangent(LegSpec s, int fin, int i, float v)
        {
            float t = i / (Ribs - 1f);
            float a = (rakeDeg + fanDeg * (t - 0.5f)) * Mathf.Deg2Rad + ribCurveDeg * Mathf.Deg2Rad * v;
            return new Vector3(0f, -Mathf.Cos(a), Mathf.Sin(a));
        }

        // The membrane between ribs i and i + 1 at (u across 0..1, v along 0..1): between the two ribs, the trailing
        // edge arced in toward the roots by scallop x the tips' spacing, bellied along +X by billow x the spacing.
        public Vector3 MembranePoint(LegSpec s, int fin, int i, float u, float v)
        {
            Vector3 a = RibPoint(s, fin, i, v), b = RibPoint(s, fin, i + 1, v);
            Vector3 ta = RibPoint(s, fin, i, 1f), tb = RibPoint(s, fin, i + 1, 1f);
            Vector3 ra = RibRoot(s, fin, i), rb = RibRoot(s, fin, i + 1);
            float chord = (tb - ta).magnitude;
            Vector3 inward = PartMeshEmitter.Unit(0.5f * (ra + rb) - 0.5f * (ta + tb), Vector3.up);
            float bump = Mathf.Sin(Mathf.PI * u);
            return Vector3.Lerp(a, b, u) + inward * (scallop * chord * bump * v * v) + Vector3.right * (billow * chord * bump * Mathf.Sin(Mathf.PI * v));
        }

        // The fold's affine map in the bone's Y-Z plane (scale 1): y' = sy y, z' = a (z + shear y) + t. sy = groundFold;
        // shear = foldShear (the ribs laid back up the leg); a and t fit the sheared wing (roots and rib tips) into
        // foldSpan of the roots' span around its centre (only pressed, never stretched: a <= 1).
        public void FoldMap(LegSpec s, int fin, out float sy, out float shear, out float a, out float t)
        {
            sy = groundFold; shear = foldShear;
            float lo = float.MaxValue, hi = float.MinValue;
            int n = Ribs;
            for (int i = 0; i < n; i++)
                for (int k = 0; k <= 4; k++)
                {
                    Vector3 p = RibPoint(s, fin, i, k / 4f);
                    float z = p.z + shear * p.y;
                    lo = Mathf.Min(lo, z); hi = Mathf.Max(hi, z);
                }
            float r0 = RibRoot(s, fin, 0).z, r1 = RibRoot(s, fin, n - 1).z, mid = 0.5f * (r0 + r1), half = 0.5f * foldSpan * (r1 - r0);
            a = hi - lo > 1e-6f ? Mathf.Min(1f, 2f * half / (hi - lo)) : 1f;
            t = mid - a * 0.5f * (lo + hi);
        }

        // ------------------------------------------------------------ mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            CreatureSpecies sp = m.Species;
            int l = LegIndex(sp);
            if (l < 0) return;
            LegSpec s = sp.legs[l];
            int start = sp.Layout.Start(c.Index);
            int n = Ribs, zones = n - 1;
            int cols = zones * kColumnsPerZone, rows = kRows;
            var p = new Vector3[(cols + 1) * (rows + 1)];
            for (int fin = 0; fin < Fins; fin++)
            {
                if (!(Height(fin) > 0f)) continue;
                int finBone = start + fin;
                Matrix4x4 bone = m.Bones[finBone];   // the wing bone's rest (bind) pose: its leg bone's, spread
                Vector3 side = bone.MultiplyVector(Vector3.right).normalized;
                // the membrane: zone by zone, columns shared at the ribs
                for (int ci = 0; ci <= cols; ci++)
                {
                    int zi = Mathf.Min(ci / kColumnsPerZone, zones - 1);
                    float u = (ci - zi * kColumnsPerZone) / (float)kColumnsPerZone;
                    for (int r = 0; r <= rows; r++)
                        p[ci * (rows + 1) + r] = bone.MultiplyPoint3x4(MembranePoint(s, fin, zi, u, r / (float)rows));
                }
                for (int sheet = 0; sheet < 2; sheet++)
                {
                    float sign = sheet == 0 ? 1f : -1f;
                    int first = m.VertexCount;
                    for (int ci = 0; ci <= cols; ci++)
                    {
                        for (int r = 0; r <= rows; r++)
                        {
                            int k = ci * (rows + 1) + r;
                            Vector3 dc = p[Mathf.Min(ci + 1, cols) * (rows + 1) + r] - p[Mathf.Max(ci - 1, 0) * (rows + 1) + r];
                            Vector3 dr = p[ci * (rows + 1) + Mathf.Min(r + 1, rows)] - p[ci * (rows + 1) + Mathf.Max(r - 1, 0)];
                            Vector3 nn = PartMeshEmitter.Unit(Vector3.Cross(dc, dr), side);
                            if (Vector3.Dot(nn, side) < 0f) nn = -nn;
                            nn *= sign;
                            float v = r / (float)rows;
                            // uv0 = (distance from the root along the rib m, zone coordinate / 5: a main vein at each
                            // rib, fine veins between), uv1.y = sheet x (0.1 + 0.9 v): the rim at the trailing edge
                            float dist = v * Height(fin);
                            m.Vertex(p[k] + nn * kSheetOffset, nn, dist, ci / (float)kColumnsPerZone / 5f, PartMeshEmitter.PartMembrane,
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
                // the ribs: finger-like tapering bones with two knuckles, rooted inside the leg
                if (ribRadius > 0f)
                {
                    var q = new List<PartMeshEmitter.TubeSample>();
                    Vector4 rig = PartMeshEmitter.Rigid(finBone);
                    for (int i = 0; i < n; i++)
                    {
                        q.Clear();
                        float len = (RibPoint(s, fin, i, 1f) - RibRoot(s, fin, i)).magnitude;
                        Vector3 t0 = bone.MultiplyVector(RibTangent(s, fin, i, 0f)).normalized;
                        Vector3 root = bone.MultiplyPoint3x4(RibRoot(s, fin, i));
                        q.Add(PartMeshEmitter.Sample(root - t0 * ribRadius, t0, 0f, 0f, 0f, rig, true));
                        for (int k = 0; k <= kRibSegments; k++)
                        {
                            float v = k / (float)kRibSegments * 0.97f;
                            Vector3 tk = bone.MultiplyVector(RibTangent(s, fin, i, v)).normalized;
                            q.Add(PartMeshEmitter.Sample(bone.MultiplyPoint3x4(RibPoint(s, fin, i, v)), tk, RibRadius(v), 0f, ribRadius + v * len, rig));
                        }
                        Vector3 t1 = bone.MultiplyVector(RibTangent(s, fin, i, 1f)).normalized;
                        q.Add(PartMeshEmitter.Sample(bone.MultiplyPoint3x4(RibPoint(s, fin, i, 1f)), t1, 0f, 0f, ribRadius + len, rig, true));
                        m.Tube(q, side, 6, PartMeshEmitter.PartLeg);
                    }
                }
            }
        }

        // The rib's radius at v: a taper to a fine tip with a knuckle at a third and two thirds.
        float RibRadius(float v)
        {
            float g1 = (v - 0.34f) / 0.07f, g2 = (v - 0.66f) / 0.06f;
            return ribRadius * (1f - 0.8f * v) * (1f + 0.3f * Mathf.Exp(-g1 * g1) + 0.25f * Mathf.Exp(-g2 * g2));
        }
    }

    // One LegFin of one individual: eases the ground fold, turns the spread wings toward the side in the air (eased
    // droop for powered flight and glides, a bank's differential tilt, the wingbeat's lagging flex) and writes the two
    // wing bones on top of the leg's (stage Late; WriteBones runs after the legs' and the flight's).
    public sealed class LegFinRuntime : IPartRuntime
    {
        readonly LegFin f;
        readonly CreatureSim sim;
        readonly int leg, start, attach, side;
        readonly int[] legBone = new int[LegFin.Fins];
        readonly float scale, T;
        readonly Matrix4x4[] foldMap = new Matrix4x4[LegFin.Fins];   // the full fold, bone-local (scale 1 lengths x scale)
        float fold;          // 0 spread (the bind) .. 1 folded on the ground; eased
        float glide;         // 0 powered .. 1 gliding; eased
        float bank;          // -1..1 of the flight's max bank; eased
        float flex, flexRate; // the wingbeat flex spring, deg
        bool stepped;        // the rest pose (no step yet) is the bind: spread, unturned

        public LegFin Part => f;
        public float Fold => fold;
        public float Flex => flex;
        public int FirstBone => start;

        public LegFinRuntime(LegFin fin, CreatureSim sim, int leg, int firstBone, SizeCtx size)
        {
            f = fin; this.sim = sim; this.leg = leg; start = firstBone;
            scale = size.L; T = size.T;
            CreatureSpecies sp = sim.Species;
            LegSpec s = sp.legs[leg];
            attach = s.attachJoint;
            side = s.side < 0 ? -1 : 1;
            for (int k = 0; k < LegFin.Fins; k++)
            {
                legBone[k] = sp.LegBone(leg, k);
                // the bone matrices carry the size as a uniform scale, so the bone-local map stays at scale 1
                f.FoldMap(s, k, out float sy, out float shear, out float a, out float t);
                var m = Matrix4x4.identity;
                m.m11 = sy;
                m.m22 = a; m.m21 = a * shear; m.m23 = t;
                foldMap[k] = m;
            }
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
            float k = 1f - Mathf.Exp(-dt / (f.foldSeconds * T));
            fold += (1f - Air - fold) * k;
            CreatureFlight fl = sim.Flight;
            bool air = fl != null && fl.Mode != FlightMode.Ground;
            float glideTarget = air && fl.Model.Gliding ? 1f : 0f;
            glide += (glideTarget - glide) * k;
            float maxBank = air ? Mathf.Max(1e-3f, fl.Model.Spec.maxBankDeg * Mathf.Deg2Rad) : 1f;
            float bankTarget = air ? Mathf.Clamp(fl.Model.Bank / maxBank, -1f, 1f) : 0f;
            bank += (bankTarget - bank) * k;
            // the wingbeat flex: a lightly damped spring after the main wing on this side (its stroke angle over its
            // band), sub-stepped so a coarse frame stays stable
            float target = 0f;
            if (air && f.flapDeg > 0f)
            {
                CreatureWings cw = fl.Wings;
                for (int w = 0; w < cw.Count; w++)
                {
                    if (cw.Side(w) != side) continue;
                    float ang = cw.State[w].angle, band = ang >= 0f ? cw.BandUpAngle(w) : cw.BandDownAngle(w);
                    target = -f.flapDeg * Mathf.Clamp(ang / Mathf.Max(1e-3f, band), -1f, 1f) * (1f - glide);
                    break;
                }
            }
            float om = 2f * Mathf.PI * f.flutterHz / T, zeta = 0.35f;
            int subs = Mathf.Max(1, Mathf.CeilToInt(dt * om / 0.5f));
            float h = dt / subs;
            for (int i = 0; i < subs; i++)
            {
                flexRate += (om * om * (target - flex) - 2f * zeta * om * flexRate) * h;
                flex += flexRate * h;
            }
        }

        // The wing bone k from its leg bone's world matrix (the leg's, written before this runtime's).
        public Matrix4x4 FinBone(Matrix4x4 legWorld, int k)
        {
            if (!stepped) return legWorld;
            float air = Air;
            // the roll about the bone axis that turns the wing's spread direction (-Y) toward the side, at its droop
            float roll = 0f;
            if (air > 0f)
            {
                Vector3 axis = legWorld.MultiplyVector(Vector3.forward), yb = legWorld.MultiplyVector(Vector3.up), xb = legWorld.MultiplyVector(Vector3.right);
                Quaternion ar = sim.Rotations[attach];
                float droop = Mathf.Lerp(f.airDroopDeg, f.glideDroopDeg, glide)
                              + (k == 0 ? 0.6f : 1f) * flex      // the wingbeat flex, more on the outer (shin) wing
                              + side * bank * f.bankDeg;         // a bank: one side down, the other up
                Vector3 want = ar * (Quaternion.AngleAxis(side * droop, Vector3.forward) * new Vector3(side, 0f, 0f));
                float tx = Vector3.Dot(want, xb), ty = Vector3.Dot(want, yb);
                if (tx * tx + ty * ty > 1e-10f * Mathf.Max(1e-12f, axis.sqrMagnitude))
                    roll = air * Mathf.Atan2(tx, -ty);    // the angle from local -Y to (tx, ty) about +Z
            }
            // the fold: the identity blended to the fold map (affine in Y-Z; X, the wing's normal, untouched)
            Matrix4x4 fm = Matrix4x4.identity, fo = foldMap[k];
            if (fold > 0f)
            {
                fm.m11 = Mathf.Lerp(1f, fo.m11, fold);
                fm.m21 = fold * fo.m21;
                fm.m22 = Mathf.Lerp(1f, fo.m22, fold);
                fm.m23 = fold * fo.m23;
            }
            return legWorld * Matrix4x4.TRS(Vector3.zero, Quaternion.AngleAxis(roll * Mathf.Rad2Deg, Vector3.forward), Vector3.one) * fm;
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            for (int k = 0; k < LegFin.Fins; k++) dst[offset + start + k] = FinBone(dst[offset + legBone[k]], k);
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            ref LegState ls = ref sim.Legs[leg];
            float r = 1.1f * Mathf.Max(f.thighHeight, f.shinHeight) * scale;
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
