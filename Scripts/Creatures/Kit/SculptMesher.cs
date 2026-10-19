// Creature kit: SculptMesher - polygonises a Sculpt's signed-distance field by surface nets (Gibson 1998, the
// isosurface family this project's terrain uses) on a grid of `cell` metres, once per shape set:
//   1. the grid covers the solid's bounds plus two cells; it is cut into 8^3-cell blocks, and each block keeps only the
//      shapes that can change the field near it (bounds grown by the shape's blend and three cells) - a block outside
//      an Intersect shape is outside the solid. Corners are evaluated once each, over their block's list.
//   2. a vertex for every cell the surface crosses: the mean of its edge crossings, moved onto the surface by one Newton
//      step (the cell's trilinear gradient) and kept within a quarter cell of its cell; its normal is the field's
//      central-difference gradient there (smooth shading, no faceted glow).
//   3. a quad round every grid edge with a sign change, wound so cross(b - a, c - a) points out of the solid (the inside
//      corner's side), split along the diagonal whose triangles agree best with the vertex normals - none inverted.
// The mesh is closed (every edge in an even number of triangles). Seam: uv0.y is the angle round the long axis from
// the belly; a triangle across the belly seam gets duplicates of its low side at v + 1.
// Cached by a hash of the shapes, the cell and the teeth (the build is the cost; every later build copies the piece).
// Build-time only: it allocates; main thread.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace CreatureKit
{
    public static class SculptMesher
    {
        const int B = 4;                         // block edge, cells
        public const int MaxCorners = 3000000;   // the grid coarsens past this

        static readonly Dictionary<ulong, SculptPiece> cache = new Dictionary<ulong, SculptPiece>();
        public static int Builds { get; private set; }   // cache misses (tests)

        public static SculptPiece For(Sculpt s)
        {
            ulong key = Key(s);
            if (cache.TryGetValue(key, out SculptPiece p)) return p;
            p = Build(s);
            cache[key] = p;
            return p;
        }

        public static void ClearCache() => cache.Clear();

        // ------------------------------------------------------------ the key

        static ulong Mix(ulong h, float f) { h ^= (uint)BitConverter.SingleToInt32Bits(f); return h * 1099511628211UL; }
        static ulong Mix(ulong h, int i) { h ^= (uint)i; return h * 1099511628211UL; }
        static ulong Mix(ulong h, Vector3 v) => Mix(Mix(Mix(h, v.x), v.y), v.z);

        public static ulong Key(Sculpt s)
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, s.cell);
            SculptShape[] sh = s.shapes ?? new SculptShape[0];
            h = Mix(h, sh.Length);
            foreach (SculptShape q in sh)
            {
                h = Mix(Mix(Mix(h, (int)q.kind), (int)q.op), q.blend);
                h = Mix(Mix(Mix(Mix(h, q.centre), q.size), q.end), q.rotationDeg);
                h = Mix(Mix(Mix(h, q.round), q.squash), q.mirror ? 1 : 0);
            }
            SculptTeeth[] tt = s.teeth ?? new SculptTeeth[0];
            h = Mix(h, tt.Length);
            foreach (SculptTeeth t in tt)
            {
                h = Mix(Mix(Mix(Mix(h, t.from), t.to), t.bow), t.direction);
                h = Mix(Mix(Mix(Mix(h, t.count), t.length), t.radius), t.splayDeg);
                h = Mix(Mix(Mix(Mix(h, t.curveDeg), t.jitter), t.sink), t.mirror ? 1 : 0);
            }
            return h;
        }

        // ------------------------------------------------------------ the build

        public static SculptPiece Build(Sculpt s)
        {
            var sw = Stopwatch.StartNew();
            SculptField f = s.Field();
            var piece = new SculptPiece();
            var pos = new List<Vector3>(4096);
            var nrm = new List<Vector3>(4096);
            var tris = new List<int>(24576);
            Polygonise(f, s.cell, pos, nrm, tris);
            piece.Folds = Unfold(pos, nrm, tris);
            SeamUV(f, pos, nrm, tris, out float[] v);
            piece.Pos = pos.ToArray(); piece.Nrm = nrm.ToArray(); piece.Tris = tris.ToArray(); piece.V = v;
            piece.Teeth = PlaceTeeth(s, f);
            piece.BuildMs = sw.Elapsed.TotalMilliseconds;
            Builds++;
            return piece;
        }

        // Surface nets over the field; positions and normals in the field's frame.
        public static void Polygonise(SculptField f, float cell, List<Vector3> pos, List<Vector3> nrm, List<int> tris)
        {
            if (!(f.Hi.x > f.Lo.x)) return;
            float h = Mathf.Max(Sculpt.MinCell, cell);
            Vector3 lo = default;
            int nx = 0, ny = 0, nz = 0;
            for (int guard = 0; guard < 32; guard++)
            {
                float pad = 2f * h;
                lo = f.Lo - new Vector3(pad, pad, pad);
                Vector3 hi = f.Hi + new Vector3(pad, pad, pad);
                nx = Mathf.CeilToInt((hi.x - lo.x) / h) + 1; ny = Mathf.CeilToInt((hi.y - lo.y) / h) + 1; nz = Mathf.CeilToInt((hi.z - lo.z) / h) + 1;
                if ((long)nx * ny * nz <= MaxCorners) break;
                h *= 1.25f;
            }
            int cx = nx - 1, cy = ny - 1, cz = nz - 1;            // cells per axis
            int bx = (cx + B - 1) / B, by = (cy + B - 1) / B, bz = (cz + B - 1) / B;

            // 1. per-block shape lists
            int shapes = f.Count;
            var lists = new int[bx * by * bz][];
            var listCount = new int[lists.Length];
            var fill = new float[lists.Length];   // a block the surface is far from: every corner gets this (0 = evaluate)
            var scratch = new int[shapes];
            float margin = 3f * h;
            for (int k = 0; k < bz; k++)
                for (int j = 0; j < by; j++)
                    for (int i = 0; i < bx; i++)
                    {
                        Vector3 a = lo + new Vector3(i * B, j * B, k * B) * h;
                        Vector3 b = lo + new Vector3(Mathf.Min(i * B + B, cx), Mathf.Min(j * B + B, cy), Mathf.Min(k * B + B, cz)) * h;
                        int n = 0;
                        bool outside = false, solid = false;
                        for (int q = 0; q < shapes; q++)
                        {
                            int copies = f.Touches(q, a, b, margin);
                            if (f.Op(q) == SculptOp.Intersect && f.Mirrored(q) && copies != 3) copies = 0;   // outside one of its images
                            if (copies != 0) { scratch[n++] = q << 2 | copies; if (f.Op(q) == SculptOp.Union) solid = true; }
                            else if (f.Op(q) == SculptOp.Intersect) { outside = true; break; }
                        }
                        int bi = (k * by + j) * bx + i;
                        if (outside || !solid) { lists[bi] = null; fill[bi] = SculptField.Far; continue; }
                        var l = new int[n];
                        Array.Copy(scratch, l, n);
                        lists[bi] = l; listCount[bi] = n;
                        // far from the surface (with a 1.5 x slack on the field's slope): no corner of it is on a crossed cell
                        float fc = f.Eval(0.5f * (a + b), l, n);
                        if (Mathf.Abs(fc) > 1.5f * (0.5f * (b - a).magnitude + 2f * h)) fill[bi] = fc > 0f ? SculptField.Far : -SculptField.Far;
                    }

            int Block(int i, int j, int k) => (Mathf.Min(k / B, bz - 1) * by + Mathf.Min(j / B, by - 1)) * bx + Mathf.Min(i / B, bx - 1);

            // corner values, each once, over its block's list
            var val = new float[nx * ny * nz];
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int bi = Block(i, j, k);
                        val[(k * ny + j) * nx + i] = fill[bi] != 0f ? fill[bi] : f.Eval(lo + new Vector3(i, j, k) * h, lists[bi], listCount[bi]);
                    }

            // 2. a vertex per crossed cell (kept inside its cell: no two vertices meet, no sliver triangles)
            var cellVert = new int[cx * cy * cz];
            var c8 = new float[8];
            float eps = 0.02f * h;
            for (int k = 0; k < cz; k++)
                for (int j = 0; j < cy; j++)
                    for (int i = 0; i < cx; i++)
                    {
                        int ci = (k * cy + j) * cx + i;
                        cellVert[ci] = -1;
                        int mask = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            float vv = val[((k + (c >> 2)) * ny + j + ((c >> 1) & 1)) * nx + i + (c & 1)];
                            c8[c] = vv;
                            if (vv < 0f) mask |= 1 << c;
                        }
                        if (mask == 0 || mask == 255) continue;
                        // mean of the edge crossings (local 0..1 coordinates)
                        Vector3 sum = Vector3.zero;
                        int cnt = 0;
                        for (int c = 0; c < 8; c++)
                            for (int axis = 0; axis < 3; axis++)
                            {
                                int d = c | (1 << axis);
                                if (d == c) continue;
                                float v0 = c8[c], v1 = c8[d];
                                if ((v0 < 0f) == (v1 < 0f)) continue;
                                float t = v0 / (v0 - v1);
                                Vector3 p0 = new Vector3(axis == 0 ? t : c & 1, axis == 1 ? t : (c >> 1) & 1, axis == 2 ? t : c >> 2);
                                sum += p0; cnt++;
                            }
                        Vector3 u = sum / cnt;
                        Vector3 origin = lo + new Vector3(i, j, k) * h;
                        Vector3 p = origin + u * h;
                        int bi = Block(i, j, k);
                        int[] l = lists[bi];
                        int ln = listCount[bi];
                        // two Newton steps along the trilinear gradient, kept inside the cell
                        Vector3 g = Trilinear(c8, u) / h;
                        if (l != null)
                        {
                            Vector3 mn = origin + new Vector3(0.02f, 0.02f, 0.02f) * h, mx = origin + new Vector3(0.98f, 0.98f, 0.98f) * h;
                            for (int it = 0; it < 2; it++)
                            {
                                Vector3 gi = it == 0 ? g : Trilinear(c8, (p - origin) / h) / h;
                                float g2 = gi.sqrMagnitude;
                                if (!(g2 > 1e-12f)) break;
                                p -= gi * (f.Eval(p, l, ln) / g2);
                                p = Vector3.Max(mn, Vector3.Min(mx, p));
                            }
                        }
                        Vector3 n = l == null ? g : f.Tetra(p, eps, l, ln);
                        n = PartMeshEmitter.Unit(n, PartMeshEmitter.Unit(g, Vector3.up));
                        cellVert[ci] = pos.Count;
                        pos.Add(p); nrm.Add(n);
                    }

            // 3. quads round the crossed edges
            int Cell(int i, int j, int k) => (i < 0 || j < 0 || k < 0 || i >= cx || j >= cy || k >= cz) ? -1 : cellVert[(k * cy + j) * cx + i];
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        float v0 = val[(k * ny + j) * nx + i];
                        bool in0 = v0 < 0f;
                        for (int axis = 0; axis < 3; axis++)
                        {
                            int i1 = i + (axis == 0 ? 1 : 0), j1 = j + (axis == 1 ? 1 : 0), k1 = k + (axis == 2 ? 1 : 0);
                            if (i1 >= nx || j1 >= ny || k1 >= nz) continue;
                            bool in1 = val[(k1 * ny + j1) * nx + i1] < 0f;
                            if (in0 == in1) continue;
                            int a, b, c, d;
                            if (axis == 0) { a = Cell(i, j - 1, k - 1); b = Cell(i, j, k - 1); c = Cell(i, j, k); d = Cell(i, j - 1, k); }
                            else if (axis == 1) { a = Cell(i - 1, j, k - 1); b = Cell(i, j, k - 1); c = Cell(i, j, k); d = Cell(i - 1, j, k); }
                            else { a = Cell(i - 1, j - 1, k); b = Cell(i, j - 1, k); c = Cell(i, j, k); d = Cell(i - 1, j, k); }
                            if (a < 0 || b < 0 || c < 0 || d < 0) continue;   // cannot happen inside the padded grid
                            float sg = in0 ? 1f : -1f;
                            Vector3 outward = new Vector3(axis == 0 ? sg : 0f, axis == 1 ? sg : 0f, axis == 2 ? sg : 0f);
                            Quad(pos, nrm, tris, a, b, c, d, outward);
                        }
                    }
        }

        // A crest finer than a cell can fold a quad over (its face against its vertex normals): turn such triangles to
        // face the way their normals do (they shade and cull as the surface round them). Returns how many.
        static int Unfold(List<Vector3> pos, List<Vector3> nrm, List<int> tris)
        {
            int n = 0;
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (Vector3.Dot(Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]), nrm[a] + nrm[b] + nrm[c]) > 0f) continue;
                tris[t + 1] = c; tris[t + 2] = b; n++;
            }
            return n;
        }

        // d field / d local coordinate (per cell) at u in 0..1^3 from the 8 corner values (corner c = x | y << 1 | z << 2).
        static Vector3 Trilinear(float[] c, Vector3 u)
        {
            float x = u.x, y = u.y, z = u.z;
            float gx = (1 - y) * (1 - z) * (c[1] - c[0]) + y * (1 - z) * (c[3] - c[2]) + (1 - y) * z * (c[5] - c[4]) + y * z * (c[7] - c[6]);
            float gy = (1 - x) * (1 - z) * (c[2] - c[0]) + x * (1 - z) * (c[3] - c[1]) + (1 - x) * z * (c[6] - c[4]) + x * z * (c[7] - c[5]);
            float gz = (1 - x) * (1 - y) * (c[4] - c[0]) + x * (1 - y) * (c[5] - c[1]) + (1 - x) * y * (c[6] - c[2]) + x * y * (c[7] - c[3]);
            return new Vector3(gx, gy, gz);
        }

        // One quad a-b-c-d round an edge (cyclic in the plane across it), wound outward, split on the better diagonal.
        static void Quad(List<Vector3> pos, List<Vector3> nrm, List<int> tris, int a, int b, int c, int d, Vector3 outward)
        {
            Vector3 pa = pos[a], pb = pos[b], pc = pos[c], pd = pos[d];
            // the side the solid's gradient points to: the vertex normals (exact), the edge's direction breaking a tie
            Vector3 side = nrm[a] + nrm[b] + nrm[c] + nrm[d] + outward * 0.01f;
            if (Vector3.Dot(Vector3.Cross(pc - pa, pd - pb), side) < 0f) { int t = b; b = d; d = t; }
            float s1 = Mathf.Min(Score(pos, nrm, a, b, c, outward), Score(pos, nrm, a, c, d, outward));
            float s2 = Mathf.Min(Score(pos, nrm, a, b, d, outward), Score(pos, nrm, b, c, d, outward));
            if (s1 >= s2) { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(a); tris.Add(c); tris.Add(d); }
            else { tris.Add(a); tris.Add(b); tris.Add(d); tris.Add(b); tris.Add(c); tris.Add(d); }
        }

        // How well a triangle agrees with its vertex normals and the edge's outward side (-1 .. 1; degenerate: -2).
        static float Score(List<Vector3> pos, List<Vector3> nrm, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            float m = n.magnitude;
            if (!(m > 1e-14f)) return -2f;
            n /= m;
            return Mathf.Min(Mathf.Min(Vector3.Dot(n, nrm[a]), Vector3.Dot(n, nrm[b])), Mathf.Min(Vector3.Dot(n, nrm[c]), Vector3.Dot(n, outward) + 1f));
        }

        // uv0.y round the long (z) axis from the belly, seam triangles given duplicates at v + 1.
        static void SeamUV(SculptField f, List<Vector3> pos, List<Vector3> nrm, List<int> tris, out float[] v)
        {
            float cy = 0.5f * (f.Lo.y + f.Hi.y);
            var vl = new List<float>(pos.Count + 256);
            for (int i = 0; i < pos.Count; i++)
            {
                Vector3 p = pos[i];
                float a = Mathf.Atan2(p.x, -(p.y - cy)) / (2f * Mathf.PI);
                vl.Add(a < 0f ? a + 1f : a);
            }
            var dup = new Dictionary<int, int>();
            for (int t = 0; t < tris.Count; t += 3)
            {
                float lo = Mathf.Min(vl[tris[t]], Mathf.Min(vl[tris[t + 1]], vl[tris[t + 2]]));
                float hi = Mathf.Max(vl[tris[t]], Mathf.Max(vl[tris[t + 1]], vl[tris[t + 2]]));
                if (hi - lo <= 0.5f) continue;
                for (int e = 0; e < 3; e++)
                {
                    int i = tris[t + e];
                    if (vl[i] >= 0.5f) continue;
                    if (!dup.TryGetValue(i, out int k))
                    {
                        k = pos.Count;
                        pos.Add(pos[i]); nrm.Add(nrm[i]); vl.Add(vl[i] + 1f);
                        dup[i] = k;
                    }
                    tris[t + e] = k;
                }
            }
            v = vl.ToArray();
        }

        // ------------------------------------------------------------ teeth

        static float Hash01(int a, int b)
        {
            uint x = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
            x ^= x >> 15; x *= 0x2C1B3C6Du; x ^= x >> 12; x *= 0x297A2D39u; x ^= x >> 15;
            return (x & 0xFFFFFF) / 16777216f;
        }

        static SculptPiece.Tooth[] PlaceTeeth(Sculpt s, SculptField f)
        {
            var list = new List<SculptPiece.Tooth>();
            SculptTeeth[] rows = s.teeth ?? new SculptTeeth[0];
            float h = Mathf.Max(Sculpt.MinCell, s.cell);
            for (int r = 0; r < rows.Length; r++)
            {
                SculptTeeth row = rows[r];
                for (int side = 0; side < (row.mirror ? 2 : 1); side++)
                {
                    float mx = side == 0 ? 1f : -1f;
                    for (int i = 0; i < row.count; i++)
                    {
                        float u = row.count == 1 ? 0.5f : i / (float)(row.count - 1);
                        Vector3 g = Vector3.Lerp(row.from, row.to, u) + row.bow * (4f * u * (1f - u));
                        Vector3 D = row.direction;
                        g.x *= mx; D.x *= mx;
                        D = PartMeshEmitter.Unit(D, Vector3.down);
                        // the lip: where the guide line, going along D, leaves the solid (the crossing nearest the guide point)
                        float R = 3f * row.length + 4f * h, step = 0.5f * h;
                        int steps = Mathf.CeilToInt(2f * R / step);
                        float best = float.NaN;
                        float prev = f.Eval(g - D * R);
                        for (int k = 1; k <= steps; k++)
                        {
                            float sk = -R + k * step;
                            float cur = f.Eval(g + D * sk);
                            if (prev < 0f && cur >= 0f)
                            {
                                float a = sk - step, b = sk;
                                for (int it = 0; it < 14; it++) { float m = 0.5f * (a + b); if (f.Eval(g + D * m) < 0f) a = m; else b = m; }
                                float x = 0.5f * (a + b);
                                if (float.IsNaN(best) || Mathf.Abs(x) < Mathf.Abs(best)) best = x;
                            }
                            prev = cur;
                        }
                        Vector3 lip = float.IsNaN(best) ? g : g + D * best;
                        float ends = 0.6f + 0.4f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(0.15f + 0.7f * u));
                        if (row.count == 1) ends = 1f;
                        float len = row.length * ends * (1f + row.jitter * (2f * Hash01(r * 64 + i, side) - 1f));
                        // splayed away from the midline
                        Vector3 outw = new Vector3(Mathf.Sign(g.x == 0f ? mx : g.x), 0f, 0f);
                        outw -= D * Vector3.Dot(outw, D);
                        float sa = row.splayDeg * Mathf.Deg2Rad;
                        Vector3 dir = PartMeshEmitter.Unit(D * Mathf.Cos(sa) + PartMeshEmitter.Unit(outw, Vector3.zero) * Mathf.Sin(sa), D);
                        list.Add(new SculptPiece.Tooth
                        {
                            basePt = lip - dir * (row.sink * len), dir = dir, length = len,
                            radius = row.radius * Mathf.Lerp(0.75f, 1f, ends), curveDeg = row.curveDeg,
                        });
                    }
                }
            }
            return list.ToArray();
        }
    }
}
