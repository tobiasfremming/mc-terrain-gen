using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // The kit's DimpledOrb part (2026-10-20, the GolfballStrider's body): a round body on a bone of its own, covered in
    // golf-ball pits (spherical Fibonacci centres, analytic pit normals, pits darkened through the dorsal channel), that
    // rocks on a damped spring about a pivot when the body accelerates. Validation, the pit lattice, the mesh (on the
    // species), the runtime (rest bone rigid to the joint, rocking bounded and settling, Froude) and RED proofs.
    public class CreatureDimpledOrbTests
    {
        readonly List<Object> trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        const string kSpecies = CreatureBendyLegTests.kSpecies;

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        static DimpledOrb Ok() => new DimpledOrb { id = "orb", at = new PartSocket(1, 0, new Vector3(0f, 0.1f, -0.05f)), radius = 0.15f, pitCount = 300, pitDepth = 0.004f };

        [Test] public void DimpledOrb_Validate_RejectsBadData()
        {
            Assert.IsNull(Ok().Validate(Ctx(3)));
            DimpledOrb bad;
            bad = (DimpledOrb)Ok().Clone(); bad.radius = 0f; Assert.IsNotNull(bad.Validate(Ctx(3)), "no radius");
            bad = (DimpledOrb)Ok().Clone(); bad.at.joint = 5; Assert.IsNotNull(bad.Validate(Ctx(3)), "joint out of range");
            bad = (DimpledOrb)Ok().Clone(); bad.pitCount = -1; Assert.IsNotNull(bad.Validate(Ctx(3)), "negative pits");
            bad = (DimpledOrb)Ok().Clone(); bad.pitRadius = 0.9f; Assert.IsNotNull(bad.Validate(Ctx(3)), "pits overlapping");
            bad = (DimpledOrb)Ok().Clone(); bad.pitDepth = 0.5f; Assert.IsNotNull(bad.Validate(Ctx(3)), "pits deeper than a tenth of the radius");
            bad = (DimpledOrb)Ok().Clone(); bad.detail = 8; Assert.IsNotNull(bad.Validate(Ctx(3)), "too fine");
            bad = (DimpledOrb)Ok().Clone(); bad.squash = 0f; Assert.IsNotNull(bad.Validate(Ctx(3)), "flat");
            bad = (DimpledOrb)Ok().Clone(); bad.rockHz = 0f; Assert.IsNotNull(bad.Validate(Ctx(3)), "no rock spring");
            Assert.AreEqual(1, Ok().BoneCount(Ctx(3)));
        }

        // The pit centres are spread evenly: every nearest-neighbour distance within 0.75..1.35 of the mean spacing.
        [Test] public void DimpledOrb_Pits_EvenlySpread()
        {
            foreach (int n in new[] { 60, 300, 700 })
            {
                Vector3[] c = DimpledOrb.PitCentres(n);
                Assert.AreEqual(n, c.Length);
                float mean = DimpledOrb.Spacing(n);
                for (int i = 0; i < n; i++)
                {
                    Assert.AreEqual(1f, c[i].magnitude, 1e-5f);
                    float best = float.MaxValue;
                    for (int j = 0; j < n; j++) if (j != i) best = Mathf.Min(best, (c[i] - c[j]).magnitude);
                    Assert.That(best / mean, Is.InRange(0.75f, 1.35f), $"n {n} pit {i}");
                }
            }
        }

        sealed class Orb { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<float> dorsal = new List<float>(); public int tris; }

        // The orb's vertices (rigid to its own bone) of the species mesh.
        Orb OrbMesh(CreatureSpecies sp, out DimpledOrb part, out Vector3 centre)
        {
            CreatureSpecies rt = sp.Compiled.Runtime;
            IReadOnlyList<CreaturePart> parts = KitView.Parts(rt);
            int index = -1; part = null;
            for (int i = 0; i < parts.Count; i++) if (parts[i] is DimpledOrb o) { part = o; index = i; }
            Assert.IsNotNull(part, "the strider has a DimpledOrb");
            int bone = rt.Layout.Start(index);
            CreatureSim rest = CreatureRestPose.Sim(rt);
            centre = rest.Joints[part.at.joint] + rest.Rotations[part.at.joint] * part.at.offset;
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            trash.Add(m);
            var v = m.vertices; var nn = m.normals; var t = m.triangles;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
            m.GetUVs(1, uv1); m.GetUVs(2, uv2);
            var r = new Orb();
            var mine = new bool[v.Length];
            for (int i = 0; i < v.Length; i++)
                if ((int)uv2[i].x == bone && uv2[i].z >= 0.999f) { mine[i] = true; r.v.Add(v[i]); r.n.Add(nn[i]); r.dorsal.Add(uv1[i].y); }
            for (int k = 0; k < t.Length; k += 3) if (mine[t[k]]) r.tris++;
            return r;
        }

        // Pits: the surface lies between R - depth and R (per axis), every normal is unit and faces out of the orb, a
        // real share of the surface is pitted, and pits read darker (the dorsal channel drops in them).
        [Test] public void DimpledOrb_Mesh_PittedSurface_NormalsOut_PitsDarker()
        {
            Orb o = OrbMesh(CreatureKitAssets.Load(kSpecies), out DimpledOrb p, out Vector3 c);
            Assert.Greater(o.v.Count, 2000, "a fine enough sphere for its pits");
            Assert.Greater(o.tris, 4000);
            int deep = 0; float darkDeep = 0f, darkRim = 0f; int rim = 0;
            for (int i = 0; i < o.v.Count; i++)
            {
                Vector3 d = o.v[i] - c;
                Vector3 unit = new Vector3(d.x / p.radius, d.y / (p.radius * p.squash), d.z / (p.radius * p.stretch));
                float r = unit.magnitude;   // 1 on the sphere, 1 - depth/R at a pit's floor
                Assert.That(r, Is.InRange(1f - p.pitDepth / p.radius - 1e-3f, 1f + 1e-3f), $"vertex {i}");
                Assert.AreEqual(1f, o.n[i].magnitude, 1e-3f, $"vertex {i} normal");
                Assert.Greater(Vector3.Dot(o.n[i], d.normalized), 0.5f, $"vertex {i} normal faces into the orb");
                float depth = (1f - r) * p.radius / p.pitDepth;
                float plain = DimpledOrb.DorsalOf(unit.normalized.y);
                if (depth > 0.7f) { deep++; darkDeep += o.dorsal[i] - plain; }
                else if (depth < 0.02f) { rim++; darkRim += o.dorsal[i] - plain; }
            }
            Assert.Greater(deep, o.v.Count / 20, "pits cover the surface");
            Assert.Less(darkDeep / deep, darkRim / Mathf.Max(1, rim) - 0.3f * p.pitShade, "pit floors are darker than the rims");
        }

        // RED: the pit check bites - a smooth orb (no pits) fails "pits cover the surface".
        [Test] public void DimpledOrb_Red_SmoothOrbHasNoPits()
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset(kSpecies);
            DimpledOrb a = null;
            foreach (CreaturePart q in asset.parts) if (q is DimpledOrb d) a = d;
            int keep = a.pitCount;
            a.pitCount = 0;
            try
            {
                asset.InvalidateCompiled();
                Orb o = OrbMesh(asset, out DimpledOrb p, out Vector3 c);
                int deep = 0;
                for (int i = 0; i < o.v.Count; i++)
                {
                    Vector3 d = o.v[i] - c;
                    float r = new Vector3(d.x / p.radius, d.y / (p.radius * p.squash), d.z / (p.radius * p.stretch)).magnitude;
                    if ((1f - r) * p.radius / Mathf.Max(1e-6f, a.pitDepth) > 0.7f) deep++;
                }
                Assert.AreEqual(0, deep);
            }
            finally { a.pitCount = keep; asset.InvalidateCompiled(); }
        }

        static DimpledOrbRuntime Runtime(CreatureSim w)
        {
            foreach (IPartRuntime r in w.PartRuntimes) if (r is DimpledOrbRuntime o) return o;
            return null;
        }

        // At rest the orb's bone is rigid to its joint (the pivot in the joint frame); walking it rocks (a few degrees,
        // never past maxRockDeg); halted, it settles back to rigid.
        [Test] public void DimpledOrb_Rock_RigidAtRest_BoundedWalking_Settles()
        {
            CreatureSpecies rt = CreatureKitAssets.Load(kSpecies).Compiled.Runtime;
            CreatureSim w = CreatureBendyLegTests.Sim(rt);
            DimpledOrbRuntime o = Runtime(w);
            Assert.IsNotNull(o);
            DimpledOrb p = o.Part;
            var bones = new Matrix4x4[rt.BoneCount];
            w.WriteBoneWorld(bones, 0);
            Matrix4x4 want = Matrix4x4.TRS(w.Joints[p.at.joint] + w.Rotations[p.at.joint] * p.pivot, w.Rotations[p.at.joint], Vector3.one);
            for (int e = 0; e < 16; e++) Assert.AreEqual(want[e], bones[o.Bone][e], 1e-5f, "rest bone");
            float peak = 0f;
            for (int f = 0; f < 600; f++)
            {
                w.Tick(f % 50 == 0 ? 0.1f : 1f / 60f, CreatureBendyLegTests.Weave(w, f / 60f, 1f));
                peak = Mathf.Max(peak, o.RockDeg);
                Assert.LessOrEqual(o.RockDeg, p.maxRockDeg + 1e-3f, $"frame {f}");
            }
            Assert.Greater(peak, 0.5f, "it rocks while walking");
            w.Halt = true;
            for (int f = 0; f < 900; f++) w.Tick(1f / 60f, w.Joints[0]);
            Assert.Less(o.RockDeg, 0.05f, "settled");
        }

        // Froude: the rock (degrees, size-free) is the same at 0.9x and 1.1x ticked at dt x sqrt(s).
        [Test] public void DimpledOrb_Rock_FroudeSimilar()
        {
            CreatureSpecies rt = CreatureKitAssets.Load(kSpecies).Compiled.Runtime;
            float[] peak = new float[2]; float[] sc = { 0.9f, 1.1f };
            for (int c = 0; c < 2; c++)
            {
                CreatureSim w = CreatureBendyLegTests.Sim(rt, sc[c]);
                DimpledOrbRuntime o = Runtime(w);
                for (int f = 0; f < 300; f++)
                {
                    w.Tick(Mathf.Sqrt(sc[c]) / 60f, w.Joints[0] + Vector3.forward * (2f * sc[c]));
                    peak[c] = Mathf.Max(peak[c], o.RockDeg);
                }
            }
            Assert.Greater(peak[0], 0.2f);
            Assert.AreEqual(peak[0], peak[1], 0.02f * peak[0]);
        }
    }
}
