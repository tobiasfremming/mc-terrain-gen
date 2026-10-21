using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Dewlap (Apex redesign §1 throat flap + flap motion, §5.3): the throat sail on its own swinging chain. Fixture: the
    // CrestBeakHead fixture (CreatureFlowBodyTests' steep neck re-keyed to the head, its hind leg, Eyes, the beaked head on
    // joint 0) with a Dewlap hanging from body joints 1..7 (the neck; the chest centre is joint ~6.9), five stations of
    // three links.
    public class CreatureDewlapTests
    {
        static readonly CreatureModule[] kWalk = { new Walk { speed = 0.8f, turnRateDeg = 160f, accel = 2f, turnSlowdown = 0.5f } };

        internal static Dewlap Flap() => new Dewlap
        {
            id = "dewlap", fromJoint = 1, toJoint = 7, stations = 5, links = 3,
            depthAt = new[] { 0.12f, 0.2f, 0.24f, 0.22f, 0.16f },
        };

        static List<CreaturePart> Parts(bool head, Dewlap d)
        {
            var parts = new List<CreaturePart> { CreatureCrestBeakHeadTests.Neck(), CreatureFlowBodyTests.HindLeg(), CreatureKitComposeTests.TwoEyes() };
            if (head) parts.Add(CreatureCrestBeakHeadTests.Head());
            if (d != null) parts.Add(d);
            return parts;
        }

        internal static CreatureSpecies Species(Dewlap d) => FlowKit.Build("DewlapFixture", 1f, Parts(true, d), kWalk);

        static DewlapRuntime Runtime(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is DewlapRuntime d) return d;
            return null;
        }

        static Dewlap Part(CreatureSpecies sp)
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is Dewlap d) return d;
            return null;
        }

        static int VertexCount(List<CreaturePart> parts)
        {
            Mesh mesh = FlowKit.Mesh(FlowKit.Build("DewlapCount", 1f, parts, kWalk));
            int n = mesh.vertexCount;
            Object.DestroyImmediate(mesh);
            return n;
        }

        // ------------------------------------------------------------ below the neck, never inside it

        // Möller-Trumbore: the ray o + t d (t > 0) crosses triangle abc.
        static bool Crosses(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 e1 = b - a, e2 = c - a, pv = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            Vector3 tv = o - a;
            float u = Vector3.Dot(tv, pv) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, qv) * inv;
            if (v < 0f || u + v > 1f) return false;
            return Vector3.Dot(e2, qv) * inv > 0f;
        }

        // distance from p to triangle abc (Ericson, closest point on a triangle)
        static float DistToTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return (p - a).magnitude;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return (p - b).magnitude;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return (p - (a + ab * (d1 / (d1 - d3)))).magnitude;
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return (p - c).magnitude;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return (p - (a + ac * (d2 / (d2 - d6)))).magnitude;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).magnitude;
            float den = 1f / (va + vb + vc);
            return (p - (a + ab * (vb * den) + ac * (vc * den))).magnitude;
        }

        // The posed vertex p is inside the posed (closed) body: an odd number of crossings along a fixed skew ray.
        static bool InsideBody(CreaturePteroWingTests.Skinned m, List<int> bodyTris, Vector3 p)
        {
            Vector3 d = new Vector3(0.317f, -0.829f, 0.461f).normalized;
            int n = 0;
            for (int k = 0; k < bodyTris.Count; k += 3)
                if (Crosses(p, d, m.P[bodyTris[k]], m.P[bodyTris[k + 1]], m.P[bodyTris[k + 2]])) n++;
            return (n & 1) == 1;
        }

        // null, or the first pose in which the sail leaves its place: a dewlap vertex above the lateral plane of the
        // nearest station joint (fromJoint..toJoint) or inside the body (the FlowBody's posed skin; the root rows -
        // skinned to body joints only, tucked Dewlap.RootTuck under the throat's facets - are exempt from this one), or a
        // dewlap link bone pointing above its station's lateral plane. Poses: the rest, walking 10 s, a tight circle 10 s
        // (every 0.5 s).
        static string BelowProblem(Dewlap d)
        {
            CreatureSpecies sp = Species(d);
            Dewlap part = Part(sp);
            int bodyEnd = VertexCount(Parts(false, null));   // legs, eyes and the FlowBody: the head and the sail come after
            int first = VertexCount(Parts(true, null));
            var m = new CreaturePteroWingTests.Skinned(sp);
            Assert.Greater(m.V.Length - first, 200, "the sail has vertices");
            var bodyTris = new List<int>();
            for (int k = 0; k < m.T.Length; k += 3)
            {
                int i0 = m.T[k], i1 = m.T[k + 1], i2 = m.T[k + 2];
                if (i0 >= bodyEnd || i1 >= bodyEnd || i2 >= bodyEnd) continue;
                if (m.Part(i0) != PartMeshEmitter.PartBody || m.Part(i1) != PartMeshEmitter.PartBody || m.Part(i2) != PartMeshEmitter.PartBody) continue;
                bodyTris.Add(i0); bodyTris.Add(i1); bodyTris.Add(i2);
            }
            Assert.Greater(bodyTris.Count, 3000, "the body's triangles");
            int start = sp.Layout.Start(KitView.Parts(sp).Count - 1);

            CreatureSim sim = FlowKit.Sim(sp, 1f);
            string Check(string when)
            {
                m.Pose(sim);
                for (int i = first; i < m.P.Length; i++)
                {
                    Vector3 p = m.P[i];
                    int best = part.fromJoint; float bd = float.MaxValue;
                    for (int j = part.fromJoint; j <= part.toJoint; j++) { float dd = (sim.Joints[j] - p).sqrMagnitude; if (dd < bd) { bd = dd; best = j; } }
                    float h = Vector3.Dot(p - sim.Joints[best], sim.Rotations[best] * Vector3.up);
                    if (!(h < 0f)) return $"{when}: sail vertex {i - first} is {h * 1000f:F1} mm above joint {best}'s lateral plane";
                    bool root = m.Uv2[i].x < sp.JointCount && m.Uv2[i].y < sp.JointCount;
                    if (!root && InsideBody(m, bodyTris, p))
                    {
                        float dist = float.MaxValue;
                        for (int k = 0; k < bodyTris.Count; k += 3) dist = Mathf.Min(dist, DistToTri(p, m.P[bodyTris[k]], m.P[bodyTris[k + 1]], m.P[bodyTris[k + 2]]));
                        return $"{when}: sail vertex {i - first} is {dist * 1000f:F2} mm inside the body";
                    }
                }
                Matrix4x4[] w = FlowKit.Bones(sim);
                for (int k = 0; k < part.stations; k++)
                {
                    Dewlap.StationFrame(sim.Joints, sim.Rotations, part.StationJoint(k), out _, out Quaternion q);
                    for (int l = 0; l < part.links; l++)
                    {
                        Vector3 dir = -(Vector3)w[start + k * part.links + l].GetColumn(1);
                        if (!(Vector3.Dot(dir, q * Vector3.up) < 0f)) return $"{when}: station {k} link {l} points above its station's lateral plane";
                    }
                }
                return null;
            }

            string e = Check("rest");
            if (e != null) return e;
            for (int t = 1; t <= 20; t++)
            {
                FlowKit.Run(sim, 0.5f, new Vector3(0f, 0f, 30f));
                if ((e = Check($"walking t={t * 0.5f:F1} s")) != null) return e;
            }
            sim = FlowKit.Sim(sp, 1f);
            Vector3 centre = new Vector3(0f, 0f, 0.25f);
            for (int t = 0; t < 600; t++)
            {
                float a = t / 60f * 3f;
                sim.Tick(FlowKit.kDt, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.12f);
                if (t % 30 == 29 && (e = Check($"circle t={(t + 1) / 60f:F1} s")) != null) return e;
            }
            return null;
        }

        [Test] public void Dewlap_HangsBelowTheNeck() => Assert.IsNull(BelowProblem(Flap()));

        [Test] public void Red_Dewlap_Above()
        {
            Dewlap d = Flap(); d.gravityDeg = -40f;   // gravity reversed: the sail curls up toward the head
            Assert.IsNull(BelowProblem(d), "control: with the plane stop even a reversed gravity keeps the sail below the neck");
            Dewlap.NoPlaneClampForTest = true;
            try
            {
                string e = BelowProblem(d);
                TestContext.Out.WriteLine(e);
                Assert.IsNotNull(e, "a sail curled above the neck without the plane clamp must fail");
            }
            finally { Dewlap.NoPlaneClampForTest = false; }
        }

        // The sail's top never separates from the throat: its root vertices (skinned to body joints only) stay within
        // 2.5 mm of the posed body skin while walking and circling (RootTuck 1.5 mm under it at rest).
        [Test] public void Dewlap_TopEdge_StaysOnTheThroat()
        {
            CreatureSpecies sp = Species(Flap());
            int bodyEnd = VertexCount(Parts(false, null)), first = VertexCount(Parts(true, null)), joints = sp.JointCount;
            var m = new CreaturePteroWingTests.Skinned(sp);
            var roots = new List<int>();
            for (int i = first; i < m.V.Length; i++) if (m.Uv2[i].x < joints && m.Uv2[i].y < joints) roots.Add(i);
            Assert.GreaterOrEqual(roots.Count, 2 * 17, "the root rows of both faces along the 17 columns, skinned to the body joints");
            var body = new List<int>();
            for (int k = 0; k < m.T.Length; k += 3)
                if (m.T[k] < bodyEnd && m.T[k + 1] < bodyEnd && m.T[k + 2] < bodyEnd && m.Part(m.T[k]) == PartMeshEmitter.PartBody) { body.Add(m.T[k]); body.Add(m.T[k + 1]); body.Add(m.T[k + 2]); }
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            float worst = 0f;
            for (int t = 0; t < 600; t++)
            {
                float a = t / 60f * 3f;
                sim.Tick(FlowKit.kDt, t < 300 ? new Vector3(0f, 0f, 30f) : new Vector3(0f, 0f, 0.25f) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.12f);
                if (t % 20 != 19) continue;
                m.Pose(sim);
                foreach (int i in roots)
                {
                    float d = float.MaxValue;
                    for (int k = 0; k < body.Count; k += 3) d = Mathf.Min(d, DistToTri(m.P[i], m.P[body[k]], m.P[body[k + 1]], m.P[body[k + 2]]));
                    worst = Mathf.Max(worst, d);
                }
            }
            TestContext.Out.WriteLine($"root row: at most {worst * 1000f:F2} mm off the posed throat");
            Assert.Less(worst, 0.0025f, "the sail's top stays on the throat");
        }

        // ------------------------------------------------------------ the bind: links along the drawn rows

        // null, or where the rest link bones leave the drawn sail: at each station's mesh column, every link's root (the
        // rest bone's origin) within 1 mm of the column's midline (the two faces' rows 1.. averaged, the creases' sideways
        // ripple taken out, carried up past h = 0 - and, at a station the rear sweep cuts short, on down past the full
        // row) and the last link's end within 1 mm of the rim's lowest point (a swept station: that point within 1 mm of
        // its links). worst: the largest miss (m).
        static string RowsProblem(CreatureSpecies sp, int first, out float worst)
        {
            Dewlap d = Part(sp);
            int L = d.links, F = Dewlap.RowsPerLink * L, B = Dewlap.RimSegments, prof = 2 * (F + 1) + (B - 1);
            int start = sp.Layout.Start(KitView.Parts(sp).Count - 1);
            Mesh mesh = FlowKit.Mesh(sp);
            Vector3[] v = mesh.vertices;
            Object.DestroyImmediate(mesh);
            Matrix4x4[] bones = CreatureRestPose.BoneWorld(sp);
            CreatureSim rest = CreatureRestPose.BindSim(sp);
            worst = 0f;
            for (int k = 0; k < d.stations; k++)
            {
                int ci = k * Dewlap.ColumnsPerSpan, col = ci == 0 ? first : first + prof * (ci + 1);
                Dewlap.StationFrame(rest.Joints, rest.Rotations, d.StationJoint(k), out _, out Quaternion q);
                Vector3 x = q * Vector3.right;
                var mid = new Vector3[F + 1];
                for (int r = 1; r <= F; r++) mid[r] = Vector3.ProjectOnPlane(0.5f * (v[col + r] + v[col + prof - 1 - r]), x);
                // the rows' line carried up past h = 0 (row 0 itself sits on the throat's facets) and, where the rear sweep
                // cuts the drawn row short (its links are the full row's), on down past the full row's end
                Vector3 up = (mid[1] - mid[2]).normalized;
                mid[0] = mid[1] + up;                                    // (1 m: past any root)
                if (d.RearScale(k) < 1f) mid[F] = mid[F - 1] - up;       // (past any row's end)
                for (int l = 0; l < L; l++)
                {
                    Vector3 o = Vector3.ProjectOnPlane(bones[start + k * L + l].GetColumn(3), x);
                    float best = float.MaxValue;
                    for (int r = 0; r < F; r++)
                    {
                        Vector3 a = mid[r], ab = mid[r + 1] - a;
                        float t = Mathf.Clamp01(Vector3.Dot(o - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
                        best = Mathf.Min(best, (a + ab * t - o).magnitude);
                    }
                    worst = Mathf.Max(worst, best);
                    if (best > 0.001f) return $"station {k} link {l}: its rest root is {best * 1000f:F1} mm off the drawn column";
                }
                Matrix4x4 lastB = bones[start + k * L + L - 1];
                float len = L > 1 ? ((Vector3)lastB.GetColumn(3) - (Vector3)bones[start + k * L + L - 2].GetColumn(3)).magnitude
                                  : ((Vector3)lastB.GetColumn(3) - v[col + F + B / 2]).magnitude;
                Vector3 tip = (Vector3)lastB.GetColumn(3) - ((Vector3)lastB.GetColumn(1)).normalized * len;
                Vector3 low = v[col + F + B / 2];
                float miss;
                if (d.RearScale(k) < 1f)
                {
                    // the swept rear is the full row cut short: its rim's lowest point lies on the station's links
                    miss = float.MaxValue;
                    for (int l = 0; l < L; l++)
                    {
                        Vector3 a = bones[start + k * L + l].GetColumn(3), b = l + 1 < L ? (Vector3)bones[start + k * L + l + 1].GetColumn(3) : tip, ab = b - a;
                        float t = Mathf.Clamp01(Vector3.Dot(low - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
                        miss = Mathf.Min(miss, (a + ab * t - low).magnitude);
                    }
                    worst = Mathf.Max(worst, miss);
                    if (miss > 0.001f) return $"station {k} (swept): the rim's lowest point is {miss * 1000f:F1} mm off its links";
                    continue;
                }
                miss = (tip - low).magnitude;
                worst = Mathf.Max(worst, miss);
                if (miss > 0.001f) return $"station {k}: its last link ends {miss * 1000f:F1} mm from the rim's lowest point";
            }
            return null;
        }

        [Test] public void Dewlap_RestLinks_RunAlongTheDrawnRows()
        {
            string e = RowsProblem(Species(Flap()), VertexCount(Parts(true, null)), out float worst);
            TestContext.Out.WriteLine($"largest miss {worst * 1000f:F2} mm");
            Assert.IsNull(e);
        }

        // A tall-stance winged FlowBody (the shipped Pterodactyl's parts, its SpineBody swapped for a FlowBody of the same
        // profile): its bind straightens the spine (WingedMeshBuilder.StraightenBind, via CreatureRestPose.BindSim), and
        // the Dewlap's bind geometry must come from that same rest, or its links miss the drawn rows.
        static CreatureSpecies PteroFlow(bool dewlap, bool restAtStance = false)
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset("Pterodactyl");
            var parts = new List<CreaturePart>();
            foreach (CreaturePart p in asset.parts)
            {
                if (p is CreatureKit.SpineBody sb)
                {
                    var keys = new FlowKey[sb.profile.Length];
                    for (int i = 0; i < keys.Length; i++)
                        keys[i] = new FlowKey { t = sb.profile[i].t, halfWidth = sb.profile[i].halfWidth, halfHeight = sb.profile[i].halfHeight, clearance = sb.profile[i].clearance, bendLimitDeg = sb.profile[i].bendLimitDeg };
                    // restAtStance off: a body bound at its stance keeps it (CreatureWings.StraightBind), only the flat rest
                    // is straightened - the path this fixture proves the Dewlap follows
                    parts.Add(new FlowBody { id = sb.id, jointCount = sb.JointCount, spacing = sb.spacing, headCapLength = sb.headCapLength, tailTipLength = sb.tailTipLength, profile = keys, restAtStance = restAtStance });
                }
                else parts.Add(p.Clone());
            }
            if (dewlap) parts.Add(new Dewlap { id = "dewlap", fromJoint = 1, toJoint = 5, stations = 4, links = 3, depthAt = new[] { 0.08f, 0.12f, 0.12f, 0.08f } });
            return FlowKit.Build((dewlap ? "PteroFlowDewlap" : "PteroFlow") + (restAtStance ? "Stance" : ""), asset.scale, parts, asset.modules);
        }

        static string TallStanceRows(out float worst, out float straightened, bool restAtStance = false)
        {
            CreatureSpecies sp = PteroFlow(true, restAtStance);
            Assert.IsTrue(sp.WingCount > 0, "winged");
            straightened = 0f;
            CreatureSim plain = CreatureRestPose.Sim(sp), bind = CreatureRestPose.BindSim(sp);
            for (int i = 0; i < sp.JointCount; i++) straightened = Mathf.Max(straightened, (plain.Joints[i] - bind.Joints[i]).magnitude);
            Mesh m = FlowKit.Mesh(PteroFlow(false, restAtStance));
            int first = m.vertexCount;
            Object.DestroyImmediate(m);
            return RowsProblem(sp, first, out worst);
        }

        [Test] public void Dewlap_TallStanceWings_LinksFollowTheStraightenedBind()
        {
            string e = TallStanceRows(out float worst, out float straightened);
            TestContext.Out.WriteLine($"the bind straightens the spine by up to {straightened * 1000f:F0} mm; largest miss {worst * 1000f:F2} mm");
            Assert.Greater(straightened, 0.02f, "the tall stance's bind really differs from the plain rest");
            Assert.IsNull(e);
        }

        // The same winged FlowBody bound at its stance (restAtStance on): the bind keeps the stance (no straightening:
        // CreatureWings.StraightBind) and the Dewlap's links still run along the drawn rows.
        [Test] public void Dewlap_TallStanceWings_RestAtStance_LinksFollowTheBind()
        {
            string e = TallStanceRows(out float worst, out float straightened, true);
            TestContext.Out.WriteLine($"bound at its stance: the bind moves the spine by {straightened * 1000f:F2} mm; largest miss {worst * 1000f:F2} mm");
            Assert.Less(straightened, 1e-5f, "a stance-bound body is not straightened");
            Assert.IsNull(e);
        }

        // RED: the bind geometry from the plain rest sim (not the straightened bind) puts the links off the drawn rows.
        [Test] public void Red_Dewlap_PlainRestGeometry()
        {
            Dewlap.PlainRestForTest = true;
            try { StringAssert.Contains("off the drawn column", TallStanceRows(out _, out _) ?? "", "links built on the unstraightened rest must miss the rows"); }
            finally { Dewlap.PlainRestForTest = false; }
        }

        // ------------------------------------------------------------ the swing

        sealed class SwingRun
        {
            public readonly List<float> T = new List<float>(), Swing = new List<float>(), Yaw = new List<float>();   // Froude seconds, deg, deg/s (Froude)
            public float Peak, PeakT, YawPeak, YawPeakT, Settled, Jitter;
        }

        // The turn: walking ahead 3 s, then a target 90 degrees to the right (4 s), then halted (5 s). Times in Froude seconds
        // (x sqrt s), lengths x s. Swing = the largest station's Swing; Yaw = the middle station joint's yaw rate. The swing
        // peak is searched over the 4 s of the turn (it comes as the neck stops turning: the sail swings on).
        static SwingRun Turn(Dewlap d, float scale)
        {
            CreatureSpecies sp = Species(d);
            CreatureSim sim = FlowKit.Sim(sp, scale);
            DewlapRuntime r = Runtime(sim);
            Assert.IsNotNull(r, "the dewlap has a runtime");
            float T = Mathf.Sqrt(scale);
            int mid = Mathf.RoundToInt(Part(sp).StationJoint(Part(sp).stations / 2));
            var run = new SwingRun();
            int walk = Mathf.RoundToInt(3f * T / FlowKit.kDt), turn = Mathf.RoundToInt(4f * T / FlowKit.kDt), halt = Mathf.RoundToInt(5f * T / FlowKit.kDt);
            for (int i = 0; i < walk; i++) sim.Tick(FlowKit.kDt, new Vector3(0f, 0f, 30f * scale));
            Vector3 target = sim.Joints[0] + new Vector3(30f * scale, 0f, 0f);
            Vector3 fwd = sim.Rotations[mid] * Vector3.forward;
            for (int i = 0; i < turn + halt; i++)
            {
                if (i == turn) sim.Halt = true;
                sim.Tick(FlowKit.kDt, target);
                Vector3 f = sim.Rotations[mid] * Vector3.forward;
                float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(fwd, Vector3.up), Vector3.ProjectOnPlane(f, Vector3.up), Vector3.up) / FlowKit.kDt * T;
                fwd = f;
                float s = 0f;
                for (int k = 0; k < r.Stations; k++) s = Mathf.Max(s, r.Swing(k));
                float t = (i + 1) * FlowKit.kDt / T;
                run.T.Add(t); run.Swing.Add(s); run.Yaw.Add(yaw);
                if (i < turn && s > run.Peak) { run.Peak = s; run.PeakT = t; }
            }
            // the yaw rate's peak: averaged over 0.25 s (the walk turns in steps: the raw rate is a plateau of pulses)
            int win = Mathf.Max(1, Mathf.RoundToInt(0.25f * T / FlowKit.kDt));
            for (int i = 0; i + win <= turn; i++)
            {
                float m = 0f;
                for (int k = i; k < i + win; k++) m += run.Yaw[k];
                m = Mathf.Abs(m / win);
                if (m > run.YawPeak) { run.YawPeak = m; run.YawPeakT = run.T[i + win / 2]; }
            }
            // halted: the swing 3 s after the halt; then the frame-to-frame change over the last 2 s
            int at3 = turn + Mathf.RoundToInt(3f * T / FlowKit.kDt) - 1;
            for (int i = at3; i < run.Swing.Count; i++) run.Settled = Mathf.Max(run.Settled, run.Swing[i]);
            for (int i = run.Swing.Count - Mathf.RoundToInt(2f * T / FlowKit.kDt); i < run.Swing.Count; i++) run.Jitter = Mathf.Max(run.Jitter, Mathf.Abs(run.Swing[i] - run.Swing[i - 1]));
            return run;
        }

        // The walking turn: the sail swings (>= 5 deg) and settles within 3 s of the halt.
        [Test] public void Dewlap_Swings_InAWalkingTurn_AndSettles()
        {
            SwingRun run = Turn(Flap(), 1f);
            TestContext.Out.WriteLine($"yaw rate peak (0.25 s mean) {run.YawPeak:F0} deg/s at {run.YawPeakT:F2} s; swing peak {run.Peak:F2} deg at {run.PeakT:F2} s; 3 s halted {run.Settled:F3} deg");
            Assert.Greater(run.YawPeak, 30f, "the neck turned");
            Assert.GreaterOrEqual(run.Peak, 5f, "the sail swings in the turn");
            Assert.Less(run.Settled, 0.5f, "settled within 3 s at rest");
        }

        // null, or why the sail does not swing after the scripted neck move (Scripted): its peak >= 5 deg; the swinging
        // station's swing peaks >= 0.1 s (Froude) after the lean its acceleration asks for (an instant pendulum would
        // peak with it); and it swings on after the neck has stopped (>= 25 % of its peak 0.2 .. 0.6 s after).
        static string SwingProblem(ScriptedRun run)
        {
            if (run.Peak < 5f) return $"swing peak {run.Peak:F2} deg < 5 deg";
            float lag = run.PeakT - run.LeanPeakT;
            if (lag < 0.1f) return $"swing peak at {run.PeakT:F2} s lags its lean's peak ({run.LeanPeakT:F2} s) by {lag:F3} s < 0.1 s";
            if (run.After < 0.25f * run.Peak) return $"after the move the swing dies at once: {run.After:F2} deg < 25 % of the peak {run.Peak:F2}";
            return null;
        }

        [Test] public void Dewlap_Swings_AfterANeckMove()
        {
            ScriptedRun run = Scripted(Flap(), 1f);
            TestContext.Out.WriteLine($"station {run.Station}: lean peak {run.LeanPeak:F2} deg at {run.LeanPeakT:F2} s; swing peak {run.Peak:F2} deg at {run.PeakT:F2} s; after the move {run.After:F2} deg; 3 s after {run.Settled:F3} deg");
            Assert.IsNull(SwingProblem(run));
            Assert.Less(run.Settled, 0.5f, "settled within 3 s of the move's end");
        }

        // RED: a near-instant flap (swingHz 30, overdamped) follows its lean at once - no lag.
        [Test] public void Red_Dewlap_Instant()
        {
            Dewlap d = Flap(); d.swingHz = 30f; d.swingDamping = 1.5f;
            ScriptedRun run = Scripted(d, 1f);
            TestContext.Out.WriteLine($"lean peak {run.LeanPeakT:F3} s, swing peak {run.PeakT:F3} s, after {run.After:F2} / {run.Peak:F2} deg");
            StringAssert.Contains("lags its lean", SwingProblem(run), "an instant flap must fail the lag test");
        }

        [Test] public void Red_Dewlap_Stiff()
        {
            Dewlap d = Flap(); d.maxSwingDeg = 0f;
            StringAssert.Contains("swing peak", SwingProblem(Scripted(d, 1f)), "a sail that cannot swing must fail the swing test");
        }

        [Test] public void Dewlap_NoJitter_AtRest()
        {
            SwingRun run = Turn(Flap(), 1f);
            TestContext.Out.WriteLine($"settled: largest frame-to-frame swing change {run.Jitter:E2} deg");
            Assert.Less(run.Jitter, 0.01f, "frame-to-frame swing change once settled (deg)");
        }

        // A scripted neck move, exactly Froude-similar at every size: the creature's rest pose (scale s) swung 90 degrees
        // about the vertical through joint 8 over 0.8 s (smoothstep, from 0.3 s), then held still; the dewlap stepped on it
        // directly (the walking sim's own Froude similarity is the generic Size check's). Times in Froude seconds.
        sealed class ScriptedRun
        {
            public readonly List<Vector2> Curve = new List<Vector2>();   // (t, the largest station's Swing)
            public int Station;
            public float Peak, PeakT, LeanPeak, LeanPeakT, After, Settled;
        }

        const float kMoveStart = 0.3f, kMoveTime = 0.8f;

        static ScriptedRun Scripted(Dewlap d, float scale)
        {
            CreatureSpecies sp = Species(d);
            CreatureSim sim = FlowKit.Sim(sp, scale);
            DewlapRuntime r = Runtime(sim);
            float T = Mathf.Sqrt(scale);
            int n = sim.Joints.Length, S = r.Stations;
            var j0 = (Vector3[])sim.Joints.Clone(); var r0 = (Quaternion[])sim.Rotations.Clone();
            Vector3 pivot = j0[8];
            var run = new ScriptedRun();
            var swing = new List<float[]>(); var lean = new List<float[]>(); var times = new List<float>();
            int frames = Mathf.RoundToInt(4.5f * T / FlowKit.kDt);
            for (int f = 1; f <= frames; f++)
            {
                float t = f * FlowKit.kDt / T, x = Mathf.Clamp01((t - kMoveStart) / kMoveTime);
                Quaternion q = Quaternion.AngleAxis(90f * x * x * (3f - 2f * x), Vector3.up);
                for (int i = 0; i < n; i++) { sim.Joints[i] = pivot + q * (j0[i] - pivot); sim.Rotations[i] = q * r0[i]; }
                r.Step(new StepCtx(sim), FlowKit.kDt);
                var sw = new float[S]; var le = new float[S];
                float mx = 0f;
                for (int k = 0; k < S; k++) { sw[k] = r.Swing(k); le[k] = r.Lean(k); mx = Mathf.Max(mx, sw[k]); }
                swing.Add(sw); lean.Add(le); times.Add(t);
                run.Curve.Add(new Vector2(t, mx));
            }
            // the station that swings most; its swing and lean peaks
            for (int i = 0; i < times.Count; i++)
                for (int k = 0; k < S; k++)
                    if (swing[i][k] > run.Peak) { run.Peak = swing[i][k]; run.PeakT = times[i]; run.Station = k; }
            float end = kMoveStart + kMoveTime;
            for (int i = 0; i < times.Count; i++)
            {
                float le = lean[i][run.Station], sw = swing[i][run.Station];
                if (le > run.LeanPeak) { run.LeanPeak = le; run.LeanPeakT = times[i]; }
                if (times[i] >= end + 0.2f && times[i] <= end + 0.6f) run.After = Mathf.Max(run.After, sw);
                if (times[i] >= end + 3f) run.Settled = Mathf.Max(run.Settled, run.Curve[i].y);
            }
            return run;
        }

        // The largest difference of two swing curves (b linearly interpolated at a's Froude times); peak: a's largest swing.
        static float CurveDifference(List<Vector2> a, List<Vector2> b, out float peak, out float at)
        {
            float worst = 0f; peak = 0f; at = 0f;
            foreach (Vector2 p in a) peak = Mathf.Max(peak, p.y);
            int j = 0;
            foreach (Vector2 p in a)
            {
                while (j + 1 < b.Count && b[j + 1].x < p.x) j++;
                if (j + 1 >= b.Count || b[j].x > p.x) continue;
                float f = (p.x - b[j].x) / (b[j + 1].x - b[j].x);
                float diff = Mathf.Abs(p.y - Mathf.Lerp(b[j].y, b[j + 1].y, f));
                if (diff > worst) { worst = diff; at = p.x; }
            }
            return worst;
        }

        [Test] public void Dewlap_Froude()
        {
            float worst = CurveDifference(Scripted(Flap(), 0.9f).Curve, Scripted(Flap(), 1.1f).Curve, out float peak, out float at);
            TestContext.Out.WriteLine($"peak {peak:F3} deg; worst difference {worst:F4} deg at {at:F2} s (Froude)");
            Assert.Greater(peak, 5f, "the move swings the sail");
            Assert.LessOrEqual(worst, 0.02f * peak, "0.9x and 1.1x swing alike in Froude time (2 % of the peak)");
        }

        // RED for the size rule: the 1.1x individual's swingHz raised by sqrt(1.1 / 0.9) swings at the 0.9x individual's
        // real frequency (the / sqrt s rule undone through the data) and must fail the 2 % match.
        [Test] public void Red_Dewlap_Froude_Unscaled()
        {
            Dewlap unscaled = Flap(); unscaled.swingHz *= Mathf.Sqrt(1.1f / 0.9f);
            float worst = CurveDifference(Scripted(Flap(), 0.9f).Curve, Scripted(unscaled, 1.1f).Curve, out float peak, out _);
            Assert.Greater(worst, 0.02f * peak, "a swing that ignores the size rule must be caught");
        }

        // maxSwingDeg 0: a stiff sail - its bones are the rest bones carried by the joints, whatever swingHz says.
        [Test] public void Dewlap_ZeroSwing_IsRestPose()
        {
            Dewlap slow = Flap(), fast = Flap();
            slow.maxSwingDeg = 0f; fast.maxSwingDeg = 0f; fast.swingHz = 7f; fast.swingDamping = 0.1f;
            CreatureSpecies sa = Species(slow), sb = Species(fast);
            CreatureSim a = FlowKit.Sim(sa, 1f), b = FlowKit.Sim(sb, 1f);
            DewlapRuntime ra = Runtime(a);
            int start = sa.Layout.Start(KitView.Parts(sa).Count - 1), n = slow.stations * slow.links;
            var rest = new Matrix4x4[sa.BoneCount];
            int compared = 0;
            for (int t = 0; t < 600; t++)
            {
                Vector3 target = t < 240 ? new Vector3(0f, 0f, 30f) : new Vector3(30f, 0f, 2f);
                a.Tick(FlowKit.kDt, target); b.Tick(FlowKit.kDt, target);
                if (t % 15 != 14) continue;
                Matrix4x4[] wa = FlowKit.Bones(a), wb = FlowKit.Bones(b);
                ra.WriteRest(rest, 0);
                for (int i = start; i < start + n; i++)
                    for (int e = 0; e < 16; e++)
                    {
                        Assert.IsTrue(wa[i][e] == rest[i][e], $"t={t}: bone {i - start} element {e}: {wa[i][e]} != rest {rest[i][e]}");
                        Assert.IsTrue(wa[i][e] == wb[i][e], $"t={t}: bone {i - start} element {e} depends on swingHz");
                    }
                compared++;
            }
            Assert.Greater(compared, 30);
        }

        [Test] public void Dewlap_NoGC()
        {
            GcAllocs.AssertCounterWorks();
            CreatureSpecies sp = Species(Flap());
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            DewlapRuntime r = Runtime(sim);
            var bones = new Matrix4x4[sp.BoneCount];
            Vector3 lo = Vector3.zero, hi = Vector3.zero;
            int t = 0;
            void Frame()
            {
                Vector3 target = (t / 120) % 2 == 0 ? new Vector3(0f, 0f, 30f) : new Vector3(30f, 0f, 0f);
                sim.Tick(FlowKit.kDt, target);
                sim.WriteBoneWorld(bones, 0);
                lo = sim.Joints[0]; hi = lo;
                r.EncapsulateBounds(ref lo, ref hi);
                t++;
            }
            for (int i = 0; i < 240; i++) Frame();
            Assert.AreEqual(0, GcAllocs.Count(() => { for (int i = 0; i < 240; i++) Frame(); }), "dewlap step, bones and bounds allocate nothing");
        }

        // The bounds hold the sail's lowest points.
        [Test] public void Dewlap_Bounds_HoldTheSail()
        {
            CreatureSpecies sp = Species(Flap());
            int first = VertexCount(Parts(true, null));
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            DewlapRuntime r = Runtime(sim);
            var m = new CreaturePteroWingTests.Skinned(sp);
            for (int step = 0; step < 8; step++)
            {
                FlowKit.Run(sim, 0.5f, step < 4 ? new Vector3(0f, 0f, 30f) : new Vector3(30f, 0f, 0f));
                m.Pose(sim);
                Vector3 lo = sim.Joints[0], hi = lo;
                r.EncapsulateBounds(ref lo, ref hi);
                float lowest = float.MaxValue;
                for (int i = first; i < m.P.Length; i++) lowest = Mathf.Min(lowest, m.P[i].y);
                Assert.LessOrEqual(lo.y, lowest + 1e-4f, $"step {step}: the bounds reach the sail's lowest point");
            }
        }

        // ------------------------------------------------------------ the mesh

        [Test] public void Dewlap_Mesh_PaleKindOnEverySailVertex()
        {
            CreatureSpecies sp = Species(Flap());
            int slot = MaterialSlots.For(sp).SlotOf("dewlap");
            Assert.GreaterOrEqual(slot, PartLayers.LegacyIds, "dewlap has a kit slot");
            Assert.IsTrue(PartLayers.Find("dewlap", out PartLayers.Entry e), "dewlap has a default row");
            Color pale = new Color(0xD9 / 255f, 0xCD / 255f, 0xB8 / 255f);
            Assert.AreEqual(pale.r, e.Row.tint.r, 2e-3f); Assert.AreEqual(pale.g, e.Row.tint.g, 2e-3f); Assert.AreEqual(pale.b, e.Row.tint.b, 2e-3f);
            int first = VertexCount(Parts(true, null));
            var m = new CreaturePteroWingTests.Skinned(sp);
            for (int i = first; i < m.V.Length; i++) Assert.AreEqual(slot, m.Part(i), $"sail vertex {i - first} on the dewlap kind");
        }

        // No sail triangle turns inside out: KitChecks.MaxBendProblem's 5 s minimum-radius circle, then walking and a
        // 90 degree turn, every 5th frame. (MaxBendProblem itself is not used: this fixture's body has a near-degenerate
        // sliver by the hind leg that it flags with or without a dewlap - dot -6e-9 at t=54.)
        static string InvertedProblem(CreatureSpecies sp)
        {
            int slot = MaterialSlots.For(sp).SlotOf("dewlap");
            var m = new CreaturePteroWingTests.Skinned(sp);
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            Vector3 centre = new Vector3(0f, 0f, 0.25f);
            for (int t = 0; t < 900; t++)
            {
                float a = t / 60f * 3f;
                Vector3 target = t < 300 ? centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.12f : t < 600 ? new Vector3(0f, 0f, 30f) : new Vector3(30f, 0f, 2f);
                sim.Tick(FlowKit.kDt, target);
                if (t % 5 != 4) continue;
                m.Pose(sim);
                int n = m.Inverted(slot, out string where);
                if (n > 0) return $"t={t}: {n} sail triangles inverted, first {where}";
            }
            return null;
        }

        [Test] public void Dewlap_NoInvertedTriangles() => Assert.IsNull(InvertedProblem(Species(Flap())));

        // ------------------------------------------------------------ a heavy bob (the Apex in flight)

        // The Apex (its own body, wings, flight and bob - the girdle heaves ~13 % of the body every beat) with its sail
        // replaced by the part's DEFAULTS on the Apex's neck: joints 1..7 (the last station on the girdle joint, the
        // chest), the Apex's depths, every other field the part's default.
        internal static CreatureSpecies HeavyBobSpecies(System.Action<Dewlap> edit = null) => CreatureApexTests.Apex(ps =>
        {
            for (int i = 0; i < ps.Count; i++)
                if (ps[i] is Dewlap old)
                {
                    var d = new Dewlap { id = old.id, fromJoint = 1, toJoint = 7, depthAt = (float[])old.depthAt.Clone() };
                    edit?.Invoke(d);
                    ps[i] = d;
                }
        });

        // null, or the first frame in which the sail turns over: a launch from a short walk, the climb-out and a beating
        // cruise (goals: altitude 6 x s, the cruise speed; `seconds` x sqrt s in all), every 2nd frame: no sail triangle
        // inverted (skinned winding against its bind winding) and no sail vertex normal turned away from a triangle it
        // belongs to (the rim's shading flip), on triangles clean at bind. frames: frames judged; minDot: the least
        // normal-to-face dot seen (unit vectors).
        internal static string HeavyBobProblem(CreatureSpecies sp, float seconds, out int frames, out float minDot)
        {
            frames = 0; minDot = 1f;
            float s = sp.scale, T = Mathf.Sqrt(s), dt = FlowKit.kDt;
            int slot = MaterialSlots.For(sp).SlotOf("dewlap");
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] n0 = mesh.normals;
            Object.DestroyImmediate(mesh);
            var m = new CreaturePteroWingTests.Skinned(sp);
            var tris = new List<int>();
            for (int k = 0; k < m.T.Length; k += 3)
            {
                int i0 = m.T[k], i1 = m.T[k + 1], i2 = m.T[k + 2];
                if (m.Part(i0) != slot || m.Part(i1) != slot || m.Part(i2) != slot) continue;
                Vector3 f = Vector3.Cross(m.V[i1] - m.V[i0], m.V[i2] - m.V[i0]);
                if (f.sqrMagnitude < 1e-20f) continue;
                f.Normalize();
                if (Vector3.Dot(n0[i0], f) > 0.5f && Vector3.Dot(n0[i1], f) > 0.5f && Vector3.Dot(n0[i2], f) > 0.5f) { tris.Add(i0); tris.Add(i1); tris.Add(i2); }
            }
            Assert.Greater(tris.Count, 3000, "the sail's triangles clean at bind");
            var nrm = new Vector3[m.V.Length];
            CreatureSim sim = FlowKit.Sim(sp, s);
            for (int t = 0; t < 30; t++) sim.Tick(dt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * (2f * s));
            sim.Flight.Launch(6f * s);
            bool flew = false;
            for (int t = 0, N = Mathf.RoundToInt(seconds * T / dt); t < N; t++)
            {
                sim.Flight.SetGoals(6f * s, sp.flight.cruiseSpeed * T, 0f);
                sim.Tick(dt, sim.Joints[0]);
                flew |= sim.Mode == FlightMode.Air;
                if (t % 2 != 1) continue;
                m.Pose(sim);
                frames++;
                int bad = m.Inverted(slot, out string where);
                if (bad > 0) return $"t={sim.Time:F2} s ({sim.Mode}): {bad} sail triangles inverted, first {where}";
                for (int i = 0; i < m.V.Length; i++)
                {
                    if (m.Part(i) != slot) continue;
                    Vector4 w = m.Uv2[i];
                    nrm[i] = (w.z * m.Skin((int)w.x).MultiplyVector(n0[i]) + w.w * m.Skin((int)w.y).MultiplyVector(n0[i])).normalized;
                }
                for (int k = 0; k < tris.Count; k += 3)
                {
                    Vector3 a = m.P[tris[k]], f = Vector3.Cross(m.P[tris[k + 1]] - a, m.P[tris[k + 2]] - a);
                    if (f.sqrMagnitude < 1e-20f) continue;
                    f.Normalize();
                    for (int q = 0; q < 3; q++)
                    {
                        float d = Vector3.Dot(nrm[tris[k + q]], f);
                        if (d < minDot) minDot = d;
                        if (!(d > 0f)) return $"t={sim.Time:F2} s ({sim.Mode}): sail vertex {tris[k + q]}'s normal faces away from its triangle (dot {d:F2})";
                    }
                }
            }
            return flew ? null : "it never flew";
        }

        [Test] public void Dewlap_HeavyBob_RimNeverTurnsOver()
        {
            string e = HeavyBobProblem(HeavyBobSpecies(), 60f, out int frames, out float minDot);
            TestContext.Out.WriteLine($"the part's defaults on the Apex in flight: {frames} frames judged, least normal-to-face dot {minDot:F3}");
            Assert.IsNull(e, e);
        }

        // RED: the same flight with the stations uncoupled (no lean smoothing, no neighbour pull, no twist limit): the
        // girdle station swings apart from its neighbour and the sheet between them turns over.
        [Test] public void Red_Dewlap_HeavyBob_Uncoupled()
        {
            Dewlap.NoCouplingForTest = true;
            try
            {
                string e = HeavyBobProblem(HeavyBobSpecies(), 60f, out _, out _);
                TestContext.Out.WriteLine("RED: " + e);
                Assert.IsNotNull(e, "uncoupled stations must turn the rim over under the Apex's bob");
            }
            finally { Dewlap.NoCouplingForTest = false; }
        }

        // ------------------------------------------------------------ a pouch, not a card

        // The bind mesh's column at station k: the vertex index of its profile's first point.
        static int Column(Dewlap d, int first, int k)
        {
            int prof = 2 * (Dewlap.RowsPerLink * d.links + 1) + (Dewlap.RimSegments - 1), ci = k * Dewlap.ColumnsPerSpan;
            return ci == 0 ? first : first + prof * (ci + 1);
        }

        // null, or why the sail is a flat card: at the middle station its sheet (the two faces' distance, row by row) is
        // thicker just under the throat than at the rim (>= 1.2 x thickness at the first row), bellies out halfway down
        // (>= thickness + 0.9 x billow x depth at its widest, and > 1.5 x thickness), and meets the rim at the sheet's own
        // thickness. first, widest: m.
        static string PouchProblem(Dewlap d, out float first, out float widest)
        {
            CreatureSpecies sp = Species(d);
            int f0 = VertexCount(Parts(true, null)), F = Dewlap.RowsPerLink * d.links;
            int prof = 2 * (F + 1) + (Dewlap.RimSegments - 1);
            Mesh mesh = FlowKit.Mesh(sp);
            Vector3[] v = mesh.vertices;
            Object.DestroyImmediate(mesh);
            int col = Column(d, f0, d.stations / 2);
            float Thick(int r) => (v[col + r] - v[col + prof - 1 - r]).magnitude;
            first = Thick(1); widest = 0f;
            for (int r = 1; r <= F; r++) widest = Mathf.Max(widest, Thick(r));
            float depth = (v[col + F + Dewlap.RimSegments / 2] - v[col]).magnitude;
            if (!(first >= 1.2f * d.thickness)) return $"the sheet is {first * 1000f:F1} mm thick under the throat (< 1.2 x {d.thickness * 1000f:F0} mm)";
            if (!(widest >= d.thickness + 0.9f * d.billow * depth) || !(widest > 1.5f * d.thickness)) return $"no belly: at most {widest * 1000f:F1} mm thick (the sheet {d.thickness * 1000f:F0} mm, the row {depth * 1000f:F0} mm deep)";
            if (Mathf.Abs(Thick(F) - d.thickness) > 0.15f * d.thickness) return $"the sheet meets the rim {Thick(F) * 1000f:F2} mm thick, not {d.thickness * 1000f:F0}";
            return null;
        }

        [Test] public void Dewlap_IsAPouch_ThickAtTheRoot_Bellied()
        {
            string e = PouchProblem(Flap(), out float first, out float widest);
            TestContext.Out.WriteLine($"mid station: {first * 1000f:F1} mm under the throat, {widest * 1000f:F1} mm at the belly (sheet {Flap().thickness * 1000f:F0} mm)");
            Assert.IsNull(e, e);
        }

        // RED: no belly, no root swell - the old constant-thickness card.
        [Test] public void Red_Dewlap_FlatCard()
        {
            Dewlap d = Flap(); d.billow = 0f; d.rootThickness = d.thickness;
            StringAssert.Contains("thick", PouchProblem(d, out _, out _) ?? "", "a constant-thickness sheet must fail");
        }

        // null, or why the rear end is square: the chest end's row (the last column, rim to root) is at most
        // 1.5 x RearEnd of the deepest row, and over the sweep the rows shorten column by column toward it (a rounded
        // end, no step back down). ratio: the last row / the deepest.
        static string RearProblem(Dewlap d, out float ratio)
        {
            CreatureSpecies sp = Species(d);
            int f0 = VertexCount(Parts(true, null)), F = Dewlap.RowsPerLink * d.links, B = Dewlap.RimSegments;
            int prof = 2 * (F + 1) + (B - 1), cols = (d.stations - 1) * Dewlap.ColumnsPerSpan + 1;
            Mesh mesh = FlowKit.Mesh(sp);
            Vector3[] v = mesh.vertices;
            Object.DestroyImmediate(mesh);
            var depth = new float[cols];
            float deepest = 0f;
            for (int ci = 0; ci < cols; ci++)
            {
                int col = ci == 0 ? f0 : f0 + prof * (ci + 1);
                depth[ci] = (v[col + F + B / 2] - 0.5f * (v[col] + v[col + prof - 1])).magnitude;
                deepest = Mathf.Max(deepest, depth[ci]);
            }
            ratio = depth[cols - 1] / deepest;
            if (ratio > 1.5f * Dewlap.RearEnd) return $"the chest end's row is {ratio:F2} of the deepest: a square rear end";
            int from = Mathf.FloorToInt((1f - d.rearSweep) * (cols - 1)) + 1;
            for (int ci = from + 1; ci < cols; ci++)
                if (depth[ci] > depth[ci - 1] + 1e-4f) return $"column {ci} is deeper ({depth[ci] * 1000f:F0} mm) than column {ci - 1} ({depth[ci - 1] * 1000f:F0} mm) in the sweep";
            return null;
        }

        [Test] public void Dewlap_RearEnd_SweepsIntoTheChest()
        {
            string e = RearProblem(Flap(), out float ratio);
            TestContext.Out.WriteLine($"the chest end's row: {ratio:F2} of the deepest");
            Assert.IsNull(e, e);
        }

        // RED: no sweep - the old square rear end.
        [Test] public void Red_Dewlap_SquareRear()
        {
            Dewlap d = Flap(); d.rearSweep = 0f;
            StringAssert.Contains("square rear end", RearProblem(d, out _) ?? "");
        }

        // The pouch puffs out with the swing: while it swings some sail bone is widened across the sheet (its x scale over
        // its y), and once settled the bones are square again. widest: the largest x / y; settled: after a 10 s halt.
        static float MostPuff(Dewlap d, out float settled)
        {
            CreatureSpecies sp = Species(d);
            CreatureSim sim = FlowKit.Sim(sp, 1f);
            int start = sp.Layout.Start(KitView.Parts(sp).Count - 1), n = d.stations * d.links;
            float most = 1f;
            float Puff()
            {
                Matrix4x4[] w = FlowKit.Bones(sim);
                float p = 1f;
                for (int i = start; i < start + n; i++) p = Mathf.Max(p, ((Vector3)w[i].GetColumn(0)).magnitude / ((Vector3)w[i].GetColumn(1)).magnitude);
                return p;
            }
            for (int t = 0; t < 420; t++)
            {
                sim.Tick(FlowKit.kDt, t < 180 ? new Vector3(0f, 0f, 30f) : new Vector3(30f, 0f, 2f));
                if (t % 5 == 4) most = Mathf.Max(most, Puff());
            }
            sim.Halt = true;
            for (int t = 0; t < 600; t++) sim.Tick(FlowKit.kDt, new Vector3(30f, 0f, 2f));
            settled = Puff();
            return most;
        }

        [Test] public void Dewlap_Billows_WithTheSwing()
        {
            float most = MostPuff(Flap(), out float settled);
            TestContext.Out.WriteLine($"widest puff {most:F3} x, settled {settled:F5} x");
            Assert.Greater(most, 1.02f, "the swinging pouch puffs out");
            Assert.LessOrEqual(most, 1f + Dewlap.PuffPerBillow * Flap().billow + 1e-4f, "at most PuffPerBillow x billow");
            Assert.Less(settled, 1.002f, "settled, the pouch is back to its drawn width");
        }

        // RED: billow 0 never puffs.
        [Test] public void Red_Dewlap_NoBillow_NoPuff()
        {
            Dewlap d = Flap(); d.billow = 0f;
            Assert.AreEqual(1f, MostPuff(d, out _), 1e-5f, "billow 0 never puffs");
        }

        // The sail's triangles wind with their vertex normals (outward on both faces, the rim and the end caps).
        [Test] public void Dewlap_WindsOutward()
        {
            CreatureSpecies sp = Species(Flap());
            int first = VertexCount(Parts(true, null));
            Mesh mesh = FlowKit.Mesh(sp);
            Vector3[] v = mesh.vertices, n = mesh.normals; int[] tri = mesh.triangles;
            Object.DestroyImmediate(mesh);
            int bad = 0, tris = 0;
            for (int k = 0; k < tri.Length; k += 3)
            {
                int i0 = tri[k], i1 = tri[k + 1], i2 = tri[k + 2];
                if (i0 < first) continue;
                Vector3 g = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                if (g.sqrMagnitude < 1e-14f) continue;
                tris++;
                if (Vector3.Dot(g, n[i0] + n[i1] + n[i2]) <= 0f) bad++;
            }
            Assert.Greater(tris, 300);
            Assert.AreEqual(0, bad, $"{bad} of {tris} sail triangles wind against their normals");
        }

        [Test] public void Validate_Messages()
        {
            Dewlap d = Flap(); d.depthAt = new[] { 0.1f, 0.1f };
            var ctx = new SpeciesCtx(Parts(true, null), new CreatureModule[0], 24, false);
            StringAssert.Contains("dewlap", d.Validate(ctx));
            Dewlap back = Flap(); back.toJoint = 0;
            StringAssert.Contains("dewlap", back.Validate(ctx));
            var noBody = new SpeciesCtx(new List<CreaturePart> { Flap() }, new CreatureModule[0], 24, false);
            Assert.IsNotNull(Flap().Validate(noBody));
            Assert.IsNull(Flap().Validate(ctx));
            Dewlap thinRoot = Flap(); thinRoot.rootThickness = 0.5f * thinRoot.thickness;
            StringAssert.Contains("rootThickness", thinRoot.Validate(ctx));
            Dewlap balloon = Flap(); balloon.billow = 0.5f;
            StringAssert.Contains("billow", balloon.Validate(ctx));
            Dewlap loose = Flap(); loose.maxTwistDeg = 0f;
            StringAssert.Contains("maxTwistDeg", loose.Validate(ctx));
        }
    }
}
