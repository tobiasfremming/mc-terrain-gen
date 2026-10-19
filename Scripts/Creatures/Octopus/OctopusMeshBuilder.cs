using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The octopus's skinned mesh, built once per species in its rest pose (OctopusRestPose), in the vertex
// conventions of CreatureMeshBuilder so the creature shader can draw it:
//   position, normal: rest space (body space at scale 1)
//   uv0 = (arc length along the part in metres, angle / 2pi)
//   uv1 = (part id, dorsal coordinate -1 .. +1)
//   uv2 = (bone0, bone1, w0, w1), w0 + w1 = 1, bone0 the heavier
//   uv3 = (bone2, bone3, e2, e3): extra influences (web only; zero elsewhere). The exact skin is
//         (1 - e2 - e3) (w0 M0 + w1 M1) + e2 M2 + e3 M3; a 2-bone shader that ignores uv3 still gets a
//         valid (normalised top-2) blend.
// Parts (uv1.x): PartEye 3 (as CreatureMeshBuilder), PartArm 4, PartWeb 5, PartMantle 6, PartSiphon 7.
//   arms    tapered tubes, ArmRing vertices around x 3 rings per segment, ventral half flattened, a round
//           root end buried in the body and a pointed tip; skinned to neighbouring arm bones (joints at
//           50/50), the buried root to the web soft bone. Arm uv0.y starts at the ventral midline, so the
//           sucker rows sit around uv0.y = 0 (and 1).
//   web     a thin membrane (two sheets and a rounded rim) between neighbouring arms on their oral side,
//           scalloped between the arms; each vertex blends the two arms' bones at its arc length.
//   mantle  head + mantle shell (OctopusShape), skinned along its axis to the 6 soft bones.
//   eyes    spheres rigid to the eye bones; uv0 = (polar angle from the gaze / pi, azimuth / 2pi).
//   siphon  a short flared funnel rigid to the siphon bone.
// Normals come from central differences over each part's parametric grid; every triangle is wound to
// agree with its vertex normals.
public static class OctopusMeshBuilder
{
    public const int PartEye = 3 /* = CreatureMeshBuilder.PartEye */, PartArm = 4, PartWeb = 5, PartMantle = 6, PartSiphon = 7;
    public const int ArmRing = 12, RingsPerSegment = 3;
    const int kShellRows = 44, kShellAround = 28, kEyeRows = 10, kEyeAround = 14, kSiphonRows = 8, kSiphonAround = 10;
    const int kWebCols = 8, kWebRows = 6, kWebRim = 3;
    const float kRootBlob = 0.8f;        // x root radius: how far the arm's round end reaches into the body
    const float kTipLength = 3.5f;       // x tip radius: the pointed tip past the last particle
    const float kWebScallop = 0.45f;     // the web edge dips to (1 - this) x depth halfway between arms
    const float kWebThickRoot = 0.009f, kWebThickEdge = 0.004f; // metres at scale 1

    // Raw arrays, for tests and tools that do not need a Mesh.
    public sealed class Data
    {
        public readonly List<Vector3> pos = new List<Vector3>(), nrm = new List<Vector3>();
        public readonly List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
        public readonly List<Vector4> uv2 = new List<Vector4>(), uv3 = new List<Vector4>();
        public readonly List<int> tris = new List<int>();
        public int BoneCount;
    }

    public static Mesh Build(in CephalopodSpec sp, string name = "octopus")
    {
        Data d = BuildData(sp);
        var mesh = new Mesh { name = name };
        if (d.pos.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(d.pos);
        mesh.SetNormals(d.nrm);
        mesh.SetUVs(0, d.uv0);
        mesh.SetUVs(1, d.uv1);
        mesh.SetUVs(2, d.uv2);
        mesh.SetUVs(3, d.uv3);
        mesh.SetTriangles(d.tris, 0, true);
        return mesh;
    }

    public static Data BuildData(in CephalopodSpec sp)
    {
        var b = new Builder(sp);
        for (int a = 0; a < sp.ArmCount; a++) b.Arm(a);
        // (two arms leave from opposite sides: no web between them - it would be a sheet across the body)
        if (sp.ArmCount > 2) for (int a = 0; a < sp.ArmCount; a++) b.Web(a, (a + 1) % sp.ArmCount);
        for (int m = 0; m < sp.MouthCount; m++) b.Mouth(m);
        b.Shell();
        b.Eyes();
        b.SiphonTube();
        if (sp.eyeStalk > 0f) b.EyeStalks();
        if (sp.beakLength > 0f) b.Beak();
        b.data.BoneCount = b.body.BoneCount;
        return b.data;
    }

    // Surface sample: position plus the per-vertex attributes that do not come from the geometry.
    delegate Vector3 Surface(float u, float v);
    delegate void Attributes(float u, float v, Vector3 p, Vector3 n, out Vector4 skin, out Vector4 skin2, out Vector2 uv0, out float dorsal);

    sealed class Builder
    {
        public readonly Data data = new Data();
        public readonly OctopusBody body;
        readonly CephalopodSpec sp;
        readonly TentacleSpec arm;
        readonly int n;

        readonly Vector3[] armRoot, armDir;
        float radiusS = float.NaN, radiusCache;   // ArmPoint: one radius per ring

        public Builder(in CephalopodSpec sp)
        {
            this.sp = sp; arm = sp.arm; n = arm.Segments;
            body = OctopusRestPose.Body(sp);
            armRoot = new Vector3[sp.ArmCount]; armDir = new Vector3[sp.ArmCount];
            for (int a = 0; a < sp.ArmCount; a++) { armRoot[a] = OctopusShape.ArmRoot(sp, a); armDir[a] = OctopusShape.ArmDir(sp, a); }
            int estimate = 12000;
            data.pos.Capacity = estimate; data.nrm.Capacity = estimate; data.uv0.Capacity = estimate; data.uv1.Capacity = estimate;
            data.uv2.Capacity = estimate; data.uv3.Capacity = estimate; data.tris.Capacity = 3 * 2 * estimate;
        }

        static Vector4 Blend(int b0, int b1, float w1) =>
            w1 > 0.5f ? new Vector4(b1, b0, w1, 1f - w1) : new Vector4(b0, b1, 1f - w1, w1);

        // A (rows + 1) x (cols + 1) vertex grid over u = us[i], v = vs[j]. Rows flagged as poles collapse to
        // one point and take poleNormal. Normals: cross of the parameter derivatives, flipped by `flip`;
        // each quad's triangles are wound to agree with them (degenerate pole triangles are skipped).
        void Grid(float[] us, float[] vs, Surface f, Attributes attr, int part, bool flip,
                  bool poleFirst, Vector3 poleFirstNormal, bool poleLast, Vector3 poleLastNormal)
        {
            int rows = us.Length, cols = vs.Length, first = data.pos.Count;
            // positions once; normals from the grid neighbours (central differences, one-sided at open
            // edges, wrapped across a duplicated seam), in the parameter order of Cross(dP/dv, dP/du)
            var P = new Vector3[rows * cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++) P[i * cols + j] = f(us[i], vs[j]);
            bool closed = true;
            for (int i = 0; i < rows && closed; i++) closed = (P[i * cols] - P[i * cols + cols - 1]).sqrMagnitude < 1e-14f;
            for (int i = 0; i < rows; i++)
            {
                float u = us[i];
                bool pole = (i == 0 && poleFirst) || (i == rows - 1 && poleLast);
                int iu0 = Mathf.Max(0, i - 1), iu1 = Mathf.Min(rows - 1, i + 1);
                for (int j = 0; j < cols; j++)
                {
                    Vector3 p = P[i * cols + j], nn;
                    if (pole) nn = i == 0 ? poleFirstNormal : poleLastNormal;
                    else
                    {
                        int jv0 = j - 1, jv1 = j + 1;
                        if (closed) { if (jv0 < 0) jv0 = cols - 2; if (jv1 > cols - 1) jv1 = 1; }
                        else { jv0 = Mathf.Max(0, jv0); jv1 = Mathf.Min(cols - 1, jv1); }
                        Vector3 pu = P[iu1 * cols + j] - P[iu0 * cols + j], pv = P[i * cols + jv1] - P[i * cols + jv0];
                        nn = Vector3.Cross(pv, pu);
                        // scale-free normalisation: crosses on thin parts (the web rim) are tiny, and .normalized
                        // zeroes anything under 1e-5
                        double nl = Math.Sqrt((double)nn.x * nn.x + (double)nn.y * nn.y + (double)nn.z * nn.z);
                        nn = nl > 1e-30 ? new Vector3((float)(nn.x / nl), (float)(nn.y / nl), (float)(nn.z / nl)) : Vector3.up;
                        if (flip) nn = -nn;
                    }
                    attr(u, vs[j], p, nn, out Vector4 skin, out Vector4 skin2, out Vector2 uv, out float dorsal);
                    data.pos.Add(p); data.nrm.Add(nn); data.uv0.Add(uv);
                    data.uv1.Add(new Vector2(part, Mathf.Clamp(dorsal, -1f, 1f))); data.uv2.Add(skin); data.uv3.Add(skin2);
                }
            }
            for (int i = 0; i + 1 < rows; i++)
                for (int j = 0; j + 1 < cols; j++)
                {
                    int a = first + i * cols + j, b = a + 1, c = a + cols, d = c + 1;
                    bool topPole = i == 0 && poleFirst, botPole = i + 1 == rows - 1 && poleLast;
                    if (!topPole) Tri(a, b, d);
                    if (!botPole) Tri(a, d, c);
                }
        }

        void Tri(int i0, int i1, int i2)
        {
            List<Vector3> p = data.pos, nr = data.nrm;
            Vector3 a = p[i0], b = p[i1], c = p[i2];
            float ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
            float fx = uy * vz - uz * vy, fy = uz * vx - ux * vz, fz = ux * vy - uy * vx;
            if (fx * fx + fy * fy + fz * fz < 1e-22f) return;
            Vector3 n0 = nr[i0], n1 = nr[i1], n2 = nr[i2];
            float dot = fx * (n0.x + n1.x + n2.x) + fy * (n0.y + n1.y + n2.y) + fz * (n0.z + n1.z + n2.z);
            data.tris.Add(i0);
            if (dot >= 0f) { data.tris.Add(i1); data.tris.Add(i2); }
            else { data.tris.Add(i2); data.tris.Add(i1); }
        }

        static float[] Range(float a, float b, int steps)
        {
            var r = new float[steps + 1];
            for (int i = 0; i <= steps; i++) r[i] = Mathf.Lerp(a, b, (float)i / steps);
            return r;
        }

        // ---------------------------------------------------------------- arms

        float ArmRadius(float s)
        {
            float L = arm.Length, r0 = arm.RootRadius, rt = arm.TipRadius;
            float blob = kRootBlob * r0, tip = kTipLength * rt;
            if (s < 0f) { float q = s / blob; return r0 * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
            if (s > L) { float q = Mathf.Clamp01((s - L) / tip); return rt * Mathf.Pow(1f - q, 0.6f) * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q * q * q)); }
            return arm.RadiusAt(s);
        }

        // Arm a, rest pose: point at arc s, ring angle phi (0 = ventral midline, increasing toward +x).
        Vector3 ArmPoint(int a, float s, float phi)
        {
            Vector3 root = armRoot[a], d = armDir[a];
            if (s != radiusS) { radiusS = s; radiusCache = ArmRadius(s); }
            float r = radiusCache;
            float cs = MathF.Sin(phi), sn = -MathF.Cos(phi); // phi 0 -> belly (sn = -1); = cos/sin(phi - pi/2)
            float half = sn > 0f ? r : r * arm.Flatten;
            // x = up cross d = (d.z, 0, -d.x)
            float lx = r * cs;
            return new Vector3(root.x + d.x * s + d.z * lx, root.y + d.y * s + half * sn, root.z + d.z * s - d.x * lx);
        }

        // Arm skin by arc s: joints blend 50/50, segment middles are rigid; the buried root follows the web.
        Vector4 ArmSkin(int a, float s)
        {
            float sp0 = arm.Spacing, L = arm.Length;
            int b0 = body.ArmBone(a, 0), web = body.SoftBone(OctopusShape.ClusterWeb);
            if (s <= 0f) return Blend(web, b0, 0.5f);
            if (s < 0.5f * sp0) return Blend(web, b0, 0.5f + s / sp0);
            if (s >= L) return Blend(body.ArmBone(a, n - 2), body.ArmBone(a, n - 1), Mathf.Min(1f, 0.5f + (s - L) / sp0));
            float f = s / sp0;
            int k = Mathf.Min((int)f, n - 2);
            float fr = f - k;
            if (fr < 0.5f) return Blend(body.ArmBone(a, k - 1), body.ArmBone(a, k), 0.5f + fr);
            return Blend(body.ArmBone(a, k), body.ArmBone(a, k + 1), fr - 0.5f);
        }

        public void Arm(int a)
        {
            float L = arm.Length, r0 = arm.RootRadius, blob = kRootBlob * r0, tip = kTipLength * arm.TipRadius;
            var us = new List<float> { -blob, -blob * Mathf.Sin(60f * Mathf.Deg2Rad), -blob * Mathf.Sin(30f * Mathf.Deg2Rad) };
            int rings = (n - 1) * RingsPerSegment;
            for (int i = 0; i <= rings; i++) us.Add(L * i / rings);
            us.Add(L + tip * 0.3f); us.Add(L + tip * 0.6f); us.Add(L + tip * 0.85f); us.Add(L + tip);
            Vector3 d = OctopusShape.ArmDir(sp, a);
            float skinS = float.NaN; Vector4 skinCache = default;   // one skin per ring
            Grid(us.ToArray(), Range(0f, 2f * Mathf.PI, ArmRing),
                 (s, phi) => ArmPoint(a, s, phi),
                 (float s, float phi, Vector3 p, Vector3 nn, out Vector4 skin, out Vector4 skin2, out Vector2 uv, out float dorsal) =>
                 {
                     skin2 = default;
                     if (s != skinS) { skinS = s; skinCache = ArmSkin(a, s); }
                     skin = skinCache;
                     uv = new Vector2(s, phi / (2f * Mathf.PI));
                     dorsal = Mathf.Sin(phi - 0.5f * Mathf.PI);
                 },
                 PartArm, OutwardFlipArm(a), true, -d, true, d);
        }

        // The arm grid's raw normal orientation, decided once from a side sample.
        bool OutwardFlipArm(int a)
        {
            float s = 0.5f * arm.Length, phi = 0.5f * Mathf.PI, h = 1e-3f;
            Vector3 p = ArmPoint(a, s, phi);
            Vector3 nn = Vector3.Cross(ArmPoint(a, s, phi + h) - ArmPoint(a, s, phi - h), ArmPoint(a, s + h, phi) - ArmPoint(a, s - h, phi));
            Vector3 c = OctopusShape.ArmRoot(sp, a) + OctopusShape.ArmDir(sp, a) * s;
            return Vector3.Dot(nn, p - c) < 0f;
        }

        // ---------------------------------------------------------------- mouth tentacles

        // Mouth tentacle m (cephalopod.mouthCount): the arm's tube (round buried root, tapered, pointed tip, flat
        // ventral half) along its rest direction, in the frame of OctopusShape.MouthRot (dorsal = its +Y), skinned
        // to its own bones (joints 50/50) and the buried root to the web. Drawn as an arm (PartArm: suckers).
        public void Mouth(int m)
        {
            TentacleSpec ms = sp.mouth;
            int nm = ms.Segments;
            float L = ms.Length, r0 = ms.RootRadius, rt = ms.TipRadius, blob = kRootBlob * r0, tip = kTipLength * rt, sp0 = ms.Spacing;
            Vector3 root = OctopusShape.MouthRoot(sp, m);
            Quaternion q = OctopusShape.MouthRot(sp, m);
            Vector3 d = OctoMath.Rotate(q, Vector3.forward), up = OctoMath.Rotate(q, Vector3.up), side = OctoMath.Rotate(q, Vector3.right);
            int web = body.SoftBone(OctopusShape.ClusterWeb);
            float Rad(float s)
            {
                if (s < 0f) { float k = s / blob; return r0 * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k)); }
                if (s > L) { float k = Mathf.Clamp01((s - L) / tip); return rt * Mathf.Pow(1f - k, 0.6f) * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k * k * k)); }
                return ms.RadiusAt(s);
            }
            Vector3 Point(float s, float phi)
            {
                float r = Rad(s), cs = MathF.Sin(phi), sn = -MathF.Cos(phi);
                float half = sn > 0f ? r : r * ms.Flatten;
                return root + d * s + side * (r * cs) + up * (half * sn);
            }
            Vector4 Skin(float s)
            {
                int b0 = body.MouthBone(m, 0);
                if (s <= 0f) return Blend(web, b0, 0.5f);
                if (s < 0.5f * sp0) return Blend(web, b0, 0.5f + s / sp0);
                if (s >= L) return Blend(body.MouthBone(m, nm - 2), body.MouthBone(m, nm - 1), Mathf.Min(1f, 0.5f + (s - L) / sp0));
                float f = s / sp0;
                int k = Mathf.Min((int)f, nm - 2);
                float fr = f - k;
                if (fr < 0.5f) return Blend(body.MouthBone(m, Mathf.Max(0, k - 1)), body.MouthBone(m, k), 0.5f + fr);
                return Blend(body.MouthBone(m, k), body.MouthBone(m, k + 1), fr - 0.5f);
            }
            var us = new List<float> { -blob, -blob * Mathf.Sin(60f * Mathf.Deg2Rad), -blob * Mathf.Sin(30f * Mathf.Deg2Rad) };
            int rings = (nm - 1) * RingsPerSegment;
            for (int i = 0; i <= rings; i++) us.Add(L * i / rings);
            us.Add(L + tip * 0.3f); us.Add(L + tip * 0.6f); us.Add(L + tip * 0.85f); us.Add(L + tip);
            // the grid's raw normal orientation from a side sample
            float hs = 0.5f * L, hp = 0.5f * Mathf.PI, h = 1e-3f;
            Vector3 nn0 = Vector3.Cross(Point(hs, hp + h) - Point(hs, hp - h), Point(hs + h, hp) - Point(hs - h, hp));
            bool flip = Vector3.Dot(nn0, Point(hs, hp) - (root + d * hs)) < 0f;
            Grid(us.ToArray(), Range(0f, 2f * Mathf.PI, ArmRing), Point,
                 (float s, float phi, Vector3 p, Vector3 nn, out Vector4 skin, out Vector4 skin2, out Vector2 uv, out float dorsal) =>
                 {
                     skin2 = default;
                     skin = Skin(s);
                     uv = new Vector2(s, phi / (2f * Mathf.PI));
                     dorsal = Mathf.Sin(phi - 0.5f * Mathf.PI);
                 },
                 PartArm, flip, true, -d, true, d);
        }

        // ---------------------------------------------------------------- web

        // Side point of arm a at arc s on the side facing `toward`, on the oral half, inside the tube.
        Vector3 ArmSide(int a, float s, Vector3 toward)
        {
            Vector3 root = armRoot[a], d = armDir[a];
            // x = up cross d = (d.z, 0, -d.x), turned toward the neighbour
            float xx = d.z, xz = -d.x;
            if (xx * toward.x + xz * toward.z < 0f) { xx = -xx; xz = -xz; }
            float r = arm.RadiusAt(s > 0f ? s : 0f), l = 0.6f * r;
            return new Vector3(root.x + d.x * s + xx * l, root.y + d.y * s - 0.3f * r, root.z + d.z * s + xz * l);
        }

        public void Web(int a, int b)
        {
            float depth = sp.WebDepth * arm.Length;
            Vector3 da = OctopusShape.ArmDir(sp, a), db = OctopusShape.ArmDir(sp, b);
            // v in [0, 1]: bottom sheet root -> edge; [1, 2]: around the rim; [2, 3]: top sheet edge -> root
            Vector3 P(float u, float v)
            {
                float edge = depth * (1f - kWebScallop * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u)));
                float t, side; // t: fraction of the way to the edge; side: -1 bottom .. +1 top
                if (v <= 1f) { t = v; side = -1f; }
                else if (v >= 2f) { t = 3f - v; side = 1f; }
                else { t = 1f; side = -Mathf.Cos(Mathf.PI * (v - 1f)); }
                float s = edge * t;
                Vector3 sa = ArmSide(a, s, db), sb = ArmSide(b, s, da);
                Vector3 c = Vector3.Lerp(sa, sb, u);
                float width = (sa - sb).magnitude;
                float sag = 0.08f * width * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u)) * t;
                float thick = 0.5f * Mathf.Lerp(kWebThickRoot, kWebThickEdge, t);
                // rim: the membrane edge rounds over by its half thickness, outward along the arms' mean direction
                Vector3 outDir = (da + db).normalized;
                float rimOut = v > 1f && v < 2f ? thick * Mathf.Sin(Mathf.PI * (v - 1f)) : 0f;
                return c + Vector3.up * (side * thick - sag) + outDir * rimOut;
            }
            float[] us = Range(0f, 1f, kWebCols);
            var vs = new List<float>();
            for (int i = 0; i <= kWebRows; i++) vs.Add((float)i / kWebRows);
            for (int i = 1; i < kWebRim; i++) vs.Add(1f + (float)i / kWebRim);
            for (int i = 0; i <= kWebRows; i++) vs.Add(2f + (float)i / kWebRows);
            int ba = a, bb = b;
            // rows along u (across), columns along v (around the membrane)
            Grid(us, vs.ToArray(), P,
                 (float u, float v, Vector3 p, Vector3 nn, out Vector4 skin, out Vector4 skin2, out Vector2 uv, out float dorsal) =>
                 {
                     skin2 = default;
                     float edge = depth * (1f - kWebScallop * Mathf.Sin(Mathf.PI * u));
                     float t = v <= 1f ? v : v >= 2f ? 3f - v : 1f;
                     float s = edge * t;
                     FourBones(ArmSkin(ba, s), ArmSkin(bb, s), u, out skin, out skin2);
                     uv = new Vector2(s, u);
                     dorsal = nn.y;
                 },
                 PartWeb, WebFlip(P), false, Vector3.zero, false, Vector3.zero);
        }

        // Bilinear web skin: (1 - u) x arm a's skin + u x arm b's skin at the same arc length (up to 4 bones,
        // duplicates merged): the heaviest two, normalised, into uv2; the other two with their weights into uv3.
        static void FourBones(Vector4 sa, Vector4 sb, float u, out Vector4 skin, out Vector4 extra)
        {
            Span<int> b = stackalloc int[4];
            Span<float> w = stackalloc float[4];
            b[0] = (int)sa.x; b[1] = (int)sa.y; b[2] = (int)sb.x; b[3] = (int)sb.y;
            w[0] = (1f - u) * sa.z; w[1] = (1f - u) * sa.w; w[2] = u * sb.z; w[3] = u * sb.w;
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (w[j] > 0f && b[j] == b[i]) { w[i] += w[j]; w[j] = 0f; }
            // sort by weight, descending (4 elements)
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (w[j] > w[i]) { float tw = w[i]; w[i] = w[j]; w[j] = tw; int tb = b[i]; b[i] = b[j]; b[j] = tb; }
            float top = w[0] + w[1];
            skin = new Vector4(b[0], w[1] > 0f ? b[1] : b[0], w[0] / top, w[1] / top);
            extra = new Vector4(w[2] > 0f ? b[2] : b[0], w[3] > 0f ? b[3] : b[0], w[2], w[3]);
        }

        // Orientation of the web grid: the top sheet's normal must point up.
        static bool WebFlip(Surface p)
        {
            float u = 0.5f, v = 2.5f, h = 1e-3f;
            Vector3 nn = Vector3.Cross(p(u, v + h) - p(u, v - h), p(u + h, v) - p(u - h, v));
            return nn.y < 0f;
        }

        // ---------------------------------------------------------------- head / mantle shell

        public void Shell()
        {
            // soft-bone anchors along the axis: web, head, mantle base, mid, tip clusters, then the apex particle
            int sb = OctopusBody.SoftBoneCount;
            var anchor = new float[sb];
            for (int c = 0; c < sb; c++) anchor[c] = OctopusShape.NearestT(sp, body.SoftBonePivot(c));
            for (int c = 1; c < sb; c++) anchor[c] = Mathf.Max(anchor[c], anchor[c - 1] + 1e-3f);

            // arc length along the axis for uv0.x
            const int kArc = 128;
            var arcLen = new float[kArc + 1];
            for (int i = 1; i <= kArc; i++)
                arcLen[i] = arcLen[i - 1] + (OctopusShape.Axis(sp, (float)i / kArc) - OctopusShape.Axis(sp, (float)(i - 1) / kArc)).magnitude;
            float Arc(float t) { float f = Mathf.Clamp01(t) * kArc; int i = Mathf.Min((int)f, kArc - 1); return Mathf.Lerp(arcLen[i], arcLen[i + 1], f - i); }

            var ts = new float[kShellRows + 1];
            for (int i = 0; i <= kShellRows; i++) ts[i] = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / kShellRows);
            CephalopodSpec spec = sp;
            // the grid evaluates row by row: cache the axis point, front and profile of the current row
            float rowT = float.NaN, rowProfile = 0f;
            Vector3 rowC = default, rowN = default;
            Surface f = (t, phi) =>
            {
                t = Mathf.Clamp01(t);
                if (t != rowT) { rowT = t; rowC = OctopusShape.Axis(spec, t); rowN = OctopusShape.AxisFront(spec, t); rowProfile = OctopusShape.Profile(spec, t); }
                return OctopusShape.ShellPoint(rowC, rowN, rowProfile, t, phi);
            };
            Vector3 p0 = f(0.5f, 0.5f * Mathf.PI), h0 = Vector3.Cross(f(0.5f, 0.5f * Mathf.PI + 1e-3f) - f(0.5f, 0.5f * Mathf.PI - 1e-3f), f(0.501f, 0.5f * Mathf.PI) - f(0.499f, 0.5f * Mathf.PI));
            bool flip = Vector3.Dot(h0, p0 - OctopusShape.Axis(sp, 0.5f)) < 0f;
            Grid(ts, Range(-Mathf.PI, Mathf.PI, kShellAround), f,
                 (float t, float phi, Vector3 p, Vector3 nn, out Vector4 skin, out Vector4 skin2, out Vector2 uv, out float dorsal) =>
                 {
                     skin2 = default;
                     skin = SoftSkin(anchor, t);
                     uv = new Vector2(Arc(t), (phi + Mathf.PI) / (2f * Mathf.PI));
                     dorsal = nn.y;
                 },
                 PartMantle, flip, true, -OctopusShape.AxisTangent(sp, 0f), true, OctopusShape.AxisTangent(sp, 1f));
        }

        Vector4 SoftSkin(float[] anchor, float t)
        {
            int last = anchor.Length - 1;
            if (t <= anchor[0]) return new Vector4(body.SoftBone(0), body.SoftBone(0), 1f, 0f);
            if (t >= anchor[last]) return new Vector4(body.SoftBone(last), body.SoftBone(last), 1f, 0f);
            int i = 0;
            while (i < last - 1 && t > anchor[i + 1]) i++;
            float w = OctoMath.Smooth((t - anchor[i]) / (anchor[i + 1] - anchor[i]));
            return Blend(body.SoftBone(i), body.SoftBone(i + 1), w);
        }

        // ---------------------------------------------------------------- eyes, siphon

        public void Eyes()
        {
            float r = sp.EyeRadius;
            for (int e = 0; e < 2; e++)
            {
                OctopusShape.Eye(sp, e, out Vector3 c, out Quaternion q);
                int bone = body.EyeBone(e);
                Vector3 gaze = OctoMath.Rotate(q, Vector3.forward);
                Surface f = (th, ph) => c + OctoMath.Rotate(q, new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th))) * r;
                Grid(Range(0f, Mathf.PI, kEyeRows), Range(0f, 2f * Mathf.PI, kEyeAround), f,
                     (float th, float ph, Vector3 p, Vector3 nn, out Vector4 skin, out Vector4 skin2, out Vector2 uv, out float dorsal) =>
                     {
                         skin2 = default;
                         skin = new Vector4(bone, bone, 1f, 0f);
                         uv = new Vector2(th / Mathf.PI, ph / (2f * Mathf.PI));
                         dorsal = nn.y;
                     },
                     PartEye, SphereFlip(f, c), true, gaze, true, -gaze);
            }
        }

        static bool SphereFlip(Surface f, Vector3 centre)
        {
            float u = 0.5f * Mathf.PI, v = 1f, h = 1e-3f;
            Vector3 nn = Vector3.Cross(f(u, v + h) - f(u, v - h), f(u + h, v) - f(u - h, v));
            return Vector3.Dot(nn, f(u, v) - centre) < 0f;
        }

        public void SiphonTube()
        {
            OctopusShape.Siphon(sp, out Vector3 basePt, out Quaternion q);
            float len = sp.SiphonLength, r = sp.SiphonRadius, buried = 0.6f * len;
            int bone = body.SiphonBone;
            Vector3 axis = OctoMath.Rotate(q, Vector3.forward);
            float R(float s)
            {
                if (s < -buried + r) { float k = (s + buried) / r; return r * Mathf.Sqrt(Mathf.Clamp01(k * (2f - k))); }
                if (s > len - 0.5f * r) { float k = (len - s) / (0.5f * r); return r * (1.25f * Mathf.Sqrt(Mathf.Clamp01(k * (2f - k)))); }
                return r * (0.85f + 0.4f * Mathf.Clamp01(s / len));
            }
            Surface f = (s, ph) => basePt + OctoMath.Rotate(q, new Vector3(Mathf.Cos(ph) * R(s), Mathf.Sin(ph) * R(s), s));
            var us = new List<float> { -buried };
            for (int i = 1; i < kSiphonRows; i++) us.Add(Mathf.Lerp(-buried, len, (float)i / kSiphonRows));
            us.Add(len);
            Grid(us.ToArray(), Range(0f, 2f * Mathf.PI, kSiphonAround), f,
                 (float s, float ph, Vector3 p, Vector3 nn, out Vector4 skin, out Vector4 skin2, out Vector2 uv, out float dorsal) =>
                 {
                     skin2 = default;
                     skin = new Vector4(bone, bone, 1f, 0f);
                     uv = new Vector2(s + buried, ph / (2f * Mathf.PI));
                     dorsal = nn.y;
                 },
                 PartSiphon, SiphonFlip(f, basePt, axis), true, -axis, true, axis);
        }

        // A tube along `axis` from basePt - axis buried to basePt + axis len, radius R(s), rigid to one bone
        // (round closed ends from R going to zero). Eye stalks and the beak (cephalopod.eyeStalk / beakLength).
        void RigidTube(Vector3 basePt, Vector3 axis, float buried, float len, Func<float, float> R, int bone, int part, int rows, int around)
        {
            Quaternion q = OctoMath.Look(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right);
            Surface f = (s, ph) => basePt + OctoMath.Rotate(q, new Vector3(Mathf.Cos(ph) * R(s), Mathf.Sin(ph) * R(s), s));
            var us = new List<float> { -buried };
            for (int i = 1; i < rows; i++) us.Add(Mathf.Lerp(-buried, len, (float)i / rows));
            us.Add(len);
            Grid(us.ToArray(), Range(0f, 2f * Mathf.PI, around), f,
                 (float s, float ph, Vector3 p, Vector3 nn, out Vector4 skin, out Vector4 skin2, out Vector2 uv, out float dorsal) =>
                 {
                     skin2 = default;
                     skin = new Vector4(bone, bone, 1f, 0f);
                     uv = new Vector2(s + buried, ph / (2f * Mathf.PI));
                     dorsal = nn.y;
                 },
                 part, SiphonFlip(f, basePt, axis), true, -axis, true, axis);
        }

        // Eyes on stalks: a tapering stalk from inside the head to the eye ball, rigid to the eye's bone (drawn as
        // skin: PartMantle).
        public void EyeStalks()
        {
            float r0 = 0.42f * sp.EyeRadius, r1 = 0.3f * sp.EyeRadius, len = sp.eyeStalk, buried = 0.6f * r0 + 0.3f * sp.HeadRadius;
            for (int e = 0; e < 2; e++)
            {
                Vector3 root = OctopusShape.EyeStalkRoot(sp, e), dir = OctopusShape.EyeStalkDir(sp, e);
                float R(float s)
                {
                    if (s < -buried + r0) { float k = (s + buried) / r0; return r0 * Mathf.Sqrt(Mathf.Clamp01(k * (2f - k))); }
                    return Mathf.Lerp(r0, r1, Mathf.Clamp01(s / len));
                }
                RigidTube(root, dir, buried, len, R, body.EyeBone(e), PartMantle, 8, 10);
            }
        }

        // The beak: a dark horny cone at the oral pole, pointing out of the mouth ring, rigid to the web (PartSiphon).
        public void Beak()
        {
            float len = sp.beakLength, rb = 0.45f * len, buried = 0.5f * len;
            Vector3 axis = -OctopusShape.AxisTangent(sp, 0f), basePt = OctopusShape.Axis(sp, 0f);
            float R(float s)
            {
                if (s < -buried + rb) { float k = (s + buried) / rb; return rb * Mathf.Sqrt(Mathf.Clamp01(k * (2f - k))); }
                float t = Mathf.Clamp01(s / len);
                return rb * Mathf.Pow(1f - t, 0.8f) * Mathf.Sqrt(Mathf.Clamp01(1f - t * t * t * t)) + 1e-4f;
            }
            RigidTube(basePt, axis, buried, len, R, body.SoftBone(OctopusShape.ClusterWeb), PartSiphon, 8, 10);
        }

        static bool SiphonFlip(Surface f, Vector3 basePt, Vector3 axis)
        {
            float s = 0f, v = 1f, h = 1e-3f;
            Vector3 p = f(s, v), nn = Vector3.Cross(f(s, v + h) - f(s, v - h), f(s + h, v) - f(s - h, v));
            Vector3 c = basePt + axis * Vector3.Dot(p - basePt, axis);
            return Vector3.Dot(nn, p - c) < 0f;
        }
    }
}

// The octopus rest pose: a fresh, unticked OctopusBody at the origin, identity rotation, scale 1 (arms
// straight and horizontal, lattice at rest). The mesh is authored in this space; bind poses are the
// inverses of its bone worlds (skin = boneWorld x bindPose).
public static class OctopusRestPose
{
    internal static OctopusBody Body(in CephalopodSpec sp) => new OctopusBody(sp, Vector3.zero, Quaternion.identity, 1f);

    public static Matrix4x4[] BoneWorld(in CephalopodSpec sp)
    {
        OctopusBody b = Body(sp);
        var m = new Matrix4x4[b.BoneCount];
        b.WriteBoneWorld(m, 0);
        return m;
    }

    public static Matrix4x4[] BindPoses(in CephalopodSpec sp)
    {
        Matrix4x4[] m = BoneWorld(sp);
        for (int i = 0; i < m.Length; i++) m[i] = OctoMath.InverseAffine(m[i]);
        return m;
    }
}
