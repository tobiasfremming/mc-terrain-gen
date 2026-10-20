using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests.Scratch
{
    // SCRATCH (kit spike): per species, the runtime spec summary (SPIKE_DIR/<sp>.spec) and walking OBJ frames
    // ("v x y z part nx ny nz dorsal bone", body frame) <sp>_walk.obj / <sp>_walk2.obj.
    public class SpikeDump
    {
        static string Dir => System.Environment.GetEnvironmentVariable("SPIKE_DIR");
        static string Only => System.Environment.GetEnvironmentVariable("SPIKE_ONLY");
        static readonly System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;

        static void Write(string name, KitRender r, Matrix4x4[] bones, Vector3 origin, Quaternion frame)
        {
            Mesh m = r.Mesh;
            var v = m.vertices; var nn = m.normals; var t = m.triangles;
            var uv1 = new List<Vector2>(); m.GetUVs(1, uv1);
            var uv2 = new List<Vector4>(); m.GetUVs(2, uv2);
            var skin = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++) skin[b] = bones[b] * r.BindPoses[b];
            Quaternion inv = Quaternion.Inverse(frame);
            var sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                Vector4 s = uv2[i];
                int b0 = (int)s.x, b1 = (int)s.y;
                Vector3 p = skin[b0].MultiplyPoint3x4(v[i]) * s.z + skin[b1].MultiplyPoint3x4(v[i]) * s.w;
                Vector3 n = skin[b0].MultiplyVector(nn[i]) * s.z + skin[b1].MultiplyVector(nn[i]) * s.w;
                p = inv * (p - origin); n = (inv * n).normalized;
                int bone = s.z >= s.w ? b0 : b1;
                sb.Append(string.Format(ci, "v {0} {1} {2} {3} {4} {5} {6} {7} {8}\n", p.x, p.y, p.z, uv1[i].x, n.x, n.y, n.z, uv1[i].y, bone));
            }
            for (int k = 0; k < t.Length; k += 3) sb.Append($"f {t[k] + 1} {t[k + 1] + 1} {t[k + 2] + 1}\n");
            File.WriteAllText(Path.Combine(Dir, name + ".obj"), sb.ToString());
        }

        static string F(float x) => x.ToString("0.###", ci);

        public static string Summary(CreatureSpecies sp)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"scale {F(sp.scale)} spacing {F(sp.spacing)} joints {sp.JointCount} bodyLen {F(sp.BodyLength)} headCap {F(sp.headCapLength)} tail {F(sp.tailTipLength)} rigidHead {sp.RigidHead}");
            for (int i = 0; i < sp.JointCount; i++) { var j = sp.joints[i]; sb.AppendLine($"  j{i} hw {F(j.halfWidth)} hh {F(j.halfHeight)} clr {F(j.clearance)} bend {F(j.bendLimitDeg)}{(j.rigid ? " rigid" : "")}"); }
            sb.AppendLine($"speed {F(sp.speed)} turn {F(sp.turnRateDeg)} accel {F(sp.accel)} sway {F(sp.gaitSwayDeg)} roll {F(sp.gaitRollDeg)} undul {F(sp.undulationDeg)}@{F(sp.undulationHz)} substeps {sp.subSteps} wave {sp.waveGait} bob {F(sp.gaitBob.amplitude)}");
            for (int l = 0; l < (sp.legs?.Length ?? 0); l++)
            {
                LegSpec q = sp.legs[l];
                if (q.side > 0) continue;
                float total = 0f; if (q.IsArthropod) foreach (var s in q.segments) total += s.length;
                sb.Append($" leg{l} att {q.attachJoint} sh ({F(q.shoulderOffset.x)},{F(q.shoulderOffset.y)},{F(q.shoulderOffset.z)}) rest ({F(q.restOffset.x)},{F(q.restOffset.z)}) gg {q.gaitGroup} thr {F(q.stepThreshold)} dur {F(q.stepDuration)} lift {F(q.stepLift)} over {F(q.overshoot)} feeler {q.feeler}");
                sb.AppendLine($" contact {F(q.contactAngleDeg)} kneeLift {F(q.kneeLiftDeg)} retract {F(q.swingRetract)} elev {F(q.elevMinDeg)}..{F(q.elevMaxDeg)} swing {F(q.forwardSwingLimitDeg)}/{F(q.backSwingLimitDeg)} reach {F(q.kneeRangeDeg)}/{F(q.kneeFlexDeg)} delay {F(q.liftDelay)} foot {F(q.footLength)} toes {q.toeCount} total {F(total)}");
                if (q.IsArthropod)
                    foreach (var s in q.segments)
                        sb.AppendLine($"     len {F(s.length)} r {F(s.radiusStart)}-{F(s.radiusEnd)} rest {F(s.restBendDeg)} [{F(s.minBendDeg)},{F(s.maxBendDeg)}] flex {F(s.flexWeight)} lift {F(s.liftWeight)} tilt {F(s.axisTiltDeg)} knuckle {F(s.knuckle)} band {F(s.bandStart)}-{F(s.bandEnd)}");
            }
            var L = sp.look;
            sb.AppendLine($"look pattern {L.patternType} scale {F(L.patternScale)} contrast {F(L.patternContrast)} smooth {F(L.smoothness)} rim {F(L.rimStrength)} sss {F(L.subsurface)} sheen {F(L.sheen)}");
            return sb.ToString();
        }

        public static void DumpWalk(string name, CreatureSpecies sp, string dir, string tag)
        {
            var r = KitRender.For(sp);
            var bones = new Matrix4x4[r.Bones];
            var w = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, 1f, 1);
            int lo = sp.JointCount, hi = 0;
            for (int l = 0; l < sp.legs.Length; l++) { if (sp.legs[l].feeler) continue; lo = Mathf.Min(lo, sp.legs[l].attachJoint); hi = Mathf.Max(hi, sp.legs[l].attachJoint); }
            if (lo >= hi) { lo = 0; hi = sp.JointCount - 1; }
            Vector3 far = new Vector3(0f, 0f, 60f);
            void Frame(string t)
            {
                w.WriteBoneWorld(bones, 0);
                Vector3 fwd = Vector3.ProjectOnPlane(w.Joints[0] - w.Joints[sp.JointCount - 1], Vector3.up).normalized;
                Vector3 o = 0.5f * (w.Joints[lo] + w.Joints[hi]); o.y = 0f;
                Write(name + tag + "_" + t, r, bones, o, Quaternion.LookRotation(fwd, Vector3.up));
            }
            for (int k = 0; k < 300; k++) w.Tick(1f / 60f, far);
            Frame("walk");
            for (int k = 0; k < 7; k++) w.Tick(1f / 60f, far);
            Frame("walk2");
            // a species with a posture part (Tuck): scared, the tucked pose once it sleeps
            bool tuck = false;
            foreach (IPartRuntime pr in w.PartRuntimes) if (pr is TuckRuntime) tuck = true;
            if (!tuck) return;
            w.Scare(1f);
            for (int k = 0; k < 90; k++) w.Tick(1f / 60f, far);
            Frame("tuck");
        }

        [Test] public void Dump()
        {
            if (string.IsNullOrEmpty(Dir)) Assert.Ignore("SPIKE_DIR not set");
            foreach (string name in (string.IsNullOrEmpty(Only) ? "Tarantula;TreeSpider;DaddyLongLegs;TentacleCrab;RockCrab" : Only).Split(';'))
            {
                CreatureSpecies sp = CreatureKitAssets.Load(name);
                File.WriteAllText(Path.Combine(Dir, name + ".spec"), Summary(sp));
                DumpWalk(name, sp, Dir, "");
                TestContext.WriteLine($"{name}: ok");
            }
        }
    }
}
