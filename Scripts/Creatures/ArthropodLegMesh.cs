using System.Collections.Generic;
using UnityEngine;

// Vertex streams of a creature mesh under construction (CreatureMeshBuilder's layout):
//   uv0 = (arc length along the part, metres; angle / 2pi), uv1 = (part id, dorsal -1..1), uv2 = (bone0, bone1, w0, w1)
public sealed class CreatureMeshLists
{
    public readonly List<Vector3> pos = new List<Vector3>(), nrm = new List<Vector3>();
    public readonly List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
    public readonly List<Vector4> uv2 = new List<Vector4>();
    public readonly List<int> tris = new List<int>();
}

// Mesh of one arthropod leg in its rest pose: every segment a closed capsule (rounded caps centred on its
// joints, per-segment taper) RIGID to its own bone, so no joint bend can fold the skin - the overlapping caps
// of two segments cover their joint at any angle, like a hard exoskeleton coming out of a socket. Optional
// joint bulbs (knuckles) rigid to the proximal bone, knee bands (separate part id, edge rings duplicated so
// every triangle has a single part id), and claws at the tarsus tip. Only the coxa's buried start is blended
// 50/50 with the body's attach bone, like the lizard shoulder ring.
public static class ArthropodLegMesh
{
    // Part ids: 1 leg and 2 foot as CreatureMeshBuilder; the octopus uses 4..7 and the wyvern 8.
    public const int PartLeg = 1, PartFoot = 2, PartBand = 9;

    const int kCapRings = 2;            // rings per hemispherical cap (besides the pole)
    const int kBulbBands = 4;
    const int kClawSides = 5;
    const float kClawBend = 70f;        // degrees a claw curls down over its length
    const float kClawSplay = 22f;       // degrees between two claws' directions (fanned about the up axis)
    const float kMinRadius = 1e-4f;

    // pts[offset .. offset + segs.Length]: the rest-pose points from ArthropodLegIK.Solve (coxa base, joints,
    // ankle, foot), planeNormal its pose.planeNormal; bones firstBone + k (k = segment), attachBone < 0 = none.
    // arcStart: uv0.x of the coxa base. claws: count at the tarsus tip, clawLength in metres (0 = 4 x tip radius).
    public static void Build(CreatureMeshLists m, LegSegment[] segs, Vector3[] pts, int offset, Vector3 planeNormal,
                             int firstBone, int attachBone, int sides, int claws, float clawLength, float arcStart = 0f)
    {
        int n = segs.Length;
        sides = Mathf.Max(3, sides);
        float arc = arcStart;
        for (int k = 0; k < n; k++)
        {
            Vector3 a = pts[offset + k], b = pts[offset + k + 1];
            Quaternion q = ArthropodLegIK.SegmentRotation(a, b, planeNormal);
            Segment(m, segs[k], a, b, q, firstBone + k, k == 0 ? attachBone : -1, sides, arc);
            float L = (b - a).magnitude;
            if (k + 1 < n && segs[k].knuckle > 0f)
            {
                float r = segs[k].knuckle * Mathf.Max(R0(segs[k + 1]), R1(segs[k]));
                int part = segs[k].bandEnd >= 1f && segs[k].bandEnd > segs[k].bandStart ? PartBand : PartLeg;
                Sphere(m, b, q, r, sides, kBulbBands, part, firstBone + k, arc + L);
            }
            arc += L;
        }
        int last = n - 1;
        if (claws > 0)
        {
            Vector3 a = pts[offset + last], b = pts[offset + n];
            Quaternion q = ArthropodLegIK.SegmentRotation(a, b, planeNormal);
            float rt = R1(segs[last]);
            float cl = clawLength > 0f ? clawLength : 4f * rt;
            for (int c = 0; c < claws; c++)
            {
                float yaw = claws == 1 ? 0f : Mathf.Lerp(-0.5f * kClawSplay, 0.5f * kClawSplay, c / (claws - 1f));
                Claw(m, b + q * Vector3.forward * (0.5f * rt), q * Quaternion.AngleAxis(yaw, Vector3.up), 0.5f * rt, cl, firstBone + last, arc);
            }
        }
    }

    static float R0(LegSegment s) => Mathf.Max(kMinRadius, s.radiusStart);
    static float R1(LegSegment s) => Mathf.Max(kMinRadius, s.radiusEnd > 0f ? s.radiusEnd : s.radiusStart);

    static Vector4 Rigid(int bone) => new Vector4(bone, bone, 1f, 0f);

    static void Vertex(CreatureMeshLists m, Vector3 p, Vector3 normal, float u, float v, int part, Vector4 skin)
    {
        m.pos.Add(p); m.nrm.Add(normal);
        m.uv0.Add(new Vector2(u, v)); m.uv1.Add(new Vector2(part, Mathf.Clamp(normal.y, -1f, 1f))); m.uv2.Add(skin);
    }

    // A ring of sides + 1 vertices (seam duplicated) around centre c in the plane of x, y; normals tilted
    // along t by tilt (radial * 1 + t * tilt, normalized).
    static int Ring(CreatureMeshLists m, Vector3 c, Vector3 x, Vector3 y, Vector3 t, float r, float tilt, float u, int part, Vector4 skin, int sides)
    {
        int first = m.pos.Count;
        for (int k = 0; k <= sides; k++)
        {
            float phi = 2f * Mathf.PI * k / sides;
            float cs = k == sides ? 1f : Mathf.Cos(phi), sn = k == sides ? 0f : Mathf.Sin(phi);
            Vector3 radial = x * cs + y * sn;
            Vertex(m, c + radial * r, (radial + t * tilt).normalized, u, (float)k / sides, part, skin);
        }
        return first;
    }

    static int Pole(CreatureMeshLists m, Vector3 p, Vector3 normal, float u, int part, Vector4 skin, int sides)
    {
        int first = m.pos.Count;
        for (int k = 0; k <= sides; k++) Vertex(m, p, normal, u, (k + 0.5f) / sides, part, skin);
        return first;
    }

    // Quads from ring A to ring B (B further along t), outward for rings that run from x toward y with x cross y = t.
    static void Stitch(CreatureMeshLists m, int a, int b, int sides, bool poleA, bool poleB)
    {
        for (int k = 0; k < sides; k++)
        {
            if (!poleA) { m.tris.Add(a + k); m.tris.Add(a + k + 1); m.tris.Add(b + k); }
            if (!poleB) { m.tris.Add(poleA ? a + k : a + k + 1); m.tris.Add(b + k + 1); m.tris.Add(b + k); }
        }
    }

    struct Sample { public float sigma, r, tilt; public int part; public bool pole, restart; public Vector4 skin; }

    static readonly List<Sample> samples = new List<Sample>(); // build-time scratch (mesh building is main-thread only)

    static void Segment(CreatureMeshLists m, LegSegment s, Vector3 a, Vector3 b, Quaternion q, int bone, int attach, int sides, float arc)
    {
        Vector3 t = q * Vector3.forward, x = q * Vector3.right, y = q * Vector3.up;
        float L = (b - a).magnitude, r0 = R0(s), r1 = R1(s);
        float slope = L > 1e-6f ? (r1 - r0) / L : 0f;   // dr / dsigma along the tube
        bool band = s.bandEnd > s.bandStart;
        float bs = Mathf.Clamp01(s.bandStart) * L, be = Mathf.Clamp01(s.bandEnd) * L;
        int startPart = band && bs <= 0f ? PartBand : PartLeg, endPart = band && be >= L ? PartBand : PartLeg;
        Vector4 rigid = Rigid(bone), startSkin = attach >= 0 ? new Vector4(bone, attach, 0.5f, 0.5f) : rigid;

        samples.Clear();
        void Add(float sg, float r, float tilt, int part, Vector4 skin, bool pole = false, bool restart = false) =>
            samples.Add(new Sample { sigma = sg, r = r, tilt = tilt, part = part, skin = skin, pole = pole, restart = restart });

        // start cap: hemisphere of radius r0 centred on the start joint
        Add(-r0, 0f, 0f, startPart, startSkin, pole: true);
        for (int i = 1; i <= kCapRings; i++)
        {
            float th = 0.5f * Mathf.PI * i / (kCapRings + 1);           // polar angle from -t
            float sg = -r0 * Mathf.Cos(th), rr = r0 * Mathf.Sin(th);
            Add(sg, rr, -Mathf.Cos(th) / Mathf.Max(1e-6f, Mathf.Sin(th)), startPart, startSkin);
        }
        // tube: the start ring, band edges (duplicated across a part change), the end ring
        Add(0f, r0, -slope, startPart, startSkin);
        if (band && bs > 0f && bs < L) { Add(bs, r0 + slope * bs, -slope, PartLeg, rigid); Add(bs, r0 + slope * bs, -slope, PartBand, rigid, restart: true); }
        if (band && be > 0f && be < L && be > bs) { Add(be, r0 + slope * be, -slope, PartBand, rigid); Add(be, r0 + slope * be, -slope, PartLeg, rigid, restart: true); }
        Add(L, r1, -slope, endPart, rigid);
        // end cap
        for (int i = kCapRings; i >= 1; i--)
        {
            float th = 0.5f * Mathf.PI * i / (kCapRings + 1);           // polar angle from +t
            Add(L + r1 * Mathf.Cos(th), r1 * Mathf.Sin(th), Mathf.Cos(th) / Mathf.Max(1e-6f, Mathf.Sin(th)), endPart, rigid);
        }
        Add(L + r1, 0f, 0f, endPart, rigid, pole: true);

        int prev = -1; bool prevPole = false;
        for (int i = 0; i < samples.Count; i++)
        {
            Sample sm = samples[i];
            Vector3 c = a + t * sm.sigma;
            int cur = sm.pole ? Pole(m, c, i == 0 ? -t : t, arc + sm.sigma, sm.part, sm.skin, sides)
                              : Ring(m, c, x, y, t, sm.r, sm.tilt, arc + sm.sigma, sm.part, sm.skin, sides);
            if (prev >= 0 && !sm.restart) Stitch(m, prev, cur, sides, prevPole, sm.pole);
            prev = cur; prevPole = sm.pole;
        }
    }

    // Sphere of radius r at c, poles along the frame's +-Z, rigid to one bone.
    static void Sphere(CreatureMeshLists m, Vector3 c, Quaternion q, float r, int sides, int bands, int part, int bone, float arc)
    {
        Vector3 t = q * Vector3.forward, x = q * Vector3.right, y = q * Vector3.up;
        Vector4 skin = Rigid(bone);
        int prev = Pole(m, c - t * r, -t, arc - r, part, skin, sides);
        for (int j = 1; j < bands; j++)
        {
            float beta = -0.5f * Mathf.PI + Mathf.PI * j / bands;
            float cb = Mathf.Cos(beta), sb = Mathf.Sin(beta);
            int cur = Ring(m, c + t * (r * sb), x, y, t, r * cb, sb / Mathf.Max(1e-6f, cb), arc + r * sb, part, skin, sides);
            Stitch(m, prev, cur, sides, j == 1, false);
            prev = cur;
        }
        int tip = Pole(m, c + t * r, t, arc + r, part, skin, sides);
        Stitch(m, prev, tip, sides, false, true);
    }

    static void Claw(CreatureMeshLists m, Vector3 basePt, Quaternion q, float r0, float len, int bone, float arc)
        => Horn(m, basePt, q, r0, 0f, len, kClawBend, bone, PartFoot, kClawSides, arc, out _, out _);

    // A curved tapered tube (claw, chelicera, fang, horn) rigid to one bone: from basePt along the frame's +Z,
    // curling toward the frame's -Y by curveDeg over its length, radius r0 -> r1 (0 = a point). Closed at both
    // ends. tip/tipDir: where it ends and its direction there (to continue it with another horn).
    public static void Horn(CreatureMeshLists m, Vector3 basePt, Quaternion q, float r0, float r1, float len, float curveDeg,
                            int bone, int part, int sides, float arc, out Vector3 tip, out Vector3 tipDir)
    {
        Vector4 skin = Rigid(bone);
        const int rings = 3;
        sides = Mathf.Max(3, sides);
        len = Mathf.Max(1e-5f, len); r0 = Mathf.Max(kMinRadius, r0); r1 = Mathf.Max(0f, r1);
        float bendRad = curveDeg * Mathf.Deg2Rad;
        Vector3 C(float u)
        {
            float k = Mathf.Abs(bendRad) > 1e-4f ? len / bendRad : 0f;  // an arc of a circle in the z-y plane
            Vector3 l = Mathf.Abs(bendRad) > 1e-4f ? new Vector3(0f, -(k - k * Mathf.Cos(bendRad * u)), k * Mathf.Sin(bendRad * u)) : new Vector3(0f, 0f, len * u);
            return basePt + q * l;
        }
        Vector3 D(float u)
        {
            float a = bendRad * u;
            return q * new Vector3(0f, -Mathf.Sin(a), Mathf.Cos(a));
        }
        Vector3 x = q * Vector3.right;                      // the curl is about the frame's x: x stays perpendicular
        float slope = (r0 - r1) / len;
        Vector3 t0 = D(0f);
        int prev = Pole(m, C(0f) - t0 * (0.5f * r0), -t0, arc, part, skin, sides);
        bool pointed = r1 <= kMinRadius;
        int last = pointed ? rings - 1 : rings;
        for (int i = 0; i <= last; i++)
        {
            float u = (float)i / rings;
            Vector3 t = D(u);
            Vector3 y = Vector3.Cross(t, x);
            float r = Mathf.Lerp(r0, r1, u);
            int cur = Ring(m, C(u), x, y, t, Mathf.Max(kMinRadius, r), slope, arc + len * u, part, skin, sides);
            Stitch(m, prev, cur, sides, i == 0, false);
            prev = cur;
        }
        tip = C(1f); tipDir = D(1f);
        int end = Pole(m, pointed ? tip : tip + tipDir * (0.5f * r1), tipDir, arc + len, part, skin, sides);
        Stitch(m, prev, end, sides, false, true);
    }
}
