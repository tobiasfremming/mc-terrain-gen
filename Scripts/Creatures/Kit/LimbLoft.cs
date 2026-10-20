// Creature kit (2026-10-20, big lizard legs): mesh helpers for limbs with a sculpted surface - a loft through
// rings of any shape (muscle lobes, skin folds, scale bumps) whose normals come from the surface itself, a keratin
// claw (laterally flattened, curling), and a foot with digits described one by one (length, phalanges, claw or
// none, webbing). Used by SprawlLeg; any limb part may reuse them. Runs once per species mesh: it may allocate.
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // A tube through rings of `Around` points each (or a single pole point at either end). Normals are the surface's
    // own (central differences round the ring and between rings), so any section shape shades right; every
    // triangle is wound to agree with its vertex normals.
    public sealed class LimbLoft
    {
        public readonly int Around;
        readonly List<Vector3[]> rings = new List<Vector3[]>();
        readonly List<Vector3> centres = new List<Vector3>();
        readonly List<Vector4> skins = new List<Vector4>();
        readonly List<float> arcs = new List<float>();
        readonly List<bool> poles = new List<bool>();

        public LimbLoft(int around) { Around = Mathf.Max(3, around); }

        // Optional: a vertex's part id by (ring, point round it) instead of Emit's `part` (a lip line: the palate of a
        // head in a mouth layer). Null = every vertex in `part` (unchanged).
        public System.Func<int, int, int> PartOf;

        public int Count => rings.Count;

        // A ring: centre c, unit frame x, y (the ring's plane), radius r(phi) about c + y d; phi = 0 along x,
        // pi/2 along y.
        public void Ring(Vector3 c, Vector3 x, Vector3 y, float d, System.Func<float, float> r, Vector4 skin, float arc)
        {
            var p = new Vector3[Around];
            Vector3 o = c + y * d;
            for (int k = 0; k < Around; k++)
            {
                float phi = 2f * Mathf.PI * k / Around;
                float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                p[k] = o + (x * cs + y * sn) * Mathf.Max(PartMeshEmitter.MinRadius, r(phi));
            }
            Add(p, o, skin, arc, false);
        }

        public void Pole(Vector3 p, Vector4 skin, float arc) => Add(new[] { p }, p, skin, arc, true);

        void Add(Vector3[] p, Vector3 c, Vector4 skin, float arc, bool pole)
        {
            rings.Add(p); centres.Add(c); skins.Add(skin); arcs.Add(arc); poles.Add(pole);
        }

        Vector3 P(int i, int k)
        {
            Vector3[] r = rings[i];
            if (r.Length == 1) return r[0];
            k %= Around; if (k < 0) k += Around;
            return r[k];
        }

        public void Emit(PartMeshEmitter m, int part)
        {
            int n = rings.Count;
            if (n < 2) return;
            var first = new int[n];
            var normals = new List<Vector3[]>(n);
            for (int i = 0; i < n; i++)
            {
                var nn = new Vector3[Around];
                Vector3 axis = centres[Mathf.Min(n - 1, i + 1)] - centres[Mathf.Max(0, i - 1)];
                if (poles[i])
                {
                    Vector3 outward = i == 0 ? centres[0] - centres[1] : centres[n - 1] - centres[n - 2];
                    Vector3 pn = PartMeshEmitter.Unit(outward, Vector3.up);
                    for (int k = 0; k < Around; k++) nn[k] = pn;
                }
                else
                    for (int k = 0; k < Around; k++)
                    {
                        Vector3 p = P(i, k);
                        Vector3 dphi = P(i, k + 1) - P(i, k - 1);
                        Vector3 ds = P(Mathf.Min(n - 1, i + 1), k) - P(Mathf.Max(0, i - 1), k);
                        Vector3 nv = Vector3.Cross(dphi, ds);
                        Vector3 radial = p - centres[i];
                        radial -= axis.sqrMagnitude > 1e-14f ? Vector3.Project(radial, axis) : Vector3.zero;
                        if (Vector3.Dot(nv, radial) < 0f) nv = -nv;
                        nn[k] = PartMeshEmitter.Unit(nv, PartMeshEmitter.Unit(radial, Vector3.up));
                    }
                normals.Add(nn);
                first[i] = m.VertexCount;
                for (int k = 0; k <= Around; k++)
                {
                    Vector3 nk = nn[k % Around];
                    float v = poles[i] ? (k + 0.5f) / Around : (float)k / Around;
                    m.Vertex(P(i, k % Around), nk, arcs[i], v, PartOf != null ? PartOf(i, k % Around) : part, nk.y, skins[i]);
                }
            }
            for (int i = 0; i + 1 < n; i++)
            {
                bool pa = poles[i], pb = poles[i + 1];
                if (pa && pb) continue;
                for (int k = 0; k < Around; k++)
                {
                    int a0 = first[i] + k, a1 = a0 + 1, b0 = first[i + 1] + k, b1 = b0 + 1;
                    if (!pa) Tri(m, a0, a1, b0, normals[i][k % Around] + normals[i][(k + 1) % Around] + normals[i + 1][k % Around]);
                    if (!pb) Tri(m, pa ? a0 : a1, b1, b0, normals[i][(k + 1) % Around] + normals[i + 1][(k + 1) % Around] + normals[i + 1][k % Around]);
                }
            }
        }

        // One triangle wound so its face normal agrees with the summed vertex normals.
        public static void Tri(PartMeshEmitter m, int a, int b, int c, Vector3 nsum)
        {
            Vector3 pa = m.Position(a);
            Vector3 fn = Vector3.Cross(m.Position(b) - pa, m.Position(c) - pa);
            m.Tri(a, b, c, Vector3.Dot(fn, nsum) < 0f);
        }

        // A keratin claw rigid to `bone`: a circular arc from basePt along dir, curling toward -up by curveDeg over
        // its length; the section is flattened side to side (width = flat x height) and tapers to a point.
        public static void Claw(PartMeshEmitter m, Vector3 basePt, Vector3 dir, Vector3 up, float r0, float len, float curveDeg, float flat,
                                int bone, int part, int around, float arc)
        {
            dir = PartMeshEmitter.Unit(dir, Vector3.forward);
            Vector3 side = PartMeshEmitter.Unit(Vector3.Cross(up, dir), Vector3.right);
            up = Vector3.Cross(dir, side);
            float bend = Mathf.Max(1e-3f, curveDeg * Mathf.Deg2Rad);
            float k = len / bend;
            Vector3 C(float u) => basePt + dir * (k * Mathf.Sin(bend * u)) - up * (k - k * Mathf.Cos(bend * u));
            var loft = new LimbLoft(around);
            Vector4 skin = PartMeshEmitter.Rigid(bone);
            loft.Pole(C(0f) - dir * (0.6f * r0), skin, arc);
            const int rings = 6;
            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / rings;
                Vector3 t = dir * Mathf.Cos(bend * u) - up * Mathf.Sin(bend * u);
                Vector3 y = up * Mathf.Cos(bend * u) + dir * Mathf.Sin(bend * u);
                float r = r0 * Mathf.Pow(1f - u, 0.75f) * (i == 0 ? 0.92f : 1f);
                float a = r * flat;
                loft.Ring(C(u), side, y, 0f, phi => { float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi); return 1f / Mathf.Sqrt(cs * cs / (a * a) + sn * sn / (r * r)); }, skin, arc + len * u);
            }
            loft.Pole(C(1f), skin, arc + len);
            loft.Emit(m, part);
        }
    }

    // One digit of a DigitFoot, inner (I) to outer.
    public struct DigitSpec
    {
        public float length;     // metres, from the knuckle
        public float radius;     // metres at the root
        public int phalanges;    // knuckle swellings along it
        public float yawDeg;     // outward from the foot's forward (+ = away from the body)
        public float across;     // root position across the knuckles, -1 inner .. +1 outer
        public bool claw;
    }

    // A foot in the foot bone's frame (origin: the contact point, +Y the ground normal, +Z forward), rigid to it:
    // a lofted palm/sole from the heel under the ankle to the knuckles, then the digits fanned on the ground, each
    // with a claw or a blunt tip, webbing between neighbours.
    public static class DigitFoot
    {
        public struct Spec
        {
            public float footLength, ankleHeight, ankleRadius, palmWidth, palmThick, yawDeg, knuckleBump;
            public float clawLength, clawCurveDeg, clawThickness, clawFlat, web;
            public int side;
            public DigitSpec[] digits;
        }

        public static void Emit(PartMeshEmitter m, int bone, Spec s, float arc, int around, int digitAround)
        {
            Matrix4x4 frame = m.Bones[bone] * Matrix4x4.TRS(Vector3.zero, Quaternion.AngleAxis(s.side * s.yawDeg, Vector3.up), Vector3.one);
            Vector4 skin = PartMeshEmitter.Rigid(bone);
            float fl = Mathf.Max(1e-3f, s.footLength), ra = Mathf.Max(PartMeshEmitter.MinRadius, s.ankleRadius);
            int n = s.digits != null ? s.digits.Length : 0;
            float h = Mathf.Max(s.ankleHeight, 0.5f * ra);
            float zh = -0.26f * fl, zb = 0.3f * fl;
            float rt = 0f;
            for (int i = 0; i < n; i++) rt = Mathf.Max(rt, s.digits[i].radius);
            float pad = Mathf.Max(1.1f * rt, 0.45f * ra) * Mathf.Max(0.3f, s.palmThick);
            float palmW = s.palmWidth;
            float hock = Mathf.Max(0f, h - 2.2f * ra);
            // the loft: sections across +Z, heel to knuckles
            float Top(float z) => z <= 0f ? Mathf.Lerp(hock + 1.3f * ra, h + 0.35f * ra, Mathf.SmoothStep(0f, 1f, (z - zh) / -zh))
                                         : Mathf.Lerp(h + 0.35f * ra, 2.4f * pad, Mathf.SmoothStep(0f, 1f, z / zb));
            float Bot(float z) => z <= 0f ? hock : Mathf.Lerp(hock, 0f, Mathf.SmoothStep(0f, 1f, z / zb));
            float Wid(float z) => z <= 0f ? Mathf.Lerp(0.8f * ra, 1.1f * ra, Mathf.SmoothStep(0f, 1f, (z - zh) / -zh))
                                         : Mathf.Lerp(1.1f * ra, palmW, Mathf.SmoothStep(0f, 1f, z / zb));
            Vector3 X = frame.MultiplyVector(Vector3.right).normalized, Y = frame.MultiplyVector(Vector3.up).normalized;
            var palm = new LimbLoft(around);
            palm.Pole(frame.MultiplyPoint3x4(new Vector3(0f, 0.5f * (Top(zh) + Bot(zh)), zh)), skin, arc);
            float[] zs = { zh + 0.22f * ra, 0.6f * zh, 0.25f * zh, 0f, 0.3f * zb, 0.6f * zb, 0.85f * zb, zb };
            foreach (float z in zs)
            {
                float yc = 0.5f * (Top(z) + Bot(z)), b = 0.5f * (Top(z) - Bot(z)), a = Wid(z);
                // a flat sole: the lower half of the section is squarer than the top
                palm.Ring(frame.MultiplyPoint3x4(new Vector3(0f, yc, z)), X, Y, 0f, phi =>
                {
                    float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                    float e = sn < 0f ? 2.6f : 2f;   // superellipse exponent
                    return 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(cs) / a, e) + Mathf.Pow(Mathf.Abs(sn) / b, e), 1f / e);
                }, skin, arc + z - zh);
            }
            palm.Pole(frame.MultiplyPoint3x4(new Vector3(0f, pad, zb + 0.55f * pad)), skin, arc + zb - zh + pad);
            palm.Emit(m, PartMeshEmitter.PartLeg);

            var roots = new Vector3[n]; var dirs = new Vector3[n]; var lens = new float[n];
            float inner = -s.side;   // +X is the inner side of a left foot
            for (int i = 0; i < n; i++)
            {
                DigitSpec d = s.digits[i];
                Vector3 dir = Quaternion.AngleAxis(s.side * d.yawDeg, Vector3.up) * Vector3.forward;
                float len = Mathf.Max(1e-3f, d.length), r0 = Mathf.Max(PartMeshEmitter.MinRadius, d.radius);
                Vector3 root = new Vector3(-inner * d.across * 0.8f * palmW, r0, zb - 0.7f * pad);
                float arch = 0.55f * r0;
                Vector3 C(float q) => root + dir * (len * q) + Vector3.up * (arch * 4f * q * (1f - q) - 0.2f * r0 * q);
                Vector3 D(float q) => dir * len + Vector3.up * (arch * (4f - 8f * q) - 0.2f * r0);
                float R(float q)
                {
                    float r = r0 * (1f - 0.42f * q);
                    // a swelling over each joint between phalanges (the last one near the tip)
                    if (d.phalanges > 0 && s.knuckleBump > 0f)
                    {
                        float ph = q * d.phalanges;
                        float f = ph - Mathf.Floor(ph);
                        r *= 1f + s.knuckleBump * Mathf.Pow(Mathf.Abs(Mathf.Cos(Mathf.PI * f)), 8f) * Mathf.SmoothStep(0f, 1f, q * d.phalanges);
                    }
                    return r;
                }
                var loft = new LimbLoft(digitAround);
                loft.Pole(frame.MultiplyPoint3x4(root - dir * (0.9f * r0)), skin, arc);
                int rings = 6 + 3 * Mathf.Max(0, d.phalanges);
                for (int j = 0; j <= rings; j++)
                {
                    float q = (float)j / rings;
                    Vector3 t = D(q).normalized;
                    Vector3 xx = Vector3.Cross(Vector3.up, t).normalized, yy = Vector3.Cross(t, xx);
                    float r = R(q);
                    // a pad below: the section is flatter underneath
                    loft.Ring(frame.MultiplyPoint3x4(C(q)), frame.MultiplyVector(xx).normalized, frame.MultiplyVector(yy).normalized, 0f,
                              phi => { float sn = Mathf.Sin(phi); return r * (sn < 0f ? 1f + 0.2f * sn : 1f - 0.08f * sn); }, skin, arc + len * q);
                }
                Vector3 tipDir = D(1f).normalized;
                float rTip = R(1f);
                loft.Pole(frame.MultiplyPoint3x4(C(1f) + tipDir * ((d.claw ? 0.45f : 0.85f) * rTip)), skin, arc + len + rTip);
                loft.Emit(m, PartMeshEmitter.PartLeg);
                if (d.claw)
                {
                    // the claw leaves the tip pointing a little up and curls down to the ground
                    Vector3 cd = (tipDir + Vector3.up * 0.3f).normalized;
                    float cl = s.clawLength > 0f ? s.clawLength : 0.18f * fl;
                    Vector3 basePt = C(1f) + tipDir * (0.05f * rTip) + Vector3.up * (0.1f * rTip);
                    LimbLoft.Claw(m, frame.MultiplyPoint3x4(basePt), frame.MultiplyVector(cd), frame.MultiplyVector(Vector3.up),
                                  s.clawThickness * rTip, cl, s.clawCurveDeg > 0f ? s.clawCurveDeg : 60f, s.clawFlat > 0f ? s.clawFlat : 0.65f,
                                  bone, PartMeshEmitter.PartFoot, 6, arc + len);
                }
                roots[i] = root; dirs[i] = dir; lens[i] = len;
            }
            Web(m, frame, skin, roots, dirs, lens, rt, Mathf.Clamp01(s.web), arc);
        }

        // Webbing between neighbouring digits: a thin two-sided membrane from their roots to `web` of their length,
        // the free edge scalloped back between them.
        static void Web(PartMeshEmitter m, Matrix4x4 frame, Vector4 skin, Vector3[] roots, Vector3[] dirs, float[] lens, float rt, float web, float arc)
        {
            int n = roots.Length;
            if (web <= 0f || n < 2) return;
            const int kRows = 4, kCols = 4;
            float wy = 0.65f * rt, thick = 0.1f * rt;
            for (int i = 0; i + 1 < n; i++)
                for (int side = 0; side < 2; side++)
                {
                    float up = side == 0 ? 1f : -1f;
                    Vector3 nrm = frame.MultiplyVector(Vector3.up * up).normalized;
                    int first = m.VertexCount;
                    for (int r = 0; r <= kRows; r++)
                    {
                        float q = web * r / kRows;
                        for (int c = 0; c <= kCols; c++)
                        {
                            float f = (float)c / kCols;
                            // the edge row is drawn back toward the roots between the digits (a scallop)
                            float scallop = 1f - 0.3f * Mathf.Sin(Mathf.PI * f) * (r / (float)kRows);
                            Vector3 pa = roots[i] + dirs[i] * (lens[i] * q * (c == 0 ? 1f : scallop));
                            Vector3 pb = roots[i + 1] + dirs[i + 1] * (lens[i + 1] * q * (c == kCols ? 1f : scallop));
                            Vector3 pp = Vector3.Lerp(pa, pb, f);
                            float arch = 0.35f * rt * Mathf.Sin(Mathf.PI * Mathf.Clamp01(q));
                            Vector3 lp = new Vector3(pp.x, wy + arch + up * thick, pp.z);
                            m.Vertex(frame.MultiplyPoint3x4(lp), nrm, arc + lens[i] * q, f, PartMeshEmitter.PartLeg, nrm.y, skin);
                        }
                    }
                    for (int r = 0; r < kRows; r++)
                        for (int c = 0; c < kCols; c++)
                        {
                            int a0 = first + r * (kCols + 1) + c, a1 = a0 + 1, b0 = a0 + kCols + 1, b1 = b0 + 1;
                            LimbLoft.Tri(m, a0, b0, a1, nrm);
                            LimbLoft.Tri(m, a1, b0, b1, nrm);
                        }
                }
        }
    }
}
