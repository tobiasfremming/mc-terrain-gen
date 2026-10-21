using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // FlowBody (Apex redesign §5.1): one chain from the head to the tail tip, drawn as one surface whose section is a
    // superellipse with a sharp dorsal hump, a keel, flank masses and a tall ribbon tail. The fixture: 24 joints at
    // 0.1 m, a narrow raised neck, a chest with a hump and a keel, a ribbon tail from t = 0.55, one mirrored hind leg.
    public class CreatureFlowBodyTests
    {
        const int kJoints = 24;
        const float kChestT = 0.3f;
        const float kHump = 0.06f, kKeel = 0.08f, kChestHH = 0.14f;

        internal static FlowKey[] Profile(float tailBlade = 1f) => new[]
        {
            new FlowKey { t = 0f,    halfWidth = 0.05f, halfHeight = 0.06f, clearance = 0.8f, bendLimitDeg = 8f },
            new FlowKey { t = 0.12f, halfWidth = 0.04f, halfHeight = 0.05f, clearance = 0.72f, bendLimitDeg = 14f },
            new FlowKey { t = kChestT, halfWidth = 0.12f, halfHeight = kChestHH, clearance = 0.5f, bendLimitDeg = 10f, hump = kHump, keel = kKeel, flank = 0.02f },
            new FlowKey { t = 0.45f, halfWidth = 0.1f,  halfHeight = 0.11f, clearance = 0.42f, bendLimitDeg = 12f },
            new FlowKey { t = 0.55f, halfWidth = 0.03f, halfHeight = 0.12f, clearance = 0.4f,  bendLimitDeg = 18f, blade = tailBlade },
            new FlowKey { t = 1f,    halfWidth = 0.01f, halfHeight = 0.04f, clearance = 0.3f,  bendLimitDeg = 20f, blade = tailBlade },
        };

        internal static FlowBody Body(float tailBlade = 1f, float humpSharpness = 8f) => new FlowBody
        {
            id = "body", jointCount = kJoints, spacing = 0.1f, headCapLength = 0.06f, tailTipLength = 0.12f,
            profile = Profile(tailBlade), humpSharpness = humpSharpness,
        };

        internal static Leg2Bone HindLeg() => new Leg2Bone
        {
            id = "hind", at = new PartSocket(10, 0, new Vector3(0.09f, -0.05f, 0f)), mirror = true,
            restOffset = new Vector3(0.22f, 0f, 0.02f), upperLength = 0.24f, lowerLength = 0.24f, upperRadius = 0.04f, lowerRadius = 0.03f,
            pole = new Vector3(0.3f, 0.2f, 0.9f), stepThreshold = 0.15f, stepDuration = 0.2f, stepLift = 0.06f,
        };

        internal static CreatureSpecies Species(FlowBody body) =>
            FlowKit.Build("FlowBodyFixture", 1f, new List<CreaturePart> { body, HindLeg(), CreatureKitComposeTests.TwoEyes() }, new CreatureModule[] { new Walk { speed = 0.8f, turnRateDeg = 160f, accel = 2f, turnSlowdown = 0.5f } });

        internal static FlowBody Part(CreatureSpecies sp)
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is FlowBody f) return f;
            return null;
        }

        // the body fraction of joint k (0 head joint .. n-1 last joint)
        internal static float UAtJoint(FlowBody b, float k) => (b.headCapLength + b.spacing * k) / (b.headCapLength + b.spacing * (b.jointCount - 1) + b.tailTipLength);

        // ------------------------------------------------------------ compose

        [Test] public void FlowBody_Composes_AsTheChain()
        {
            CreatureSpecies sp = Species(Body());
            Assert.AreEqual(kJoints, sp.JointCount);
            Assert.AreEqual(0, sp.ringSegments, "the SpineBody mesh is off: FlowBody draws the body");
            Assert.IsNull(sp.Validate());
            Assert.AreEqual(0.1f, sp.spacing, 1e-6f);
            // the joints follow the profile (monotone cubic, exact at the keys)
            Assert.AreEqual(0.05f, sp.joints[0].halfWidth, 1e-5f);
            Assert.AreEqual(0.8f, sp.joints[0].clearance, 1e-5f);
            Assert.AreEqual(0.01f, sp.joints[kJoints - 1].halfWidth, 1e-5f);
        }

        // ------------------------------------------------------------ hump

        // Half-angle of the hump term (deg from the top) at the chest (joint `chest`, fractional): where the section's top
        // rise over the same body without a hump drops to half the hump (`hump`, the chest key's).
        static float HumpHalfAngle(FlowBody body, float chest, float hump)
        {
            FlowBody flat = (FlowBody)body.Clone();
            for (int i = 0; i < flat.profile.Length; i++) flat.profile[i].hump = 0f;
            float u = UAtJoint(body, chest);
            for (int d = 0; d <= 900; d++)
            {
                float phi = 0.5f * Mathf.PI - d * 0.1f * Mathf.Deg2Rad;
                body.Section(u, phi, out Vector2 a); flat.Section(u, phi, out Vector2 b);
                if (a.y - b.y < 0.5f * hump) return d * 0.1f;
            }
            return 90f;
        }

        static string HumpProblem(FlowBody body) => HumpProblem(body, kChestT * (kJoints - 1), kHump);

        // The profile key with the largest hump (the Apex's own part data): its joint (fractional) and its hump.
        internal static float HumpKey(FlowBody body, out float hump)
        {
            int best = 0;
            for (int i = 1; i < body.profile.Length; i++) if (body.profile[i].hump > body.profile[best].hump) best = i;
            hump = body.profile[best].hump;
            return body.profile[best].t * (body.jointCount - 1);
        }

        internal static string HumpProblem(FlowBody body, float chest, float hump)
        {
            float u = UAtJoint(body, chest);
            body.Section(u, 0.5f * Mathf.PI, out Vector2 top);
            for (int k = 0; k < 3600; k++)
            {
                float phi = -0.5f * Mathf.PI + 2f * Mathf.PI * k / 3600f;
                body.Section(u, phi, out Vector2 o);
                if (o.y > top.y + 1e-6f) return $"the point at {phi * Mathf.Rad2Deg:F1} deg ({o.y:F4}) is above the top ({top.y:F4})";
            }
            float half = HumpHalfAngle(body, chest, hump);
            TestContext.Out.WriteLine($"hump top {top.y:F4} m, half-angle {half:F1} deg");
            return half < 25f ? null : $"the hump is broad: its term halves {half:F1} deg from the top (>= 25)";
        }

        [Test] public void FlowBody_Hump_IsSharpPeak()
        {
            Assert.IsNull(HumpProblem(Part(Species(Body()))));
        }

        [Test] public void Red_Hump_Broad()
        {
            Assert.IsNotNull(HumpProblem(Part(Species(Body(humpSharpness: 2f)))), "a sharpness-2 hump must fail the half-angle test");
        }

        // ------------------------------------------------------------ keel

        static string KeelProblem(FlowBody body) => KeelProblem(body, kChestT * (kJoints - 1), kChestHH, kKeel);

        // The profile key with the deepest keel: its joint (fractional), its half height and its keel.
        internal static float KeelKey(FlowBody body, out float halfHeight, out float keel)
        {
            int best = 0;
            for (int i = 1; i < body.profile.Length; i++) if (body.profile[i].keel > body.profile[best].keel) best = i;
            halfHeight = body.profile[best].halfHeight; keel = body.profile[best].keel;
            return body.profile[best].t * (body.jointCount - 1);
        }

        internal static string KeelProblem(FlowBody body, float chest, float chestHH, float keelDepth)
        {
            float u = UAtJoint(body, chest);
            float low = 0f;
            for (int k = 0; k < 3600; k++)
            {
                body.Section(u, -0.5f * Mathf.PI + 2f * Mathf.PI * k / 3600f, out Vector2 o);
                low = Mathf.Min(low, o.y);
            }
            TestContext.Out.WriteLine($"chest lowest point {low:F4} m below the joint (oval {chestHH}, keel {keelDepth})");
            return low <= -(chestHH + 0.9f * keelDepth) ? null : $"no keel: the chest's lowest point is {-low:F4} m below the joint (< {chestHH + 0.9f * keelDepth:F4})";
        }

        [Test] public void FlowBody_Keel_JutsBelowTheOval()
        {
            Assert.IsNull(KeelProblem(Part(Species(Body()))));
        }

        [Test] public void Red_Keel_None()
        {
            FlowBody body = Body();
            for (int i = 0; i < body.profile.Length; i++) body.profile[i].keel = 0f;
            Assert.IsNotNull(KeelProblem(Part(Species(body))), "a body without a keel must fail the keel test");
        }

        // ------------------------------------------------------------ tail ribbon

        // The rest mesh's ring through the tail's middle joint (the tail from profile place tailFrom to the last joint):
        // height / width, and the smallest vertex angle of the loop.
        static string RibbonProblem(CreatureSpecies sp) => RibbonProblem(sp, 0.55f);

        // The first profile key that is the full ribbon (blade >= 1): the Apex's own tail start.
        internal static float RibbonFrom(FlowBody body)
        {
            foreach (FlowKey k in body.profile) if (k.blade >= 1f) return k.t;
            return 1f;
        }

        internal static string RibbonProblem(CreatureSpecies sp, float tailFrom)
        {
            FlowBody body = Part(sp);
            int n = body.jointCount;
            int first = Mathf.CeilToInt(tailFrom * (n - 1)), mid = (first + n - 1) / 2;
            float s = body.headCapLength + body.spacing * mid;
            List<Vector3> ring = Ring(sp, s);
            if (ring.Count < 8) return $"no ring at the tail's middle joint {mid} (s {s:F3}): {ring.Count} vertices";
            Vector3 c = Vector3.zero; foreach (Vector3 p in ring) c += p; c /= ring.Count;
            Vector3 nrm = Vector3.zero;
            for (int k = 0; k < ring.Count; k++) nrm += Vector3.Cross(ring[k] - c, ring[(k + 1) % ring.Count] - c);
            Vector3 along = nrm.normalized;
            Vector3 across = Vector3.Cross(Vector3.up, along).normalized, up = Vector3.Cross(along, across).normalized;
            float w0 = float.MaxValue, w1 = float.MinValue, h0 = float.MaxValue, h1 = float.MinValue;
            foreach (Vector3 p in ring)
            {
                float x = Vector3.Dot(p - c, across), y = Vector3.Dot(p - c, up);
                w0 = Mathf.Min(w0, x); w1 = Mathf.Max(w1, x); h0 = Mathf.Min(h0, y); h1 = Mathf.Max(h1, y);
            }
            float ratio = (h1 - h0) / Mathf.Max(1e-6f, w1 - w0);
            float minAngle = 180f;
            for (int k = 0; k < ring.Count; k++)
            {
                Vector3 p = ring[k], a = ring[(k + ring.Count - 1) % ring.Count], b = ring[(k + 1) % ring.Count];
                minAngle = Mathf.Min(minAngle, Vector3.Angle(a - p, b - p));
            }
            TestContext.Out.WriteLine($"tail joint {mid}: height {h1 - h0:F4} width {w1 - w0:F4} ratio {ratio:F2}, smallest loop angle {minAngle:F1} deg");
            if (ratio < 3f) return $"the tail section is not a ribbon: height/width {ratio:F2} < 3";
            if (minAngle < 60f) return $"the ribbon's edge is sharp: a loop angle of {minAngle:F1} deg < 60";
            return null;
        }

        // The rest mesh's body ring at arc s (uv0.x, metres from the front tip), seam duplicate dropped.
        static List<Vector3> Ring(CreatureSpecies sp, float s)
        {
            Mesh mesh = FlowKit.Mesh(sp);
            Vector3[] v = mesh.vertices;
            var uv0 = new List<Vector2>(); var uv1 = new List<Vector2>();
            mesh.GetUVs(0, uv0); mesh.GetUVs(1, uv1);
            Object.DestroyImmediate(mesh);
            var o = new List<Vector3>();
            for (int i = 0; i < v.Length; i++)
                if (IsBody(sp, uv1[i]) && Mathf.Abs(uv0[i].x - s) < 1e-4f && uv0[i].y < 1f - 1e-4f) o.Add(v[i]);
            return o;
        }

        internal static bool IsBody(CreatureSpecies sp, Vector2 uv1)
        {
            int part = Mathf.RoundToInt(uv1.x);
            MaterialSlots slots = MaterialSlots.For(sp);
            FlowBody b = Part(sp);   // the body's own kinds (the Apex draws its tail and stripe in the wing's wingfan)
            return part == 0 || part == slots.SlotOf(b.tailKind) || part == slots.SlotOf(b.dorsalStripeKind);
        }

        [Test] public void FlowBody_Tail_IsARibbon()
        {
            Assert.IsNull(RibbonProblem(Species(Body())));
        }

        // RED for the rounded edges: a sharp hump ridge run along the whole ribbon makes its top edge a blade.
        [Test] public void Red_Tail_SharpEdge()
        {
            FlowBody body = Body(humpSharpness: 64f);
            for (int i = 4; i < body.profile.Length; i++) body.profile[i].hump = 0.04f;
            string p = RibbonProblem(Species(body));
            Assert.IsNotNull(p, "a ribbon with a knife-edge top must fail the rounded-edge test");
            StringAssert.Contains("sharp", p);
        }

        [Test] public void Red_Tail_Round()
        {
            Assert.IsNotNull(RibbonProblem(Species(Body(tailBlade: 0f))), "an oval tail (blade 0) must fail the ribbon test");
        }

        // ------------------------------------------------------------ bending, normals

        [Test] public void FlowBody_NoInvertedTriangles_TightCircle()
        {
            Assert.IsNull(KitChecks.MaxBendProblem(Species(Body())));
        }

        // The spec's stance (§1): the neck raised steeply, the head highest, hump and keel on - head joint 0.45 m above
        // the chest joint. The walking sim keeps each segment `spacing` long, so this neck stands far steeper than a rest
        // laid one spacing apart along the ground (SpineBody's rest); restAtStance binds it as it stands.
        internal static FlowBody SteepNeck()
        {
            FlowBody body = Body();
            body.profile[0].clearance = 0.95f; body.profile[1].clearance = 0.85f;   // chest (t 0.3) 0.5
            return body;
        }

        // The worst facing of a body triangle while standing (walking straight, sampled every 0.25 s for 1.25 s): the
        // least cosine between a triangle's skinned normal and its rest normal carried by its bone (< 0 = inverted).
        static float StandingFacing(FlowBody body, out CreatureSpecies sp, out CreatureSim sim)
        {
            sp = Species(body);
            sim = FlowKit.Sim(sp, 1f);
            var m = new CreaturePteroWingTests.Skinned(sp);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            float worst = 1f;
            for (int step = 0; step < 6; step++)
            {
                FlowKit.Run(sim, 0.25f, new Vector3(0f, 0f, 3f));
                m.Pose(sim);
                Matrix4x4[] world = FlowKit.Bones(sim);
                for (int k = 0; k < m.T.Length; k += 3)
                {
                    int i0 = m.T[k], i1 = m.T[k + 1], i2 = m.T[k + 2];
                    if (!IsBody(sp, m.Uv1[i0])) continue;
                    Vector3 rest = Vector3.Cross(m.V[i1] - m.V[i0], m.V[i2] - m.V[i0]);
                    if (rest.magnitude < 1e-12f) continue;   // a pole's zero-area triangles
                    int bone = (int)m.Uv2[i0].x;
                    Vector3 want = (world[bone] * bind[bone]).MultiplyVector(rest);
                    Vector3 now = Vector3.Cross(m.P[i1] - m.P[i0], m.P[i2] - m.P[i0]);
                    worst = Mathf.Min(worst, Vector3.Dot(now, want) / Mathf.Max(1e-30f, now.magnitude * want.magnitude));
                }
            }
            TestContext.Out.WriteLine($"restAtStance {body.restAtStance}: worst standing facing cos {worst:F2}");
            return worst;
        }

        [Test] public void FlowBody_SteepNeck_HumpAndKeel_NoInvertedTriangles()
        {
            FlowBody body = SteepNeck();
            float facing = StandingFacing(body, out CreatureSpecies sp, out CreatureSim sim);
            int chest = Mathf.RoundToInt(kChestT * (kJoints - 1));
            Assert.GreaterOrEqual(sp.joints[0].clearance - sp.joints[chest].clearance, 0.4f, "a steep neck");
            for (int i = 1; i < sp.JointCount; i++) Assert.Greater(sim.Joints[0].y, sim.Joints[i].y, $"joint {i} above the head");
            Assert.GreaterOrEqual(facing, 0.5f, "standing: every body triangle faces its bind way, with margin");
            Assert.IsNull(KitChecks.MaxBendProblem(sp));
        }

        // The runtime view is reused on every recompile (CreatureSpecies.CompileKit): switching the body FlowBody ->
        // SpineBody in the Inspector must clear restAtSpacing and restore the SpineBody's rings, not keep the FlowBody's.
        [Test] public void FlowBody_SwitchToSpineBody_Recompile_ClearsRestAtSpacing()
        {
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = "FlowSwitch"; sp.scale = 1f; sp.kitVersion = 1;
            sp.parts = new List<CreaturePart> { SteepNeck(), HindLeg(), CreatureKitComposeTests.TwoEyes() };
            sp.modules = new List<CreatureModule> { new Walk { speed = 0.8f, turnRateDeg = 160f, accel = 2f, turnSlowdown = 0.5f } };
            CreatureSpecies flow = sp.Compiled.Runtime;
            Assert.IsTrue(flow.restAtSpacing, "FlowBody restAtStance on binds at spacing");
            Assert.AreEqual(0, flow.ringSegments);
            CreatureKit.SpineBody spine = CreatureKitComposeTests.Body(kJoints);
            sp.parts[0] = spine;
            sp.InvalidateCompiled();   // what OnValidate does
            CreatureSpecies view = sp.Compiled.Runtime;
            Assert.AreSame(flow, view, "the runtime view is reused");
            Assert.IsFalse(view.restAtSpacing, "a SpineBody never binds at spacing");
            Assert.AreEqual(spine.ringSegments, view.ringSegments);
            Assert.AreEqual(spine.ringsPerSegment, view.ringsPerSegment);
            Object.DestroyImmediate(sp);
        }

        // RED: the same steep neck bound one spacing apart along the ground (restAtStance off) folds the back - the
        // inside of the neck-root bend, under the hump - in the standing pose: a triangle turned well past edge-on.
        [Test] public void Red_SteepNeck_FlatBind_FoldsTheBack()
        {
            FlowBody flat = SteepNeck(); flat.restAtStance = false;
            Assert.LessOrEqual(StandingFacing(flat, out _, out _), -0.1f, "a flat bind of a steep neck must fold in the stance");
        }

        // Every rest vertex normal points away from its ring's centre line (the poles: away from the next ring), and
        // every body triangle faces the way its vertices' normals do.
        static string NormalsProblem(CreatureSpecies sp)
        {
            Mesh mesh = FlowKit.Mesh(sp);
            Vector3[] v = mesh.vertices, n = mesh.normals; int[] tri = mesh.triangles;
            var uv0 = new List<Vector2>(); var uv1 = new List<Vector2>();
            mesh.GetUVs(0, uv0); mesh.GetUVs(1, uv1);
            Object.DestroyImmediate(mesh);
            // rings by arc
            var rings = new SortedDictionary<float, List<int>>();
            for (int i = 0; i < v.Length; i++)
            {
                if (!IsBody(sp, uv1[i])) continue;
                if (!rings.TryGetValue(uv0[i].x, out List<int> l)) rings[uv0[i].x] = l = new List<int>();
                l.Add(i);
            }
            var keys = new List<float>(rings.Keys);
            if (keys.Count <= 3 * (kJoints - 1)) return $"only {keys.Count} rings along the body";
            Vector3 Centre(List<int> r) { Vector3 c = Vector3.zero; foreach (int i in r) c += v[i]; return c / r.Count; }
            int bad = 0; string first = null;
            for (int k = 0; k < keys.Count; k++)
            {
                List<int> r = rings[keys[k]];
                Vector3 c = Centre(r);
                bool pole = (v[r[0]] - v[r[r.Count / 2]]).sqrMagnitude < 1e-12f;
                if (pole) c = Centre(rings[keys[k == 0 ? 1 : k - 1]]);
                foreach (int i in r)
                    if (!(Vector3.Dot(n[i], v[i] - c) > 0f)) { bad++; if (first == null) first = $"vertex {i} at arc {keys[k]:F3} (ring {k}{(pole ? ", pole" : "")}) normal {n[i]} offset {v[i] - c}"; }
            }
            if (bad > 0) return $"{bad} vertex normals point inward, first {first}";
            for (int t = 0; t < tri.Length; t += 3)
            {
                int a = tri[t], b = tri[t + 1], c = tri[t + 2];
                if (!IsBody(sp, uv1[a])) continue;
                Vector3 face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (face.sqrMagnitude < 1e-16f) continue;
                if (!(Vector3.Dot(face, n[a] + n[b] + n[c]) > 0f)) return $"body triangle {t / 3} faces inward";
            }
            return null;
        }

        [Test] public void FlowBody_Normals_PointOut()
        {
            Assert.IsNull(NormalsProblem(Species(Body())));
        }

        [Test] public void Red_Normals_Flipped()
        {
            FlowBody.FlipNormalsForTest = true;
            try { Assert.IsNotNull(NormalsProblem(Species(Body())), "flipped normals must fail the normals test"); }
            finally { FlowBody.FlipNormalsForTest = false; }
        }

        // ------------------------------------------------------------ auto rigid head

        // A neck narrowing gently from the head (< 5 % per joint) and swelling into the chest, then the chest narrowing
        // into the tail inside the front half: no rigid head (SpineBody's rule took joints 0..10 - the whole neck and
        // chest - as one rigid block, and the walk's bends all fell on the chest-tail joint).
        internal static FlowBody GentleNeck()
        {
            FlowBody b = SteepNeck();
            b.profile[0].halfWidth = 0.04f; b.profile[0].halfHeight = 0.042f;
            b.profile[1].halfWidth = 0.036f; b.profile[1].halfHeight = 0.042f;
            return b;
        }

        [Test] public void FlowBody_AutoHead_NotTheChestNarrowingIntoTheTail()
        {
            FlowBody b = GentleNeck();
            SpineJoint[] js = b.BuildJoints();
            TestContext.Out.WriteLine($"FlowBody.AutoHead {FlowBody.AutoHead(js)}, SpineBody.AutoHead {CreatureKit.SpineBody.AutoHead(js)}");
            Assert.AreEqual(0, b.RigidHeadJoints, "no head joints in front of a neck that swells into the chest");
            Assert.AreEqual(0, FlowBody.AutoHead(SteepNeck().BuildJoints()), "the steep neck: unchanged (its first step narrows at joint 1)");
            Assert.IsNull(KitChecks.MaxBendProblem(Species(b)));
        }

        // RED: SpineBody's rule on the same profile makes a long rigid block and folds a sliver at its end.
        [Test] public void Red_AutoHead_SpineBodyRule()
        {
            FlowBody b = GentleNeck();
            b.rigidHeadJoints = CreatureKit.SpineBody.AutoHead(b.BuildJoints());
            Assert.Greater(b.rigidHeadJoints, 5, "SpineBody's rule reads the chest-tail narrowing as the neck");
            Assert.IsNotNull(KitChecks.MaxBendProblem(Species(b)), "that rigid block must fold the body in the tight circle");
        }

        // ------------------------------------------------------------ colour kinds

        [Test] public void FlowBody_ColourKinds_TailAndDorsalStripe()
        {
            FlowBody body = Body();
            body.tailColourFrom = 0.6f; body.dorsalStripeTo = 0.2f;
            CreatureSpecies sp = Species(body);
            MaterialSlots slots = MaterialSlots.For(sp);
            int tail = slots.SlotOf("tailfan"), stripe = slots.SlotOf("dorsalstripe");
            Assert.GreaterOrEqual(tail, PartLayers.LegacyIds); Assert.GreaterOrEqual(stripe, PartLayers.LegacyIds);
            Mesh mesh = FlowKit.Mesh(sp);
            var uv0 = new List<Vector2>(); var uv1 = new List<Vector2>();
            mesh.GetUVs(0, uv0); mesh.GetUVs(1, uv1);
            Object.DestroyImmediate(mesh);
            FlowBody f = Part(sp);
            float len = f.headCapLength + f.spacing * (kJoints - 1) + f.tailTipLength;
            int tails = 0, stripes = 0;
            for (int i = 0; i < uv0.Count; i++)
            {
                int part = Mathf.RoundToInt(uv1[i].x);
                if (!IsBody(sp, uv1[i])) continue;
                float u = uv0[i].x / len;
                if (part == tail) { tails++; Assert.GreaterOrEqual(u, 0.6f - 1e-4f); }
                if (part == stripe) { stripes++; Assert.LessOrEqual(u, 0.2f + 1e-4f); Assert.Greater(uv1[i].y, Mathf.Cos(15f * Mathf.Deg2Rad) - 1e-3f, "on the top"); }
                if (u > 0.6f + 1e-3f) Assert.AreEqual(tail, part, "the tail behind tailColourFrom is tailfan");
            }
            Assert.Greater(tails, 0); Assert.Greater(stripes, 0);
        }

        // The body's kinds are data (tailKind, dorsalStripeKind) and take a material slot only while their colour is on:
        // the plain body wants none; a tail and a stripe drawn in one kind (here the wing's `wingfan`, as the Apex does - it
        // is short of kit slots) share its one slot, every tail vertex behind tailColourFrom and every stripe vertex on it.
        static int NamedSlots(CreatureSpecies sp)
        {
            int n = 0;
            foreach (string k in MaterialSlots.For(sp).KindOfSlot) if (k != null) n++;
            return n;
        }

        [Test] public void FlowBody_ColourKinds_OnlyWhenDrawn_OneSlotWhenShared()
        {
            CreatureSpecies plain = Species(Body());
            Assert.AreEqual(0, NamedSlots(plain), "colours off: no kit slot");
            Assert.AreEqual(-1, MaterialSlots.For(plain).SlotOf("tailfan")); Assert.AreEqual(-1, MaterialSlots.For(plain).SlotOf("dorsalstripe"));

            FlowBody body = Body();
            body.tailColourFrom = 0.6f; body.dorsalStripeTo = 0.2f; body.tailKind = "wingfan"; body.dorsalStripeKind = "wingfan";
            CreatureSpecies sp = Species(body);
            MaterialSlots slots = MaterialSlots.For(sp);
            int shared = slots.SlotOf("wingfan");
            Assert.GreaterOrEqual(shared, PartLayers.LegacyIds);
            Assert.AreEqual(1, NamedSlots(sp), "one slot for the tail and the stripe");
            Assert.AreEqual(-1, slots.SlotOf("tailfan"), "tailfan not wanted");
            Mesh mesh = FlowKit.Mesh(sp);
            var uv0 = new List<Vector2>(); var uv1 = new List<Vector2>();
            mesh.GetUVs(0, uv0); mesh.GetUVs(1, uv1);
            Object.DestroyImmediate(mesh);
            FlowBody f = Part(sp);
            float len = f.headCapLength + f.spacing * (kJoints - 1) + f.tailTipLength;
            int tails = 0, stripes = 0;
            for (int i = 0; i < uv0.Count; i++)
            {
                int part = Mathf.RoundToInt(uv1[i].x);
                float u = uv0[i].x / len;
                if (u > 0.6f + 1e-3f && (part == 0 || part == shared)) { Assert.AreEqual(shared, part, "the tail behind tailColourFrom"); tails++; }
                if (u < 0.2f - 1e-3f && part == shared) stripes++;
            }
            Assert.Greater(tails, 0); Assert.Greater(stripes, 0);
        }

        // RED: the default kinds draw the same two colours in two kit slots (tailfan and dorsalstripe)
        [Test] public void Red_ColourKinds_DefaultKinds_TwoSlots()
        {
            FlowBody body = Body();
            body.tailColourFrom = 0.6f; body.dorsalStripeTo = 0.2f;
            Assert.AreEqual(2, NamedSlots(Species(body)));
        }

        // restAtStance: a stance segment so steep the rest run's floor stretches it by more than 1 % (~78 deg; the floor
        // starts at 75.5) is a Validate message; the fixtures' steep neck (76 deg, 0.03 %) is not; restAtStance off binds
        // along the ground (no message).
        [Test] public void FlowBody_Validate_StanceSegmentTooSteep()
        {
            var ctx = new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], kJoints, false);
            Assert.IsNull(SteepNeck().Validate(ctx), "the steep neck fixture");
            TestContext.Out.WriteLine($"max stance segment {FlowBody.MaxStanceSegmentDeg:F1} deg");
            Assert.AreEqual(78.1f, FlowBody.MaxStanceSegmentDeg, 0.1f);
            FlowBody cliff = Cliff();
            string e = cliff.Validate(ctx);
            StringAssert.Contains("restAtStance", e);
            cliff.restAtStance = false;
            Assert.IsNull(cliff.Validate(ctx), "off: the rest is laid along the ground");
        }

        // A head joint 0.2 m above joint 1 (0.1 m segments): the first segment would have to stand past vertical.
        static FlowBody Cliff()
        {
            FlowBody b = SteepNeck();
            var keys = new List<FlowKey>(b.profile);
            FlowKey k = keys[0]; k.t = 1f / (kJoints - 1); k.clearance = 0.75f; keys.Insert(1, k);
            b.profile = keys.ToArray();
            return b;
        }

        // RED: what the message guards against - a rise past the limit really is stretched by RestRun's floor (the bound
        // segment longer than spacing), one just inside it is not.
        [Test] public void Red_StanceSegmentTooSteep_Stretches()
        {
            CreatureSpecies sp = Species(SteepNeck());
            float c1 = sp.joints[1].clearance;
            float Segment(float rise)
            {
                sp.joints[0].clearance = c1 + rise;
                float run = CreatureSim.RestRun(sp, 1, 0.1f, 1f);
                return Mathf.Sqrt(run * run + rise * rise);
            }
            float inside = Segment(0.096f), past = Segment(0.2f);
            TestContext.Out.WriteLine($"bound segment 0-1: rise 0.096 -> {inside:F4} m, rise 0.2 -> {past:F4} m (spacing 0.1)");
            Assert.AreEqual(0.1f, inside, 1e-5f, "inside the limit: spacing long");
            Assert.Greater(past, 0.1f * 1.01f, "past it the floor stretches the segment");
        }

        [Test] public void FlowBody_Validate_ColourKindNamed()
        {
            var ctx = new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], kJoints, false);
            FlowBody b = Body(); b.tailColourFrom = 0.6f; b.tailKind = "";
            StringAssert.Contains("tailKind", b.Validate(ctx));
            b = Body(); b.dorsalStripeTo = 0.2f; b.dorsalStripeKind = null;
            StringAssert.Contains("dorsalStripeKind", b.Validate(ctx));
            b = Body(); b.tailKind = ""; b.dorsalStripeKind = "";
            Assert.IsNull(b.Validate(ctx), "colours off: the kinds are not used");
        }
    }
}
