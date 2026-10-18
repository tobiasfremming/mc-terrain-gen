// Stubbed UnityEngine (default mode only): pure C# math with the exact arithmetic of the scratch harness the
// offline goldens (LegacyLegsGoldenTests, #else branch) were captured with. Add members; never change the
// arithmetic of an existing one, or the offline goldens move.
using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering { public enum IndexFormat { UInt16, UInt32 } }

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public class CreateAssetMenuAttribute : Attribute { public string menuName; }
    [AttributeUsage(AttributeTargets.Field)] public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.Field)] public class MinAttribute : Attribute { public MinAttribute(float f) { } }
    [AttributeUsage(AttributeTargets.Field)] public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.Field)] public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }

    public static class Debug { public static void Log(object o) => Console.WriteLine(o); public static void LogWarning(object o) => Console.WriteLine("WARN " + o); }

    public class Object
    {
        public string name { get; set; } = "";
        public HideFlags hideFlags { get; set; }
        public static void DestroyImmediate(Object o) { }
        public static void Destroy(Object o) { }
        // Unity copies a ScriptableObject through serialization: a deep copy of the serialized fields.
        public static T Instantiate<T>(T original) where T : Object => (T)HarnessClone.Deep(original);
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null);
    }
    [Flags] public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }
    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; } }

    public static class Mathf
    {
        public const float PI = 3.14159274f, Deg2Rad = PI / 180f, Rad2Deg = 180f / PI, Infinity = float.PositiveInfinity;
        public const float Epsilon = 1.17549435E-38f;
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int f) => Math.Abs(f);
        public static float Acos(float f) => MathF.Acos(f);
        public static float Asin(float f) => MathF.Asin(f);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        public static float Atan(float f) => MathF.Atan(f);
        public static float Cos(float f) => MathF.Cos(f);
        public static float Sin(float f) => MathF.Sin(f);
        public static float Tan(float f) => MathF.Tan(f);
        public static float Exp(float f) => MathF.Exp(f);
        public static float Pow(float a, float b) => MathF.Pow(a, b);
        public static float Sqrt(float f) => MathF.Sqrt(f);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;
        public static float Round(float f) => MathF.Round(f);
        public static int RoundToInt(float f) => (int)MathF.Round(f);
        public static int FloorToInt(float f) => (int)MathF.Floor(f);
        public static int CeilToInt(float f) => (int)MathF.Ceiling(f);
        public static float Floor(float f) => MathF.Floor(f);
        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float MoveTowards(float c, float t, float d) => Abs(t - c) <= d ? t : c + Sign(t - c) * d;
        public static float SmoothStep(float a, float b, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return b * t + a * (1f - t); }
        public static int NextPowerOfTwo(int v) { int p = 1; while (p < v) p <<= 1; return p; }
        public static float Max(params float[] v) { if (v.Length == 0) return 0f; float m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] > m) m = v[i]; return m; }
        public static float Min(params float[] v) { if (v.Length == 0) return 0f; float m = v[0]; for (int i = 1; i < v.Length; i++) if (v[i] < m) m = v[i]; return m; }
        public static float Repeat(float t, float length) => Clamp(t - Floor(t / length) * length, 0f, length);
        public static float DeltaAngle(float current, float target) { float d = Repeat(target - current, 360f); if (d > 180f) d -= 360f; return d; }
        public static float PingPong(float t, float length) { t = Repeat(t, length * 2f); return length - Abs(t - length); }
        public static float Log(float f) => MathF.Log(f);
        public static bool Approximately(float a, float b) => Abs(b - a) < Max(1E-06f * Max(Abs(a), Abs(b)), 1.1754944E-38f * 8f);
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => MathF.Sqrt(x * x + y * y);
        public static Vector2 zero => new Vector2(0f, 0f);
        public Vector2 normalized { get { float m = magnitude; return m > 1E-05f ? new Vector2(x / m, y / m) : zero; } }
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public override string ToString() => $"({x:F3}, {y:F3})";
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2}, {w:F2})";
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static implicit operator Vector3(Vector4 v) => new Vector3(v.x, v.y, v.z);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 left => new Vector3(-1, 0, 0);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);
        public Vector3 normalized { get { float m = magnitude; return m > 1E-05f ? this / m : zero; } }
        public void Normalize() { this = normalized; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object o) => o is Vector3 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode();
        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3})";
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 Project(Vector3 v, Vector3 n) { float s = Dot(n, n); return s < Mathf.Epsilon ? zero : n * (Dot(v, n) / s); }
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) { float s = Dot(n, n); return s < Mathf.Epsilon ? v : v - n * (Dot(v, n) / s); }
        public static float Angle(Vector3 a, Vector3 b)
        {
            float den = MathF.Sqrt(a.sqrMagnitude * b.sqrMagnitude);
            if (den < 1E-15f) return 0f;
            return MathF.Acos(Mathf.Clamp(Dot(a, b) / den, -1f, 1f)) * Mathf.Rad2Deg;
        }
        public static float SignedAngle(Vector3 a, Vector3 b, Vector3 axis) => Angle(a, b) * Mathf.Sign(Dot(axis, Cross(a, b)));
        public static Vector3 MoveTowards(Vector3 c, Vector3 t, float d)
        {
            Vector3 v = t - c; float m = v.magnitude;
            return m <= d || m == 0f ? t : c + v / m * d;
        }
        static Vector3 RotateAbout(Vector3 v, Vector3 a, float rad)
        {
            float c = MathF.Cos(rad), s = MathF.Sin(rad);
            return v * c + Cross(a, v) * s + a * (Dot(a, v) * (1f - c));
        }
        static Vector3 AnyPerp(Vector3 v)
        {
            Vector3 p = Cross(v, Vector3.right);
            if (p.sqrMagnitude < 1e-6f) p = Cross(v, Vector3.up);
            return p.normalized;
        }
        public static Vector3 RotateTowards(Vector3 cur, Vector3 tgt, float maxRad, float maxMag)
        {
            float cm = cur.magnitude, tm = tgt.magnitude;
            if (cm > 1e-6f && tm > 1e-6f)
            {
                Vector3 cn = cur / cm, tn = tgt / tm;
                float d = Dot(cn, tn);
                float newMag = Mathf.MoveTowards(cm, tm, maxMag);
                if (d > 1f - 1e-6f) return MoveTowards(cur, tgt, maxMag);
                Vector3 axis = d < -1f + 1e-6f ? AnyPerp(cn) : Cross(cn, tn).normalized;
                float ang = MathF.Acos(Mathf.Clamp(d, -1f, 1f));
                return RotateAbout(cn, axis, Math.Min(maxRad, ang)) * newMag;
            }
            return MoveTowards(cur, tgt, maxMag);
        }
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
        {
            t = Mathf.Clamp01(t);
            float am = a.magnitude, bm = b.magnitude;
            if (am < 1e-6f || bm < 1e-6f) return Lerp(a, b, t);
            Vector3 an = a / am, bn = b / bm;
            float d = Dot(an, bn);
            float mag = am + (bm - am) * t;
            if (d > 1f - 1e-6f) return Lerp(a, b, t);
            Vector3 axis = d < -1f + 1e-6f ? AnyPerp(an) : Cross(an, bn).normalized;
            float ang = MathF.Acos(Mathf.Clamp(d, -1f, 1f));
            return RotateAbout(an, axis, ang * t) * mag;
        }
        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion operator *(Quaternion l, Quaternion r) => new Quaternion(
            l.w * r.x + l.x * r.w + l.y * r.z - l.z * r.y,
            l.w * r.y + l.y * r.w + l.z * r.x - l.x * r.z,
            l.w * r.z + l.z * r.w + l.x * r.y - l.y * r.x,
            l.w * r.w - l.x * r.x - l.y * r.y - l.z * r.z);
        public static Vector3 operator *(Quaternion q, Vector3 p)
        {
            float x = q.x * 2f, y = q.y * 2f, z = q.z * 2f;
            float xx = q.x * x, yy = q.y * y, zz = q.z * z, xy = q.x * y, xz = q.x * z, yz = q.y * z;
            float wx = q.w * x, wy = q.w * y, wz = q.w * z;
            return new Vector3(
                (1f - (yy + zz)) * p.x + (xy - wz) * p.y + (xz + wy) * p.z,
                (xy + wz) * p.x + (1f - (xx + zz)) * p.y + (yz - wx) * p.z,
                (xz - wy) * p.x + (yz + wx) * p.y + (1f - (xx + yy)) * p.z);
        }
        public static Quaternion Inverse(Quaternion q) { float n = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w; return new Quaternion(-q.x / n, -q.y / n, -q.z / n, q.w / n); }
        public static Quaternion AngleAxis(float deg, Vector3 axis)
        {
            if (axis.sqrMagnitude < 1e-12f) return identity;
            axis = axis.normalized;
            float h = deg * Mathf.Deg2Rad * 0.5f, s = MathF.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, MathF.Cos(h));
        }
        public static Quaternion FromBasis(Vector3 r, Vector3 u, Vector3 f)
        {
            float m00 = r.x, m01 = u.x, m02 = f.x, m10 = r.y, m11 = u.y, m12 = f.y, m20 = r.z, m21 = u.z, m22 = f.z;
            float tr = m00 + m11 + m22;
            Quaternion q;
            if (tr > 0f) { float s = 0.5f / MathF.Sqrt(tr + 1f); q = new Quaternion((m21 - m12) * s, (m02 - m20) * s, (m10 - m01) * s, 0.25f / s); }
            else if (m00 > m11 && m00 > m22) { float s = 2f * MathF.Sqrt(1f + m00 - m11 - m22); q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s); }
            else if (m11 > m22) { float s = 2f * MathF.Sqrt(1f + m11 - m00 - m22); q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s); }
            else { float s = 2f * MathF.Sqrt(1f + m22 - m00 - m11); q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s); }
            float n = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n);
        }
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            if (forward.sqrMagnitude < 1e-12f) return identity;
            Vector3 f = forward.normalized;
            Vector3 r = Vector3.Cross(up, f);
            if (r.sqrMagnitude < 1e-12f) return FromToRotation(Vector3.forward, f);
            r = r.normalized;
            Vector3 u = Vector3.Cross(f, r);
            return FromBasis(r, u, f);
        }
        public static Quaternion LookRotation(Vector3 forward) => LookRotation(forward, Vector3.up);
        public static Quaternion FromToRotation(Vector3 a, Vector3 b)
        {
            a = a.normalized; b = b.normalized;
            float d = Vector3.Dot(a, b);
            if (d > 1f - 1e-7f) return identity;
            if (d < -1f + 1e-6f)
            {
                Vector3 ax = Vector3.Cross(Vector3.right, a);
                if (ax.sqrMagnitude < 1e-6f) ax = Vector3.Cross(Vector3.up, a);
                return AngleAxis(180f, ax.normalized);
            }
            Vector3 c = Vector3.Cross(a, b);
            var q = new Quaternion(c.x, c.y, c.z, 1f + d);
            float n = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n);
        }
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            t = Mathf.Clamp01(t);
            float d = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
            if (d < 0f) { b = new Quaternion(-b.x, -b.y, -b.z, -b.w); d = -d; }
            float k0, k1;
            if (d > 0.9995f) { k0 = 1f - t; k1 = t; }
            else { float th = MathF.Acos(d), s = MathF.Sin(th); k0 = MathF.Sin((1f - t) * th) / s; k1 = MathF.Sin(t * th) / s; }
            var q = new Quaternion(a.x * k0 + b.x * k1, a.y * k0 + b.y * k1, a.z * k0 + b.z * k1, a.w * k0 + b.w * k1);
            float n = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n);
        }
        public static float Angle(Quaternion a, Quaternion b)
        {
            float d = Math.Min(Math.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w), 1f);
            return d > 1f - 1e-6f ? 0f : MathF.Acos(d) * 2f * Mathf.Rad2Deg;
        }
    }

    public struct Matrix4x4
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public static Matrix4x4 identity => new Matrix4x4 { m00 = 1, m11 = 1, m22 = 1, m33 = 1 };
        public static Matrix4x4 zero => new Matrix4x4();
        public float this[int i] { get => this[i % 4, i / 4]; set => this[i % 4, i / 4] = value; }   // column-major, like Unity
        public Vector4 GetColumn(int c) => new Vector4(this[0, c], this[1, c], this[2, c], this[3, c]);
        public Vector4 GetRow(int r) => new Vector4(this[r, 0], this[r, 1], this[r, 2], this[r, 3]);
        public void SetColumn(int c, Vector4 v) { this[0, c] = v.x; this[1, c] = v.y; this[2, c] = v.z; this[3, c] = v.w; }
        public Vector3 lossyScale => new Vector3(new Vector3(m00, m10, m20).magnitude, new Vector3(m01, m11, m21).magnitude, new Vector3(m02, m12, m22).magnitude);
        public static bool operator ==(Matrix4x4 a, Matrix4x4 b) { for (int i = 0; i < 16; i++) if (a[i] != b[i]) return false; return true; }
        public static bool operator !=(Matrix4x4 a, Matrix4x4 b) => !(a == b);
        public override bool Equals(object o) => o is Matrix4x4 m && m == this;
        public override int GetHashCode() => m00.GetHashCode() ^ m11.GetHashCode() ^ m22.GetHashCode() ^ m03.GetHashCode();
        public float this[int r, int c]
        {
            get
            {
                switch (r * 4 + c)
                {
                    case 0: return m00; case 1: return m01; case 2: return m02; case 3: return m03;
                    case 4: return m10; case 5: return m11; case 6: return m12; case 7: return m13;
                    case 8: return m20; case 9: return m21; case 10: return m22; case 11: return m23;
                    case 12: return m30; case 13: return m31; case 14: return m32; default: return m33;
                }
            }
            set
            {
                switch (r * 4 + c)
                {
                    case 0: m00 = value; break; case 1: m01 = value; break; case 2: m02 = value; break; case 3: m03 = value; break;
                    case 4: m10 = value; break; case 5: m11 = value; break; case 6: m12 = value; break; case 7: m13 = value; break;
                    case 8: m20 = value; break; case 9: m21 = value; break; case 10: m22 = value; break; case 11: m23 = value; break;
                    case 12: m30 = value; break; case 13: m31 = value; break; case 14: m32 = value; break; default: m33 = value; break;
                }
            }
        }
        public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s)
        {
            Vector3 r = q * Vector3.right, u = q * Vector3.up, f = q * Vector3.forward;
            return new Matrix4x4
            {
                m00 = r.x * s.x, m10 = r.y * s.x, m20 = r.z * s.x,
                m01 = u.x * s.y, m11 = u.y * s.y, m21 = u.z * s.y,
                m02 = f.x * s.z, m12 = f.y * s.z, m22 = f.z * s.z,
                m03 = p.x, m13 = p.y, m23 = p.z, m33 = 1f,
            };
        }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    float s = 0f;
                    for (int k = 0; k < 4; k++) s += a[i, k] * b[k, j];
                    m[i, j] = s;
                }
            return m;
        }
        public Vector3 MultiplyPoint3x4(Vector3 p) => new Vector3(
            m00 * p.x + m01 * p.y + m02 * p.z + m03, m10 * p.x + m11 * p.y + m12 * p.z + m13, m20 * p.x + m21 * p.y + m22 * p.z + m23);
        public Vector3 MultiplyPoint(Vector3 p) => MultiplyPoint3x4(p);
        public Vector3 MultiplyVector(Vector3 p) => new Vector3(
            m00 * p.x + m01 * p.y + m02 * p.z, m10 * p.x + m11 * p.y + m12 * p.z, m20 * p.x + m21 * p.y + m22 * p.z);
        public Quaternion rotation => Quaternion.FromBasis(new Vector3(m00, m10, m20).normalized, new Vector3(m01, m11, m21).normalized, new Vector3(m02, m12, m22).normalized);
        public Matrix4x4 inverse
        {
            get
            {
                double[] a = new double[16];
                for (int i = 0; i < 16; i++) a[i] = this[i / 4, i % 4];
                double[] inv = new double[16];
                double[,] m = new double[4, 8];
                for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) m[i, j] = a[i * 4 + j]; m[i, 4 + i] = 1; }
                for (int c = 0; c < 4; c++)
                {
                    int piv = c;
                    for (int r = c + 1; r < 4; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
                    for (int j = 0; j < 8; j++) { double t = m[c, j]; m[c, j] = m[piv, j]; m[piv, j] = t; }
                    double d = m[c, c];
                    for (int j = 0; j < 8; j++) m[c, j] /= d;
                    for (int r = 0; r < 4; r++)
                    {
                        if (r == c) continue;
                        double f = m[r, c];
                        for (int j = 0; j < 8; j++) m[r, j] -= f * m[c, j];
                    }
                }
                var res = new Matrix4x4();
                for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) res[i, j] = (float)m[i, 4 + j];
                return res;
            }
        }
    }

    public class Mesh : Object
    {
        public Rendering.IndexFormat indexFormat;
        Vector3[] v = new Vector3[0], n = new Vector3[0]; int[] t = new int[0];
        readonly Dictionary<int, object> uvs = new Dictionary<int, object>();
        public Vector3[] vertices => (Vector3[])v.Clone();
        public Vector3[] normals => (Vector3[])n.Clone();
        public int[] triangles => (int[])t.Clone();
        public int vertexCount => v.Length;
        public void SetVertices(List<Vector3> p) => v = p.ToArray();
        public void SetNormals(List<Vector3> p) => n = p.ToArray();
        public void SetUVs(int ch, List<Vector2> p) => uvs[ch] = new List<Vector2>(p);
        public void SetUVs(int ch, List<Vector4> p) => uvs[ch] = new List<Vector4>(p);
        public void GetUVs(int ch, List<Vector2> dst) { dst.Clear(); dst.AddRange((List<Vector2>)uvs[ch]); }
        public void GetUVs(int ch, List<Vector4> dst) { dst.Clear(); dst.AddRange((List<Vector4>)uvs[ch]); }
        public void SetTriangles(List<int> tris, int sub, bool calc) => t = tris.ToArray();
    }
}
