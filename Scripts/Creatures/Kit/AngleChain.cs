// Creature kit: AngleChain - a chain of links on one angle each (degrees), the chain rule as a reusable piece: link 0
// is driven, every further link FOLLOWS the one before it - a damped spring on its absolute angle (natural frequency,
// damping ratio) toward the previous link's angle plus a static offset - so a motion of the root travels out along
// the chain with a growing lag and the end whips over at a turn (damping < 0.7 overshoots). The bend between two
// neighbouring links is clamped. The sky manta's RayWing runs the same rule on its span and chord lattice; this is
// the generic form a part opts into (PteroWing: the wing finger's phalanges, the membrane's chord rays).
// Allocation-free after construction; main thread.
using UnityEngine;

namespace CreatureKit
{
    public sealed class AngleChain
    {
        readonly float[] a, v, offset;
        bool started;

        public int Count => a.Length;
        public float Angle(int i) => a[i];
        public float Rate(int i) => v[i];
        // the bend of link i over link i - 1 (degrees)
        public float Bend(int i) => a[i] - a[i - 1];
        // link i's static offset over link i - 1 (degrees; a glide's bow, a billow)
        public void SetOffset(int i, float deg) => offset[i] = deg;

        // A hard stop from outside the chain (a part's own limit, e.g. Dewlap's plane): link i set to `deg` and its rate
        // zeroed, as a contact would. The chain's own rule is unchanged.
        public void Hold(int i, float deg) { a[i] = deg; v[i] = 0f; }

        // A coupling from outside the chain (Dewlap: neighbouring stations' chains drawn toward each other): link i set
        // to `deg` moving at `rate` (deg/s). The chain's own rule is unchanged.
        public void Set(int i, float deg, float rate) { a[i] = deg; v[i] = rate; }

        public AngleChain(int links)
        {
            int n = Mathf.Max(2, links);
            a = new float[n]; v = new float[n]; offset = new float[n];
        }

        public void Reset(float root)
        {
            for (int i = 0; i < a.Length; i++) { a[i] = root; v[i] = 0f; }
            started = true;
        }

        // Drive link 0 to `root` and let the rest follow for dt: omega (rad/s), zeta (damping ratio), maxBend (deg).
        public void Step(float root, float dt, float omega, float zeta, float maxBend)
        {
            if (!(dt > 0f)) return;
            if (!started) Reset(root);
            v[0] = (root - a[0]) / dt; a[0] = root;
            // semi-implicit Euler stays accurate (and stable) for omega x h <= 0.5
            int sub = Mathf.Clamp(Mathf.CeilToInt(omega * dt / 0.5f), 1, 64);
            float h = dt / sub;
            for (int it = 0; it < sub; it++)
                for (int i = 1; i < a.Length; i++)
                {
                    float target = a[i - 1] + offset[i];
                    v[i] += (omega * omega * (target - a[i]) - 2f * zeta * omega * v[i]) * h;
                    a[i] += v[i] * h;
                    float rel = a[i] - a[i - 1];
                    if (rel > maxBend) { a[i] = a[i - 1] + maxBend; v[i] = Mathf.Min(v[i], v[i - 1]); }
                    else if (rel < -maxBend) { a[i] = a[i - 1] - maxBend; v[i] = Mathf.Max(v[i], v[i - 1]); }
                }
        }
    }
}
