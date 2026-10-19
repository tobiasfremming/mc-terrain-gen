// Creature kit: mesh helpers for parts that draw free-form solids rigid to one bone (Chela, SpineField, LimbSpines):
// a swept surface P(t, phi) between two poles, closed and outward-wound whatever the handedness of the frame it was
// built in (a mirrored side flips it), with normals from the surface's own derivatives; and a spike (a short curved
// cone, base sunk under the surface it grows from). Mesh building runs once per species: these may allocate.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public static class KitSweep
    {
        const float kE = 1e-3f;

        // A closed swept solid: sections t0 .. t1 (rings + 1 of them, ends pinched to poles when poleA / poleB), each of
        // `around` + 1 vertices (seam duplicated) at phi = 2 pi k / around. P(t, phi) the surface point and C(t) the
        // section's centre (to orient the normals outward), both in world space at rest. uv0.x = uStart + t x uPerT.
        // cosine: sections bunched toward both ends (rounded caps keep their shape).
        public static void Sweep(PartMeshEmitter m, Func<float, float, Vector3> P, Func<float, Vector3> C, float t0, float t1, int rings, int around,
                                 bool poleA, bool poleB, int part, Vector4 skin, float uStart, float uPerT, bool cosine = true)
        {
            rings = Mathf.Max(3, rings); around = Mathf.Max(3, around);
            var starts = new List<int>(rings + 1);
            var centres = new List<Vector3>(rings + 1);
            for (int r = 0; r <= rings; r++)
            {
                float f = (float)r / rings;
                if (cosine) f = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * f);
                float t = t0 + (t1 - t0) * f;
                Vector3 c = C(t);
                centres.Add(c);
                starts.Add(m.VertexCount);
                float u = uStart + t * uPerT;
                bool pole = (r == 0 && poleA) || (r == rings && poleB);
                if (pole)
                {
                    float tn = r == 0 ? Mathf.Min(t1, t + 0.02f * (t1 - t0)) : Mathf.Max(t0, t - 0.02f * (t1 - t0));
                    Vector3 p = P(t, 0f);
                    Vector3 n = PartMeshEmitter.Unit(p - C(tn), Vector3.up);
                    m.Pole(p, n, u, part, n.y, skin, around);
                    continue;
                }
                float tA = Mathf.Max(t0, t - kE * (t1 - t0)), tB = Mathf.Min(t1, t + kE * (t1 - t0));
                for (int k = 0; k <= around; k++)
                {
                    float phi = 2f * Mathf.PI * (k == around ? 0 : k) / around;
                    Vector3 p = P(t, phi);
                    Vector3 dPhi = P(t, phi + kE) - P(t, phi - kE);
                    Vector3 dT = P(tB, phi) - P(tA, phi);
                    Vector3 nl = Vector3.Cross(dPhi, dT);
                    Vector3 radial = p - c;
                    if (nl.sqrMagnitude < 1e-16f) nl = radial;
                    if (Vector3.Dot(nl, radial) < 0f) nl = -nl;
                    Vector3 n = PartMeshEmitter.Unit(nl, PartMeshEmitter.Unit(radial, Vector3.up));
                    m.Vertex(p, n, u, (float)k / around, part, n.y, skin);
                }
            }
            // one winding for the whole sweep: the first quad between two full rings against the outward direction
            int ra = poleA ? 1 : 0, rb = ra + 1;
            Vector3 q0 = m.Position(starts[ra]);
            Vector3 face = Vector3.Cross(m.Position(starts[ra] + 1) - q0, m.Position(starts[rb]) - q0);
            bool flip = Vector3.Dot(face, q0 - centres[ra]) < 0f;
            for (int i = 0; i + 1 < starts.Count; i++)
                m.Stitch(starts[i], starts[i + 1], around, i == 0 && poleA, i + 2 == starts.Count && poleB, flip);
        }

        // A spike from `basePt` (world, rest) along `dir`, its base `sink` x length under the surface: a curved cone
        // (curling toward `curlToward` by curveDeg), base radius r0 to tip radius r1 (0 = sharp).
        public static void Spike(PartMeshEmitter m, Vector3 basePt, Vector3 dir, Vector3 curlToward, float length, float r0, float r1, float curveDeg,
                                 float sink, int bone, int part, int sides, float u)
        {
            dir = PartMeshEmitter.Unit(dir, Vector3.up);
            Vector3 side = Vector3.ProjectOnPlane(curlToward, dir);
            if (side.sqrMagnitude < 1e-10f) side = Vector3.ProjectOnPlane(Vector3.forward, dir);
            if (side.sqrMagnitude < 1e-10f) side = Vector3.ProjectOnPlane(Vector3.right, dir);
            // Horn curls toward its frame's -Y: up = -side
            Quaternion q = Quaternion.LookRotation(dir, -side.normalized);
            float total = length * (1f + sink);
            m.Horn(basePt - dir * (length * sink), q, r0, r1, total, curveDeg, bone, part, sides, u, out _, out _);
        }

        // Deterministic hash in 0..1 (seeded scatter).
        public static float Hash01(int a, int b)
        {
            uint x = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
            x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }
    }
}
