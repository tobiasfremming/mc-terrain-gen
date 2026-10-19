// Creature kit T3.1 (design §2.3, §3e): the species mesh, emitted part by part. One emitter per mesh build holds
// the rest pose (CreatureRestPose: a fresh sim on flat ground; a winged species with its wings spread) and the
// vertex streams, and offers the primitives every part builds from (rings, poles, stitching, ellipsoids, tubes,
// horns, the rest spine's sections). Each part draws itself in CreaturePart.EmitMesh (Kit/PartMeshes.cs); the
// emitter calls them in the shipped order (CreaturePart.MeshOrder: body, limbs, eyes, cones, fins, antennae,
// wings, then new part types) so every existing species keeps its mesh byte for byte
// (CreatureMeshEmitterTests.Mesh_Bytes_EqualLegacy against the frozen pre-kit builders).
// Vertex layout (CreatureMeshBuilder's): uv0 = (arc length along the part m, angle / 2pi), uv1 = (part id, dorsal
// -1 belly .. +1 ridge), uv2 = (bone0, bone1, w0, w1) with w0 + w1 = 1, bone0 the heavier.
// Runs once per species (not per frame): it may allocate.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CreatureKit
{
    // Size of an individual (design §2.3): lengths x L, times x T (Froude: T = sqrt L).
    public readonly struct SizeCtx
    {
        public readonly float L, T;
        public SizeCtx(float sizeScale) { L = sizeScale; T = Mathf.Sqrt(Mathf.Max(1e-4f, sizeScale)); }
        public static SizeCtx One => new SizeCtx(1f);
    }

    // What a part gets when it emits its mesh or creates its runtime: the runtime species (the fields the systems
    // read), the part's place in the flat part list, its ordinal among the runtime entries of its family (leg l
    // of sp.legs, wing w, cone c of sp.appendages, fin f, antenna a), the individual's size and its sim (mesh
    // emission: the rest pose's sim at size 1).
    public readonly struct PartCtx
    {
        public readonly CreatureSpecies Species;
        public readonly int Index, Ordinal;
        public readonly SizeCtx Size;
        public readonly CreatureSim Sim;
        public PartCtx(CreatureSpecies species, int index, int ordinal, SizeCtx size, CreatureSim sim = null)
        { Species = species; Index = index; Ordinal = ordinal; Size = size; Sim = sim; }
    }

    public sealed class PartMeshEmitter
    {
        // Part ids (the shader's registry, creature-module rule 9): 4-7 belong to the octopus.
        public const int PartBody = 0, PartLeg = 1, PartFoot = 2, PartEye = 3, PartMembrane = 8, PartBand = 9,
            PartPlate = 10, PartPincer = 11;   // kit: ArmourPlates, Pincer
        public const float MinRadius = 1e-4f;
        public const float DiffStep = 1e-4f;

        public readonly CreatureSpecies Species;     // the runtime view
        public readonly CreatureSim Rest;            // the rest pose's sim (spine, legs, antennae)
        public readonly Matrix4x4[] Bones;           // rest bone worlds (a winged species: wings spread)
        public readonly WingSpec[] WingSpecs;        // the wings drawn (empty: none)
        public readonly CreatureWings Wings;         // their spread rest pose; null without wings
        public readonly CreatureMeshLists Lists = new CreatureMeshLists();
        public readonly int Joints;

        readonly List<Vector3> pos, nrm;
        readonly List<Vector2> uv0, uv1;
        readonly List<Vector4> uv2;
        readonly List<int> tris;

        PartMeshEmitter(CreatureSpecies sp, WingSpec[] wings)
        {
            Species = sp;
            WingSpecs = wings ?? new WingSpec[0];
            Rest = CreatureRestPose.Sim(sp);
            Joints = sp.JointCount;
            if (WingSpecs.Length > 0)
            {
                // a tall stance's bind keeps a straight spine (WingedMeshBuilder.StraightenBind); no-op otherwise
                WingedMeshBuilder.StraightenBind(sp, WingSpecs, Rest);
                Bones = WingedMeshBuilder.RestBoneWorld(sp, WingSpecs, Rest, out Wings);
            }
            else Bones = CreatureRestPose.BoneWorld(Rest);
            pos = Lists.pos; nrm = Lists.nrm; uv0 = Lists.uv0; uv1 = Lists.uv1; uv2 = Lists.uv2; tris = Lists.tris;
        }

        // ---------------------------------------------------------------- the build

        // RED test hook (CreatureMeshEmitterRedTests): a different emit order must change the bytes.
        internal static bool ReverseOrderForTest;

        // The species' mesh in its rest pose. wings: the wings to draw (null = the species' own).
        public static Mesh Build(CreatureSpecies sp, WingSpec[] wings = null)
        {
            sp = sp.Compiled.Runtime;   // the runtime view (a legacy asset is its own)
            var m = new PartMeshEmitter(sp, wings ?? sp.wings);
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            int[] ordinal = KitView.Ordinals(parts);
            var order = new List<int>(parts.Count);
            for (int i = 0; i < parts.Count; i++) order.Add(i);
            // stable by MeshOrder (insertion sort keeps part order within an order class)
            for (int i = 1; i < order.Count; i++)
                for (int j = i; j > 0 && parts[order[j]].MeshOrder < parts[order[j - 1]].MeshOrder; j--) { int t = order[j]; order[j] = order[j - 1]; order[j - 1] = t; }
            if (ReverseOrderForTest) order.Reverse();
            foreach (int i in order) parts[i].EmitMesh(m, new PartCtx(sp, i, ordinal[i], SizeCtx.One, m.Rest));
            return m.ToMesh(m.WingSpecs.Length > 0 ? " winged creature" : " creature");
        }

        Mesh ToMesh(string suffix)
        {
            var mesh = new Mesh { name = (Species != null ? Species.name : "") + suffix };
            if (pos.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(tris, 0, true);
            return mesh;
        }

        // ---------------------------------------------------------------- queries

        public int VertexCount => pos.Count;
        public Vector3 Position(int i) => pos[i];

        // Leg l carries a wing (its Wing part draws the arm).
        public bool IsWingArm(int leg)
        {
            for (int w = 0; w < WingSpecs.Length; w++) if (WingSpecs[w].leg == leg) return true;
            return false;
        }

        // ---------------------------------------------------------------- primitives

        public void Vertex(Vector3 p, Vector3 normal, float u, float v, int part, float dorsal, Vector4 skin)
        {
            pos.Add(p); nrm.Add(normal);
            uv0.Add(new Vector2(u, v)); uv1.Add(new Vector2(part, Mathf.Clamp(dorsal, -1f, 1f))); uv2.Add(skin);
        }

        public static Vector4 Rigid(int bone) => new Vector4(bone, bone, 1f, 0f);

        // A vertex normal from an unnormalised direction (a cross product of mesh tangents). Never .normalized:
        // Unity returns ZERO below a magnitude of 1e-5, and the tangents of small parts (mm-sized fins, finite
        // differences over 1e-3 of a section) cross to ~1e-6 - a zero normal is a NaN pixel in Creature.shader,
        // which bloom spreads into a white blow-out (the glowing heads and tails, CreatureMeshNormalTests).
        // Bit-identical to .normalized wherever that returned a unit vector.
        public static Vector3 Unit(Vector3 v, Vector3 fallback)
        {
            float len = v.magnitude;
            return len > 1e-12f && !float.IsInfinity(len) ? v / len : fallback;
        }
        // bone0 is always the heavier bone (the nearest rigid frame of the vertex)
        public static Vector4 Blend(int b0, int b1, float w1) => w1 > 0.5f ? new Vector4(b1, b0, w1, 1f - w1) : new Vector4(b0, b1, 1f - w1, w1);

        // One elliptical ring of m + 1 vertices (seam duplicated), starting at the belly (phi = -pi/2):
        //   p = c + x a cos(phi) + y (d + b sin(phi))
        // The normal is the ellipse normal (x cos/a + y sin/b) tilted along t by the taper slopes:
        //   da, db = d(semi-axis)/d(distance along t), dd = d(centre offset along y)/d(distance along t).
        public int Ring(Vector3 c, Vector3 x, Vector3 y, Vector3 t, float a, float b, float d,
                        float da, float db, float dd, float u, int part, Vector4 skin, int m, bool dorsalFromAngle)
        {
            int first = pos.Count;
            a = Mathf.Max(a, MinRadius); b = Mathf.Max(b, MinRadius);
            for (int k = 0; k <= m; k++)
            {
                float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * k / m;
                float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                if (k == m) { cs = 0f; sn = -1f; } // exact seam duplicate
                Vector3 p = c + x * (a * cs) + y * (d + b * sn);
                float tilt = -(cs * cs * da / a + sn * sn * db / b) - sn * dd / b;
                Vector3 nn = (x * (cs / a) + y * (sn / b) + t * tilt).normalized;
                Vertex(p, nn, u, (float)k / m, part, dorsalFromAngle ? sn : nn.y, skin);
            }
            return first;
        }

        // A closed end: m + 1 copies of the tip (angle uv at the middle of each quad).
        public int Pole(Vector3 p, Vector3 normal, float u, int part, float dorsal, Vector4 skin, int m)
        {
            int first = pos.Count;
            for (int k = 0; k <= m; k++) Vertex(p, normal, u, (k + 0.5f) / m, part, dorsal, skin);
            return first;
        }

        // Quads between ring A and the next ring B. Outward when cross(ring tangent at phi=0, A->B)
        // points along x (ring basis x = cross(y, A->B)); flip otherwise.
        public void Stitch(int a, int b, int m, bool poleA, bool poleB, bool flip)
        {
            for (int k = 0; k < m; k++)
            {
                if (!poleA) Tri(a + k, a + k + 1, b + k, flip);
                if (!poleB) Tri(poleA ? a + k : a + k + 1, b + k + 1, b + k, flip);
            }
        }

        public void Tri(int i0, int i1, int i2, bool flip)
        {
            tris.Add(i0);
            if (flip) { tris.Add(i2); tris.Add(i1); } else { tris.Add(i1); tris.Add(i2); }
        }

        // Ellipsoid about local +Z (poles at -az and +az) in the rigid frame m, rigid to one bone.
        public void Ellipsoid(Matrix4x4 frame, Vector3 centre, float ax, float ay, float az, int around, int bands, int part, int bone)
        {
            Vector4 skin = Rigid(bone);
            int prev = -1;
            for (int j = 0; j <= bands; j++)
            {
                float beta = -0.5f * Mathf.PI + Mathf.PI * j / bands;
                float u = az * (beta + 0.5f * Mathf.PI);
                int cur;
                if (j == 0 || j == bands)
                {
                    float z = j == 0 ? -1f : 1f;
                    Vector3 nn = frame.MultiplyVector(new Vector3(0f, 0f, z)).normalized;
                    cur = Pole(frame.MultiplyPoint3x4(centre + new Vector3(0f, 0f, az * z)), nn, u, part, nn.y, skin, around);
                }
                else
                {
                    cur = pos.Count;
                    float cb = Mathf.Cos(beta), sb = Mathf.Sin(beta);
                    for (int k = 0; k <= around; k++)
                    {
                        float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * k / around;
                        float cs = k == around ? 0f : Mathf.Cos(phi), sn = k == around ? -1f : Mathf.Sin(phi);
                        Vector3 lp = centre + new Vector3(ax * cb * cs, ay * cb * sn, az * sb);
                        Vector3 nn = frame.MultiplyVector(new Vector3(cb * cs / ax, cb * sn / ay, sb / az)).normalized;
                        Vertex(frame.MultiplyPoint3x4(lp), nn, u, (float)k / around, part, nn.y, skin);
                    }
                }
                if (prev >= 0) Stitch(prev, cur, around, j == 1, j == bands, false);
                prev = cur;
            }
        }

        // A sample of a round tube (Tube): centre, unit tangent, radius, d(radius)/d(arc), arc (uv0.x), skin, a pole.
        public struct TubeSample { public Vector3 c, t; public float r, dr, s; public Vector4 skin; public bool pole; }

        public static TubeSample Sample(Vector3 c, Vector3 t, float r, float dr, float s, Vector4 skin, bool pole = false) =>
            new TubeSample { c = c, t = t, r = r, dr = dr, s = s, skin = skin, pole = pole };

        // A tube through the samples: circular rings (from angle 0, unlike Ring's belly start) in a
        // rotation-minimising frame started from x0, poles at the flagged ends. Outward winding (ring basis x, y = t x x).
        public void Tube(List<TubeSample> q, Vector3 x0, int m, int part)
        {
            Vector3 x = x0;
            int prev = -1; bool prevPole = false;
            for (int i = 0; i < q.Count; i++)
            {
                TubeSample a = q[i];
                Vector3 xt = x - a.t * Vector3.Dot(x, a.t);
                x = xt.sqrMagnitude > 1e-10f ? xt.normalized : x;
                Vector3 y = Vector3.Cross(a.t, x);
                int cur = pos.Count;
                if (a.pole)
                {
                    Vector3 pn = i == 0 ? -a.t : a.t;
                    for (int k = 0; k <= m; k++) Vertex(a.c, pn, a.s, (k + 0.5f) / m, part, pn.y, a.skin);
                }
                else
                    for (int k = 0; k <= m; k++)
                    {
                        float phi = 2f * Mathf.PI * k / m;
                        float cs = k == m ? 1f : Mathf.Cos(phi), sn = k == m ? 0f : Mathf.Sin(phi);
                        Vector3 radial = x * cs + y * sn;
                        Vector3 nn = (radial - a.t * a.dr).normalized;
                        Vertex(a.c + radial * Mathf.Max(1e-4f, a.r), nn, a.s, (float)k / m, part, nn.y, a.skin);
                    }
                if (prev >= 0)
                    for (int k = 0; k < m; k++)
                    {
                        if (!prevPole) Tri(prev + k, prev + k + 1, cur + k, false);
                        if (!a.pole) Tri(prevPole ? prev + k : prev + k + 1, cur + k + 1, cur + k, false);
                    }
                prev = cur; prevPole = a.pole;
            }
        }

        // A grid of (rows + 1) x (cols + 1) vertices from `first`, row-major; front faces along sn; row 0 a pole
        // (one triangle per quad).
        public void Grid(int first, Vector3 sn, int rows, int cols)
        {
            int c = cols + 1;
            Vector3 p00 = pos[first + c], p01 = pos[first + c + 1], p10 = pos[first + 2 * c];
            bool flip = Vector3.Dot(Vector3.Cross(p01 - p00, p10 - p00), sn) < 0f;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int a = first + j * c + i, b = a + 1, d = a + c, e = d + 1;
                    if (j > 0) Tri(a, b, d, flip);
                    Tri(b, e, d, flip);
                }
        }

        // A curved tapered cone rigid to one bone (claw, horn, chelicera, fang): ArthropodLegMesh.Horn.
        public void Horn(Vector3 basePt, Quaternion q, float r0, float r1, float len, float curveDeg, int bone, int part, int sides, float arc,
                         out Vector3 tip, out Vector3 tipDir)
            => ArthropodLegMesh.Horn(Lists, basePt, q, r0, r1, len, curveDeg, bone, part, sides, arc, out tip, out tipDir);

        // ---------------------------------------------------------------- the rest spine

        // Joint i of the rest spine, extrapolated one segment past either end.
        public Vector3 J(int i)
        {
            Vector3[] p = Rest.Joints;
            int n = Joints;
            if (i < 0) return 2f * p[0] - p[1];
            if (i > n - 1) return 2f * p[n - 1] - p[n - 2];
            return p[i];
        }

        public static float CR(float p0, float p1, float p2, float p3, float t) =>
            0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);
        public static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) =>
            0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (3f * p1 - p0 - 3f * p2 + p3) * (t * t * t));
        public static Vector3 CRd(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) =>
            0.5f * ((p2 - p0) + 2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t + 3f * (3f * p1 - p0 - 3f * p2 + p3) * (t * t));

        public float HW(int i) => Species.joints[Mathf.Clamp(i, 0, Joints - 1)].halfWidth;
        public float HH(int i) => Species.joints[Mathf.Clamp(i, 0, Joints - 1)].halfHeight;

        // The rest spine's cross-section at u in joint units (0 = head joint .. n-1 = last joint); t points to the
        // head (Catmull-Rom through the joints, frames slerped, semi-axes Catmull-Rom through the joint profile).
        public SpineSection SpineAt(float u)
        {
            int n = Joints;
            u = Mathf.Clamp(u, 0f, n - 1);
            int i = Mathf.Min((int)u, n - 2);
            float f = u - i;
            Vector3 p0 = J(i - 1), p1 = J(i), p2 = J(i + 1), p3 = J(i + 2);
            Vector3 tan = -CRd(p0, p1, p2, p3, f);
            Quaternion r0 = Rest.Rotations[i], r1 = Rest.Rotations[i + 1];
            Vector3 t = tan.sqrMagnitude > 1e-12f ? tan.normalized : Quaternion.Slerp(r0, r1, f) * Vector3.forward;
            Vector3 up = Vector3.Slerp(r0 * Vector3.up, r1 * Vector3.up, f);
            Vector3 y = Vector3.ProjectOnPlane(up, t);
            y = y.sqrMagnitude > 1e-10f ? y.normalized : Vector3.ProjectOnPlane(Vector3.up, t).normalized;
            return new SpineSection
            {
                c = CR(p0, p1, p2, p3, f), t = t, y = y, x = Vector3.Cross(y, t),
                a = Mathf.Max(MinRadius, CR(HW(i - 1), HW(i), HW(i + 1), HW(i + 2), f)),
                b = Mathf.Max(MinRadius, CR(HH(i - 1), HH(i), HH(i + 1), HH(i + 2), f)),
            };
        }
    }

    // The flat parts that go with a runtime species' fields. Every runtime entry point starts from
    // sp.Compiled.Runtime; its parts are the compiled ones (an authored kit asset: the composed parts; a legacy
    // asset: the adapter's description of its fields). Code-built species whose description did not compose fall
    // back to the adapter's raw description, so emission and part runtimes never depend on compose succeeding.
    public static class KitView
    {
        public static IReadOnlyList<CreaturePart> Parts(CreatureSpecies runtime)
        {
            CompiledSpecies c = runtime.Compiled;
            if (Aligned(c, runtime)) return c.Parts;
            return LegacySpeciesAdapter.ToParts(runtime, false, out _);
        }

        public static IReadOnlyList<CreatureModule> Modules(CreatureSpecies runtime)
        {
            CompiledSpecies c = runtime.Compiled;
            if (Aligned(c, runtime)) return c.Modules;
            LegacySpeciesAdapter.ToParts(runtime, false, out List<CreatureModule> modules);
            return modules;
        }

        // The compiled parts describe exactly the runtime arrays (same counts per family, one body, one Eyes,
        // flight iff the species can fly).
        static bool Aligned(CompiledSpecies c, CreatureSpecies r)
        {
            if (c == null || c.Body == null) return false;
            bool soft = r.bodyPlan == CreatureBodyPlan.Cephalopod;
            if (c.Soft != soft) return false;
            if (soft) return true;
            int limbs = 0, wings = 0, eyes = 0, cones = 0, fins = 0, antennae = 0;
            foreach (CreaturePart p in c.Parts)
                switch (p)
                {
                    case LimbPart _: limbs++; break;
                    case Wing _: wings++; break;
                    case Eyes _: eyes++; break;
                    case RigidCone rc: if (!rc.ownBone) cones++; break;
                    case FinStrip _: fins++; break;
                    case Antenna _: antennae++; break;
                }
            bool fly = false;
            foreach (CreatureModule m in c.Modules) if (m is Fly) fly = true;
            return limbs == (r.legs != null ? r.legs.Length : 0) && wings == r.WingCount && eyes == 1
                && cones == (r.appendages != null ? r.appendages.Length : 0) && fins == (r.fins != null ? r.fins.Length : 0)
                && antennae == r.AntennaCount && (!r.CanFly || fly);
        }

        // Each part's ordinal among the runtime entries of its family (leg l, wing w, cone c, fin f, antenna a; the
        // body and eyes 0; a part of a new type: its place among the parts of its type).
        public static int[] Ordinals(IReadOnlyList<CreaturePart> parts)
        {
            var ord = new int[parts.Count];
            var count = new Dictionary<Type, int>();
            for (int i = 0; i < parts.Count; i++)
            {
                CreaturePart p = parts[i];
                if (p is RigidCone rc && rc.ownBone) { ord[i] = -1; continue; }
                Type family = p is LimbPart ? typeof(LimbPart) : p.GetType();
                count.TryGetValue(family, out int k);
                ord[i] = k;
                count[family] = k + 1;
            }
            return ord;
        }
    }
}
