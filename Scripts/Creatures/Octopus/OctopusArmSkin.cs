using System;
using UnityEngine;

// Curve skinning for the octopus's arm tubes (CreatureSkin.hlsl, OCTOPUS ARM CURVE, mirrors this line by line).
//
// Linear blending of two bone matrices draws a joint as the chords of the two segments with the ring between
// them squeezed: past ~70-90 deg per joint (a wrap round a thin branch, a tight curl) the inner side of the
// tube crossed itself and turned inside out, though a smooth tube there would be fine. Here a vertex between
// consecutive bones e (earlier) and l (later), at t = its weight on l (0 at the middle of e's segment, 0.5 at
// the joint, 1 at the middle of l's segment - linear in arc length), sits on a circular arc from the middle of
// e's segment, leaving along e and turning at a constant rate to l's direction, its end put onto the middle of
// l's segment (tangent-continuous from joint region to joint region, exact for a straight arm). Its ring
// offset (the later bone's local x, y) is laid out in a frame that turns with the curve's tangent, its x axis
// blended between the bones' (twist), scaled by the blended cross-section.
// The flag is the mesh's uv3 = (rest segment spacing, 0, 0, 0) on such vertices (no extra weights).
public static class OctopusArmSkin
{
    public static Vector3 Skin(in Matrix4x4 skinE, in Matrix4x4 skinL, in Matrix4x4 bindE, in Matrix4x4 bindL,
                               Vector3 rest, Vector3 restNormal, float t, float spacing, out Vector3 normal)
    {
        Matrix4x4 we = OctoMath.MulAffine(skinE, RigidInverse(bindE)), wl = OctoMath.MulAffine(skinL, RigidInverse(bindL));
        Vector3 local = OctoMath.MulPoint(bindL, rest), nl = OctoMath.MulVector(bindL, restNormal);
        float h = 0.5f * spacing, u = 1f - t;
        Vector3 p = new Vector3(wl.m03, wl.m13, wl.m23);
        Vector3 m1 = OctoMath.MulPoint(wl, new Vector3(0f, 0f, h)), m0 = OctoMath.MulPoint(we, new Vector3(0f, 0f, h));
        // a circular arc leaving m0 along e's segment and turning at a constant rate to l's direction (its end put
        // onto m1 by a linear correction): the bend spread evenly over the joint region
        Vector3 l0 = p - m0, l1 = m1 - p;
        float a0 = l0.magnitude, a1 = l1.magnitude;
        Vector3 d0 = a0 > 1e-12f ? l0 / a0 : l1 / Mathf.Max(1e-12f, a1), d1 = a1 > 1e-12f ? l1 / a1 : d0;
        float cth = Mathf.Clamp(Vector3.Dot(d0, d1), -1f, 1f), th = MathF.Acos(cth);
        Vector3 c, tng;
        if (th < 1e-3f) { c = m0 + (m1 - m0) * t; tng = d0; }
        else
        {
            Vector3 nb = d1 - d0 * cth;
            nb /= Mathf.Max(1e-12f, nb.magnitude);
            float r = (a0 + a1) * 0.5f / MathF.Tan(0.5f * th);   // the arc tangent to both legs (equal legs: exact)
            float st = MathF.Sin(th * t), ct = MathF.Cos(th * t);
            Vector3 arc1 = m0 + (d0 * MathF.Sin(th) + nb * (1f - cth)) * r;
            c = m0 + (d0 * st + nb * (1f - ct)) * r + (m1 - arc1) * t;
            tng = d0 * ct + nb * st;
        }
        Vector3 xe = new Vector3(we.m00, we.m10, we.m20), xl = new Vector3(wl.m00, wl.m10, wl.m20);
        Vector3 ye = new Vector3(we.m01, we.m11, we.m21), yl = new Vector3(wl.m01, wl.m11, wl.m21);
        Vector3 ze = new Vector3(we.m02, we.m12, we.m22), zl = new Vector3(wl.m02, wl.m12, wl.m22);
        float lxe = xe.magnitude, lxl = xl.magnitude;
        float sx = lxe * u + lxl * t, sy = ye.magnitude * u + yl.magnitude * t, sz = ze.magnitude * u + zl.magnitude * t;
        float tl = tng.magnitude;
        Vector3 tn = tl > 1e-12f ? tng / tl : zl / Mathf.Max(1e-12f, zl.magnitude);
        Vector3 xs = xe * (u / Mathf.Max(1e-12f, lxe)) + xl * (t / Mathf.Max(1e-12f, lxl));
        xs -= tn * Vector3.Dot(xs, tn);
        float xsl = xs.magnitude;
        Vector3 xd = xsl > 1e-9f ? xs / xsl : xl / Mathf.Max(1e-12f, lxl);
        Vector3 yd = Vector3.Cross(tn, xd);
        Vector3 n = xd * (nl.x / sx) + yd * (nl.y / sy) + tn * (nl.z / sz);
        float nlen = n.magnitude;
        normal = nlen > 1e-12f ? n / nlen : yd;
        return c + xd * (sx * local.x) + yd * (sy * local.y);
    }

    // The bind poses are rigid (rest bone world at scale 1): the inverse is the transposed rotation.
    public static Matrix4x4 RigidInverse(in Matrix4x4 b)
    {
        Matrix4x4 m = default;
        m.m00 = b.m00; m.m01 = b.m10; m.m02 = b.m20;
        m.m10 = b.m01; m.m11 = b.m11; m.m12 = b.m21;
        m.m20 = b.m02; m.m21 = b.m12; m.m22 = b.m22;
        m.m03 = -(m.m00 * b.m03 + m.m01 * b.m13 + m.m02 * b.m23);
        m.m13 = -(m.m10 * b.m03 + m.m11 * b.m13 + m.m12 * b.m23);
        m.m23 = -(m.m20 * b.m03 + m.m21 * b.m13 + m.m22 * b.m23);
        m.m33 = 1f;
        return m;
    }

    // Whether a vertex (uv1 part, uv2 skin, uv3 extra) takes the curve skin.
    public static bool Takes(Vector2 uv1, Vector4 uv2, Vector4 uv3) =>
        Mathf.RoundToInt(uv1.x) == OctopusMeshBuilder.PartArm && uv3.x > 0f && uv3.z == 0f && uv3.w == 0f
        && Mathf.Abs(Mathf.Round(uv2.x) - Mathf.Round(uv2.y)) == 1f;
}
