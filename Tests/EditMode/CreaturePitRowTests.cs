using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // PitRow (Apex redesign §1 breathing holes, §5.7): recessed pits with pale raised rims on a FlowBody's surface.
    // Fixture: CreatureFlowBodyTests' body (steep neck, chest), a mirrored PitRow along the throat - at the part's
    // defaults the pits are oblique slits (aspect, slantDeg) growing toward the head (sizeGrowth), so every size check
    // below is per pit (PitRow.PitRadius).
    public class CreaturePitRowTests
    {
        static PitRow Pits(bool mirrored = true) => new PitRow
        {
            id = "spiracles", mirror = mirrored, at = new PartSocket(0, mirrored ? 0 : 1, Vector3.zero),
            fromU = 0.18f, toU = 0.4f, angleDeg = -35f, count = 6, radius = 0.012f, depth = 0.6f, rim = 0.35f,
        };

        internal static CreatureSpecies Species(PitRow pits)
        {
            var parts = new List<CreaturePart> { CreatureFlowBodyTests.Body(), CreatureFlowBodyTests.HindLeg(), CreatureKitComposeTests.TwoEyes() };
            if (pits != null) parts.Add(pits);
            return FlowKit.Build("PitRowFixture", 1f, parts, new CreatureModule[] { new Walk { speed = 0.8f, turnRateDeg = 160f, accel = 2f, turnSlowdown = 0.5f } });
        }

        sealed class Rest
        {
            public Vector3[] V; public int[] T; public List<Vector2> Uv1 = new List<Vector2>(); public int Pit;   // first pit vertex
        }

        static Rest Mesh(PitRow pits)
        {
            int bodyOnly;
            Mesh plain = FlowKit.Mesh(Species(null));
            bodyOnly = plain.vertexCount;
            Object.DestroyImmediate(plain);
            Mesh mesh = FlowKit.Mesh(Species(pits));
            var r = new Rest { V = mesh.vertices, T = mesh.triangles, Pit = bodyOnly };
            mesh.GetUVs(1, r.Uv1);
            Object.DestroyImmediate(mesh);
            return r;
        }

        // distance from p to triangle abc (Ericson, closest point on a triangle)
        static float DistToTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return (p - a).magnitude;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return (p - b).magnitude;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return (p - (a + ab * (d1 / (d1 - d3)))).magnitude;
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return (p - c).magnitude;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return (p - (a + ac * (d2 / (d2 - d6)))).magnitude;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).magnitude;
            float den = 1f / (va + vb + vc);
            return (p - (a + ab * (vb * den) + ac * (vc * den))).magnitude;
        }

        // distance from p to the body skin (the part-0 triangles of the body-only mesh)
        static float DistToBody(Rest r, Vector3 p)
        {
            float best = float.MaxValue;
            for (int k = 0; k < r.T.Length; k += 3)
            {
                int i0 = r.T[k], i1 = r.T[k + 1], i2 = r.T[k + 2];
                if (i0 >= r.Pit || i1 >= r.Pit || i2 >= r.Pit) continue;
                if (Mathf.RoundToInt(r.Uv1[i0].x) != PartMeshEmitter.PartBody || Mathf.RoundToInt(r.Uv1[i1].x) != PartMeshEmitter.PartBody || Mathf.RoundToInt(r.Uv1[i2].x) != PartMeshEmitter.PartBody) continue;
                best = Mathf.Min(best, DistToTri(p, r.V[i0], r.V[i1], r.V[i2]));
            }
            return best;
        }

        // null, or the first pit whose rim does not stand on the surface: its outer foot ring within 0.15 x its half width of
        // the body, every rim vertex within the lip's height + 0.15 x its half width.
        static string SurfaceProblem(PitRow pits)
        {
            Rest r = Mesh(pits);
            int pitCount = 2 * pits.count;
            for (int k = 0; k < pitCount; k++)
            {
                float R = pits.PitRadius(k % pits.count), tol = 0.15f * R;
                int cup = r.Pit + k * PitRow.VertsPerPit, rim = cup + PitRow.CupVertices;
                float foot = 0f;
                for (int j = 0; j < PitRow.Segs; j++)
                {
                    float d = DistToBody(r, r.V[rim + (PitRow.RimRings - 1) * (PitRow.Segs + 1) + j]);
                    foot = Mathf.Max(foot, d);
                    if (d > tol) return $"pit {k}: rim foot vertex {j} is {d / R:F2} radius off the surface (max {0.15f})";
                }
                for (int j = 0; j < PitRow.RimVertices; j++)
                {
                    float d = DistToBody(r, r.V[rim + j]);
                    if (d > (pits.rim + 0.15f) * R) return $"pit {k}: rim vertex {j} is {d / R:F2} radius off the surface";
                }
            }
            return null;
        }

        [Test] public void Pits_SitOnTheSurface()
        {
            Assert.IsNull(SurfaceProblem(Pits()));
        }

        [Test] public void Red_Pits_Float()
        {
            PitRow.SurfaceBiasForTest = 0.01f;   // 0.8 radius off
            try { Assert.IsNotNull(SurfaceProblem(Pits()), "pits floating off the surface must fail the surface test"); }
            finally { PitRow.SurfaceBiasForTest = 0f; }
        }

        // null, or what is wrong with the pit count: `perSide` pits each side, left (negative x) first, mirrored.
        static string CountProblem(PitRow p, int perSide)
        {
            Rest r = Mesh(p);
            int total = r.V.Length - r.Pit;
            if (total != 2 * perSide * PitRow.VertsPerPit) return $"{total} pit vertices, want {2 * perSide} pits x {PitRow.VertsPerPit}";
            int left = 0, right = 0;
            for (int k = 0; k < 2 * perSide; k++)
            {
                Vector3 pole = r.V[r.Pit + k * PitRow.VertsPerPit];   // the cup's deepest point
                if (pole.x < 0f) left++; else right++;
            }
            if (left != perSide || right != perSide) return $"{left} left / {right} right pits, want {perSide} each";
            for (int k = 0; k < perSide; k++)
            {
                Vector3 l = r.V[r.Pit + k * PitRow.VertsPerPit], q = r.V[r.Pit + (perSide + k) * PitRow.VertsPerPit];
                if (Mathf.Abs(-q.x - l.x) > 2e-3f || Mathf.Abs(q.y - l.y) > 2e-3f || Mathf.Abs(q.z - l.z) > 2e-3f) return $"pit {k} is not the mirror image of its right twin";
            }
            return null;
        }

        [Test] public void Pits_Count_SixEachSide() => Assert.IsNull(CountProblem(Pits(), 6));

        [Test] public void Red_Pits_Count()
        {
            PitRow five = Pits(); five.count = 5;
            StringAssert.Contains("pit vertices", CountProblem(five, 6), "five pits per side must not pass as six");
        }

        [Test] public void Pits_AreRecessed_RimIsRaised_PitKindAndPaleRim()
        {
            PitRow p = Pits(false);
            Rest r = Mesh(p);
            MaterialSlots slots = MaterialSlots.For(Species(p));
            int pit = slots.SlotOf("pit");
            Assert.GreaterOrEqual(pit, PartLayers.LegacyIds, "pit has a kit slot");
            Assert.IsTrue(PartLayers.Find("pit", out PartLayers.Entry e), "pit has a default row");
            Assert.Less(Mathf.Max(e.Row.tint.r, e.Row.tint.g, e.Row.tint.b), 0.1f, "near-black cup");
            for (int k = 0; k < p.count; k++)
            {
                float R = p.PitRadius(k);
                int cup = r.Pit + k * PitRow.VertsPerPit, rim = cup + PitRow.CupVertices;
                Assert.AreEqual(pit, Mathf.RoundToInt(r.Uv1[cup].x), "cup on the pit kind");
                float below = DistToBody(r, r.V[cup]);
                Assert.AreEqual(p.depth * R, below, 0.2f * R, $"pit {k} is {p.depth} radius deep");
                float top = 0f;
                for (int j = 0; j < PitRow.RimVertices; j++)
                {
                    Assert.AreEqual(PartMeshEmitter.PartBody, Mathf.RoundToInt(r.Uv1[rim + j].x), "rim in the body skin");
                    Assert.AreEqual(-1f, r.Uv1[rim + j].y, 1e-5f, "rim at the palest (belly) end");
                    top = Mathf.Max(top, DistToBody(r, r.V[rim + j]));
                }
                Assert.AreEqual(p.rim * R, top, 0.2f * R, $"pit {k}'s lip stands {p.rim} radius proud");
            }
        }

        // null, or how many pit triangles wind against their vertex normals (the body's own winding is the reference)
        static string WindingProblem(PitRow pits)
        {
            Rest r = Mesh(pits);
            Mesh mesh = FlowKit.Mesh(Species(pits));
            Vector3[] n = mesh.normals;
            Object.DestroyImmediate(mesh);
            int bad = 0, tris = 0, body = 0, bodyBad = 0;
            for (int k = 0; k < r.T.Length; k += 3)
            {
                int i0 = r.T[k], i1 = r.T[k + 1], i2 = r.T[k + 2];
                bool pit = i0 >= r.Pit;
                if (!pit && Mathf.RoundToInt(r.Uv1[i0].x) != PartMeshEmitter.PartBody) continue;
                Vector3 g = Vector3.Cross(r.V[i1] - r.V[i0], r.V[i2] - r.V[i0]);
                if (g.sqrMagnitude < 1e-14f) continue;
                bool wrong = Vector3.Dot(g, n[i0] + n[i1] + n[i2]) <= 0f;
                if (pit) { tris++; if (wrong) bad++; } else { body++; if (wrong) bodyBad++; }
            }
            Assert.Greater(tris, 100); Assert.Greater(body, 500);
            Assert.AreEqual(0, bodyBad, "reference: the body winds with its normals");
            return bad == 0 ? null : $"{bad} of {tris} pit triangles wind against their normals";
        }

        [Test] public void Pits_WindOutward() => Assert.IsNull(WindingProblem(Pits()));

        [Test] public void Red_Pits_Reversed()
        {
            PitRow.ReverseWindingForTest = true;
            try
            {
                StringAssert.Contains("wind against their normals", WindingProblem(Pits()));
            }
            finally { PitRow.ReverseWindingForTest = false; }
        }

        [Test] public void Pits_Bend_NoInvertedTriangles()
        {
            Assert.IsNull(KitChecks.MaxBendProblem(Species(Pits())));
        }

        // null, or the pit that leaves the posed body skin: the sim bent hard toward a far side target, then every rim foot
        // vertex within 0.5 x radius (the rearmost pit's half width) of the posed body triangles. bend = the sharpest joint
        // angle reached.
        static string PosedProblem(PitRow pits, out float bend)
        {
            CreatureSpecies sp = Species(pits);
            Mesh plain = FlowKit.Mesh(Species(null));
            int first = plain.vertexCount;
            Object.DestroyImmediate(plain);
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            bend = 0f;
            for (int t = 0; t < 240; t++)
            {
                sim.Tick(FlowKit.kDt, new Vector3(0.9f, 0f, 0.1f));
                for (int i = 1; i < sp.JointCount - 1; i++) bend = Mathf.Max(bend, Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1]));
            }
            var m = new CreaturePteroWingTests.Skinned(sp);
            m.Pose(sim);
            var posed = new Rest { V = m.P, T = m.T, Uv1 = m.Uv1, Pit = first };
            float R = pits.radius, worst = 0f; int worstK = -1;
            for (int k = 0; k < 2 * pits.count; k++)
            {
                int rim = first + k * PitRow.VertsPerPit + PitRow.CupVertices;
                for (int j = 0; j < PitRow.Segs; j++)
                {
                    float d = DistToBody(posed, m.P[rim + (PitRow.RimRings - 1) * (PitRow.Segs + 1) + j]);
                    if (d > worst) { worst = d; worstK = k; }
                }
            }
            return worst > 0.5f * R ? $"pit {worstK}: rim foot {worst / R:F2} radius off the posed skin" : null;
        }

        [Test] public void Pits_FollowTheSkin()
        {
            string problem = PosedProblem(Pits(), out float bend);
            TestContext.Out.WriteLine($"sharpest joint angle {bend:F1} deg");
            Assert.Greater(bend, 15f, "the pose really bends the body");
            Assert.IsNull(problem);
        }

        // Bend invariant: MaxBendProblem compares each triangle's posed orientation with its own rest orientation, so a
        // consistent reversal or a rigid bone choice (Red_Pits_FixedSkin) cannot bite it - only a skin that shears the pit
        // can. Every other ring on the tail bone does.
        [Test] public void Red_Pits_TornSkin_BendBites()
        {
            PitRow.TornSkinForTest = true;
            try { StringAssert.Contains("inverted", KitChecks.MaxBendProblem(Species(Pits())), "a sheared pit must be caught by the bend sweep"); }
            finally { PitRow.TornSkinForTest = false; }
        }

        [Test] public void Red_Pits_FixedSkin()
        {
            PitRow.RigidSkinForTest = true;
            try { StringAssert.Contains("off the posed skin", PosedProblem(Pits(), out _), "pits glued to joint 0 must leave a bent body"); }
            finally { PitRow.RigidSkinForTest = false; }
        }

        // ------------------------------------------------------------ slits growing toward the head (Apex face close-up)

        struct PitShape { public float halfWidth, halfLength, slantDeg; public Vector3 pole; }

        // One side's pits (the first `count` in the mesh), measured on the cup's edge ring: half length = half the
        // longest span across it, half width = half the shortest span between opposite vertices, and the long axis's
        // angle from the row's own line (the next pit's pole, the line of the body there).
        static PitShape[] Shapes(PitRow pits)
        {
            Rest r = Mesh(pits);
            int w = PitRow.Segs + 1, n = pits.count;
            var o = new PitShape[n];
            var axis = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                int edge = r.Pit + k * PitRow.VertsPerPit + PitRow.CupRings * w;
                float lo = float.MaxValue, hi = 0f;
                for (int j = 0; j < PitRow.Segs / 2; j++)
                {
                    Vector3 d = r.V[edge + j] - r.V[edge + j + PitRow.Segs / 2];
                    float m = 0.5f * d.magnitude;
                    if (m > hi) { hi = m; axis[k] = d.normalized; }
                    lo = Mathf.Min(lo, m);
                }
                o[k] = new PitShape { halfWidth = lo, halfLength = hi, pole = r.V[r.Pit + k * PitRow.VertsPerPit] };
            }
            for (int k = 0; k < n; k++)
            {
                Vector3 along = (k + 1 < n ? o[k + 1].pole - o[k].pole : o[k].pole - o[k - 1].pole).normalized;
                o[k].slantDeg = Mathf.Acos(Mathf.Min(1f, Mathf.Abs(Vector3.Dot(axis[k], along)))) * Mathf.Rad2Deg;
                TestContext.Out.WriteLine($"pit {k} (u {pits.PitU(k):F3}): half width {o[k].halfWidth * 1000f:F2} mm, half length {o[k].halfLength * 1000f:F2} mm (x {o[k].halfLength / o[k].halfWidth:F2}), long axis {o[k].slantDeg:F1} deg off the row");
            }
            return o;
        }

        // The pits grow toward the head: each one's half width larger than the next one back, the front one >= 1.3 x the rearmost.
        static string GrowthProblem(PitRow pits)
        {
            PitShape[] s = Shapes(pits);
            for (int k = 0; k + 1 < s.Length; k++)
                if (!(s[k].halfWidth > s[k + 1].halfWidth * 1.01f)) return $"the pits do not grow toward the head: pit {k} ({s[k].halfWidth * 1000f:F2} mm) is not larger than pit {k + 1} behind it ({s[k + 1].halfWidth * 1000f:F2} mm)";
            float ratio = s[0].halfWidth / s[s.Length - 1].halfWidth;
            return ratio >= 1.3f ? null : $"the pits do not grow toward the head enough: front / rear x {ratio:F2} (< 1.3)";
        }

        [Test] public void Pits_GrowTowardTheHead() => Assert.IsNull(GrowthProblem(Pits()));

        [Test] public void Red_Pits_AllAlike()
        {
            PitRow p = Pits(); p.sizeGrowth = 0f;
            StringAssert.Contains("do not grow", GrowthProblem(p));
        }

        // The pits are oblique slits: every one >= 1.6 x as long as it is wide, its long axis within 15 deg of the wanted
        // slant (the part's default, 50 deg) off the row's line (neither along nor around the body).
        static string SlitProblem(PitRow pits, float wantSlantDeg)
        {
            PitShape[] s = Shapes(pits);
            for (int k = 0; k < s.Length; k++)
            {
                float asp = s[k].halfLength / s[k].halfWidth;
                if (asp < 1.6f) return $"pit {k} is round, not a slit: x {asp:F2} long / wide (< 1.6)";
                if (Mathf.Abs(s[k].slantDeg - Mathf.Abs(wantSlantDeg)) > 15f) return $"pit {k} is not oblique: its long axis is {s[k].slantDeg:F1} deg off the row (want {wantSlantDeg:F0} +- 15)";
            }
            return null;
        }

        [Test] public void Pits_AreObliqueSlits() => Assert.IsNull(SlitProblem(Pits(), Pits().slantDeg));

        [Test] public void Red_Pits_Round()
        {
            PitRow p = Pits(); p.aspect = 1f;
            StringAssert.Contains("round", SlitProblem(p, Pits().slantDeg));
        }

        [Test] public void Red_Pits_AlongTheBody()
        {
            PitRow p = Pits(); p.slantDeg = 0f;
            StringAssert.Contains("not oblique", SlitProblem(p, Pits().slantDeg));   // laid along the body: not the slant asked for
        }

        [Test] public void Validate_Messages()
        {
            PitRow p = Pits(); p.count = 0;
            var parts = new List<CreaturePart> { CreatureFlowBodyTests.Body(), p };
            var ctx = new SpeciesCtx(parts, new CreatureModule[0], 24, false);
            StringAssert.Contains("spiracles", p.Validate(ctx));
            var noBody = new SpeciesCtx(new List<CreaturePart> { Pits() }, new CreatureModule[0], 24, false);
            Assert.IsNotNull(Pits().Validate(noBody));
        }
    }
}
