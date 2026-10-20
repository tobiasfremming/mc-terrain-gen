using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests.Scratch
{
    // SCRATCH (kit spike): gait metrics walking straight on flat ground, 8 s after a 4 s warm-up, at scale 1.
    // speed (body lengths/s), stepHz per leg, duty factor, lift / leg length, step time in Froude units
    // (t sqrt(g / L)), max legs in swing, body bob (cm), pitch/roll range (deg), knee rise above the shoulder / L.
    public class SpikeGait
    {
        static string Only => System.Environment.GetEnvironmentVariable("SPIKE_ONLY");

        public static string Metrics(string name, CreatureSpecies sp)
        {
            var w = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, 1f, 1);
            int n = sp.legs.Length;
            Vector3 far = new Vector3(0f, 0f, 200f);
            float dt = 1f / 60f;
            for (int k = 0; k < 240; k++) w.Tick(dt, far);
            int steps = 0, swingTicks = 0, walkLegs = 0, maxSwing = 0, ticks = 480;
            float maxLift = 0f, legLen = 0f, yMin = 1e9f, yMax = -1e9f, pMin = 1e9f, pMax = -1e9f, rMin = 1e9f, rMax = -1e9f, kneeRise = 0f;
            bool[] was = new bool[n];
            for (int l = 0; l < n; l++)
            {
                if (sp.legs[l].feeler) continue;
                walkLegs++;
                float L = 0f; if (sp.legs[l].IsArthropod) foreach (var s in sp.legs[l].segments) L += s.length; else L = sp.legs[l].upperLength + sp.legs[l].lowerLength;
                legLen += L;
            }
            legLen /= Mathf.Max(1, walkLegs);
            var bones = new Matrix4x4[sp.BoneCount];
            Vector3 z0 = w.Joints[0];
            float stepTime = 0f; int stepCount = 0; float[] start = new float[n];
            for (int k = 0; k < ticks; k++)
            {
                w.Tick(dt, far);
                int sw = 0;
                Vector3 mid = 0.5f * (w.Joints[0] + w.Joints[sp.JointCount - 1]);
                Vector3 fwd = (w.Joints[0] - w.Joints[sp.JointCount - 1]).normalized;
                yMin = Mathf.Min(yMin, mid.y); yMax = Mathf.Max(yMax, mid.y);
                float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg; pMin = Mathf.Min(pMin, pitch); pMax = Mathf.Max(pMax, pitch);
                if (k % 10 == 0) w.WriteBoneWorld(bones, 0);
                for (int l = 0; l < n; l++)
                {
                    if (sp.legs[l].feeler) continue;
                    LegState st = w.Legs[l];
                    if (st.stepping) { sw++; swingTicks++; maxLift = Mathf.Max(maxLift, st.foot.y); }
                    if (st.stepping && !was[l]) { steps++; start[l] = k * dt; }
                    if (!st.stepping && was[l]) { stepTime += k * dt - start[l]; stepCount++; }
                    was[l] = st.stepping;
                    if (k % 10 == 0 && sp.legs[l].IsArthropod)
                    {
                        float sh = bones[sp.LegBone(l, 0)].GetColumn(3).y;
                        for (int s = 1; s < sp.legs[l].segments.Length; s++) kneeRise = Mathf.Max(kneeRise, (bones[sp.LegBone(l, s)].GetColumn(3).y - sh) / legLen);
                    }
                }
                maxSwing = Mathf.Max(maxSwing, sw);
            }
            float T = ticks * dt;
            float dist = (w.Joints[0] - z0).magnitude;
            float st0 = stepCount > 0 ? stepTime / stepCount : 0f;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(ci, "{0,-14} L {1:0.000} body {2:0.000}  speed {3:0.00} BL/s  {4:0.00} L/s  stepHz {5:0.00}  duty {6:0.00}  lift/L {7:0.000}  stepT {8:0.000}s Froude {9:0.00}  maxSwing {10}/{11}  bob {12:0.0}mm  pitch {13:0.0}deg  kneeRise/L {14:0.00}",
                name, legLen, sp.BodyLength, dist / T / sp.BodyLength, dist / T / legLen, steps / T / Mathf.Max(1, walkLegs), 1f - swingTicks / (float)(ticks * Mathf.Max(1, walkLegs)),
                maxLift / legLen, st0, st0 * Mathf.Sqrt(9.81f / legLen), maxSwing, walkLegs, (yMax - yMin) * 1000f, pMax - pMin, kneeRise);
        }

        [Test] public void Gait()
        {
            foreach (string name in (string.IsNullOrEmpty(Only) ? "Tarantula;TreeSpider;DaddyLongLegs;TentacleCrab;RockCrab;DesertBeetle;GiantCentipede" : Only).Split(';'))
                TestContext.WriteLine(Metrics(name, CreatureKitAssets.Load(name)));
        }
    }
}
