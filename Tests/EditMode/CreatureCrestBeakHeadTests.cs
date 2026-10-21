using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // CrestBeakHead (Apex redesign §5.4): the beaked serpent head on a FlowBody's head joint - SDF skull flowing into a
    // pointed, hooked beak, two teal crests curling back on the upper beak, lamellae along the mouth line, a jaw on its
    // own bone that opens on its own call timer. Fixture: CreatureFlowBodyTests' steep-necked FlowBody (head joint 0),
    // its hind leg, Eyes, and the head at joint 0.
    public class CreatureCrestBeakHeadTests
    {
        internal static CrestBeakHead Head() => new CrestBeakHead { id = "head", at = new PartSocket(0, 0, Vector3.zero) };

        // CreatureFlowBodyTests' steep neck, its front re-keyed to the head's back (the skull's back end, ~0.6 x its half
        // extents, starts inside the neck; the neck's front ring is a little smaller than the skull's widest).
        internal static FlowBody Neck()
        {
            FlowBody b = CreatureFlowBodyTests.SteepNeck();
            b.profile[0].halfWidth = 0.04f; b.profile[0].halfHeight = 0.052f;
            b.profile[1].halfWidth = 0.036f; b.profile[1].halfHeight = 0.05f;
            return b;
        }

        internal static CreatureSpecies Species(CrestBeakHead head = null) =>
            FlowKit.Build("CrestBeakHeadFixture", 1f,
                new List<CreaturePart> { Neck(), CreatureFlowBodyTests.HindLeg(), CreatureKitComposeTests.TwoEyes(), head ?? Head() },
                new CreatureModule[] { new Walk { speed = 0.8f, turnRateDeg = 160f, accel = 2f, turnSlowdown = 0.5f } });

        static CrestBeakHead Part(CreatureSpecies sp)
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is CrestBeakHead h) return h;
            return null;
        }

        static CrestBeakHeadRuntime Runtime(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is CrestBeakHeadRuntime h) return h;
            return null;
        }

        // The skull's frame in the rest pose (the head joint's frame turned by the head's tilt): the pieces are drawn in it.
        internal static Matrix4x4 SkullFrame(CreatureSpecies sp)
        {
            CreatureSim rest = CreatureRestPose.Sim(sp);
            CrestBeakHeadRuntime rt = Runtime(rest);
            return Matrix4x4.TRS(rest.Joints[0], rest.Rotations[0] * rt.Tilt, Vector3.one);
        }

        // ------------------------------------------------------------ closed solids

        [Test] public void Head_Solids_AreClosed()
        {
            CrestBeakHead h = Part(Species());
            Assert.AreEqual(2 + 2, h.PieceCount, "skull, jaw and the default mirrored pair of blades");
            for (int i = 0; i < h.PieceCount; i++)
            {
                SculptPiece p = h.Piece(i);
                Assert.Greater(p.Tris.Length, 300, $"piece {i} is a real solid");
                int bad = EdgesNotTwice(p.Pos, p.Tris, out int edges);
                TestContext.Out.WriteLine($"piece {i}: {p.Pos.Length} vertices, {p.Tris.Length / 3} triangles, {edges} edges, built {p.BuildMs:F1} ms, folds {p.Folds}");
                Assert.AreEqual(0, bad, $"piece {i}: every edge in exactly two triangles");
                Assert.AreEqual(0, p.Folds, $"piece {i}: no triangle turned by the mesher (a feature finer than its cell)");
            }
            // the teeth (horns on the teeth kind in the species mesh): closed too
            CreatureSpecies sp = Species();
            Mesh mesh = FlowKit.Mesh(sp);
            Vector3[] v = mesh.vertices; int[] tri = mesh.triangles;
            var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);
            Object.DestroyImmediate(mesh);
            int teeth = MaterialSlots.For(sp).SlotOf(Head().toothKind);
            var tt = new List<int>();
            for (int t = 0; t < tri.Length; t += 3)
                if (Mathf.RoundToInt(uv1[tri[t]].x) == teeth && Mathf.RoundToInt(uv1[tri[t + 1]].x) == teeth && Mathf.RoundToInt(uv1[tri[t + 2]].x) == teeth) { tt.Add(tri[t]); tt.Add(tri[t + 1]); tt.Add(tri[t + 2]); }
            int badT = EdgesNotTwice(v, tt.ToArray(), out int edgesT);
            TestContext.Out.WriteLine($"teeth: {tt.Count / 3} triangles, {edgesT} edges, {badT} not in exactly two");
            Assert.Greater(tt.Count / 3, 100, "the teeth are in the mesh");
            Assert.AreEqual(0, badT, "teeth: every edge in exactly two triangles");
        }

        // Edges (vertices welded by position) not used by exactly two triangles.
        internal static int EdgesNotTwice(Vector3[] pos, int[] tris, out int edges)
        {
            var weld = new Dictionary<Vector3, int>();
            var id = new int[pos.Length];
            for (int i = 0; i < pos.Length; i++) { if (!weld.TryGetValue(pos[i], out int w)) { w = weld.Count; weld[pos[i]] = w; } id[i] = w; }
            var count = new Dictionary<long, int>();
            for (int t = 0; t < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = id[tris[t + e]], b = id[tris[t + (e + 1) % 3]];
                    long k = a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;
                    count.TryGetValue(k, out int c); count[k] = c + 1;
                }
            edges = count.Count;
            int bad = 0;
            foreach (int c in count.Values) if (c != 2) bad++;
            return bad;
        }

        // ------------------------------------------------------------ crests

        // The upper beak's top (head frame) at x, z: the skull's field marched down from above.
        static float BeakTop(SculptField f, float x, float z)
        {
            float y = 0.5f, step = 0.001f;
            while (y > -0.5f && f.Eval(new Vector3(x, y, z)) > 0f) y -= step;
            return y;
        }

        // Blade b's vertices and centre line in the head frame.
        static Vector3[] BladeVerts(CrestBeakHead h, int b)
        {
            SculptPiece p = h.Piece(2 + b); Matrix4x4 f = h.BladeFrame(b);
            var o = new Vector3[p.Pos.Length];
            for (int i = 0; i < o.Length; i++) o[i] = f.MultiplyPoint3x4(p.Pos[i]);
            return o;
        }

        static Vector3[] BladeSpine(CrestBeakHead h, int b)
        {
            Vector3[] sp = h.CrestSpine(b); Matrix4x4 f = h.BladeFrame(b);
            for (int i = 0; i < sp.Length; i++) sp[i] = f.MultiplyPoint3x4(sp[i]);
            return sp;
        }

        struct CrestMeasure { public float zBase, zBaseRear, beakTop, top, zOverhang; }

        // Blade b in the head frame: its base (the vertices within 15 % of its height of its lowest point), the beak's top
        // there (at its root's x), its highest point, and how far back its upper half reaches.
        static CrestMeasure Measure(CrestBeakHead h, int b)
        {
            Vector3[] p = BladeVerts(h, b);
            float H = h.BladeDef(b).height;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Vector3 v in p) { lo = Mathf.Min(lo, v.y); hi = Mathf.Max(hi, v.y); }
            float band = lo + 0.15f * H;
            float zs = 0f, zr = float.MaxValue; int n = 0;
            foreach (Vector3 v in p) if (v.y <= band) { zs += v.z; n++; zr = Mathf.Min(zr, v.z); }
            var m = new CrestMeasure { zBase = zs / Mathf.Max(1, n), zBaseRear = zr, top = hi };
            m.beakTop = BeakTop(new SculptField(ToArray(h.SkullSolid())), h.BladeFrame(b).GetColumn(3).x, m.zBase);
            m.zOverhang = float.MaxValue;
            foreach (Vector3 v in p) if (v.y > m.beakTop + 0.5f * H) m.zOverhang = Mathf.Min(m.zOverhang, v.z);
            return m;
        }

        static SculptShape[] ToArray(IReadOnlyList<SculptShape> l) { var a = new SculptShape[l.Count]; for (int i = 0; i < a.Length; i++) a[i] = l[i]; return a; }

        static string CrestProblem(CrestBeakHead h)
        {
            for (int k = 0; k < h.BladeCount; k++)
            {
                CrestMeasure m = Measure(h, k);
                Vector3[] spine = BladeSpine(h, k);
                float rise = m.top - m.beakTop, H = h.BladeDef(k).height;
                TestContext.Out.WriteLine($"crest {k}: base z {m.zBase:F3} (rear {m.zBaseRear:F3}), beak top {m.beakTop:F3}, crest top {m.top:F3} (rise {rise:F3} vs height {H:F3}), upper half reaches back to z {m.zOverhang:F3}; spine tip z {spine[spine.Length - 1].z:F3} vs base {spine[0].z:F3}");
                if (rise < 0.8f * H) return $"crest {k} stands only {rise:F3} above the beak line (< 0.8 x {H:F3})";
                if (!(spine[spine.Length - 1].z < spine[0].z)) return $"crest {k} does not curl back: its tip z {spine[spine.Length - 1].z:F3} is not behind its base z {spine[0].z:F3}";
                if (!(m.zOverhang < m.zBaseRear)) return $"crest {k} does not curl back: its upper half reaches back only to z {m.zOverhang:F3}, not behind its root (z {m.zBaseRear:F3})";
            }
            return null;
        }

        [Test] public void Head_Crests_StandAboveTheBeakLine_AndCurlBack()
        {
            Assert.IsNull(CrestProblem(Part(Species())));
        }

        [Test] public void Red_Crests_Flat()
        {
            CrestBeakHead head = Head();
            for (int k = 0; k < head.crests.Length; k++) head.crests[k].curlDeg = 0f;
            string p = CrestProblem(Part(Species(head)));
            Assert.IsNotNull(p, "straight crests (curlDeg 0) must fail the curl-back test");
            StringAssert.Contains("curl back", p);
        }

        // RED for the above-the-beak-line half: crests sunk by half their height into the beak.
        [Test] public void Red_Crests_Sunk()
        {
            CrestBeakHead.CrestSinkForTest = 0.75f;   // (fix 1: the crescent's width outside the curl stands ~0.2 x height above the centre line)
            try
            {
                string p = CrestProblem(Part(Species()));
                Assert.IsNotNull(p, "crests sunk half their height must fail the above-the-beak-line test");
                StringAssert.Contains("above the beak line", p);
            }
            finally { CrestBeakHead.CrestSinkForTest = 0f; }
        }

        // The default crests are one mirrored CrestDef: two blades whose vertex sets mirror each other across the head's
        // midplane (x -> -x; every vertex of one within 2.5 mm - about the blades' polygoniser cell, 0.3 x thickness: the
        // grid is not symmetric - of a mirrored vertex of the other, both ways) and stand
        // equally high above the beak (within 1 mm).
        static string PairProblem(CrestBeakHead h, out float worst, out float dTop)
        {
            worst = 0f; dTop = 0f;
            if (h.BladeCount != 2) return $"{h.BladeCount} blades, not a pair";
            Vector3[] a = BladeVerts(h, 0), b = BladeVerts(h, 1);
            float Far(Vector3[] from, Vector3[] to)
            {
                float most = 0f;
                for (int i = 0; i < from.Length; i += 3)   // every third vertex: a dense enough sample
                {
                    Vector3 m = new Vector3(-from[i].x, from[i].y, from[i].z);
                    float best = float.MaxValue;
                    foreach (Vector3 q in to) best = Mathf.Min(best, (q - m).sqrMagnitude);
                    most = Mathf.Max(most, Mathf.Sqrt(best));
                }
                return most;
            }
            worst = Mathf.Max(Far(a, b), Far(b, a));
            CrestMeasure ma = Measure(h, 0), mb = Measure(h, 1);
            dTop = Mathf.Abs((ma.top - ma.beakTop) - (mb.top - mb.beakTop));
            TestContext.Out.WriteLine($"pair: farthest mirrored vertex {worst * 1000f:F2} mm; rises {ma.top - ma.beakTop:F4} / {mb.top - mb.beakTop:F4} (difference {dTop * 1000f:F2} mm)");
            if (worst > 0.0025f) return $"the blades do not mirror each other: a vertex {worst * 1000f:F2} mm from its mirror image";
            if (dTop > 0.001f) return $"the blades are not equally high: {dTop * 1000f:F2} mm apart";
            return null;
        }

        [Test] public void Head_Crests_AreAMirroredEqualPair()
        {
            CrestBeakHead h = Part(Species());
            Assert.AreEqual(1, h.crests.Length, "the defaults: one crest definition");
            Assert.IsTrue(h.crests[0].mirror, "... mirrored into a pair");
            Assert.IsNull(PairProblem(h, out _, out _));
        }

        [Test] public void Red_Crests_UnequalPair()
        {
            CrestBeakHead.BladeScaleForTest = 1.15f;
            try { Assert.IsNotNull(PairProblem(Part(Species()), out _, out _), "a pair with one blade 15 % larger must fail"); }
            finally { CrestBeakHead.BladeScaleForTest = 0f; }
        }

        // Seen from the front, the pair is a V: each blade's tip is farther from the midplane than its root by at least
        // sin(max(splayDeg, 25 deg) / 2) x height, the tips on opposite sides, and the roots close together (within 2 x thickness).
        const float kMinSplayDeg = 25f;   // the reference's V: each blade at least this far from upright

        static string VProblem(CrestBeakHead h)
        {
            if (h.BladeCount != 2) return $"{h.BladeCount} blades, not a pair";
            CrestDef d = h.BladeDef(0);
            float want = Mathf.Sin(0.5f * Mathf.Max(d.splayDeg, kMinSplayDeg) * Mathf.Deg2Rad) * d.height;   // a clear V: at least kMinSplayDeg each
            var root = new float[2]; var tip = new float[2];
            for (int b = 0; b < 2; b++)
            {
                Vector3[] sp = BladeSpine(h, b);
                root[b] = sp[0].x; tip[b] = sp[sp.Length - 1].x;
                TestContext.Out.WriteLine($"blade {b}: root x {root[b] * 1000f:F1} mm, tip x {tip[b] * 1000f:F1} mm (out by {(Mathf.Abs(tip[b]) - Mathf.Abs(root[b])) * 1000f:F1} mm, want >= {want * 1000f:F1})");
                if (!(Mathf.Abs(tip[b]) - Mathf.Abs(root[b]) >= want)) return $"blade {b} does not lean out: its tip is only {(Mathf.Abs(tip[b]) - Mathf.Abs(root[b])) * 1000f:F1} mm farther from the midplane than its root (< {want * 1000f:F1})";
            }
            if (!(tip[0] * tip[1] < 0f)) return "the tips are not on opposite sides of the midplane";
            if (Mathf.Abs(root[0] - root[1]) > 2f * d.thickness) return $"the roots are {Mathf.Abs(root[0] - root[1]) * 1000f:F1} mm apart (> 2 x thickness)";
            return null;
        }

        [Test] public void Head_Crests_FormAV()
        {
            Assert.IsNull(VProblem(Part(Species())));
        }

        [Test] public void Red_Crests_NoSplay()
        {
            CrestBeakHead head = Head();
            head.crests[0].splayDeg = 0f;
            string p = VProblem(Part(Species(head)));
            Assert.IsNotNull(p, "an unsplayed pair (two parallel blades) must fail the V test");
            StringAssert.Contains("lean out", p);
        }

        // The fixture walks its tightest circle without a folded triangle (it once folded a body sliver: FlowBody's auto
        // rigid head took the neck and chest as one block - FlowBody.AutoHead).
        [Test] public void Head_Fixture_MaxBend()
        {
            Assert.IsNull(KitChecks.MaxBendProblem(Species()));
        }

        // The blade's side outline, measured on the outline itself (fix 1: the crescent's hooked, widening end made rays
        // from the centre line cross the blade). The outline is the zero contour of the blade's mid-plane slice (x = 0 in
        // its own frame; the flattened blade is widest there), traced by marching squares on a 0.3 mm grid and resampled
        // every 0.25 mm of arc. Along it, clear of the root (stations nearer the centre line's first 30 %) and the point
        // (an arc of 15 % of its length either side of its point):
        //   ripple   each outline point's distance from the cubic through the points h and 2h of arc (h = 5 % of the
        //            blade's length) either side, |P - (-P(-2h) + 4 P(-h) + 4 P(+h) - P(+2h)) / 6|: any smooth outline -
        //            the crescent's swell, its hook - leaves ~0 (a circle of radius R: h^4 / 6 R^3), a chain of blended
        //            blobs a scallop at every join - more than kRipple anywhere bulges;
        // and then the point: the outline's farthest from the centre line over its last 5 % is < 0.25 x its farthest
        // anywhere (a point, not a blunt end).
        // 0.45 mm (Task 4) x 1.5: the crests are 1.5 x the Task 4b blade and h scales with it. The sickle's own swell and
        // fall read ~0.55 mm (a smooth outline's residual h^4 e'''' / 6: the crescent falls from its widest to the point
        // over a third of the blade); the six-blob chains 1.5 - 1.75 mm.
        internal const float kRipple = 0.000675f;

        internal static string SmoothProblem(CrestBeakHead h, out float worstRipple)
        {
            worstRipple = 0f;
            string bump = null, blunt = null;
            for (int k = 0; k < h.BladeCount; k++)
            {
                var f = new SculptField(ToArray(h.CrestSolid(k)));
                Vector3[] sp = h.CrestSpine(k);
                float len = 0f;
                for (int i = 1; i < sp.Length; i++) len += (sp[i] - sp[i - 1]).magnitude;
                List<Vector2> o = Outline2D(f, 0.0003f);
                if (o == null || o.Count < 100) return $"crest {k}: no outline traced";
                List<Vector2> P = Resample(o, 0.00025f);
                int n = P.Count, K = Mathf.Max(2, Mathf.RoundToInt(0.05f * len / 0.00025f));
                // each point's place along the centre line (its nearest node) and its distance from it
                var st = new float[n]; var dist = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float best = float.MaxValue; int bi = 0;
                    for (int j = 0; j < sp.Length; j++) { float d = (new Vector2(sp[j].y, sp[j].z) - P[i]).sqrMagnitude; if (d < best) { best = d; bi = j; } }
                    st[i] = (float)bi / (sp.Length - 1); dist[i] = Mathf.Sqrt(best);
                }
                // the point: the outline's point nearest the centre line's end; the ripple stencil stays an arc of 5 % of
                // the length clear of it (its needle tip is a corner, not a scallop)
                int tipIdx = 0; Vector2 end = new Vector2(sp[sp.Length - 1].y, sp[sp.Length - 1].z);
                for (int i = 1; i < n; i++) if ((P[i] - end).sqrMagnitude < (P[tipIdx] - end).sqrMagnitude) tipIdx = i;
                int clear = 2 * K + K;
                float ripple = 0f; Vector2 at = default; float atU = 0f, widest = 0f, tipWidest = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (st[i] >= 0.3f && st[i] <= 0.95f) widest = Mathf.Max(widest, dist[i]);
                    if (st[i] > 0.95f) tipWidest = Mathf.Max(tipWidest, dist[i]);
                    int fromTip = Mathf.Abs(i - tipIdx); fromTip = Mathf.Min(fromTip, n - fromTip);
                    if (st[i] < 0.3f || fromTip < clear) continue;
                    bool rootIn = false;
                    for (int q = -2; q <= 2; q++) if (st[((i + q * K) % n + n) % n] < 0.25f) rootIn = true;
                    if (rootIn) continue;
                    Vector2 c = (-P[(i - 2 * K + 4 * n) % n] + 4f * P[(i - K + n) % n] + 4f * P[(i + K) % n] - P[(i + 2 * K) % n]) / 6f;
                    float r = (P[i] - c).magnitude;
                    if (r > ripple) { ripple = r; at = P[i]; atU = st[i]; }
                }
                worstRipple = Mathf.Max(worstRipple, ripple);
                TestContext.Out.WriteLine($"crest {k}: outline {n} points ({n * 0.25f:F0} mm); widest {widest * 1000f:F2} mm from the centre line, over its last 5 % {tipWidest * 1000f:F2} mm; ripple {ripple * 1000f:F3} mm at u {atU:F2} (y {at.x:F3} z {at.y:F3}) (limit {kRipple * 1000f:F2} mm)");
                if (bump == null && ripple > kRipple) bump = $"crest {k}: its outline bulges - a {ripple * 1000f:F2} mm ripple at u {atU:F2} (scallops in the silhouette)";
                if (blunt == null && tipWidest > 0.25f * widest) blunt = $"crest {k}: a blunt tip ({tipWidest:F4} from the centre line over its last 5 % vs its widest {widest:F4})";
            }
            return bump ?? blunt;
        }

        internal static string SmoothProblem(CrestBeakHead h) => SmoothProblem(h, out _);

        // The zero contour of f(0, y, z) (the longest closed loop), as (y, z) points, by marching squares on a grid of `cell`.
        static List<Vector2> Outline2D(SculptField f, float cell)
        {
            float y0 = f.Lo.y - 2f * cell, z0 = f.Lo.z - 2f * cell;
            int ny = Mathf.CeilToInt((f.Hi.y - f.Lo.y) / cell) + 5, nz = Mathf.CeilToInt((f.Hi.z - f.Lo.z) / cell) + 5;
            var v = new float[ny, nz];
            for (int a = 0; a < ny; a++) for (int b = 0; b < nz; b++) v[a, b] = f.Eval(new Vector3(0f, y0 + a * cell, z0 + b * cell));
            // segments between edge crossings, keyed by the crossing's grid edge
            var next = new Dictionary<long, long>(); var pos = new Dictionary<long, Vector2>();
            long EdgeKey(int a, int b, int dir) => ((long)a * 100000 + b) * 2 + dir;   // dir 0: (a,b)-(a+1,b), 1: (a,b)-(a,b+1)
            Vector2 Cross(int a, int b, int dir)
            {
                float va = v[a, b], vb = dir == 0 ? v[a + 1, b] : v[a, b + 1], t = va / (va - vb);
                return dir == 0 ? new Vector2(y0 + (a + t) * cell, z0 + b * cell) : new Vector2(y0 + a * cell, z0 + (b + t) * cell);
            }
            for (int a = 0; a + 1 < ny; a++)
                for (int b = 0; b + 1 < nz; b++)
                {
                    // the cell's four edges in order round it: bottom (a,b)-(a+1,b), right (a+1,b)-(a+1,b+1), top (a,b+1)-(a+1,b+1), left (a,b)-(a,b+1)
                    var e = new List<long>(4);
                    bool In(int i, int j) => v[i, j] < 0f;
                    if (In(a, b) != In(a + 1, b)) e.Add(EdgeKey(a, b, 0));
                    if (In(a + 1, b) != In(a + 1, b + 1)) e.Add(EdgeKey(a + 1, b, 1));
                    if (In(a, b + 1) != In(a + 1, b + 1)) e.Add(EdgeKey(a, b + 1, 0));
                    if (In(a, b) != In(a, b + 1)) e.Add(EdgeKey(a, b, 1));
                    if (e.Count == 2) Link(e[0], e[1]);
                    else if (e.Count == 4) { Link(e[0], e[1]); Link(e[2], e[3]); }   // a saddle (rare at 0.3 mm): either pairing closes the loops
                    void Link(long p, long q)
                    {
                        foreach (long x in new[] { p, q })
                            if (!pos.ContainsKey(x)) { int d = (int)(x % 2); long ab = x / 2; pos[x] = Cross((int)(ab / 100000), (int)(ab % 100000), d); }
                        if (!next.ContainsKey(p)) next[p] = q; else next[p | (1L << 62)] = q;
                        if (!next.ContainsKey(q)) next[q] = p; else next[q | (1L << 62)] = p;
                    }
                }
            // walk the loops
            var seen = new HashSet<long>(); List<Vector2> best = null;
            foreach (long start in pos.Keys)
            {
                if (seen.Contains(start)) continue;
                var loop = new List<Vector2>(); long cur = start, prev = -1;
                while (cur >= 0 && seen.Add(cur))
                {
                    loop.Add(pos[cur]);
                    long a1 = next.TryGetValue(cur, out long x1) ? x1 : -1, a2 = next.TryGetValue(cur | (1L << 62), out long x2) ? x2 : -1;
                    long nx = a1 != prev && a1 >= 0 && !seen.Contains(a1) ? a1 : a2 != prev && a2 >= 0 && !seen.Contains(a2) ? a2 : -1;
                    prev = cur; cur = nx;
                }
                if (best == null || loop.Count > best.Count) best = loop;
            }
            return best;
        }

        // A closed polyline resampled every `step` of arc.
        static List<Vector2> Resample(List<Vector2> loop, float step)
        {
            var o = new List<Vector2>();
            int n = loop.Count; float carry = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = loop[i], b = loop[(i + 1) % n]; float L = (b - a).magnitude, s = carry;
                while (s < L) { o.Add(a + (b - a) * (s / Mathf.Max(1e-12f, L))); s += step; }
                carry = s - L;
            }
            return o;
        }

        [Test] public void Head_Crests_SmoothSharpBlades()
        {
            Assert.IsNull(SmoothProblem(Part(Species()), out float ripple));
            // (fix 2, controller ruling: no margin assertion here - the GREEN residual, ~0.56 mm, is the crescent's own fall
            // from its widest to the point, not ripple; the margin is held on the other side: the blob REDs stay > 2 x kRipple)
        }

        // RED for the bump rule alone: the first six-blob crest, but tapering to the same sharp point - only its scallops
        // can fail it.
        [Test] public void Red_Crests_Blobby_SharpTip()
        {
            CrestBeakHead.BlobCrestsForTest = true; CrestBeakHead.BlobSharpTipForTest = true;
            try
            {
                string p = SmoothProblem(Part(Species()), out float ripple);
                Assert.IsNotNull(p, "a sharp-tipped chain of six blobs must fail the smoothness test");
                StringAssert.Contains("bulges", p);
                Assert.Greater(ripple, 2f * kRipple, "its scallops clear the limit by a margin");
            }
            finally { CrestBeakHead.BlobCrestsForTest = false; CrestBeakHead.BlobSharpTipForTest = false; }
        }

        // RED for the tip rule: the first six-blob crest as it was (blunt end at 0.35 x thickness).
        [Test] public void Red_Crests_Blobby()
        {
            CrestBeakHead.BlobCrestsForTest = true;
            try { Assert.IsNotNull(SmoothProblem(Part(Species())), "the six-blob crest must fail the smoothness test"); }
            finally { CrestBeakHead.BlobCrestsForTest = false; }
        }

        // ------------------------------------------------------------ the join

        // The side-view outline (head frame: z along the head, y up) of the rest mesh's body and head skin (no crests,
        // eyes or limbs) at 20 stations from 0.05 m behind the skull's back end to 0.15 m ahead of it: the largest
        // change of the top or bottom outline between neighbouring stations.
        // gape >= 0: the posed mesh of an unticked sim held at that gape (the rest pose otherwise), and only the top
        // outline (the open jaw changes the bottom one by design).
        internal static string StepProblem(CreatureSpecies sp, float limit, out float worst, float gape = -1f)
        {
            CrestBeakHead h = Part(sp);
            Matrix4x4 toHead = SkullFrame(sp).inverse;
            Vector3[] v; int[] tri; var uv1 = new List<Vector2>();
            if (gape < 0f)
            {
                Mesh mesh = FlowKit.Mesh(sp);
                v = mesh.vertices; tri = mesh.triangles;
                mesh.GetUVs(1, uv1);
                Object.DestroyImmediate(mesh);
            }
            else
            {
                CreatureSim sim = FlowKit.Sim(sp, 1f);
                Runtime(sim).GapeOverride = gape;
                var sk = new CreaturePteroWingTests.Skinned(sp); sk.Pose(sim);
                v = sk.P; tri = sk.T; uv1 = sk.Uv1;
            }
            bool topOnly = gape >= 0f;
            MaterialSlots slots = MaterialSlots.For(sp);
            int crest = slots.SlotOf("crest"), teethSlot = slots.SlotOf(h.toothKind), mouthSlot = slots.SlotOf("mouth");
            var hv = new Vector3[v.Length];
            var use = new bool[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                hv[i] = toHead.MultiplyPoint3x4(v[i]);
                int part = Mathf.RoundToInt(uv1[i].x);
                use[i] = part != crest && part != teethSlot && part != mouthSlot && part != PartMeshEmitter.PartEye && part != PartMeshEmitter.PartLeg && part != PartMeshEmitter.PartFoot
                         && Mathf.Abs(hv[i].x) < 0.3f && Mathf.Abs(hv[i].y) < 0.3f;
            }
            const int kStations = 20;
            float z0 = -0.5f * h.skullLength - 0.05f, z1 = -0.5f * h.skullLength + 0.15f;
            var top = new float[kStations]; var bot = new float[kStations];
            for (int s = 0; s < kStations; s++)
            {
                float z = Mathf.Lerp(z0, z1, s / (kStations - 1f));
                top[s] = float.MinValue; bot[s] = float.MaxValue;
                for (int t = 0; t < tri.Length; t += 3)
                {
                    int a = tri[t], b = tri[t + 1], c = tri[t + 2];
                    if (!use[a] || !use[b] || !use[c]) continue;
                    Cut(hv[a], hv[b], z, ref top[s], ref bot[s]); Cut(hv[b], hv[c], z, ref top[s], ref bot[s]); Cut(hv[c], hv[a], z, ref top[s], ref bot[s]);
                }
            }
            // a bulge: within 0.05 m past the skull's back end the outline stays within the neck's +- the limit
            float bulgeTop = top[0] + limit, bulgeBot = bot[0] - limit;
            for (int s = 0; s < kStations; s++)
            {
                float z = Mathf.Lerp(z0, z1, s / (kStations - 1f));
                if (z > -0.5f * h.skullLength + 0.05f) break;
                if (top[s] > bulgeTop || (!topOnly && bot[s] < bulgeBot))
                {
                    worst = Mathf.Max(top[s] - top[0], bot[0] - bot[s]);
                    return $"the head bulges out of the neck at z {z:F3}: outline {top[s]:F3}/{bot[s]:F3} vs the neck's {top[0]:F3}/{bot[0]:F3} (+-{limit:F4})";
                }
            }
            worst = 0f; int at = -1;
            var line = new System.Text.StringBuilder();
            for (int s = 0; s < kStations; s++)
            {
                line.Append($" {top[s]:F3}/{bot[s]:F3}");
                if (s == 0) continue;
                float d = topOnly ? Mathf.Abs(top[s] - top[s - 1]) : Mathf.Max(Mathf.Abs(top[s] - top[s - 1]), Mathf.Abs(bot[s] - bot[s - 1]));
                if (d > worst) { worst = d; at = s; }
            }
            TestContext.Out.WriteLine($"outline top/bottom z {z0:F3}..{z1:F3}:{line}");
            TestContext.Out.WriteLine($"largest step {worst:F4} m at station {at} (limit {limit:F4})");
            return worst <= limit ? null : $"the outline steps {worst:F4} m between stations {at - 1} and {at} (> 0.15 x skullDepth = {limit:F4})";
        }

        // Where the edge a-b crosses the plane z: grow the outline's top and bottom there.
        static void Cut(Vector3 a, Vector3 b, float z, ref float top, ref float bot)
        {
            if ((a.z - z) * (b.z - z) > 0f || a.z == b.z) return;
            float f = (z - a.z) / (b.z - a.z);
            float y = a.y + (b.y - a.y) * f;
            top = Mathf.Max(top, y); bot = Mathf.Min(bot, y);
        }

        [Test] public void Head_Flows_IntoTheNeck()
        {
            Assert.IsNull(StepProblem(Species(), 0.15f * Head().skullDepth, out _));
        }

        [Test] public void Red_Head_Step()
        {
            CrestBeakHead head = Head();
            head.skullDepth *= 2f;
            // the same neck, so the same limit: 0.15 x the depth the head was authored for
            Assert.IsNotNull(StepProblem(Species(head), 0.15f * Head().skullDepth, out _), "a skull twice as deep must step out of the neck");
        }

        // ------------------------------------------------------------ jaw

        [Test] public void Head_Jaw_RestGape_OpensOnCall()
        {
            CreatureSpecies sp = Species();
            CrestBeakHead h = Part(sp);
            foreach (float size in new[] { 1f, 0.9f, 1.1f })
            {
                CreatureSim sim = FlowKit.Sim(sp, size, seed: 7);
                CrestBeakHeadRuntime rt = Runtime(sim);
                Assert.IsNotNull(rt, "the head has a runtime");
                Assert.AreEqual(h.restGapeDeg, rt.Gape, "at its resting gape at t = 0");
                float T = Mathf.Sqrt(size);
                int frames = Mathf.CeilToInt((h.callEverySeconds.y + h.callSeconds) * T / FlowKit.kDt) + 2;
                float most = 0f; bool closedAfter = false; float openedAt = -1f;
                for (int f = 0; f < frames; f++)
                {
                    sim.Tick(FlowKit.kDt, new Vector3(0f, 0f, 3f));
                    most = Mathf.Max(most, rt.Gape);
                    if (most >= 0.9f * h.gapeDeg && openedAt < 0f) openedAt = f * FlowKit.kDt;
                    if (most >= 0.9f * h.gapeDeg && rt.Gape == h.restGapeDeg) { closedAfter = true; break; }
                }
                TestContext.Out.WriteLine($"size {size}: widest {most:F1} deg (gapeDeg {h.gapeDeg}) first at {openedAt:F2} s, back to rest ({h.restGapeDeg} deg): {closedAfter}");
                Assert.GreaterOrEqual(most, 0.9f * h.gapeDeg, $"size {size}: opens on a call");
                Assert.IsTrue(closedAfter, $"size {size}: back to its resting gape after the call");
            }
        }

        [Test] public void Head_JawRange_NoInvertedTriangles()
        {
            CreatureSpecies sp = Species();
            CrestBeakHead h = Part(sp);
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            FlowKit.Run(sim, 1f, new Vector3(0f, 0f, 3f));
            CrestBeakHeadRuntime rt = Runtime(sim);
            foreach (float f in new[] { 0f, 0.5f, 1f })
            {
                rt.GapeOverride = f * h.gapeDeg;
                Assert.AreEqual(0, FlowKit.InvertedTriangles(sp, sim, out string where), $"gape {f * h.gapeDeg:F0} deg: {where}");
            }
            rt.GapeOverride = -1f;
        }

        // Through the gape (a quarter, half and full gapeDeg), in the skull's frame: the jaw's front (ahead of 0.4 of the
        // way from the hinge to the tip) stays out of the skull (field >= -1 mm), and the jaw's part behind the hinge does
        // not poke out of the closed head's skin (HeadSolid) by more than 0.1 x skullDepth (it slides into the throat).
        internal static string JawThroughSkull(CreatureSpecies sp, out float frontIn, out float rearOut)
        {
            CrestBeakHead h = Part(sp);
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            CrestBeakHeadRuntime rt = Runtime(sim);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            Matrix4x4 rest = SkullFrame(sp);
            var skull = new SculptField(ToArray(h.SkullSolid())); var head = new SculptField(ToArray(h.HeadSolid()));
            SculptPiece jaw = h.Piece(1);
            Matrix4x4 bindJaw = h.JawPose(h.restGapeDeg);   // the mesh draws the jaw at the resting gape (the bind)
            float zt = 0.5f * h.skullLength + h.beakLength, zFront = h.Hinge.z + 0.4f * (zt - h.Hinge.z);
            frontIn = 0f; rearOut = 0f;
            Vector3 rearAt = default, rearTo = default;
            foreach (float frac in new[] { 0.25f, 0.5f, 1f })
            {
                rt.GapeOverride = frac * h.gapeDeg;
                Matrix4x4[] bones = FlowKit.Bones(sim);
                Matrix4x4 jawToSkull = (bones[rt.UpperBone] * bind[rt.UpperBone] * rest).inverse * bones[rt.JawBone] * bind[rt.JawBone] * rest;
                foreach (Vector3 p in jaw.Pos)
                {
                    Vector3 q = jawToSkull.MultiplyPoint3x4(bindJaw.MultiplyPoint3x4(p));
                    if (p.z > zFront) frontIn = Mathf.Max(frontIn, -skull.Eval(q));
                    else if (p.z < h.Hinge.z) { float o = head.Eval(q); if (o > rearOut) { rearOut = o; rearAt = p; rearTo = q; } }   // out of the closed head's skin
                }
            }
            TestContext.Out.WriteLine($"gape 1/4, 1/2, full: jaw front deepest in the skull {frontIn * 1000f:F1} mm, jaw rear farthest out of the head {rearOut * 1000f:F1} mm (jaw point {rearAt} -> {rearTo}; hinge {h.Hinge})");
            if (frontIn > 0.001f) return $"the jaw's front runs {frontIn * 1000f:F1} mm into the skull";
            if (rearOut > 0.1f * h.skullDepth) return $"the jaw's rear pokes {rearOut * 1000f:F1} mm out of the head";
            return null;
        }

        [Test] public void Head_Jaw_FullGape_ClearOfTheSkull()
        {
            Assert.GreaterOrEqual(Head().gapeDeg, 55f, "a call opens wide (the face close-up: ~60-70 deg)");
            Assert.IsNull(JawThroughSkull(Species(), out _, out _));
        }

        [Test] public void Red_Jaw_IntoTheSkull()
        {
            CrestBeakHeadRuntime.FlipGapeForTest = true;
            try
            {
                string p = JawThroughSkull(Species(), out _, out _);
                Assert.IsNotNull(p, "a jaw turning up must run into the skull");
                StringAssert.Contains("into the skull", p);
            }
            finally { CrestBeakHeadRuntime.FlipGapeForTest = false; }
        }

        // The jaw really moves its vertices: the jaw tip drops with the gape.
        [Test] public void Head_Jaw_Opens_TheMouth()
        {
            CreatureSpecies sp = Species();
            CrestBeakHead h = Part(sp);
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            CrestBeakHeadRuntime rt = Runtime(sim);
            Matrix4x4[] closed = FlowKit.Bones(sim);
            rt.GapeOverride = h.gapeDeg;
            Matrix4x4[] open = FlowKit.Bones(sim);
            Matrix4x4 head = closed[0];
            Vector3 tip = new Vector3(0f, 0f, 0.5f * h.skullLength + 0.8f * h.beakLength);
            Vector3 a = head.inverse.MultiplyPoint3x4(closed[rt.JawBone].MultiplyPoint3x4(tip)), b = head.inverse.MultiplyPoint3x4(open[rt.JawBone].MultiplyPoint3x4(tip));
            TestContext.Out.WriteLine($"jaw point closed {a} open {b}");
            Assert.Less(b.y, a.y - 0.05f, "the jaw's front drops when it gapes");
        }

        // ------------------------------------------------------------ the face close-ups (Task 4c)

        // The resting gape, from the rest pose's bones: the jaw bone turned front-down against the head joint's by
        // more than 0 and less than 15 degrees (the mouth hangs a little open at rest, showing the teeth).
        static float RestGape(CreatureSpecies sp)
        {
            CreatureSim sim = CreatureRestPose.Sim(sp);
            CrestBeakHeadRuntime rt = Runtime(sim);
            Matrix4x4[] w = FlowKit.Bones(sim);
            Matrix4x4 rel = w[rt.UpperBone].inverse * w[rt.JawBone];
            Vector3 f = rel.MultiplyVector(Vector3.forward).normalized;
            float deg = Vector3.Angle(Vector3.forward, f) * (f.y < 0f ? 1f : -1f);
            TestContext.Out.WriteLine($"rest pose: the jaw bone turned {deg:F2} deg front-down from the head's");
            return deg;
        }

        [Test] public void Head_Jaw_SlightlyOpenAtRest()
        {
            float g = RestGape(Species());
            Assert.Greater(g, 0.5f, "the mouth hangs a little open at rest");
            Assert.Less(g, 15f, "... only a little");
        }

        [Test] public void Red_Jaw_ShutAtRest()
        {
            CrestBeakHead head = Head(); head.restGapeDeg = 0f;
            Assert.LessOrEqual(RestGape(Species(head)), 0.5f, "restGapeDeg 0 must read as shut at rest");
        }

        static SculptField Field(IReadOnlyList<SculptShape> l) => new SculptField(ToArray(l));

        // The teeth: 2 x teeth per row (upper and lower, both sides), every one
        //   on the lip line   where it leaves the gum within 3 mm of the mouth line and of the closed head's surface, its
        //                     root in its own solid (the upper in the skull, the lower in the jaw: within 0.5 mm);
        //   showing           even with the mouth shut (gape 0; at rest it hangs open, so this is the harder case) its tip
        //                     across the mouth line by >= 0.25 x its length and outside both solids - outboard of the
        //                     opposite lip: in the side view a row of interlocking teeth crossing the lip line.
        internal static string TeethProblem(CrestBeakHead h, out int hidden)
        {
            hidden = 0;
            var skull = Field(h.SkullSolid()); var jaw = Field(h.JawSolid()); var head = Field(h.HeadSolid());
            int upper = 0, lower = 0;
            float worstLine = 0f, worstRoot = -1f, leastCross = float.MaxValue, leastOut = float.MaxValue;
            string first = null;
            for (int i = 0; i < h.ToothCount; i++)
            {
                CrestBeakHead.ToothDef t = h.Tooth(i);
                if (t.fang) continue;
                if (t.lower) lower++; else upper++;
                float line = Mathf.Max(Mathf.Abs(t.emerge.y - h.MouthY(t.emerge.z)), Mathf.Abs(head.Eval(t.emerge)));
                float root = (t.lower ? jaw : skull).Eval(t.basePt);
                worstLine = Mathf.Max(worstLine, line); worstRoot = Mathf.Max(worstRoot, root);
                if (first == null && line > 0.003f) first = $"tooth {i} ({(t.lower ? "lower" : "upper")}, z {t.emerge.z:F3}) is not on the lip line: {line * 1000f:F1} mm off";
                if (first == null && root > 0.0005f) first = $"tooth {i} ({(t.lower ? "lower" : "upper")}) is not rooted in its {(t.lower ? "jaw" : "skull")}: its root {root * 1000f:F1} mm outside";
                // shut: an upper tip against the jaw, a lower tip against the skull
                float cross = t.lower ? (t.tip.y - h.MouthY(t.tip.z)) : (h.MouthY(t.tip.z) - t.tip.y);
                float outOwn = t.lower ? jaw.Eval(t.tip) : skull.Eval(t.tip);
                float outOther = t.lower ? skull.Eval(t.tip) : jaw.Eval(t.tip);
                float o = Mathf.Min(outOwn, outOther);
                leastCross = Mathf.Min(leastCross, cross / t.length); leastOut = Mathf.Min(leastOut, o);
                if (cross < 0.25f * t.length || o < 0f) hidden++;
            }
            TestContext.Out.WriteLine($"teeth: {upper} upper, {lower} lower; worst off the lip line {worstLine * 1000f:F2} mm, worst root {worstRoot * 1000f:F2} mm (field), least crossing {leastCross:F2} x length, least clearance of a tip {leastOut * 1000f:F2} mm; {hidden} hidden with the mouth shut");
            if (upper == 0 || lower == 0) return $"no teeth ({upper} upper, {lower} lower)";
            if (upper != 2 * h.teeth || lower != 2 * h.teeth) return $"{upper} upper / {lower} lower teeth, want {2 * h.teeth} each";
            if (first != null) return first;
            if (hidden > 0) return $"{hidden} teeth hidden with the mouth shut (tip not across the mouth line by 0.25 x its length, or inside a solid)";
            return null;
        }

        [Test] public void Head_Teeth_OnTheLipLine_ShowingShut()
        {
            Assert.IsNull(TeethProblem(Part(Species()), out _));
        }

        [Test] public void Red_Teeth_None()
        {
            CrestBeakHead head = Head(); head.teeth = 0;
            StringAssert.Contains("no teeth", TeethProblem(Part(Species(head)), out _));
        }

        [Test] public void Red_Teeth_OffTheLipLine()
        {
            CrestBeakHead.TeethLiftForTest = 0.02f;
            try { StringAssert.Contains("lip line", TeethProblem(Part(Species()), out _)); }
            finally { CrestBeakHead.TeethLiftForTest = 0f; }
        }

        [Test] public void Red_Teeth_HiddenBehindTheLips()
        {
            // rooted 8 mm in from the lip: the opposite lip hides the tips (the lip-line clause fails too - this RED is
            // for the showing clause alone: its own count)
            CrestBeakHead.TeethInsetForTest = 0.008f;
            try
            {
                Assert.IsNotNull(TeethProblem(Part(Species()), out int hidden));
                Assert.Greater(hidden, 0, "teeth set in behind the lips must count as hidden");
            }
            finally { CrestBeakHead.TeethInsetForTest = 0f; }
        }

        // Fangs: 2 x fangPairs (2..4) on the jaw, leaving it within 0.12 x beakLength of its front end, pointing forward,
        // longer than the teeth (>= 1.3 x), rooted in the jaw.
        static string FangProblem(CrestBeakHead h)
        {
            var jaw = Field(h.JawSolid());
            int n = 0;
            for (int i = 0; i < h.ToothCount; i++)
            {
                CrestBeakHead.ToothDef t = h.Tooth(i);
                if (!t.fang) continue;
                n++;
                float back = h.JawFrontZ - t.emerge.z, root = jaw.Eval(t.basePt);
                TestContext.Out.WriteLine($"fang {i}: {back * 1000f:F1} mm behind the jaw's front, length {t.length * 1000f:F1} mm, dir {t.dir}, root {root * 1000f:F2} mm (field), lower {t.lower}");
                if (!t.lower) return $"fang {i} is not on the jaw";
                if (back < -0.002f || back > 0.12f * h.beakLength) return $"fang {i} is not at the jaw's tip ({back * 1000f:F1} mm behind its front)";
                if (t.dir.z < 0.5f) return $"fang {i} does not point forward";
                if (t.length < 1.3f * h.toothLength) return $"fang {i} is no longer than a tooth";
                if (root > 0.0005f) return $"fang {i} is not rooted in the jaw";
            }
            if (n < 2 || n > 4) return $"{n} fangs at the lower jaw's tip, want 2..4";
            return null;
        }

        [Test] public void Head_Fangs_AtTheLowerJawsTip() => Assert.IsNull(FangProblem(Part(Species())));

        [Test] public void Red_Fangs_None()
        {
            CrestBeakHead head = Head(); head.fangPairs = 0;
            StringAssert.Contains("fangs", FangProblem(Part(Species(head))));
        }

        // The knob under the jaw's tip: at the knob, the jaw's underside (its lowest point at x = 0) dips >= 0.3 x the knob's
        // radius below the same jaw's without a knob (chinKnob 0).
        static float JawBottom(CrestBeakHead h, float z)
        {
            var jaw = Field(h.JawSolid());
            for (float y = -0.4f; y < 0.4f; y += 0.0002f) if (jaw.Eval(new Vector3(0f, y, z)) < 0f) return y;
            return float.NaN;
        }

        static string KnobProblem(CrestBeakHead h, float zk, float radius, out float dip)
        {
            CrestBeakHead bare = Head(); bare.chinKnob = 0f;
            float at = JawBottom(h, zk), without = JawBottom(Part(Species(bare)), zk);
            dip = without - at;
            TestContext.Out.WriteLine($"jaw underside at the knob (z {zk:F3}) {at:F4}, without a knob {without:F4}: dips {dip * 1000f:F2} mm");
            return dip >= 0.3f * radius ? null : $"no knob under the jaw's tip: the underside dips only {dip * 1000f:F2} mm (< {0.3f * radius * 1000f:F2})";
        }

        [Test] public void Head_Knob_UnderTheJawsTip()
        {
            CrestBeakHead h = Part(Species());
            Assert.IsNull(KnobProblem(h, h.KnobCentre.z, h.chinKnob, out _));
        }

        [Test] public void Red_Knob_None()
        {
            CrestBeakHead h0 = Part(Species());
            CrestBeakHead head = Head(); head.chinKnob = 0f;
            StringAssert.Contains("no knob", KnobProblem(Part(Species(head)), h0.KnobCentre.z, h0.chinKnob, out _));
        }

        // The mouth line runs far back: the gape's corner (the hinge) behind the eye by >= the eye socket's length
        // (0.11 x skullLength), and below it.
        static string CornerProblem(CrestBeakHead h)
        {
            Vector3 e = h.EyeSurface, c = h.Hinge;
            TestContext.Out.WriteLine($"gape corner z {c.z:F3} y {c.y:F3}; eye z {e.z:F3} y {e.y:F3} ({(e.z - c.z) * 1000f:F0} mm behind it)");
            if (!(e.z - c.z >= 0.11f * h.skullLength)) return $"the gape's corner is not past the eye: only {(e.z - c.z) * 1000f:F0} mm behind it";
            if (!(c.y < e.y)) return "the gape's corner is not under the eye";
            return null;
        }

        [Test] public void Head_Mouth_CornerFarBack() => Assert.IsNull(CornerProblem(Part(Species())));

        [Test] public void Red_Mouth_CornerUnderTheEye()
        {
            CrestBeakHead head = Head(); head.mouthCornerAt = 0.1f;
            StringAssert.Contains("not past the eye", CornerProblem(Part(Species(head))));
        }

        // The crests are sickles (the face close-up): a thin prong at the root (0.1 of the blade's length) widening toward its
        // END into a crescent - the widest half chord (both edges together) at >= 0.55 of the length and >= 1.5 x the
        // prong's; the width on the outside of the curl (the front edge's half chord >= 1.5 x the back's there); and a
        // hooked point (the centre line turns back by >= 50 deg over its last fifth).
        internal static string FlareProblem(CrestBeakHead h, out float ratio)
        {
            ratio = 0f;
            for (int k = 0; k < h.BladeCount; k++)
            {
                var f = new SculptField(ToArray(h.CrestSolid(k)));
                Vector3[] sp = h.CrestSpine(k);
                float root = Chord(f, sp, 0.1f, 1f) + Chord(f, sp, 0.1f, -1f), widest = 0f, at = 0f;
                for (float u = 0.1f; u <= 0.97f; u += 0.01f)
                {
                    float c = Chord(f, sp, u, 1f) + Chord(f, sp, u, -1f);
                    if (c > widest) { widest = c; at = u; }
                }
                float front = Chord(f, sp, at, 1f), back = Chord(f, sp, at, -1f);
                ratio = widest / Mathf.Max(1e-6f, root);
                Vector3 t0 = (sp[(int)(0.8f * (sp.Length - 1))] - sp[(int)(0.8f * (sp.Length - 1)) - 1]).normalized, t1 = (sp[sp.Length - 1] - sp[sp.Length - 2]).normalized;
                float hook = Vector3.Angle(t0, t1);
                TestContext.Out.WriteLine($"blade {k}: chord at 0.1 {root * 1000f:F2} mm, widest {widest * 1000f:F2} mm at u {at:F2} (x {ratio:F2}); there front {front * 1000f:F2} / back {back * 1000f:F2} mm; the last fifth turns {hook:F1} deg");
                if (at < 0.55f) return $"blade {k} is not a crescent: widest at u {at:F2} (< 0.55), not toward its end";
                if (ratio < 1.5f) return $"blade {k} does not flare: x {ratio:F2} from its root to its widest";
                if (front < 1.5f * back) return $"blade {k}: its width is not on the outside of the curl (front {front * 1000f:F2} vs back {back * 1000f:F2} mm)";
                if (hook < 50f) return $"blade {k}: its point does not hook ({hook:F1} deg over the last fifth)";
            }
            return null;
        }

        // One edge's half chord (side +1 front / -1 back; side plane, square to the centre line) at u of the blade's length.
        static float Chord(SculptField f, Vector3[] sp, float u, float side)
        {
            var cum = new float[sp.Length];
            for (int i = 1; i < sp.Length; i++) cum[i] = cum[i - 1] + (sp[i] - sp[i - 1]).magnitude;
            float s = u * cum[sp.Length - 1]; int j = 1;
            while (j < sp.Length - 1 && cum[j] < s) j++;
            float w = (s - cum[j - 1]) / Mathf.Max(1e-9f, cum[j] - cum[j - 1]);
            Vector3 p = Vector3.Lerp(sp[j - 1], sp[j], w), t = (sp[j] - sp[j - 1]).normalized;
            Vector3 n = new Vector3(0f, -t.z, t.y) * side;
            float e = 0f;
            while (e < 0.2f && f.Eval(p + n * e) < 0f) e += 0.00005f;
            return e;
        }

        [Test] public void Head_Crests_SickleFlare()
        {
            Assert.IsNull(FlareProblem(Part(Species()), out _));
        }

        [Test] public void Red_Crests_NoFlare()
        {
            CrestBeakHead head = Head(); head.crests[0].flare = 0f;
            Assert.IsNotNull(FlareProblem(Part(Species(head)), out _), "a plain taper is no crescent");
        }

        [Test] public void Red_Crests_WidestMidBlade()
        {
            CrestBeakHead head = Head(); head.crests[0].flareAt = 0.4f;
            StringAssert.Contains("not a crescent", FlareProblem(Part(Species(head)), out _));
        }

        [Test] public void Red_Crests_Symmetric()
        {
            CrestBeakHead head = Head(); head.crests[0].outside = 0f;
            StringAssert.Contains("outside of the curl", FlareProblem(Part(Species(head)), out _));
        }

        [Test] public void Red_Crests_NoHook()
        {
            CrestBeakHead head = Head(); head.crests[0].hookDeg = 0f;
            StringAssert.Contains("does not hook", FlareProblem(Part(Species(head)), out _));
        }

        // ------------------------------------------------------------ the jaw's own body, the mouth, the cheeks (fix 1)

        // The lower jaw is deep and fleshy: at mid-length (half way from the gape's corner to its front) it reaches
        // >= 0.7 x skullDepth below the mouth line (the head's own outline gave a gharial-like rod).
        internal static string JawDepthProblem(CrestBeakHead h, out float depth)
        {
            var jaw = Field(h.JawSolid());
            float z = 0.5f * (h.Hinge.z + h.JawFrontZ), bottom = float.NaN;
            for (float y = -0.4f; y < 0.4f; y += 0.0002f) if (jaw.Eval(new Vector3(0f, y, z)) < 0f) { bottom = y; break; }
            depth = h.MouthY(z) - bottom;
            TestContext.Out.WriteLine($"jaw at mid-length (z {z:F3}): {depth * 1000f:F1} mm below the mouth line ({depth / h.skullDepth:F2} x skullDepth)");
            return depth >= 0.7f * h.skullDepth ? null : $"the lower jaw is shallow: {depth * 1000f:F1} mm deep at mid-length (< 0.7 x skullDepth)";
        }

        [Test] public void Head_Jaw_DeepAndFleshy() => Assert.IsNull(JawDepthProblem(Part(Species()), out _));

        [Test] public void Red_Jaw_Rod()
        {
            CrestBeakHead head = Head(); head.jawDepth = 0f; head.chinDepth = 0f; head.throatDepth = 0f;
            StringAssert.Contains("shallow", JawDepthProblem(Part(Species(head)), out _));
        }

        // The posed mesh at a gape (degrees) in the upper head's frame (skull frame at rest), and the head's own vertices
        // (those after the species without the head).
        sealed class Posed { public Vector3[] P; public int[] T; public List<Vector2> Uv1; public int First; }

        static Posed PoseHead(CreatureSpecies sp, float gape)
        {
            Mesh plain = FlowKit.Mesh(FlowKit.Build("NoHead", 1f, new List<CreaturePart> { Neck(), CreatureFlowBodyTests.HindLeg(), CreatureKitComposeTests.TwoEyes() },
                new CreatureModule[] { new Walk { speed = 0.8f, turnRateDeg = 160f, accel = 2f, turnSlowdown = 0.5f } }));
            int first = plain.vertexCount;
            Object.DestroyImmediate(plain);
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            CrestBeakHeadRuntime rt = Runtime(sim);
            rt.GapeOverride = gape;
            var m = new CreaturePteroWingTests.Skinned(sp);
            m.Pose(sim);
            Matrix4x4[] bones = FlowKit.Bones(sim), bind = CreatureRestPose.BindPoses(sp);
            Matrix4x4 toHead = (bones[rt.UpperBone] * bind[rt.UpperBone] * SkullFrame(sp)).inverse;
            var o = new Posed { P = new Vector3[m.P.Length], T = m.T, Uv1 = m.Uv1, First = first };
            for (int i = 0; i < o.P.Length; i++) o.P[i] = toHead.MultiplyPoint3x4(m.P[i]);
            return o;
        }

        // As PoseHead, for any species at its own scale after walking `seconds` (straight ahead); First = the head's first
        // vertex (its bones), so a hit before it is the body's (or another part drawn before the head).
        static Posed PoseHeadWalked(CreatureSpecies sp, float gape, float seconds)
        {
            float s = sp.scale;
            CreatureSim sim = FlowKit.Sim(sp, s);
            for (int t = 0, n = Mathf.RoundToInt(seconds / FlowKit.kDt); t < n; t++)
                sim.Tick(FlowKit.kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * (2f * s));
            CrestBeakHeadRuntime rt = Runtime(sim);
            rt.GapeOverride = gape;
            var m = new CreaturePteroWingTests.Skinned(sp);
            m.Pose(sim);
            Matrix4x4[] bones = FlowKit.Bones(sim), bind = CreatureRestPose.BindPoses(sp);
            Matrix4x4 toHead = (bones[rt.UpperBone] * bind[rt.UpperBone] * SkullFrame(sp)).inverse;
            int first = m.P.Length;
            for (int i = 0; i < m.P.Length; i++) { int b = (int)m.Uv2[i].x; if ((b == rt.UpperBone || b == rt.JawBone) && i < first) first = i; }
            var o = new Posed { P = new Vector3[m.P.Length], T = m.T, Uv1 = m.Uv1, First = first };
            for (int i = 0; i < o.P.Length; i++) o.P[i] = toHead.MultiplyPoint3x4(m.P[i]);
            return o;
        }

        // Moeller-Trumbore: the ray from o along d hits triangle abc ahead of it.
        static bool Hit(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 e1 = b - a, e2 = c - a, pv = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det; Vector3 tv = o - a;
            float u = Vector3.Dot(tv, pv) * inv; if (u < 0f || u > 1f) return false;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, qv) * inv; if (v < 0f || u + v > 1f) return false;
            return Vector3.Dot(e2, qv) * inv > 1e-5f;
        }

        static bool HitsHead(Posed m, Vector3 o, Vector3 d, int skipVertex = -1)
        {
            for (int t = 0; t < m.T.Length; t += 3)
            {
                int a = m.T[t], b = m.T[t + 1], c = m.T[t + 2];
                if (a < m.First || b < m.First || c < m.First || a == skipVertex || b == skipVertex || c == skipVertex) continue;
                if (Hit(o, d, m.P[a], m.P[b], m.P[c])) return true;
            }
            return false;
        }

        // The open mouth shows its inside: at the full gape, of the mouth-kind vertices (a sample), >= 25 look out into the
        // open along their normal (a ray from 1 mm off the surface meets none of the head's triangles) - the roof and
        // floor seen through the gape, in the mouth's pink-red.
        static string MouthProblem(CreatureSpecies sp, out int seen)
        {
            CrestBeakHead h = Part(sp);
            Posed m = PoseHead(sp, h.gapeDeg);
            int mouth = MaterialSlots.For(sp).SlotOf("mouth");
            Mesh mesh = FlowKit.Mesh(sp); Vector3[] restN = mesh.normals; Object.DestroyImmediate(mesh);
            CreatureSim sim = FlowKit.Sim(sp, 1f); CrestBeakHeadRuntime rt = Runtime(sim); rt.GapeOverride = h.gapeDeg;
            var sk = new CreaturePteroWingTests.Skinned(sp); sk.Pose(sim);
            Matrix4x4[] bones = FlowKit.Bones(sim), bind = CreatureRestPose.BindPoses(sp);
            Matrix4x4 toHead = (bones[rt.UpperBone] * bind[rt.UpperBone] * SkullFrame(sp)).inverse;
            seen = 0; int tried = 0, count = 0;
            for (int i = m.First; i < m.P.Length; i++)
            {
                if (Mathf.RoundToInt(m.Uv1[i].x) != mouth) continue;
                if (count++ % 25 != 0) continue;
                // the posed normal: the vertex's rest normal through its own skin
                Vector4 w = sk.Uv2[i];
                Matrix4x4 S = sk.Skin((int)w.x);
                Vector3 n = toHead.MultiplyVector(S.MultiplyVector(restN[i])).normalized;
                tried++;
                if (!HitsHead(m, m.P[i] + n * 0.001f, n, i)) seen++;
            }
            // and only the mouth: in the rest mesh no mouth-kind vertex stands more than 12 mm above the mouth line (the eye
            // sockets and nostrils are cuts inside the closed head too, but skin)
            Mesh rm = FlowKit.Mesh(sp); Vector3[] rv = rm.vertices; var ruv = new List<Vector2>(); rm.GetUVs(1, ruv); Object.DestroyImmediate(rm);
            Matrix4x4 toRest = SkullFrame(sp).inverse; int high = 0;
            for (int i = m.First; i < rv.Length; i++)
                if (Mathf.RoundToInt(ruv[i].x) == mouth) { Vector3 q = toRest.MultiplyPoint3x4(rv[i]); if (q.y > h.MouthY(q.z) + 0.012f) high++; }
            TestContext.Out.WriteLine($"mouth-kind vertices {count}; sampled {tried}, {seen} look out into the open at the full gape; {high} above the mouth line at rest");
            if (count == 0) return "no mouth-kind vertices: the inside of the mouth is body skin";
            if (high > 0) return $"{high} mouth-kind vertices above the mouth line (an eye socket or nostril in the mouth's colour)";
            return seen >= 25 ? null : $"the open mouth hardly shows its inside: {seen} sampled mouth vertices in the open (< 25)";
        }

        [Test] public void Head_OpenMouth_ShowsTheMouthKind() => Assert.IsNull(MouthProblem(Species(), out _));

        [Test] public void Red_Mouth_BodySkin()
        {
            CrestBeakHead.NoMouthKindForTest = true;
            try { StringAssert.Contains("hardly shows its inside", MouthProblem(Species(), out int seen)); }
            finally { CrestBeakHead.NoMouthKindForTest = false; }
        }

        // No see-through slot at the corner: at the full gape, side rays (along -x) through the gap between the lips - the
        // first triangle of the whole mesh each meets must be the head's body skin (the cheek web's outside), -
        // from the gape's corner forward over the cheek web's reach (of the default head), at 1/4, 1/2 and 3/4 of the way
        // from the upper lip down to the opened jaw's lip - all meet the head's mesh (the cheek web).
        static string CornerGapProblem(CreatureSpecies sp, float reachL, out int through)
        {
            CrestBeakHead h = Part(sp);
            Posed m = PoseHead(sp, h.gapeDeg);
            Matrix4x4 open = h.JawPose(h.gapeDeg);
            through = 0; int rays = 0;
            for (int c = 1; c <= 6; c++)
            {
                float z = h.Hinge.z + reachL * h.skullLength * c / 7f;
                Vector3 up = new Vector3(0f, h.MouthY(z) - 0.003f, z), lo = open.MultiplyPoint3x4(new Vector3(0f, h.MouthY(z) + 0.003f, z));
                foreach (float f in new[] { 0.25f, 0.5f, 0.75f })
                {
                    Vector3 q = Vector3.Lerp(up, lo, f);
                    rays++;
                    // the first thing a side ray meets (whole mesh) is the head's own skin - the cheek - not the inside of
                    // the mouth or the far side
                    int hit = FirstHit(m, new Vector3(0.5f, q.y, q.z), Vector3.left);
                    if (hit < 0 || m.T[hit] < m.First || Mathf.RoundToInt(m.Uv1[m.T[hit]].x) != PartMeshEmitter.PartBody) through++;
                }
            }
            TestContext.Out.WriteLine($"side rays between the lips at the full gape: {rays}, {through} see into or through the mouth (not the cheek)");
            return through == 0 ? null : $"{through} of {rays} side rays see into the open mouth at the corner (a see-through slot)";
        }

        [Test] public void Head_OpenMouth_CheeksClosedAtTheCorner() => Assert.IsNull(CornerGapProblem(Species(), 0.8f * Head().cheekWeb, out _));

        [Test] public void Red_Mouth_NoCheeks()
        {
            CrestBeakHead head = Head(); head.cheekWeb = 0f;
            StringAssert.Contains("see-through", CornerGapProblem(Species(head), 0.8f * Head().cheekWeb, out _));
        }

        // On a call the upper head tips up: at the full gape its bone is turned nose-up by callPitchDeg (+-0.5) from the
        // head joint's, at rest by 0.
        static string CallPitchProblem(CreatureSpecies sp)
        {
            CrestBeakHead h = Part(sp);
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            CrestBeakHeadRuntime rt = Runtime(sim);
            float Up(float gape)
            {
                rt.GapeOverride = gape;
                Matrix4x4[] w = FlowKit.Bones(sim);
                Vector3 f = (w[0].inverse * w[rt.UpperBone]).MultiplyVector(Vector3.forward).normalized;
                return Vector3.Angle(Vector3.forward, f) * (f.y > 0f ? 1f : -1f);
            }
            float rest = Up(h.restGapeDeg), full = Up(h.gapeDeg);
            TestContext.Out.WriteLine($"upper head nose-up: at rest {rest:F2} deg, at the full gape {full:F2} deg (callPitchDeg {h.callPitchDeg})");
            if (Mathf.Abs(rest) > 0.01f) return $"the upper head is tipped {rest:F2} deg at rest";
            if (full < 2f || Mathf.Abs(full - h.callPitchDeg) > 0.5f) return $"the upper head does not tip up on a call ({full:F2} deg)";
            return null;
        }

        [Test] public void Head_Call_UpperHeadTipsUp() => Assert.IsNull(CallPitchProblem(Species()));

        [Test] public void Red_Call_UpperHeadStill()
        {
            CrestBeakHead head = Head(); head.callPitchDeg = 0f;
            StringAssert.Contains("does not tip up", CallPitchProblem(Species(head)));
        }


        // Inside the open mouth only the mouth shows (fix 2, C1): at the full gape, in the WHOLE species mesh (the body's
        // front end too), rays from inside the mouth meet a mouth-kind or tooth triangle first -
        //   back     from 5 cm behind the jaw's front, between the lips (1/4, 1/2, 3/4 of the way from the upper lip to
        //            the opened jaw's), at x -1, 0, +1 cm, toward the throat (straight back, 10 deg up, 10 deg down);
        //   sideways from the middle of the mouth over the cheek web's reach (of the default head) toward each side.
        internal static string InsideMouthProblem(CreatureSpecies sp, out int wrong, out int rays) => InsideMouthProblem(sp, 0f, out wrong, out rays);

        // walkSeconds > 0: the species (any, at its own scale) walks that long first and the mouth opens in its walking
        // pose - the neck bent by the walk, the body's front end moved in the head (the Apex's gate, task 10).
        internal static string InsideMouthProblem(CreatureSpecies sp, float walkSeconds, out int wrong, out int rays)
        {
            CrestBeakHead h = Part(sp);
            Posed m = walkSeconds > 0f ? PoseHeadWalked(sp, h.gapeDeg, walkSeconds) : PoseHead(sp, h.gapeDeg);
            MaterialSlots slots = MaterialSlots.For(sp);
            int mouth = slots.SlotOf("mouth"), teeth = slots.SlotOf(h.toothKind);
            Matrix4x4 open = h.JawPose(h.gapeDeg);
            int nWrong = 0, nRays = 0; string first = null;
            void Ray(Vector3 o, Vector3 d)
            {
                nRays++;
                int hit = FirstHit(m, o, d.normalized);
                int kind = hit < 0 ? -1 : Mathf.RoundToInt(m.Uv1[m.T[hit]].x);
                if (kind == mouth || kind == teeth) return;
                nWrong++;
                if (first == null) first = hit < 0 ? $"a ray from {o} along {d} leaves the mouth unseen" : $"a ray from {o} along {d} meets a {(m.T[hit] < m.First ? "body" : "head")} triangle of kind {kind} (not the mouth's)";
            }
            float zb = h.JawFrontZ - 0.05f;
            Vector3 up = new Vector3(0f, h.MouthY(zb) - 0.004f, zb), lo = open.MultiplyPoint3x4(new Vector3(0f, h.MouthY(zb) + 0.004f, zb));
            foreach (float f in new[] { 0.25f, 0.5f, 0.75f })
                foreach (float x in new[] { -0.01f, 0f, 0.01f })
                    foreach (float tilt in new[] { 0f, 0.18f, -0.18f })
                    {
                        Vector3 o = Vector3.Lerp(up, lo, f); o.x = x;
                        Ray(o, new Vector3(0f, tilt, -1f));
                    }
            float reachL = 0.8f * Head().cheekWeb;
            for (int c = 1; c <= 6; c++)
            {
                float z = h.Hinge.z + reachL * h.skullLength * c / 7f;
                Vector3 u = new Vector3(0f, h.MouthY(z) - 0.003f, z), l = open.MultiplyPoint3x4(new Vector3(0f, h.MouthY(z) + 0.003f, z));
                foreach (float f in new[] { 0.25f, 0.5f, 0.75f }) { Vector3 o = Vector3.Lerp(u, l, f); Ray(o, Vector3.right); Ray(o, Vector3.left); }
            }
            wrong = nWrong; rays = nRays;
            TestContext.Out.WriteLine($"rays from inside the open mouth: {rays}, {wrong} meet something other than the mouth or a tooth{(first != null ? ": " + first : "")}");
            return wrong == 0 ? null : $"{wrong} of {rays} rays from inside the open mouth see other skin: {first}";
        }

        // The nearest triangle (of the whole mesh) facing the ray from o along d that it meets, or -1 (back faces are
        // culled, as the renderer does: a two-sided sheet shows the face toward the eye).
        static int FirstHit(Posed m, Vector3 o, Vector3 d)
        {
            int best = -1; float bt = float.MaxValue;
            for (int t = 0; t < m.T.Length; t += 3)
            {
                Vector3 a = m.P[m.T[t]], b = m.P[m.T[t + 1]], c = m.P[m.T[t + 2]];
                Vector3 e1 = b - a, e2 = c - a, pv = Vector3.Cross(d, e2);
                if (Vector3.Dot(Vector3.Cross(e1, e2), d) > 0f) continue;
                float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-14f) continue;
                float inv = 1f / det; Vector3 tv = o - a;
                float u = Vector3.Dot(tv, pv) * inv; if (u < 0f || u > 1f) continue;
                Vector3 qv = Vector3.Cross(tv, e1);
                float v = Vector3.Dot(d, qv) * inv; if (v < 0f || u + v > 1f) continue;
                float dist = Vector3.Dot(e2, qv) * inv;
                if (dist > 1e-5f && dist < bt) { bt = dist; best = t; }
            }
            return best;
        }

        [Test] public void Head_OpenMouth_OnlyMouthInside() => Assert.IsNull(InsideMouthProblem(Species(), out _, out _));

        [Test] public void Red_Mouth_NoThroat_NeckShows()
        {
            CrestBeakHead head = Head(); head.throatRadius = 0f;
            string p = InsideMouthProblem(Species(head), out int wrong, out _);
            StringAssert.Contains("body triangle", p, "without the throat the neck's front end shows in the open mouth");
        }

        // The mouth's colour stays in the mouth: with every cut inside the closed head on the mouth kind (the eye sockets
        // and nostrils too), the above-the-mouth-line clause bites.
        [Test] public void Red_Mouth_EyeSocketsPink()
        {
            CrestBeakHead.MouthNoBoxForTest = true;
            try { StringAssert.Contains("above the mouth line", MouthProblem(Species(), out _)); }
            finally { CrestBeakHead.MouthNoBoxForTest = false; }
        }

        // The seam into the neck holds on a call too: the same outline test on the posed mesh at the full gape (the upper
        // head tipped up by callPitchDeg about the skull's back), in the rest skull frame.
        [Test] public void Head_Flows_IntoTheNeck_AtFullGape()
        {
            Assert.IsNull(StepProblem(Species(), 0.15f * Head().skullDepth, out _, Head().gapeDeg));
        }

        // toothKind must be a kind the species can draw: a registry / kit kind, or one a Layers row names.
        [Test] public void Validate_ToothKind()
        {
            CrestBeakHead h = Head();
            var parts = new List<CreaturePart> { h };
            Assert.IsNull(h.Validate(new SpeciesCtx(parts, new CreatureModule[0], 24, false)), "ivory-teeth (kit row)");
            h.toothKind = "teeth";
            Assert.IsNull(h.Validate(new SpeciesCtx(parts, new CreatureModule[0], 24, false)), "teeth (registry row 13)");
            h.toothKind = "fangs-of-doom";
            StringAssert.Contains("toothKind", h.Validate(new SpeciesCtx(parts, new CreatureModule[0], 24, false)), "an unknown kind");
            var layers = new Layers { rows = new[] { new LayerDef { kind = "fangs-of-doom" } } };
            Assert.IsNull(h.Validate(new SpeciesCtx(parts, new CreatureModule[] { layers }, 24, false)), "a kind the species' Layers row names");
        }

        // ------------------------------------------------------------ mesh time

        [Test] public void Head_MeshTime_UnderBudget()
        {
            CreatureSpecies sp = Species();
            double best = double.MaxValue;
            int builds = 0;
            for (int i = 0; i < 3; i++)
            {
                if (i == 1) builds = SculptMesher.Builds;
                var sw = Stopwatch.StartNew();
                Mesh m = FlowKit.Mesh(sp);
                best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds);
                Object.DestroyImmediate(m);
            }
            TestContext.Out.WriteLine($"species mesh {best:F2} ms (best of 3), polygonisations in builds 2-3: {SculptMesher.Builds - builds}");
            Assert.AreEqual(builds, SculptMesher.Builds, "builds 2 and 3 reuse the cached pieces");
            Assert.Less(best, 5.0, "the species mesh builds within 5 ms once the head's pieces are cached");
        }

        // ------------------------------------------------------------ look

        [Test] public void Head_Crests_AreTheCrestKind_SkullShadedByHeight()
        {
            CreatureSpecies sp = Species();
            MaterialSlots slots = MaterialSlots.For(sp);
            int crest = slots.SlotOf("crest");
            Assert.GreaterOrEqual(crest, PartLayers.LegacyIds, "crest has a kit slot");
            Assert.IsTrue(PartLayers.Find("crest", out PartLayers.Entry e), "crest has a default row");
            Mesh mesh = FlowKit.Mesh(sp);
            var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);
            Vector3[] v = mesh.vertices;
            Object.DestroyImmediate(mesh);
            int crests = 0;
            foreach (Vector2 u in uv1) if (Mathf.RoundToInt(u.x) == crest) crests++;
            Assert.Greater(crests, 100, "crest vertices on the crest slot");
            // the head's skin: dorsal follows the height in the head frame (top dark +, underside pale -)
            CrestBeakHead h = Part(sp);
            Matrix4x4 toHead = SkullFrame(sp).inverse;
            float topD = 0f, botD = 0f; int nt = 0, nb = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.RoundToInt(uv1[i].x) != 0) continue;
                Vector3 p = toHead.MultiplyPoint3x4(v[i]);
                if (p.z < 0.1f * h.skullLength || p.z > 0.4f * h.skullLength || Mathf.Abs(p.x) > 0.02f) continue;
                if (p.y > 0.6f * h.skullDepth) { topD += uv1[i].y; nt++; }
                if (p.y < -0.6f * h.skullDepth) { botD += uv1[i].y; nb++; }
            }
            Assert.Greater(nt, 0); Assert.Greater(nb, 0);
            TestContext.Out.WriteLine($"head top dorsal {topD / nt:F2} ({nt}), underside {botD / nb:F2} ({nb})");
            Assert.Greater(topD / nt, 0.6f, "the top of the head is dorsal");
            Assert.Less(botD / nb, -0.6f, "the underside is pale");
        }
    }
}
