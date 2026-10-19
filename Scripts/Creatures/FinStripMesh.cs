using System;
using UnityEngine;

// A spine cross-section in the rest pose (CreatureMeshBuilder's Spine section): centre, unit tangent toward the
// head, unit side (x) and up (y) axes, ellipse semi-axes a (along x) and b (along y).
public struct SpineSection
{
    public Vector3 c, t, x, y;
    public float a, b;
}

// Mesh of a FinStrip (Kit/FinStrip.cs) in the rest pose: a double-sided membrane strip whose base row sits on the
// body surface at aroundDeg and whose rows rise along a curve that sweeps back by leanDeg; the free edge is
// scalloped into tufts and the ends taper. Row r (fraction v of the height) is skinned to the spine at the base's
// joint coordinate + flowJoints x v x (local height / tallest): the strip trails the body, its tall stretches most. Membrane part 8; uv0 = (arc along the strip m,
// fin-ray coordinate: thick rays at the tufts' crests), uv1 = (8, sheet sign x (0.1 + 0.9 v)): the rim is the tip.
public static class FinStripMesh
{
    public const int Rows = 5;               // rows over the height (Rows + 1 vertex rows)
    const float kEndTaper = 0.12f;           // the ends taper over this fraction of the length
    const float kMinEnd = 0.4f;              // ... down to this fraction of the height
    const float kSheetOffset = 1e-4f;        // the two sheets 0.1 mm apart along their normals (no depth fight)

    public static void Build(CreatureMeshLists m, FinStripSpec f, Func<float, SpineSection> spine, int joints, int part)
    {
        for (int side = 0; side < (f.mirror && f.aroundDeg != 0f ? 2 : 1); side++)
            BuildOne(m, f, spine, joints, part, side == 0 ? f.aroundDeg : -f.aroundDeg);
    }

    static void BuildOne(CreatureMeshLists m, FinStripSpec f, Func<float, SpineSection> spine, int joints, int part, float aroundDeg)
    {
        int cols = f.Columns, rows = Rows;
        int a0 = Mathf.Clamp(f.fromJoint, 0, joints - 1), a1 = Mathf.Clamp(f.toJoint, 0, joints - 1);
        var p = new Vector3[(cols + 1) * (rows + 1)];
        var skin = new Vector4[p.Length];
        var arc = new float[cols + 1];
        float lean = Mathf.Clamp(f.leanDeg, 0f, 90f) * Mathf.Deg2Rad, th = aroundDeg * Mathf.Deg2Rad;
        // the trail scales with the local height: a low stretch (a valley between tufts, a tapered end) lags less,
        // so its small quads are not sheared by frames a whole joint apart
        float hMax = 1e-6f;
        for (int c = 0; c <= cols; c++) { float fr = c / (float)cols; hMax = Mathf.Max(hMax, Mathf.Lerp(f.heightStart, f.heightEnd, fr) * Profile(f, fr)); }
        for (int c = 0; c <= cols; c++)
        {
            float fr = c / (float)cols;
            float u = Mathf.Lerp(a0, a1, fr);
            SpineSection s = spine(u);
            // the base on the ellipse at angle th from the back, toward +x for th > 0
            Vector3 dir = (s.y * Mathf.Cos(th) + s.x * Mathf.Sin(th)).normalized;
            float sa = Mathf.Sin(th) / Mathf.Max(1e-5f, s.a), cb = Mathf.Cos(th) / Mathf.Max(1e-5f, s.b);
            float r = 1f / Mathf.Sqrt(sa * sa + cb * cb);
            Vector3 basePt = s.c + dir * (r * (1f - Mathf.Clamp01(f.inset)));
            float h = Mathf.Lerp(f.heightStart, f.heightEnd, fr) * Profile(f, fr);
            if (c > 0) arc[c] = arc[c - 1] + (p[(c - 1) * (rows + 1)] - basePt).magnitude; // base arc length
            for (int k = 0; k <= rows; k++)
            {
                float v = k / (float)rows;
                // a curve that turns back from `dir` toward the tail by lean x v (integrated: a circular arc)
                Vector3 off = lean > 1e-4f
                    ? (dir * Mathf.Sin(lean * v) - s.t * (1f - Mathf.Cos(lean * v))) * (h / lean)
                    : dir * (h * v);
                p[c * (rows + 1) + k] = basePt + off;
                skin[c * (rows + 1) + k] = SpineSkin(u + Mathf.Max(0f, f.flowJoints) * v * (h / hMax), joints);
            }
        }
        // two sheets (normals +n, -n); vertex normals from the grid's tangents
        for (int sheet = 0; sheet < 2; sheet++)
        {
            float sign = sheet == 0 ? 1f : -1f;
            int first = m.pos.Count;
            for (int c = 0; c <= cols; c++)
                for (int k = 0; k <= rows; k++)
                {
                    int i = c * (rows + 1) + k;
                    Vector3 dc = p[Mathf.Min(c + 1, cols) * (rows + 1) + k] - p[Mathf.Max(c - 1, 0) * (rows + 1) + k];
                    Vector3 dr = p[c * (rows + 1) + Mathf.Min(k + 1, rows)] - p[c * (rows + 1) + Mathf.Max(k - 1, 0)];
                    Vector3 n = Vector3.Cross(dc, dr);
                    n = CreatureKit.PartMeshEmitter.Unit(n, Vector3.right);   // never .normalized (zero below 1e-5)
                    n *= sign;
                    float v = k / (float)rows;
                    m.pos.Add(p[i] + n * kSheetOffset);
                    m.nrm.Add(n);
                    m.uv0.Add(new Vector2(arc[c], (float)c / cols * Mathf.Max(1, f.tufts) / 5f));
                    m.uv1.Add(new Vector2(part, sign * (0.1f + 0.9f * v)));
                    m.uv2.Add(skin[i]);
                }
            for (int c = 0; c < cols; c++)
                for (int k = 0; k < rows; k++)
                {
                    int i00 = first + c * (rows + 1) + k, i10 = i00 + rows + 1, i01 = i00 + 1, i11 = i10 + 1;
                    // split the quad along the diagonal that gives the better-shaped triangles (steep tufts curl a
                    // quad into a sliver along one diagonal); winding with the sheet's normal: cross(dc, dr) is the
                    // +n sheet's front
                    int q00 = c * (rows + 1) + k, q10 = q00 + rows + 1, q01 = q00 + 1, q11 = q10 + 1;
                    bool main = Math.Min(Shape(p[q00], p[q10], p[q11]), Shape(p[q00], p[q11], p[q01]))
                              >= Math.Min(Shape(p[q00], p[q10], p[q01]), Shape(p[q10], p[q11], p[q01]));
                    if (main)
                    {
                        if (sheet == 0) { Tri(m, i00, i10, i11); Tri(m, i00, i11, i01); }
                        else { Tri(m, i00, i11, i10); Tri(m, i00, i01, i11); }
                    }
                    else
                    {
                        if (sheet == 0) { Tri(m, i00, i10, i01); Tri(m, i10, i11, i01); }
                        else { Tri(m, i00, i01, i10); Tri(m, i10, i01, i11); }
                    }
                }
        }
    }

    static void Tri(CreatureMeshLists m, int a, int b, int c) { m.tris.Add(a); m.tris.Add(b); m.tris.Add(c); }

    // A triangle's shape quality: area over its longest edge squared (0 = a sliver).
    static float Shape(Vector3 a, Vector3 b, Vector3 c)
    {
        float l = Mathf.Max((b - a).sqrMagnitude, Mathf.Max((c - b).sqrMagnitude, (a - c).sqrMagnitude));
        return l > 0f ? Vector3.Cross(b - a, c - a).magnitude / l : 0f;
    }

    // Height profile along the strip: tufts (peaks at k / tufts, smooth valleys) and tapered ends.
    public static float Profile(FinStripSpec f, float fr)
    {
        float tuft = f.tufts > 0 ? 1f - Mathf.Clamp(f.tuftDepth, 0f, 0.9f) * Sq(Mathf.Sin(Mathf.PI * f.tufts * fr)) : 1f;
        float end = Mathf.Min(Smooth(fr / kEndTaper), Smooth((1f - fr) / kEndTaper));
        return tuft * (kMinEnd + (1f - kMinEnd) * end);
    }

    static float Sq(float x) => x * x;
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    // Skin weights at a spine coordinate u (joint units): the two joints around it, smoothstep-blended
    // (CreatureMeshBuilder's body rings), bone0 the heavier one.
    public static Vector4 SpineSkin(float u, int joints)
    {
        u = Mathf.Clamp(u, 0f, joints - 1);
        int i = Mathf.Min((int)u, joints - 2);
        float w = u - i;
        w = w * w * (3f - 2f * w);
        return w > 0.5f ? new Vector4(i + 1, i, w, 1f - w) : new Vector4(i, i + 1, 1f - w, w);
    }
}
