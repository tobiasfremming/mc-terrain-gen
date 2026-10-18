using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Creature kit (design 2026-10-18-creature-kit §3c): the generic checks every species asset must pass, one
    // fixture per asset in Config/Creatures. A new species gets all of them for free; a check that does not
    // apply names its reason in CreatureKitAssets.SkipReason (about the species, never "it fails").
    // The checks are problem functions (null = fine) so the RED tests below can prove each one bites without
    // catching NUnit assertions. Offline: `creature_kit.py check <Species>` (Tools~/CreatureHarness).
    [TestFixtureSource(typeof(CreatureKitAssets), nameof(CreatureKitAssets.Species))]
    public class CreatureKitTests
    {
        internal const float kDt = 1f / 60f;
        const float kWanderSeconds = 300f;

        readonly string name;
        CreatureSpecies sp;

        public CreatureKitTests(string species) { name = species; }

        [OneTimeSetUp] public void LoadSpecies() { sp = CreatureKitAssets.Load(name); }

        void Applies(string check)
        {
            Assert.IsNotNull(sp, CreatureKitAssets.PathOf(name) + " did not load as a CreatureSpecies");
            string why = CreatureKitAssets.SkipReason(sp, check);
            if (why != null) Assert.Ignore(why);
        }

        // 1. The species' own validation.
        [Test] public void Validate()
        {
            Applies(CreatureKitAssets.Validate);
            Assert.IsNull(CreatureKitAssets.LoadAsset(name).Validate(), name);   // a kit asset: compose errors included
        }

        // 2. The asset's script is CreatureSpecies and every serialized key is a field (a renamed or misspelt
        // field silently reads its default). SerializeReference types join in T2.5.
        [Test] public void NoMissingTypes()
        {
            Applies(CreatureKitAssets.NoMissingTypes);
            Assert.IsNull(KitChecks.MissingTypesProblem(name), name);
        }

        // 3. Bone layout: spine, legs, wings, eyes contiguous and summing to BoneCount; the bind poses and the
        // mesh agree with it (every skinned index in range, every bone that should carry skin does).
        [Test] public void Layout()
        {
            Applies(CreatureKitAssets.Layout);
            Assert.IsNull(KitChecks.LayoutProblem(sp), name);
        }

        // 4. MeshTime: timing, in CreatureKitPerfTests (TerrainGen.Tests.Perf) so routine kit runs leave it out.

        // 5. Skinned through a 5 s minimum-radius circle (the bend limits), no triangle inverts.
        [Test] public void MaxBend()
        {
            Applies(CreatureKitAssets.MaxBend);
            Assert.IsNull(KitChecks.MaxBendProblem(sp), name);
        }

        // 6. The renderer's culling bounds hold every skinned vertex of its largest individual in a tight turn.
        [Test] public void Bounds()
        {
            Applies(CreatureKitAssets.Bounds);
            Assert.IsNull(KitChecks.BoundsProblem(sp), name);
        }

        // 7. Five minutes in its lab enclosure (aviary for a flyer), two seeds at the smallest and the largest
        // individual: every joint, foot and knee stays inside, under the roof, and it does travel.
        [Test] public void Wander()
        {
            Applies(CreatureKitAssets.Wander);
            float big = CreatureLab.MaxScale(sp), small = sp.scale * (1f - Mathf.Abs(sp.look.sizeJitter));
            EnclosureArea area = KitChecks.LabArea(sp);
            Assert.IsNull(KitChecks.WanderProblem(sp, big, 1, area, kWanderSeconds), name + " largest");
            Assert.IsNull(KitChecks.WanderProblem(sp, small, 2, area, kWanderSeconds), name + " smallest");
        }

        // 8. Gait invariants: planted feet fixed, legs never over-reached, every walking leg steps, feelers never
        // step, never two gait groups in the air at once.
        [Test] public void Gait()
        {
            Applies(CreatureKitAssets.Gait);
            Assert.IsNull(KitChecks.GaitProblem(sp), name);
        }

        // 9. Size (Froude similarity): a 0.9x and a 1.1x individual walk the same motion, lengths x s and times
        // x sqrt(s): distance / s, height / s and steps within 1 %.
        [Test] public void Size()
        {
            Applies(CreatureKitAssets.Size);
            Assert.IsNull(KitChecks.SizeProblem(sp, sp.scale * 0.9f, sp.scale * 1.1f, sp.scale * 1.1f), name);
        }

        // 10. Brain + tick + bone write allocate nothing after warm-up (positive control first).
        [Test] public void Gc()
        {
            Applies(CreatureKitAssets.Gc);
            GcAllocs.AssertCounterWorks();
            int allocs = KitChecks.TickAllocs(sp, false);
            Assert.AreEqual(0, allocs, name + ": brain/tick/bones allocated");
        }

        // 11. Perf: timing, in CreatureKitPerfTests (TerrainGen.Tests.Perf).

        // 12. The lab can house it: a habitat from its data and an enclosure (or aviary) it fits.
        [Test] public void Lab()
        {
            Applies(CreatureKitAssets.Lab);
            Assert.IsNull(KitChecks.LabProblem(sp), name);
        }
    }

    // RED proofs: each generic check fails on a deliberately broken input.
    public class CreatureKitRedTests
    {
        static CreatureSpecies Monitor() => CreatureKitAssets.Load("DesertMonitor");

        // A wander that cannot move (safe radius 0) must fail the travel guard: containment alone would pass.
        [Test] public void Red_Wander_SafeRadiusZero_Fails()
        {
            var sp = Monitor();
            EnclosureArea area = KitChecks.LabArea(sp);
            area.safeRadius = 0f;
            string p = KitChecks.WanderProblem(sp, sp.scale, 1, area, 30f);
            TestContext.Out.WriteLine("RED: " + p);
            Assert.IsNotNull(p, "a creature pinned to the centre passed the wander check");
            Assert.IsNull(KitChecks.WanderProblem(sp, sp.scale, 1, KitChecks.LabArea(sp), 30f), "control: the real area passes");
        }

        // Comparing a 1.0x individual against the 0.9x one as if it were 1.1x must fail the 1 % size rule.
        [Test] public void Red_Size_WrongScale_Fails()
        {
            var sp = Monitor();
            string p = KitChecks.SizeProblem(sp, sp.scale * 0.9f, sp.scale * 1f, sp.scale * 1.1f);
            TestContext.Out.WriteLine("RED: " + p);
            Assert.IsNotNull(p, "a 1.0x individual passed as 1.1x");
        }

        // The GC check's counter: counts known allocations, and an allocating tick is seen.
        [Test] public void Red_Gc_PositiveControl()
        {
            GcAllocs.AssertCounterWorks();
            int allocs = KitChecks.TickAllocs(Monitor(), true);
            TestContext.Out.WriteLine("RED: an allocating tick counted " + allocs);
            Assert.Greater(allocs, 0, "an allocating tick counted 0: the GC check would be vacuous");
        }

        // A mesh index past the layout must be reported.
        [Test] public void Red_Layout_IndexOutOfRange_Fails()
        {
            var sp = Monitor();
            string p = KitChecks.SkinIndexProblem(sp, new List<Vector4> { new Vector4(0f, sp.BoneCount, 0.5f, 0.5f) }, null, sp.BoneCount);
            TestContext.Out.WriteLine("RED: " + p);
            Assert.IsNotNull(p);
        }
    }

    // The checks behind CreatureKitTests: each returns the first problem found, or null.
    static class KitChecks
    {
        const float kDt = CreatureKitTests.kDt;

        static CreatureSim Flat(CreatureSpecies sp, float sizeScale, int seed = 0)
            => new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sizeScale, seed);

        static Vector3 Weave(CreatureSim sim, int t)
        {
            Vector3 fwd = sim.Rotations[0] * Vector3.forward;
            return sim.Joints[0] + Quaternion.AngleAxis(50f * Mathf.Sin(t / 50f), Vector3.up) * fwd * (2f * sim.SizeScale);
        }

        static bool Feeler(CreatureSpecies sp, int l) => sp.legs[l].feeler;

        // ------------------------------------------------------------ 2. types

        internal static string MissingTypesProblem(string name)
        {
            string path = CreatureKitAssets.PathOf(name);
            List<string> keys = CreatureKitAssets.TopLevelKeys(path, out string guid);
            string want = CreatureKitAssets.ScriptGuid(CreatureKitAssets.kSpeciesScript);
            if (guid != want) return $"m_Script guid {guid} is not CreatureSpecies.cs ({want})";
            HashSet<string> fields = CreatureKitAssets.SerializedFields(typeof(CreatureSpecies));
            var unknown = new List<string>();
            // `references` is Unity's store of the [SerializeReference] parts and modules (checked below)
            foreach (string k in keys) if (!k.StartsWith("m_", StringComparison.Ordinal) && k != "references" && !fields.Contains(k)) unknown.Add(k);
            unknown.AddRange(CreatureKitAssets.KitYamlProblems(System.IO.File.ReadAllLines(CreatureKitAssets.FullPath(path))));
#if CREATURE_HARNESS
            // offline the YAML reader also sees nested keys (legs[i].x, look.x, ...)
            foreach (string k in HarnessYaml.UnknownKeys(CreatureKitAssets.FullPath(path))) if (!unknown.Contains(k)) unknown.Add(k);
#endif
            return unknown.Count > 0 ? "keys with no serialized field (stale or misspelt): " + string.Join(", ", unknown) : null;
        }

        // ------------------------------------------------------------ 3. layout

        internal static string LayoutProblem(CreatureSpecies sp)
        {
            int bones = CreatureKitAssets.RendererBones(sp);
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            var uv2 = new List<Vector4>(); var uv3 = new List<Vector4>();
            mesh.GetUVs(2, uv2);
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) mesh.GetUVs(3, uv3);
            int verts = mesh.vertexCount;
            Object.DestroyImmediate(mesh);
            if (uv2.Count != verts) return $"uv2 (skin) has {uv2.Count} entries for {verts} vertices";
            string skin = SkinIndexProblem(sp, uv2, sp.bodyPlan == CreatureBodyPlan.Cephalopod ? uv3 : null, bones);
            if (skin != null) return skin;
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) return null;   // own layout (OctopusBody); registry in T1.2

            int n = sp.JointCount, next = n;
            for (int l = 0; l < (sp.legs != null ? sp.legs.Length : 0); l++)
            {
                if (sp.LegBone(l, 0) != next) return $"leg {l} starts at bone {sp.LegBone(l, 0)}, expected {next} (contiguous after the spine and earlier legs)";
                next += sp.legs[l].SegmentCount;
            }
            for (int w = 0; w < sp.WingCount; w++)
            {
                if (sp.WingBone(w, 0) != next) return $"wing {w} starts at bone {sp.WingBone(w, 0)}, expected {next}";
                next += WingBoneLayout.BonesPerWing;
            }
            if (sp.EyeBone(0) != next || sp.EyeBone(1) != next + 1) return $"eyes at bones {sp.EyeBone(0)}, {sp.EyeBone(1)}, expected {next}, {next + 1}";
            next += 2;
            for (int a = 0; a < sp.AntennaCount; a++)
            {
                if (sp.AntennaBone(a, 0) != next) return $"antenna {a} starts at bone {sp.AntennaBone(a, 0)}, expected {next}";
                next += sp.antennae[a].Segments;
            }
            if (sp.BoneCount != next) return $"BoneCount {sp.BoneCount} != spine + legs + wings + eyes + antennae = {next}";
            int bind = CreatureRestPose.BindPoses(sp).Length;
            if (bind != sp.BoneCount) return $"{bind} bind poses for {sp.BoneCount} bones";

            // which bones carry skin: every spine joint, every leg bone, every wing bone, the eyes when they exist
            var used = new bool[bones];
            foreach (Vector4 s in uv2) { if (s.z > 0f) used[(int)s.x] = true; if (s.w > 0f) used[(int)s.y] = true; }
            var bare = new List<string>();
            for (int i = 0; i < n; i++) if (!used[i]) bare.Add("spine " + i);
            for (int l = 0; l < (sp.legs != null ? sp.legs.Length : 0); l++)
                for (int k = 0; k < sp.legs[l].SegmentCount; k++) if (!used[sp.LegBone(l, k)]) bare.Add($"leg {l} bone {k}");
            for (int w = 0; w < sp.WingCount; w++)
                for (int k = 0; k < WingBoneLayout.BonesPerWing; k++) if (!used[sp.WingBone(w, k)]) bare.Add($"wing {w} bone {k}");
            if (sp.eyeRadius > 0f) for (int e = 0; e < 2; e++) if (!used[sp.EyeBone(e)]) bare.Add("eye " + e);
            return bare.Count > 0 ? "bones with no skinned vertex: " + string.Join(", ", bare) : null;
        }

        // Skin indices within [0, bones) wherever their weight is non-zero (uv3: the octopus's extra influences).
        internal static string SkinIndexProblem(CreatureSpecies sp, List<Vector4> uv2, List<Vector4> uv3, int bones)
        {
            for (int i = 0; i < uv2.Count; i++)
            {
                Vector4 s = uv2[i];
                if ((s.z > 0f && !InRange(s.x, bones)) || (s.w > 0f && !InRange(s.y, bones))) return $"vertex {i} skins bones {s} outside 0..{bones - 1}";
                if (Mathf.Abs(s.z + s.w - 1f) > 1e-3f) return $"vertex {i} weights {s.z} + {s.w} != 1";
                if (uv3 != null && i < uv3.Count)
                {
                    Vector4 e = uv3[i];
                    if ((e.z > 0f && !InRange(e.x, bones)) || (e.w > 0f && !InRange(e.y, bones))) return $"vertex {i} extra influences {e} outside 0..{bones - 1}";
                }
            }
            return null;
        }

        static bool InRange(float b, int bones) => b >= 0f && b < bones && b == Mathf.Floor(b);

        // ------------------------------------------------------------ 5. max bend

        internal static string MaxBendProblem(CreatureSpecies sp)
        {
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = mesh.vertices; int[] tri = mesh.triangles;
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
            mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
            Object.DestroyImmediate(mesh);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var world = new Matrix4x4[sp.BoneCount]; var skin = new Matrix4x4[sp.BoneCount];
            var pos = new Vector3[v.Length];
            var sim = Flat(sp, sp.scale);
            float sc = sp.scale, ts = Mathf.Sqrt(sc);
            Vector3 centre = new Vector3(0f, 0f, 0.25f) * sc;
            float maxBend = 0f; int frames = 0;
            for (int t = 0; t < (int)(300 * ts); t++)
            {
                float a = t / 60f * 3f / ts;
                sim.Tick(kDt, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.12f * sc));
                if (t % 5 != 4) continue;
                for (int i = 1; i < sp.JointCount - 1; i++)
                    maxBend = Mathf.Max(maxBend, Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1]));
                sim.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector4 s = uv2[i];
                    pos[i] = s.z * skin[(int)s.x].MultiplyPoint3x4(v[i]) + s.w * skin[(int)s.y].MultiplyPoint3x4(v[i]);
                }
                for (int k = 0; k < tri.Length; k += 3)
                {
                    int i0 = tri[k], i1 = tri[k + 1], i2 = tri[k + 2];
                    Vector3 rest = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    if (!(Vector3.Dot(now, want) > 0f))
                        return $"t={t}: triangle {k / 3} (part {Mathf.RoundToInt(uv1[i0].x)}, bones {uv2[i0]}) inverted (dot {Vector3.Dot(now, want):E2})";
                }
                frames++;
            }
            TestContext.Out.WriteLine($"{sp.name}: max joint bend {maxBend:F1} deg over {frames} frames");
            float bite = CreatureLegTests.BendBite(sp);
            return maxBend > bite ? null : $"the chain never reached its bend limit ({maxBend:F1} <= {bite:F1} deg): the check does not bite";
        }

        // ------------------------------------------------------------ 6. bounds

        internal static string BoundsProblem(CreatureSpecies sp)
        {
            float s = CreatureLab.MaxScale(sp);
            var r = new CreatureRenderer(sp, null);
            try
            {
                Vector3 c = new Vector3(3f, 0f, -2f) * s;
                var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, c, Vector3.forward, s);
                r.Add(sim, new CreatureInstance { sizeScale = s });
                Vector3[] v = r.Mesh.vertices;
                var w = new List<Vector4>(); r.Mesh.GetUVs(2, w);
                int outside = 0; float worst = 0f; int checks = 0;
                for (int t = 0; t < 240; t++)
                {
                    float a = t / 60f * 2f / Mathf.Sqrt(s);
                    sim.Tick(kDt, c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.25f * sp.BodyLength * s));
                    if (t % 20 != 19) continue;
                    r.Render();
                    UnityEngine.Bounds b = r.LastBounds;
                    Matrix4x4[] skin = r.SkinForTest;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector4 q = w[i];
                        Vector3 p = q.z * skin[(int)q.x].MultiplyPoint3x4(v[i]) + q.w * skin[(int)q.y].MultiplyPoint3x4(v[i]);
                        if (!b.Contains(p)) { outside++; worst = Mathf.Max(worst, Mathf.Sqrt(b.SqrDistance(p))); }
                        checks++;
                    }
                }
                return outside == 0 ? null : $"{outside}/{checks} skinned vertices outside the culling bounds (worst {worst:F3} m)";
            }
            finally { r.Dispose(); }
        }

        // ------------------------------------------------------------ 7. wander

        // The lab's area for the species' largest individual: its enclosure, or the aviary (with its roof).
        internal static EnclosureArea LabArea(CreatureSpecies sp)
        {
            float big = CreatureLab.MaxScale(sp);
            EnclosureArea area = EnclosureArea.For(Vector3.zero, Vector3.up, CreatureLab.EnclosureRadius(sp, big), sp, big);
            if (sp.CanFly) area.maxHeight = AviarySizing.Height(sp, big, CreatureLab.kAviaryHeight);
            return area;
        }

        internal static string WanderProblem(CreatureSpecies sp, float sizeScale, int seed, EnclosureArea area, float seconds)
        {
            const float dt = 1f / 30f;
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, area.center, Vector3.forward, sizeScale, seed) { Bounds = area };
            ICreatureBrain brain = sp.CanFly ? new FlightBrain(area, seed) : (ICreatureBrain)new WanderBrain(area, seed);
            float far = 0f, path = 0f, top = 0f;
            Vector3 last = sim.Joints[0];
            for (int t = 0; t < (int)(seconds / dt); t++)
            {
                sim.Tick(dt, brain.Target(sim, dt));
                for (int i = 0; i < sim.Joints.Length; i++)
                {
                    float d = area.PlanarDistance(sim.Joints[i]);
                    far = Mathf.Max(far, d);
                    if (d > area.radius) return $"seed {seed} x{sizeScale:F2}: joint {i} {d:F2} m out of {area.radius:F2} m at t={sim.Time:F1}";
                    top = Mathf.Max(top, Vector3.Dot(sim.Joints[i] - area.center, area.up));
                }
                for (int l = 0; l < sim.Legs.Length; l++)
                {
                    if (area.PlanarDistance(sim.Legs[l].foot) > area.radius) return $"seed {seed} x{sizeScale:F2}: foot {l} outside at t={sim.Time:F1}";
                    if (area.PlanarDistance(sim.Legs[l].elbow) > area.radius) return $"seed {seed} x{sizeScale:F2}: knee {l} outside at t={sim.Time:F1}";
                }
                if (area.maxHeight > 0f && top > area.maxHeight + 0.5f * sizeScale) return $"seed {seed} x{sizeScale:F2}: {top:F2} m above the roof ({area.maxHeight:F2} m) at t={sim.Time:F1}";
                path += (sim.Joints[0] - last).magnitude; last = sim.Joints[0];
            }
            TestContext.Out.WriteLine($"{sp.name} seed {seed} x{sizeScale:F2}: farthest joint {far:F2} m of {area.radius:F2} m (safe {area.safeRadius:F2}), top {top:F2} m, head travelled {path:F0} m");
            // the travel guard: a body length per 30 s (10 in the five minutes), or containment proves nothing
            float lengths = seconds / 30f, need = lengths * sp.BodyLength * sizeScale;
            return path > need ? null : $"seed {seed}: the head travelled {path:F2} m <= {need:F2} m ({lengths:F0} body lengths): the containment check does not bite";
        }

        // ------------------------------------------------------------ 8. gait

        internal static string GaitProblem(CreatureSpecies sp)
        {
            float s = sp.scale;
            int n = sp.legs.Length;
            // weaving walk: planted feet fixed, reach never exceeded
            var sim = Flat(sp, s);
            var prev = new Vector3[n]; var prevPlanted = new bool[n];
            int planted = 0;
            for (int l = 0; l < n; l++) { prev[l] = sim.Legs[l].foot; prevPlanted[l] = Planted(sim.Legs[l]); }
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, Weave(sim, t));
                for (int l = 0; l < n; l++)
                {
                    LegState st = sim.Legs[l];
                    bool now = Planted(st);
                    if (!Feeler(sp, l) && now && prevPlanted[l])
                    {
                        float slide = (st.foot - prev[l]).magnitude;
                        if (slide >= 1e-5f * s) return $"weave t={sim.Time:F2}: planted foot {l} slid {slide * 1000f:F3} mm";
                        planted++;
                    }
                    float reach = (st.foot - st.shoulder).magnitude, max = sp.legs[l].TotalLength * s * 1.001f;
                    if (reach > max) return $"weave t={sim.Time:F2}: leg {l} reaches {reach:F4} m > its length {max:F4} m";
                    prev[l] = st.foot; prevPlanted[l] = now;
                }
            }
            if (planted < 1000) return $"only {planted} planted-foot samples: the check does not bite";

            // straight walk: every walking leg steps, feelers never, gait groups take turns
            sim = Flat(sp, s);
            var steps = new int[n]; var was = new bool[n];
            int overlap = 0, airborne = 0;
            Vector3 far = new Vector3(0f, 0f, 1000f * s);
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(kDt, far);
                int g = -1;
                for (int l = 0; l < n; l++)
                {
                    bool air = sim.Legs[l].stepping && sim.Legs[l].stepT >= 0f;
                    if (sim.Legs[l].stepping && !was[l]) steps[l]++;
                    was[l] = sim.Legs[l].stepping;
                    if (Feeler(sp, l)) { if (sim.Legs[l].stepping) return $"feeler {l} stepped at t={sim.Time:F2}"; continue; }
                    if (!air) continue;
                    if (g >= 0 && g != sp.legs[l].gaitGroup) overlap++;
                    g = sp.legs[l].gaitGroup;
                }
                if (g >= 0) airborne++;
            }
            TestContext.Out.WriteLine($"{sp.name}: steps per leg {string.Join(", ", steps)}; ticks with a group in the air {airborne}/900, two groups at once {overlap}");
            for (int l = 0; l < n; l++) if (!Feeler(sp, l) && steps[l] < 3) return $"leg {l} stepped {steps[l]} times in 15 s of walking";
            if (overlap > 0) return $"two gait groups in the air at once on {overlap} ticks";
            return null;
        }

        // A foot on the ground: not stepping, or waiting out its lift-off delay (arthropod metachronal wave).
        static bool Planted(LegState st) => !st.stepping || st.stepT < 0f;

        // ------------------------------------------------------------ 9. size

        internal struct Gauge { public float distance, height, steps; }

        // One straight walk at sizeScale, in the time and length units of `assumedScale` (the scale the caller
        // believes it is): ticks of kDt x sqrt(assumed / scale), lengths / assumed.
        internal static Gauge Froude(CreatureSpecies sp, float sizeScale, float assumedScale)
        {
            float k = assumedScale / sp.scale, dt = kDt * Mathf.Sqrt(k);
            var sim = Flat(sp, sizeScale);
            Vector3 far = new Vector3(0f, 0f, 1000f * assumedScale);
            float height = 0f; int samples = 0;
            for (int t = 0; t < 900; t++)
            {
                sim.Tick(dt, far);
                if (t < 120) continue;
                height += sim.Joints[0].y; samples++;
            }
            return new Gauge { distance = sim.Joints[0].z / assumedScale, height = height / samples / assumedScale, steps = sim.StepsTaken };
        }

        internal static string SizeProblem(CreatureSpecies sp, float scaleA, float scaleB, float assumedB)
        {
            Gauge a = Froude(sp, scaleA, scaleA), b = Froude(sp, scaleB, assumedB);
            TestContext.Out.WriteLine($"{sp.name}: x{scaleA / sp.scale:F2} distance {a.distance:F4} height {a.height:F4} steps {a.steps}; " +
                                      $"x{scaleB / sp.scale:F2} (as x{assumedB / sp.scale:F2}) distance {b.distance:F4} height {b.height:F4} steps {b.steps}");
            string d = Ratio("distance / size", a.distance, b.distance), h = Ratio("head height / size", a.height, b.height);
            if (d != null) return d;
            if (h != null) return h;
            if (Mathf.Abs(a.steps - b.steps) > Mathf.Max(1f, 0.01f * Mathf.Max(a.steps, b.steps))) return $"steps {a.steps} vs {b.steps}: more than 1 % (or one step) apart";
            return null;
        }

        static string Ratio(string what, float a, float b)
        {
            float r = Mathf.Abs(a) > 1e-6f ? b / a : (Mathf.Abs(b) > 1e-6f ? float.PositiveInfinity : 1f);
            return Mathf.Abs(r - 1f) <= 0.01f ? null : $"{what}: {a:F4} vs {b:F4} (ratio {r:F4}, more than 1 % apart)";
        }

        // ------------------------------------------------------------ 10./11. GC and time

        static object sink;

        // Allocations of 300 frames (brain + tick + bone write) after 120 frames of warm-up. allocate = the RED
        // control: one deliberate allocation per frame.
        internal static int TickAllocs(CreatureSpecies sp, bool allocate)
        {
            EnclosureArea area = LabArea(sp);
            var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale, 1) { Bounds = area };
            ICreatureBrain brain = sp.CanFly ? new FlightBrain(area, 1) : (ICreatureBrain)new WanderBrain(area, 1);
            var bones = new Matrix4x4[sp.BoneCount];
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, brain.Target(sim, kDt)); sim.WriteBoneWorld(bones, 0); }
            return GcAllocs.Count(() =>
            {
                for (int t = 0; t < 300; t++)
                {
                    sim.Tick(kDt, brain.Target(sim, kDt));
                    sim.WriteBoneWorld(bones, 0);
                    if (allocate) sink = new float[4];
                }
            });
        }

        internal static double TickMedianMs(CreatureSpecies sp)
        {
            var sim = Flat(sp, sp.scale, 1);
            var bones = new Matrix4x4[sp.BoneCount];
            for (int t = 0; t < 120; t++) { sim.Tick(kDt, Weave(sim, t)); sim.WriteBoneWorld(bones, 0); }
            var ms = new double[600];
            for (int t = 0; t < ms.Length; t++)
            {
                long t0 = Stopwatch.GetTimestamp();
                sim.Tick(kDt, Weave(sim, t));
                sim.WriteBoneWorld(bones, 0);
                ms[t] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            }
            Array.Sort(ms);
            return ms[ms.Length / 2];
        }

        // ------------------------------------------------------------ 12. lab

        internal static string LabProblem(CreatureSpecies sp)
        {
            CreatureKitAssets.Habitat h = CreatureKitAssets.HabitatOf(sp);
            TestContext.Out.WriteLine($"{sp.name}: habitat {h}");
            if (h == CreatureKitAssets.Habitat.Grove) return null;   // OctopusLab's tree grove places it (roster: T3.5)
            float big = CreatureLab.MaxScale(sp);
            EnclosureArea area = LabArea(sp);
            if (h == CreatureKitAssets.Habitat.Aviary)
                return FlightBrain.FitsAviary(sp, area, big) ? null : $"its cruise turn or minimum altitude does not fit the aviary (radius {area.radius:F1} m, roof {area.maxHeight:F1} m)";
            // room to turn: the safe disc (where the head may go) is at least a body length across
            float need = 0.5f * sp.BodyLength * big;
            return area.safeRadius >= need ? null : $"safe radius {area.safeRadius:F2} m < half a body length {need:F2} m in a {area.radius:F2} m enclosure";
        }
    }
}
