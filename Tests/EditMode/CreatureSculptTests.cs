using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's Sculpt part (a signed-distance solid polygonised by surface nets, SculptMesher): validation; the field's
    // shapes and ops (smooth union / subtraction / intersection, mirror images, an Intersect trimming by both images);
    // the mesh is deterministic (two builds, byte-equal), closed (every edge in an even number of triangles once the
    // seam duplicates are welded), never inverted (every triangle faces the field's gradient at its centre and agrees
    // with its vertex normals), its vertices on the surface with normals along the field's gradient (no glow), a sphere
    // comes out round; the first build is bounded and every later one is the cached piece; its vertices are rigid to
    // one bone (its joint's or the followed Jaw's) and move rigidly through a gape; tooth rows sit on the lip. The
    // Crocodile's skull and lower jaw are the real cases. RED: the checks bite on a flipped triangle, a missing one, a
    // turned normal and a vertex off the surface. The KomodoDragon's head and jaw are checked alike.
    public class CreatureSculptTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static SpeciesCtx Ctx(int joints, bool soft = false, params CreaturePart[] parts) => new SpeciesCtx(parts, new CreatureModule[0], joints, soft);

        static SculptShape Ellipsoid(Vector3 c, Vector3 r, SculptOp op = SculptOp.Union, float k = 0f, bool mirror = false) =>
            new SculptShape { kind = SculptKind.Ellipsoid, op = op, centre = c, size = r, blend = k, mirror = mirror };

        static Sculpt Ball(float r = 0.05f, float cell = 0.005f) => new Sculpt
        {
            id = "s", at = new PartSocket(0, 0, Vector3.zero), cell = cell,
            shapes = new[] { Ellipsoid(Vector3.zero, new Vector3(r, r, r)) },
        };

        // A head-like test solid: a flattened capsule, a mirrored bulge, a carved socket pair, trimmed by a plane.
        static Sculpt Head() => new Sculpt
        {
            id = "h", at = new PartSocket(0, 0, Vector3.zero), cell = 0.005f,
            shapes = new[]
            {
                new SculptShape { kind = SculptKind.RoundCone, op = SculptOp.Union, centre = new Vector3(0f, 0f, -0.05f), end = new Vector3(0f, -0.01f, 0.12f), size = new Vector3(0.05f, 0.025f, 0f), squash = 0.5f },
                Ellipsoid(new Vector3(0.04f, 0.0f, -0.04f), new Vector3(0.02f, 0.02f, 0.035f), SculptOp.Union, 0.015f, true),
                new SculptShape { kind = SculptKind.Box, op = SculptOp.Union, centre = new Vector3(0f, 0.02f, -0.05f), size = new Vector3(0.035f, 0.015f, 0.03f), round = 0.008f, blend = 0.01f },
                Ellipsoid(new Vector3(0.025f, 0.035f, -0.03f), new Vector3(0.01f, 0.008f, 0.012f), SculptOp.Subtract, 0.005f, true),
                new SculptShape { kind = SculptKind.Plane, op = SculptOp.Intersect, centre = new Vector3(0f, -0.012f, 0f), rotationDeg = new Vector3(180f, 0f, 0f), blend = 0.004f },
            },
        };

        static List<Sculpt> CrocSculpts()
        {
            var r = new List<Sculpt>();
            foreach (CreaturePart p in CreatureKitAssets.Load("Crocodile").Compiled.Parts) if (p is Sculpt s) r.Add(s);
            Assert.AreEqual(2, r.Count, "the crocodile's skull and lower jaw are Sculpts");
            return r;
        }

        static List<Sculpt> SculptsOf(string species)
        {
            var r = new List<Sculpt>();
            foreach (CreaturePart p in CreatureKitAssets.Load(species).Compiled.Parts) if (p is Sculpt s) r.Add(s);
            return r;
        }

        static IEnumerable<Sculpt> Cases()
        {
            yield return Ball();
            yield return Head();
            foreach (Sculpt s in CrocSculpts()) yield return s;
            List<Sculpt> komodo = SculptsOf("KomodoDragon");
            Assert.AreEqual(2, komodo.Count, "the Komodo dragon's head and lower jaw are Sculpts");
            foreach (Sculpt s in komodo) yield return s;
        }

        // ------------------------------------------------------------ validation and the field

        [Test] public void Sculpt_Validate_RejectsBadData()
        {
            Sculpt ok = Head();
            Assert.IsNull(ok.Validate(Ctx(6)));
            Assert.AreEqual(0, ok.BoneCount(Ctx(6)), "rigid: no bones of its own");
            Assert.IsNotNull(ok.Validate(Ctx(6, true)), "needs a spine body");
            var bad = (Sculpt)ok.Clone(); bad.shapes = new SculptShape[0]; Assert.IsNotNull(bad.Validate(Ctx(6)), "no shapes");
            bad = (Sculpt)ok.Clone(); bad.cell = 0f; Assert.IsNotNull(bad.Validate(Ctx(6)), "no cell");
            bad = (Sculpt)ok.Clone(); bad.at.joint = 6; Assert.IsNotNull(bad.Validate(Ctx(6)), "joint out of range");
            bad = (Sculpt)ok.Clone(); bad.shapes[0].op = SculptOp.Subtract; Assert.IsNotNull(bad.Validate(Ctx(6)), "carves before any union");
            bad = (Sculpt)ok.Clone(); bad.shapes[4].op = SculptOp.Union; Assert.IsNotNull(bad.Validate(Ctx(6)), "an unbounded plane added");
            bad = (Sculpt)ok.Clone(); bad.shapes[1].size.y = 0f; Assert.IsNotNull(bad.Validate(Ctx(6)), "a flat ellipsoid");
            bad = (Sculpt)ok.Clone(); bad.shapes[0].end = bad.shapes[0].centre; Assert.IsNotNull(bad.Validate(Ctx(6)), "a round cone shorter than its radius change");
            bad = (Sculpt)ok.Clone(); bad.shapes[2].round = 0.5f; Assert.IsNotNull(bad.Validate(Ctx(6)), "box rounding past its size");
            bad = (Sculpt)ok.Clone(); bad.teeth = new[] { new SculptTeeth { count = 0, length = 0.01f, radius = 0.002f, direction = Vector3.down } };
            Assert.IsNotNull(bad.Validate(Ctx(6)), "an empty tooth row");
            bad = (Sculpt)ok.Clone(); bad.follow = "nope"; Assert.IsNotNull(bad.Validate(Ctx(6)), "follows a part that is not there");
            var jaw = new Jaw { id = "j", at = new PartSocket(0, 0, Vector3.zero), length = 0.1f, keys = new[] { new LoftKey { t = 0f, halfWidth = 0.02f, halfHeight = 0.01f, squareTop = 2f, squareBottom = 2f }, new LoftKey { t = 1f, halfWidth = 0.02f, halfHeight = 0.01f, squareTop = 2f, squareBottom = 2f } } };
            bad = (Sculpt)ok.Clone(); bad.follow = "j"; Assert.IsNull(bad.Validate(Ctx(6, false, jaw, bad)), "follows a Jaw");
            var refs = new List<string>(); bad.CollectReferences(refs);
            Assert.AreEqual(1, refs.Count, "names the jaw it follows"); Assert.AreEqual("j", refs[0]);
        }

        [Test] public void Field_Shapes_And_Ops()
        {
            var f = new SculptField(new[] { Ellipsoid(Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f)) });
            Assert.AreEqual(-0.1f, f.Eval(Vector3.zero), 1e-5f, "a sphere: minus its radius at the centre");
            Assert.AreEqual(0.05f, f.Eval(new Vector3(0.15f, 0f, 0f)), 1e-5f, "distance outside");
            // smooth union: never above the plain minimum, equal to it far from the seam
            Assert.LessOrEqual(SculptField.SMin(0.01f, 0.012f, 0.02f), 0.01f);
            Assert.AreEqual(0.01f, SculptField.SMin(0.01f, 0.05f, 0.02f), 0f);
            // subtract a mirrored pair: both sockets carved
            var g = new SculptField(new[] { Ellipsoid(Vector3.zero, new Vector3(0.1f, 0.05f, 0.1f)), Ellipsoid(new Vector3(0.05f, 0.05f, 0f), new Vector3(0.02f, 0.02f, 0.02f), SculptOp.Subtract, 0f, true) });
            Assert.Greater(g.Eval(new Vector3(0.05f, 0.04f, 0f)), 0f, "the socket at +x is carved");
            Assert.Greater(g.Eval(new Vector3(-0.05f, 0.04f, 0f)), 0f, "and its mirror image at -x");
            Assert.AreEqual(g.Eval(new Vector3(0.03f, 0.01f, 0.02f)), g.Eval(new Vector3(-0.03f, 0.01f, 0.02f)), 1e-6f, "symmetric");
            // a mirrored Intersect trims by both images: a plane leaning out on +x cuts both sides
            var h = new SculptField(new[] { Ellipsoid(Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f)),
                new SculptShape { kind = SculptKind.Plane, op = SculptOp.Intersect, centre = new Vector3(0.05f, 0f, 0f), rotationDeg = new Vector3(0f, 0f, -90f), mirror = true } });
            Assert.Greater(h.Eval(new Vector3(0.08f, 0f, 0f)), 0f, "trimmed at +x");
            Assert.Greater(h.Eval(new Vector3(-0.08f, 0f, 0f)), 0f, "trimmed at -x");
            Assert.Less(h.Eval(Vector3.zero), 0f, "the middle stays");
        }

        // ------------------------------------------------------------ the mesh

        static void Same<T>(T[] a, T[] b, string what)
        {
            Assert.AreEqual(a.Length, b.Length, what + ": count");
            for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) Assert.Fail($"{what}: element {i} differs ({a[i]} vs {b[i]})");
        }

        static SculptPiece Fresh(Sculpt s) { SculptMesher.ClearCache(); return SculptMesher.For(s); }

        [Test] public void Mesh_Deterministic()
        {
            foreach (Sculpt s in Cases())
            {
                SculptPiece a = Fresh(s), b = Fresh(s);
                Assert.AreNotSame(a, b, "two real builds");
                Same(a.Pos, b.Pos, s.id + ": positions");
                Same(a.Nrm, b.Nrm, s.id + ": normals");
                Same(a.V, b.V, s.id + ": uv");
                Same(a.Tris, b.Tris, s.id + ": triangles");
                Assert.AreEqual(a.Teeth.Length, b.Teeth.Length, s.id + ": teeth");
                for (int i = 0; i < a.Teeth.Length; i++) Assert.AreEqual(a.Teeth[i].basePt, b.Teeth[i].basePt, s.id + ": tooth " + i);
            }
        }

        // Undirected edges used an odd number of times once vertices at the same place are welded (0 = closed).
        internal static int OpenEdges(Vector3[] pos, int[] tris)
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
            int odd = 0;
            foreach (int c in count.Values) if ((c & 1) != 0) odd++;
            return odd;
        }

        // Triangles whose face points against the field's gradient at their centre, or against their vertex normals.
        internal static int Inverted(SculptField f, Vector3[] pos, Vector3[] nrm, int[] tris)
        {
            int bad = 0;
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 a = pos[tris[t]], b = pos[tris[t + 1]], c = pos[tris[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Vector3 g = f.Gradient((a + b + c) / 3f, 1e-4f);
                if (!(Vector3.Dot(n, nrm[tris[t]] + nrm[tris[t + 1]] + nrm[tris[t + 2]]) > 0f) || Vector3.Dot(n, g) < 0f) bad++;
            }
            return bad;
        }

        // The worst angle (deg) between a vertex normal and the field's gradient there, and the farthest vertex from the
        // surface (|field|, m).
        internal static void NormalsAndSurface(SculptField f, Vector3[] pos, Vector3[] nrm, out float worstDeg, out float worstOff) =>
            NormalsAndSurface(f, pos, nrm, out worstDeg, out _, out worstOff);

        internal static void NormalsAndSurface(SculptField f, Vector3[] pos, Vector3[] nrm, out float worstDeg, out float meanDeg, out float worstOff)
        {
            worstDeg = 0f; worstOff = 0f; meanDeg = 0f;
            for (int i = 0; i < pos.Length; i++)
            {
                float a = Vector3.Angle(nrm[i], f.Gradient(pos[i], 1e-4f));
                worstDeg = Mathf.Max(worstDeg, a); meanDeg += a / pos.Length;
                worstOff = Mathf.Max(worstOff, Mathf.Abs(f.Eval(pos[i])));
            }
        }

        [Test] public void Mesh_Closed_NotInverted_OnTheSurface_NormalsAlongTheGradient()
        {
            foreach (Sculpt s in Cases())
            {
                SculptPiece p = Fresh(s);
                SculptField f = s.Field();
                int open = OpenEdges(p.Pos, p.Tris), inv = Inverted(f, p.Pos, p.Nrm, p.Tris);
                NormalsAndSurface(f, p.Pos, p.Nrm, out float deg, out float mean, out float off);
                TestContext.Out.WriteLine($"{s.id}: {p.Pos.Length} verts, {p.Tris.Length / 3} tris, open edges {open}, inverted {inv}, folds turned {p.Folds}, worst normal {deg:F2} deg (mean {mean:F3}), farthest off the surface {off * 1000f:F3} mm (cell {s.cell * 1000f:F1} mm)");
                Assert.AreEqual(0, open, s.id + ": closed");
                Assert.AreEqual(0, inv, s.id + ": no triangle against the gradient or its normals");
                Assert.LessOrEqual(p.Folds, p.Tris.Length / 3 / 500, s.id + ": folds over a crest are rare (<= 0.2 %)");
                // the normal is the gradient a hair from the vertex: identical on smooth skin, within degrees in a crease
                // finer than a cell (a nostril's rim)
                Assert.Less(mean, 0.25f, s.id + ": vertex normals are the field's gradient");
                Assert.Less(deg, 10f, s.id + ": no normal turned away (glow)");
                Assert.Less(off, 0.25f * s.cell, s.id + ": vertices on the surface (a vertex stays in its cell: surface nets chamfer a corner sharper than a cell a little)");
                Assert.LessOrEqual(p.Pos.Length, 5000, s.id + ": vertex budget");
            }
        }

        [Test] public void Mesh_Sphere_IsRound()
        {
            const float r = 0.05f;
            SculptPiece p = Fresh(Ball(r, 0.005f));
            float worst = 0f, worstN = 0f;
            for (int i = 0; i < p.Pos.Length; i++)
            {
                worst = Mathf.Max(worst, Mathf.Abs(p.Pos[i].magnitude - r));
                worstN = Mathf.Max(worstN, Vector3.Angle(p.Nrm[i], p.Pos[i]));
            }
            TestContext.Out.WriteLine($"sphere r {r}: {p.Pos.Length} verts, worst radius error {worst * 1000f:F3} mm, worst normal {worstN:F3} deg");
            Assert.Less(worst, 0.05f * 0.005f, "on the sphere (within 5 % of a cell)");
            Assert.Less(worstN, 0.5f, "radial normals");
            Assert.Greater(p.Pos.Length, 300, "a real mesh");
        }

        // The seam: v round the long axis from the belly; no triangle spans more than half a turn (seam duplicates at
        // v + 1) - except round the axis itself (a pole at the tip), within 1.5 cells of it.
        [Test] public void Mesh_SeamUV_NoTriangleWraps()
        {
            foreach (Sculpt s in Cases())
            {
                SculptPiece p = Fresh(s);
                SculptField f = s.Field();
                float cy = 0.5f * (f.Lo.y + f.Hi.y);
                int poles = 0;
                for (int t = 0; t < p.Tris.Length; t += 3)
                {
                    float a = p.V[p.Tris[t]], b = p.V[p.Tris[t + 1]], c = p.V[p.Tris[t + 2]];
                    if (Mathf.Max(a, Mathf.Max(b, c)) - Mathf.Min(a, Mathf.Min(b, c)) <= 0.5f) continue;
                    float r = float.MaxValue;
                    for (int e = 0; e < 3; e++) { Vector3 q = p.Pos[p.Tris[t + e]]; r = Mathf.Min(r, new Vector2(q.x, q.y - cy).magnitude); }
                    Assert.Less(r, 1.5f * s.cell, $"{s.id}: triangle {t / 3} wraps the seam away from the axis");
                    poles++;
                }
                TestContext.Out.WriteLine($"{s.id}: {poles} triangles round the axis");
            }
        }

        [Test] public void Build_Time_Bounded_Cached()
        {
            foreach (Sculpt s in CrocSculpts())
            {
                Fresh(s);   // JIT warm-up
                double best = double.MaxValue;
                for (int i = 0; i < 3; i++) best = System.Math.Min(best, Fresh(s).BuildMs);
                SculptPiece first = SculptMesher.For(s);
                var sw = Stopwatch.StartNew();
                SculptPiece again = SculptMesher.For(s);
                double cached = sw.Elapsed.TotalMilliseconds;
                TestContext.Out.WriteLine($"{s.id}: first build {best:F2} ms (best of 3), cached {cached * 1000.0:F1} us");
                Assert.AreSame(first, again, s.id + ": the second build is the cached piece");
                Assert.Less(best, 40.0, s.id + ": one build, once per species");
                Assert.Less(cached, 0.5, s.id + ": a cached build is a lookup");
            }
        }

        // ------------------------------------------------------------ on the creature

        [Test] public void OnTheCrocodile_RigidToItsBone_MovesRigidlyThroughTheGape()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Crocodile").Compiled.Runtime;
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            trash.Add(mesh);
            Vector3[] v = mesh.vertices;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
            mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            int skullBone = -1, jawBone = -1;
            foreach (CreaturePart p in parts)
                if (p is Sculpt s) { if (string.IsNullOrEmpty(s.follow)) skullBone = s.Bone(sp); else jawBone = s.Bone(sp); }
            Assert.AreEqual(0, skullBone, "the skull rides the head joint's bone");
            Assert.Greater(jawBone, sp.JointCount - 1, "the lower jaw rides the Jaw's own bone");
            // every vertex skinned to one of them is rigid (weight 1)
            var jaw = new List<int>();
            for (int i = 0; i < v.Length; i++)
            {
                Vector4 w = uv2[i];
                if ((int)w.x == jawBone) { Assert.AreEqual(1f, w.z, 0f, "rigid"); Assert.AreEqual(jawBone, (int)w.y); jaw.Add(i); }
            }
            Assert.Greater(jaw.Count, 1000, "the lower jaw and its teeth");
            // posed through a gape the jaw's vertices keep their distances
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale, 1);
            JawRuntime jr = null;
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is JawRuntime j) jr = j;
            var world = new Matrix4x4[sp.BoneCount];
            float worst = 0f, widest = 0f;
            int a0 = jaw[0], a1 = jaw[jaw.Count / 2], a2 = jaw[jaw.Count - 1];
            float d01 = (v[a0] - v[a1]).magnitude * sp.scale, d12 = (v[a1] - v[a2]).magnitude * sp.scale;
            for (int t = 0; t < 60 * 30; t++)
            {
                sim.Tick(1f / 60f, sim.Joints[0]);
                if (t % 10 != 0) continue;
                sim.WriteBoneWorld(world, 0);
                Matrix4x4 m = world[jawBone] * bind[jawBone];
                Vector3 p0 = m.MultiplyPoint3x4(v[a0]), p1 = m.MultiplyPoint3x4(v[a1]), p2 = m.MultiplyPoint3x4(v[a2]);
                worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs((p0 - p1).magnitude - d01), Mathf.Abs((p1 - p2).magnitude - d12)));
                widest = Mathf.Max(widest, jr.Angle);
            }
            TestContext.Out.WriteLine($"widest gape {widest:F1} deg, worst distance change {worst * 1000f:F4} mm");
            Assert.Greater(widest, 1f, "the window covers a gape");
            Assert.Less(worst, 1e-4f * sp.scale, "rigid");
        }

        // Each tooth sits on the solid's surface: its base is sunk `sink` of its length below where its line leaves the solid.
        [Test] public void Teeth_SitOnTheLip()
        {
            foreach (Sculpt s in CrocSculpts())
            {
                SculptPiece p = Fresh(s);
                SculptField f = s.Field();
                Assert.Greater(p.Teeth.Length, 10, s.id + ": tooth rows");
                float worst = 0f;
                for (int i = 0; i < p.Teeth.Length; i++)
                {
                    SculptPiece.Tooth t = p.Teeth[i];
                    float sink = 0f;
                    foreach (SculptTeeth row in s.teeth) sink = row.sink;   // every crocodile row sinks alike
                    Vector3 lip = t.basePt + t.dir * (sink * t.length);
                    worst = Mathf.Max(worst, Mathf.Abs(f.Eval(lip)));
                    Assert.Less(f.Eval(t.basePt), 0f, $"{s.id}: tooth {i}'s root is in the gum");
                    Assert.Greater(f.Eval(t.basePt + t.dir * t.length), 0f, $"{s.id}: tooth {i}'s tip is out of the jaw");
                }
                TestContext.Out.WriteLine($"{s.id}: {p.Teeth.Length} teeth, worst lip point off the surface {worst * 1000f:F3} mm");
                Assert.Less(worst, 0.25f * s.cell, s.id + ": on the lip line");
            }
        }

        // ------------------------------------------------------------ RED

        [Test] public void Red_Checks_Bite()
        {
            Sculpt s = Head();
            SculptPiece p = Fresh(s);
            SculptField f = s.Field();
            var flipped = (int[])p.Tris.Clone();
            int t = flipped[1]; flipped[1] = flipped[2]; flipped[2] = t;
            Assert.AreEqual(1, Inverted(f, p.Pos, p.Nrm, flipped), "one flipped triangle is found");
            var missing = new int[p.Tris.Length - 3];
            System.Array.Copy(p.Tris, 3, missing, 0, missing.Length);
            Assert.Greater(OpenEdges(p.Pos, missing), 0, "a missing triangle opens the mesh");
            var turned = (Vector3[])p.Nrm.Clone(); turned[0] = Quaternion.AngleAxis(10f, Vector3.up) * Vector3.Cross(turned[0], Vector3.right).normalized;
            NormalsAndSurface(f, p.Pos, turned, out float deg, out _);
            Assert.Greater(deg, 1f, "a turned normal is found");
            var moved = (Vector3[])p.Pos.Clone(); moved[0] += p.Nrm[0] * s.cell;
            NormalsAndSurface(f, moved, p.Nrm, out _, out float off);
            Assert.Greater(off, 0.1f * s.cell, "a vertex off the surface is found");
        }
    }
}
