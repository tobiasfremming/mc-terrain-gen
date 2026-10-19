using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's Tongue part (a forked tongue on a TentacleChain) on the KomodoDragon: validation; walking and standing it
    // flicks again and again - shoots out to its full length, waves (the tip off the mouth's axis by a visible but
    // bounded amount), draws back until the whole tongue is hidden inside the head; the tube never inverts; Froude
    // (0.9x / 1.1x: the same extension trace and tip path in body units); no allocation. RED: the hidden check bites on a
    // tongue that is out.
    public class CreatureTongueTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Komodo() => CreatureKitAssets.Load("KomodoDragon").Compiled.Runtime;

        static CreatureSim Sim(CreatureSpecies sp, float s, int seed = 1) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed);

        static TongueRuntime TongueOf(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is TongueRuntime t) return t;
            return null;
        }

        static SpeciesCtx Ctx(int joints, bool soft = false) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, soft);

        [Test] public void Tongue_Validate_RejectsBadData()
        {
            var ok = new Tongue { id = "t", at = new PartSocket(1, 0, Vector3.zero) };
            Assert.IsNull(ok.Validate(Ctx(6)));
            Assert.AreEqual(8, ok.BoneCount(Ctx(6)), "one bone per particle");
            Assert.IsNotNull(ok.Validate(Ctx(6, true)), "needs a spine body");
            var bad = (Tongue)ok.Clone(); bad.segments = 3; Assert.IsNotNull(bad.Validate(Ctx(6)), "too few segments");
            bad = (Tongue)ok.Clone(); bad.tipRadius = 0.1f; Assert.IsNotNull(bad.Validate(Ctx(6)), "tip thicker than root");
            bad = (Tongue)ok.Clone(); bad.outSeconds = 0f; Assert.IsNotNull(bad.Validate(Ctx(6)), "instant shoot-out");
            bad = (Tongue)ok.Clone(); bad.intervalMax = 0.1f; Assert.IsNotNull(bad.Validate(Ctx(6)), "interval range reversed");
            bad = (Tongue)ok.Clone(); bad.waveDeg = 60f; Assert.IsNotNull(bad.Validate(Ctx(6)), "wave too wide");
            bad = (Tongue)ok.Clone(); bad.layer = 16; Assert.IsNotNull(bad.Validate(Ctx(6)), "layer outside the table");
        }

        // The posed mesh: vertices of the tongue's bones and every other vertex.
        sealed class Posed
        {
            public Vector3[] v; public int[] tri; public List<Vector4> uv2 = new List<Vector4>(); public Matrix4x4[] bind, world, skin; public Vector3[] pos;
            public int lo, hi;
            public Posed(CreatureSpecies sp, TongueRuntime t)
            {
                Mesh mesh = CreatureKitAssets.BuildMesh(sp);
                v = mesh.vertices; tri = mesh.triangles; mesh.GetUVs(2, uv2);
                Object.DestroyImmediate(mesh);
                bind = CreatureRestPose.BindPoses(sp);
                world = new Matrix4x4[sp.BoneCount]; skin = new Matrix4x4[sp.BoneCount]; pos = new Vector3[v.Length];
                lo = t.FirstBone; hi = lo + t.Part.Segments;
            }
            public bool IsTongue(int i) => uv2[i].x >= lo && uv2[i].x < hi;
            public void Pose(CreatureSim sim)
            {
                sim.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < v.Length; i++)
                {
                    Vector4 s = uv2[i];
                    pos[i] = s.z * skin[(int)s.x].MultiplyPoint3x4(v[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(v[i]);
                }
            }
            // How far the tongue's farthest vertex is from its root, and how far the nearest head vertex (any other
            // vertex within reach) is: the tongue is hidden when it fits in the ball the head's skin leaves round the root.
            public void Hidden(Vector3 root, out float tongue, out float skinGap)
            {
                tongue = 0f; skinGap = float.PositiveInfinity;
                for (int i = 0; i < pos.Length; i++)
                {
                    float d = (pos[i] - root).magnitude;
                    if (IsTongue(i)) tongue = Mathf.Max(tongue, d); else skinGap = Mathf.Min(skinGap, d);
                }
            }
            public string Inverted()
            {
                for (int q = 0; q < tri.Length; q += 3)
                {
                    int i0 = tri[q], i1 = tri[q + 1], i2 = tri[q + 2];
                    if (!IsTongue(i0)) continue;
                    Vector3 rest = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    if (!(Vector3.Dot(now, want) > 0f)) return $"tongue triangle {q / 3} (bones {uv2[i0]}) inverted";
                }
                return null;
            }
        }

        // Walk a weave for 10 s, stand 10 s, walk on: every frame the extension, every 4th the posed tongue.
        [Test] public void Tongue_FlicksOutFully_HidesFully_WavesInRange_NeverInverts()
        {
            CreatureSpecies sp = Komodo();
            float s = sp.scale;
            var sim = Sim(sp, s);
            TongueRuntime t = TongueOf(sim);
            Assert.IsNotNull(t, "the komodo has a tongue");
            var m = new Posed(sp, t);
            float L = t.Part.length * s, worstHidden = 0f, minGap = float.PositiveInfinity, maxTip = 0f, minTip = float.PositiveInfinity, maxOff = 0f;
            int hiddenFrames = 0, outFrames = 0;
            for (int k = 0; k < (int)(30f / kDt); k++)
            {
                float time = k * kDt;
                Vector3 goal = time < 10f || time > 20f ? new Vector3(Mathf.Sin(time * 0.6f) * 8f, 0f, 40f) * s : sim.Joints[0];
                sim.Tick(kDt, goal);
                if (k % 4 != 3) continue;
                m.Pose(sim);
                string inv = m.Inverted();
                Assert.IsNull(inv, $"t={time:F2}: {inv}");
                Vector3 root = t.Root;
                if (t.State == TongueRuntime.Phase.Hidden)
                {
                    m.Hidden(root, out float r, out float gap);
                    worstHidden = Mathf.Max(worstHidden, r / gap); minGap = Mathf.Min(minGap, gap);
                    hiddenFrames++;
                }
                else if (t.State == TongueRuntime.Phase.Flick)
                {
                    TentacleChain c = t.Chain;
                    Vector3 tip = c.DisplayX[c.N - 1];
                    int j = t.Part.at.joint;
                    t.Part.RootLocal(out _, out Quaternion f);
                    Vector3 axis = sim.Rotations[j] * f * Vector3.forward;
                    Vector3 d = tip - root;
                    float along = Vector3.Dot(d, axis);
                    maxTip = Mathf.Max(maxTip, d.magnitude); minTip = Mathf.Min(minTip, d.magnitude);
                    maxOff = Mathf.Max(maxOff, (d - axis * along).magnitude);
                    outFrames++;
                }
            }
            TestContext.Out.WriteLine($"{t.Flicks} flicks; out: tip {minTip / L:F2} .. {maxTip / L:F2} x length from the root, off the mouth's axis <= {maxOff / L:F2} x length; hidden: tongue fits {worstHidden:F2} of the skin's clearance round its root ({minGap * 1000f:F0} mm)");
            Assert.GreaterOrEqual(t.Flicks, 6, "it flicks often");
            Assert.Greater(hiddenFrames, 10); Assert.Greater(outFrames, 10);
            Assert.Less(worstHidden, 1f, "drawn back, the whole tongue is inside the head");
            Assert.Greater(minTip, 0.85f * L, "flicking, it is out at nearly its full length");
            Assert.Less(maxTip, 1.06f * L, "and never longer than it");
            Assert.Greater(maxOff, 0.05f * L, "it waves");
            Assert.Less(maxOff, 0.5f * L, "within a bounded wave");
        }

        // RED: the hidden check bites - a tongue that is out does not fit inside the head.
        [Test] public void Red_TongueOut_IsNotHidden()
        {
            CreatureSpecies sp = Komodo();
            var sim = Sim(sp, sp.scale);
            TongueRuntime t = TongueOf(sim);
            var m = new Posed(sp, t);
            for (int k = 0; k < 1200 && t.State != TongueRuntime.Phase.Flick; k++) sim.Tick(kDt, sim.Joints[0]);
            Assert.AreEqual(TongueRuntime.Phase.Flick, t.State, "it flicked");
            m.Pose(sim);
            m.Hidden(t.Root, out float r, out float gap);
            TestContext.Out.WriteLine($"RED: out, the tongue reaches {r * 1000f:F0} mm from its root, the head's skin {gap * 1000f:F0} mm");
            Assert.Greater(r / gap, 1f, "the hidden check bites");
        }

        // Froude: the extension trace (time / sqrt k) and the tip relative to the head in body units match at 0.9x / 1.1x.
        [Test] public void Tongue_Size_FroudeSimilar()
        {
            CreatureSpecies sp = Komodo();
            List<float> Trace(float k, out List<Vector3> tip)
            {
                var sim = Sim(sp, sp.scale * k);
                TongueRuntime t = TongueOf(sim);
                var e = new List<float>(); tip = new List<Vector3>();
                for (int f = 0; f < (int)(12f / kDt); f++)
                {
                    sim.Tick(kDt * Mathf.Sqrt(k), sim.Joints[0]);
                    e.Add(t.Extension);
                    tip.Add(Quaternion.Inverse(sim.Rotations[0]) * (t.Chain.DisplayX[t.Chain.N - 1] - sim.Joints[0]) / (sp.scale * k));
                }
                return e;
            }
            List<float> a = Trace(0.9f, out List<Vector3> ta), b = Trace(1.1f, out List<Vector3> tb);
            float worstE = 0f, worstTip = 0f; int flick = 0;
            for (int i = 0; i < a.Count; i++)
            {
                worstE = Mathf.Max(worstE, Mathf.Abs(a[i] - b[i]));
                if (a[i] >= 1f && b[i] >= 1f) { worstTip = Mathf.Max(worstTip, (ta[i] - tb[i]).magnitude); flick++; }
            }
            float L = sp.joints.Length * sp.spacing;
            TestContext.Out.WriteLine($"extension traces differ <= {worstE:F4}; tips apart <= {worstTip / L:F4} body lengths over {flick} frames out");
            Assert.Less(worstE, 0.02f, "the same flick timing in Froude time");
            Assert.Greater(flick, 30, "the comparison covers flicks");
            Assert.Less(worstTip, 0.01f * L, "the same tip path in body units");
        }

        [Test] public void Tongue_Flicking_AllocatesNothing()
        {
            GcAllocs.AssertCounterWorks();
            CreatureSpecies sp = Komodo();
            var sim = Sim(sp, sp.scale);
            var bones = new Matrix4x4[sp.BoneCount];
            TongueRuntime t = TongueOf(sim);
            for (int k = 0; k < 120; k++) { sim.Tick(kDt, new Vector3(0f, 0f, 40f)); sim.WriteBoneWorld(bones, 0); }
            int before = t.Flicks;
            int n = GcAllocs.Count(() =>
            {
                for (int k = 0; k < 900; k++)
                {
                    sim.Tick(kDt, k < 500 ? new Vector3(0f, 0f, 40f) : sim.Joints[0]);
                    sim.WriteBoneWorld(bones, 0);
                }
            });
            TestContext.Out.WriteLine($"{t.Flicks - before} flicks, {n} allocations");
            Assert.Greater(t.Flicks - before, 1, "the window covers flicks");
            Assert.AreEqual(0, n, "no allocation per frame");
        }
    }
}
