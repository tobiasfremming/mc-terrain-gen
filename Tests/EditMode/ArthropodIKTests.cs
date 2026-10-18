using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Test legs (scale 1, metres/degrees) - the prototype's pholcid leg I, tarantula leg IV and darkling beetle
    // leg II (scratchpad spider_design/synergy_ik.py; design 2026-10-17-multi-segment-legs-spider-design.md).
    static class ArthropodTestLegs
    {
        static LegSegment Seg(float len, float r0, float r1, float rest, float lo, float hi, float kappa, float sigma,
                              float knuckle = 0f, float bandStart = 0f, float bandEnd = 0f) => new LegSegment
        {
            length = len, radiusStart = r0, radiusEnd = r1, restBendDeg = rest, minBendDeg = lo, maxBendDeg = hi,
            flexWeight = kappa, liftWeight = sigma, knuckle = knuckle, bandStart = bandStart, bandEnd = bandEnd,
        };

        // coxa, trochanter, femur, patella, tibia, metatarsus, tarsus
        internal static LegSegment[] Pholcid() => new[]
        {
            Seg(0.012f, 0.006f, 0.005f, 10f, -30f, 45f, 0.0f, 0.2f),
            Seg(0.008f, 0.005f, 0.0045f, 30f, -10f, 70f, 0.1f, 0.8f),
            Seg(0.30f, 0.0035f, 0.004f, 25f, -20f, 60f, 0.25f, 0.5f, knuckle: 1.5f, bandStart: 0.85f, bandEnd: 1f),
            Seg(0.025f, 0.004f, 0.0035f, -65f, -130f, 10f, -1.0f, -1.0f, knuckle: 1.2f, bandStart: 0f, bandEnd: 1f),
            Seg(0.32f, 0.003f, 0.0025f, -35f, -90f, 20f, -0.6f, -0.6f, bandStart: 0f, bandEnd: 0.08f),
            Seg(0.40f, 0.0022f, 0.0017f, -20f, -70f, 20f, -0.4f, -0.4f),
            Seg(0.12f, 0.0015f, 0.0008f, 37f, -40f, 90f, 0f, 0f),
        };
        internal static ArthropodLegParams PholcidParams => new ArthropodLegParams
        { contactAngleDeg = 18f, kneeLiftDeg = 20f, sMinDeg = float.NegativeInfinity, sMaxDeg = float.PositiveInfinity };

        internal static LegSegment[] Tarantula() => new[]
        {
            Seg(0.03f, 0.022f, 0.02f, 5f, -30f, 40f, 0.0f, 0.2f),
            Seg(0.02f, 0.02f, 0.018f, 20f, -10f, 60f, 0.1f, 0.7f, knuckle: 1.1f),
            Seg(0.11f, 0.017f, 0.016f, 20f, -20f, 60f, 0.3f, 0.4f, knuckle: 1.15f, bandStart: 0.9f, bandEnd: 1f),
            Seg(0.05f, 0.016f, 0.015f, -55f, -120f, 10f, -1.0f, -1.0f, knuckle: 1.1f, bandStart: 0f, bandEnd: 1f),
            Seg(0.085f, 0.014f, 0.013f, -30f, -80f, 20f, -0.6f, -0.5f, knuckle: 1.1f, bandStart: 0f, bandEnd: 0.12f),
            Seg(0.09f, 0.012f, 0.0105f, -15f, -60f, 20f, -0.4f, -0.3f, knuckle: 1.1f),
            Seg(0.045f, 0.009f, 0.007f, 30f, -40f, 90f, 0f, 0f),
        };
        internal static ArthropodLegParams TarantulaParams => new ArthropodLegParams
        { contactAngleDeg = 25f, kneeLiftDeg = 10f, sMinDeg = float.NegativeInfinity, sMaxDeg = float.PositiveInfinity };

        // coxa, femur (trochanter fused), tibia, tarsus
        internal static LegSegment[] Beetle() => new[]
        {
            Seg(0.02f, 0.016f, 0.015f, -10f, -35f, 20f, 0.0f, 0.1f),
            Seg(0.095f, 0.014f, 0.011f, 45f, -5f, 85f, 0.35f, 0.8f, knuckle: 1.1f),
            Seg(0.11f, 0.008f, 0.006f, -100f, -150f, -40f, -1.0f, -0.8f),
            Seg(0.06f, 0.005f, 0.004f, 35f, -40f, 100f, 0f, 0f),
        };
        internal static ArthropodLegParams BeetleParams => new ArthropodLegParams
        { contactAngleDeg = 30f, kneeLiftDeg = 12f, sMinDeg = float.NegativeInfinity, sMaxDeg = float.PositiveInfinity };

        internal static ArthropodLegModel Model(string which)
        {
            switch (which)
            {
                case "Pholcid": return new ArthropodLegModel(Pholcid(), PholcidParams);
                case "Tarantula": return new ArthropodLegModel(Tarantula(), TarantulaParams);
                default: return new ArthropodLegModel(Beetle(), BeetleParams);
            }
        }
        internal static readonly string[] All = { "Pholcid", "Tarantula", "Beetle" };
    }

    // Prototype parity vectors (numpy float64, synergy_ik.py): foot (x outward, y up) relative to the coxa base,
    // beta, the solved s, and the solved chain's points (coxa .. ankle) in the leg plane.
    static class ArthropodParity
    {
        internal readonly struct Row
        {
            public readonly float fx, fy, beta, s; public readonly float[] pts;
            public Row(float fx, float fy, float beta, float s, float[] pts) { this.fx = fx; this.fy = fy; this.beta = beta; this.s = s; this.pts = pts; }
        }
        internal static readonly Row[] Pholcid =
        {
            new Row(0.8109947f, -0.2220991f, 0.000f, -1.96063f, new[] { 0.0000000f, 0.0000000f, 0.0117413f, 0.0024783f, 0.0177123f, 0.0078024f, 0.1386210f, 0.2823586f, 0.1635822f, 0.2837511f, 0.4389324f, 0.1207104f, 0.6968679f, -0.1850170f }),
            new Row(0.8538225f, -0.3017318f, 0.000f, -8.42953f, new[] { 0.0000000f, 0.0000000f, 0.0119987f, -0.0001748f, 0.0190411f, 0.0036207f, 0.2069603f, 0.2374719f, 0.2318512f, 0.2351386f, 0.4930230f, 0.0502352f, 0.7396958f, -0.2646498f }),
            new Row(0.7185848f, -0.2249028f, 0.000f, 6.50136f, new[] { 0.0000000f, 0.0000000f, 0.0111912f, 0.0043311f, 0.0161377f, 0.0106185f, 0.0762953f, 0.3045251f, 0.1011126f, 0.3075419f, 0.3725772f, 0.1381106f, 0.6044581f, -0.1878208f }),
            new Row(0.8599041f, -0.2729686f, 0.000f, -8.07737f, new[] { 0.0000000f, 0.0000000f, 0.0119946f, 0.0003591f, 0.0188586f, 0.0044684f, 0.1956684f, 0.2468282f, 0.2206334f, 0.2455047f, 0.4884339f, 0.0703395f, 0.7457773f, -0.2358866f }),
            new Row(0.7194000f, -0.2954255f, 0.000f, 4.10224f, new[] { 0.0000000f, 0.0000000f, 0.0117450f, 0.0024606f, 0.0176675f, 0.0078388f, 0.1287461f, 0.2865169f, 0.1537435f, 0.2861530f, 0.4048880f, 0.0878440f, 0.6052732f, -0.2583434f }),
            new Row(0.7057024f, -0.2756774f, 0.000f, 6.03244f, new[] { 0.0000000f, 0.0000000f, 0.0115488f, 0.0032597f, 0.0170692f, 0.0090497f, 0.1054146f, 0.2957466f, 0.1304017f, 0.2965497f, 0.3867076f, 0.1049582f, 0.5915757f, -0.2385953f }),
            new Row(0.7767111f, -0.2628295f, 0.000f, 0.08212f, new[] { 0.0000000f, 0.0000000f, 0.0117939f, 0.0022143f, 0.0178643f, 0.0074249f, 0.1414999f, 0.2807639f, 0.1664986f, 0.2810169f, 0.4303158f, 0.0999079f, 0.6625843f, -0.2257474f }),
            new Row(0.9163378f, -0.2344885f, 10.000f, -22.65411f, new[] { 0.0000000f, 0.0000000f, 0.0118466f, -0.0019128f, 0.0190021f, 0.0016645f, 0.2082095f, 0.2344747f, 0.2332016f, 0.2338434f, 0.5134728f, 0.0794152f, 0.8022110f, -0.1974064f }),
            new Row(0.8101653f, -0.2112271f, 10.000f, -11.51430f, new[] { 0.0000000f, 0.0000000f, 0.0119711f, 0.0008323f, 0.0180247f, 0.0060623f, 0.1306639f, 0.2841134f, 0.1555882f, 0.2860581f, 0.4337434f, 0.1278505f, 0.6960385f, -0.1741451f }),
            new Row(0.6944513f, -0.3094338f, 10.000f, -4.13730f, new[] { 0.0000000f, 0.0000000f, 0.0119836f, 0.0006279f, 0.0180590f, 0.0058326f, 0.1228697f, 0.2869281f, 0.1478631f, 0.2863560f, 0.3936100f, 0.0813963f, 0.5803246f, -0.2723517f }),
        };
        internal static readonly Row[] Tarantula =
        {
            new Row(0.3441677f, -0.0868226f, 0.000f, -4.69330f, new[] { 0.0000000f, 0.0000000f, 0.0299931f, -0.0006450f, 0.0489817f, 0.0056345f, 0.1369577f, 0.0716665f, 0.1855932f, 0.0600656f, 0.2501313f, 0.0047498f, 0.3033839f, -0.0678047f }),
            new Row(0.2846358f, -0.0665719f, 0.000f, 12.90805f, new[] { 0.0000000f, 0.0000000f, 0.0293545f, 0.0061898f, 0.0460902f, 0.0171405f, 0.1058871f, 0.1094678f, 0.1549952f, 0.1000665f, 0.2112263f, 0.0363242f, 0.2438520f, -0.0475540f }),
            new Row(0.3290661f, -0.0492739f, 0.000f, 1.72427f, new[] { 0.0000000f, 0.0000000f, 0.0295880f, 0.0049546f, 0.0469644f, 0.0148572f, 0.1173827f, 0.0993632f, 0.1670585f, 0.0936783f, 0.2344365f, 0.0418594f, 0.2882822f, -0.0302560f }),
            new Row(0.3458898f, -0.0666050f, 0.000f, -4.02790f, new[] { 0.0000000f, 0.0000000f, 0.0299609f, 0.0015314f, 0.0484354f, 0.0091926f, 0.1310555f, 0.0818143f, 0.1803470f, 0.0734267f, 0.2480157f, 0.0219880f, 0.3051059f, -0.0475872f }),
            new Row(0.3322310f, -0.0780689f, 0.000f, -0.49281f, new[] { 0.0000000f, 0.0000000f, 0.0299689f, 0.0013646f, 0.0484388f, 0.0090369f, 0.1296559f, 0.0832244f, 0.1785456f, 0.0727459f, 0.2419096f, 0.0160890f, 0.2914471f, -0.0590511f }),
            new Row(0.2821708f, -0.0804388f, 0.000f, 12.74741f, new[] { 0.0000000f, 0.0000000f, 0.0296661f, 0.0044634f, 0.0470160f, 0.0144123f, 0.1122068f, 0.1030134f, 0.1607020f, 0.0908393f, 0.2133359f, 0.0240959f, 0.2413869f, -0.0614210f }),
            new Row(0.3523714f, -0.0800860f, 0.000f, -6.88287f, new[] { 0.0000000f, 0.0000000f, 0.0299943f, -0.0005836f, 0.0489941f, 0.0056621f, 0.1378366f, 0.0705235f, 0.1867467f, 0.0601407f, 0.2538596f, 0.0079788f, 0.3115875f, -0.0610682f }),
            new Row(0.3190898f, -0.0885515f, 5.000f, -1.79831f, new[] { 0.0000000f, 0.0000000f, 0.0299952f, -0.0005339f, 0.0484994f, 0.0070553f, 0.1279450f, 0.0831368f, 0.1763653f, 0.0706678f, 0.2355585f, 0.0096664f, 0.2783059f, -0.0695337f }),
            new Row(0.3666146f, -0.0820703f, 5.000f, -16.44025f, new[] { 0.0000000f, 0.0000000f, 0.0297534f, -0.0038388f, 0.0491160f, 0.0011702f, 0.1428340f, 0.0587630f, 0.1917436f, 0.0483779f, 0.2616904f, 0.0000825f, 0.3258307f, -0.0630524f }),
            new Row(0.3085699f, -0.0508578f, 5.000f, 2.99664f, new[] { 0.0000000f, 0.0000000f, 0.0295961f, 0.0049062f, 0.0463291f, 0.0158610f, 0.1076634f, 0.1071742f, 0.1573028f, 0.1011799f, 0.2212741f, 0.0452096f, 0.2677860f, -0.0318400f }),
        };
        internal static readonly Row[] Beetle =
        {
            new Row(0.1966348f, -0.0687833f, 0.000f, 1.61561f, new[] { 0.0000000f, 0.0000000f, 0.0198899f, -0.0020953f, 0.0931396f, 0.0583984f, 0.1446733f, -0.0387833f }),
            new Row(0.2055218f, -0.0717866f, 0.000f, -5.31446f, new[] { 0.0000000f, 0.0000000f, 0.0198255f, -0.0026361f, 0.0971029f, 0.0526198f, 0.1535603f, -0.0417866f }),
            new Row(0.1680234f, -0.0775045f, 0.000f, 18.26531f, new[] { 0.0000000f, 0.0000000f, 0.0197044f, -0.0034257f, 0.0908220f, 0.0595607f, 0.1160619f, -0.0475045f }),
            new Row(0.1964968f, -0.0680939f, 0.000f, 1.83944f, new[] { 0.0000000f, 0.0000000f, 0.0199001f, -0.0019962f, 0.0927641f, 0.0589615f, 0.1445352f, -0.0380939f }),
            new Row(0.1864756f, -0.0758788f, 0.000f, 7.03687f, new[] { 0.0000000f, 0.0000000f, 0.0197666f, -0.0030468f, 0.0939071f, 0.0563517f, 0.1345141f, -0.0458788f }),
            new Row(0.1658179f, -0.0818796f, 0.000f, 18.45299f, new[] { 0.0000000f, 0.0000000f, 0.0195586f, -0.0041786f, 0.0929696f, 0.0561192f, 0.1138564f, -0.0518796f }),
            new Row(0.1838608f, -0.0886464f, 0.000f, 5.48104f, new[] { 0.0000000f, 0.0000000f, 0.0193878f, -0.0049104f, 0.0993263f, 0.0464203f, 0.1318993f, -0.0586464f }),
            new Row(0.2176027f, -0.0821128f, 6.000f, -21.64160f, new[] { 0.0000000f, 0.0000000f, 0.0193410f, -0.0050919f, 0.1036254f, 0.0387389f, 0.1656411f, -0.0521128f }),
            new Row(0.2287115f, -0.0761123f, 6.000f, -29.59468f, new[] { 0.0000000f, 0.0000000f, 0.0194565f, -0.0046307f, 0.1048024f, 0.0370956f, 0.1767499f, -0.0461123f }),
            new Row(0.2031510f, -0.0747442f, 6.000f, -8.90633f, new[] { 0.0000000f, 0.0000000f, 0.0196101f, -0.0039299f, 0.0970944f, 0.0510354f, 0.1511895f, -0.0447442f }),
        };
    }

    public class ArthropodIKTests
    {
        static readonly Vector3 Up = Vector3.up, Out = Vector3.right;

        static void Solve(ArthropodLegModel m, Vector3 coxa, Vector3 foot, Vector3[] pts, out ArthropodLegPose pose, float beta = 0f, float scale = 1f, Vector3? normal = null) =>
            ArthropodLegIK.Solve(m, coxa, foot, normal ?? Up, Up, Out, beta, scale, pts, 0, out pose);

        static Vector3 RestFoot(ArthropodLegModel m, Vector3 coxa, float scale = 1f)
        {
            Vector2 f = m.RestFootLocal();
            return coxa + new Vector3(f.x, f.y, 0f) * scale;
        }

        // A reachable foot: the model's own chain at (s, beta), pitched by phi, yawed by psi about up, plus the
        // tarsus on level ground. The solve must recover it exactly.
        static Vector3 PoseFoot(ArthropodLegModel m, Vector3 coxa, float s, float beta, float phiDeg, float psiDeg, float scale)
        {
            m.FK(s, beta, out Vector3 e, out _);
            float c = Mathf.Cos(phiDeg * Mathf.Deg2Rad), sn = Mathf.Sin(phiDeg * Mathf.Deg2Rad);
            Vector3 a = new Vector3(e.x * c - e.y * sn, e.x * sn + e.y * c, 0f);
            Vector3 f = a + new Vector3(m.TarsusLength * m.CosC, -m.TarsusLength * m.SinC, 0f);
            return coxa + Quaternion.AngleAxis(psiDeg, Vector3.up) * f * scale;
        }

        static float SegLen(Vector3[] p, int k) => (p[k + 1] - p[k]).magnitude;

        [Test] public void Models_AreValid()
        {
            foreach (string w in ArthropodTestLegs.All)
            {
                var m = ArthropodTestLegs.Model(w);
                Assert.IsNull(m.Error, w);
                TestContext.Out.WriteLine($"{w}: s [{m.SLo:F1}, {m.SHi:F1}] deg, ankle reach [{m.ReachShort:F3}, {m.ReachLong:F3}] m, knee joint {m.KneeJoint}, rest foot {m.RestFootLocal()}");
                Assert.Less(m.SLo, -5f, w); Assert.Greater(m.SHi, 5f, w);
                Assert.Greater(m.ReachLong, m.ReachShort, w);
            }
        }

        static void Parity(string w, ArthropodParity.Row[] rows)
        {
            var m = ArthropodTestLegs.Model(w);
            var pts = new Vector3[m.PointCount];
            float worstS = 0f, worstP = 0f;
            foreach (var r in rows)
            {
                Solve(m, Vector3.zero, new Vector3(r.fx, r.fy, 0f), pts, out var pose, r.beta);
                Assert.IsFalse(pose.clamped, $"{w}: parity target clamped");
                worstS = Mathf.Max(worstS, Mathf.Abs(pose.s - r.s));
                for (int k = 0; k <= m.Solved; k++)
                    worstP = Mathf.Max(worstP, (pts[k] - new Vector3(r.pts[2 * k], r.pts[2 * k + 1], 0f)).magnitude);
            }
            TestContext.Out.WriteLine($"{w}: {rows.Length} rows, worst |ds| {worstS:E2} deg, worst joint {worstP:E2} m");
            Assert.Less(worstS, 0.01f, w + ": s differs from the prototype");
            Assert.Less(worstP, 5e-5f, w + ": joints differ from the prototype");
        }

        [Test] public void Parity_Prototype_Pholcid() => Parity("Pholcid", ArthropodParity.Pholcid);
        [Test] public void Parity_Prototype_Tarantula() => Parity("Tarantula", ArthropodParity.Tarantula);
        [Test] public void Parity_Prototype_Beetle() => Parity("Beetle", ArthropodParity.Beetle);

        [Test] public void Reach_Exact()
        {
            var rng = new System.Random(11);
            foreach (string w in ArthropodTestLegs.All)
                foreach (float scale in new[] { 1f, 3f })
                {
                    var m = ArthropodTestLegs.Model(w);
                    var pts = new Vector3[m.PointCount];
                    Vector3 coxa = new Vector3(1f, 2f, -3f);
                    float worstFoot = 0f, worstLen = 0f;
                    for (int i = 0; i < 1000; i++)
                    {
                        float s = Mathf.Lerp(m.SLo + 1f, m.SHi - 1f, (float)rng.NextDouble());
                        Vector3 foot = PoseFoot(m, coxa, s, 0f, (float)rng.NextDouble() * 50f - 25f, (float)rng.NextDouble() * 100f - 50f, scale);
                        Solve(m, coxa, foot, pts, out var pose, 0f, scale);
                        Assert.IsFalse(pose.clamped, $"{w} x{scale}: a reachable foot was clamped (s {s:F1})");
                        worstFoot = Mathf.Max(worstFoot, (pts[m.PointCount - 1] - foot).magnitude);
                        for (int k = 0; k < m.SegmentCount; k++)
                            worstLen = Mathf.Max(worstLen, Mathf.Abs(SegLen(pts, k) - m.len[k] * scale));
                    }
                    TestContext.Out.WriteLine($"{w} x{scale}: foot error {worstFoot:E2} m, worst segment length error {worstLen:E2} m");
                    Assert.AreEqual(0f, worstFoot, 0f, $"{w}: the foot must be exactly the target");
                    Assert.Less(worstLen, 1e-5f * m.Total * scale, $"{w}: segment lengths");
                }
        }

        [Test] public void Unreachable_ClampsAlongDirection()
        {
            foreach (string w in ArthropodTestLegs.All)
            {
                var m = ArthropodTestLegs.Model(w);
                var pts = new Vector3[m.PointCount];
                foreach (Vector3 dir in new[] { new Vector3(1f, -0.3f, 0.4f), new Vector3(0.2f, -1f, 0.1f) })
                {
                    Vector3 foot = dir.normalized * (3f * m.Total);
                    Solve(m, Vector3.zero, foot, pts, out var pose);
                    Assert.IsTrue(pose.clamped, w + " far foot not clamped");
                    for (int k = 0; k < m.SegmentCount; k++) Assert.AreEqual(m.len[k], SegLen(pts, k), 1e-5f * m.Total, $"{w} segment {k}");
                    Vector3 ankle = pts[m.Solved];
                    Vector3 h = foot; h.y = 0f;
                    Vector3 X = h.normalized;
                    Vector3 want = foot - (X * m.CosC - Up * m.SinC) * m.TarsusLength;   // the ankle target it reaches toward
                    float ang = Vector3.Angle(ankle, want);
                    Assert.Less(ang, 0.5f, $"{w}: clamped ankle points {ang:F2} deg off the target direction");
                    Assert.AreEqual(m.ReachLong, ankle.magnitude, 1e-4f, w + ": clamped at the longest reach");
                }
            }
        }

        // Relative bends (joints 1..Solved-1) from the output geometry, signed about the leg plane's normal.
        static void Bends(ArthropodLegModel m, Vector3[] pts, Vector3 normal, float[] dst)
        {
            for (int k = 1; k < m.Solved; k++)
                dst[k] = Vector3.SignedAngle(pts[k] - pts[k - 1], pts[k + 1] - pts[k], normal);
        }

        [Test] public void Joints_WithinLimits()
        {
            var rng = new System.Random(5);
            foreach (string w in ArthropodTestLegs.All)
            {
                var m = ArthropodTestLegs.Model(w);
                var pts = new Vector3[m.PointCount]; var b = new float[m.SegmentCount];
                float worst = 0f;
                for (int i = 0; i < 2000; i++)
                {
                    // anywhere in a box around the leg: reachable, too near and too far
                    Vector3 foot = new Vector3((float)rng.NextDouble() * 1.6f - 0.3f, (float)rng.NextDouble() * 1.4f - 1.0f, (float)rng.NextDouble() * 1.6f - 0.8f) * m.Total;
                    float beta = i % 3 == 0 ? m.KneeLiftDeg * (float)rng.NextDouble() : 0f;
                    Solve(m, Vector3.zero, foot, pts, out var pose, beta);
                    Bends(m, pts, pose.planeNormal, b);
                    for (int k = 1; k < m.Solved; k++)
                        worst = Mathf.Max(worst, Mathf.Max(b[k] - m.hi[k], m.lo[k] - b[k]));
                }
                TestContext.Out.WriteLine($"{w}: worst limit excess {worst:F4} deg");
                Assert.LessOrEqual(worst, 0.01f, w + ": a joint left its hinge range");
            }
        }

        // The pose follows a continuous foot path continuously: no joint jumps (a flip moves a joint by up to
        // the leg's length in one step; a smooth solve moves it by about the foot's own step).
        static float WorstJump(ArthropodLegModel m, Func<float, Vector3> path, int steps, out float footStep)
        {
            var a = new Vector3[m.PointCount]; var b = new Vector3[m.PointCount];
            Solve(m, Vector3.zero, path(0f), a, out _);
            float worst = 0f; footStep = 0f;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 f0 = path((i - 1f) / steps), f1 = path((float)i / steps);
                Solve(m, Vector3.zero, f1, b, out _);
                footStep = Mathf.Max(footStep, (f1 - f0).magnitude);
                for (int k = 0; k < m.PointCount; k++) worst = Mathf.Max(worst, (b[k] - a[k]).magnitude);
                var t = a; a = b; b = t;
            }
            return worst;
        }

        [Test] public void NoFlip_ContinuousPath()
        {
            foreach (string w in ArthropodTestLegs.All)
            {
                var m = ArthropodTestLegs.Model(w);
                Vector3 rest = RestFoot(m, Vector3.zero);
                float L = m.Total;
                // yaw sweep +-60 deg, radial and vertical oscillation through and past the reach limits
                Vector3 Sweep(float t)
                {
                    float yaw = 60f * Mathf.Sin(2f * Mathf.PI * t);
                    float rad = rest.x + 0.25f * L * Mathf.Sin(10f * Mathf.PI * t);
                    float y = rest.y + 0.12f * L * Mathf.Sin(6f * Mathf.PI * t);
                    return Quaternion.AngleAxis(yaw, Vector3.up) * new Vector3(rad, y, 0f);
                }
                float jump = WorstJump(m, Sweep, 4000, out float step);
                // a foot circling the coxa's vertical axis, deep below it (the yaw blends toward rest there)
                Vector3 Axis(float t)
                {
                    float r = 0.08f * L * (1f - t);
                    return new Vector3(r * Mathf.Cos(8f * Mathf.PI * t), -0.6f * L, r * Mathf.Sin(8f * Mathf.PI * t));
                }
                float jump2 = WorstJump(m, Axis, 4000, out float step2);
                TestContext.Out.WriteLine($"{w}: sweep worst joint move {jump * 1000f:F2} mm per foot step {step * 1000f:F2} mm; coxa axis {jump2 * 1000f:F2} / {step2 * 1000f:F2} mm");
                Assert.Less(jump, 3f * step + 1e-5f, w + ": the pose jumped on a continuous path (flip)");
                Assert.Less(jump2, 0.02f * L, w + ": the pose jumped near the coxa's axis");
            }
        }

        [Test] public void Stateless_Deterministic()
        {
            var m = ArthropodTestLegs.Model("Pholcid");
            var a = new Vector3[m.PointCount]; var b = new Vector3[m.PointCount]; var junk = new Vector3[m.PointCount];
            Vector3 foot = RestFoot(m, Vector3.zero) + new Vector3(0.05f, 0.03f, 0.1f);
            Solve(m, Vector3.zero, foot, a, out var pa);
            var rng = new System.Random(3);
            for (int i = 0; i < 50; i++)
                Solve(m, Vector3.zero, new Vector3((float)rng.NextDouble(), -(float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f), junk, out _, 10f * (float)rng.NextDouble());
            Solve(m, Vector3.zero, foot, b, out var pb);
            for (int k = 0; k < m.PointCount; k++)
            {
                Assert.AreEqual(a[k].x, b[k].x, 0f); Assert.AreEqual(a[k].y, b[k].y, 0f); Assert.AreEqual(a[k].z, b[k].z, 0f);
            }
            Assert.AreEqual(pa.s, pb.s, 0f);
        }

        [Test] public void MonotoneRun_RejectsBadTemplate()
        {
            var segs = ArthropodTestLegs.Pholcid();
            for (int i = 0; i < segs.Length; i++) segs[i].flexWeight = -segs[i].flexWeight;   // reach grows with s
            Assert.IsNotNull(new ArthropodLegModel(segs, ArthropodTestLegs.PholcidParams).Error, "reversed synergy accepted");
            segs = ArthropodTestLegs.Pholcid();
            segs[3].restBendDeg = segs[3].minBendDeg;                                         // rest on a limit
            Assert.IsNotNull(new ArthropodLegModel(segs, ArthropodTestLegs.PholcidParams).Error, "rest on a joint limit accepted");
            segs = ArthropodTestLegs.Pholcid();
            for (int i = 0; i < segs.Length; i++) segs[i].flexWeight = 0f;
            Assert.IsNotNull(new ArthropodLegModel(segs, ArthropodTestLegs.PholcidParams).Error, "no synergy accepted");
            Assert.IsNotNull(new ArthropodLegModel(new LegSegment[2], ArthropodTestLegs.PholcidParams).Error, "2 segments accepted");
            // the pholcid's joint limits alone allow s down to -75: r(s) folds back there and must be clipped
            var m = ArthropodTestLegs.Model("Pholcid");
            m.LimitRange(0f, out float a, out _);
            TestContext.Out.WriteLine($"pholcid: joint limits allow s >= {a:F1}, monotone run starts at {m.SLo:F1}");
            Assert.Greater(m.SLo, a + 5f, "the non-monotone fold was not clipped");
        }

        [Test] public void Pholcid_RestPose_KneePeakAndLowBody()
        {
            var m = ArthropodTestLegs.Model("Pholcid");
            var pts = new Vector3[m.PointCount];
            Vector3 coxa = Vector3.zero, foot = RestFoot(m, coxa);
            Solve(m, coxa, foot, pts, out var pose);
            float shoulder = -foot.y;                    // coxa height above the ground
            int peak = 0;
            for (int k = 1; k < m.PointCount; k++) if (pts[k].y > pts[peak].y) peak = k;
            float rise = pts[peak].y - coxa.y;
            Vector3 tar = pts[m.PointCount - 1] - pts[m.Solved];
            float tarsusToGround = Vector3.Angle(tar, Vector3.ProjectOnPlane(tar, Up));
            TestContext.Out.WriteLine($"coxa {shoulder:F3} m above ground, knee peak (point {peak}) {rise:F3} m above the coxa, tarsus {tarsusToGround:F1} deg to the ground");
            Assert.AreEqual(0f, pose.s, 1e-3f, "rest foot solves to the rest pose");
            Assert.GreaterOrEqual(rise, 0.9f * shoulder, "the knee must peak well above the body");
            Assert.LessOrEqual(tarsusToGround, 25f, "the tarsus must lie at a shallow angle");
            for (int k = peak + 1; k < m.PointCount; k++) Assert.Less(pts[k].y, pts[k - 1].y + 1e-6f, $"after the knee the leg descends (point {k})");
        }

        [Test] public void TarsusContact_OnSlope()
        {
            foreach (string w in ArthropodTestLegs.All)
            {
                var m = ArthropodTestLegs.Model(w);
                var pts = new Vector3[m.PointCount];
                foreach (float slope in new[] { -25f, 25f })
                {
                    Vector3 n = Quaternion.AngleAxis(slope, Vector3.forward) * Up;   // tilted in the leg plane
                    Vector3 foot = RestFoot(m, Vector3.zero) + new Vector3(0f, 0.02f * m.Total, 0f);
                    Solve(m, Vector3.zero, foot, pts, out var pose, 0f, 1f, n);
                    Assert.IsFalse(pose.clamped, w);
                    Vector3 tar = pts[m.PointCount - 1] - pts[m.Solved];
                    float toGround = Vector3.Angle(tar, Vector3.ProjectOnPlane(tar, n));
                    float contact = Mathf.Atan2(m.SinC, m.CosC) * Mathf.Rad2Deg;
                    Assert.AreEqual(contact, toGround, 0.5f, $"{w} on a {slope} deg slope");
                    Assert.Less(Vector3.Dot(tar, n), 0f, w + ": the tarsus points into the ground");
                }
            }
        }

        // Flexing lifts the knee: a swing that draws the foot in toward the coxa (and lifts it a little) raises
        // the knee well above its planted height - the high-knee step comes from the swing path, not a separate
        // lift. The lift synergy (beta) only reshapes the leg and must keep the foot on its target.
        [Test] public void Retract_LiftsKnee()
        {
            foreach (string w in ArthropodTestLegs.All)
            {
                var m = ArthropodTestLegs.Model(w);
                var a = new Vector3[m.PointCount]; var b = new Vector3[m.PointCount]; var c = new Vector3[m.PointCount];
                Vector3 rest = RestFoot(m, Vector3.zero);
                Solve(m, Vector3.zero, rest, a, out _);
                Vector3 drawn = new Vector3(rest.x * 0.85f, rest.y + 0.04f * m.Total, 0f);
                Solve(m, Vector3.zero, drawn, b, out var pose);
                Solve(m, Vector3.zero, drawn, c, out var shaped, m.KneeLiftDeg);
                int knee = m.KneeJoint;
                float lift = (b[knee].y - a[knee].y) / m.len[knee - 1];
                TestContext.Out.WriteLine($"{w}: foot drawn in 15 %: knee (point {knee}) rises {lift:F3} x its proximal segment, s {pose.s:F1}; with beta {m.KneeLiftDeg}: s {shaped.s:F1}");
                Assert.IsFalse(pose.clamped, w);
                Assert.GreaterOrEqual(lift, 0.10f, w + ": flexing must lift the knee");
                Assert.IsFalse(shaped.clamped, w);
                Assert.AreEqual(0f, (c[m.PointCount - 1] - drawn).magnitude, 1e-6f, w + ": beta keeps the foot on its target");
            }
        }

        [Test] public void Solve_NoAlloc()
        {
            GcAllocs.AssertCounterWorks();
            var m = ArthropodTestLegs.Model("Pholcid");
            var pts = new Vector3[m.PointCount];
            Vector3 rest = RestFoot(m, Vector3.zero);
            ArthropodLegPose pose;
            for (int i = 0; i < 10; i++) Solve(m, Vector3.zero, rest, pts, out pose, 5f);
            int allocs = GcAllocs.Count(() =>
            {
                for (int i = 0; i < 1000; i++)
                    ArthropodLegIK.Solve(m, Vector3.zero, rest + new Vector3(0.0002f * i, 0f, 0.0001f * i), Up, Up, Out, (i % 2) * 7f, 1f, pts, 0, out pose);
            });
            Assert.AreEqual(0, allocs, "the solve allocated");
        }

        // ------------------------------------------------------------------ mesh

        const int kFirstBone = 10, kAttach = 2;

        static LegSegment[] Segs(string w) => w == "Pholcid" ? ArthropodTestLegs.Pholcid() : w == "Tarantula" ? ArthropodTestLegs.Tarantula() : ArthropodTestLegs.Beetle();

        static CreatureMeshLists BuildMesh(string w, out ArthropodLegModel m, out Vector3[] pts)
        {
            m = ArthropodTestLegs.Model(w);
            pts = new Vector3[m.PointCount];
            Vector3 coxa = new Vector3(0.05f, 0.3f, 0f);
            Solve(m, coxa, RestFoot(m, coxa), pts, out var pose);
            var lists = new CreatureMeshLists();
            ArthropodLegMesh.Build(lists, Segs(w), pts, 0, pose.planeNormal, kFirstBone, kAttach, 8, 2, 0f);
            return lists;
        }

        [Test] public void Mesh_OneBonePerVertex()
        {
            foreach (string w in ArthropodTestLegs.All)
            {
                var lists = BuildMesh(w, out var m, out _);
                int blended = 0;
                for (int i = 0; i < lists.uv2.Count; i++)
                {
                    Vector4 s = lists.uv2[i];
                    Assert.AreEqual(1f, s.z + s.w, 1e-6f, "weights sum");
                    Assert.That(s.x, Is.InRange(kFirstBone, kFirstBone + m.SegmentCount - 1), "bone0");
                    if (s.w == 0f) continue;
                    // only the coxa's buried start blends, with the body's attach bone
                    Assert.AreEqual((float)kFirstBone, s.x, 0f, $"{w}: vertex {i} blends a bone other than the coxa");
                    Assert.AreEqual((float)kAttach, s.y, 0f, $"{w}: vertex {i} blends with something other than the attach bone");
                    blended++;
                }
                Assert.Greater(blended, 0, w + ": the coxa start should blend into the body");
                TestContext.Out.WriteLine($"{w}: {lists.pos.Count} vertices, {lists.tris.Count / 3} triangles, {blended} blended (coxa root)");
            }
        }

        [Test] public void Mesh_SinglePartPerTriangle_BandsPresent()
        {
            foreach (string w in ArthropodTestLegs.All)
            {
                var lists = BuildMesh(w, out _, out _);
                int band = 0;
                for (int t = 0; t < lists.tris.Count; t += 3)
                {
                    float p0 = lists.uv1[lists.tris[t]].x, p1 = lists.uv1[lists.tris[t + 1]].x, p2 = lists.uv1[lists.tris[t + 2]].x;
                    Assert.IsTrue(p0 == p1 && p1 == p2, $"{w}: triangle {t / 3} mixes part ids {p0} {p1} {p2}");
                    if (p0 == ArthropodLegMesh.PartBand) band++;
                }
                if (w != "Beetle") Assert.Greater(band, 0, w + ": no band triangles");
            }
        }

        // Every closed piece (capsule, bulb, claw) is watertight once coincident vertices are welded, with every
        // edge used once in each direction (consistent winding), and encloses positive volume (outward normals).
        [Test] public void Mesh_Watertight_OutwardWinding()
        {
            foreach (string w in ArthropodTestLegs.All)
            {
                var lists = BuildMesh(w, out _, out _);
                var weld = new Dictionary<(long, long, long, int), int>(); var id = new int[lists.pos.Count];
                for (int i = 0; i < lists.pos.Count; i++)
                {
                    Vector3 p = lists.pos[i];
                    // welded per bone: two segments' rings around a joint touch on the hinge axis without being one surface
                    var key = ((long)Math.Round(p.x * 1e7), (long)Math.Round(p.y * 1e7), (long)Math.Round(p.z * 1e7), (int)lists.uv2[i].x);
                    if (!weld.TryGetValue(key, out int v)) { v = weld.Count; weld[key] = v; }
                    id[i] = v;
                }
                var directed = new Dictionary<(int, int), int>();
                var parent = new int[weld.Count];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;
                int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
                var tris = new List<(int, int, int)>();
                for (int t = 0; t < lists.tris.Count; t += 3)
                {
                    int a = id[lists.tris[t]], b = id[lists.tris[t + 1]], c = id[lists.tris[t + 2]];
                    if (a == b || b == c || a == c) continue;   // degenerate after welding
                    tris.Add((a, b, c));
                    foreach (var e in new[] { (a, b), (b, c), (c, a) }) { directed.TryGetValue(e, out int k); directed[e] = k + 1; }
                    parent[Find(a)] = Find(b); parent[Find(b)] = Find(c);
                }
                foreach (var kv in directed)
                {
                    Assert.AreEqual(1, kv.Value, $"{w}: edge {kv.Key} used {kv.Value} times in one direction");
                    Assert.IsTrue(directed.ContainsKey((kv.Key.Item2, kv.Key.Item1)), $"{w}: open edge {kv.Key}");
                }
                var wp = new double[weld.Count, 3];
                foreach (var kv in weld) { wp[kv.Value, 0] = kv.Key.Item1 * 1e-7; wp[kv.Value, 1] = kv.Key.Item2 * 1e-7; wp[kv.Value, 2] = kv.Key.Item3 * 1e-7; }
                var vol = new Dictionary<int, double>();
                foreach (var (a, b, c) in tris)
                {
                    int r = Find(a);   // volume relative to a point of the piece (double: the pieces are tiny)
                    double ax = wp[a, 0] - wp[r, 0], ay = wp[a, 1] - wp[r, 1], az = wp[a, 2] - wp[r, 2];
                    double bx = wp[b, 0] - wp[r, 0], by = wp[b, 1] - wp[r, 1], bz = wp[b, 2] - wp[r, 2];
                    double cx = wp[c, 0] - wp[r, 0], cy = wp[c, 1] - wp[r, 1], cz = wp[c, 2] - wp[r, 2];
                    vol.TryGetValue(r, out double v);
                    vol[r] = v + (ax * (by * cz - bz * cy) + ay * (bz * cx - bx * cz) + az * (bx * cy - by * cx)) / 6.0;
                }
                foreach (var kv in vol) Assert.IsTrue(kv.Value > 0.0, $"{w}: a closed piece is inside out");
                TestContext.Out.WriteLine($"{w}: {vol.Count} closed pieces, all watertight and outward");
            }
        }

        // The rigid capsules cover their joints at any bend because both caps are hemispheres centred on the joint:
        // every capsule vertex lies within its radius of the segment's axis [joint k, joint k+1], and each end cap
        // reaches all the way round its joint.
        [Test] public void Mesh_CapsCentredOnJoints()
        {
            foreach (string w in ArthropodTestLegs.All)
            {
                var lists = BuildMesh(w, out var m, out var pts);
                var segs = Segs(w);
                var maxEnd = new float[m.SegmentCount];
                for (int i = 0; i < lists.pos.Count; i++)
                {
                    if (lists.uv1[i].x == ArthropodLegMesh.PartFoot) continue;                // claws
                    int k = (int)lists.uv2[i].x - kFirstBone;
                    Vector3 a = pts[k], b = pts[k + 1], p = lists.pos[i];
                    float t = Mathf.Clamp01(Vector3.Dot(p - a, b - a) / (b - a).sqrMagnitude);
                    float dist = (p - (a + (b - a) * t)).magnitude;
                    float bulb = k + 1 < segs.Length ? segs[k].knuckle * Mathf.Max(segs[k].radiusEnd, segs[k + 1].radiusStart) : 0f;
                    float rmax = Mathf.Max(Mathf.Max(segs[k].radiusStart, segs[k].radiusEnd), bulb);
                    Assert.LessOrEqual(dist, rmax + 1e-6f, $"{w}: vertex {i} of segment {k} is {dist:F5} m from its axis (max {rmax:F5})");
                    if (t >= 1f) maxEnd[k] = Mathf.Max(maxEnd[k], (p - b).magnitude);
                }
                for (int k = 0; k + 1 < m.SegmentCount; k++)
                    Assert.GreaterOrEqual(maxEnd[k], segs[k].radiusEnd - 1e-6f, $"{w}: segment {k}'s end cap does not reach round its joint");
            }
        }
    }
}
