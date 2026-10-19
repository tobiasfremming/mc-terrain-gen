// Creature kit: RigidLoft - a lofted solid rigid to one spine joint: a squared snout, a lower jaw, a brow ridge, a
// nostril bump, a cheek plate, a crest bone. The spine body's cross-section is an ellipse; a loft's is a
// superellipse (|x/a|^p + |y/b|^p = 1) with its own exponent above and below the centre, so it can be flat-topped
// and box-like (p 3-5) or round (p 2), keyed along its length (width, height, a rise of its centre line, the two
// exponents), along a direction in the joint frame, curling down by curveDeg and rolled about its axis by rollDeg.
// Both ends close (a pole at the end key's centre: a small end key rounds the tip, a large one leaves it blunt).
// Mesh only, no bones of its own: every vertex is rigid to its joint's bone, so it cannot deform or invert; the
// spine's head cap may hide inside it. Shading: skin (part 0: the body's colours, pattern, shimmer) or claw (part 2:
// horn). mirror expands into .L / .R (side -1 / +1: offset.x, direction.x and the roll mirrored).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // One cross-section of a loft, at a place along it.
    [Serializable]
    public struct LoftKey
    {
        [Param(ParamUnit.Fraction, "place along the loft, 0 = its start .. 1 = its end", Min = 0, Max = 1)] public float t;
        [Param(ParamUnit.Metres, "half width (across, the joint frame's x)")] public float halfWidth;
        [Param(ParamUnit.Metres, "half height (the loft's up)")] public float halfHeight;
        [Param(ParamUnit.Metres, "centre line raised (+) or lowered (-) along the loft's up")] public float rise;
        [Param(ParamUnit.Fraction, "superellipse exponent of the upper half: 2 = round, 4 = squared, flat-topped", Min = 1, Max = 12)] public float squareTop;
        [Param(ParamUnit.Fraction, "superellipse exponent of the lower half", Min = 1, Max = 12)] public float squareBottom;
    }

    [Serializable]
    [Part("RigidLoft", PartKind.Rigid, Bones = "0 (rigid to its joint)", Summary = "Lofted solid rigid to a spine joint: superellipse cross-sections (round to squared and flat-topped, separate above and below) keyed along a direction, curled and rolled. Snouts, jaws, brow ridges, nostril bumps, cheek plates, skull shapes the spine tube cannot express.",
          System = "mesh only (RigidLoft.EmitMesh), rigid to the joint's bone", MaterialLayers = new[] { 0, 2 }, KitOnly = true,
          UseWhen = "head shapes: squared snouts, jaws, brows, nostrils, bosses; any rigid lump on a joint")]
    public sealed partial class RigidLoft : CreaturePart
    {
        [Param(ParamUnit.Direction, "the loft's axis in the joint frame (x mirrored by side); its up is the joint's up made perpendicular")] public Vector3 direction = new Vector3(0f, 0f, 1f);
        [Param(ParamUnit.Metres, "length along the axis", Example = "dragon muzzle 0.15")] public float length;
        [Param(ParamUnit.Struct, "cross-sections along the loft (monotone cubic between them); >= 2, t = 0 and t = 1 included")] public LoftKey[] keys = new LoftKey[0];
        [Param(ParamUnit.Degrees, "the axis curls down (+) or up (-) by this much over the length")] public float curveDeg;
        [Param(ParamUnit.Degrees, "cross-section rolled about the axis (mirrored by side; + turns the top outward)")] public float rollDeg;
        [Param(ParamUnit.Count, "shading: 0 = skin (the body's), 2 = claw / horn", Min = 0, Max = 2)] public int shade;
        [Param(ParamUnit.Count, "mesh: vertices around each section (0 = 16)", Min = 0, Max = 48)] public int around;
        [Param(ParamUnit.Count, "mesh: sections along the length (0 = 12)", Min = 0, Max = 64)] public int rings;

        public override int BoneCount(in SpeciesCtx c) => 0;
        protected override void CopyArrays() { keys = (LoftKey[])keys?.Clone(); }

        public int Around => around > 0 ? around : 16;
        public int Rings => rings > 0 ? rings : 12;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a RigidLoft needs a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!(length > 0f)) return $"{id}: length must be > 0";
            if (direction.sqrMagnitude < 1e-8f) return $"{id}: direction must not be zero";
            if (keys == null || keys.Length < 2) return $"{id}: needs >= 2 keys";
            if (keys[0].t != 0f || keys[keys.Length - 1].t != 1f) return $"{id}: keys must start at t = 0 and end at t = 1";
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0 && !(keys[i].t > keys[i - 1].t)) return $"{id}: keys must be in increasing t";
                if (!(keys[i].halfWidth > 0f && keys[i].halfHeight > 0f)) return $"{id}: key {i}: half sizes must be > 0";
                if (keys[i].squareTop < 1f || keys[i].squareTop > 12f || keys[i].squareBottom < 1f || keys[i].squareBottom > 12f)
                    return $"{id}: key {i}: squareTop / squareBottom must be in 1..12";
            }
            if (shade != 0 && shade != 2) return $"{id}: shade must be 0 (skin) or 2 (claw)";
            return null;
        }

        // ---------------------------------------------------------------- the shape (pure, also for tests)

        static float Channel(in LoftKey k, int ch) => ch == 0 ? k.halfWidth : ch == 1 ? k.halfHeight : ch == 2 ? k.rise : ch == 3 ? k.squareTop : k.squareBottom;

        // Monotone cubic Hermite through the keys (SpineBody.Profile's rule): no overshoot between keys.
        public float Key(float t, int ch)
        {
            LoftKey[] k = keys;
            int n = k.Length;
            if (t <= k[0].t) return Channel(k[0], ch);
            if (t >= k[n - 1].t) return Channel(k[n - 1], ch);
            int i = 0;
            while (i < n - 2 && t >= k[i + 1].t) i++;
            float h = k[i + 1].t - k[i].t, y0 = Channel(k[i], ch), y1 = Channel(k[i + 1], ch);
            float m0 = Tangent(i, ch), m1 = Tangent(i + 1, ch);
            float s = (t - k[i].t) / h, s2 = s * s, s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * y0 + (s3 - 2f * s2 + s) * h * m0 + (-2f * s3 + 3f * s2) * y1 + (s3 - s2) * h * m1;
        }

        float Tangent(int i, int ch)
        {
            LoftKey[] k = keys;
            int n = k.Length;
            float dPrev = i > 0 ? (Channel(k[i], ch) - Channel(k[i - 1], ch)) / (k[i].t - k[i - 1].t) : 0f;
            float dNext = i < n - 1 ? (Channel(k[i + 1], ch) - Channel(k[i], ch)) / (k[i + 1].t - k[i].t) : 0f;
            if (i == 0) return dNext;
            if (i == n - 1) return dPrev;
            if (dPrev * dNext <= 0f) return 0f;
            float lim = 3f * Mathf.Min(Mathf.Abs(dPrev), Mathf.Abs(dNext));
            return Mathf.Clamp(0.5f * (dPrev + dNext), -lim, lim);
        }

        // The loft's frame at its start in the joint frame (mirrored by side): forward along the axis, up the joint's
        // up made perpendicular, rolled.
        public void LocalFrame(out Vector3 origin, out Vector3 fwd, out Vector3 up, out Vector3 right)
        {
            float mx = at.side < 0 ? -1f : 1f;
            origin = at.offset; origin.x *= mx;
            Vector3 d = direction; d.x *= mx;
            fwd = d.normalized;
            up = Vector3.ProjectOnPlane(Vector3.up, fwd);
            if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(Vector3.back, fwd);
            up.Normalize();
            if (rollDeg != 0f) up = Quaternion.AngleAxis(-mx * rollDeg, fwd) * up;
            right = Vector3.Cross(up, fwd);
        }

        // Point on the surface in the joint frame: t along the loft 0..1, phi round the section from the belly (-pi/2).
        public Vector3 Surface(float t, float phi) => Point(SectionAt(t), phi);

        // One section, evaluated once for all the points round it.
        public struct Section { public Vector3 c, up, right; public float a, b, eTop, eBottom; }

        public Section SectionAt(float t)
        {
            Centre(t, out Vector3 c, out Vector3 up, out Vector3 right);
            return new Section
            {
                c = c, up = up, right = right,
                a = Mathf.Max(PartMeshEmitter.MinRadius, Key(t, 0)), b = Mathf.Max(PartMeshEmitter.MinRadius, Key(t, 1)),
                eTop = 2f / Mathf.Max(1f, Key(t, 3)), eBottom = 2f / Mathf.Max(1f, Key(t, 4)),
            };
        }

        public static Vector3 Point(in Section q, float phi)
        {
            float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
            float e = sn >= 0f ? q.eTop : q.eBottom;
            float x = Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), e), y = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), e);
            return q.c + q.right * (q.a * x) + q.up * (q.b * y);
        }

        // The (curled) centre line at t with its section axes, in the joint frame.
        public void Centre(float t, out Vector3 c, out Vector3 up, out Vector3 right)
        {
            LocalFrame(out Vector3 o, out Vector3 f, out Vector3 u0, out right);
            float bend = curveDeg * Mathf.Deg2Rad, L = length;
            // an arc in the (forward, up) plane, curling down for curveDeg > 0
            Vector3 along;
            if (Mathf.Abs(bend) > 1e-4f)
            {
                float k = L / bend, a = bend * t;
                along = f * (k * Mathf.Sin(a)) - u0 * (k * (1f - Mathf.Cos(a)));
                up = u0 * Mathf.Cos(a) + f * Mathf.Sin(a);
            }
            else { along = f * (L * t); up = u0; }
            c = o + along + up * Key(t, 2);
        }

        public Vector3 Tip => Surface(1f, 0f);

        // ---------------------------------------------------------------- the mesh

        public override int MeshOrder => 100;

        // Sections of `around` + 1 vertices (seam duplicated) from a pole at the start to a pole at the end, rigid to
        // the joint's bone, in the rest pose. Normals from the surface's own derivatives, outward.
        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            CreatureSpecies sp = m.Species;
            CreatureSim sim = m.Rest;
            int j = Mathf.Clamp(at.joint, 0, m.Joints - 1);
            Vector3 jp = sim.Joints[j];
            Quaternion jr = sim.Rotations[j];
            Vector4 skin = PartMeshEmitter.Rigid(j);
            int part = shade == 2 ? PartMeshEmitter.PartFoot : PartMeshEmitter.PartBody;
            int na = Around, nr = Rings;
            float uBase = sp.headCapLength + sp.spacing * j;    // the body's uv0.x at this joint (metres from the snout tip)
            Vector3 jUp = jr * Vector3.up, jF = jr * Vector3.forward;
            const float e = 1e-3f;

            int Pole(float t, Vector3 nLocal)
            {
                Centre(t, out Vector3 cc, out _, out _);
                Vector3 p = jp + jr * cc, nn = PartMeshEmitter.Unit(jr * nLocal, jr * Vector3.up);
                float u = Mathf.Max(0f, uBase - Vector3.Dot(p - jp, jF));
                return m.Pole(p, nn, u, part, Vector3.Dot(nn, jUp), skin, na);
            }

            LocalFrame(out _, out Vector3 f0, out _, out _);
            var starts = new List<int>(nr + 3);
            starts.Add(Pole(0f, -f0));
            Vector3 c1 = Vector3.zero;
            for (int r = 0; r <= nr; r++)
            {
                // sections bunched toward the ends (where the poles round off), cosine spacing
                float t = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * r / nr);
                float tA = Mathf.Max(0f, t - e), tB = Mathf.Min(1f, t + e);
                Section q = SectionAt(t), qA = SectionAt(tA), qB = SectionAt(tB);
                Vector3 cc = q.c;
                if (r == 1) c1 = jp + jr * cc;
                starts.Add(m.VertexCount);
                for (int k = 0; k <= na; k++)
                {
                    float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * (k == na ? 0 : k) / na;
                    Vector3 p = Point(q, phi);
                    Vector3 dPhi = Point(q, phi + e) - Point(q, phi - e);
                    Vector3 dT = Point(qB, phi) - Point(qA, phi);
                    Vector3 nl = Vector3.Cross(dPhi, dT);
                    if (nl.sqrMagnitude < 1e-14f) nl = p - cc;
                    if (Vector3.Dot(nl, p - cc) < 0f) nl = -nl;
                    Vector3 wp = jp + jr * p, wn = PartMeshEmitter.Unit(jr * nl, PartMeshEmitter.Unit(jr * (p - cc), jUp));   // never .normalized (zero below 1e-5)
                    float u = Mathf.Max(0f, uBase - Vector3.Dot(wp - jp, jF));
                    m.Vertex(wp, wn, u, (float)k / na, part, Vector3.Dot(wn, jUp), skin);
                }
            }
            Centre(1f, out _, out Vector3 u1, out Vector3 r1);
            starts.Add(Pole(1f, Vector3.Cross(r1, u1)));

            // one winding for every section pair (all are generated alike): the first quad of sections 1 -> 2
            // (Stitch's first triangle a, a + 1, b) against the outward direction from section 1's centre
            int s1 = starts[2], s2 = starts[3];
            Vector3 q0 = m.Position(s1), face = Vector3.Cross(m.Position(s1 + 1) - q0, m.Position(s2) - q0);
            bool flip = Vector3.Dot(face, q0 - c1) < 0f;
            for (int i = 0; i + 1 < starts.Count; i++)
                m.Stitch(starts[i], starts[i + 1], na, i == 0, i + 2 == starts.Count, flip);
        }
    }
}
