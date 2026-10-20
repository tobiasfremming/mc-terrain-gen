// Creature fur: the CPU reference of what Resources/CreatureFur/FurSim.compute does per strand, line for line, plus
// the per-frame choice of which individuals get strands (FurSlots) and the renderer's hook (ICreatureFurDraw).
//   FurSkinning   a skin vertex exactly as CreatureSkin.hlsl (w0 M[b0] + w1 M[b1]), a root as the barycentric blend
//                 of its triangle's three skinned vertices (so it sits on the drawn surface), its comb and tip
//                 directions through the blended matrix
//   FurStrand     one strand: the root and two simulated points. Each segment is a damped spring toward its rest
//                 direction in the skinned frame (implicit Euler: stable at any step), damped relative to the root's
//                 velocity (AirDrag of it against still air: running sweeps the hair back a little), gravity x droop, then the
//                 segment lengths are restored and the points kept off the skin; velocities are the points' motion
//                 (position based), so a hair trails the body when it moves and settles back when it stops.
// Pure C#, zero allocation per step.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // The fur system drawn by a CreatureRenderer after the body (CreatureWorld attaches it; null = no fur).
    // bones / instances: the renderer's buffers this frame (count individuals, BonesPerCreature skin matrices each);
    // centres: one point per individual (a mid-spine bone), the distance and culling origin.
    public interface ICreatureFurDraw : IDisposable
    {
        void Draw(GraphicsBuffer bones, GraphicsBuffer instances, int count, IReadOnlyList<ICreatureBody> bodies, Vector3[] centres, CreatureInstance[] inst);
    }

    public static class FurSkinning
    {
        // w0 S[b0] + w1 S[b1] (the 2-bone skin of CreatureSkin.hlsl)
        public static Matrix4x4 Blend(Matrix4x4[] skin, int baseIndex, Vector4 s)
        {
            Matrix4x4 a = skin[baseIndex + Mathf.RoundToInt(s.x)], b = skin[baseIndex + Mathf.RoundToInt(s.y)];
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++) m[i] = a[i] * s.z + b[i] * s.w;
            return m;
        }

        public static void Vertex(Matrix4x4[] skin, int baseIndex, in FurSkinVertex v, out Vector3 p, out Vector3 n)
        {
            Matrix4x4 m = Blend(skin, baseIndex, v.skin);
            p = m.MultiplyPoint3x4(v.pos);
            n = PartMeshEmitter.Unit(m.MultiplyVector(v.nrm), Vector3.up);
        }

        // A root on the skinned surface: position and normal interpolated from the three skinned vertices (as the
        // rasteriser does), comb and tip through the barycentric blend of their matrices, normalised.
        public static void Root(Matrix4x4[] skin, int baseIndex, FurSkinVertex[] verts, in FurRoot r,
                                out Vector3 p, out Vector3 n, out Vector3 comb, out Vector3 tip)
        {
            float l1 = r.b1, l2 = r.b2, l0 = 1f - l1 - l2;
            FurSkinVertex a = verts[r.v0], b = verts[r.v1], c = verts[r.v2];
            Matrix4x4 ma = Blend(skin, baseIndex, a.skin), mb = Blend(skin, baseIndex, b.skin), mc = Blend(skin, baseIndex, c.skin);
            p = ma.MultiplyPoint3x4(a.pos) * l0 + mb.MultiplyPoint3x4(b.pos) * l1 + mc.MultiplyPoint3x4(c.pos) * l2;
            Vector3 na = PartMeshEmitter.Unit(ma.MultiplyVector(a.nrm), Vector3.up), nb = PartMeshEmitter.Unit(mb.MultiplyVector(b.nrm), Vector3.up),
                    nc = PartMeshEmitter.Unit(mc.MultiplyVector(c.nrm), Vector3.up);
            n = PartMeshEmitter.Unit(na * l0 + nb * l1 + nc * l2, na);
            Vector3 ca = ma.MultiplyVector(r.comb), cb = mb.MultiplyVector(r.comb), cc = mc.MultiplyVector(r.comb);
            comb = PartMeshEmitter.Unit(ca * l0 + cb * l1 + cc * l2, n);
            Vector3 ta = ma.MultiplyVector(r.tipDir), tb = mb.MultiplyVector(r.tipDir), tc = mc.MultiplyVector(r.tipDir);
            tip = PartMeshEmitter.Unit(ta * l0 + tb * l1 + tc * l2, comb);
        }
    }

    // One strand's state (the GPU's FurStrand without its packed colours).
    public struct FurStrand
    {
        public Vector3 p0, p1, p2, v1, v2;
    }

    public static class FurStrandSim
    {
        public const float Gravity = 9.81f;
        public const float MaxStep = 1f / 90f;     // sub-step length
        public const int MaxSubSteps = 4;
        public const float MinRise = 0.15f;        // a point stays this many segment lengths off the skin plane
        public const float AirDrag = 0.1f;         // the share of the damping against still air (the rest: against the root)

        // The spring's angular frequency for a strand of an individual of size s (Hz / sqrt s: Froude).
        public static float Omega(float hz, float sizeScale) => 2f * Mathf.PI * hz / Mathf.Sqrt(Mathf.Max(1e-4f, sizeScale));

        public static void Reset(ref FurStrand s, Vector3 p0, Vector3 comb, Vector3 tip, float length)
        {
            float seg = 0.5f * length;
            s.p0 = p0; s.p1 = p0 + comb * seg; s.p2 = s.p1 + tip * seg;
            s.v1 = Vector3.zero; s.v2 = Vector3.zero;
        }

        // Advance by dt (split into sub-steps of at most MaxStep, at most MaxSubSteps of them; the root moves
        // linearly from the old root to p0 over them). length: world metres; omega: rad/s; droop: x g.
        public static void Step(ref FurStrand s, Vector3 p0, Vector3 n, Vector3 comb, Vector3 tip, float length,
                                float omega, float zeta, float droop, float dt)
        {
            if (!(dt > 0f)) { s.p0 = p0; return; }
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / MaxStep), 1, MaxSubSteps);
            float h = dt / steps;
            Vector3 from = s.p0;
            Vector3 rootVel = (p0 - from) / dt;
            for (int k = 1; k <= steps; k++)
                SubStep(ref s, Vector3.Lerp(from, p0, (float)k / steps), rootVel, n, comb, tip, length, omega, zeta, droop, h);
        }

        // implicit Euler of x'' = w^2 (t - x) - 2 zeta w (x' - vr) + g, vr = (1 - AirDrag) x the root's velocity:
        //   v' = (v + h (w^2 (t - x) + 2 zeta w vr + g)) / (1 + 2 zeta w h + w^2 h^2)
        public static void SubStep(ref FurStrand s, Vector3 p0, Vector3 rootVel, Vector3 n, Vector3 comb, Vector3 tip, float length,
                                   float omega, float zeta, float droop, float h)
        {
            float seg = 0.5f * length;
            float k2 = omega * omega, c = 2f * zeta * omega, den = 1f + c * h + k2 * h * h;
            Vector3 force = new Vector3(0f, -Gravity * droop, 0f) + rootVel * (c * (1f - AirDrag));
            Vector3 o1 = s.p1, o2 = s.p2;
            s.p0 = p0;

            // springs toward the rest directions (the tip's from the simulated first point)
            Vector3 t1 = p0 + comb * seg;
            s.v1 = (s.v1 + (k2 * (t1 - s.p1) + force) * h) / den;
            s.p1 += s.v1 * h;
            Vector3 t2 = s.p1 + tip * seg;
            s.v2 = (s.v2 + (k2 * (t2 - s.p2) + force) * h) / den;
            s.p2 += s.v2 * h;

            // exact segment lengths, off the skin
            s.p1 = p0 + PartMeshEmitter.Unit(s.p1 - p0, comb) * seg;
            float r1 = Vector3.Dot(s.p1 - p0, n);
            if (r1 < MinRise * seg) { s.p1 += n * (MinRise * seg - r1); s.p1 = p0 + PartMeshEmitter.Unit(s.p1 - p0, comb) * seg; }
            s.p2 = s.p1 + PartMeshEmitter.Unit(s.p2 - s.p1, tip) * seg;
            float r2 = Vector3.Dot(s.p2 - p0, n);
            if (r2 < MinRise * seg) { s.p2 += n * (MinRise * seg - r2); s.p2 = s.p1 + PartMeshEmitter.Unit(s.p2 - s.p1, tip) * seg; }

            // position-based velocities, capped (a teleport must not fling the hair)
            float vmax = seg / h;   // at most one segment length per sub-step
            s.v1 = Cap((s.p1 - o1) / h, vmax);
            s.v2 = Cap((s.p2 - o2) / h, vmax);
        }

        static Vector3 Cap(Vector3 v, float max)
        {
            float m = v.magnitude;
            return m > max ? v * (max / m) : v;
        }
    }

    // Which individuals get strands this frame: at most `slots` of them, nearest first within their fade-out
    // distance. A slot keeps its individual while it stays within reach (x1.15 hysteresis), so the strands' state
    // belongs to one animal; a newly filled slot is flagged for a reset. Identity is the body (the renderer
    // swap-removes, so indices move). No allocation after construction.
    public sealed class FurSlots
    {
        public readonly ICreatureBody[] Body;
        public readonly int[] Index;       // the body's index in this frame's list, -1 = free
        public readonly bool[] Reset;
        readonly bool[] taken;

        public FurSlots(int slots, int maxCreatures = 256)
        {
            Body = new ICreatureBody[slots]; Index = new int[slots]; Reset = new bool[slots];
            taken = new bool[Mathf.Max(1, maxCreatures)];
            for (int i = 0; i < slots; i++) Index[i] = -1;
        }

        public int Count => Body.Length;

        // reach[i]: the distance past which individual i gets no strands.
        public void Assign(Vector3 viewer, IReadOnlyList<ICreatureBody> bodies, Vector3[] centres, float[] reach, int count)
        {
            if (count > taken.Length) count = taken.Length;
            for (int i = 0; i < count; i++) taken[i] = false;
            // keep: find each slot's body in this frame's list
            for (int s = 0; s < Body.Length; s++)
            {
                Reset[s] = false;
                Index[s] = -1;
                if (Body[s] == null) continue;
                int at = -1;
                for (int i = 0; i < count; i++) if (ReferenceEquals(bodies[i], Body[s])) { at = i; break; }
                if (at < 0 || (centres[at] - viewer).sqrMagnitude > Sq(1.15f * reach[at])) { Body[s] = null; continue; }
                Index[s] = at; taken[at] = true;
            }
            // fill free slots, nearest first
            for (int s = 0; s < Body.Length; s++)
            {
                if (Body[s] != null) continue;
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    if (taken[i]) continue;
                    float d = (centres[i] - viewer).sqrMagnitude;
                    if (d > Sq(reach[i]) || d >= bd) continue;
                    bd = d; best = i;
                }
                if (best < 0) break;
                Body[s] = bodies[best]; Index[s] = best; Reset[s] = true; taken[best] = true;
            }
        }

        static float Sq(float x) => x * x;
    }
}
