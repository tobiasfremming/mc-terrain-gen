using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Creature kit check 13 (kit services 2026-10-20): the POSE SWEEP. MaxBend skins the mesh through one walking
    // circle; the sweep skins it through every phase the species has, driven by its own modules: its own brain
    // (BrainFactory: wander, flight, a ModularBrain over its behaviours) in its lab area (aviary for a flyer), the
    // phases read from CreatureKit.PosePhases (flight: wing-walk, launch, beat, glide, flare, touchdown; Soar; SkyHunt's
    // stoop and pull-up; Burrow's dig-in, dive, under, rise; Tuck; ThreatDisplay; Stumble; any IPosePhases). Every
    // sample checks:
    //   inverted   a triangle whose skinned winding turned against its bind winding (folded through itself)
    //   normal     a vertex normal that no longer faces the way its own triangle does (shading flips), or that
    //              blended to nearly nothing (two bones turned against each other), where the bind was clean
    //   kink       a (non-rigid) spine joint bent more than kKinkMargin past its own bend limit (the chain's limit
    //              is soft: walking turns run 10-25 deg over it; an accordion fold between two joints is 60-100)
    // Transitions (a phase change) are sampled every frame for a while: that is where the kinks are.
    // Duration: at least kMinSeconds, then until every required phase was seen (and kTailSeconds more), at most
    // kMaxSeconds (all x sqrt s: Froude time). A required phase never reached is itself a problem (the sweep does not
    // bite there). Triangles degenerate at bind or now (zero area: unseen) are not judged.
    // Reports name the phase, the time, the part (bone -> layout range) and the world positions.
    public static class KitPoseSweep
    {
        public const float kDt = 1f / 60f;
        public const float kKinkMargin = 45f;      // deg over a joint's own bend limit
        public const float kMinSeconds = 90f, kTailSeconds = 30f, kMaxSeconds = 360f;
        public const float kNormalCollapse = 0.2f;  // a blended unit normal shorter than this is a pinch
        const float kDegenerate = 1e-20f;           // squared area (m^4 at scale 1) below which a triangle is not judged
        const float kCleanAtBind = 0.5f;            // a vertex counts as clean at bind when its normal is this close to its faces'
        const float kTransitionSeconds = 0.6f;      // after a phase change every frame is sampled (x sqrt s)
        const int kEvery = 6;                       // otherwise every 6th frame (10 Hz)

        public sealed class PhaseStats
        {
            public int samples;
            public float maxBend, maxBendOver = float.NegativeInfinity, minDot = float.PositiveInfinity, minNormal = float.PositiveInfinity;
            public int bendJoint = -1;
        }

        public sealed class Result
        {
            public string Problem;                       // the first defect found (or a coverage gap); null = clean
            public readonly List<string> Defects = new List<string>();   // every defect found (one per phase and kind)
            public readonly SortedDictionary<string, PhaseStats> Phases = new SortedDictionary<string, PhaseStats>(StringComparer.Ordinal);
            public readonly List<string> Missing = new List<string>();   // required "channel=phase" never seen
            public readonly List<string> Seen = new List<string>();      // every "channel=phase" seen
            public float Seconds;
            public int Samples;

            public string Table()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"  {"phase",-34} {"samples",7} {"max bend",9} {"over lim",9} {"min tri dot",12} {"min |n|",8}");
                foreach (var kv in Phases)
                {
                    PhaseStats s = kv.Value;
                    sb.AppendLine($"  {kv.Key,-34} {s.samples,7} {s.maxBend,8:F1}d {(s.maxBendOver > -1e6f ? s.maxBendOver.ToString("F1") + "d" : "-"),9} {s.minDot,12:E1} {s.minNormal,8:F2}");
                }
                if (Missing.Count > 0) sb.AppendLine("  never reached: " + string.Join(", ", Missing));
                return sb.ToString();
            }
        }

        // The brain the sweep runs: the species' own (BrainFactory), else its wander.
        public static ICreatureBrain BrainOf(CreatureSpecies sp, EnclosureArea area, int seed)
            => BrainFactory.Create(sp, area, seed) ?? WanderBrain.For(sp, area, seed);

        // The check's run: the adaptive duration above. seconds > 0: exactly that long instead (x sqrt s; tools).
        // stopAtFirst: return at the first defect (the check); false: the whole run, every defect (one per phase and
        // kind: the report tools). onFrame: every tick, the bones just written (dump / trace tools; a RED may change
        // them: the sample judges what it leaves).
        public static Result Run(CreatureSpecies sp, float sizeScale, int seed, float seconds = 0f, bool stopAtFirst = true,
                                 Action<CreatureSim, string, Matrix4x4[]> onFrame = null)
        {
            var res = new Result();
            EnclosureArea area = KitChecks.LabArea(sp);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, sizeScale, seed) { Bounds = area };
            ICreatureBrain brain = BrainOf(sp, area, seed);
            List<IPosePhases> channels = PosePhases.Collect(sim, brain);

            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = mesh.vertices; Vector3[] nrm = mesh.normals; int[] tri = mesh.triangles;
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            Object.DestroyImmediate(mesh);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var world = new Matrix4x4[sp.BoneCount]; var skin = new Matrix4x4[sp.BoneCount];
            var pos = new Vector3[v.Length]; var nor = new Vector3[v.Length]; var norLen = new float[v.Length];
            bool[] cleanAtBind = CleanAtBind(v, nrm, tri);

            bool fixedLength = seconds > 0f;
            int ticks = Mathf.CeilToInt((fixedLength ? seconds : kMaxSeconds) * sim.TimeScale / kDt);
            int minTicks = Mathf.CeilToInt(kMinSeconds * sim.TimeScale / kDt), tail = Mathf.CeilToInt(kTailSeconds * sim.TimeScale / kDt);
            int covered = -1;
            var required = new List<string>();
            for (int c = 0; c < channels.Count; c++)
                for (int p = 0; p < channels[c].PhaseCount; p++)
                    if (channels[c].PhaseRequired(p)) required.Add(channels[c].PhaseChannel + "=" + channels[c].PhaseName(p));
            int transitionTicks = Mathf.CeilToInt(kTransitionSeconds * sim.TimeScale / kDt);
            string last = null; int hot = 0;
            var seen = new HashSet<string>();
            var reported = new HashSet<string>();
            for (int t = 0; t < ticks; t++)
            {
                sim.Tick(kDt, brain.Target(sim, kDt));
                // which phases now
                for (int c = 0; c < channels.Count; c++)
                {
                    int p = channels[c].CurrentPhase;
                    if (p >= 0 && seen.Add(channels[c].PhaseChannel + "=" + channels[c].PhaseName(p))) res.Seen.Add(channels[c].PhaseChannel + "=" + channels[c].PhaseName(p));
                }
                if (!fixedLength)
                {
                    if (covered < 0 && required.TrueForAll(seen.Contains)) covered = t;
                    if (covered >= 0 && t >= minTicks && t >= covered + tail) break;
                }
                string label = PosePhases.Label(channels);
                if (label != last) { hot = transitionTicks; last = label; }
                bool sample = hot > 0 || t % kEvery == 0;
                if (hot > 0) hot--;
                if (onFrame != null) { sim.WriteBoneWorld(world, 0); onFrame(sim, label, world); }
                if (!sample) continue;
                if (onFrame == null) sim.WriteBoneWorld(world, 0);
                if (!res.Phases.TryGetValue(label, out PhaseStats st)) res.Phases[label] = st = new PhaseStats();
                st.samples++; res.Samples++;

                // spine bends against the joints' own limits (rigid joints: the rigid head / body is drawn rigid)
                for (int i = 1; i < sp.JointCount - 1; i++)
                {
                    if (sp.joints[i].rigid) continue;
                    float a = Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1]);
                    float over = a - sp.joints[i].bendLimitDeg;
                    if (a > st.maxBend) st.maxBend = a;
                    if (over > st.maxBendOver) { st.maxBendOver = over; st.bendJoint = i; }
                    if (over > kKinkMargin && reported.Add(label + "|kink"))
                    {
                        string d = $"{label} t={sim.Time:F2} s: spine joint {i} bent {a:F1} deg, its limit {sp.joints[i].bendLimitDeg:F1} (+{kKinkMargin:F0}) at {V(sim.Joints[i])}";
                        res.Defects.Add("kink: " + d);
                        if (stopAtFirst) { res.Problem = res.Defects[0]; return Done(res, sim); }
                    }
                }

                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector4 s = uv2[i];
                    Matrix4x4 m0 = skin[(int)s.x], m1 = skin[(int)s.y];
                    pos[i] = s.z * m0.MultiplyPoint3x4(v[i]) + s.w * m1.MultiplyPoint3x4(v[i]);
                    Vector3 a0 = m0.MultiplyVector(nrm[i]), a1 = m1.MultiplyVector(nrm[i]);
                    nor[i] = s.z * a0 + s.w * a1;
                    // the blend's length against its parts' (1 = the bones agree; a rigid bone's scale cancels out)
                    float parts = s.z * a0.magnitude + s.w * a1.magnitude;
                    norLen[i] = parts > 1e-12f ? nor[i].magnitude / parts : 1f;
                }
                for (int k = 0; k < tri.Length; k += 3)
                {
                    int i0 = tri[k], i1 = tri[k + 1], i2 = tri[k + 2];
                    Vector3 rest = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    double nn = (double)now.magnitude * want.magnitude;
                    if (!(nn > kDegenerate * sizeScale * sizeScale * sizeScale * sizeScale) || rest.sqrMagnitude < kDegenerate) continue;   // no area: unseen
                    float dot = (float)(((double)now.x * want.x + (double)now.y * want.y + (double)now.z * want.z) / nn);
                    if (dot < st.minDot) st.minDot = dot;
                    if (dot < 0f)
                    {
                        if (reported.Add(label + "|inverted"))
                        {
                            res.Defects.Add("inverted: " + Where(sp, label, sim.Time, k / 3, i0, i1, i2, uv2, pos, sizeScale));
                            if (stopAtFirst) { res.Problem = res.Defects[0]; return Done(res, sim); }
                        }
                        continue;
                    }
                    if (!cleanAtBind[k / 3]) continue;
                    // shading: every corner's normal still on its triangle's side, and not pinched to nothing
                    float area2 = now.magnitude;
                    if (area2 < 1e-12f * sizeScale * sizeScale) continue;
                    Vector3 fn = now / area2;
                    for (int q = 0; q < 3; q++)
                    {
                        int vi = tri[k + q];
                        float len = norLen[vi];
                        if (len < st.minNormal) st.minNormal = len;
                        bool pinched = len < kNormalCollapse;
                        bool flipped = !pinched && Vector3.Dot(nor[vi], fn) < 0f;
                        if ((pinched || flipped) && reported.Add(label + "|normal"))
                        {
                            res.Defects.Add($"normal ({(pinched ? $"pinched to {len:F2}" : "faces away from its triangle")}): " + Where(sp, label, sim.Time, k / 3, i0, i1, i2, uv2, pos, sizeScale));
                            if (stopAtFirst) { res.Problem = res.Defects[0]; return Done(res, sim); }
                        }
                    }
                }
            }
            // coverage: every required phase seen
            for (int c = 0; c < channels.Count; c++)
                for (int p = 0; p < channels[c].PhaseCount; p++)
                    if (channels[c].PhaseRequired(p) && !seen.Contains(channels[c].PhaseChannel + "=" + channels[c].PhaseName(p)))
                        res.Missing.Add(channels[c].PhaseChannel + "=" + channels[c].PhaseName(p));
            if (res.Defects.Count > 0) res.Problem = res.Defects[0];
            else if (res.Missing.Count > 0)
                res.Problem = $"never reached {string.Join(", ", res.Missing)} in {seconds:F0} s (x sqrt s): the sweep does not bite there (seen: {string.Join(", ", res.Seen)})";
            return Done(res, sim);
        }

        static Result Done(Result r, CreatureSim sim) { r.Seconds = sim.Time; return r; }

        static string V(Vector3 p) => $"({p.x:F3}, {p.y:F3}, {p.z:F3})";

        // Which triangles are clean at bind: every corner's normal within kCleanAtBind of the face's.
        static bool[] CleanAtBind(Vector3[] v, Vector3[] n, int[] tri)
        {
            var ok = new bool[tri.Length / 3];
            for (int k = 0; k < tri.Length; k += 3)
            {
                Vector3 f = Vector3.Cross(v[tri[k + 1]] - v[tri[k]], v[tri[k + 2]] - v[tri[k]]);
                if (f.sqrMagnitude < 1e-20f) continue;
                f.Normalize();
                ok[k / 3] = Vector3.Dot(n[tri[k]], f) > kCleanAtBind && Vector3.Dot(n[tri[k + 1]], f) > kCleanAtBind && Vector3.Dot(n[tri[k + 2]], f) > kCleanAtBind;
            }
            return ok;
        }

        // The part a bone belongs to, by the species' bone layout ("leg2.L bone 3", "spine joint 4", "eye 0").
        public static string BonePart(CreatureSpecies sp, int bone)
        {
            if (bone < sp.JointCount) return "spine joint " + bone;
            foreach (BoneRange r in sp.Layout.Ranges)
                if (bone >= r.Start && bone < r.Start + r.Count) return $"{r.Id} bone {bone - r.Start}";
            for (int e = 0; e < 2; e++) if (sp.EyeBone(e) == bone) return "eye " + e;
            return "bone " + bone;
        }

        static string Where(CreatureSpecies sp, string label, float time, int triangle, int i0, int i1, int i2, List<Vector4> uv2, Vector3[] pos, float s)
        {
            Vector4 w = uv2[i0];
            int bone = w.z >= w.w ? (int)w.x : (int)w.y;
            return $"{label} t={time:F2} s: triangle {triangle} of {BonePart(sp, bone)} (bones {(int)w.x}/{(int)w.y}) at {V(pos[i0])} {V(pos[i1])} {V(pos[i2])} (x{s:F2})";
        }
    }
}
