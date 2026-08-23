using UnityEngine;

// Minimal double-precision 3-vector for the floating-origin precision fix
// (see the "Floating-Origin Precision Fix for Large-Radius Planets" plan).
// Unity's Vector3 is float-only, which is exactly the problem this exists to
// route around: a chunk's world-space origin, computed as (Vector3)coord*S,
// loses precision once its magnitude grows with a large planet's radius.
// Double3 lets that multiply/subtract happen in double precision, with the
// cast down to a (small, precise) Vector3 deferred to the last possible
// moment. Deliberately not Unity.Mathematics.double3: that package is only
// an unused transitive dependency here (pulled in by something else), and
// this project's own convention is plain UnityEngine/float math throughout
// -- a self-contained struct with just the operators actually needed fits
// that better than adding a direct package dependency for one feature.
public struct Double3
{
    public double x, y, z;

    public Double3(double x, double y, double z)
    {
        this.x = x; this.y = y; this.z = z;
    }

    public static Double3 operator +(Double3 a, Double3 b) => new Double3(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Double3 operator -(Double3 a, Double3 b) => new Double3(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Double3 operator *(Double3 a, double s) => new Double3(a.x * s, a.y * s, a.z * s);

    public static explicit operator Vector3(Double3 v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
    public static explicit operator Double3(Vector3 v) => new Double3(v.x, v.y, v.z);
    public static explicit operator Double3(Vector3Int v) => new Double3(v.x, v.y, v.z);
}
