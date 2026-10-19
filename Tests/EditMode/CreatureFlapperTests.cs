using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's Flapper part (T5.2) on the FinnedSandEel's pectorals: validation, the beat follows the body wave
    // (frequency = undulation x beatsPerWave, Froude-scaled), the pair beats in phase, the amplitude fades to the
    // idle flutter at a standstill, no triangle of the fin flips over a beat, and the size rule. RED proofs: a beat
    // at the wrong rate is told apart, a flipped bone is caught by the flip detector.
    public class CreatureFlapperTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Eel() => CreatureKitAssets.Load("FinnedSandEel");

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        static List<FlapperRuntime> Flappers(CreatureSim sim)
        {
            var l = new List<FlapperRuntime>();
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is FlapperRuntime f) l.Add(f);
            return l;
        }

        static CreatureSim Walker(CreatureSpecies sp, float s) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, 1);

        static Vector3 Ahead(CreatureSim sim) => sim.Joints[0] + Vector3.forward * (10f * sim.SizeScale);

        [Test] public void Flapper_Validate_RejectsBadData()
        {
            var ok = new Flapper { id = "f", at = new PartSocket(2, 1, Vector3.zero), length = 0.03f, rootChord = 0.015f, tipChord = 0.01f };
            Assert.IsNull(ok.Validate(Ctx(10)));
            Assert.AreEqual(2, ok.BoneCount(Ctx(10)), "one bone per segment");
            var bad = (Flapper)ok.Clone(); bad.segments = 4;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "too many segments");
            bad = (Flapper)ok.Clone(); bad.length = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "no span");
            bad = (Flapper)ok.Clone(); bad.at.side = 0;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "no side and not mirrored");
            bad = (Flapper)ok.Clone(); bad.at.joint = 10;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "joint out of range");
            bad = (Flapper)ok.Clone(); bad.beatsPerWave = 0f;
            Assert.IsNotNull(bad.Validate(Ctx(10)), "no beat rate");
        }

        // Beat frequency from upward zero crossings of the root bone's angle over `seconds` of straight walking.
        static float MeasuredHz(CreatureSim sim, FlapperRuntime f, float seconds)
        {
            float prev = f.AngleOf(0), first = -1f, last = -1f; int crossings = 0;
            for (int t = 0; t < (int)(seconds / kDt); t++)
            {
                sim.Tick(kDt, Ahead(sim));
                float a = f.AngleOf(0);
                if (prev < 0f && a >= 0f && f.Amplitude > 1f)
                {
                    if (first < 0f) first = sim.Time; else { last = sim.Time; crossings++; }
                }
                prev = a;
            }
            return crossings > 0 ? crossings / (last - first) : 0f;
        }

        [TestCase(0.9f)] [TestCase(1.1f)] public void Flapper_BeatFrequency_FollowsUndulation(float jitter)
        {
            CreatureSpecies sp = Eel();
            float s = sp.scale * jitter;
            CreatureSim sim = Walker(sp, s);
            List<FlapperRuntime> fl = Flappers(sim);
            Assert.AreEqual(2, fl.Count, "the eel's pectoral pair");
            Flapper part = fl[0].Part;
            float want = sp.undulationHz * part.beatsPerWave / Mathf.Sqrt(s);
            float got = MeasuredHz(sim, fl[0], 12f);
            TestContext.WriteLine($"x{s:F2}: beat {got:F3} Hz, undulation {sp.undulationHz / Mathf.Sqrt(s):F3} Hz x {part.beatsPerWave} = {want:F3} Hz");
            Assert.AreEqual(want, got, 0.03f * want, "the beat follows the body wave (Froude-scaled)");
            Assert.AreEqual(want, fl[0].BeatHz, 1e-4f);
            // RED: a beat at the bare undulation rate (beatsPerWave ignored) would be told apart
            Assert.Greater(Mathf.Abs(sp.undulationHz / Mathf.Sqrt(s) - got), 0.1f * want, "the check tells 1x from 2x");
        }

        // Both fins of the pair beat in phase (the same lift at every instant), each a mirror of the other.
        [Test] public void Flapper_Pair_BeatsInPhase()
        {
            CreatureSpecies sp = Eel();
            CreatureSim sim = Walker(sp, sp.scale);
            List<FlapperRuntime> fl = Flappers(sim);
            Assert.AreEqual(-1, fl[0].Part.Side * fl[1].Part.Side, "one left, one right");
            for (int t = 0; t < 300; t++)
            {
                sim.Tick(kDt, Ahead(sim));
                for (int k = 0; k < fl[0].Part.Segments; k++) Assert.AreEqual(fl[0].AngleOf(k), fl[1].AngleOf(k), 1e-5f, $"bone {k} at t={sim.Time:F2}");
            }
            // at rest (amplitude 0) the tips mirror exactly about the body's midplane
            CreatureSim rest = Walker(sp, 1f);
            List<FlapperRuntime> r = Flappers(rest);
            Vector3 a = r[0].Tip(), b = r[1].Tip();
            Assert.AreEqual(-a.x, b.x, 1e-5f); Assert.AreEqual(a.y, b.y, 1e-5f); Assert.AreEqual(a.z, b.z, 1e-5f);
        }

        [Test] public void Flapper_Amplitude_FadesToIdleAtAStandstill()
        {
            CreatureSpecies sp = Eel();
            CreatureSim sim = Walker(sp, sp.scale);
            FlapperRuntime f = Flappers(sim)[0];
            Assert.AreEqual(0f, f.Amplitude, "the rest pose (bind) has no beat");
            for (int t = 0; t < 600; t++) sim.Tick(kDt, Ahead(sim));
            Assert.AreEqual(f.Part.beatDeg, f.Amplitude, 0.05f * f.Part.beatDeg, "full beat while travelling");
            Vector3 stop = sim.Joints[0];
            for (int t = 0; t < 900; t++) sim.Tick(kDt, stop);
            Assert.AreEqual(f.Part.beatDeg * f.Part.idleAmplitude, f.Amplitude, 0.1f * f.Part.beatDeg, "idle flutter at a standstill");
            // eased: one tick never jumps by more than a few degrees
            float prev = f.Amplitude;
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Ahead(sim)); Assert.Less(Mathf.Abs(f.Amplitude - prev), 2f); prev = f.Amplitude; }
        }

        // ------------------------------------------------------------ no flips

        // Every fin triangle's skinned normal against its rest normal over a full beat cycle while slithering.
        static int Flips(CreatureSpecies sp, CreatureSim sim, Mesh mesh, Matrix4x4[] bind, List<int> tris, Matrix4x4[] bones, int flippedBone)
        {
            var pos = new List<Vector3>(mesh.vertices);
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            sim.WriteBoneWorld(bones, 0);
            if (flippedBone >= 0) bones[flippedBone] = bones[flippedBone] * Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, -1f, 1f));
            int flips = 0;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 r0 = pos[tris[i]], r1 = pos[tris[i + 1]], r2 = pos[tris[i + 2]];
                Vector3 nRest = Vector3.Cross(r1 - r0, r2 - r0);
                Vector3 p0 = Skin(r0, uv2[tris[i]], bones, bind), p1 = Skin(r1, uv2[tris[i + 1]], bones, bind), p2 = Skin(r2, uv2[tris[i + 2]], bones, bind);
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                // rotate the rest normal with the triangle's first bone, then compare
                Matrix4x4 m = bones[(int)uv2[tris[i]].x] * bind[(int)uv2[tris[i]].x];
                if (Vector3.Dot(n, m.MultiplyVector(nRest)) <= 0f) flips++;
            }
            return flips;
        }

        static Vector3 Skin(Vector3 p, Vector4 s, Matrix4x4[] bones, Matrix4x4[] bind)
            => (bones[(int)s.x] * bind[(int)s.x]).MultiplyPoint3x4(p) * s.z + (bones[(int)s.y] * bind[(int)s.y]).MultiplyPoint3x4(p) * s.w;

        static List<int> FinTriangles(Mesh mesh, int first, int count)
        {
            var tris = new List<int>(mesh.triangles);
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            var fin = new List<int>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                int b = (int)uv2[tris[i]].x;
                if (b >= first && b < first + count) { fin.Add(tris[i]); fin.Add(tris[i + 1]); fin.Add(tris[i + 2]); }
            }
            return fin;
        }

        [Test] public void Flapper_NoFlips_OverABeat()
        {
            CreatureSpecies sp = Eel();
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            CreatureSim sim = Walker(sp, 1f);
            List<FlapperRuntime> fl = Flappers(sim);
            int first = fl[0].FirstBone, count = 2 * fl[0].Part.Segments;
            Assert.AreEqual(first + fl[0].Part.Segments, fl[1].FirstBone, "the pair's bones are contiguous");
            List<int> tris = FinTriangles(mesh, first, count);
            Assert.Greater(tris.Count, 60, "the fins have triangles");
            var bones = new Matrix4x4[sp.Compiled.Runtime.BoneCount];
            int worst = 0;
            for (int t = 0; t < 360; t++)
            {
                sim.Tick(kDt, Ahead(sim));
                if (t % 6 == 0) worst = Mathf.Max(worst, Flips(sp, sim, mesh, bind, tris, bones, -1));
            }
            Assert.AreEqual(0, worst, "no fin triangle flips over the beat");
            // RED: a mirrored fin bone is caught
            Assert.Greater(Flips(sp, sim, mesh, bind, tris, bones, first), 0, "the detector bites");
            Object.DestroyImmediate(mesh);
        }

        // ------------------------------------------------------------ size

        [Test] public void Flapper_Size_LengthsXs_RatesOverSqrtS()
        {
            CreatureSpecies sp = Eel();
            float spanA = 0f, spanB = 0f, hzA = 0f, hzB = 0f;
            foreach (float s in new[] { 0.9f, 1.1f })
            {
                CreatureSim sim = Walker(sp, s);
                FlapperRuntime f = Flappers(sim)[0];
                var b = new Matrix4x4[sp.Compiled.Runtime.BoneCount];
                sim.WriteBoneWorld(b, 0);
                float span = (f.Tip() - (Vector3)b[f.FirstBone].GetColumn(3)).magnitude;
                if (s < 1f) { spanA = span; hzA = f.BeatHz; } else { spanB = span; hzB = f.BeatHz; }
            }
            Assert.AreEqual(1.1f / 0.9f, spanB / spanA, 0.01f * 1.1f / 0.9f, "span x s");
            Assert.AreEqual(Mathf.Sqrt(0.9f / 1.1f), hzB / hzA, 0.01f, "beat / sqrt s");
        }
    }
}
