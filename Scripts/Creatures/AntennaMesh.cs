using UnityEngine;

// Mesh of one antenna (Kit/Antenna.cs) in the rest pose: a tapered tube along its strand, a ring at every point
// and one mid-segment; a point's ring blends its two segments' bones 50/50 (a smooth strand), a mid ring is rigid
// to its segment; rounded root (buried in the body) and a fine tip. Part `part` (the membrane shading, part 8:
// translucent like the fins); uv0 = (arc m, 0.1: between the membrane's veins), uv1 = (part, 0.5).
public static class AntennaMesh
{
    const int kSides = 6;
    const float kMinRadius = 1e-4f;

    public static void Build(CreatureMeshLists m, AntennaSpec s, Vector3[] pts, int first, int firstBone, int part)
    {
        int N = s.Segments;
        // samples: root pole, every point (N + 1) and every segment middle (N), the tip pole
        int count = 2 * N + 3;
        var c = new Vector3[count]; var t = new Vector3[count]; var r = new float[count]; var arc = new float[count];
        var skin = new Vector4[count];
        float total = 0f;
        int q = 0;
        for (int k = 0; k <= N; k++)
        {
            Vector3 p = pts[first + k];
            Vector3 dir = k < N ? pts[first + k + 1] - p : p - pts[first + k - 1];
            dir = dir.sqrMagnitude > 1e-18f ? dir.normalized : Vector3.forward;
            float f = k / (float)N;
            if (k == 0)
            {
                // the root pole a little inside, then the root ring
                c[q] = p - dir * Mathf.Max(s.radiusStart, kMinRadius); t[q] = dir; r[q] = 0f; arc[q] = 0f; skin[q] = Rigid(firstBone); q++;
            }
            c[q] = p; t[q] = dir; r[q] = Mathf.Lerp(s.radiusStart, s.radiusEnd, f);
            skin[q] = k == 0 ? Rigid(firstBone) : k == N ? Rigid(firstBone + N - 1) : Blend(firstBone + k - 1, firstBone + k, 0.5f);
            arc[q] = total; q++;
            if (k < N)
            {
                Vector3 nxt = pts[first + k + 1];
                c[q] = 0.5f * (p + nxt); t[q] = dir; r[q] = Mathf.Lerp(s.radiusStart, s.radiusEnd, (k + 0.5f) / N);
                skin[q] = Rigid(firstBone + k); arc[q] = total + 0.5f * (nxt - p).magnitude; q++;
                total += (nxt - p).magnitude;
            }
        }
        // the tip pole
        Vector3 last = pts[first + N], ld = t[q - 1];
        c[q] = last + ld * Mathf.Max(2f * s.radiusEnd, kMinRadius); t[q] = ld; r[q] = 0f; arc[q] = total; skin[q] = Rigid(firstBone + N - 1); q++;

        // rings in a rotation-minimising frame
        Vector3 x = Vector3.Cross(t[0], Mathf.Abs(t[0].y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        int prev = -1; bool prevPole = false;
        for (int i = 0; i < q; i++)
        {
            Vector3 xt = x - t[i] * Vector3.Dot(x, t[i]);
            x = xt.sqrMagnitude > 1e-10f ? xt.normalized : x;
            Vector3 y = Vector3.Cross(t[i], x);
            bool pole = i == 0 || i == q - 1;
            int cur = m.pos.Count;
            for (int k = 0; k <= kSides; k++)
            {
                float phi = 2f * Mathf.PI * k / kSides;
                float cs = k == kSides ? 1f : Mathf.Cos(phi), sn = k == kSides ? 0f : Mathf.Sin(phi);
                Vector3 radial = x * cs + y * sn;
                Vector3 nn = pole ? (i == 0 ? -t[i] : t[i]) : radial;
                m.pos.Add(c[i] + radial * (pole ? 0f : Mathf.Max(kMinRadius, r[i])));
                m.nrm.Add(nn);
                m.uv0.Add(new Vector2(arc[i], 0.1f));
                m.uv1.Add(new Vector2(part, 0.5f));
                m.uv2.Add(skin[i]);
            }
            if (prev >= 0)
                for (int k = 0; k < kSides; k++)
                {
                    if (!prevPole) Tri(m, prev + k, prev + k + 1, cur + k);
                    if (!pole) Tri(m, prevPole ? prev + k : prev + k + 1, cur + k + 1, cur + k);
                }
            prev = cur; prevPole = pole;
        }
    }

    static void Tri(CreatureMeshLists m, int a, int b, int c) { m.tris.Add(a); m.tris.Add(b); m.tris.Add(c); }
    static Vector4 Rigid(int bone) => new Vector4(bone, bone, 1f, 0f);
    static Vector4 Blend(int b0, int b1, float w1) => w1 > 0.5f ? new Vector4(b1, b0, w1, 1f - w1) : new Vector4(b0, b1, 1f - w1, w1);
}
