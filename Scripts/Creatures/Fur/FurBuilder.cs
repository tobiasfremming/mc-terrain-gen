// Creature fur, CPU side (pure C#: runs in the offline harness). Once per species, from the rest mesh the renderer
// draws (KitRender: PartMeshEmitter's vertex layout) and its bind poses:
//   FurSkinVertex   every mesh vertex a root or a shell uses, with its skin (bones, weights), its pattern
//                   coordinates (uv0, uv1, rest radius) - the GPU skins these exactly as CreatureSkin.hlsl does
//   FurRoot         a strand's root: a triangle (three skin vertices) and barycentrics, length, width, its rest comb
//                   and tip directions, its region; shuffled so any prefix is a uniform subset (distance LOD)
//   FurRegionRow    per region: colours (linear), dynamics (spring Hz, damping, droop)
//   shell streams   the furry triangles again, with the undercoat's length, hair size and colours per vertex
// Deterministic: every random number is a hash of (seed, region, triangle, sample); no Random, no Dictionary order.
// Allocates (construction only).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace CreatureKit
{
    // 64 bytes; HLSL mirror FurSkinVertex in Resources/CreatureFur/FurCommon.hlsl
    [StructLayout(LayoutKind.Sequential)]
    public struct FurSkinVertex
    {
        public Vector3 pos;      // rest (mesh) space
        public Vector3 nrm;
        public Vector4 skin;     // uv2: bone0, bone1, w0, w1
        public Vector2 uv0;      // arc length m, angle / 2pi
        public Vector2 uv1;      // part id, dorsal
        public float radius;     // rest distance from the blended bone axis (CreatureSkin.hlsl's pattern radius)
        public float pad;
    }

    // 64 bytes; HLSL mirror FurRoot
    [StructLayout(LayoutKind.Sequential)]
    public struct FurRoot
    {
        public uint v0, v1, v2;  // FurData.Verts indices of the triangle
        public float b1, b2;     // barycentrics of v1, v2 (v0: 1 - b1 - b2)
        public float length;     // m at scale 1
        public float width;      // m at scale 1 (the lock's width at the root)
        public uint region;
        public Vector3 comb;     // rest-space direction of the first segment (unit, off the skin by the lift)
        public uint seed;
        public Vector3 tipDir;   // rest-space direction of the second segment (curled, gathered into its lock)
        public float rank;       // its place in the shuffled order, 0..1 (distance LOD keeps rank < fraction)
    }

    // 48 bytes; HLSL mirror FurRegionRow
    [StructLayout(LayoutKind.Sequential)]
    public struct FurRegionRow
    {
        public Vector4 root;     // linear rgb, bodyTint
        public Vector4 tip;      // linear rgb, 0
        public Vector4 dyn;      // stiffness Hz (scale 1), damping ratio, droop (x g), kind
    }

    public sealed class FurData
    {
        public FurSkinVertex[] Verts;
        public int[] SourceVertex;          // Verts[i] is mesh vertex SourceVertex[i]
        public FurRoot[] Roots;
        public int[] RootTriangle;          // the mesh triangle (index / 3) each root lies on
        public FurRegionRow[] Regions;
        public float MaxLength;             // the longest hair (strands or undercoat), rest metres
        public float MaxStrandLength;

        // the undercoat's shell mesh: the furry triangles, their vertices with the body's streams plus
        //   uv3 = (shell height m, hair radius m, hairs per metre, droop), uv4 = (root rgb linear, bodyTint), uv5 = (tip rgb linear, 0)
        public readonly List<Vector3> ShellPos = new List<Vector3>(), ShellNrm = new List<Vector3>();
        public readonly List<Vector2> ShellUv0 = new List<Vector2>(), ShellUv1 = new List<Vector2>();
        public readonly List<Vector4> ShellUv2 = new List<Vector4>(), ShellUv3 = new List<Vector4>(), ShellUv4 = new List<Vector4>(), ShellUv5 = new List<Vector4>();
        public readonly List<int> ShellTris = new List<int>();
        public readonly List<int> ShellSource = new List<int>();   // mesh vertex of each shell vertex

        public int StrandCount => Roots != null ? Roots.Length : 0;
        public bool HasShells => ShellTris.Count > 0;

        public Mesh BuildShellMesh(string name)
        {
            var m = new Mesh { name = name };
            if (ShellPos.Count > 65535) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(ShellPos);
            m.SetNormals(ShellNrm);
            m.SetUVs(0, ShellUv0);
            m.SetUVs(1, ShellUv1);
            m.SetUVs(2, ShellUv2);
            m.SetUVs(3, ShellUv3);
            m.SetUVs(4, ShellUv4);
            m.SetUVs(5, ShellUv5);
            m.SetTriangles(ShellTris, 0, true);
            return m;
        }
    }

    public static class FurBuilder
    {
        public const int MaxRegions = 8;

        // The species' Fur module (null = no fur).
        public static Fur ModuleOf(CreatureSpecies sp)
        {
            if (sp == null) return null;
            CreatureSpecies rt = sp.Compiled.Runtime;
            if (rt.bodyPlan == CreatureBodyPlan.Cephalopod) return null;
            IReadOnlyList<CreatureModule> mods = KitView.Modules(rt);
            if (mods == null) return null;
            for (int i = 0; i < mods.Count; i++) if (mods[i] is Fur f && f.regions != null && f.regions.Length > 0) return f;
            return null;
        }

        // ------------------------------------------------------------------ deterministic random

        public static uint Hash(uint a, uint b, uint c, uint d)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= c * 0xC2B2AE3Du; h ^= h >> 12; h *= 0x297A2D39u;
            h ^= d * 0x27D4EB2Fu; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return h;
        }
        static float U(uint h) => (h & 0xFFFFFFu) / 16777216f;
        static float U(uint a, uint b, uint c, uint d) => U(Hash(a, b, c, d));

        // ------------------------------------------------------------------ masks

        // 1 inside [a, b], smoothly to 0 over e outside it (e = 0: hard).
        public static float Window(float x, float a, float b, float e)
        {
            if (x < a) return e > 0f ? 1f - Smooth01((a - x) / e) : 0f;
            if (x > b) return e > 0f ? 1f - Smooth01((x - b) / e) : 0f;
            return 1f;
        }
        static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        public static bool PartIn(int partMask, float partId)
        {
            int p = Mathf.RoundToInt(partId);
            return p >= 0 && p < 31 && ((partMask >> p) & 1) != 0;
        }

        // A region's weight at a vertex (0 when its part is not in the mask).
        public static float Weight(in FurRegion r, Vector2 uv0, Vector2 uv1)
        {
            if (!PartIn(r.partMask, uv1.x)) return 0f;
            return Window(uv0.x, r.arcFrom, r.arcTo, r.arcEdge) * Window(uv1.y, r.dorsalFrom, r.dorsalTo, r.dorsalEdge);
        }

        // ------------------------------------------------------------------ the build

        // From the species' render data (KitRender: the mesh and bind poses). linear: decode sRGB colours (a
        // linear-space project, as SetColor would).
        public static FurData Build(CreatureSpecies sp, Mesh mesh, Matrix4x4[] bindPoses, bool linear = true)
        {
            Fur fur = ModuleOf(sp);
            if (fur == null) throw new ArgumentException((sp != null ? sp.name : "null") + " has no Fur module", nameof(sp));
            Vector3[] pos = mesh.vertices, nrm = mesh.normals;
            int[] tris = mesh.triangles;
            var uv0 = new List<Vector2>(); var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
            mesh.GetUVs(0, uv0); mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
            return Build(fur, pos, nrm, uv0, uv1, uv2, tris, bindPoses, linear);
        }

        public static FurData Build(Fur fur, Vector3[] pos, Vector3[] nrm, List<Vector2> uv0, List<Vector2> uv1, List<Vector4> uv2,
                                    int[] tris, Matrix4x4[] bindPoses, bool linear)
        {
            int nv = pos.Length, nt = tris.Length / 3, nr = fur.regions.Length;
            var d = new FurData();
            uint seed = (uint)fur.seed;

            // region weights per vertex
            var w = new float[nr][];
            for (int r = 0; r < nr; r++)
            {
                w[r] = new float[nv];
                for (int i = 0; i < nv; i++) w[r][i] = Weight(fur.regions[r], uv0[i], uv1[i]);
            }

            // rest bone worlds (forward axes for the comb)
            var restWorld = new Matrix4x4[bindPoses.Length];
            for (int b = 0; b < bindPoses.Length; b++) restWorld[b] = bindPoses[b].inverse;

            d.Regions = new FurRegionRow[nr];
            for (int r = 0; r < nr; r++)
            {
                FurRegion g = fur.regions[r];
                Color rc = linear ? g.rootColour.linear : g.rootColour, tc = linear ? g.tipColour.linear : g.tipColour;
                d.Regions[r] = new FurRegionRow
                {
                    root = new Vector4(rc.r, rc.g, rc.b, Mathf.Clamp01(g.bodyTint)),
                    tip = new Vector4(tc.r, tc.g, tc.b, 0f),
                    dyn = new Vector4(g.stiffnessHz, g.damping, g.droop, (float)(int)g.kind),
                };
                float lmax = g.length * (1f + g.lengthJitter);
                d.MaxLength = Mathf.Max(d.MaxLength, lmax);
                if (g.kind == FurKind.Strands) d.MaxStrandLength = Mathf.Max(d.MaxStrandLength, lmax);
            }

            // ---- strands: budget, then sampling
            double expected = 0;
            for (int r = 0; r < nr; r++)
            {
                FurRegion g = fur.regions[r];
                if (g.kind != FurKind.Strands) continue;
                for (int t = 0; t < nt; t++)
                {
                    int i0 = tris[3 * t], i1 = tris[3 * t + 1], i2 = tris[3 * t + 2];
                    if (!TriIn(g, uv1, i0, i1, i2)) continue;
                    expected += g.density * Area(pos, i0, i1, i2) * (w[r][i0] + w[r][i1] + w[r][i2]) / 3.0;
                }
            }
            float budget = expected > fur.maxStrands ? (float)(fur.maxStrands / expected) : 1f;

            var roots = new List<FurRoot>();
            var rootTri = new List<int>();
            var rootPos = new List<Vector3>();
            var rootNrm = new List<Vector3>();
            var rootSide = new List<Vector3>();
            var remap = new Dictionary<int, int>();
            var verts = new List<FurSkinVertex>();
            var source = new List<int>();

            for (int r = 0; r < nr; r++)
            {
                FurRegion g = fur.regions[r];
                if (g.kind != FurKind.Strands) continue;
                int regionFirst = roots.Count;
                float dens = g.density * budget;
                for (int t = 0; t < nt; t++)
                {
                    int i0 = tris[3 * t], i1 = tris[3 * t + 1], i2 = tris[3 * t + 2];
                    if (!TriIn(g, uv1, i0, i1, i2)) continue;
                    float w0 = w[r][i0], w1 = w[r][i1], w2 = w[r][i2];
                    float wmax = Mathf.Max(w0, Mathf.Max(w1, w2));
                    if (!(wmax > 0f)) continue;
                    float area = Area(pos, i0, i1, i2);
                    if (!(area > 1e-12f)) continue;
                    float exp = dens * area;   // bound 1: each sample is kept with the region's weight at its point
                    int n = Mathf.FloorToInt(exp + U(seed, (uint)r, (uint)t, 0xA11u));
                    for (int k = 0; k < n; k++)
                    {
                        uint ks = (uint)k * 8u + 1u;
                        float su = Mathf.Sqrt(U(seed, (uint)r, (uint)t, ks)), sv = U(seed, (uint)r, (uint)t, ks + 1u);
                        // uniform on the triangle: (1 - sqrt u, sqrt u (1 - v), sqrt u v)
                        float l0 = 1f - su, l1 = su * (1f - sv), l2 = su * sv;
                        // the weight at the point itself (the windows of its interpolated arc and dorsal coordinates)
                        Vector2 pu0 = l0 * uv0[i0] + l1 * uv0[i1] + l2 * uv0[i2], pu1 = new Vector2(uv1[i0].x, l0 * uv1[i0].y + l1 * uv1[i1].y + l2 * uv1[i2].y);
                        float wp = Weight(g, pu0, pu1);
                        if (U(seed, (uint)r, (uint)t, ks + 2u) >= wp) continue;   // rejection by the weight here
                        if (!(wp > 0f)) continue;

                        Vector3 p = l0 * pos[i0] + l1 * pos[i1] + l2 * pos[i2];
                        Vector3 nn = PartMeshEmitter.Unit(l0 * nrm[i0] + l1 * nrm[i1] + l2 * nrm[i2], FaceNormal(pos, i0, i1, i2));
                        int heavy = l0 >= l1 && l0 >= l2 ? i0 : (l1 >= l2 ? i1 : i2);
                        int bone = Mathf.RoundToInt(uv2[heavy].x);
                        Vector3 fwd = bone >= 0 && bone < restWorld.Length ? (Vector3)restWorld[bone].GetColumn(2) : Vector3.forward;
                        Vector3 flow = -fwd.normalized * g.flowBack + Vector3.down * g.flowDown;
                        Vector3 tan = flow - nn * Vector3.Dot(flow, nn);
                        if (tan.sqrMagnitude < 1e-8f)
                        {
                            Vector3 any = Mathf.Abs(nn.x) < 0.9f ? Vector3.right : Vector3.forward;
                            tan = Vector3.Cross(nn, any);
                        }
                        tan.Normalize();
                        float jit = g.jitterDeg * Mathf.Deg2Rad;
                        float a = (2f * U(seed, (uint)r, (uint)t, ks + 3u) - 1f) * jit;
                        Vector3 side = Vector3.Cross(nn, tan);
                        tan = (tan * Mathf.Cos(a) + side * Mathf.Sin(a)).normalized;
                        side = Vector3.Cross(nn, tan);
                        float lift = Mathf.Clamp(g.liftDeg + (2f * U(seed, (uint)r, (uint)t, ks + 4u) - 1f) * 0.5f * g.jitterDeg, 2f, 89f) * Mathf.Deg2Rad;
                        Vector3 comb = (tan * Mathf.Cos(lift) + nn * Mathf.Sin(lift)).normalized;
                        // the hair curves away from its rise: the second segment lies flatter, toward the comb
                        Vector3 tip = (comb + tan * 0.35f).normalized;

                        float len = g.length * (1f + g.lengthJitter * (2f * U(seed, (uint)r, (uint)t, ks + 5u) - 1f)) * (0.4f + 0.6f * Mathf.Clamp01(wp));
                        float wid = g.width * (0.8f + 0.4f * U(seed, (uint)r, (uint)t, ks + 6u));
                        roots.Add(new FurRoot
                        {
                            v0 = (uint)Remap(i0, remap, verts, source, pos, nrm, uv0, uv1, uv2, bindPoses),
                            v1 = (uint)Remap(i1, remap, verts, source, pos, nrm, uv0, uv1, uv2, bindPoses),
                            v2 = (uint)Remap(i2, remap, verts, source, pos, nrm, uv0, uv1, uv2, bindPoses),
                            b1 = l1, b2 = l2, length = len, width = wid, region = (uint)r,
                            comb = comb, tipDir = tip, seed = Hash(seed, (uint)r, (uint)t, ks + 7u),
                        });
                        rootTri.Add(t); rootPos.Add(p); rootNrm.Add(nn); rootSide.Add(tan);
                    }
                }
                if (g.clump > 0f && g.clumpSize > 1) Clump(roots, rootPos, rootNrm, regionFirst, roots.Count, g, dens, seed, (uint)r);
            }

            // shuffle (any prefix is a uniform subset), cap, rank
            int count = roots.Count;
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = count - 1; i > 0; i--)
            {
                int j = (int)(Hash(seed, 0x5EEDu, (uint)i, 0x51u) % (uint)(i + 1));
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }
            int keep = Mathf.Min(count, Mathf.Max(0, fur.maxStrands));
            d.Roots = new FurRoot[keep];
            d.RootTriangle = new int[keep];
            for (int i = 0; i < keep; i++)
            {
                FurRoot rt = roots[order[i]];
                rt.rank = keep > 0 ? (i + 0.5f) / keep : 0f;
                d.Roots[i] = rt;
                d.RootTriangle[i] = rootTri[order[i]];
            }

            // ---- undercoat shells: furry triangles, per-vertex height / hair / colours blended by the regions' weights
            var shellMap = new Dictionary<int, int>();
            for (int t = 0; t < nt; t++)
            {
                int i0 = tris[3 * t], i1 = tris[3 * t + 1], i2 = tris[3 * t + 2];
                bool any = false;
                for (int r = 0; r < nr && !any; r++)
                {
                    FurRegion g = fur.regions[r];
                    if (g.kind != FurKind.Undercoat || !TriIn(g, uv1, i0, i1, i2)) continue;
                    if (w[r][i0] > 0f || w[r][i1] > 0f || w[r][i2] > 0f) any = true;
                }
                if (!any) continue;
                d.ShellTris.Add(ShellVertex(d, fur, w, i0, shellMap, pos, nrm, uv0, uv1, uv2, linear));
                d.ShellTris.Add(ShellVertex(d, fur, w, i1, shellMap, pos, nrm, uv0, uv1, uv2, linear));
                d.ShellTris.Add(ShellVertex(d, fur, w, i2, shellMap, pos, nrm, uv0, uv1, uv2, linear));
            }

            d.Verts = verts.ToArray();
            d.SourceVertex = source.ToArray();
            return d;
        }

        static bool TriIn(in FurRegion g, List<Vector2> uv1, int i0, int i1, int i2)
            => PartIn(g.partMask, uv1[i0].x) && PartIn(g.partMask, uv1[i1].x) && PartIn(g.partMask, uv1[i2].x);

        static float Area(Vector3[] p, int i0, int i1, int i2) => 0.5f * Vector3.Cross(p[i1] - p[i0], p[i2] - p[i0]).magnitude;

        static Vector3 FaceNormal(Vector3[] p, int i0, int i1, int i2) => PartMeshEmitter.Unit(Vector3.Cross(p[i1] - p[i0], p[i2] - p[i0]), Vector3.up);

        // CreatureSkin.hlsl's pattern radius: the rest distance from the blended bone axis
        public static float RestRadius(Matrix4x4[] bindPoses, Vector3 p, Vector4 skin)
        {
            int b0 = Mathf.RoundToInt(skin.x), b1 = Mathf.RoundToInt(skin.y);
            Vector3 a = bindPoses[b0].MultiplyPoint3x4(p) * skin.z + bindPoses[b1].MultiplyPoint3x4(p) * skin.w;
            return new Vector2(a.x, a.y).magnitude;
        }

        static int Remap(int i, Dictionary<int, int> remap, List<FurSkinVertex> verts, List<int> source, Vector3[] pos, Vector3[] nrm,
                         List<Vector2> uv0, List<Vector2> uv1, List<Vector4> uv2, Matrix4x4[] bindPoses)
        {
            if (remap.TryGetValue(i, out int k)) return k;
            k = verts.Count;
            remap[i] = k;
            verts.Add(new FurSkinVertex { pos = pos[i], nrm = nrm[i], skin = uv2[i], uv0 = uv0[i], uv1 = uv1[i], radius = RestRadius(bindPoses, pos[i], uv2[i]) });
            source.Add(i);
            return k;
        }

        static int ShellVertex(FurData d, Fur fur, float[][] w, int i, Dictionary<int, int> map, Vector3[] pos, Vector3[] nrm,
                               List<Vector2> uv0, List<Vector2> uv1, List<Vector4> uv2, bool linear)
        {
            if (map.TryGetValue(i, out int k)) return k;
            k = d.ShellPos.Count;
            map[i] = k;
            float sw = 0f, len = 0f, rad = 0f, hpm = 0f, droop = 0f, bt = 0f;
            Vector3 rc = Vector3.zero, tc = Vector3.zero;
            for (int r = 0; r < fur.regions.Length; r++)
            {
                FurRegion g = fur.regions[r];
                if (g.kind != FurKind.Undercoat) continue;
                float wi = w[r][i];
                if (!(wi > 0f)) continue;
                sw += wi;
                len = Mathf.Max(len, wi * g.length);
                rad += wi * g.width; hpm += wi * Mathf.Sqrt(g.density * 1e4f); droop += wi * g.droop; bt += wi * g.bodyTint;
                Color a = linear ? g.rootColour.linear : g.rootColour, b = linear ? g.tipColour.linear : g.tipColour;
                rc += wi * new Vector3(a.r, a.g, a.b); tc += wi * new Vector3(b.r, b.g, b.b);
            }
            if (sw > 0f) { float inv = 1f / sw; rad *= inv; hpm *= inv; droop *= inv; bt *= inv; rc *= inv; tc *= inv; }
            d.ShellPos.Add(pos[i]); d.ShellNrm.Add(nrm[i]);
            d.ShellUv0.Add(uv0[i]); d.ShellUv1.Add(uv1[i]); d.ShellUv2.Add(uv2[i]);
            d.ShellUv3.Add(new Vector4(len, rad, hpm, droop));
            d.ShellUv4.Add(new Vector4(rc.x, rc.y, rc.z, bt));
            d.ShellUv5.Add(new Vector4(tc.x, tc.y, tc.z, 0f));
            d.ShellSource.Add(i);
            return k;
        }

        // Locks: about 1 in clumpSize strands leads; every other strand of the region bends its tip toward the tip of
        // the nearest leader (a uniform grid of the lock radius, 27 cells).
        static void Clump(List<FurRoot> roots, List<Vector3> rp, List<Vector3> rn, int first, int end, in FurRegion g, float dens, uint seed, uint r)
        {
            if (end - first < 2) return;
            float spacing = 1f / Mathf.Sqrt(Mathf.Max(dens, 1e-3f));
            float cell = Mathf.Max(1e-4f, spacing * Mathf.Sqrt(g.clumpSize) * 1.2f);
            var leaders = new List<int>();
            var grid = new Dictionary<long, List<int>>();
            for (int i = first; i < end; i++)
            {
                if (U(seed, r, (uint)i, 0xC1u) * g.clumpSize >= 1f) continue;
                leaders.Add(i);
                long key = Key(rp[i], cell);
                if (!grid.TryGetValue(key, out List<int> l)) grid[key] = l = new List<int>();
                l.Add(i);
            }
            if (leaders.Count == 0) return;
            for (int i = first; i < end; i++)
            {
                int best = -1; float bd = float.MaxValue;
                Vector3 p = rp[i];
                int cx = Mathf.FloorToInt(p.x / cell), cy = Mathf.FloorToInt(p.y / cell), cz = Mathf.FloorToInt(p.z / cell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                            if (grid.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out List<int> l))
                                foreach (int j in l)
                                {
                                    float dd = (rp[j] - p).sqrMagnitude;
                                    if (dd < bd) { bd = dd; best = j; }
                                }
                if (best < 0 || best == i) continue;
                FurRoot s = roots[i], lead = roots[best];
                Vector3 leadTip = rp[best] + lead.comb * (0.5f * lead.length) + lead.tipDir * (0.5f * lead.length);
                Vector3 mid = p + s.comb * (0.5f * s.length);
                Vector3 pull = leadTip - mid;
                if (pull.sqrMagnitude < 1e-12f) continue;
                Vector3 tip = Vector3.Lerp(s.tipDir, pull.normalized, g.clump);
                Vector3 n = rn[i];
                float up = Vector3.Dot(tip, n);
                if (up < 0.1f) tip += n * (0.1f - up);
                s.tipDir = PartMeshEmitter.Unit(tip, s.tipDir);
                roots[i] = s;
            }
        }

        static long Key(Vector3 p, float cell) => Key(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
        static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
    }
}
