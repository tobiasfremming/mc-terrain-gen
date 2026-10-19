using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's heavy-crab parts (2026-10-20, the king crab and the stone crab): Chela (a claw on an ArthropodLeg's last
    // segment), SpineField (spines / teeth on a RigidLoft), LimbSpines (spines / hairs on leg segments), RigidLoft's
    // ridged lumps and Tuck's per-segment fold planes. Checks: validation; every new vertex rigid to the bone it rides
    // (a claw and its leg's last bone, a spine and its segment, a shell spine and the loft's joint); closed surfaces
    // wound outward (every triangle's face agrees with its vertex normals); the claw grips its leg (the propodus's end
    // inside the palm); spines start on the surface they grow from; mirrored fields are mirror-symmetric; spine counts
    // follow perMetre; ridged lumps stay within lumpHeight; per-segment fold planes equal to yawDeg change nothing.
    // RED: a flipped sweep is caught by the winding check; a spine field on the wrong side is caught by the symmetry check.
    public class CreatureCrabPartsTests
    {
        static CreatureSpecies Load(string n) => CreatureKitAssets.Load(n).Compiled.Runtime;

        static SpeciesCtx Ctx(IReadOnlyList<CreaturePart> parts, int joints) => new SpeciesCtx(parts, new CreatureModule[0], joints, false);

        static T First<T>(CreatureSpecies sp, System.Predicate<T> where = null) where T : CreaturePart
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is T t && (where == null || where(t))) return t;
            return null;
        }

        static int IndexOf(CreatureSpecies sp, string id)
        {
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            for (int i = 0; i < parts.Count; i++) if (parts[i].id == id) return i;
            return -1;
        }

        // ------------------------------------------------------------ validation

        [Test] public void Parts_Validate_RejectBadData()
        {
            CreatureSpecies sp = Load("RockCrab");
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            SpeciesCtx c = Ctx(parts, sp.JointCount);
            Chela chela = First<Chela>(sp);
            Assert.IsNull(chela.Validate(c));
            Assert.AreEqual(0, chela.BoneCount(c), "mesh only");
            var bad = (Chela)chela.Clone(); bad.leg = "nope"; Assert.IsNotNull(bad.Validate(c), "names an ArthropodLeg");
            bad = (Chela)chela.Clone(); bad.fingerHeight = 2f * bad.palmHeight; Assert.IsNotNull(bad.Validate(c), "finger taller than the palm");
            bad = (Chela)chela.Clone(); bad.tipLayer = 5; Assert.IsNotNull(bad.Validate(c), "an octopus layer");
            bad = (Chela)chela.Clone(); bad.openDeg = 80f; Assert.IsNotNull(bad.Validate(c), "open too far");

            SpineField f = First<SpineField>(sp);
            Assert.IsNull(f.Validate(c));
            var bf = (SpineField)f.Clone(); bf.loft = "eyes"; Assert.IsNotNull(bf.Validate(c), "names a RigidLoft");
            bf = (SpineField)f.Clone(); bf.lengthMax = 0.5f * bf.lengthMin; Assert.IsNotNull(bf.Validate(c), "lengthMin <= lengthMax");
            bf = (SpineField)f.Clone(); bf.tMin = 0.9f; bf.tMax = 0.2f; Assert.IsNotNull(bf.Validate(c), "tMin <= tMax");

            LimbSpines h = First<LimbSpines>(sp);
            Assert.IsNull(h.Validate(c));
            var bh = (LimbSpines)h.Clone(); bh.fromSegment = 9; Assert.IsNotNull(bh.Validate(c), "segments in range");
            bh = (LimbSpines)h.Clone(); bh.leg = "carapace"; Assert.IsNotNull(bh.Validate(c), "names an ArthropodLeg");

            Tuck t = First<Tuck>(sp);
            var bt = (Tuck)t.Clone(); bt.legs[0].segmentYawDeg = new float[2];
            var withBad = new List<CreaturePart>(parts); withBad[withBad.Count - 1] = bt;
            Assert.IsNotNull(bt.Validate(Ctx(withBad, sp.JointCount)), "one fold yaw per segment");
        }

        // ------------------------------------------------------------ the mesh

        // Every triangle of both crabs agrees with its vertex normals (outward winding): a sweep closed the wrong way
        // round or a mirrored side left unflipped would show as dark, inside-out patches.
        [TestCase("RockCrab")]
        [TestCase("TentacleCrab")]
        public void Crab_Mesh_TrianglesFaceOut(string species)
        {
            Mesh m = CreatureKitAssets.BuildMesh(Load(species));
            Vector3[] v = m.vertices, n = m.normals; int[] t = m.triangles;
            int bad = 0, total = 0;
            for (int k = 0; k < t.Length; k += 3)
            {
                Vector3 f = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
                if (f.sqrMagnitude < 1e-16f) continue;
                total++;
                if (Vector3.Dot(f, n[t[k]] + n[t[k + 1]] + n[t[k + 2]]) < 0f) bad++;
            }
            Object.DestroyImmediate(m);
            TestContext.Out.WriteLine($"{species}: {bad} of {total} triangles face in");
            Assert.LessOrEqual(bad, total / 1000, "triangles wound against their normals");
        }

        // RED for the winding check: the same mesh with every triangle reversed fails it.
        [Test] public void Red_ReversedWinding_IsCaught()
        {
            Mesh m = CreatureKitAssets.BuildMesh(Load("RockCrab"));
            Vector3[] v = m.vertices, n = m.normals; int[] t = m.triangles;
            int bad = 0, total = 0;
            for (int k = 0; k < t.Length; k += 3)
            {
                Vector3 f = Vector3.Cross(v[t[k + 2]] - v[t[k]], v[t[k + 1]] - v[t[k]]);   // reversed
                if (f.sqrMagnitude < 1e-16f) continue;
                total++;
                if (Vector3.Dot(f, n[t[k]] + n[t[k + 1]] + n[t[k + 2]]) < 0f) bad++;
            }
            Object.DestroyImmediate(m);
            Assert.Greater(bad, total / 2);
        }

        // Each claw is rigid to its leg's last bone, and grips it: the propodus's end lies inside the palm's span.
        [TestCase("RockCrab")]
        [TestCase("TentacleCrab")]
        public void Chela_RigidToLastBone_GripsTheLeg(string species)
        {
            CreatureSpecies sp = Load(species);
            Mesh m = CreatureKitAssets.BuildMesh(sp);
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>(); m.GetUVs(1, uv1); m.GetUVs(2, uv2);
            Vector3[] v = m.vertices;
            Object.DestroyImmediate(m);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            int checkedClaws = 0;
            foreach (CreaturePart p in KitView.Parts(sp))
            {
                if (!(p is Chela c)) continue;
                int li = IndexOf(sp, c.LegId);
                var leg = (ArthropodLeg)KitView.Parts(sp)[li];
                int last = sp.Layout.Start(li) + leg.segments.Length - 1;
                // the claw's vertices (tip layer: fingers and teeth) are rigid to the last bone
                int n = 0;
                Vector3 lo = Vector3.one * 1e9f, hi = -lo;
                for (int i = 0; i < v.Length; i++)
                {
                    if ((int)uv2[i].x != last) continue;
                    Assert.AreEqual(1f, uv2[i].z, 1e-6f, "rigid");
                    Vector3 l = bind[last].MultiplyPoint3x4(v[i]);
                    lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l);
                    if (Mathf.RoundToInt(uv1[i].x) == c.tipLayer) n++;
                }
                Assert.Greater(n, 100, $"{c.id}: finger vertices on the last bone");
                // the propodus runs along the bone's +z from 0 to its length: its end inside the claw's extent
                float L = leg.segments[leg.segments.Length - 1].length;
                Assert.IsTrue(lo.z < 0f && hi.z > L, $"{c.id}: the claw spans the propodus ({lo.z:F3}..{hi.z:F3} vs 0..{L:F3})");
                float palmSize = Mathf.Max(c.palmHeight, c.palmWidth);
                Assert.Greater(hi.x - lo.x, 1.4f * palmSize, $"{c.id}: a heavy palm");
                checkedClaws++;
            }
            Assert.AreEqual(2, checkedClaws, "a crusher and a cutter");
        }

        // ------------------------------------------------------------ spines

        // Spine bases lie on the lumpy loft (within the surface noise), the spot list is deterministic, and a mirrored
        // field's left side is the mirror image of its right.
        [Test] public void SpineField_OnTheSurface_Deterministic_Symmetric()
        {
            CreatureSpecies sp = Load("TentacleCrab");
            RigidLoft loft = First<RigidLoft>(sp, l => l.id == "carapace");
            SpineField right = First<SpineField>(sp, f => f.id == "spines.R"), left = First<SpineField>(sp, f => f.id == "spines.L");
            Assert.NotNull(right); Assert.NotNull(left);
            List<SpineField.Spot> a = right.Spots(loft), b = right.Spots(loft), l2 = left.Spots(loft);
            Assert.AreEqual(a.Count, b.Count);
            Assert.Greater(a.Count, right.count / 2, "the scatter places most of its spines");
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].t, b[i].t); Assert.AreEqual(a[i].phi, b[i].phi);
                Assert.AreEqual(a[i].t, l2[i].t, "the same places along");
                Assert.AreEqual(Mathf.PI - a[i].phi, l2[i].phi, 1e-5f, "mirrored round");
                SpineField.Surface(loft, a[i].t, a[i].phi, out Vector3 p, out Vector3 nrm, out _);
                Vector3 smooth = RigidLoft.Point(loft.SectionAt(a[i].t), a[i].phi);
                Assert.LessOrEqual((p - smooth).magnitude, loft.lumpHeight + 1e-5f, "on the lumpy surface");
                Assert.Greater(Vector3.Dot(nrm, p - loft.SectionAt(a[i].t).c), 0f, "the normal points out");
                Vector3 d = right.Direction(nrm, Vector3.forward);
                Assert.Greater(Vector3.Dot(d, nrm), Mathf.Cos((Mathf.Abs(right.leanDeg) + 1f) * Mathf.Deg2Rad), "raked by leanDeg at most");
            }
        }

        // RED for the symmetry: a field on the other side (side flipped) is not the mirror image of the right one.
        [Test] public void Red_SpineField_WrongSide_IsNotMirrored()
        {
            CreatureSpecies sp = Load("TentacleCrab");
            RigidLoft loft = First<RigidLoft>(sp, l => l.id == "carapace");
            SpineField right = First<SpineField>(sp, f => f.id == "spines.R");
            var wrong = (SpineField)right.Clone(); wrong.at.side = 1;
            List<SpineField.Spot> a = right.Spots(loft), w = wrong.Spots(loft);
            float worst = 0f;
            for (int i = 0; i < a.Count; i++) worst = Mathf.Max(worst, Mathf.Abs(Mathf.PI - a[i].phi - w[i].phi));
            Assert.Greater(worst, 0.1f);
        }

        // LimbSpines: perMetre spines per segment (each a horn of 4 + sides rings), every one rigid to its segment's bone.
        [Test] public void LimbSpines_CountAndRigid()
        {
            CreatureSpecies sp = Load("TentacleCrab");
            LimbSpines h = First<LimbSpines>(sp, x => x.id == "legSpines1.R");
            Assert.NotNull(h);
            int li = IndexOf(sp, h.LegId);
            var leg = (ArthropodLeg)KitView.Parts(sp)[li];
            int expected = 0;
            for (int k = h.fromSegment; k <= Mathf.Min(h.toSegment, leg.segments.Length - 1); k++) expected += Mathf.RoundToInt(h.perMetre * leg.segments[k].length);
            Assert.Greater(expected, 10);
            // the species without this part: the vertex count drops by exactly its spines
            CreatureSpecies a = CreatureKitAssets.LoadAsset("TentacleCrab");
            var k2 = ScriptableObject.CreateInstance<CreatureSpecies>();
            k2.name = "TentacleCrab-spines"; k2.scale = a.scale; k2.kitVersion = 1;
            k2.parts = new List<CreaturePart>(a.parts); k2.modules = new List<CreatureModule>(a.modules);
            k2.parts.RemoveAll(p => p is LimbSpines ls && ls.id == "legSpines1");
            Mesh with = CreatureKitAssets.BuildMesh(sp), without = CreatureKitAssets.BuildMesh(k2.Compiled.Runtime);
            int sides = h.sides > 0 ? h.sides : 5;
            int perSpine = (sides + 1) * 5;   // Horn: a base pole, 3 rings (pointed: the last ring is the tip pole), a tip pole
            int diff = with.vertexCount - without.vertexCount;
            Object.DestroyImmediate(with); Object.DestroyImmediate(without); Object.DestroyImmediate(k2);
            TestContext.Out.WriteLine($"{expected} spines per leg, {diff} vertices for the pair");
            Assert.AreEqual(2 * expected * perSpine, diff, "perMetre spines on each segment of both legs");
        }

        // ------------------------------------------------------------ RigidLoft ridges, Tuck fold planes

        [Test] public void RigidLoft_RidgedLumps_Bounded()
        {
            RigidLoft shell = First<RigidLoft>(Load("RockCrab"), l => l.id == "carapace");
            Assert.Greater(shell.lumpRidge, 0f);
            foreach (float ridge in new[] { 0.5f, 1f })
            {
                var r = (RigidLoft)shell.Clone(); r.lumpRidge = ridge;
                float worst = 0f;
                for (int k = 1; k < 40; k++)
                    for (int a = 0; a < 64; a++)
                    {
                        RigidLoft.Section q = r.SectionAt(k / 40f);
                        float phi = 2f * Mathf.PI * a / 64f;
                        worst = Mathf.Max(worst, (r.Lumped(q, phi) - RigidLoft.Point(q, phi)).magnitude);
                    }
                Assert.LessOrEqual(worst, r.lumpHeight + 1e-6f, "ridge " + ridge);
            }
            var plain = (RigidLoft)shell.Clone(); plain.lumpRidge = 0f;
            var tiny = (RigidLoft)shell.Clone(); tiny.lumpRidge = 1e-6f;
            RigidLoft.Section s = plain.SectionAt(0.5f);
            Assert.AreEqual((plain.Lumped(s, 1f) - tiny.Lumped(s, 1f)).magnitude, 0f, 1e-5f, "continuous at 0");
        }

        // segmentYawDeg equal to yawDeg on every segment folds exactly as without it (the per-segment planes are a pure
        // generalisation).
        [Test] public void Tuck_SegmentYaws_EqualToYaw_Identical()
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset("RockCrab");
            Matrix4x4[] Tucked(bool perSegment)
            {
                var k = ScriptableObject.CreateInstance<CreatureSpecies>();
                k.name = "RockCrab-planes"; k.scale = a.scale; k.kitVersion = 1;
                k.parts = new List<CreaturePart>(a.parts); k.modules = new List<CreatureModule>(a.modules);
                var t = (Tuck)k.parts[k.parts.Count - 1].Clone();
                for (int i = 0; i < t.legs.Length; i++)
                {
                    int n = t.legs[i].pitchDeg.Length;
                    t.legs[i].segmentYawDeg = perSegment ? new float[n] : null;
                    if (perSegment) for (int s = 0; s < n; s++) t.legs[i].segmentYawDeg[s] = t.legs[i].yawDeg;
                }
                k.parts[k.parts.Count - 1] = t;
                CreatureSpecies sp = k.Compiled.Runtime;
                var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale, 3);
                for (int f = 0; f < 60; f++) sim.Tick(1f / 60f, new Vector3(0f, 0f, 30f));
                sim.Scare(1f);
                for (int f = 0; f < 60; f++) sim.Tick(1f / 60f, new Vector3(0f, 0f, 30f));
                var bones = new Matrix4x4[sp.BoneCount];
                sim.WriteBoneWorld(bones, 0);
                Object.DestroyImmediate(k);
                return bones;
            }
            Matrix4x4[] x = Tucked(false), y = Tucked(true);
            for (int i = 0; i < x.Length; i++) for (int e = 0; e < 16; e++) Assert.AreEqual(x[i][e], y[i][e], "bone " + i);
        }
    }
}
