// Creature kit service (2026-10-20, kit services): capsules fitted to the real mesh, one or more per bone, that ride
// the bones - the body, legs, wings, tails of an individual as solid volumes for contact tests (the group check: no two
// members touch) and future collisions.
//   Fit      once per species: the bind-space vertices each bone carries (its weight >= minWeight), in the bone's
//            frame; their principal axis is the capsule's axis, the ends sit one radius in from the extremes, the radius
//            is the largest distance from the axis. A flat patch (a membrane, a wing panel: its second extent much larger
//            than its thickness) is split across its width into up to four capsules, so a sheet is not a fat cylinder.
//   World    per frame: the capsules in world space from the bone matrices (the bone's scale carries the size).
// Fit allocates; World and the distance helpers do not. Main thread.
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public sealed class BoneCapsules
    {
        public readonly int Count;
        public readonly int[] Bone;        // the bone each capsule rides
        public readonly Vector3[] A, B;    // ends in the bone's frame (the bind's: scale 1)
        public readonly float[] R;         // radius in the bone's frame

        BoneCapsules(List<int> bone, List<Vector3> a, List<Vector3> b, List<float> r)
        {
            Count = bone.Count; Bone = bone.ToArray(); A = a.ToArray(); B = b.ToArray(); R = r.ToArray();
        }

        // vertices: the mesh's (bind space); skin: uv2 (bone0, bone1, w0, w1); bind: inverse rest bone matrices.
        public static BoneCapsules Fit(Vector3[] vertices, List<Vector4> skin, Matrix4x4[] bind, float minWeight = 0.5f)
        {
            int bones = bind.Length;
            var pts = new List<Vector3>[bones];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector4 s = skin[i];
                int b = s.z >= s.w ? (int)s.x : (int)s.y;
                float w = Mathf.Max(s.z, s.w);
                if (w < minWeight || b < 0 || b >= bones) continue;
                (pts[b] ?? (pts[b] = new List<Vector3>())).Add(bind[b].MultiplyPoint3x4(vertices[i]));
            }
            var bo = new List<int>(); var aa = new List<Vector3>(); var bb = new List<Vector3>(); var rr = new List<float>();
            for (int b = 0; b < bones; b++)
            {
                List<Vector3> p = pts[b];
                if (p == null || p.Count < 3) continue;
                Vector3 c = Vector3.zero;
                foreach (Vector3 q in p) c += q;
                c /= p.Count;
                Vector3 ax = Principal(p, c, Vector3.zero), side = Principal(p, c, ax);
                Vector3 up = Vector3.Cross(ax, side);
                float t0 = float.MaxValue, t1 = float.MinValue, s0 = float.MaxValue, s1 = float.MinValue, thick = 0f;
                foreach (Vector3 q in p)
                {
                    Vector3 d = q - c;
                    float t = Vector3.Dot(d, ax), u = Vector3.Dot(d, side);
                    t0 = Mathf.Min(t0, t); t1 = Mathf.Max(t1, t); s0 = Mathf.Min(s0, u); s1 = Mathf.Max(s1, u);
                    thick = Mathf.Max(thick, Mathf.Abs(Vector3.Dot(d, up)));
                }
                // a sheet: split across its width
                int strips = Mathf.Clamp(Mathf.FloorToInt((s1 - s0) / Mathf.Max(4f * thick, 1e-6f)), 1, 4);
                float sw = (s1 - s0) / strips;
                for (int k = 0; k < strips; k++)
                {
                    float lo = s0 + k * sw, hi = lo + sw, mid = 0.5f * (lo + hi);
                    float r = 0f, ta = float.MaxValue, tb = float.MinValue;
                    int n = 0;
                    foreach (Vector3 q in p)
                    {
                        Vector3 d = q - c;
                        float u = Vector3.Dot(d, side);
                        if (strips > 1 && (u < lo - 1e-6f || u > hi + 1e-6f)) continue;
                        float t = Vector3.Dot(d, ax);
                        ta = Mathf.Min(ta, t); tb = Mathf.Max(tb, t);
                        Vector3 perp = d - ax * t - (strips > 1 ? side * mid : Vector3.zero);
                        r = Mathf.Max(r, perp.magnitude);
                        n++;
                    }
                    if (n == 0) continue;
                    Vector3 o = c + (strips > 1 ? side * mid : Vector3.zero);
                    float half = 0.5f * (tb - ta);
                    if (r >= half)
                    {
                        // round: one sphere at the middle holding every point
                        Vector3 m = o + ax * (0.5f * (ta + tb));
                        float rs = 0f;
                        foreach (Vector3 q in p)
                        {
                            float u = Vector3.Dot(q - c, side);
                            if (strips > 1 && (u < lo - 1e-6f || u > hi + 1e-6f)) continue;
                            rs = Mathf.Max(rs, (q - m).magnitude);
                        }
                        bo.Add(b); aa.Add(m); bb.Add(m); rr.Add(rs);
                    }
                    else { bo.Add(b); aa.Add(o + ax * (ta + r)); bb.Add(o + ax * (tb - r)); rr.Add(r); }
                }
            }
            return new BoneCapsules(bo, aa, bb, rr);
        }

        // The dominant direction of the points about c, orthogonal to `not` (power iteration on the covariance).
        static Vector3 Principal(List<Vector3> p, Vector3 c, Vector3 not)
        {
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (Vector3 q in p)
            {
                Vector3 d = q - c;
                if (not != Vector3.zero) d -= not * Vector3.Dot(d, not);
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }
            Vector3 v = not == Vector3.zero ? new Vector3(0.3f, 0.5f, 0.81f) : Vector3.Cross(not, Mathf.Abs(not.y) < 0.9f ? Vector3.up : Vector3.right);
            for (int it = 0; it < 32; it++)
            {
                Vector3 w = new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z);
                if (not != Vector3.zero) w -= not * Vector3.Dot(w, not);
                float m = w.magnitude;
                if (m < 1e-20f) break;
                v = w / m;
            }
            if (not != Vector3.zero) v = (v - not * Vector3.Dot(v, not)).normalized;
            return v.sqrMagnitude > 0.5f ? v : (not == Vector3.zero ? Vector3.forward : Vector3.Cross(not, Vector3.up).normalized);
        }

        // The capsules in world space from the bone matrices (bones: the sim's WriteBoneWorld). The radius scales with the
        // bone's largest axis scale.
        public void World(Matrix4x4[] bones, Vector3[] a, Vector3[] b, float[] r)
        {
            for (int i = 0; i < Count; i++)
            {
                Matrix4x4 m = bones[Bone[i]];
                a[i] = m.MultiplyPoint3x4(A[i]); b[i] = m.MultiplyPoint3x4(B[i]);
                float sx = ((Vector3)m.GetColumn(0)).magnitude, sy = ((Vector3)m.GetColumn(1)).magnitude, sz = ((Vector3)m.GetColumn(2)).magnitude;
                r[i] = R[i] * Mathf.Max(sx, Mathf.Max(sy, sz));
            }
        }

        // The closest distance between segments p1q1 and p2q2 (Ericson, Real-Time Collision Detection 5.1.9).
        public static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a <= 1e-12f && e <= 1e-12f) { s = t = 0f; }
            else if (a <= 1e-12f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-12f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-12f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s; c2 = p2 + d2 * t;
            return (c1 - c2).magnitude;
        }
    }
}
