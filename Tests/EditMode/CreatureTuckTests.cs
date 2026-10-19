using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's posture parts on the RockCrab (a stone crab): Tuck (scared -> halt, crouch, fold the legs and claws
    // ONTO the shell - legs flat under its margin, claws pressed across the face - and the EyeStalks into their orbits,
    // sleep exactly still, peek, unfold, walk on) and EyeStalk (a stalk on its own bone that folds, its eye riding the
    // tip), and the CreatureSim posture inputs they use (Scare, Halt, Crouch, Asleep: off by default, every other
    // species bit-identical - the goldens). Checks: validation; the scare input (weak scares ignored, the hold grows with
    // the intensity); the tucked pose is reached within its time bound; tucked, nothing moves (0 mm/frame, the sim
    // sleeps); tucked, the limbs lie on the rock (every folded segment touches the shell or the ground, sinks into the
    // shell no deeper than a bound and stands off it no farther than a claw's thickness; the stalks and eyes fold away);
    // the tucked silhouette is jagged (cut in well below its convex hull: teeth, knees, claws), a smooth shell is not;
    // no inverted triangle through a whole tuck; it unfolds and walks on; random scares are staggered (individuals never
    // tuck in sync); Froude (0.9x / 1.1x tuck in sqrt-scaled time); no GC through a tuck.
    // RED: a walking frame moves (the stillness check bites); standing limbs stand far off the rock and the old
    // fold-into-the-shell sinks them (the fit checks bite); a smooth shell is not jagged (the silhouette check bites).
    public class CreatureTuckTests
    {
        const float kDt = 1f / 60f;
        const int CreatureLab_FirstSeed = 7 + 1000;   // a lab group's seeds: (lab seed + slot) + 1000 + individual

        static CreatureSpecies Crab() => CreatureKitAssets.Load("RockCrab").Compiled.Runtime;

        static CreatureSim Walker(CreatureSpecies sp, float s, int seed = 1) =>
            new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed);

        static TuckRuntime TuckOf(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is TuckRuntime t) return t;
            return null;
        }

        static RigidLoft Shell(CreatureSpecies sp)
        {
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is RigidLoft l && l.id == "carapace") return l;
            return null;
        }

        static SpeciesCtx Ctx(IList<CreaturePart> parts, int joints = 5) => new SpeciesCtx(parts as IReadOnlyList<CreaturePart> ?? new List<CreaturePart>(parts), new CreatureModule[0], joints, false);

        static Vector3 Far(CreatureSpecies sp) => new Vector3(0.3f, 0f, 30f) * sp.scale;

        // Walks toward a far point for `seconds`, then scares it; returns the ticks until it sleeps tucked (-1: never).
        static int TuckAfterWalk(CreatureSim sim, TuckRuntime tk, float walkSeconds, float intensity = 1f, int max = 600)
        {
            Vector3 far = Far(sim.Species) * sim.SizeScale;
            for (int t = 0; t < (int)(walkSeconds * sim.TimeScale / kDt); t++) sim.Tick(kDt, far);
            sim.Scare(intensity);
            for (int t = 0; t < max; t++)
            {
                sim.Tick(kDt, far);
                if (tk.State == TuckRuntime.Phase.Curled) return t + 1;
            }
            return -1;
        }

        // ------------------------------------------------------------ skinning and the shell

        sealed class Skinner
        {
            public readonly Vector3[] v; public readonly List<Vector2> uv1 = new List<Vector2>(); public readonly List<Vector4> uv2 = new List<Vector4>();
            public readonly int[] tri; public readonly Matrix4x4[] bind, world, skin; public readonly Vector3[] pos;
            public Skinner(CreatureSpecies sp)
            {
                Mesh mesh = CreatureKitAssets.BuildMesh(sp);
                v = mesh.vertices; tri = mesh.triangles; mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
                Object.DestroyImmediate(mesh);
                bind = CreatureRestPose.BindPoses(sp);
                world = new Matrix4x4[sp.BoneCount]; skin = new Matrix4x4[sp.BoneCount]; pos = new Vector3[v.Length];
            }
            public void Skin(CreatureSim sim)
            {
                sim.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector4 s = uv2[i];
                    pos[i] = s.z * skin[(int)s.x].MultiplyPoint3x4(v[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(v[i]);
                }
            }
            // the first inverted triangle (null: none)
            public string Inverted()
            {
                for (int k = 0; k < tri.Length; k += 3)
                {
                    int i0 = tri[k], i1 = tri[k + 1], i2 = tri[k + 2];
                    Vector3 rest = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    if (!(Vector3.Dot(now, want) > 0f)) return $"triangle {k / 3} (part {Mathf.RoundToInt(uv1[i0].x)}, bones {uv2[i0]}) inverted";
                }
                return null;
            }
        }

        // How far (m, this individual's) a world point lies outside the lumpy shell; <= 0 inside it or in the ground.
        internal static float ShellExcess(CreatureSim sim, RigidLoft loft, Vector3 world)
        {
            if (world.y <= 0f) return 0f;   // flat ground at y = 0: in the ground is hidden
            int j = loft.at.joint;
            float s = sim.SizeScale;
            Vector3 p = Quaternion.Inverse(sim.Rotations[j]) * (world - sim.Joints[j]) / s;
            // the section whose plane holds p (the centre line barely curls)
            float bestT = 0f, bestD = float.PositiveInfinity;
            for (int k = 0; k <= 200; k++)
            {
                float t = k / 200f;
                loft.Centre(t, out Vector3 c, out Vector3 up, out Vector3 right);
                Vector3 tan = Vector3.Cross(right, up);
                float d = Mathf.Abs(Vector3.Dot(p - c, tan));
                if (d < bestD) { bestD = d; bestT = t; }
            }
            RigidLoft.Section q = loft.SectionAt(bestT);
            Vector3 tangent = Vector3.Cross(q.right, q.up);
            float along = Vector3.Dot(p - q.c, tangent);
            float x = Vector3.Dot(p - q.c, q.right), y = Vector3.Dot(p - q.c, q.up);
            float ang = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y);
            float bestA = float.PositiveInfinity, R = 0f;
            for (int k = 0; k < 720; k++)
            {
                float phi = 2f * Mathf.PI * k / 720f;
                Vector3 sp = loft.Lumped(q, phi) - q.c;
                float dx = Vector3.Dot(sp, q.right), dy = Vector3.Dot(sp, q.up);
                float da = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, ang * Mathf.Rad2Deg));
                if (da < bestA) { bestA = da; R = Mathf.Sqrt(dx * dx + dy * dy); }
            }
            float excess = r - R;
            // past either end of the loft: outside by at least how far past its last section
            if ((bestT <= 0f && along < 0f) || (bestT >= 1f && along > 0f)) excess = Mathf.Max(excess, Mathf.Abs(along));
            return excess * s;
        }

        // Where the tucked limbs lie against the rock, per limb bone: `contact` the smallest distance of its vertices to
        // the shell or the ground (<= 0: touching), `depth` its deepest vertex inside the shell, `reach` how far its
        // farthest vertex stands off the shell and the ground. Every ArthropodLeg bone (with what rides it: the claw,
        // spines, hairs), the eye stalks and the eyes.
        struct BoneFit { public int bone, leg, seg; public string name; public float contact, depth, reach; }

        static List<BoneFit> LimbFit(CreatureSim sim, Skinner sk, RigidLoft loft)
        {
            CreatureSpecies sp = sim.Species;
            var of = new Dictionary<int, BoneFit>();
            for (int l = 0; l < sp.legs.Length; l++)
                for (int s = 0; s < sp.legs[l].SegmentCount; s++)
                    of[sp.LegBone(l, s)] = new BoneFit { bone = sp.LegBone(l, s), leg = l, seg = s, name = $"leg {l} seg {s}", contact = float.PositiveInfinity, depth = 0f, reach = float.NegativeInfinity };
            for (int e = 0; e < 2; e++) of[sp.EyeBone(e)] = new BoneFit { bone = sp.EyeBone(e), leg = -1, seg = -1, name = "eye " + e, contact = float.PositiveInfinity, reach = float.NegativeInfinity };
            foreach (IPartRuntime r in sim.PartRuntimes)
                if (r is EyeStalkRuntime es) of[es.Bone] = new BoneFit { bone = es.Bone, leg = -1, seg = -2, name = "stalk " + es.Part.id, contact = float.PositiveInfinity, reach = float.NegativeInfinity };
            for (int i = 0; i < sk.pos.Length; i++)
            {
                int b = (int)sk.uv2[i].x;
                if (!of.TryGetValue(b, out BoneFit f)) continue;
                Vector3 p = sk.pos[i];
                float ex = ShellExcess(sim, loft, p);
                float d = Mathf.Min(ex, p.y);
                f.contact = Mathf.Min(f.contact, d);
                f.reach = Mathf.Max(f.reach, d);
                if (p.y > 0f) f.depth = Mathf.Max(f.depth, -ex);
                of[b] = f;
            }
            var list = new List<BoneFit>(of.Values);
            list.Sort((x, y) => x.bone.CompareTo(y.bone));
            return list;
        }

        // How jagged a silhouette is: how deeply its outline cuts in below its convex hull - the valleys between knobs,
        // teeth, folded knees and a claw block. The outline's radius round the hull's centre (3-degree bins, the
        // farthest point in each) against the hull's radius on the same ray: the mean shortfall over the mean radius.
        // Any convex shape - a smooth dome, an oval, an oval cut flat by the ground - scores ~0. Averaged over the top,
        // front and side views (body frame `frame` at `origin`).
        internal static float Jaggedness(IList<Vector3> pts, Vector3 origin, Quaternion frame)
        {
            Quaternion inv = Quaternion.Inverse(frame);
            float sum = 0f;
            for (int view = 0; view < 3; view++)
            {
                var q = new List<Vector2>(pts.Count);
                foreach (Vector3 w in pts)
                {
                    Vector3 b = inv * (w - origin);
                    q.Add(view == 0 ? new Vector2(b.x, b.z) : view == 1 ? new Vector2(b.x, b.y) : new Vector2(b.z, b.y));
                }
                List<Vector2> hull = Hull(q);
                Vector2 c = Vector2.zero;
                foreach (Vector2 h in hull) c += h;
                c /= hull.Count;
                const int kBins = 120;
                var r = new float[kBins];
                foreach (Vector2 v in q)
                {
                    Vector2 d = v - c;
                    float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; if (a < 0f) a += 360f;
                    int k = Mathf.Min(kBins - 1, (int)(a / 360f * kBins));
                    r[k] = Mathf.Max(r[k], d.magnitude);
                }
                float cut = 0f, mean = 0f;
                for (int k = 0; k < kBins; k++)
                {
                    // the hull's radius at the bin's middle; the outline's: the bin's farthest point
                    float ang = (k + 0.5f) / kBins * 2f * Mathf.PI;
                    float rh = RayHull(hull, c, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)));
                    cut += Mathf.Max(0f, rh - r[k]); mean += rh;
                }
                sum += cut / mean;
            }
            return sum / 3f;
        }

        // Convex hull (Andrew's monotone chain), counter-clockwise.
        static List<Vector2> Hull(List<Vector2> pts)
        {
            var p = new List<Vector2>(pts);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    Vector2 v = pass == 0 ? p[i] : p[p.Count - 1 - i];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], v) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(v);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        // Distance from c (inside the hull) along unit d to the hull's boundary.
        static float RayHull(List<Vector2> hull, Vector2 c, Vector2 d)
        {
            float best = 0f;
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % hull.Count], e = b - a;
                float den = d.x * e.y - d.y * e.x;
                if (Mathf.Abs(den) < 1e-12f) continue;
                Vector2 ac = a - c;
                float t = (ac.x * e.y - ac.y * e.x) / den, u = (ac.x * d.y - ac.y * d.x) / den;
                if (t > 0f && u >= -1e-6f && u <= 1f + 1e-6f) best = Mathf.Max(best, t);
            }
            return best;
        }

        // The skinned mesh's points now (every vertex).
        static List<Vector3> Points(Skinner sk) => new List<Vector3>(sk.pos);


        // ------------------------------------------------------------ data

        [Test] public void Tuck_Validate_RejectsBadData()
        {
            CreatureSpecies sp = Crab();
            CompiledSpecies c = sp.Compiled;
            Assert.IsNull(c.Error, c.Error);
            var parts = new List<CreaturePart>(c.Parts);
            Tuck tuck = null;
            foreach (CreaturePart p in parts) if (p is Tuck t) tuck = t;
            Assert.NotNull(tuck);
            Assert.IsNull(tuck.Validate(Ctx(parts)));
            Assert.AreEqual(0, tuck.BoneCount(Ctx(parts)));
            // not the last part
            var moved = new List<CreaturePart>(parts); moved.Remove(tuck); moved.Insert(1, tuck);
            Assert.IsNotNull(tuck.Validate(Ctx(moved)), "must be the last part");
            var bad = (Tuck)tuck.Clone(); bad.legs[0].pitchDeg = new float[2];
            var withBad = new List<CreaturePart>(parts); withBad[withBad.Count - 1] = bad;
            Assert.IsNotNull(bad.Validate(Ctx(withBad)), "one pitch per segment");
            bad = (Tuck)tuck.Clone(); bad.legs[0].leg = "nope"; withBad[withBad.Count - 1] = bad;
            Assert.IsNotNull(bad.Validate(Ctx(withBad)), "a fold names a limb");
            bad = (Tuck)tuck.Clone(); bad.curlSeconds = 0f; withBad[withBad.Count - 1] = bad;
            Assert.IsNotNull(bad.Validate(Ctx(withBad)), "curl time > 0");
            var stalk = new EyeStalk { id = "s", at = new PartSocket(0, 1, Vector3.zero) };
            Assert.IsNull(stalk.Validate(Ctx(parts)));
            Assert.AreEqual(1, stalk.BoneCount(Ctx(parts)));
            stalk.layer = 3; Assert.IsNotNull(stalk.Validate(Ctx(parts)), "an eye layer");
        }

        // At rest the stalk's bone is rigid to its joint; the eye of its side sits on its tip.
        [Test] public void EyeStalk_RigidToJoint_EyeOnTip()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            var bones = new Matrix4x4[sp.BoneCount];
            int n = 0;
            for (int t = 0; t < 240; t++)
            {
                sim.Tick(kDt, Far(sp));
                sim.WriteBoneWorld(bones, 0);
                foreach (IPartRuntime r in sim.PartRuntimes)
                {
                    if (!(r is EyeStalkRuntime es)) continue;
                    n++;
                    EyeStalk p = es.Part;
                    Vector3 root = bones[es.Bone].MultiplyPoint3x4(Vector3.zero);
                    Assert.Less((root - es.Root).magnitude, 1e-5f, "root on its socket");
                    Vector3 tip = bones[es.Bone].MultiplyPoint3x4(new Vector3(0f, 0f, p.length + 0.004f));
                    Vector3 eye = bones[sp.EyeBone(p.EyeIndex)].MultiplyPoint3x4(Vector3.zero);
                    Assert.Less((tip - eye).magnitude, 2e-3f * sp.scale, "the eye sits on the stalk's tip");
                }
            }
            Assert.AreEqual(2 * 240, n, "two stalks");
        }

        // ------------------------------------------------------------ the scare input and the time bound

        [Test] public void Tuck_WeakScareIgnored_StrongScareTucksWithinBound()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            TuckRuntime tk = TuckOf(sim);
            Assert.NotNull(tk);
            for (int t = 0; t < 60; t++) sim.Tick(kDt, Far(sp));
            sim.Scare(0.1f);
            for (int t = 0; t < 30; t++) sim.Tick(kDt, Far(sp));
            Assert.AreEqual(TuckRuntime.Phase.Active, tk.State, "a scare below minScare is ignored");
            int ticks = TuckAfterWalk(sim, tk, 0.5f);
            float seconds = ticks * kDt, bound = (tk.Part.curlSeconds + 0.25f) * sim.TimeScale + 2f * kDt;
            TestContext.Out.WriteLine($"tucked and asleep {seconds:F3} s after the scare (bound {bound:F3} s)");
            Assert.Greater(ticks, 0, "it tucks");
            Assert.LessOrEqual(seconds, bound, "the rock pose within curlSeconds + settle");
            Assert.IsTrue(sim.Asleep, "tucked: the sim sleeps");
            Assert.IsTrue(sim.Halt);
            Assert.AreEqual(tk.Part.crouch * sim.SizeScale, sim.Crouch, 1e-6f, "fully crouched");
        }

        // A stronger scare holds longer (the same individual, the same tuck count).
        [Test] public void Tuck_HoldGrowsWithIntensity()
        {
            CreatureSpecies sp = Crab();
            float Hold(float intensity)
            {
                var sim = Walker(sp, sp.scale, 5);
                TuckRuntime tk = TuckOf(sim);
                Assert.Greater(TuckAfterWalk(sim, tk, 0.5f, intensity), 0);
                int t = 0;
                while (tk.State == TuckRuntime.Phase.Curled && t < 3000) { sim.Tick(kDt, Far(sp)); t++; }
                return t * kDt;
            }
            float weak = Hold(0.4f), strong = Hold(1f);
            TestContext.Out.WriteLine($"held {weak:F2} s at 0.4, {strong:F2} s at 1.0");
            Assert.Greater(strong, weak * 1.2f);
        }

        // ------------------------------------------------------------ tucked: still, and a rock

        // Tucked, nothing moves: every bone and so every vertex is bit-identical frame to frame (0 mm/frame).
        [Test] public void Tuck_Curled_ZeroMotion()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            TuckRuntime tk = TuckOf(sim);
            Assert.Greater(TuckAfterWalk(sim, tk, 1f), 0);
            var a = new Matrix4x4[sp.BoneCount]; var b = new Matrix4x4[sp.BoneCount];
            sim.WriteBoneWorld(a, 0);
            Vector3 head = sim.Joints[0];
            int frames = 0; float worst = 0f;
            while (tk.State == TuckRuntime.Phase.Curled && frames < 2000)
            {
                sim.Tick(kDt, Far(sp));
                if (tk.State != TuckRuntime.Phase.Curled) break;
                sim.WriteBoneWorld(b, 0);
                for (int i = 0; i < a.Length; i++)
                    for (int k = 0; k < 16; k++) worst = Mathf.Max(worst, Mathf.Abs(a[i][k] - b[i][k]));
                frames++;
            }
            TestContext.Out.WriteLine($"{frames} tucked frames, largest bone matrix change {worst:E2}");
            Assert.Greater(frames * kDt, 0.5f * tk.Part.holdSeconds * sim.TimeScale * 0.8f, "held tucked for its hold");
            Assert.AreEqual(0f, worst, "tucked: exactly still");
            Assert.AreEqual(head, sim.Joints[0]);
        }

        // RED for the stillness: walking frames move.
        [Test] public void Red_Walking_Moves()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            var a = new Matrix4x4[sp.BoneCount]; var b = new Matrix4x4[sp.BoneCount];
            for (int t = 0; t < 60; t++) sim.Tick(kDt, Far(sp));
            sim.WriteBoneWorld(a, 0);
            sim.Tick(kDt, Far(sp));
            sim.WriteBoneWorld(b, 0);
            float worst = 0f;
            for (int i = 0; i < a.Length; i++) for (int k = 0; k < 16; k++) worst = Mathf.Max(worst, Mathf.Abs(a[i][k] - b[i][k]));
            Assert.Greater(worst, 1e-4f, "a walking frame moves");
        }

        // Tucked, the limbs lie ON the rock (the user's rule: no hiding inside the shell - the legs and claws fold tight onto
        // its surface): every folded limb segment past the coxa (buried in the body by design) touches the shell or the
        // ground (contact <= 1 cm), sinks into the shell no deeper than 1.2 cm, and stands off it no farther than its own
        // thickness allows (reach <= 12 cm: the crusher claw pressed across the face); the eye stalks and eyes fold into their
        // orbits (nothing stands out). At 0.9x and 1.1x (bounds x the size).
        [TestCase(0.9f)]
        [TestCase(1.1f)]
        public void Tuck_Curled_LimbsLieOnTheShell(float f)
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale * f);
            TuckRuntime tk = TuckOf(sim);
            Assert.Greater(TuckAfterWalk(sim, tk, 1f), 0);
            var sk = new Skinner(sp);
            sk.Skin(sim);
            float s = sim.SizeScale;
            var folded = new HashSet<int>();
            for (int k = 0; k < tk.FoldedLegs; k++) folded.Add(tk.LegOf(k));
            Assert.AreEqual(sp.legs.Length, folded.Count, "every limb folds");
            var problems = new List<string>();
            foreach (BoneFit b in LimbFit(sim, sk, Shell(sp)))
            {
                TestContext.Out.WriteLine($"x{f} {b.name}: contact {b.contact * 1000f:F1} mm, depth {b.depth * 1000f:F1} mm, reach {b.reach * 1000f:F1} mm");
                if (b.seg >= 1)
                {
                    if (b.contact > kTouch * s) problems.Add($"{b.name} floats {b.contact * 1000f:F1} mm off the rock");
                    if (b.depth > kSink * s) problems.Add($"{b.name} sinks {b.depth * 1000f:F1} mm into the shell");
                    if (b.reach > kStandOff * s) problems.Add($"{b.name} stands {b.reach * 1000f:F1} mm off the rock");
                }
                else if (b.seg < 0 && b.reach > 4e-3f * s) problems.Add($"{b.name} stands out {b.reach * 1000f:F1} mm");
            }
            Assert.IsEmpty(problems, string.Join("; ", problems));
        }

        const float kTouch = 0.01f, kSink = 0.012f, kStandOff = 0.12f;

        // RED for the fit: walking, the limbs stand far off the rock (the stand-off check bites); with a fold that sinks the
        // legs into the shell (the old hide-inside fold), the depth check bites.
        [Test] public void Red_Standing_And_Sunk_Fail()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            for (int t = 0; t < 60; t++) sim.Tick(kDt, Far(sp));
            var sk = new Skinner(sp);
            sk.Skin(sim);
            float worst = 0f;
            foreach (BoneFit b in LimbFit(sim, sk, Shell(sp))) if (b.seg >= 1) worst = Mathf.Max(worst, b.reach);
            TestContext.Out.WriteLine($"standing: farthest limb {worst * 1000f:F1} mm off the rock");
            Assert.Greater(worst, kStandOff * sp.scale, "standing, the legs reach out");
            // the pre-2026-10-19 fold (legs folded back in, under and into the shell)
            CreatureSpecies a = CreatureKitAssets.LoadAsset("RockCrab");
            var k = ScriptableObject.CreateInstance<CreatureSpecies>();
            k.name = "RockCrab+sunk"; k.scale = a.scale; k.kitVersion = 1;
            k.parts = new List<CreaturePart>(a.parts); k.modules = new List<CreatureModule>(a.modules);
            var tuck = (Tuck)k.parts[k.parts.Count - 1].Clone();
            float[] inside = { 30, 168, 62, -12, -62 };
            for (int i = 0; i < tuck.legs.Length; i++)
                if (tuck.legs[i].pitchDeg.Length == inside.Length) { tuck.legs[i].pitchDeg = inside; tuck.legs[i].segmentYawDeg = null; tuck.legs[i].yawDeg = 0f; }
            k.parts[k.parts.Count - 1] = tuck;
            CreatureSpecies sunk = k.Compiled.Runtime;
            var sim2 = Walker(sunk, sunk.scale);
            Assert.Greater(TuckAfterWalk(sim2, TuckOf(sim2), 1f), 0);
            sk.Skin(sim2);
            float deepest = 0f;
            foreach (BoneFit b in LimbFit(sim2, sk, Shell(sunk))) if (b.seg >= 1) deepest = Mathf.Max(deepest, b.depth);
            TestContext.Out.WriteLine($"folded into the shell: deepest {deepest * 1000f:F1} mm");
            Object.DestroyImmediate(k);
            Assert.Greater(deepest, kSink * sunk.scale, "a fold into the shell is caught");
        }

        // Tucked, the rock is jagged, not a smooth dome: the silhouette's jaggedness (top, front, side) is well above a
        // smooth shell's - the margin teeth, the lumpy ridged dome, the folded knees and the claw block in front.
        [Test] public void Tuck_Curled_JaggedSilhouette()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            Assert.Greater(TuckAfterWalk(sim, TuckOf(sim), 1f), 0);
            var sk = new Skinner(sp);
            sk.Skin(sim);
            int j = Shell(sp).at.joint;
            float rock = Jaggedness(Points(sk), sim.Joints[j], sim.Rotations[j]);
            // the smooth shell alone (no lumps, no limbs, no teeth): an oval
            var smooth = (RigidLoft)Shell(sp).Clone(); smooth.lumpHeight = 0f;
            var pts = new List<Vector3>();
            for (int a = 0; a <= 200; a++) for (int b = 0; b < 240; b++)
            {
                Vector3 w = sim.Joints[j] + sim.Rotations[j] * (smooth.Surface(a / 200f, 2f * Mathf.PI * b / 240f) * sim.SizeScale);
                if (w.y >= 0f) pts.Add(w);
            }
            float oval = Jaggedness(pts, sim.Joints[j], sim.Rotations[j]);
            TestContext.Out.WriteLine($"jaggedness: tucked rock {rock:F4}, smooth shell {oval:F4} (min {kJagged})");
            Assert.Less(oval, kJagged, "RED: a smooth shell is not jagged");
            Assert.GreaterOrEqual(rock, kJagged, "the tucked crab reads as a jagged rock");
        }

        const float kJagged = 0.035f;


        // No inverted triangle anywhere through a whole tuck: walking, curling, tucked, peeking, unfolding, walking on.
        [Test] public void Tuck_WholeCycle_NoInvertedTriangles()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            TuckRuntime tk = TuckOf(sim);
            var sk = new Skinner(sp);
            var seen = new HashSet<TuckRuntime.Phase>();
            int frames = 0, walkedOn = 0;
            for (int t = 0; t < 1500; t++)
            {
                if (t == 60) sim.Scare(1f);
                float a = t / 60f;
                sim.Tick(kDt, new Vector3(Mathf.Sin(a) * 2f, 0f, 3f + a) * sp.scale);
                seen.Add(tk.State);
                // a second second of walking after it stood up, then done (a random scare may come later)
                if (seen.Contains(TuckRuntime.Phase.Uncurling) && tk.State == TuckRuntime.Phase.Active && ++walkedOn > 60) break;
                if (t % 3 != 0) continue;
                sk.Skin(sim);
                string bad = sk.Inverted();
                Assert.IsNull(bad, $"t={t} {tk.State} u={tk.Progress:F2}: {bad}");
                frames++;
            }
            Assert.IsTrue(seen.Contains(TuckRuntime.Phase.Curled) && seen.Contains(TuckRuntime.Phase.Peeking) && seen.Contains(TuckRuntime.Phase.Uncurling),
                          "the whole cycle ran: " + string.Join(",", seen));
            Assert.AreEqual(TuckRuntime.Phase.Active, tk.State, "back on its feet");
            TestContext.Out.WriteLine($"{frames} frames checked");
        }

        // ------------------------------------------------------------ uncurl, size, sync, GC

        // After the hold it peeks (stalks up first, body still down), unfolds and walks on.
        [Test] public void Tuck_Uncurl_ResumesWalking()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            TuckRuntime tk = TuckOf(sim);
            Assert.Greater(TuckAfterWalk(sim, tk, 1f), 0);
            Vector3 tucked = sim.Joints[0];
            bool peekedDown = false;
            int t = 0;
            while (tk.State != TuckRuntime.Phase.Active && t < 3000)
            {
                sim.Tick(kDt, Far(sp));
                if (tk.State == TuckRuntime.Phase.Peeking && tk.StalkFold < 0.5f && tk.Progress >= 1f) peekedDown = true;
                t++;
            }
            Assert.AreEqual(TuckRuntime.Phase.Active, tk.State, "it unfolds");
            Assert.IsTrue(peekedDown, "the stalks rise while the body is still tucked (the peek)");
            Assert.IsFalse(sim.Halt || sim.Asleep);
            Assert.AreEqual(0f, sim.Crouch, "standing again");
            for (int k = 0; k < (int)(2f / kDt); k++) sim.Tick(kDt, Far(sp));
            float walked = (sim.Joints[0] - tucked).magnitude;
            TestContext.Out.WriteLine($"walked {walked:F2} m in the 2 s after it stood up");
            Assert.Greater(walked, 0.25f * sp.speed * sim.TimeScale * 2f, "it walks on");
        }

        // Froude: a 0.9x and a 1.1x individual, ticked at dt x sqrt s, run the whole tuck (curl, sleep, peek, unfold) on
        // the same frames.
        [Test] public void Tuck_Size_FroudeTime()
        {
            CreatureSpecies sp = Crab();
            string Run(float f)
            {
                var sim = Walker(sp, sp.scale * f);
                TuckRuntime tk = TuckOf(sim);
                float dt = kDt * Mathf.Sqrt(f);
                var sb = new System.Text.StringBuilder();
                TuckRuntime.Phase ph = tk.State;
                for (int t = 0; t < 900; t++)
                {
                    if (t == 30) sim.Scare(1f);
                    sim.Tick(dt, Far(sp) * f);
                    if (tk.State != ph) { sb.Append($" {t}:{tk.State}"); ph = tk.State; }
                }
                return sb.ToString();
            }
            string a = Run(0.9f), b = Run(1.1f);
            TestContext.Out.WriteLine($"x0.9{a}; x1.1{b}");
            Assert.AreEqual(a, b, "the same phases on the same frames");
            StringAssert.Contains("Uncurling", a);
        }

        // Five individuals on random scares, consecutive seeds as the lab spawns them: they never tuck in sync - the first
        // tucks evenly staggered (golden-ratio sequence), the later ones independent.
        [Test] public void Tuck_RandomScares_Staggered()
        {
            CreatureSpecies sp = Crab();
            const int kN = 5;
            var sims = new CreatureSim[kN]; var tks = new TuckRuntime[kN];
            var starts = new List<float>[kN];
            for (int i = 0; i < kN; i++)
            {
                sims[i] = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, new Vector3(3f * i, 0f, 0f), Vector3.forward, sp.scale, CreatureLab_FirstSeed + i);
                tks[i] = TuckOf(sims[i]); starts[i] = new List<float>();
            }
            int allTucked = 0, frames = (int)(180f / kDt);
            var last = new int[kN];
            for (int t = 0; t < frames; t++)
            {
                int tucked = 0;
                for (int i = 0; i < kN; i++)
                {
                    float a = t * kDt * 0.3f + i;
                    sims[i].Tick(kDt, new Vector3(3f * i + Mathf.Sin(a) * 2f, 0f, Mathf.Cos(a) * 2f));
                    if (tks[i].Tucks != last[i]) { starts[i].Add(t * kDt); last[i] = tks[i].Tucks; }
                    if (tks[i].State != TuckRuntime.Phase.Active) tucked++;
                }
                if (tucked == kN) allTucked++;
            }
            // pairs: the share of one's tuck starts within 1 s of one of the other's (in sync: ~1)
            float worstShare = 0f;
            for (int i = 0; i < kN; i++)
            {
                Assert.GreaterOrEqual(starts[i].Count, 5, $"individual {i} tucks now and then ({starts[i].Count} in 3 min)");
                for (int j = 0; j < kN; j++)
                {
                    if (j == i) continue;
                    int near = 0;
                    foreach (float a in starts[i]) foreach (float b in starts[j]) if (Mathf.Abs(a - b) < 1f) { near++; break; }
                    worstShare = Mathf.Max(worstShare, (float)near / starts[i].Count);
                }
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < kN; i++) sb.Append($" [{i}] " + string.Join(" ", starts[i].ConvertAll(x => x.ToString("F1"))));
            TestContext.Out.WriteLine($"tuck starts:{sb}; worst pair: {worstShare:P0} of the starts within 1 s; all five tucked {allTucked * kDt:F1} s of 180");
            float minFirst = float.PositiveInfinity;
            for (int i = 0; i < kN; i++) for (int j = i + 1; j < kN; j++) minFirst = Mathf.Min(minFirst, Mathf.Abs(starts[i][0] - starts[j][0]));
            TestContext.Out.WriteLine($"first tucks >= {minFirst:F2} s apart");
            Assert.Greater(minFirst, 1f, "the first tucks staggered");
            Assert.Less(worstShare, 0.4f, "no two individuals tuck together");
            Assert.Less(allTucked * kDt, 0.1f * 180f, "rarely all tucked at once");
        }

        // Zero GC through a whole tuck (scare, curl, sleep, peek, unfold), bones included.
        [Test] public void Tuck_NoGC()
        {
            CreatureSpecies sp = Crab();
            var sim = Walker(sp, sp.scale);
            var bones = new Matrix4x4[sp.BoneCount];
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Far(sp)); sim.WriteBoneWorld(bones, 0); }
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() =>
            {
                for (int t = 0; t < 900; t++)
                {
                    if (t == 10) sim.Scare(1f);
                    sim.Tick(kDt, Far(sp));
                    sim.WriteBoneWorld(bones, 0);
                }
            });
            Assert.AreEqual(0, allocs);
        }

        // The posture inputs are off by default and a species without a Tuck never sets them.
        [Test] public void PostureInputs_OffByDefault()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("TentacleCrab").Compiled.Runtime;
            var sim = Walker(sp, sp.scale);
            for (int t = 0; t < 120; t++) sim.Tick(kDt, Far(sp));
            Assert.IsFalse(sim.Halt || sim.Asleep);
            Assert.AreEqual(0f, sim.Crouch);
            sim.Scare(1f);
            sim.Tick(kDt, Far(sp));
            Assert.IsFalse(sim.Halt || sim.Asleep, "no posture part: a scare changes nothing");
            Assert.AreEqual(1f, sim.TakeScare(), "the input is kept for whoever reads it");
        }

        // The lumps are within lumpHeight of the smooth loft, and a loft without lumps is exactly the smooth one.
        [Test] public void RigidLoft_Lumps_Bounded()
        {
            RigidLoft shell = Shell(Crab());
            Assert.Greater(shell.lumpHeight, 0f);
            float worst = 0f;
            for (int k = 1; k < 40; k++)
                for (int a = 0; a < 64; a++)
                {
                    RigidLoft.Section q = shell.SectionAt(k / 40f);
                    float phi = 2f * Mathf.PI * a / 64f;
                    worst = Mathf.Max(worst, (shell.Lumped(q, phi) - RigidLoft.Point(q, phi)).magnitude);
                }
            Assert.LessOrEqual(worst, shell.lumpHeight + 1e-6f);
            Assert.Greater(worst, 0.3f * shell.lumpHeight, "lumpy");
            var smooth = (RigidLoft)shell.Clone(); smooth.lumpHeight = 0f;
            RigidLoft.Section s0 = smooth.SectionAt(0.5f);
            Assert.AreEqual(RigidLoft.Point(s0, 1f), smooth.Lumped(s0, 1f));
        }
    }
}
