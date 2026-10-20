using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests.Scratch
{
    // SCRATCH (kit spike): each walking leg's rest stance solved by the real IK in the attach frame (flat ground):
    // s (reach synergy, deg; 0 = the authored rest bends), pitch (deg the whole leg plane tilts), the knee's top
    // above the body's top (joint halfHeight), the foot's yaw from the side (+ = forward), reach / leg length.
    public class SpikeRest
    {
        static string Only => System.Environment.GetEnvironmentVariable("SPIKE_ONLY");
        public static string Table(string name, CreatureSpecies sp)
        {
            var sb = new StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int l = 0; l < sp.legs.Length; l++)
            {
                LegSpec q = sp.legs[l];
                if (q.side > 0 || q.feeler || !q.IsArthropod) continue;
                ArthropodLegModel m = sp.LegModel(l);
                Vector3 sh = new Vector3(q.side * q.shoulderOffset.x, q.shoulderOffset.y, q.shoulderOffset.z);
                Vector3 rs = new Vector3(q.side * q.restOffset.x, -sp.joints[q.attachJoint].clearance, q.restOffset.z);
                Vector3 ry = rs; ry.y = 0f;
                var pts = new Vector3[m.PointCount];
                ArthropodLegIK.Solve(m, sh, rs, Vector3.up, Vector3.up, ry.normalized, 0f, 1f, pts, 0, out ArthropodLegPose pose);
                float top = -1e9f; for (int k = 1; k < m.Solved; k++) top = Mathf.Max(top, pts[k].y);
                float yaw = Mathf.Atan2(q.restOffset.z, q.restOffset.x) * Mathf.Rad2Deg;
                Vector2 nat = m.RestFootLocal();
                sb.AppendLine(string.Format(ci, "{0,-13} leg{1} L {2:0.000} s {3,6:0.0} pitch {4,6:0.0} clamped {5}  kneeTop-bodyTop {6,7:0.000} ({7:0.00} L)  yaw {8,5:0}  reach/L {9:0.00}  natural foot ({10:0.000},{11:0.000}) vs target ({12:0.000},{13:0.000})",
                    name, l, m.Total, pose.s, pose.pitchDeg, pose.clamped, top - sp.joints[q.attachJoint].halfHeight, (top - sp.joints[q.attachJoint].halfHeight) / m.Total, yaw,
                    new Vector2(rs.x - sh.x, rs.z - sh.z).magnitude / m.Total, nat.x, nat.y, new Vector2(rs.x - sh.x, rs.z - sh.z).magnitude, rs.y - sh.y));
            }
            return sb.ToString();
        }
        [Test] public void Rest()
        {
            foreach (string name in (string.IsNullOrEmpty(Only) ? "Tarantula;DaddyLongLegs;TentacleCrab;RockCrab;DesertBeetle" : Only).Split(';'))
                TestContext.WriteLine(Table(name, CreatureKitAssets.Load(name)));
        }
    }
}
