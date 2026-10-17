using System;
using System.Runtime.CompilerServices;
using UnityEngine;

// Pure math for the octopus simulation and mesh. Two rules:
//  - no Unity engine calls (Quaternion.AngleAxis, LookRotation, Slerp, Inverse and Matrix4x4.TRS / inverse
//    are native), so the octopus sim runs off the engine too (tests, prototypes);
//  - no UnityEngine.Vector3 / Quaternion operators, constructors or Mathf in anything a tick calls: in the
//    Editor those are real calls into UnityEngine.CoreModule (it ships with JIT optimisation disabled), so
//    results are built by field writes and maths goes through System.MathF.
internal static class OctoMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 V(float x, float y, float z) { Vector3 v = default; v.x = x; v.y = y; v.z = z; return v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion Q(float x, float y, float z, float w) { Quaternion q = default; q.x = x; q.y = y; q.z = z; q.w = w; return q; }

    public static Vector3 Norm(Vector3 v, Vector3 fallback)
    {
        float m = v.x * v.x + v.y * v.y + v.z * v.z;
        if (!(m > 1e-14f)) return fallback;
        float inv = 1f / MathF.Sqrt(m);
        return V(v.x * inv, v.y * inv, v.z * inv);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Cross(Vector3 a, Vector3 b) => V(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

    public static bool Finite(Vector3 v) => !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z);

    // v rotated about the unit axis a by rad (Rodrigues).
    public static Vector3 Rotate(Vector3 v, Vector3 a, float rad)
    {
        float c = MathF.Cos(rad), s = MathF.Sin(rad), ad = Dot(a, v) * (1f - c);
        return V(v.x * c + (a.y * v.z - a.z * v.y) * s + a.x * ad,
                 v.y * c + (a.z * v.x - a.x * v.z) * s + a.y * ad,
                 v.z * c + (a.x * v.y - a.y * v.x) * s + a.z * ad);
    }

    // q * (x, y, z) without the engine operator.
    public static Vector3 Rotate(Quaternion q, float x, float y, float z)
    {
        float tx = 2f * (q.y * z - q.z * y), ty = 2f * (q.z * x - q.x * z), tz = 2f * (q.x * y - q.y * x);
        return V(x + q.w * tx + (q.y * tz - q.z * ty), y + q.w * ty + (q.z * tx - q.x * tz), z + q.w * tz + (q.x * ty - q.y * tx));
    }

    public static Vector3 Rotate(Quaternion q, Vector3 v) => Rotate(q, v.x, v.y, v.z);

    // a * b without the engine operator.
    public static Quaternion Mul(Quaternion a, Quaternion b) => Q(
        a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
        a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
        a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
        a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

    public static Quaternion AxisAngle(Vector3 unitAxis, float rad)
    {
        float h = 0.5f * rad, s = MathF.Sin(h);
        return Q(unitAxis.x * s, unitAxis.y * s, unitAxis.z * s, MathF.Cos(h));
    }

    public static Quaternion Conj(Quaternion q) => Q(-q.x, -q.y, -q.z, q.w);

    public static Quaternion Normalize(Quaternion q)
    {
        float m = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (!(m > 1e-12f)) return Q(0f, 0f, 0f, 1f);
        float inv = 1f / m;
        return Q(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
    }

    // Rotation whose columns are the orthonormal basis (x, y, z) (right-handed: x = y cross z).
    public static Quaternion FromBasis(Vector3 x, Vector3 y, Vector3 z) => FromBasis(x.x, x.y, x.z, y.x, y.y, y.z, z.x, z.y, z.z);

    public static Quaternion FromBasis(float xx, float xy, float xz, float yx, float yy, float yz, float zx, float zy, float zz)
    {
        float tr = xx + yy + zz;
        Quaternion q;
        if (tr > 0f)
        {
            float s = MathF.Sqrt(tr + 1f) * 2f, i = 1f / s;
            q = Q((yz - zy) * i, (zx - xz) * i, (xy - yx) * i, 0.25f * s);
        }
        else if (xx > yy && xx > zz)
        {
            float s = MathF.Sqrt(1f + xx - yy - zz) * 2f, i = 1f / s;
            q = Q(0.25f * s, (yx + xy) * i, (zx + xz) * i, (yz - zy) * i);
        }
        else if (yy > zz)
        {
            float s = MathF.Sqrt(1f + yy - xx - zz) * 2f, i = 1f / s;
            q = Q((yx + xy) * i, 0.25f * s, (zy + yz) * i, (zx - xz) * i);
        }
        else
        {
            float s = MathF.Sqrt(1f + zz - xx - yy) * 2f, i = 1f / s;
            q = Q((zx + xz) * i, (zy + yz) * i, 0.25f * s, (xy - yx) * i);
        }
        return Normalize(q);
    }

    // Quaternion.LookRotation without the engine: +Z = forward, +Y = up made perpendicular.
    public static Quaternion Look(Vector3 forward, Vector3 up)
    {
        Vector3 z = Norm(forward, V(0f, 0f, 1f));
        Vector3 x = Cross(up, z);
        if (Dot(x, x) < 1e-10f) x = Cross(MathF.Abs(z.y) < 0.9f ? V(0f, 1f, 0f) : V(1f, 0f, 0f), z);
        x = Norm(x, V(1f, 0f, 0f));
        return FromBasis(x, Cross(z, x), z);
    }

    // Matrix4x4.TRS without the engine; scale is per local axis (s = (sx, sy, sz)).
    public static Matrix4x4 TRS(Vector3 p, Quaternion q, float sx, float sy, float sz)
    {
        float x = q.x, y = q.y, z = q.z, w = q.w;
        float xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
        Matrix4x4 m = default;
        m.m00 = (1f - 2f * (yy + zz)) * sx; m.m10 = 2f * (xy + wz) * sx; m.m20 = 2f * (xz - wy) * sx;
        m.m01 = 2f * (xy - wz) * sy; m.m11 = (1f - 2f * (xx + zz)) * sy; m.m21 = 2f * (yz + wx) * sy;
        m.m02 = 2f * (xz + wy) * sz; m.m12 = 2f * (yz - wx) * sz; m.m22 = (1f - 2f * (xx + yy)) * sz;
        m.m03 = p.x; m.m13 = p.y; m.m23 = p.z; m.m33 = 1f;
        return m;
    }

    public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) => TRS(p, q, s.x, s.y, s.z);

    // Affine matrix [scale * A | t].
    public static Matrix4x4 Affine(in M3 a, float scale, Vector3 t)
    {
        Matrix4x4 m = default;
        m.m00 = a.m00 * scale; m.m01 = a.m01 * scale; m.m02 = a.m02 * scale; m.m03 = t.x;
        m.m10 = a.m10 * scale; m.m11 = a.m11 * scale; m.m12 = a.m12 * scale; m.m13 = t.y;
        m.m20 = a.m20 * scale; m.m21 = a.m21 * scale; m.m22 = a.m22 * scale; m.m23 = t.z;
        m.m33 = 1f;
        return m;
    }

    // Inverse of an affine matrix (last row 0 0 0 1).
    public static Matrix4x4 InverseAffine(Matrix4x4 m)
    {
        M3 a = new M3(m.m00, m.m01, m.m02, m.m10, m.m11, m.m12, m.m20, m.m21, m.m22).Inverse();
        Vector3 t = a.Mul(V(m.m03, m.m13, m.m23));
        return Affine(a, 1f, V(-t.x, -t.y, -t.z));
    }

    // Matrix product a * b of affine matrices without the engine operator.
    public static Matrix4x4 MulAffine(in Matrix4x4 a, in Matrix4x4 b)
    {
        Matrix4x4 m = default;
        m.m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20;
        m.m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21;
        m.m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22;
        m.m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03;
        m.m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20;
        m.m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21;
        m.m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22;
        m.m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13;
        m.m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20;
        m.m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21;
        m.m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22;
        m.m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23;
        m.m33 = 1f;
        return m;
    }

    public static Vector3 MulPoint(in Matrix4x4 m, Vector3 p) =>
        V(m.m00 * p.x + m.m01 * p.y + m.m02 * p.z + m.m03, m.m10 * p.x + m.m11 * p.y + m.m12 * p.z + m.m13, m.m20 * p.x + m.m21 * p.y + m.m22 * p.z + m.m23);

    public static Vector3 MulVector(in Matrix4x4 m, Vector3 p) =>
        V(m.m00 * p.x + m.m01 * p.y + m.m02 * p.z, m.m10 * p.x + m.m11 * p.y + m.m12 * p.z, m.m20 * p.x + m.m21 * p.y + m.m22 * p.z);

    public static float Smooth(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }
}

// Row-major 3x3 matrix (value type, no allocation).
internal struct M3
{
    public float m00, m01, m02, m10, m11, m12, m20, m21, m22;

    public M3(float a00, float a01, float a02, float a10, float a11, float a12, float a20, float a21, float a22)
    {
        m00 = a00; m01 = a01; m02 = a02; m10 = a10; m11 = a11; m12 = a12; m20 = a20; m21 = a21; m22 = a22;
    }

    public static M3 Identity => new M3(1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f);
    public static M3 Scale(float s) => new M3(s, 0f, 0f, 0f, s, 0f, 0f, 0f, s);

    // a b^T
    public static M3 Outer(Vector3 a, Vector3 b) =>
        new M3(a.x * b.x, a.x * b.y, a.x * b.z, a.y * b.x, a.y * b.y, a.y * b.z, a.z * b.x, a.z * b.y, a.z * b.z);

    public void AddOuter(Vector3 a, Vector3 b, float w)
    {
        float ax = a.x * w, ay = a.y * w, az = a.z * w;
        m00 += ax * b.x; m01 += ax * b.y; m02 += ax * b.z;
        m10 += ay * b.x; m11 += ay * b.y; m12 += ay * b.z;
        m20 += az * b.x; m21 += az * b.y; m22 += az * b.z;
    }

    public static M3 FromQuaternion(Quaternion q)
    {
        float x = q.x, y = q.y, z = q.z, w = q.w;
        return new M3(1f - 2f * (y * y + z * z), 2f * (x * y - z * w), 2f * (x * z + y * w),
                      2f * (x * y + z * w), 1f - 2f * (x * x + z * z), 2f * (y * z - x * w),
                      2f * (x * z - y * w), 2f * (y * z + x * w), 1f - 2f * (x * x + y * y));
    }

    public Vector3 Col(int c) => c == 0 ? OctoMath.V(m00, m10, m20) : c == 1 ? OctoMath.V(m01, m11, m21) : OctoMath.V(m02, m12, m22);

    public Vector3 Mul(Vector3 v) =>
        OctoMath.V(m00 * v.x + m01 * v.y + m02 * v.z, m10 * v.x + m11 * v.y + m12 * v.z, m20 * v.x + m21 * v.y + m22 * v.z);

    public static M3 operator *(in M3 a, in M3 b) => new M3(
        a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20, a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21, a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22,
        a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20, a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21, a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22,
        a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20, a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21, a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22);

    public static M3 operator *(in M3 a, float s) => new M3(a.m00 * s, a.m01 * s, a.m02 * s, a.m10 * s, a.m11 * s, a.m12 * s, a.m20 * s, a.m21 * s, a.m22 * s);

    public static M3 Lerp(in M3 a, in M3 b, float t) => new M3(
        a.m00 + (b.m00 - a.m00) * t, a.m01 + (b.m01 - a.m01) * t, a.m02 + (b.m02 - a.m02) * t,
        a.m10 + (b.m10 - a.m10) * t, a.m11 + (b.m11 - a.m11) * t, a.m12 + (b.m12 - a.m12) * t,
        a.m20 + (b.m20 - a.m20) * t, a.m21 + (b.m21 - a.m21) * t, a.m22 + (b.m22 - a.m22) * t);

    public float Det => m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);

    public M3 Inverse()
    {
        float c00 = m11 * m22 - m12 * m21, c01 = m12 * m20 - m10 * m22, c02 = m10 * m21 - m11 * m20;
        float det = m00 * c00 + m01 * c01 + m02 * c02;
        if (MathF.Abs(det) < 1e-20f) return Identity;
        float inv = 1f / det;
        return new M3(c00 * inv, (m02 * m21 - m01 * m22) * inv, (m01 * m12 - m02 * m11) * inv,
                      c01 * inv, (m00 * m22 - m02 * m20) * inv, (m02 * m10 - m00 * m12) * inv,
                      c02 * inv, (m01 * m20 - m00 * m21) * inv, (m00 * m11 - m01 * m10) * inv);
    }

    // Rotational part of this matrix (Mueller et al. 2016, "A Robust Method to Extract the Rotational
    // Part of Deformations"), warm-started from q; returns the updated q. Scalar maths.
    public Quaternion ExtractRotation(Quaternion q, int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            M3 r = FromQuaternion(q);
            // omega = sum_i r_i x a_i over columns, / |sum_i r_i . a_i|
            float ox = (r.m10 * m20 - r.m20 * m10) + (r.m11 * m21 - r.m21 * m11) + (r.m12 * m22 - r.m22 * m12);
            float oy = (r.m20 * m00 - r.m00 * m20) + (r.m21 * m01 - r.m01 * m21) + (r.m22 * m02 - r.m02 * m22);
            float oz = (r.m00 * m10 - r.m10 * m00) + (r.m01 * m11 - r.m11 * m01) + (r.m02 * m12 - r.m12 * m02);
            float den = MathF.Abs(r.m00 * m00 + r.m10 * m10 + r.m20 * m20 + r.m01 * m01 + r.m11 * m11 + r.m21 * m21
                                  + r.m02 * m02 + r.m12 * m12 + r.m22 * m22) + 1e-9f;
            ox /= den; oy /= den; oz /= den;
            float w = MathF.Sqrt(ox * ox + oy * oy + oz * oz);
            if (w < 1e-9f) break;
            float s = MathF.Sin(0.5f * w) / w;
            q = OctoMath.Normalize(OctoMath.Mul(OctoMath.Q(ox * s, oy * s, oz * s, MathF.Cos(0.5f * w)), q));
        }
        return q;
    }
}
