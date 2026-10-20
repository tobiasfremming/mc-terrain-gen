// Creature kit: PteroWing's mesh (see PteroWing.cs), drawn once on the emitter's spread rest pose (the bind):
// the pterosaur arm, the walking digits, the pteroid, the four phalanges of the wing finger, the brachiopatagium (the
// flight system's fan from the knuckle, its leading edge on the phalanges, its trailing edge smooth) with its flank
// patches, and the propatagium. Two-sided membranes are two sheets (part 8). Build-time only: it allocates.
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public sealed partial class PteroWing
    {
        const int kArmSides = 14, kFingerSides = 8, kDigitSides = 7;

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Wings == null) return;
            int w = WingIndex(KitView.Parts(m.Species), c.Index);
            if (w >= m.WingSpecs.Length) return;
            int extra = m.Species.Layout.Start(c.Index) + FlightBones;   // phalanx k >= 1: extra + k - 1
            var b = new PteroBuilder(this, m, w, extra);
            b.Arm(); b.Digits(); b.PteroidBone(); b.WingFinger(); b.Membrane(); b.Propatagium();
        }

        sealed class PteroBuilder
        {
            readonly PteroWing p;
            readonly PartMeshEmitter em;
            readonly CreatureSpecies sp;
            readonly CreatureWings cw;
            readonly WingSpec ws;
            readonly LegSpec arm;
            readonly WingState st;
            readonly int w, extra, bu, bl, bh, side;
            readonly Vector3 S0, E, Wr, du, dl, fwd, dors;
            readonly float lu, ll, ru, rl;
            readonly bool flexHand;
            // phalanx k's bone: the first is the flight's finger-0 bone
            int PB(int k) => k == 0 ? sp.WingBone(w, 0) : extra + k - 1;
            int PteroidB => extra + Phalanges - 1;

            public PteroBuilder(PteroWing part, PartMeshEmitter m, int wing, int firstExtra)
            {
                p = part; em = m; sp = m.Species; w = wing; extra = firstExtra;
                cw = part.bindFold > 0f ? part.BindWings(m.Rest, w) : m.Wings;   // the bind's wing (PteroWingRuntime.AfterSpreadWings)
                ws = m.WingSpecs[w]; arm = sp.legs[ws.leg]; st = cw.State[w]; side = cw.Side(w);
                bu = sp.LegBone(ws.leg, 0); bl = sp.LegBone(ws.leg, 1); bh = sp.LegBone(ws.leg, 2);
                S0 = st.shoulder; E = st.elbow; Wr = st.wrist;
                lu = Mathf.Max(1e-4f, (E - S0).magnitude); ll = Mathf.Max(1e-4f, (Wr - E).magnitude);
                du = (E - S0) / lu; dl = (Wr - E) / ll;
                ru = Mathf.Max(1e-4f, arm.upperRadius); rl = Mathf.Max(1e-4f, arm.lowerRadius);
                fwd = -side * Vector3.Cross(st.handNormal, st.handOut);   // the leading edge
                dors = st.handNormal;
                flexHand = ws.groundWristLift > 0f;
            }

            static PartMeshEmitter.TubeSample S(Vector3 c, Vector3 t, float r, float dr, float s, Vector4 skin, bool pole = false)
                => PartMeshEmitter.Sample(c, t, r, dr, s, skin, pole);
            static Vector4 Rigid(int b) => PartMeshEmitter.Rigid(b);
            static Vector4 Blend(int a, int b, float w1) => PartMeshEmitter.Blend(a, b, w1);

            void Blob(Vector3 c, Vector3 along, Vector3 up, float ax, float ay, float az, int bone, int part = PartMeshEmitter.PartLeg)
            {
                Vector3 u = up - along * Vector3.Dot(up, along);
                if (u.sqrMagnitude < 1e-10f) u = Vector3.Cross(along, Vector3.right);
                Matrix4x4 f = Matrix4x4.TRS(c, Quaternion.LookRotation(along, u.normalized), Vector3.one);
                em.Ellipsoid(f, Vector3.zero, ax, ay, az, 10, 6, part, bone);
            }

            // a capsule-ended tube along a straight axis: radius profile r(f), f = 0..1, rigid or blended per sample
            void Bone(Vector3 a, Vector3 b, System.Func<float, float> r, float[] at, System.Func<float, Vector4> skin, int sides, float s0)
            {
                Vector3 t = b - a; float L = Mathf.Max(1e-5f, t.magnitude); t /= L;
                var q = new List<PartMeshEmitter.TubeSample>(at.Length + 6);
                float r0 = r(0f), r1 = r(1f);
                q.Add(S(a - t * r0, t, 0f, 0f, s0, skin(0f), true));
                q.Add(S(a - t * (0.7f * r0), t, 0.71f * r0, 1f, s0 + 0.3f * r0, skin(0f)));
                foreach (float f in at)
                {
                    float h = 0.01f;
                    float dr = (r(Mathf.Min(1f, f + h)) - r(Mathf.Max(0f, f - h))) / (2f * h * L);
                    q.Add(S(a + t * (f * L), t, r(f), dr, s0 + r0 + f * L, skin(f)));
                }
                q.Add(S(b + t * (0.7f * r1), t, 0.71f * r1, -1f, s0 + r0 + L + 0.7f * r1, skin(1f)));
                q.Add(S(b + t * r1, t, 0f, 0f, s0 + r0 + L + r1, skin(1f), true));
                Vector3 x0 = Vector3.Cross(t, dors);
                if (x0.sqrMagnitude < 1e-8f) x0 = Vector3.Cross(t, Vector3.up);
                em.Tube(q, x0.normalized, sides, PartMeshEmitter.PartLeg);
            }

            // ---------------------------------------------------------- the arm

            public void Arm()
            {
                Vector3 carpus = Vector3.Lerp(E, Wr, p.carpusAt);
                int bc = flexHand ? bl : bh;
                // humerus: buried in the chest at the shoulder, thick, a ball at the elbow
                float rE = Mathf.Max(0.85f * ru, 1.25f * rl);
                float blob = 1.2f * ru;
                {
                    // a ball socket: the buried shoulder end is rigid to the humerus (a blend with the chest turned inside out
                    // when the launch swings the arm from the ground stance up into the stroke)
                    Vector4 shSkin = Rigid(bu);
                    var q = new List<PartMeshEmitter.TubeSample>();
                    q.Add(S(S0 - du * blob, du, 0f, 0f, 0f, shSkin, true));
                    q.Add(S(S0 - du * (0.866f * blob), du, 0.5f * 1.25f * ru, 0f, 0.13f * blob, shSkin));
                    q.Add(S(S0 - du * (0.5f * blob), du, 0.866f * 1.25f * ru, 0f, 0.5f * blob, shSkin));
                    float H(float f) { f = Mathf.Clamp01(f); return Mathf.Lerp(1.25f * ru, 0.8f * ru, f * f * (3f - 2f * f)); }
                    q.Add(S(S0, du, H(0f), 0f, blob, Rigid(bu)));
                    for (int i = 1; i <= 5; i++)
                    {
                        float f = i / 6f;
                        q.Add(S(S0 + du * (f * lu), du, H(f), (H(f + 0.01f) - H(f - 0.01f)) / (0.02f * lu), blob + f * lu, Rigid(bu)));
                    }
                    q.Add(S(E, du, rE, 0f, blob + lu, Rigid(bu)));
                    for (int k = 1; k <= 2; k++)
                    {
                        float a = 30f * k * Mathf.Deg2Rad;
                        q.Add(S(E + du * (rE * Mathf.Sin(a)), du, rE * Mathf.Cos(a), -Mathf.Tan(a), blob + lu + rE * Mathf.Sin(a), Rigid(bu)));
                    }
                    q.Add(S(E + du * rE, du, 0f, 0f, blob + lu + rE, Rigid(bu), true));
                    em.Tube(q, PartMeshEmitter.Unit(Vector3.Cross(du, dors), Vector3.right), kArmSides, PartMeshEmitter.PartLeg);
                }
                // the deltopectoral crest: a flange on the humerus' leading edge, near the shoulder
                if (p.crest > 0f)
                {
                    Vector3 hf = fwd - du * Vector3.Dot(fwd, du);
                    hf = PartMeshEmitter.Unit(hf, fwd);
                    Blob(S0 + du * (0.3f * lu) + hf * (0.45f * p.crest * ru), du, hf, 0.32f * ru, 0.55f * p.crest * ru, 0.24f * lu, bu);
                    Blob(S0 + du * (0.45f * lu) - hf * (0.4f * ru) + dors * (0.35f * ru), du, dors, 0.6f * ru, 0.55f * ru, 0.3f * lu, bu);   // triceps
                }
                // the forearm (radius + ulna) from the elbow ball to the carpus, rigid to the lower bone
                float lc = (carpus - E).magnitude;
                Bone(E, carpus, f => Mathf.Lerp(1.15f * rl, 0.95f * rl, f), new[] { 0f, 0.2f, 0.5f, 0.8f, 1f }, f => Rigid(bl), kArmSides, 0f);
                {
                    Vector3 tr = -(fwd - dl * Vector3.Dot(fwd, dl)).normalized;
                    Vector3 a0 = E + dl * (0.12f * lc) + tr * (0.6f * rl), a1 = carpus - dl * (0.05f * lc) + tr * (0.5f * rl);
                    Bone(a0, a1, f => Mathf.Lerp(0.55f * rl, 0.5f * rl, f), new[] { 0f, 0.5f, 1f }, f => Rigid(bl), kArmSides - 4, 0f);
                }
                // the olecranon on the bend's outside
                Vector3 bend = du - dl;
                if (bend.sqrMagnitude > 1e-8f) Blob(E + bend.normalized * (0.5f * rE), dl, dors, 0.6f * rE, 0.6f * rE, 0.7f * rE, bl);
                // the carpus: a knob where the pteroid roots
                Blob(carpus, dl, dors, 1.3f * rl, 1.15f * rl, 1.1f * rl, bl);
                // the long metacarpal to the knuckle, then the knuckle's condyle
                Bone(carpus, Wr, f => Mathf.Lerp(0.82f * rl, 0.72f * rl, f), new[] { 0f, 0.3f, 0.7f, 1f }, f => Rigid(bl), kArmSides - 2, lc);
                float kr = 1.3f * rl;
                Blob(Wr + dors * (0.1f * kr), dl, dors, kr, 0.95f * kr, 1.05f * kr, bc);
                Blob(Wr + fwd * (0.55f * kr) + dl * (0.2f * kr), dl, dors, 0.55f * kr, 0.5f * kr, 0.6f * kr, bc);
            }

            // ---------------------------------------------------------- walking digits (on the thumb's curve)

            public void Digits()
            {
                if (!(p.digitLength > 0f)) return;
                Vector3 tdir = (fwd * 0.8f + dors * 0.3f - dl * 0.2f).normalized;
                Vector3 root0 = Wr + fwd * (0.6f * rl);
                float len = p.digitLength, r0 = 0.32f * rl;
                for (int k = -1; k <= 1; k++)
                {
                    Vector3 d = Quaternion.AngleAxis(22f * k, dl) * tdir;
                    Vector3 root = root0 + Vector3.Cross(dl, tdir).normalized * (0.35f * rl * k);
                    Vector3 tip = root + d * (0.65f * len) - dors * (0.08f * len);
                    Bone(root, tip, f => r0 * Mathf.Lerp(1f, 0.75f, f) * (1f + 0.25f * Mathf.Exp(-(f - 0.5f) * (f - 0.5f) / 0.01f)), new[] { 0f, 0.4f, 0.5f, 0.6f, 1f }, f => Rigid(bh), kDigitSides, 0f);
                    Vector3 cd = PartMeshEmitter.Unit(tip - root, d);
                    Vector3 cu = PartMeshEmitter.Unit(Vector3.Cross(Vector3.Cross(cd, -dors), cd), dors);
                    em.Horn(tip - cd * (0.1f * len), Quaternion.LookRotation(cd, cu), 0.8f * r0, 0.05f * r0, 0.45f * len, 70f, bh, PartMeshEmitter.PartFoot, kDigitSides, 0f, out _, out _);
                }
            }

            // ---------------------------------------------------------- the pteroid

            public void PteroidBone()
            {
                Matrix4x4 b = em.Bones[PteroidB];
                Vector3 c = b.GetColumn(3), d = PartMeshEmitter.Unit(b.GetColumn(2), fwd);
                float len = p.pteroidLength;
                Bone(c, c + d * len, f => Mathf.Lerp(0.42f * rl, 0.1f * rl, f), new[] { 0f, 0.35f, 0.7f, 1f }, f => Rigid(PteroidB), kDigitSides, 0f);
            }

            // ---------------------------------------------------------- the wing finger

            float FingerR(float g) => p.fingerRoot * Mathf.Lerp(1f, 0.18f, g);   // g = 0 root .. 1 tip of the whole finger

            public void WingFinger()
            {
                float L = ws.fingerLengths[0], g0 = 0f;
                for (int k = 0; k < Phalanges; k++)
                {
                    Matrix4x4 b = em.Bones[PB(k)];
                    Vector3 a = b.GetColumn(3), t = PartMeshEmitter.Unit(b.GetColumn(2), st.handOut);
                    float lk = p.phalanges[k] * L, g1 = g0 + p.phalanges[k];
                    float ga = g0, gb = g1;
                    Bone(a, a + t * lk, f => FingerR(Mathf.Lerp(ga, gb, f)) * (1f + 0.22f * Mathf.Exp(-f * f / 0.004f) + 0.12f * Mathf.Exp(-(1 - f) * (1 - f) / 0.004f)),
                         new[] { 0f, 0.05f, 0.15f, 0.5f, 0.85f, 0.95f, 1f }, f => Rigid(PB(k)), kFingerSides, g0 * L);
                    // the joint's knuckle bulb (rigid to the phalanx beyond it)
                    if (k > 0) Blob(a, t, dors, 1.45f * FingerR(g0), 1.25f * FingerR(g0), 1.5f * FingerR(g0), PB(k));
                    g0 = g1;
                }
            }

            // the wing finger's curve in the bind at arc length s (m), and the phalanx bone there
            Vector3 FingerAt(float s, out int bone)
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

            // ---------------------------------------------------------- the brachiopatagium (the flight's ray fan, drawn as a pterosaur's)

            readonly List<Vector3> chordList = new List<Vector3>();

            public void Membrane()
            {
                int F = cw.FingerCount(w), Z = F + 2;   // finger 0-1, 1-2, 2-trailing rib, trailing-rear flank rib, rear-forearm
                var ray = new Vector3[F + WingSpec.Ribs];
                var bone = new int[ray.Length];
                for (int k = 0; k < F; k++) { ray[k] = cw.FingerTip[cw.FingerIndex(w, k)] - Wr; bone[k] = sp.WingBone(w, k); }
                for (int k = 0; k < WingSpec.Ribs; k++) { ray[F + k] = cw.RibTarget[w * WingSpec.Ribs + k] - Wr; bone[F + k] = sp.WingBone(w, WingBoneLayout.FirstRibOf(ws) + k); }
                ray[ray.Length - 1] = E - Wr;
                // the wing finger's ray ends on its curved tip
                Vector3 curvedTip = FingerAt(ws.fingerLengths[0], out _);
                var zn = new Vector3[Z];
                for (int z = 0; z < Z; z++)
                {
                    Vector3 cc = Vector3.Cross(ray[z], ray[z + 1]);
                    if (Vector3.Dot(cc, st.handNormal) < 0f) cc = -cc;
                    zn[z] = cc.sqrMagnitude > 1e-12f ? cc.normalized : st.handNormal;
                }
                Vector3 sectorN = cw.ArmSector(w).MultiplyVector(Vector3.forward).normalized;
                zn[Z - 1] = sectorN;
                var rn = new Vector3[ray.Length];
                for (int k = 0; k < ray.Length; k++)
                {
                    Vector3 sum = (k > 0 ? zn[k - 1] : Vector3.zero) + (k < Z ? zn[k] : Vector3.zero);
                    rn[k] = sum.sqrMagnitude > 1e-12f ? sum.normalized : st.handNormal;
                }
                int C = p.membraneColumns, R = p.membraneRows;
                var chord = new Vector3[C + 1];
                int fr = WingBoneLayout.FanRearRib(ws), fanRear = fr >= 0 ? sp.WingBone(w, fr) : -1;
                float L0 = ws.fingerLengths[0];
                for (int z = 0; z < Z; z++)
                {
                    Vector3 A = ray[z], B = ray[z + 1];
                    bool free = z < Z - 1, armZone = z == Z - 1;
                    float la = A.magnitude, lb = B.magnitude;
                    Vector3 da = A / Mathf.Max(1e-6f, la), db = B / Mathf.Max(1e-6f, lb);
                    float th = Mathf.Acos(Mathf.Clamp(Vector3.Dot(da, db), -1f, 1f));
                    float scallop = free ? p.trailingCurve : 0f;
                    for (int sheet = 0; sheet < 2; sheet++)
                    {
                        float sg = sheet == 0 ? 1f : -1f;
                        int first = em.VertexCount;
                        for (int j = 0; j <= R; j++)
                        {
                            float v = (float)j / R;
                            for (int i = 0; i <= C; i++)
                            {
                                float u = (float)i / C;
                                float edge = 1f - scallop * Mathf.Sin(Mathf.PI * u);
                                float ph = u * th;
                                float den = la * Mathf.Sin(ph) + lb * Mathf.Sin(th - ph);
                                float reach = th > 1e-4f && den > 1e-6f ? la * lb * Mathf.Sin(th) / den : Mathf.Lerp(la, lb, u);
                                Vector3 dir = th > 1e-4f ? (Mathf.Sin(th - ph) * da + Mathf.Sin(ph) * db) / Mathf.Sin(th) : da;
                                Vector3 pt = Wr + dir * (reach * v * Mathf.Lerp(1f, edge, v * v));
                                // camber: the sheet bellies up across the whole wing (from the leading edge, zone 0's u = 0,
                                // to the flank), most mid-chord; the zone normal points dorsal
                                float span = (z + u) / Z;   // 0 leading edge .. 1 the forearm
                                pt += st.handNormal * (p.camber * Mathf.Sin(Mathf.PI * Mathf.Clamp01(span * 1.25f)) * (pt - Wr).magnitude * 0.5f);
                                // the thick leading edge: zone 0's two sheets part over the finger, closing a quarter of the
                                // zone back (thinning to the tip with the finger)
                                if (z == 0) { float le = Mathf.Clamp01(1f - u / 0.25f); pt += st.handNormal * (sg * p.leadingEdge * le * le * (1f - 0.75f * v)); }
                                int b0 = bone[z];
                                if (z == 0)
                                {
                                    // the leading edge on the curved wing finger: the straight ray's point moved onto the curve
                                    // (fading across the zone), skinned to the phalanx there
                                    Vector3 onCurve = FingerAt(v * L0, out int pb);
                                    pt += (onCurve - (Wr + da * (la * v))) * (1f - u);
                                    b0 = pb;
                                    if (j == R && i == 0) pt = curvedTip;
                                }
                                if (armZone && j == R) chord[i] = pt;
                                float dist = (pt - Wr).magnitude;
                                int b1 = z + 2 == Z && fanRear >= 0 ? fanRear : bone[z + 1];
                                Vector4 skin = !armZone ? Blend(b0, b1, u) : Rigid(i == 0 ? bone[z] : bone[z + 1]);
                                Vector3 vn = (sg * (armZone && i > 0 ? sectorN : Vector3.Lerp(rn[z], rn[z + 1], u))).normalized;
                                em.Vertex(pt, vn, dist, (z + u) / Z, PartMeshEmitter.PartMembrane, sg * (0.1f + 0.9f * v), skin);
                            }
                        }
                        em.Grid(first, sg * zn[z], R, C);
                    }
                }
                FlankPatches(chord, sp.WingBone(w, WingBoneLayout.HumerusPatchOf(ws)), sectorN, Z);
            }

            // the membrane between the humerus and the flank (a fan from the elbow per flank patch, exact on its affine bone)
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
                                em.Vertex(pt, sg * vn, (pt - Wr).magnitude, (zones - (q + u) / P) / zones, PartMeshEmitter.PartMembrane, sg * (0.1f + 0.9f * v), Rigid(patchBone + q));
                            }
                        }
                        em.Grid(first, sg * pn[q], rows, cols);
                    }
                }
            }

            // ---------------------------------------------------------- the propatagium

            public void Propatagium()
            {
                Matrix4x4 pt = em.Bones[PteroidB];
                Vector3 carpus = pt.GetColumn(3), tip = carpus + PartMeshEmitter.Unit(pt.GetColumn(2), fwd) * p.pteroidLength;
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
                                // the free front edge (tip -> shoulder) is drawn in a little
                                float pull = patch == 1 ? 1f - 0.12f * Mathf.Sin(Mathf.PI * u) * v * v : 1f;
                                Vector3 q = E + ((1f - u) * (a - E) + u * (b - E)) * (v * pull);
                                em.Vertex(q, sg * n, (q - Wr).magnitude, 0.5f * (patch + u), PartMeshEmitter.PartMembrane, sg * (0.1f + 0.9f * v), Rigid(bone));
                            }
                        }
                        em.Grid(first, sg * n, rows, cols);
                    }
                }
            }
        }
    }
}
