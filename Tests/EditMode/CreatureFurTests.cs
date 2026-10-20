using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Creature fur (Fur look module, Scripts/Creatures/Fur): the CPU reference of the GPU path. Roots lie on the rest
    // surface inside their triangles, respect the regions (no fur on the face, claws, eyes), are deterministic and
    // within the budget; skinned with the renderer's own skin matrices they sit on the skinned mesh (the rasterised
    // surface: the barycentric blend of the skinned vertices) within 1e-4 m; the strand spring settles, trails and
    // stays finite; the slot picker keeps identities and never allocates; species without the module are untouched.
    public class CreatureFurTests
    {
        const float kDt = 1f / 60f;
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        static CreatureSpecies Shaggy() => CreatureKitAssets.Load("ShaggyMonitor");

        sealed class Built
        {
            public CreatureSpecies sp; public Fur fur; public FurData d; public KitRender rd;
            public Vector3[] v, n; public int[] t; public List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>(); public List<Vector4> uv2 = new List<Vector4>();
        }

        Built Build(CreatureSpecies sp = null)
        {
            var b = new Built { sp = sp ?? Shaggy() };
            Assert.IsNotNull(b.sp, "ShaggyMonitor.asset loads");
            b.fur = FurBuilder.ModuleOf(b.sp);
            Assert.IsNotNull(b.fur, "it has a Fur module");
            b.rd = KitRender.For(b.sp);
            trash.Add(b.rd.Mesh);
            b.d = FurBuilder.Build(b.sp, b.rd.Mesh, b.rd.BindPoses);
            b.v = b.rd.Mesh.vertices; b.n = b.rd.Mesh.normals; b.t = b.rd.Mesh.triangles;
            b.rd.Mesh.GetUVs(0, b.uv0); b.rd.Mesh.GetUVs(1, b.uv1); b.rd.Mesh.GetUVs(2, b.uv2);
            return b;
        }

        static SpeciesCtx Ctx() => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], 20, false);

        static FurRegion Region() => new FurRegion
        {
            name = "r", kind = FurKind.Strands, partMask = 1, arcFrom = 0f, arcTo = 1f, dorsalFrom = -1f, dorsalTo = 1f,
            length = 0.05f, density = 1000f, width = 0.003f, stiffnessHz = 3f, damping = 0.4f, clump = 0.5f, clumpSize = 4,
        };

        [Test] public void Fur_Validate_RejectsBadData()
        {
            var ok = new Fur { regions = new[] { Region() } };
            Assert.IsNull(ok.Validate(Ctx()));
            var soft = new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], 0, true);
            Assert.IsNotNull(ok.Validate(soft), "needs a spine body");
            Assert.IsNotNull(new Fur().Validate(Ctx()), "no regions");
            FurRegion r;
            r = Region(); r.partMask = 1 | (1 << 2); Assert.IsNotNull(new Fur { regions = new[] { r } }.Validate(Ctx()), "claws refused");
            r = Region(); r.partMask = 1 << 3; Assert.IsNotNull(new Fur { regions = new[] { r } }.Validate(Ctx()), "eyes refused");
            r = Region(); r.partMask = 1 << 13; Assert.IsNotNull(new Fur { regions = new[] { r } }.Validate(Ctx()), "teeth refused");
            r = Region(); r.partMask = 0; Assert.IsNotNull(new Fur { regions = new[] { r } }.Validate(Ctx()), "empty mask");
            r = Region(); r.arcTo = r.arcFrom; Assert.IsNotNull(new Fur { regions = new[] { r } }.Validate(Ctx()), "empty arc window");
            r = Region(); r.dorsalFrom = 1f; Assert.IsNotNull(new Fur { regions = new[] { r } }.Validate(Ctx()), "empty dorsal window");
            r = Region(); r.length = 0f; Assert.IsNotNull(new Fur { regions = new[] { r } }.Validate(Ctx()), "no length");
            r = Region(); r.stiffnessHz = 0f; Assert.IsNotNull(new Fur { regions = new[] { r } }.Validate(Ctx()), "strands need a spring");
            r = Region(); r.clumpSize = 0; Assert.IsNotNull(new Fur { regions = new[] { r } }.Validate(Ctx()), "clump size");
            var many = new FurRegion[FurBuilder.MaxRegions + 1];
            for (int i = 0; i < many.Length; i++) many[i] = Region();
            Assert.IsNotNull(new Fur { regions = many }.Validate(Ctx()), "too many regions");
            Assert.IsNotNull(new Fur { regions = new[] { Region() }, strandFadeEnd = 1f, strandFadeStart = 2f }.Validate(Ctx()), "fade order");
        }

        // The GPU structs (Resources/CreatureFur/FurCommon.hlsl) are these, byte for byte.
        [Test] public void GpuLayouts_MatchTheHlsl()
        {
            Assert.AreEqual(64, System.Runtime.InteropServices.Marshal.SizeOf<FurSkinVertex>());
            Assert.AreEqual(64, System.Runtime.InteropServices.Marshal.SizeOf<FurRoot>());
            Assert.AreEqual(48, System.Runtime.InteropServices.Marshal.SizeOf<FurRegionRow>());
            Assert.AreEqual(32, System.Runtime.InteropServices.Marshal.SizeOf<CreatureInstance>(), "CreatureSkin.hlsl's instance");
        }

        [Test] public void Window_SoftEdges()
        {
            Assert.AreEqual(1f, FurBuilder.Window(0.5f, 0f, 1f, 0.1f));
            Assert.AreEqual(0f, FurBuilder.Window(-0.2f, 0f, 1f, 0.1f));
            Assert.AreEqual(0.5f, FurBuilder.Window(-0.05f, 0f, 1f, 0.1f), 1e-6f);
            Assert.AreEqual(0f, FurBuilder.Window(1.0001f, 0f, 1f, 0f), "hard edge");
        }

        // Only ShaggyMonitor has fur; every other species draws exactly as before (no Fur module, no fur system).
        [Test] public void OnlyFurrySpecies_HaveFur()
        {
            foreach (string name in CreatureKitAssets.Species())
            {
                CreatureSpecies sp = CreatureKitAssets.Load(name);
                bool has = FurBuilder.ModuleOf(sp) != null;
                Assert.AreEqual(name == "ShaggyMonitor", has, name);
            }
        }

        [Test] public void ShaggyMonitor_ComposesAndValidates()
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("ShaggyMonitor");
            Assert.IsNotNull(a);
            Assert.IsNull(a.Validate(), "validates");
            Fur f = FurBuilder.ModuleOf(a);
            Assert.IsNotNull(f);
            Assert.AreEqual(6, f.regions.Length);
            Assert.IsTrue(f.HasKind(FurKind.Undercoat) && f.HasKind(FurKind.Strands), "both layers");
            // the monitor underneath: same bones as KitMonitor
            Assert.AreEqual(CreatureKitAssets.Load("KitMonitor").BoneCount, a.Compiled.Runtime.BoneCount);
        }

        // Roots on the rest surface, inside their triangle, the triangle's vertices the ones they name.
        [Test] public void Roots_OnTheRestSurface()
        {
            Built b = Build();
            Assert.Greater(b.d.StrandCount, 1000, "a shaggy coat");
            for (int i = 0; i < b.d.StrandCount; i++)
            {
                FurRoot r = b.d.Roots[i];
                int t = b.d.RootTriangle[i];
                Assert.AreEqual(b.t[3 * t], b.d.SourceVertex[r.v0]); Assert.AreEqual(b.t[3 * t + 1], b.d.SourceVertex[r.v1]); Assert.AreEqual(b.t[3 * t + 2], b.d.SourceVertex[r.v2]);
                float l0 = 1f - r.b1 - r.b2;
                Assert.GreaterOrEqual(r.b1, 0f); Assert.GreaterOrEqual(r.b2, 0f); Assert.GreaterOrEqual(l0, -1e-6f, "inside the triangle");
                Vector3 a = b.v[b.t[3 * t]], c = b.v[b.t[3 * t + 1]], e = b.v[b.t[3 * t + 2]];
                Vector3 p = a * l0 + c * r.b1 + e * r.b2;
                Vector3 fn = Vector3.Cross(c - a, e - a).normalized;
                Assert.Less(Mathf.Abs(Vector3.Dot(p - a, fn)), 1e-6f, "in the triangle's plane");
                Assert.IsTrue(r.length > 0f && r.width > 0f && !float.IsNaN(r.comb.x) && !float.IsNaN(r.tipDir.x));
                Assert.AreEqual(1f, r.comb.magnitude, 1e-4f); Assert.AreEqual(1f, r.tipDir.magnitude, 1e-4f);
                // off the skin, both segments
                Vector3 nn = (b.n[b.t[3 * t]] * l0 + b.n[b.t[3 * t + 1]] * r.b1 + b.n[b.t[3 * t + 2]] * r.b2).normalized;
                Assert.Greater(Vector3.Dot(r.comb, nn), 0.02f, "the hair rises off the skin");
                Assert.Greater(Vector3.Dot(r.tipDir, nn), 0.0f, "its tip too");
                Assert.AreEqual((i + 0.5f) / b.d.StrandCount, r.rank, 1e-6f);
            }
        }

        // Every root's triangle is in its region's part mask, the weight there is > 0, no root or shell on the face,
        // claws or eyes; the undercoat shells copy the body's vertex streams (they skin like the body).
        [Test] public void Masks_Respected_FaceClawsEyesBare()
        {
            Built b = Build();
            float bodyMax = 0f, legMax = 0f, clawMin = float.MaxValue;
            for (int i = 0; i < b.v.Length; i++)
            {
                int part = Mathf.RoundToInt(b.uv1[i].x);
                if (part == 0) bodyMax = Mathf.Max(bodyMax, b.uv0[i].x);
                if (part == 1) legMax = Mathf.Max(legMax, b.uv0[i].x);
                if (part == 2) clawMin = Mathf.Min(clawMin, b.uv0[i].x);
            }
            TestContext.WriteLine($"arc: body to {bodyMax:F3} m, limbs to {legMax:F3} m, claws from {clawMin:F3} m; {b.d.StrandCount} strands, {b.d.ShellTris.Count / 3} shell triangles of {b.t.Length / 3}");
            var perRegion = new int[b.fur.regions.Length];
            for (int i = 0; i < b.d.StrandCount; i++)
            {
                FurRoot r = b.d.Roots[i];
                FurRegion g = b.fur.regions[r.region];
                Assert.AreEqual(FurKind.Strands, g.kind);
                perRegion[r.region]++;
                float wsum = 0f;
                float[] lam = { 1f - r.b1 - r.b2, r.b1, r.b2 };
                uint[] vs = { r.v0, r.v1, r.v2 };
                for (int k = 0; k < 3; k++)
                {
                    FurSkinVertex sv = b.d.Verts[vs[k]];
                    Assert.IsTrue(FurBuilder.PartIn(g.partMask, sv.uv1.x), "part in the mask");
                    Assert.AreEqual(0, (1 << Mathf.RoundToInt(sv.uv1.x)) & Fur.ForbiddenParts, "never claws, eyes, teeth");
                    wsum += lam[k] * FurBuilder.Weight(g, sv.uv0, sv.uv1);
                }
                FurSkinVertex a0 = b.d.Verts[r.v0], a1 = b.d.Verts[r.v1], a2 = b.d.Verts[r.v2];
                float arc = lam[0] * a0.uv0.x + lam[1] * a1.uv0.x + lam[2] * a2.uv0.x;
                if (Mathf.RoundToInt(a0.uv1.x) == 0) Assert.Greater(arc, 0.12f, "the face is bare (the head ends behind the eyes)");
                Assert.Greater(wsum, 0f, "inside the region's window");
            }
            for (int r = 0; r < perRegion.Length; r++)
                if (b.fur.regions[r].kind == FurKind.Strands) Assert.Greater(perRegion[r], 30, b.fur.regions[r].name + " has strands");

            Assert.IsTrue(b.d.HasShells, "an undercoat");
            int furred = 0;
            for (int i = 0; i < b.d.ShellPos.Count; i++)
            {
                int s = b.d.ShellSource[i];
                Assert.AreEqual(b.v[s], b.d.ShellPos[i]); Assert.AreEqual(b.uv2[s], b.d.ShellUv2[i]);
                Assert.AreEqual(b.uv0[s], b.d.ShellUv0[i]); Assert.AreEqual(b.uv1[s], b.d.ShellUv1[i]);
                int part = Mathf.RoundToInt(b.d.ShellUv1[i].x);
                Assert.IsTrue(part == 0 || part == 1, "undercoat on the body and limbs only, part " + part);
                if (b.d.ShellUv3[i].x > 0f) furred++;
                if (part == 0 && b.d.ShellUv0[i].x < 0.12f) Assert.AreEqual(0f, b.d.ShellUv3[i].x, "no undercoat on the face");
                Assert.LessOrEqual(b.d.ShellUv3[i].x, 0.0071f);
            }
            Assert.Greater(furred, b.d.ShellPos.Count / 2);
        }

        [Test] public void Roots_Deterministic_AndWithinBudget()
        {
            Built a = Build(), b = Build();
            Assert.AreEqual(a.d.StrandCount, b.d.StrandCount);
            for (int i = 0; i < a.d.StrandCount; i++)
            {
                FurRoot x = a.d.Roots[i], y = b.d.Roots[i];
                Assert.IsTrue(x.v0 == y.v0 && x.v1 == y.v1 && x.v2 == y.v2 && x.b1 == y.b1 && x.b2 == y.b2 && x.length == y.length
                              && x.comb == y.comb && x.tipDir == y.tipDir && x.seed == y.seed && x.region == y.region, "root " + i);
            }
            Assert.AreEqual(a.d.ShellPos.Count, b.d.ShellPos.Count);
            Assert.LessOrEqual(a.d.StrandCount, a.fur.maxStrands);

            // a tighter budget thins every region (the densities scale down), a different seed moves the roots
            var tight = (Fur)a.fur.Clone(); tight.maxStrands = 1000;
            FurData td = BuildWith(a, tight);
            Assert.LessOrEqual(td.StrandCount, 1000);
            Assert.Greater(td.StrandCount, 700, "the budget is used");
            var other = (Fur)a.fur.Clone(); other.seed = a.fur.seed + 1;
            FurData od = BuildWith(a, other);
            Assert.AreNotEqual(a.d.Roots[0].b1, od.Roots[0].b1);
        }

        static FurData BuildWith(Built b, Fur f) => FurBuilder.Build(f, b.v, b.n, b.uv0, b.uv1, b.uv2, b.t, b.rd.BindPoses, true);

        // RED: the mask test bites - a region over the whole body puts strands on the face.
        [Test] public void Masks_Red_WholeBodyRegionReachesTheFace()
        {
            Built b = Build();
            var f = (Fur)b.fur.Clone();
            FurRegion g = Region(); g.arcFrom = 0f; g.arcTo = 2f; g.density = 40000f;
            f.regions = new[] { g };
            FurData d = BuildWith(b, f);
            bool face = false;
            foreach (FurRoot r in d.Roots) if (d.Verts[r.v0].uv0.x < 0.1f && Mathf.RoundToInt(d.Verts[r.v0].uv1.x) == 0) face = true;
            Assert.IsTrue(face, "the face check would see it");
        }

        // The renderer's own skin matrices (CreatureRenderer's CPU side), a walking, turning monitor: every root,
        // skinned, is the barycentric blend of the skinned mesh vertices (what the rasteriser draws) within 1e-4 m.
        [Test] public void SkinnedRoots_OnTheSkinnedMesh()
        {
            Built b = Build();
            CreatureSpecies sp = b.sp;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale * 1.05f);
            var ren = new CreatureRenderer(sp, null);
            ren.Add(sim, new CreatureInstance { sizeScale = sim.SizeScale });
            var mesh = ren.Mesh; trash.Add(mesh);
            float worst = 0f, worstN = 1f;
            for (int f = 0; f < 150; f++)
            {
                float a = f * 0.03f;
                sim.Tick(kDt, sim.Joints[0] + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3f);
                if (f % 30 != 29) continue;
                ren.Render();
                Matrix4x4[] skin = ren.SkinForTest;
                // the test's own skinning of every mesh vertex (CreatureSkin.hlsl's 2-bone blend)
                var sv = new Vector3[b.v.Length]; var sn = new Vector3[b.v.Length];
                for (int i = 0; i < b.v.Length; i++)
                {
                    Vector4 s = b.uv2[i];
                    Matrix4x4 m0 = skin[(int)(s.x + 0.5f)], m1 = skin[(int)(s.y + 0.5f)];
                    sv[i] = m0.MultiplyPoint3x4(b.v[i]) * s.z + m1.MultiplyPoint3x4(b.v[i]) * s.w;
                    sn[i] = (m0.MultiplyVector(b.n[i]) * s.z + m1.MultiplyVector(b.n[i]) * s.w).normalized;
                }
                for (int i = 0; i < b.d.StrandCount; i++)
                {
                    FurRoot r = b.d.Roots[i];
                    int t = b.d.RootTriangle[i];
                    float l0 = 1f - r.b1 - r.b2;
                    Vector3 want = sv[b.t[3 * t]] * l0 + sv[b.t[3 * t + 1]] * r.b1 + sv[b.t[3 * t + 2]] * r.b2;
                    Vector3 wantN = (sn[b.t[3 * t]] * l0 + sn[b.t[3 * t + 1]] * r.b1 + sn[b.t[3 * t + 2]] * r.b2).normalized;
                    FurSkinning.Root(skin, 0, b.d.Verts, r, out Vector3 p, out Vector3 n, out Vector3 comb, out Vector3 tip);
                    worst = Mathf.Max(worst, (p - want).magnitude);
                    worstN = Mathf.Min(worstN, Vector3.Dot(n, wantN));
                    Assert.Greater(Vector3.Dot(comb, n), 0f, "the skinned comb still rises off the skin");
                }
            }
            // RED: the same check sees a root whose barycentrics are swapped
            {
                Matrix4x4[] skin = ren.SkinForTest;
                float red = 0f;
                for (int i = 0; i < b.d.StrandCount; i++)
                {
                    FurRoot r = b.d.Roots[i];
                    FurSkinning.Root(skin, 0, b.d.Verts, r, out Vector3 p, out _, out _, out _);
                    FurRoot w = r; w.b1 = r.b2; w.b2 = r.b1;
                    FurSkinning.Root(skin, 0, b.d.Verts, w, out Vector3 q, out _, out _, out _);
                    red = Mathf.Max(red, (p - q).magnitude);
                }
                Assert.Greater(red, 1e-3f, "RED: a wrong barycentric is seen");
            }
            TestContext.WriteLine($"worst root offset {worst * 1e3f:F4} mm, worst normal cos {worstN:F6}");
            Assert.Less(worst, 1e-4f);
            Assert.Greater(worstN, 0.9999f);
            ren.Dispose();
        }

        // ---- the strand spring

        static void Rest(out Vector3 p0, out Vector3 n, out Vector3 comb, out Vector3 tip)
        {
            p0 = new Vector3(0.3f, 1f, -0.2f); n = Vector3.up;
            comb = new Vector3(0f, 0.6f, -0.8f); tip = new Vector3(0f, 0.3f, -0.95f).normalized;
        }

        [Test] public void Strand_SettlesToItsDroopedRest_LengthsExact()
        {
            Rest(out Vector3 p0, out Vector3 n, out Vector3 comb, out Vector3 tip);
            float L = 0.2f, w = FurStrandSim.Omega(3f, 4f);
            var s = new FurStrand();
            FurStrandSim.Reset(ref s, p0, comb, tip, L);
            Vector3 prev = s.p2;
            float lastMove = 0f;
            for (int f = 0; f < 600; f++)
            {
                FurStrandSim.Step(ref s, p0, n, comb, tip, L, w, 0.4f, 0.6f, kDt);
                lastMove = (s.p2 - prev).magnitude; prev = s.p2;
                Assert.AreEqual(0.5f * L, (s.p1 - s.p0).magnitude, 1e-5f);
                Assert.AreEqual(0.5f * L, (s.p2 - s.p1).magnitude, 1e-5f);
            }
            Assert.Less(lastMove, 1e-6f, "settled");
            Assert.Less(s.p2.y, p0.y + (comb.y + tip.y) * 0.5f * L - 1e-3f, "gravity droops it");
            Assert.Greater(Vector3.Dot(s.p2 - p0, n), 0f, "still off the skin");
        }

        [Test] public void Strand_TrailsTheMotion_ThenSettlesBack()
        {
            Rest(out Vector3 p0, out Vector3 n, out Vector3 comb, out Vector3 tip);
            float L = 0.2f, w = FurStrandSim.Omega(3f, 4f);
            var still = new FurStrand(); FurStrandSim.Reset(ref still, p0, comb, tip, L);
            for (int f = 0; f < 300; f++) FurStrandSim.Step(ref still, p0, n, comb, tip, L, w, 0.4f, 0.6f, kDt);
            Vector3 restTip = still.p2 - p0;

            var s = still;
            Vector3 v = new Vector3(1.5f, 0f, 0f);   // the body sets off sideways
            Vector3 root = p0;
            float lag = 0f, cruise = 0f;
            for (int f = 0; f < 60; f++)
            {
                root += v * kDt; FurStrandSim.Step(ref s, root, n, comb, tip, L, w, 0.4f, 0.6f, kDt);
                float l = Vector3.Dot((s.p2 - root) - restTip, v.normalized);
                lag = Mathf.Min(lag, l); cruise = l;
            }
            TestContext.WriteLine($"tip lags up to {-lag * 1e3f:F1} mm behind its rest offset as the body sets off, {-cruise * 1e3f:F1} mm at a steady run");
            Assert.Less(lag, -0.02f, "the tip trails the motion");
            Assert.Less(cruise, 0f, "running sweeps it back a little (air drag)");
            Assert.Greater(cruise, 0.5f * lag, "but less than the start's inertia");
            for (int f = 0; f < 600; f++) FurStrandSim.Step(ref s, root, n, comb, tip, L, w, 0.4f, 0.6f, kDt);
            Assert.Less(((s.p2 - root) - restTip).magnitude, 1e-4f, "settles back to the same rest");
        }

        [Test] public void Strand_FiniteUnderHugeStepsAndTeleports()
        {
            Rest(out Vector3 p0, out Vector3 n, out Vector3 comb, out Vector3 tip);
            float L = 0.3f, w = FurStrandSim.Omega(8f, 0.5f);
            var s = new FurStrand(); FurStrandSim.Reset(ref s, p0, comb, tip, L);
            for (int f = 0; f < 200; f++)
            {
                Vector3 root = p0 + new Vector3((f % 7) * 3f, 0f, (f % 3) * -5f);
                FurStrandSim.Step(ref s, root, n, comb, tip, L, w, 0.05f, 2f, f % 5 == 0 ? 0.5f : 0.001f);
                Assert.IsFalse(float.IsNaN(s.p2.x) || float.IsInfinity(s.p2.x) || float.IsNaN(s.v2.x), "finite");
                Assert.AreEqual(0.5f * L, (s.p2 - s.p1).magnitude, 1e-4f);
            }
        }

        [Test] public void Strand_Omega_FroudeScaled()
        {
            Assert.AreEqual(2f * Mathf.PI * 3f, FurStrandSim.Omega(3f, 1f), 1e-4f);
            Assert.AreEqual(FurStrandSim.Omega(3f, 1f) / 2f, FurStrandSim.Omega(3f, 4f), 1e-4f, "/ sqrt(s)");
        }

        // ---- slots

        sealed class FakeBody : ICreatureBody
        {
            public CreatureSpecies Species => null;
            public float SizeScale => 1f;
            public bool DynamicLook => false;
            public void WriteBoneWorld(Matrix4x4[] dst, int offset) { }
            public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }
            public void UpdateLook(ref CreatureInstance inst) { }
            public void Tick(float dt) { }
        }

        [Test] public void Slots_NearestFirst_KeepIdentity_NoGC()
        {
            var bodies = new List<ICreatureBody>();
            for (int i = 0; i < 6; i++) bodies.Add(new FakeBody());
            var c = new Vector3[8]; var reach = new float[8];
            for (int i = 0; i < 6; i++) { c[i] = new Vector3(i * 2f + 1f, 0f, 0f); reach[i] = 8f; }
            var slots = new FurSlots(2, 8);
            slots.Assign(Vector3.zero, bodies, c, reach, 6);
            Assert.AreSame(bodies[0], slots.Body[0]); Assert.AreSame(bodies[1], slots.Body[1]);
            Assert.IsTrue(slots.Reset[0] && slots.Reset[1], "new slots reset");
            slots.Assign(Vector3.zero, bodies, c, reach, 6);
            Assert.IsFalse(slots.Reset[0] || slots.Reset[1], "kept slots keep their state");
            // swap-remove body 0: body 5 moves to index 0; slot 0 frees and takes the next nearest (body 2)
            bodies[0] = bodies[5]; c[0] = c[5]; bodies.RemoveAt(5);
            slots.Assign(Vector3.zero, bodies, c, reach, 5);
            Assert.AreSame(bodies[2], slots.Body[0]); Assert.AreEqual(2, slots.Index[0]); Assert.IsTrue(slots.Reset[0]);
            Assert.AreSame(bodies[1], slots.Body[1]); Assert.AreEqual(1, slots.Index[1]); Assert.IsFalse(slots.Reset[1]);
            // hysteresis: body 1 at 3 m with reach 2.8 stays (x1.15), at 3.5 it goes
            reach[1] = 2.8f; slots.Assign(Vector3.zero, bodies, c, reach, 5);
            Assert.AreSame(bodies[1], slots.Body[1]);
            reach[1] = 2.5f; slots.Assign(Vector3.zero, bodies, c, reach, 5);
            Assert.AreNotSame(bodies[1], slots.Body[1]);
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() => { for (int k = 0; k < 100; k++) slots.Assign(new Vector3(k * 0.1f, 0f, 0f), bodies, c, reach, 5); });
            Assert.AreEqual(0, allocs, "no allocation per frame");
        }

        // ---- the renderer hook

        sealed class FakeFur : ICreatureFurDraw
        {
            public int Calls, LastCount; public Vector3 FirstCentre; public bool Disposed;
            public void Draw(GraphicsBuffer bones, GraphicsBuffer instances, int count, IReadOnlyList<ICreatureBody> bodies, Vector3[] centres, CreatureInstance[] inst)
            { Calls++; LastCount = count; FirstCentre = centres[0]; }
            public void Dispose() { Disposed = true; }
        }

        // The renderer passes each individual's mid-spine bone to the fur and never allocates for it; the CPU side
        // runs without a GPU (as in -nographics), so here only the centres are checked (Draw needs the device).
        [Test] public void Renderer_FurCentres_MidSpine_NoGC()
        {
            CreatureSpecies sp = Shaggy();
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale);
            var ren = new CreatureRenderer(sp, null);
            trash.Add(ren.Mesh);
            var fake = new FakeFur();
            ren.Fur = fake;
            ren.Add(sim, new CreatureInstance { sizeScale = sim.SizeScale });
            for (int f = 0; f < 10; f++) sim.Tick(kDt, new Vector3(0f, 0f, 5f));
            ren.Render();
            Assert.AreEqual(sim.Joints[sp.JointCount / 2].x, ren.FurCentresForTest[0].x, 1e-4f);
            Assert.AreEqual(sim.Joints[sp.JointCount / 2].z, ren.FurCentresForTest[0].z, 1e-4f);
            int allocs = GcAllocs.Count(() => { for (int k = 0; k < 30; k++) ren.Render(); });
            Assert.AreEqual(0, allocs);
            ren.Dispose();
            Assert.IsTrue(fake.Disposed, "the renderer disposes its fur");
        }
    }
}
