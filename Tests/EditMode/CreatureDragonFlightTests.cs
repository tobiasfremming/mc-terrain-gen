using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The SkyDragon's wingbeat (user feedback 2026-10-19: "make the wings also have the same changes that the
    // wyvern had, like how far down they extend, the bobbing"): level flight in the full sim (FlightModel +
    // CreatureWings + the twisting AirChain), measured like the wyvern's Flight_DeepDownstroke and
    // AirChain_FollowsShoulderBob, in the dragon's units (lengths / s, times / sqrt s).
    public class CreatureDragonFlightTests
    {
        const float kDt = 1f / 60f;
        const float kFrom = 6f, kTo = 16f;     // s at scale 1 (x sqrt s): after the launch transient

        internal struct Level
        {
            public float wristDrop, strokeDeg, belowGirdle, girdleBob, midBob, tailBob, headBob, modelBob;
            public float altMean, altHalf, beatHz, midLag;
            public int beats, deepBeats;
        }

        static float PeakToPeak(List<float> a) { float lo = float.MaxValue, hi = float.MinValue; foreach (float x in a) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); } return hi - lo; }

        // high-pass (a moving average of one slow beat removed) so the altitude drift does not count as bob
        static List<float> HighPass(List<float> a, int w)
        {
            var o = new List<float>();
            for (int i = 0; i < a.Count; i++)
            {
                float s = 0f; int c = 0;
                for (int k = i - w / 2; k <= i + w / 2; k++) if (k >= 0 && k < a.Count) { s += a[k]; c++; }
                o.Add(a[i] - s / c);
            }
            return o;
        }

        static float Lag(List<float> a, List<float> b, int maxLag, float h)
        {
            int best = 0; float bestC = float.MinValue;
            for (int l = 0; l <= maxLag; l++)
            {
                float c = 0f;
                for (int i = 0; i + l < a.Count; i++) c += a[i] * b[i + l];
                if (c > bestC) { bestC = c; best = l; }
            }
            return best * h;
        }

        // Level flight at 6 x s, swimming off (its vertical S would count as bob), manoeuvres off.
        internal static Level FlyLevel(float sizeFactor = 1f, bool swim = false)
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            AirTwistSpec keep = sp.flight.twist;
            try
            {
                AirTwistSpec t = keep;
                if (!swim) t.swimAmplitude = 0f;
                t.intervalMin = t.intervalMax = 1e6f;
                sp.flight.twist = t;
                return Fly(sp, sp.scale * sizeFactor);
            }
            finally { sp.flight.twist = keep; }
        }

        static Level Fly(CreatureSpecies sp, float s)
        {
            float alt = 6f * s, T = Mathf.Sqrt(s);
            var sim = CreatureDragonTwistTests.AirborneDragon(sp, s, alt, out _);
            CreatureWings wings = sim.Flight.Wings;
            int g = sim.Flight.Girdle, n = sp.JointCount, mid = (g + n) / 2, tail = n - 2;
            var L = new Level { wristDrop = 0f };
            var bones = new Matrix4x4[sp.BoneCount];
            float lo = float.MaxValue, angLo = float.MaxValue, belly = float.MaxValue, beatLo = float.MaxValue;
            var gy = new List<float>(); var my = new List<float>(); var ty = new List<float>(); var hy = new List<float>(); var py = new List<float>();
            float altSum = 0f, altLo = float.MaxValue, altHi = float.MinValue; int altN = 0;
            int beats0 = -1, last = -1, deep = 0;
            int ticks = Mathf.RoundToInt(kTo * T / kDt), from = Mathf.RoundToInt(kFrom * T / kDt);
            for (int k = 0; k < ticks; k++)
            {
                sim.Flight.SetGoals(alt, sp.flight.cruiseSpeed * T, 0f);
                sim.Tick(kDt, sim.Joints[0]);
                if (k < from) continue;
                sim.WriteBoneWorld(bones, 0);   // poses the wings (CreatureFlight.WriteBones)
                Assert.AreEqual(FlightMode.Air, sim.Mode, "stays in the air");
                ref WingState st = ref wings.State[0];
                Vector3 up = sim.Rotations[g] * Vector3.up;
                float hgt = Vector3.Dot(st.wrist - st.shoulder, up) - wings.WristHeight(0, 0f);
                lo = Mathf.Min(lo, hgt); angLo = Mathf.Min(angLo, st.angle);
                belly = Mathf.Min(belly, Vector3.Dot(st.wrist - sim.Joints[g], up));
                if (beats0 < 0) { beats0 = st.beats; last = st.beats; }
                beatLo = Mathf.Min(beatLo, hgt);
                if (st.beats != last) { if (beatLo < -0.75f * 0.68f * s) deep++; beatLo = float.MaxValue; last = st.beats; }
                gy.Add(sim.Joints[g].y); my.Add(sim.Joints[mid].y); ty.Add(sim.Joints[tail].y); hy.Add(sim.Joints[0].y);
                py.Add(sim.Flight.Model.Position.y);
                float a = sim.Flight.Model.Altitude;
                altSum += a; altN++; altLo = Mathf.Min(altLo, a); altHi = Mathf.Max(altHi, a);
            }
            L.beats = last - beats0; L.deepBeats = deep;
            L.wristDrop = -lo / s; L.strokeDeg = -angLo * Mathf.Rad2Deg; L.belowGirdle = -belly / s;
            int w = Mathf.RoundToInt(1.0f * T / kDt);
            var G = HighPass(gy, w); var M = HighPass(my, w);
            L.girdleBob = PeakToPeak(G) / s; L.midBob = PeakToPeak(M) / s;
            L.tailBob = PeakToPeak(HighPass(ty, w)) / s; L.headBob = PeakToPeak(HighPass(hy, w)) / s;
            L.modelBob = PeakToPeak(HighPass(py, w)) / s;
            L.midLag = Lag(G, M, Mathf.RoundToInt(1.5f * T / kDt), kDt) / T;
            L.altMean = altSum / altN / s; L.altHalf = 0.5f * (altHi - altLo) / s;
            L.beatHz = L.beats / ((kTo - kFrom) * T) * T;
            TestContext.WriteLine($"x{s / sp.scale:F2}: wrist {L.wristDrop:F3} s below its glide height (stroke {L.strokeDeg:F0} deg), {L.belowGirdle:F3} s below the girdle; " +
                                  $"{deep} of {L.beats} beats deep; {L.beatHz:F2} Hz (scale-1 units); altitude {L.altMean:F3} +- {L.altHalf:F3} s; " +
                                  $"bob p-p / s: model {L.modelBob:F3}, girdle {L.girdleBob:F3}, mid {L.midBob:F3} (lag {L.midLag:F3}), tail {L.tailBob:F3}, head {L.headBob:F3}");
            return L;
        }

        // The beat's bob shows on the body (the wyvern's AirChain_FollowsShoulderBob, on the twisting chain). Before
        // AirTwistSpec.girdleBob the body followed the head's stabilised path only: girdle, mid-body, tail and head all
        // rose and fell together by 0.05 x s (the altitude drift), while the flight model bobbed 0.32 x s. Now the
        // girdle rides 0.8 of it, the neck blends to 0.6 at the head, and behind the girdle it travels down the body
        // along the path (mid-body later), fading to 0.4 at the tail.
        // User feedback 2026-10-19, "it needs to bob way more when flying": the girdle now heaves 3x the flight model's
        // beat bob (girdleBob 3, its slow part taken out first: bobHighPass 0.6), half of it in phase down the trunk
        // (bobInPhase 0.5: at the faster beat the wave alone is too short and steep for the bend cones), half travelling;
        // the head blends to 0.45. Girdle p-p 0.221 x s before -> ~0.455 x s (x2.06; 1.46 m at full size); the model's own
        // bob shrank with the faster beat (0.316 -> 0.157 x s), so the girdle now rides well above it (bound changed
        // deliberately: it was "no more than the flight model").
        [TestCase(1f)]
        [TestCase(0.94f)]
        [TestCase(1.06f)]
        public void Flight_GirdleBob_TravelsDownTheBody(float sizeFactor)
        {
            Level L = FlyLevel(sizeFactor);
            Assert.Greater(L.girdleBob, 0.42f, "the girdle bobs way more than before (x size; 0.221 in dragon v2, 0.05 before the bob: only the drift)");
            Assert.Less(L.girdleBob, 0.6f, "but stays a heave, not a fling (x size)");
            Assert.Greater(L.girdleBob, 2f * L.modelBob, "a stylised beat: the body heaves more than the point mass");
            Assert.Greater(L.midBob, 0.4f * L.girdleBob, "mid-body follows the bob");
            Assert.Less(L.midBob, 1.15f * L.girdleBob, "no more than the shoulders");
            Assert.Less(L.tailBob, L.midBob, "the tail bobs least");
            Assert.Less(L.headBob, L.girdleBob, "the head is stabilised");
            Assert.That(L.midLag, Is.InRange(0.1f, 1.0f), "the wave travels down the body (s at scale 1)");
        }

        // The wyvern's deep downstroke (WingedSerpentFlightTests.Flight_DeepDownstroke: wrist ~0.96 of its 1.0 m reach,
        // ~70 deg, every beat deep) on the dragon: comfortDown 0.48 -> 0.70 of a 0.662 m reach (the band bottom
        // nearly straight down, as the wyvern's 1.03 of 1.0), amplitudeMin 0.3 -> 0.75; the deeper beat lifts more, so
        // kGrip 0.733 -> 0.4 (the dragon's three-times membrane grips harder per metre of wing), strokeRateMin 2 -> 3,
        // climbIntegralLimit 0.3 -> 0.6. Before: wrist 0.24 x s (22 deg) at 1.3 Hz (scale 1); after ~0.66 x s (74 deg).
        // User feedback 2026-10-19, "flap its wings with an even higher frequency": 1.5 -> 2.1 Hz at scale 1 (0.84 -> 1.17 Hz
        // in the world at 3.2) with the same deep stroke: upstrokeDuration 0.22 -> 0.15, strokeRateMin/Max 3/8 -> 5/13 rad/s,
        // and kGrip 0.4 -> 0.26 (each faster downstroke grips less, so the beat stays fast at cruise instead of the
        // controller slowing it back down). Beat bound 1.1..2.0 -> 1.9..2.6 Hz (changed deliberately).
        [TestCase(1f)]
        [TestCase(0.94f)]
        [TestCase(1.06f)]
        public void Flight_DeepDownstroke_HoldsAltitude(float sizeFactor)
        {
            Level L = FlyLevel(sizeFactor);
            Assert.Greater(L.wristDrop, 0.6f, "the wrist sweeps far down (x size; 0.24 before)");
            Assert.Greater(L.strokeDeg, 65f, "a deep stroke angle (22 deg before)");
            Assert.Greater(L.belowGirdle, 0.5f, "well below the belly");
            Assert.GreaterOrEqual(L.deepBeats, L.beats - 1, "every beat is deep");
            Assert.Greater(L.beats, 6);
            Assert.That(L.beatHz, Is.InRange(1.9f, 2.6f), "the faster beat, still deep (Hz at scale 1; / sqrt(size) in the world)");
            Assert.AreEqual(6f, L.altMean, 0.05f, "altitude held (x size)");
            Assert.Less(L.altHalf, 0.3f, "altitude wobble, the bob included (x size)");
        }

        // The faster, lighter-gripping beat still turns: a coordinated banked turn at 0.5 rad/s (scale 1, / sqrt(size) in
        // the world) for 10 s holds the altitude and the speed and comes round at about the asked rate, at 0.94x and 1.06x
        // the same in scale-1 units (Froude).
        [TestCase(0.94f)]
        [TestCase(1.06f)]
        public void Flight_BankedTurn_HoldsAltitude(float sizeFactor)
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            AirTwistSpec keep = sp.flight.twist;
            try
            {
                AirTwistSpec t = keep; t.intervalMin = t.intervalMax = 1e6f;
                sp.flight.twist = t;
                float s = sp.scale * sizeFactor, T = Mathf.Sqrt(s), alt = 6f * s, rate = 0.5f / T;
                var sim = CreatureDragonTwistTests.AirborneDragon(sp, s, alt, out _);
                FlightModel m = sim.Flight.Model;
                float turned = 0f, altLo = float.MaxValue, altHi = float.MinValue, spd = 0f, bank = 0f;
                Vector3 prev = m.Forward;
                int ticks = Mathf.RoundToInt(10f * T / kDt), settle = Mathf.RoundToInt(2f * T / kDt), n = 0;
                for (int k = 0; k < ticks; k++)
                {
                    sim.Flight.SetGoals(alt, sp.flight.cruiseSpeed * T, rate);
                    sim.Tick(kDt, sim.Joints[0]);
                    Assert.AreEqual(FlightMode.Air, sim.Mode, "stays in the air");
                    if (k < settle) { prev = m.Forward; continue; }
                    turned += Vector3.SignedAngle(prev, m.Forward, Vector3.up) * Mathf.Deg2Rad; prev = m.Forward;
                    altLo = Mathf.Min(altLo, m.Altitude / s); altHi = Mathf.Max(altHi, m.Altitude / s);
                    spd += m.HorizontalSpeed / T; bank = Mathf.Max(bank, Mathf.Abs(m.Bank) * Mathf.Rad2Deg); n++;
                }
                float got = turned / (n * kDt) * T;
                TestContext.WriteLine($"x{sizeFactor}: turn {got:F3} rad/s (scale 1; asked 0.5), altitude {altLo:F3}..{altHi:F3} x s, speed {spd / n:F2} (scale 1), bank up to {bank:F0} deg");
                Assert.AreEqual(0.5f, got, 0.1f, "turns at the asked rate");
                Assert.Greater(altLo, 5.5f, "holds its altitude in the turn (x size)");
                Assert.Less(altHi, 6.5f, "holds its altitude in the turn (x size)");
                Assert.AreEqual(sp.flight.cruiseSpeed, spd / n, 0.4f, "holds its speed (scale 1)");
            }
            finally { sp.flight.twist = keep; }
        }

        // ------------------------------------------------------------------ the bat wing

        // "The wings can actually be more bat-like" (user feedback 2026-10-19): four long fingers per wing (WingSpec 3..5
        // fingers), the membrane between every pair of neighbouring fingers (a zone skinned to both), a deeply scalloped
        // trailing edge, and the membrane on to the flank (the flank patches). Every wing membrane vertex is accounted for.
        [Test] public void Wing_BatLike_FourFingers_MembraneBetween()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            var uv1 = new List<Vector2>(); var uv2 = new List<Vector4>();
            mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
            Object.DestroyImmediate(mesh);
            int expect = 0, wingVerts = 0;
            for (int w = 0; w < sp.WingCount; w++)
            {
                WingSpec ws = sp.wings[w];
                int F = ws.FingerCount;
                Assert.GreaterOrEqual(F, 4, "a bat's hand: four or more fingers");
                Assert.GreaterOrEqual(ws.scallop, 0.25f, "a deeply scalloped trailing edge");
                Assert.Greater(WingBoneLayout.FlankPatchCount(ws), 1, "the membrane follows the flank");
                expect += WingedMeshBuilder.MembraneVertices(ws);
                for (int k = 0; k + 1 < F; k++)
                {
                    int a = sp.WingBone(w, k), b = sp.WingBone(w, k + 1), between = 0;
                    for (int i = 0; i < uv2.Count; i++)
                    {
                        if (Mathf.RoundToInt(uv1[i].x) != WingedMeshBuilder.PartMembrane) continue;
                        Vector4 s = uv2[i];
                        bool pair = ((int)s.x == a && (int)s.y == b) || ((int)s.x == b && (int)s.y == a);
                        if (pair && s.z > 0.05f && s.w > 0.05f) between++;
                    }
                    Assert.Greater(between, 0, $"wing {w}: membrane between fingers {k} and {k + 1}");
                }
            }
            // (the whiskers are membrane-shaded too: only the wing bones count)
            int lo = sp.WingBone(0, 0), hi = sp.WingBone(sp.WingCount - 1, WingBoneLayout.Bones(sp.wings[sp.WingCount - 1]) - 1);
            for (int i = 0; i < uv2.Count; i++)
                if (Mathf.RoundToInt(uv1[i].x) == WingedMeshBuilder.PartMembrane && uv2[i].x >= lo && uv2[i].x <= hi) wingVerts++;
            TestContext.WriteLine($"{sp.wings[0].FingerCount} fingers, {wingVerts} wing membrane vertices");
            Assert.AreEqual(expect, wingVerts, "every finger zone and flank patch, two sheets");
        }

        // WingSpec takes 3..5 fingers (one entry each in the three finger arrays) and the bone layout follows the count.
        [Test] public void WingSpec_FingerCount_ThreeToFive()
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            WingSpec ws = CreatureKit.KitTypes.Memberwise(sp.wings[0]);
            float[] L(int n) { var a = new float[n]; for (int k = 0; k < n; k++) a[k] = 0.4f; return a; }
            float[] D(int n, float from) { var a = new float[n]; for (int k = 0; k < n; k++) a[k] = from + 20f * k; return a; }
            for (int n = 2; n <= 6; n++)
            {
                ws.fingerLengths = L(n); ws.fingerSpreadDeg = D(n, -10f); ws.fingerFoldDeg = D(n, 100f);
                string err = ws.Validate(sp);
                if (n >= WingSpec.MinFingers && n <= WingSpec.MaxFingers) Assert.IsNull(err, $"{n} fingers");
                else Assert.IsNotNull(err, $"{n} fingers rejected");
                Assert.AreEqual(n + WingSpec.Ribs, WingBoneLayout.HumerusPatchOf(ws), "the bones after the fingers");
            }
            ws.fingerLengths = L(4); ws.fingerSpreadDeg = D(3, -10f); ws.fingerFoldDeg = D(4, 100f);
            Assert.IsNotNull(ws.Validate(sp), "one spread angle per finger");
        }

        // ------------------------------------------------------------------ the skinned mesh in flight

        // The species mesh skinned like the renderer (two bones per vertex), checked for triangles that flip against
        // their rest face turned by their heavier bone (KitChecks.MaxBendProblem's test) - in the air.
        internal sealed class Skinned
        {
            // Limb tubes (part 1) are counted, not failed: the tucked hind legs (CreatureFlight's generic tuck, straight
            // back along the belly) fold two triangles of each hind shoulder ring, flush with the flank, in every air
            // frame - pre-existing (2026-10-19: identical on the dragon before the deep beat), a finding for the tuck.
            public const int PartLeg = 1;
            readonly Vector3[] v, pos; readonly int[] tri; readonly List<Vector2> uv1 = new List<Vector2>(); readonly List<Vector4> uv2 = new List<Vector4>();
            readonly Matrix4x4[] bind, world, skin;
            public readonly Dictionary<int, int> InvertedByPart = new Dictionary<int, int>();
            public string First;
            public int Frames, Triangles;
            public Skinned(CreatureSpecies sp)
            {
                Mesh mesh = CreatureKitAssets.BuildMesh(sp);
                v = mesh.vertices; tri = mesh.triangles;
                mesh.GetUVs(1, uv1); mesh.GetUVs(2, uv2);
                Object.DestroyImmediate(mesh);
                bind = CreatureRestPose.BindPoses(sp);
                world = new Matrix4x4[sp.BoneCount]; skin = new Matrix4x4[sp.BoneCount];
                pos = new Vector3[v.Length];
                Triangles = tri.Length / 3;
            }
            public void Check(CreatureSim sim, string when)
            {
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
                    if (rest.sqrMagnitude < 1e-16f) continue;   // a pole fan's degenerate rest triangle
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    if (!(Vector3.Dot(now, want) > 0f))
                    {
                        int part = Mathf.RoundToInt(uv1[i0].x);
                        InvertedByPart.TryGetValue(part, out int c); InvertedByPart[part] = c + 1;
                        if (First == null && part != PartLeg) First = $"{when}: triangle {k / 3} (part {part}, bones {uv2[i0]}) inverted";
                    }
                }
                Frames++;
            }
            public string Report() => $"{Frames} frames x {Triangles} triangles; inverted by part: " +
                                      (InvertedByPart.Count == 0 ? "none" : string.Join(", ", new List<string>(Map())));
            IEnumerable<string> Map() { foreach (var kv in InvertedByPart) yield return $"{kv.Key}: {kv.Value}"; }
        }

        // Deep beats, then every manoeuvre (corkscrew, barrel roll, loop, slow roll), at the smallest and largest
        // individual: no skinned triangle of the body, head, fins, antennae or wings inverts.
        [TestCase(0.94f)]
        [TestCase(1.06f)]
        public void Flight_Manoeuvres_NoInvertedTriangles(float sizeFactor)
        {
            CreatureSpecies sp = CreatureDragonTwistTests.Dragon();
            float s = sp.scale * sizeFactor, T = Mathf.Sqrt(s), alt = 6f * s;
            var sk = new Skinned(sp);
            AirTwistSpec keep = sp.flight.twist;
            try
            {
                AirTwistSpec t = keep; t.intervalMin = t.intervalMax = 1e6f;   // only the manoeuvres begun here
                sp.flight.twist = t;
                var sim = CreatureDragonTwistTests.AirborneDragon(sp, s, alt, out _);
                void Fly(float seconds, string what)
                {
                    int ticks = Mathf.CeilToInt(seconds / kDt);
                    for (int k = 0; k < ticks; k++)
                    {
                        sim.Flight.SetGoals(alt, sp.flight.cruiseSpeed * T, 0f);
                        sim.Tick(kDt, sim.Joints[0]);
                        if (k % 3 == 2) sk.Check(sim, $"{what} t={k * kDt:F2}");
                    }
                }
                Fly(4f * T, "level");
                foreach (TwistManoeuvre m in new[] { TwistManoeuvre.Corkscrew, TwistManoeuvre.BarrelRoll, TwistManoeuvre.Loop, TwistManoeuvre.SlowRoll })
                {
                    AirTwist tw = sim.Flight.Twist;
                    Assert.IsTrue(tw.Begin(m, sim.Flight.Model.HorizontalSpeed, sim.Flight.Model.Forward, Vector3.up), $"{m} begins");
                    Fly(tw.Duration(m, sim.Flight.Model.HorizontalSpeed) + 1.5f * T, m.ToString());
                }
            }
            finally { sp.flight.twist = keep; }
            TestContext.WriteLine($"x{sizeFactor}: {sk.Report()}");
            Assert.IsNull(sk.First, sk.Report());
            sk.InvertedByPart.TryGetValue(Skinned.PartLeg, out int legs);
            Assert.LessOrEqual(legs, 2 * sk.Frames, "the tucked hind legs fold no more than before (2 shoulder triangles a frame, pre-existing)");
        }
    }
}
