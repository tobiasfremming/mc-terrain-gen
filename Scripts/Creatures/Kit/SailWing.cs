// Creature kit: SailWing - the Apex's wing: a long, narrow sail on a heavy arm. It flies, folds and flexes exactly as a
// PteroWing does (its bones, its runtime: CreatureFlight + CreatureWings beat it, PteroWingRuntime lags the spar's
// segments and the sail's chord rays as chains - PteroWing.chain), but it is drawn its own way, not as a pterosaur's:
//   arm      a muscular humerus with a shoulder mass (armBulk), a ball elbow, one thick forearm swelling below the
//            elbow and running lean to the knuckle - no crest, no two-bone forearm, no long slender metacarpal;
//   knuckle  a heavy knuckle it stands on, a hooked spur on the thumb's curve (CreatureWings.ThumbTip: the claw's tip
//            is what the wing-walk plants), no little walking digits;
//   spar     the wing finger (finger 0 of the spec, the PteroWing's four phalanges) as one smooth spar tapering from a
//            thick root to a blade tip (sparTip), no knuckle bulbs;
//   sail     the flight system's ray fan from the knuckle, its leading edge rolled over the spar (leadingEdge), pleated
//            across its span (pleats, pleatDepth: the folded-sail stripes), its trailing edge bellied out toward the tip
//            into a scythe (bladeBelly) and drawn in between the hidden rays (trailingCurve), cambered; the arm zone
//            rides the arm sector alone (one affine frame: it never splits between the sector and the rear rib);
//   web      the propatagium as a narrow leading web from the wrist to the shoulder (the pteroid bone carries it but
//            is not drawn).
// Bones: PteroWing's. The PteroWing look fields it does not draw (crest, digitLength) must stay 0. Mesh only beyond
// PteroWing: build-time, it allocates.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("SailWing", PartKind.Wing, Bones = "as PteroWing (as Wing + 6: spar segments 2-4, the hidden pteroid, 2 leading-web patches)",
          Summary = "A long narrow sail wing on a heavy arm, flown, folded and flexed by the PteroWing's systems (CreatureFlight + CreatureWings, the chains) but drawn as its own: a muscular arm, a heavy knuckle with a hooked spur on the thumb's curve (what it stands on), a smooth spar tapering to a blade tip, a pleated sail with a scythe-bellied trailing edge and a rolled leading edge, a narrow leading web.",
          System = "as PteroWing (CreatureFlight + CreatureWings + PteroWingRuntime) + SailWing.EmitMesh",
          MaterialLayers = new[] { 8, 1, 2 }, KitOnly = true,
          UseWhen = "sail- or scythe-winged monsters that walk on their folded wings' knuckles (the Apex): anything that should fly like the Pterodactyl but not look like a pterosaur")]
    public sealed class SailWing : PteroWing
    {
        [Param(ParamUnit.Fraction, "the arm's muscle: humerus and forearm this much thicker than the Leg2Bone's radii, a shoulder mass on the humerus", Min = 0, Max = 2, Example = "apex 0.7")]
        public float armBulk = 0.6f;
        [Param(ParamUnit.Fraction, "the knuckle's size, x the (bulked) forearm radius", Min = 0.8f, Max = 3, Example = "apex 1.5")]
        public float knuckle = 1.4f;
        [Param(ParamUnit.Fraction, "the hooked spur's root radius, x the forearm radius (its length is spec.thumbLength: the planted claw)", Min = 0.2f, Max = 1.5f)]
        public float spurRoot = 0.6f;
        [Param(ParamUnit.Fraction, "the spar's radius at the tip, x fingerRoot", Min = 0.05f, Max = 0.6f)]
        public float sparTip = 0.15f;
        [Param(ParamUnit.Count, "pleats across the sail's span (0 = a smooth sheet)", Min = 0, Max = 40, Example = "apex 11")]
        public int pleats = 10;
        [Param(ParamUnit.Fraction, "pleat depth, x the pleat's width (the sheet's slope across a pleat stays under 2 pi x this)", Min = 0, Max = 0.08f)]
        public float pleatDepth = 0.03f;
        [Param(ParamUnit.Fraction, "the scythe: the trailing edge between the spar's tip and the next ray bellies out by this x the reach", Min = 0, Max = 0.3f, Example = "apex 0.16")]
        public float bladeBelly = 0.12f;

        public override int MeshOrder => 100;

        public override string Validate(in SpeciesCtx c)
        {
            string e = base.Validate(c);
            if (e != null) return e;
            if (crest != 0f || digitLength != 0f) return $"{id}: crest and digitLength are the PteroWing's look - a SailWing draws its own arm (armBulk, knuckle, spurRoot): set them to 0";
            if (armBulk < 0f || armBulk > 2f || knuckle < 0.8f || knuckle > 3f || spurRoot < 0.2f || spurRoot > 1.5f) return $"{id}: armBulk 0..2, knuckle 0.8..3, spurRoot 0.2..1.5";
            if (sparTip < 0.05f || sparTip > 0.6f) return $"{id}: sparTip 0.05..0.6";
            if (pleats < 0 || pleats > 40 || pleatDepth < 0f || pleatDepth > 0.08f) return $"{id}: pleats 0..40, pleatDepth 0..0.08";
            if (bladeBelly < 0f || bladeBelly > 0.3f) return $"{id}: bladeBelly 0..0.3";
            if (spec.thumbLength <= 0f) return $"{id}: spec.thumbLength > 0 (the spur it stands on)";
            return null;
        }

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Wings == null) return;
            int w = WingIndex(KitView.Parts(m.Species), c.Index);
            if (w >= m.WingSpecs.Length) return;
            int extra = m.Species.Layout.Start(c.Index) + FlightBones;
            var b = new SailBuilder(this, m, w, extra);
            b.Arm(); b.Spur(); b.Spar(); b.Sail(); b.Web();
        }

        // The pleats' offset along the sail's normal at span s (0 leading edge .. 1 forearm) and distance d from the
        // knuckle, for a sail spanning `angle` radians: amplitude, and the slope across the span (for the normal).
        internal float Pleat(float s, float d, float angle, out float slope)
        {
            slope = 0f;
            if (pleats <= 0 || !(pleatDepth > 0f)) return 0f;
            // faded in from the knuckle and toward both edges (the rolled leading edge, the forearm)
            float fade = Mathf.Clamp01(s / 0.06f) * Mathf.Clamp01((1f - s) / 0.15f);
            float width = Mathf.Max(1e-5f, d * angle / pleats);
            float ph = 2f * Mathf.PI * pleats * s;
            float a = pleatDepth * width * fade;
            slope = 2f * Mathf.PI * pleatDepth * fade * Mathf.Cos(ph);
            return a * Mathf.Sin(ph);
        }

        sealed class SailBuilder
        {
            const int kArmSides = 16, kSparSides = 10, kSpurSides = 8;
            readonly SailWing p;
            readonly PartMeshEmitter em;
            readonly CreatureSpecies sp;
            readonly CreatureWings cw;
            readonly WingSpec ws;
            readonly LegSpec arm;
            readonly WingState st;
            readonly int w, extra, bu, bl, bh, bc, side;
            readonly Vector3 S0, E, Wr, du, dl, fwd, dors;
            readonly float lu, ll, ru, rl;
            int PB(int k) => k == 0 ? sp.WingBone(w, 0) : extra + k - 1;

            public SailBuilder(SailWing part, PartMeshEmitter m, int wing, int firstExtra)
            {
                p = part; em = m; sp = m.Species; w = wing; extra = firstExtra;
                cw = part.bindFold > 0f ? part.BindWings(m.Rest, w) : m.Wings;
                ws = m.WingSpecs[w]; arm = sp.legs[ws.leg]; st = cw.State[w]; side = cw.Side(w);
                bu = sp.LegBone(ws.leg, 0); bl = sp.LegBone(ws.leg, 1); bh = sp.LegBone(ws.leg, 2);
                bc = ws.groundWristLift > 0f ? bh : bl;   // the knuckle and spur ride the (flexing) hand bone
                S0 = st.shoulder; E = st.elbow; Wr = st.wrist;
                lu = Mathf.Max(1e-4f, (E - S0).magnitude); ll = Mathf.Max(1e-4f, (Wr - E).magnitude);
                du = (E - S0) / lu; dl = (Wr - E) / ll;
                ru = Mathf.Max(1e-4f, arm.upperRadius) * (1f + part.armBulk); rl = Mathf.Max(1e-4f, arm.lowerRadius) * (1f + part.armBulk);
                fwd = -side * Vector3.Cross(st.handNormal, st.handOut);
                dors = st.handNormal;
            }

            static PartMeshEmitter.TubeSample S(Vector3 c, Vector3 t, float r, float dr, float s, Vector4 skin, bool pole = false)
                => PartMeshEmitter.Sample(c, t, r, dr, s, skin, pole);

            void Blob(Vector3 c, Vector3 along, Vector3 up, float ax, float ay, float az, int bone)
            {
                Vector3 u = up - along * Vector3.Dot(up, along);
                if (u.sqrMagnitude < 1e-10f) u = Vector3.Cross(along, Vector3.right);
                Matrix4x4 f = Matrix4x4.TRS(c, Quaternion.LookRotation(along, u.normalized), Vector3.one);
                em.Ellipsoid(f, Vector3.zero, ax, ay, az, 12, 7, PartMeshEmitter.PartLeg, bone);
            }

            // a capsule-ended tube along a straight axis, radius profile r(f), rigid to one bone
            void Tube(Vector3 a, Vector3 b, Func<float, float> r, int samples, int bone, int sides, float s0)
            {
                Vector3 t = b - a; float L = Mathf.Max(1e-5f, t.magnitude); t /= L;
                var q = new List<PartMeshEmitter.TubeSample>(samples + 6);
                Vector4 skin = PartMeshEmitter.Rigid(bone);
                float r0 = r(0f), r1 = r(1f);
                q.Add(S(a - t * r0, t, 0f, 0f, s0, skin, true));
                q.Add(S(a - t * (0.7f * r0), t, 0.71f * r0, 1f, s0 + 0.3f * r0, skin));
                for (int i = 0; i <= samples; i++)
                {
                    float f = (float)i / samples, h = 0.01f;
                    float dr = (r(Mathf.Min(1f, f + h)) - r(Mathf.Max(0f, f - h))) / (2f * h * L);
                    q.Add(S(a + t * (f * L), t, r(f), dr, s0 + r0 + f * L, skin));
                }
                q.Add(S(b + t * (0.7f * r1), t, 0.71f * r1, -1f, s0 + r0 + L + 0.7f * r1, skin));
                q.Add(S(b + t * r1, t, 0f, 0f, s0 + r0 + L + r1, skin, true));
                Vector3 x0 = Vector3.Cross(t, dors);
                if (x0.sqrMagnitude < 1e-8f) x0 = Vector3.Cross(t, Vector3.up);
                em.Tube(q, x0.normalized, sides, PartMeshEmitter.PartLeg);
            }

            // ---------------------------------------------------------- the arm

            public void Arm()
            {
                // humerus: a ball buried in the chest, thick at the shoulder, a ball at the elbow (rigid to the humerus)
                float rE = Mathf.Max(0.9f * ru, 1.3f * rl);
                float blob = 1.2f * ru;
                {
                    Vector4 sk = PartMeshEmitter.Rigid(bu);
                    var q = new List<PartMeshEmitter.TubeSample>();
                    q.Add(S(S0 - du * blob, du, 0f, 0f, 0f, sk, true));
                    q.Add(S(S0 - du * (0.866f * blob), du, 0.5f * 1.3f * ru, 0f, 0.13f * blob, sk));
                    q.Add(S(S0 - du * (0.5f * blob), du, 0.866f * 1.3f * ru, 0f, 0.5f * blob, sk));
                    float H(float f) { f = Mathf.Clamp01(f); return ru * (Mathf.Lerp(1.3f, 0.85f, f * f * (3f - 2f * f)) + 0.18f * Mathf.Exp(-(f - 0.35f) * (f - 0.35f) / 0.03f)); }
                    for (int i = 0; i <= 6; i++)
                    {
                        float f = i / 7f;
                        q.Add(S(S0 + du * (f * lu), du, H(f), (H(f + 0.01f) - H(f - 0.01f)) / (0.02f * lu), blob + f * lu, sk));
                    }
                    q.Add(S(E, du, rE, 0f, blob + lu, sk));
                    for (int k = 1; k <= 2; k++)
                    {
                        float a = 30f * k * Mathf.Deg2Rad;
                        q.Add(S(E + du * (rE * Mathf.Sin(a)), du, rE * Mathf.Cos(a), -Mathf.Tan(a), blob + lu + rE * Mathf.Sin(a), sk));
                    }
                    q.Add(S(E + du * rE, du, 0f, 0f, blob + lu + rE, sk, true));
                    em.Tube(q, PartMeshEmitter.Unit(Vector3.Cross(du, dors), Vector3.right), kArmSides, PartMeshEmitter.PartLeg);
                }
                // the shoulder mass on the humerus' dorsal side (deltoid / flight muscle)
                if (p.armBulk > 0f) Blob(S0 + du * (0.3f * lu) + dors * (0.45f * ru), du, dors, 1.05f * ru, 0.85f * ru, 0.38f * lu, bu);
                // the forearm: one bone, swelling below the elbow, lean toward the knuckle (rigid to the lower bone)
                float R(float f) => rl * (Mathf.Lerp(1.45f, 0.85f, Mathf.Sqrt(Mathf.Clamp01(f))) + 0.25f * Mathf.Exp(-(f - 0.18f) * (f - 0.18f) / 0.012f));
                Tube(E, Wr, R, 12, bl, kArmSides, 0f);
                // the elbow's point on the bend's outside
                Vector3 bend = du - dl;
                if (bend.sqrMagnitude > 1e-8f) Blob(E + bend.normalized * (0.45f * rE), dl, dors, 0.62f * rE, 0.62f * rE, 0.7f * rE, bl);
                // the knuckle it stands on
                float kr = p.knuckle * rl;
                Blob(Wr + dors * (0.1f * kr), dl, dors, kr, 0.9f * kr, 1.1f * kr, bc);
            }

            // ---------------------------------------------------------- the spur (on the thumb's curve: ThumbTip)

            public void Spur()
            {
                float len = Mathf.Max(1e-3f, ws.thumbLength), rl0 = Mathf.Max(1e-4f, arm.lowerRadius);
                // CreatureWings.ThumbTip's shape in the bind hand frame: root on the leading side, tip down the claw
                Vector3 o = st.handOut, n = st.handNormal;
                Vector3 fw = -side * Vector3.Cross(n, o);
                Vector3 dir = (fw * 0.8f + n * 0.3f - o * 0.2f).normalized;
                Vector3 root = Wr + fw * (0.6f * rl0);
                Vector3 tip = root + dir * len - n * (0.25f * len);
                // a hooked cone from inside the knuckle to the claw's tip, curling toward -n (the hand's ventral side)
                Vector3 d = PartMeshEmitter.Unit(tip - root, dir);
                Vector3 up = PartMeshEmitter.Unit(n - d * Vector3.Dot(n, d), dors);
                float r0 = p.spurRoot * rl;
                Vector3 b0 = root - d * (0.6f * r0);
                float L = (tip - b0).magnitude;
                // Horn curls toward its frame's -y: aim it so the arc's end lands on the tip (curl 40 deg, start turned up by half)
                const float curl = 40f;
                Quaternion q = Quaternion.LookRotation(d, up) * Quaternion.AngleAxis(-0.5f * curl, Vector3.right);
                float chord = 2f * Mathf.Sin(0.5f * curl * Mathf.Deg2Rad) / (curl * Mathf.Deg2Rad);
                em.Horn(b0, q, r0, 0.04f * r0, L / chord, curl, bc, PartMeshEmitter.PartFoot, kSpurSides, 0f, out _, out _);
            }

            // ---------------------------------------------------------- the spar (the wing finger)

            float SparR(float g) => p.fingerRoot * Mathf.Lerp(1f, p.sparTip, Mathf.Pow(Mathf.Clamp01(g), 0.8f));

            public void Spar()
            {
                float L = ws.fingerLengths[0], g0 = 0f;
                for (int k = 0; k < Phalanges; k++)
                {
                    Matrix4x4 b = em.Bones[PB(k)];
                    Vector3 a = b.GetColumn(3), t = PartMeshEmitter.Unit(b.GetColumn(2), st.handOut);
                    float lk = p.phalanges[k] * L, g1 = g0 + p.phalanges[k];
                    float ga = g0, gb = g1;
                    // a thick root out of the knuckle on the first segment
                    Func<float, float> r = k == 0 ? (Func<float, float>)(f => SparR(Mathf.Lerp(ga, gb, f)) * (1f + 0.6f * Mathf.Exp(-f * f / 0.02f)))
                                                  : f => SparR(Mathf.Lerp(ga, gb, f));
                    Tube(a, a + t * lk, r, 6, PB(k), kSparSides, g0 * L);
                    g0 = g1;
                }
            }

            Vector3 SparAt(float s, out int bone)
            {
                float L = ws.fingerLengths[0], cum = 0f;
                for (int k = 0; k < Phalanges; k++)
                {
                    float lk = p.phalanges[k] * L;
                    if (s <= cum + lk || k == Phalanges - 1)
                    {
                        Matrix4x4 b = em.Bones[PB(k)];
                        bone = PB(k);
                        return (Vector3)b.GetColumn(3) + PartMeshEmitter.Unit(b.GetColumn(2), st.handOut) * Mathf.Min(s - cum, lk);
                    }
                    cum += lk;
                }
                bone = PB(0);
                return Wr;
            }

            // ---------------------------------------------------------- the sail

            public void Sail()
            {
                int F = cw.FingerCount(w), Z = F + 2;
                var ray = new Vector3[F + WingSpec.Ribs];
                var bone = new int[ray.Length];
                for (int k = 0; k < F; k++) { ray[k] = cw.FingerTip[cw.FingerIndex(w, k)] - Wr; bone[k] = sp.WingBone(w, k); }
                for (int k = 0; k < WingSpec.Ribs; k++) { ray[F + k] = cw.RibTarget[w * WingSpec.Ribs + k] - Wr; bone[F + k] = sp.WingBone(w, WingBoneLayout.FirstRibOf(ws) + k); }
                ray[ray.Length - 1] = E - Wr;
                Vector3 curvedTip = SparAt(ws.fingerLengths[0], out _);
                var zn = new Vector3[Z];
                float total = 0f;
                var th = new float[Z];
                for (int z = 0; z < Z; z++)
                {
                    Vector3 cc = Vector3.Cross(ray[z], ray[z + 1]);
                    if (Vector3.Dot(cc, st.handNormal) < 0f) cc = -cc;
                    zn[z] = cc.sqrMagnitude > 1e-12f ? cc.normalized : st.handNormal;
                    th[z] = Mathf.Acos(Mathf.Clamp(Vector3.Dot(ray[z].normalized, ray[z + 1].normalized), -1f, 1f));
                    total += th[z];
                }
                int sectorBone = sp.WingBone(w, WingBoneLayout.ArmRibOf(ws));
                Vector3 sectorN = cw.ArmSector(w).MultiplyVector(Vector3.forward).normalized;
                zn[Z - 1] = sectorN;
                var rn = new Vector3[ray.Length];
                for (int k = 0; k < ray.Length; k++)
                {
                    Vector3 sum = (k > 0 ? zn[k - 1] : Vector3.zero) + (k < Z ? zn[k] : Vector3.zero);
                    rn[k] = sum.sqrMagnitude > 1e-12f ? sum.normalized : st.handNormal;
                }
                int C = p.membraneColumns, Rw = p.membraneRows;
                var chord = new Vector3[C + 1];
                int fr = WingBoneLayout.FanRearRib(ws), fanRear = fr >= 0 ? sp.WingBone(w, fr) : -1;
                float L0 = ws.fingerLengths[0];
                float spanAngle = 0f;   // the free sail's angle (the pleats run over the zones before the arm's)
                for (int z = 0; z < Z - 1; z++) spanAngle += th[z];
                float before = 0f;
                for (int z = 0; z < Z; z++)
                {
                    Vector3 A = ray[z], B = ray[z + 1];
                    bool free = z < Z - 1, armZone = z == Z - 1;
                    float la = A.magnitude, lb = B.magnitude;
                    Vector3 da = A / Mathf.Max(1e-6f, la), db = B / Mathf.Max(1e-6f, lb);
                    float t = th[z];
                    for (int sheet = 0; sheet < 2; sheet++)
                    {
                        float sg = sheet == 0 ? 1f : -1f;
                        int first = em.VertexCount;
                        for (int j = 0; j <= Rw; j++)
                        {
                            float v = (float)j / Rw;
                            for (int i = 0; i <= C; i++)
                            {
                                float u = (float)i / C;
                                // the trailing edge: zone 0 bellies out into the scythe, the other free zones are drawn in
                                float edge = z == 0 ? 1f + p.bladeBelly * Mathf.Sin(Mathf.PI * u) : free ? 1f - p.trailingCurve * Mathf.Sin(Mathf.PI * u) : 1f;
                                float ph = u * t;
                                float den = la * Mathf.Sin(ph) + lb * Mathf.Sin(t - ph);
                                float reach = t > 1e-4f && den > 1e-6f ? la * lb * Mathf.Sin(t) / den : Mathf.Lerp(la, lb, u);
                                Vector3 dir = t > 1e-4f ? (Mathf.Sin(t - ph) * da + Mathf.Sin(ph) * db) / Mathf.Sin(t) : da;
                                Vector3 pt = Wr + dir * (reach * v * Mathf.Lerp(1f, edge, v * v));
                                float span = (z + u) / Z;
                                pt += st.handNormal * (p.camber * Mathf.Sin(Mathf.PI * Mathf.Clamp01(span * 1.25f)) * (pt - Wr).magnitude * 0.5f);
                                if (z == 0) { float le = Mathf.Clamp01(1f - u / 0.25f); pt += st.handNormal * (sg * p.leadingEdge * le * le * (1f - 0.75f * v)); }
                                int b0 = bone[z];
                                if (z == 0)
                                {
                                    Vector3 onCurve = SparAt(v * L0, out int pb);
                                    pt += (onCurve - (Wr + da * (la * v))) * (1f - u);
                                    b0 = pb;
                                    if (j == Rw && i == 0) pt = curvedTip;
                                }
                                // the pleats (free sail only, by angle across the span), their slope tilting the normal
                                Vector3 nrm = armZone && i > 0 ? sectorN : Vector3.Lerp(rn[z], rn[z + 1], u).normalized;
                                if (free)
                                {
                                    float s = spanAngle > 1e-5f ? (before + ph) / spanAngle : 0f;
                                    float pl = p.Pleat(s, (pt - Wr).magnitude, spanAngle, out float slope);
                                    if (pl != 0f || slope != 0f)
                                    {
                                        pt += nrm * pl;
                                        Vector3 across = PartMeshEmitter.Unit(Vector3.Cross(nrm, dir), Vector3.zero);
                                        // across points toward increasing span (from ray z to ray z+1)
                                        if (Vector3.Dot(across, db - da) < 0f) across = -across;
                                        nrm = PartMeshEmitter.Unit(nrm - across * slope, nrm);
                                    }
                                }
                                if (armZone && j == Rw) chord[i] = pt;
                                float dist = (pt - Wr).magnitude;
                                int b1 = z + 2 == Z && fanRear >= 0 ? fanRear : bone[z + 1];
                                // the arm zone rides the sector alone (affine, exact on the rib and the forearm)
                                // (its knuckle point - every ray's origin - on the rear rib itself: the same point under either frame)
                                Vector4 skin = !armZone ? PartMeshEmitter.Blend(b0, b1, u) : PartMeshEmitter.Rigid(j == 0 && i == 0 ? bone[z] : sectorBone);
                                Vector3 vn = sg * (armZone ? sectorN : nrm);
                                em.Vertex(pt, vn, dist, (z + u) / Z, PartMeshEmitter.PartMembrane, sg * (0.1f + 0.9f * v), skin);
                            }
                        }
                        em.Grid(first, sg * zn[z], Rw, C);
                    }
                    before += t;
                }
                FlankPatches(chord, sp.WingBone(w, WingBoneLayout.HumerusPatchOf(ws)), sectorN, Z);
            }

            // the membrane between the humerus and the flank (as PteroWing: a fan from the elbow per flank patch)
            void FlankPatches(Vector3[] chord, int patchBone, Vector3 nz, int zones)
            {
                int P = cw.FlankPatches(w), f0 = cw.FlankStart(w);
                float la0 = Mathf.Max(1e-6f, (chord[0] - E).magnitude);
                int rows = chord.Length - 1, cols = P > 1 ? 2 : 6;
                Vector3 Node(int k) => k == 0 ? chord[0] : cw.FlankNode[f0 + k];
                var pn = new Vector3[P];
                for (int q = 0; q < P; q++)
                {
                    Vector3 cc = Vector3.Cross(Node(q + 1) - E, Node(q) - E);
                    if (Vector3.Dot(cc, nz) < 0f) cc = -cc;
                    pn[q] = cc.sqrMagnitude > 1e-12f ? cc.normalized : nz;
                }
                for (int q = 0; q < P; q++)
                {
                    Vector3 A = Node(q) - E, B = Node(q + 1) - E;
                    float la = A.magnitude, lb = B.magnitude;
                    Vector3 da = A / Mathf.Max(1e-6f, la), db = B / Mathf.Max(1e-6f, lb);
                    float th = Mathf.Acos(Mathf.Clamp(Vector3.Dot(da, db), -1f, 1f));
                    Vector3 nA = q == 0 ? nz : (pn[q - 1] + pn[q]).normalized, nB = q == P - 1 ? pn[q] : (pn[q] + pn[q + 1]).normalized;
                    for (int sheet = 0; sheet < 2; sheet++)
                    {
                        float sg = sheet == 0 ? 1f : -1f;
                        int first = em.VertexCount;
                        for (int j = 0; j <= rows; j++)
                        {
                            float v = Mathf.Clamp01((chord[rows - j] - E).magnitude / la0);
                            for (int i = 0; i <= cols; i++)
                            {
                                float u = (float)i / cols, ph = u * th;
                                float den = la * Mathf.Sin(ph) + lb * Mathf.Sin(th - ph);
                                float reach = th > 1e-4f && den > 1e-6f ? la * lb * Mathf.Sin(th) / den : Mathf.Lerp(la, lb, u);
                                Vector3 dir = th > 1e-4f ? (Mathf.Sin(th - ph) * da + Mathf.Sin(ph) * db) / Mathf.Sin(th) : da;
                                Vector3 pt = q == 0 && i == 0 ? chord[rows - j] : E + dir * (reach * v);
                                Vector3 vn = i == 0 ? nA : i == cols ? nB : pn[q];
                                em.Vertex(pt, sg * vn, (pt - Wr).magnitude, (zones - (q + u) / P) / zones, PartMeshEmitter.PartMembrane, sg * (0.1f + 0.9f * v), PartMeshEmitter.Rigid(patchBone + q));
                            }
                        }
                        em.Grid(first, sg * pn[q], rows, cols);
                    }
                }
            }

            // ---------------------------------------------------------- the leading web (the propatagium, no pteroid drawn)

            public void Web()
            {
                int pb = extra + Phalanges - 1;
                Matrix4x4 pt = em.Bones[pb];
                Vector3 carpus = pt.GetColumn(3), tip = carpus + PartMeshEmitter.Unit(pt.GetColumn(2), fwd) * p.pteroidLength;
                // the web's root: a carpal knob on the forearm's leading side (rides the hidden pteroid bone)
                Blob(carpus, PartMeshEmitter.Unit(tip - carpus, fwd), dors, 0.55f * rl, 0.5f * rl, 0.7f * rl, pb);
                const int rows = 6, cols = 8;
                for (int patch = 0; patch < 2; patch++)
                {
                    Vector3 a = patch == 0 ? carpus : tip, b = patch == 0 ? tip : S0;
                    Vector3 n = Vector3.Cross(a - E, b - E);
                    if (Vector3.Dot(n, dors) < 0f) n = -n;
                    n = PartMeshEmitter.Unit(n, dors);
                    int bone = extra + Phalanges + patch;
                    for (int sheet = 0; sheet < 2; sheet++)
                    {
                        float sg = sheet == 0 ? 1f : -1f;
                        int first = em.VertexCount;
                        for (int j = 0; j <= rows; j++)
                        {
                            float v = (float)j / rows;
                            for (int i = 0; i <= cols; i++)
                            {
                                float u = (float)i / cols;
                                // the free front edge: a smooth concave curve from the carpus to the shoulder
                                float pull = patch == 1 ? 1f - 0.2f * Mathf.Sin(Mathf.PI * u) * v * v : 1f - 0.08f * Mathf.Sin(Mathf.PI * u) * v * v;
                                Vector3 q = E + ((1f - u) * (a - E) + u * (b - E)) * (v * pull);
                                em.Vertex(q, sg * n, (q - Wr).magnitude, 0.5f * (patch + u), PartMeshEmitter.PartMembrane, sg * (0.1f + 0.9f * v), PartMeshEmitter.Rigid(bone));
                            }
                        }
                        em.Grid(first, sg * n, rows, cols);
                    }
                }
            }
        }
    }
}
