// Creature kit (2026-10-20, the Delver): ShellBand - one armour band as a closed thin shell, the geometry BandShell
// and SerratedRings share. A band lies in the frame of the spine joint it is rigid to (x right, y up, z to the head):
// an arched section round the joint (a superellipse of half-axes A, B about (0, yc), from angle lo to hi: 0 = the
// right flank, pi/2 = the top, pi = the left flank) swept along z from its front edge (zFront) to its rear edge
// (zRear, scalloped into rounded lobes), standing a little proud at the rear and tucked at the front (`shingle`), so a
// band's rear edge always lies over the next band's tucked front edge: the bands shingle like an armadillo's or a
// pangolin's. Its rear edge rolls into a lip. Outer and inner surfaces `thick` apart, closed by hard edges.
// Everything is analytic: Surface() gives a point of the band, Clearance() how far a point lies under its inner
// surface (the tests' "plates never clip through each other"). Mesh emission runs once per species (may allocate).
using UnityEngine;

namespace CreatureKit
{
    // A part made of ShellBands (BandShell, SerratedRings): its bands for tests and posture code. Band k is rigid to
    // `joint`, `local` its frame in that joint's frame (size 1, the rest pose's spacing).
    public interface IShellBands
    {
        int BandCount { get; }
        ShellBand BandAt(CreatureSpecies sp, CreatureSim rest, int k, out int joint, out Matrix4x4 local);
    }

    public struct ShellBand
    {
        public float A, B, yc;            // half-axes of the outer section at the band's middle (u = 0.5), its centre height
        public float lo, hi;              // angle range (radians), lo < hi; lo < 0 curls under the right flank
        public float zFront, zRear;       // z of the front edge and of the rear edge at a lobe's tip (zRear < zFront)
        public float shingle;             // the section grows by +- this from the front (tucked) to the rear (proud)
        public float thick;               // shell thickness
        public float flare;               // extra radius at the lower edges (below the band's centre height)
        public float lip;                 // rolled rear rim: extra radius over the last 25 % of the band
        public int lobes;                 // scallops along the rear edge (0 = a straight edge)
        public float lobeDepth;           // how far the rear edge draws back between lobes (metres)
        public float square;              // superellipse exponent (2 = ellipse; > 2 squarer shoulders)
        public float aFront, aRear, bFront, bRear;   // the half-axes at the front and rear edge (0 = A / B): the band follows
                                                     // a growing or shrinking body profile (a parabola through front, middle, rear)

        // The rear edge's z at angle t (lobes: rounded tips, drawn back between them).
        public float RearZ(float t)
        {
            if (lobes <= 0 || lobeDepth <= 0f) return zRear;
            float tau = Mathf.Clamp01((t - lo) / Mathf.Max(1e-6f, hi - lo)) * lobes;
            float f = tau - Mathf.Floor(tau);
            float tip = Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(Mathf.PI * f)));
            return zRear + lobeDepth * (1f - tip);
        }


        // Section radius scale at angle t: unit superellipse direction.
        Vector2 Dir(float t)
        {
            float c = Mathf.Cos(t), s = Mathf.Sin(t);
            float e = Mathf.Max(2f, square);
            float r = 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(c), e) + Mathf.Pow(Mathf.Abs(s), e), 1f / e);
            return new Vector2(c * r, s * r);
        }

        // The extra offset of the surface at (t, u): shingle step, lip, lower-edge flare.
        float Offset(float t, float u)
        {
            // tucked flat over the front (the part that slides under the band ahead), proud over the rear: a band of a bend's
            // inner side slides deep under its neighbour and still meets only that neighbour's raised rear
            float o = shingle * (2f * Mathf.SmoothStep(0f, 1f, (u - 0.3f) / 0.25f) - 1f);
            o += lip * Mathf.SmoothStep(0f, 1f, (u - 0.72f) / 0.28f) * (1f - 0.35f * Mathf.SmoothStep(0.9f, 1f, u));
            float below = Mathf.Max(0f, -Mathf.Sin(t));
            float edge = Mathf.Max(Mathf.Max(0f, (lo + 0.6f) - t) / 0.6f, Mathf.Max(0f, t - (hi - 0.6f)) / 0.6f);
            o += flare * Mathf.Max(below, edge * edge);
            return o;
        }

        // a parabola through (0, f), (0.5, m), (1, r); f or r 0 = m
        static float Q(float f, float m, float r, float u)
        {
            if (f <= 0f) f = m; if (r <= 0f) r = m;
            float a = 2f * f - 4f * m + 2f * r, b = -3f * f + 4f * m - r;
            return f + b * u + a * u * u;
        }

        // A point of the band: angle t, u 0 (front edge) .. 1 (rear edge at t), depth 0 (outer) .. 1 (inner).
        public Vector3 Surface(float t, float u, float depth)
        {
            Vector2 d = Dir(t);
            float off = Offset(t, u) - depth * thick;
            float x = d.x * (Q(aFront, A, aRear, u) + off), y = yc + d.y * (Q(bFront, B, bRear, u) + off);
            float z = Mathf.Lerp(zFront, RearZ(t), u);
            return new Vector3(x, y, z);
        }

        // How far `p` (band frame) lies under the inner surface, in metres along the section's radius (> 0 under it,
        // < 0 through it); +infinity when the band does not cover p's angle and z.
        public float Clearance(Vector3 p)
        {
            float t = Mathf.Atan2((p.y - yc) / Mathf.Max(1e-6f, B), p.x / Mathf.Max(1e-6f, A));
            if (t < lo - 0.5f * Mathf.PI) t += 2f * Mathf.PI;
            if (t < lo || t > hi) return float.PositiveInfinity;
            float zr = RearZ(t);
            float u = (zFront - p.z) / Mathf.Max(1e-6f, zFront - zr);
            if (u < 0f || u > 1f) return float.PositiveInfinity;
            // the inner surface's point on p's own ray from the section centre (a few Newton-like angle corrections; z
            // is held by u, which depends on t only through the scallops)
            float angP = Mathf.Atan2(p.y - yc, p.x);
            Vector3 s = Surface(t, u, 1f);
            for (int k = 0; k < 4; k++)
            {
                float angS = Mathf.Atan2(s.y - yc, s.x);
                float d = Mathf.DeltaAngle(angS * Mathf.Rad2Deg, angP * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                t += d;
                if (t < lo || t > hi) return float.PositiveInfinity;
                zr = RearZ(t);
                u = (zFront - p.z) / Mathf.Max(1e-6f, zFront - zr);
                if (u < 0f || u > 1f) return float.PositiveInfinity;
                s = Surface(t, u, 1f);
            }
            Vector2 radial = new Vector2(s.x, s.y - yc);
            Vector2 q = new Vector2(p.x, p.y - yc);
            return radial.magnitude - q.magnitude;
        }

        // ---------------------------------------------------------------- the mesh

        // Emits the band through `frame` (band frame -> rest world), rigid to `skin`. around: vertices across the arc,
        // along: rows front to rear. arc: uv0.x of the front edge; uLen: its length along the body.
        public void Emit(PartMeshEmitter m, Matrix4x4 frame, Vector4 skin, int part, int around, int along, float arc)
        {
            int na = Mathf.Max(4, around), nu = Mathf.Max(2, along);
            float L = zFront - zRear;
            first = m.VertexCount; normals.Clear();
            var outer = new int[na + 1, nu + 1];
            var inner = new int[na + 1, nu + 1];
            // positions first (two Surface calls a vertex pair), then normals from the grid's own neighbours
            var P = new Vector3[2, na + 1, nu + 1];
            var U = new float[nu + 1];
            for (int k = 0; k <= nu; k++) { float u = (float)k / nu; U[k] = 1f - (1f - u) * (1f - u) * 0.35f - (1f - u) * 0.65f; }   // denser rows toward the lip
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i <= na; i++)
                {
                    float t = Mathf.Lerp(lo, hi, (float)i / na);
                    for (int k = 0; k <= nu; k++) P[pass, i, k] = Surface(t, U[k], pass);
                }
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i <= na; i++)
                {
                    float t = Mathf.Lerp(lo, hi, (float)i / na);
                    for (int k = 0; k <= nu; k++)
                    {
                        Vector3 p = P[pass, i, k];
                        Vector3 dt = P[pass, Mathf.Min(na, i + 1), k] - P[pass, Mathf.Max(0, i - 1), k];
                        Vector3 du = P[pass, i, Mathf.Min(nu, k + 1)] - P[pass, i, Mathf.Max(0, k - 1)];
                        Vector3 radial = new Vector3(p.x, p.y - yc, 0f);
                        Vector3 n = Vector3.Cross(dt, du);
                        if (Vector3.Dot(n, radial) < 0f) n = -n;
                        n = PartMeshEmitter.Unit(n, radial.sqrMagnitude > 1e-12f ? radial.normalized : Vector3.up);
                        if (pass == 1) n = -n;
                        Vector3 wp = frame.MultiplyPoint3x4(p);
                        Vector3 wn = PartMeshEmitter.Unit(frame.MultiplyVector(n), Vector3.up);
                        int vi = m.VertexCount;
                        normals.Add(wn);
                        m.Vertex(wp, wn, arc + L * U[k], (t - lo) / (2f * Mathf.PI), part, Mathf.Sin(t), skin);
                        if (pass == 0) outer[i, k] = vi; else inner[i, k] = vi;
                    }
                }
            for (int i = 0; i < na; i++)
                for (int k = 0; k < nu; k++)
                {
                    Quad(m, outer[i, k], outer[i + 1, k], outer[i, k + 1], outer[i + 1, k + 1]);
                    Quad(m, inner[i, k], inner[i + 1, k], inner[i, k + 1], inner[i + 1, k + 1]);
                }
            // the edges: front (u = 0), rear lip (u = 1), and the two lower edges (t = lo, hi), hard-shaded
            Edge(m, P, U, frame, skin, part, arc, na, nu, true, 0, L);
            Edge(m, P, U, frame, skin, part, arc, na, nu, true, nu, L);
            Edge(m, P, U, frame, skin, part, arc, na, nu, false, 0, L);
            Edge(m, P, U, frame, skin, part, arc, na, nu, false, na, L);
        }

        // the vertex normals this band emitted (index - first), to wind each triangle to their side
        static readonly System.Collections.Generic.List<Vector3> normals = new System.Collections.Generic.List<Vector3>();
        static int first;

        static void Quad(PartMeshEmitter m, int a, int b, int c, int d)
        {
            LimbLoft.Tri(m, a, b, c, N(a) + N(b) + N(c));
            LimbLoft.Tri(m, b, d, c, N(b) + N(d) + N(c));
        }

        static Vector3 N(int i) => normals[i - first];


        // A strip closing the shell between the outer and inner surface along one edge.
        // A strip closing the shell between the outer and inner surface along one edge, from the band's grid (P: outer and
        // inner positions, U: the rows' u).
        void Edge(PartMeshEmitter m, Vector3[,,] P, float[] U, Matrix4x4 frame, Vector4 skin, int part, float arc, int na, int nu, bool acrossT, int at, float L)
        {
            int n = acrossT ? na : nu;
            int e0 = m.VertexCount;
            for (int j = 0; j <= n; j++)
            {
                int i = acrossT ? j : at, k = acrossT ? at : j;
                float t = Mathf.Lerp(lo, hi, (float)i / na), u = U[k];
                Vector3 po = P[0, i, k], pi = P[1, i, k], mid = 0.5f * (po + pi);
                // the edge's outward direction: away from the neighbouring row / column
                int i2 = acrossT ? i : (at == 0 ? 1 : na - 1), k2 = acrossT ? (at == 0 ? 1 : nu - 1) : k;
                Vector3 dir = mid - 0.5f * (P[0, i2, k2] + P[1, i2, k2]);
                Vector3 radial = PartMeshEmitter.Unit(po - pi, new Vector3(mid.x, mid.y - yc, 0f));
                Vector3 nn = PartMeshEmitter.Unit(PartMeshEmitter.Unit(dir, radial) + 0.25f * radial, Vector3.up);
                Vector3 wn = PartMeshEmitter.Unit(frame.MultiplyVector(nn), Vector3.up);
                normals.Add(wn); normals.Add(wn);
                m.Vertex(frame.MultiplyPoint3x4(po), wn, arc + L * u, (t - lo) / (2f * Mathf.PI), part, Mathf.Sin(t), skin);
                m.Vertex(frame.MultiplyPoint3x4(pi), wn, arc + L * u, (t - lo) / (2f * Mathf.PI), part, Mathf.Sin(t), skin);
            }
            for (int j = 0; j < n; j++)
            {
                int a = e0 + 2 * j, b = a + 1, c = a + 2, d = a + 3;
                Quad(m, a, b, c, d);
            }
        }

        // The outward normal of the outer (depth 0) or inner surface at (t, u): finite differences, oriented away from
        // the band's axis.
        public Vector3 Normal(float t, float u, float depth)
        {
            const float h = 1e-3f;
            Vector3 dt = Surface(t + h, u, depth) - Surface(t - h, u, depth);
            float u0 = Mathf.Max(0f, u - h), u1 = Mathf.Min(1f, u + h);
            Vector3 du = Surface(t, u1, depth) - Surface(t, u0, depth);
            Vector3 n = Vector3.Cross(dt, du);
            Vector3 p = Surface(t, u, depth);
            Vector3 radial = new Vector3(p.x, p.y - yc, 0f);
            if (Vector3.Dot(n, radial) < 0f) n = -n;
            return PartMeshEmitter.Unit(n, radial.sqrMagnitude > 1e-12f ? radial.normalized : Vector3.up);
        }
    }
}
