// Stubbed UnityEngine (default mode only): what the creature sources use beyond UnityEngineMath.cs.
using System;

namespace UnityEngine
{
    public struct Bounds
    {
        public Vector3 center, extents;
        public Bounds(Vector3 c, Vector3 size) { center = c; extents = size * 0.5f; }
        public Vector3 size { get => extents * 2f; set => extents = value * 0.5f; }
        public Vector3 min { get => center - extents; set => SetMinMax(value, max); }
        public Vector3 max { get => center + extents; set => SetMinMax(min, value); }
        public void Expand(float a) { extents += new Vector3(a, a, a) * 0.5f; }
        public void Expand(Vector3 a) { extents += a * 0.5f; }
        public void SetMinMax(Vector3 lo, Vector3 hi) { center = (lo + hi) * 0.5f; extents = (hi - lo) * 0.5f; }
        public bool Contains(Vector3 p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
        public void Encapsulate(Vector3 p) { Vector3 lo = Vector3.Min(min, p), hi = Vector3.Max(max, p); center = (lo + hi) * 0.5f; extents = (hi - lo) * 0.5f; }
        public void Encapsulate(Bounds b) { Encapsulate(b.min); Encapsulate(b.max); }
        public bool Intersects(Bounds b) => min.x <= b.max.x && max.x >= b.min.x && min.y <= b.max.y && max.y >= b.min.y && min.z <= b.max.z && max.z >= b.min.z;
        public Vector3 ClosestPoint(Vector3 p) => Vector3.Max(min, Vector3.Min(max, p));
        public float SqrDistance(Vector3 p) => (ClosestPoint(p) - p).sqrMagnitude;
        public override string ToString() => $"Center: {center}, Extents: {extents}";
    }
}
