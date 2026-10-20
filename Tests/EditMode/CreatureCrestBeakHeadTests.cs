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
            b.profile[0].halfWidth = 0.04f; b.profile[0].halfHeight = 0.042f;
            b.profile[1].halfWidth = 0.036f; b.profile[1].halfHeight = 0.042f;
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
            Assert.AreEqual(2 + h.crests.Length, h.PieceCount);
            for (int i = 0; i < h.PieceCount; i++)
            {
                SculptPiece p = h.Piece(i);
                Assert.Greater(p.Tris.Length, 300, $"piece {i} is a real solid");
                int bad = EdgesNotTwice(p.Pos, p.Tris, out int edges);
                TestContext.Out.WriteLine($"piece {i}: {p.Pos.Length} vertices, {p.Tris.Length / 3} triangles, {edges} edges, built {p.BuildMs:F1} ms, folds {p.Folds}");
                Assert.AreEqual(0, bad, $"piece {i}: every edge in exactly two triangles");
            }
        }

        // Edges (vertices welded by position) not used by exactly two triangles.
        static int EdgesNotTwice(Vector3[] pos, int[] tris, out int edges)
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

        // The upper beak's top (head frame, x = 0) at z: the skull's field marched down from above.
        static float BeakTop(SculptField f, float z)
        {
            float y = 0.5f, step = 0.001f;
            while (y > -0.5f && f.Eval(new Vector3(0f, y, z)) > 0f) y -= step;
            return y;
        }

        struct CrestMeasure { public float zBase, zBaseRear, beakTop, top, zOverhang; }

        // Crest k in the head frame (the piece is drawn in it): its base (the vertices within 15 % of its height of its
        // lowest point), the beak's top there, its highest point, and how far back its upper half reaches.
        static CrestMeasure Measure(CrestBeakHead h, int k)
        {
            SculptPiece p = h.Piece(2 + k);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Vector3 v in p.Pos) { lo = Mathf.Min(lo, v.y); hi = Mathf.Max(hi, v.y); }
            float band = lo + 0.15f * h.crests[k].height;
            float zs = 0f, zr = float.MaxValue; int n = 0;
            foreach (Vector3 v in p.Pos) if (v.y <= band) { zs += v.z; n++; zr = Mathf.Min(zr, v.z); }
            var m = new CrestMeasure { zBase = zs / Mathf.Max(1, n), zBaseRear = zr, top = hi };
            m.beakTop = BeakTop(new SculptField(ToArray(h.SkullSolid())), m.zBase);
            m.zOverhang = float.MaxValue;
            foreach (Vector3 v in p.Pos) if (v.y > m.beakTop + 0.5f * h.crests[k].height) m.zOverhang = Mathf.Min(m.zOverhang, v.z);
            return m;
        }

        static SculptShape[] ToArray(IReadOnlyList<SculptShape> l) { var a = new SculptShape[l.Count]; for (int i = 0; i < a.Length; i++) a[i] = l[i]; return a; }

        static string CrestProblem(CrestBeakHead h)
        {
            for (int k = 0; k < h.crests.Length; k++)
            {
                CrestMeasure m = Measure(h, k);
                Vector3[] spine = h.CrestSpine(k);
                float rise = m.top - m.beakTop, H = h.crests[k].height;
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
            CrestBeakHead.CrestSinkForTest = 0.5f;
            try
            {
                string p = CrestProblem(Part(Species()));
                Assert.IsNotNull(p, "crests sunk half their height must fail the above-the-beak-line test");
                StringAssert.Contains("above the beak line", p);
            }
            finally { CrestBeakHead.CrestSinkForTest = 0f; }
        }

        // Measured on the mesh: the rear crest (the lower along) stands taller above the beak than the front one.
        static string RearProblem(CrestBeakHead h)
        {
            int rear = h.crests[0].along < h.crests[1].along ? 0 : 1, front = 1 - rear;
            CrestMeasure r = Measure(h, rear), f = Measure(h, front);
            TestContext.Out.WriteLine($"rear crest rises {r.top - r.beakTop:F3} at z {r.zBase:F3}, front {f.top - f.beakTop:F3} at z {f.zBase:F3}");
            if (!(r.zBase < f.zBase)) return "the rear crest is not behind the front one";
            if (!(r.top - r.beakTop > f.top - f.beakTop)) return $"the rear crest ({r.top - r.beakTop:F3}) is not taller than the front one ({f.top - f.beakTop:F3})";
            return null;
        }

        [Test] public void Head_Crests_RearIsLarger()
        {
            CrestBeakHead h = Part(Species());
            Assert.Greater(h.crests[0].height, h.crests[1].height, "the defaults: rear (crest 0) larger");
            Assert.IsNull(RearProblem(h));
        }

        [Test] public void Red_Crests_FrontLarger()
        {
            CrestBeakHead head = Head();
            // the two crests' shapes swapped, each kept where it stands
            CrestDef r = head.crests[0], f = head.crests[1];
            head.crests[0] = f; head.crests[0].along = r.along;
            head.crests[1] = r; head.crests[1].along = f.along;
            Assert.IsNotNull(RearProblem(Part(Species(head))), "a front crest taller than the rear must fail");
        }

        // The blade's edges seen from the side. Along the centre line from 0.3 to 0.97 of its length (120 stations, clear of the flared foot; rays
        // in the side plane square to the centre line, its tangent taken from the neighbouring points so the rays do not
        // jump at the joins) the distance to the silhouette is measured on both edges (0.05 mm steps). Then, for both
        // edges before anything else:
        //   ripple   each station's distance minus the mean of the two 8 stations (~5 % of the length) either side: a
        //            smooth taper leaves ~0, a chain of blended blobs a scallop (bulge over each blob, waist at each
        //            join) - more than kRipple anywhere bulges;
        //   widening the distance growing toward the tip by more than kRipple between neighbouring stations bulges too;
        // and only then the tip: the half chord at 0.95 must be < 0.25 x the one at 0.25 (a point, not a blunt end).
        internal const float kRipple = 0.00045f;

        internal static string SmoothProblem(CrestBeakHead h, out float worstRipple)
        {
            const int N = 120, K = 8;
            worstRipple = 0f;
            string bump = null, blunt = null;
            for (int k = 0; k < h.crests.Length; k++)
            {
                var f = new SculptField(ToArray(h.CrestSolid(k)));
                Vector3[] sp = h.CrestSpine(k);
                var cum = new float[sp.Length];
                for (int i = 1; i < sp.Length; i++) cum[i] = cum[i - 1] + (sp[i] - sp[i - 1]).magnitude;
                float len = cum[sp.Length - 1];
                Vector3 Tan(int i) => (sp[Mathf.Min(i + 1, sp.Length - 1)] - sp[Mathf.Max(i - 1, 0)]).normalized;
                float Half(float u, float side)
                {
                    float s = u * len; int i = 1;
                    while (i < sp.Length - 1 && cum[i] < s) i++;
                    float w = (s - cum[i - 1]) / Mathf.Max(1e-9f, cum[i] - cum[i - 1]);
                    Vector3 p = Vector3.Lerp(sp[i - 1], sp[i], w), t = Vector3.Lerp(Tan(i - 1), Tan(i), w).normalized;
                    Vector3 n = new Vector3(0f, -t.z, t.y) * side;
                    float e = 0f;
                    while (e < 0.2f && f.Eval(p + n * e) < 0f) e += 0.00005f;
                    return e;
                }
                float root = Mathf.Max(Half(0.25f, 1f), Half(0.25f, -1f));
                for (int side = -1; side <= 1; side += 2)
                {
                    var e = new float[N + 1];
                    for (int i = 0; i <= N; i++) e[i] = Half(Mathf.Lerp(0.3f, 0.97f, (float)i / N), side);
                    float ripple = 0f, rippleAt = 0f, widen = 0f, widenAt = 0f;
                    for (int i = K; i <= N - K; i++)
                    {
                        float r = Mathf.Abs(e[i] - 0.5f * (e[i - K] + e[i + K]));
                        if (r > ripple) { ripple = r; rippleAt = Mathf.Lerp(0.3f, 0.97f, (float)i / N); }
                    }
                    for (int i = 1; i <= N; i++)
                        if (e[i] - e[i - 1] > widen) { widen = e[i] - e[i - 1]; widenAt = Mathf.Lerp(0.3f, 0.97f, (float)i / N); }
                    float tip = Half(0.95f, side);
                    string edge = side > 0 ? "front" : "back";
                    worstRipple = Mathf.Max(worstRipple, ripple);
                    TestContext.Out.WriteLine($"crest {k} {edge} edge: half chord at 0.25 {Half(0.25f, side) * 1000f:F2} mm, at 0.95 {tip * 1000f:F2} mm; ripple {ripple * 1000f:F3} mm at u {rippleAt:F2}, widening {widen * 1000f:F3} mm at u {widenAt:F2} (limit {kRipple * 1000f:F2} mm)");
                    if (bump == null && ripple > kRipple) bump = $"crest {k}: its {edge} edge bulges - a {ripple * 1000f:F2} mm ripple at u {rippleAt:F2} (scallops in the silhouette)";
                    if (bump == null && widen > kRipple) bump = $"crest {k}: its {edge} edge bulges {widen * 1000f:F2} mm wider toward the tip at u {widenAt:F2}";
                    if (blunt == null && tip > 0.25f * root) blunt = $"crest {k}: a blunt tip (half chord {tip:F4} at 0.95 of its length vs {root:F4} at 0.25)";
                }
            }
            return bump ?? blunt;
        }

        internal static string SmoothProblem(CrestBeakHead h) => SmoothProblem(h, out _);

        [Test] public void Head_Crests_SmoothSharpBlades()
        {
            Assert.IsNull(SmoothProblem(Part(Species()), out float ripple));
            Assert.Less(ripple, 0.5f * kRipple, "the blades are smooth with a margin (ripple under half the limit)");
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
        static string StepProblem(CreatureSpecies sp, float limit, out float worst)
        {
            CrestBeakHead h = Part(sp);
            Matrix4x4 toHead = SkullFrame(sp).inverse;
            Mesh mesh = FlowKit.Mesh(sp);
            Vector3[] v = mesh.vertices; int[] tri = mesh.triangles;
            var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);
            Object.DestroyImmediate(mesh);
            MaterialSlots slots = MaterialSlots.For(sp);
            int crest = slots.SlotOf("crest");
            var hv = new Vector3[v.Length];
            var use = new bool[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                hv[i] = toHead.MultiplyPoint3x4(v[i]);
                int part = Mathf.RoundToInt(uv1[i].x);
                use[i] = part != crest && part != PartMeshEmitter.PartEye && part != PartMeshEmitter.PartLeg && part != PartMeshEmitter.PartFoot
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
                if (top[s] > bulgeTop || bot[s] < bulgeBot)
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
                float d = Mathf.Max(Mathf.Abs(top[s] - top[s - 1]), Mathf.Abs(bot[s] - bot[s - 1]));
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

        [Test] public void Head_Jaw_ClosedAtRest_OpensOnCall()
        {
            CreatureSpecies sp = Species();
            CrestBeakHead h = Part(sp);
            foreach (float size in new[] { 1f, 0.9f, 1.1f })
            {
                CreatureSim sim = FlowKit.Sim(sp, size, seed: 7);
                CrestBeakHeadRuntime rt = Runtime(sim);
                Assert.IsNotNull(rt, "the head has a runtime");
                Assert.AreEqual(0f, rt.Gape, "closed at t = 0");
                float T = Mathf.Sqrt(size);
                int frames = Mathf.CeilToInt((h.callEverySeconds.y + h.callSeconds) * T / FlowKit.kDt) + 2;
                float most = 0f; bool closedAfter = false; float openedAt = -1f;
                for (int f = 0; f < frames; f++)
                {
                    sim.Tick(FlowKit.kDt, new Vector3(0f, 0f, 3f));
                    most = Mathf.Max(most, rt.Gape);
                    if (most >= 0.9f * h.gapeDeg && openedAt < 0f) openedAt = f * FlowKit.kDt;
                    if (most >= 0.9f * h.gapeDeg && rt.Gape == 0f) { closedAfter = true; break; }
                }
                TestContext.Out.WriteLine($"size {size}: widest {most:F1} deg (gapeDeg {h.gapeDeg}) first at {openedAt:F2} s, closed again: {closedAfter}");
                Assert.GreaterOrEqual(most, 0.9f * h.gapeDeg, $"size {size}: opens on a call");
                Assert.IsTrue(closedAfter, $"size {size}: closes again after the call");
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
        static string JawThroughSkull(CreatureSpecies sp, out float frontIn, out float rearOut)
        {
            CrestBeakHead h = Part(sp);
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            CrestBeakHeadRuntime rt = Runtime(sim);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            Matrix4x4 rest = SkullFrame(sp);
            var skull = new SculptField(ToArray(h.SkullSolid())); var head = new SculptField(ToArray(h.HeadSolid()));
            SculptPiece jaw = h.Piece(1);
            float zt = 0.5f * h.skullLength + h.beakLength, zFront = h.Hinge.z + 0.4f * (zt - h.Hinge.z);
            frontIn = 0f; rearOut = 0f;
            foreach (float frac in new[] { 0.25f, 0.5f, 1f })
            {
                rt.GapeOverride = frac * h.gapeDeg;
                Matrix4x4[] bones = FlowKit.Bones(sim);
                Matrix4x4 jawToSkull = (bones[0] * bind[0] * rest).inverse * bones[rt.JawBone] * bind[rt.JawBone] * rest;
                foreach (Vector3 p in jaw.Pos)
                {
                    Vector3 q = jawToSkull.MultiplyPoint3x4(p);
                    if (p.z > zFront) frontIn = Mathf.Max(frontIn, -skull.Eval(q));
                    else if (p.z < h.Hinge.z) rearOut = Mathf.Max(rearOut, head.Eval(q));   // out of the closed head's skin
                }
            }
            TestContext.Out.WriteLine($"gape 1/4, 1/2, full: jaw front deepest in the skull {frontIn * 1000f:F1} mm, jaw rear farthest out of the head {rearOut * 1000f:F1} mm");
            if (frontIn > 0.001f) return $"the jaw's front runs {frontIn * 1000f:F1} mm into the skull";
            if (rearOut > 0.1f * h.skullDepth) return $"the jaw's rear pokes {rearOut * 1000f:F1} mm out of the head";
            return null;
        }

        [Test] public void Head_Jaw_FullGape_ClearOfTheSkull()
        {
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
