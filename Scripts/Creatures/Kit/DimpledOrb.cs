// Creature kit (2026-10-20, the GolfballStrider): DimpledOrb - a big round body (an abdomen, a puffball, a seed head)
// on a bone of its own, covered in golf-ball pits:
//   shape   an ellipsoid (radius, squash = height, stretch = length) centred at.offset in its joint's frame
//   pits    pitCount centres on a spherical Fibonacci lattice (evenly spread, no seams), each a smooth bowl
//           (depth pitDepth, radius pitRadius x the lattice spacing) with ANALYTIC normals - the sphere's resolution
//           (detail: icosphere subdivisions) only has to carry the silhouette and the bowls' shading
//   shade   the pits are darkened through the dorsal channel (pitShade): their floors take the belly/flank tone and
//           the ambient's belly occlusion - a baked cavity, no shader edit
//   motion  the orb rocks about `pivot` on a damped spring driven by the pivot's acceleration (the heavy orb lags
//           when the body bobs, starts, stops and turns): DimpledOrbRuntime, stage AfterLimbs; bounded by maxRockDeg
// Material layer `layer` (default 0, the body skin: restyle it with Layers - white, sheen for fuzz, no grain/pattern).
// Kit-only part, off unless a species names it; MeshOrder 100 (after every shipped vertex).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("DimpledOrb", PartKind.Rigid, Bones = "1", Summary = "A big round body on a bone of its own covered in golf-ball pits (spherical Fibonacci lattice, analytic pit normals, pits darkened through the dorsal channel); it rocks on a damped spring about a pivot when the body accelerates (bobs, starts, stops, turns).",
          System = "DimpledOrbRuntime (part runtime, stage AfterLimbs: its bone) + DimpledOrb.EmitMesh", MaterialLayers = new[] { 0 }, KitOnly = true,
          UseWhen = "a round abdomen (spiders, ticks), puffballs, seed heads, golf-ball or pitted shells; any heavy round mass that should wobble on its body")]
    public sealed class DimpledOrb : CreaturePart
    {
        [Param(ParamUnit.Metres, "radius", Min = 0.005f, Max = 3f, Example = "golfball strider 0.16")] public float radius = 0.15f;
        [Param(ParamUnit.Fraction, "height x the radius (1 = round)", Min = 0.3f, Max = 2f, Default = "1")] public float squash = 1f;
        [Param(ParamUnit.Fraction, "length x the radius (1 = round)", Min = 0.3f, Max = 2.5f, Default = "1")] public float stretch = 1f;
        [Param(ParamUnit.Count, "pits over the whole surface (0 = smooth)", Min = 0, Max = 3000, Example = "golf ball 336")] public int pitCount = 300;
        [Param(ParamUnit.Fraction, "pit radius x the lattice spacing (0.5 = pits touching)", Min = 0.1f, Max = 0.6f, Default = "0.42")] public float pitRadius = 0.42f;
        [Param(ParamUnit.Metres, "pit depth", Example = "golfball strider 0.005")] public float pitDepth = 0.004f;
        [Param(ParamUnit.Fraction, "how much darker a pit's floor is (dorsal channel drop)", Min = 0f, Max = 1f, Default = "0.5")] public float pitShade = 0.5f;
        [Param(ParamUnit.Count, "mesh: icosphere subdivisions (4 = 2562 vertices, 5 = 10242)", Min = 2, Max = 6, Default = "5")] public int detail = 5;
        [Param(ParamUnit.Metres, "the point it rocks about, in the joint's frame (where it sits on the body)")] public Vector3 pivot;
        [Param(ParamUnit.Hz, "rocking spring frequency", Min = 0.2f, Max = 20f, Default = "2")] public float rockHz = 2f;
        [Param(ParamUnit.Fraction, "rocking damping ratio", Min = 0f, Max = 2f, Default = "0.3")] public float rockDamping = 0.3f;
        [Param(ParamUnit.Fraction, "how strongly the pivot's acceleration rocks it (1 = a free pendulum's)", Min = 0f, Max = 3f, Default = "1")] public float rockGain = 1f;
        [Param(ParamUnit.Degrees, "largest rock", Min = 0f, Max = 45f, Default = "12")] public float maxRockDeg = 12f;
        [Param(ParamUnit.Count, "material layer (part id)", Min = 0, Max = 15, Default = "0")] public int layer;

        public override int BoneCount(in SpeciesCtx c) => 1;
        public override int MeshOrder => 100;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a DimpledOrb rides a SpineBody joint";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (!(radius > 0f)) return $"{id}: radius must be > 0";
            if (!(squash > 0f) || !(stretch > 0f)) return $"{id}: squash and stretch must be > 0";
            if (pitCount < 0 || pitCount > 3000) return $"{id}: pitCount must be in 0..3000";
            if (pitRadius < 0.1f || pitRadius > 0.6f) return $"{id}: pitRadius must be in 0.1..0.6";
            if (pitDepth < 0f || pitDepth > 0.1f * radius) return $"{id}: pitDepth must be in 0..0.1 x radius";
            if (detail < 2 || detail > 6) return $"{id}: detail must be in 2..6";
            if (!(rockHz > 0f) || rockDamping < 0f || rockGain < 0f || maxRockDeg < 0f) return $"{id}: rockHz > 0; rockDamping, rockGain, maxRockDeg >= 0";
            if (layer < 0 || layer >= PartLayers.MaxParts) return $"{id}: layer must be in 0..{PartLayers.MaxParts - 1}";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c) => c.Sim != null ? new DimpledOrbRuntime(this, c.Sim, c.Species.Layout.Start(c.Index)) : null;

        // ------------------------------------------------------------ the pit lattice

        const float kGolden = 2.39996323f;   // the golden angle (rad)

        // n evenly spread unit vectors (spherical Fibonacci lattice).
        public static Vector3[] PitCentres(int n)
        {
            var c = new Vector3[Mathf.Max(0, n)];
            for (int j = 0; j < c.Length; j++)
            {
                float y = 1f - 2f * (j + 0.5f) / n, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), phi = j * kGolden;
                c[j] = new Vector3(r * Mathf.Cos(phi), y, r * Mathf.Sin(phi));
            }
            return c;
        }

        // The mean chord between neighbouring centres of n on the unit sphere (hexagonal packing of 4 pi).
        public static float Spacing(int n) => n > 0 ? Mathf.Sqrt(8f * Mathf.PI / (Mathf.Sqrt(3f) * n)) : 2f;

        // The plain surface's dorsal value at unit height y (-1 under .. +1 on top), before the pits darken it.
        public static float DorsalOf(float y) => Mathf.Clamp(1.1f * y + 0.15f, -1f, 1f);

        // ------------------------------------------------------------ the mesh

        public override void EmitMesh(PartMeshEmitter m, in PartCtx c)
        {
            if (m.Rest == null) return;
            int bone = m.Species.Layout.Start(c.Index);
            int j = at.joint;
            Vector3 J = m.Rest.Joints[j];
            Quaternion R = m.Rest.Rotations[j];
            Vector3 centre = J + R * at.offset;
            Vector4 skin = PartMeshEmitter.Rigid(bone);

            Sphere(Mathf.Clamp(detail, 2, 6), out List<Vector3> units, out List<int> tris);
            Vector3[] pits = PitCentres(pitCount);
            float spacing = Spacing(pitCount), rho = pitRadius * spacing;
            var grid = new PitGrid(pits, Mathf.Max(rho, 1e-3f));
            int first = m.VertexCount;
            for (int i = 0; i < units.Count; i++)
            {
                Vector3 u = units[i];
                float h = 0f; Vector3 tilt = Vector3.zero; float pitFrac = 0f;
                if (pitCount > 0 && pitDepth > 0f)
                {
                    int k = grid.Nearest(u, out float delta);
                    if (k >= 0 && delta < rho)
                    {
                        float x = delta / rho, e = 1f - x * x;
                        h = -pitDepth * e * e;
                        pitFrac = e * e;
                        float dhds = 4f * pitDepth * x * e / rho / radius;   // d(height) / d(surface distance)
                        Vector3 away = u - pits[k]; away -= u * Vector3.Dot(away, u);
                        if (away.sqrMagnitude > 1e-14f) tilt = away.normalized * dhds;
                    }
                }
                Vector3 n0 = (u - tilt).normalized;
                float rr = radius + h;
                Vector3 local = new Vector3(u.x * rr, u.y * rr * squash, u.z * rr * stretch);
                Vector3 nl = PartMeshEmitter.Unit(new Vector3(n0.x, n0.y / squash, n0.z / stretch), u);
                Vector3 p = centre + R * local, nw = R * nl;
                float dorsal = DorsalOf(u.y) - 1.4f * pitShade * pitFrac;
                float arc = radius * Mathf.Acos(Mathf.Clamp(u.z, -1f, 1f));
                float ang = Mathf.Atan2(u.y, u.x) / (2f * Mathf.PI); if (ang < 0f) ang += 1f;
                m.Vertex(p, nw, arc, ang, layer, dorsal, skin);
            }
            for (int t = 0; t < tris.Count; t += 3) m.Tri(first + tris[t], first + tris[t + 1], first + tris[t + 2], false);
        }

        // The unit icosphere of a level, built once per process (main thread; read-only after).
        static readonly Dictionary<int, (List<Vector3>, List<int>)> spheres = new Dictionary<int, (List<Vector3>, List<int>)>();
        static void Sphere(int level, out List<Vector3> v, out List<int> t)
        {
            if (!spheres.TryGetValue(level, out var s)) { Icosphere(level, out List<Vector3> a, out List<int> b); s = (a, b); spheres[level] = s; }
            v = s.Item1; t = s.Item2;
        }

        // A unit icosphere: `level` subdivisions of an icosahedron, wound like every kit mesh (cross(b - a, c - a) along
        // the outward normal).
        internal static void Icosphere(int level, out List<Vector3> v, out List<int> t)
        {
            float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
            v = new List<Vector3>
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            t = new List<int> { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            for (int l = 0; l < level; l++)
            {
                var mid = new Dictionary<long, int>();
                var nt = new List<int>(t.Count * 4);
                List<Vector3> vv = v;
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int r)) return r;
                    vv.Add((vv[a] + vv[b]).normalized);
                    mid[key] = vv.Count - 1;
                    return vv.Count - 1;
                }
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                t = nt;
            }
            Vector3 p0 = v[t[0]], p1 = v[t[1]], p2 = v[t[2]];
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), p0) < 0f)
                for (int i = 0; i < t.Count; i += 3) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
        }

        // Nearest pit centre by a uniform 3D grid over the unit cube (cells >= the pit radius: 27 cells hold it).
        sealed class PitGrid
        {
            readonly Vector3[] c; readonly int n; readonly float cell; readonly int[] start, items;
            public PitGrid(Vector3[] centres, float rho)
            {
                c = centres; cell = Mathf.Max(rho, 2f / 64f); n = Mathf.CeilToInt(2f / cell) + 1;
                var count = new int[n * n * n + 1];
                foreach (Vector3 p in c) count[Cell(p) + 1]++;
                for (int i = 1; i < count.Length; i++) count[i] += count[i - 1];
                start = (int[])count.Clone();
                items = new int[c.Length];
                var fill = (int[])count.Clone();
                for (int i = 0; i < c.Length; i++) items[fill[Cell(c[i])]++] = i;
            }
            int Axis(float x) => Mathf.Clamp((int)((x + 1f) / cell), 0, n - 1);
            int Cell(Vector3 p) => (Axis(p.x) * n + Axis(p.y)) * n + Axis(p.z);
            public int Nearest(Vector3 u, out float dist)
            {
                int best = -1; float bd = float.MaxValue;
                int ax = Axis(u.x), ay = Axis(u.y), az = Axis(u.z);
                for (int x = Mathf.Max(0, ax - 1); x <= Mathf.Min(n - 1, ax + 1); x++)
                    for (int y = Mathf.Max(0, ay - 1); y <= Mathf.Min(n - 1, ay + 1); y++)
                        for (int z = Mathf.Max(0, az - 1); z <= Mathf.Min(n - 1, az + 1); z++)
                        {
                            int k = (x * n + y) * n + z;
                            for (int i = start[k]; i < start[k + 1]; i++)
                            {
                                float d = (c[items[i]] - u).sqrMagnitude;
                                if (d < bd) { bd = d; best = items[i]; }
                            }
                        }
                dist = best >= 0 ? Mathf.Sqrt(bd) : float.MaxValue;
                return best;
            }
        }
    }

    // One orb of one individual: its bone at the pivot, rocked by a small-angle rotation vector on a damped spring
    // driven by the pivot's acceleration acting on the orb's centre of mass. Zero GC; main thread.
    public sealed class DimpledOrbRuntime : IPartRuntime
    {
        const float kMaxAccel = 60f;
        readonly DimpledOrb p;
        readonly CreatureSim sim;
        readonly int bone;
        readonly Vector3 arm;      // pivot -> centre, joint frame, scale 1
        Vector3 theta, omegaV;     // rotation vector (rad, joint frame) and its rate
        Vector3 last, vel;
        int primed;                // 0: no position yet, 1: position, 2: velocity

        public DimpledOrb Part => p;
        public int Bone => bone;
        public float RockDeg => theta.magnitude * Mathf.Rad2Deg;

        public DimpledOrbRuntime(DimpledOrb part, CreatureSim sim, int bone)
        {
            p = part; this.sim = sim; this.bone = bone;
            arm = part.at.offset - part.pivot;
        }

        public PartStage Stage => PartStage.AfterLimbs;

        Vector3 Pivot => sim.Joints[p.at.joint] + sim.Rotations[p.at.joint] * (p.pivot * sim.SizeScale);

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            Vector3 x = Pivot;
            Vector3 a = Vector3.zero;
            if (primed >= 1)
            {
                Vector3 v = (x - last) / dt;
                if (primed >= 2) a = (v - vel) / dt;
                vel = v;
                primed = Mathf.Min(2, primed + 1);
            }
            else primed = 1;
            last = x;
            float am = a.magnitude;
            if (am > kMaxAccel) a *= kMaxAccel / am;
            float s = sim.SizeScale, ts = Mathf.Sqrt(Mathf.Max(1e-4f, s));
            Quaternion inv = Quaternion.Inverse(sim.Rotations[p.at.joint]);
            Vector3 al = inv * a;
            Vector3 r = arm * s;
            float r2 = Mathf.Max(1e-8f, r.sqrMagnitude);
            Vector3 drive = p.rockGain * Vector3.Cross(r, -al) / r2;   // the inertial force on the centre, as an angular acceleration
            float w = 2f * Mathf.PI * p.rockHz / ts, z = p.rockDamping, lim = p.maxRockDeg * Mathf.Deg2Rad;
            int sub = Mathf.Clamp(Mathf.CeilToInt(w * dt / 0.4f), 1, 8);
            float h = dt / sub;
            for (int i = 0; i < sub; i++)
            {
                omegaV += (-w * w * theta - 2f * z * w * omegaV + drive) * h;
                theta += omegaV * h;
            }
            float m = theta.magnitude;
            if (m > lim) { theta *= lim / m; omegaV -= Vector3.Project(omegaV, theta); }
        }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            float s = sim.SizeScale, m = theta.magnitude;
            Quaternion rot = sim.Rotations[p.at.joint];
            if (m > 1e-9f) rot = rot * Quaternion.AngleAxis(m * Mathf.Rad2Deg, theta / m);
            dst[offset + bone] = Matrix4x4.TRS(Pivot, rot, new Vector3(s, s, s));
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            float s = sim.SizeScale, r = (p.radius * Mathf.Max(1f, Mathf.Max(p.squash, p.stretch)) + arm.magnitude) * s;
            Vector3 c = Pivot;
            lo = Vector3.Min(lo, c - new Vector3(r, r, r)); hi = Vector3.Max(hi, c + new Vector3(r, r, r));
        }

        public void DrawDebug(ICreatureDebugDraw d) { }
    }
}
