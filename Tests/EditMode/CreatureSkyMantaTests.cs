using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // The sky manta (SkyManta: RayBody, RayWing x2, CephalicLobe x2, WhipTail, Soar, School) and its parts.
    //   shape       the ray body is the sim's spine chain; the wing's root column IS the body's edge (one surface:
    //               same points, same normals); thick at the root, thin at the tip; the rest mesh's faces wind out.
    //   chain rule  the beat travels out along the wing (phase lag grows root -> tip) and back along the chord (front ->
    //               back); the body heaves opposite the downstroke; the tail lags the body, more toward its tip.
    //   flight      altitude held in its band, flap bouts alternate with long glides, the glide ratio, rolls and loops
    //               recover upright with the body clear of the ground; no inverted triangle through flaps, turns, rolls
    //               and loops at 0.9x and 1.1x; Froude-similar flight.
    //   school      five mantas (size jitter) in the lab aviary for three minutes: never closer than a span, never in the
    //               ground, the roof or the wall - wing tips and tail included.
    // RED proofs: a rigid wing (no chain) has no span lag; a body without heave fails the heave test; a school without
    // separation collides.
    public class CreatureSkyMantaTests
    {
        const float kDt = 1f / 60f;

        static CreatureSpecies Manta() => CreatureKitAssets.Load("SkyManta");

        static CreatureSim Fly(CreatureSpecies sp, float s, int seed = 3, EnclosureArea? area = null)
            => new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, seed) { Bounds = area };

        static RayWingRuntime Wing(CreatureSim sim, int side = 1)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is RayWingRuntime w && w.Part.Side == side) return w;
            return null;
        }

        static WhipTailRuntime Tail(CreatureSim sim)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is WhipTailRuntime t) return t;
            return null;
        }

        static Vector3 Far(CreatureSim sim, float alt) => new Vector3(0f, alt * sim.SizeScale, 100000f * sim.SizeScale);

        // Phase (radians) of a signal against the stroke angle: x ~ A sin(theta + phi). Mean removed.
        static float Phase(List<float> x, List<float> th)
        {
            float mean = 0f; foreach (float v in x) mean += v; mean /= Mathf.Max(1, x.Count);
            float s = 0f, c = 0f;
            for (int i = 0; i < x.Count; i++) { s += (x[i] - mean) * Mathf.Sin(th[i]); c += (x[i] - mean) * Mathf.Cos(th[i]); }
            return Mathf.Atan2(c, s);
        }

        static float Amplitude(List<float> x)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float v in x) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            return 0.5f * (hi - lo);
        }

        // lag of b behind a, degrees, wrapped to (-180, 180]
        static float Lag(float phiA, float phiB) => Mathf.DeltaAngle(phiB * Mathf.Rad2Deg, phiA * Mathf.Rad2Deg);

        // ------------------------------------------------------------ shape

        [Test] public void Body_RayBody_IsTheSpineChain()
        {
            CreatureSpecies sp = Manta();
            Assert.AreEqual(8, sp.JointCount, "the ray body's joints are the sim's");
            Assert.AreEqual(0.14f, sp.spacing, 1e-6f);
            Assert.AreEqual(0.24f, sp.joints[3].halfWidth, 1e-6f);
            Assert.AreEqual(0, sp.RigidHead, "a ray's head is part of the disc");
            Assert.AreEqual(CreatureKit.Habitat.Aviary, LabHabitat.For(sp), "Soar flies: an aviary");
            Assert.IsFalse(sp.CanFly, "no legacy wings");
        }

        [Test] public void Mesh_WingRoot_IsTheBodyEdge()
        {
            CreatureSpecies sp = Manta();
            RayShape shape = RayShape.For(KitView.Parts(sp));
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = mesh.vertices, n = mesh.normals;
            var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);
            Vector3 j0 = CreatureRestPose.Sim(sp).Joints[0];
            int edgeBody = 0, matched = 0;
            float worstP = 0f, worstN = 0f;
            for (int l = 0; l < shape.LoopCount; l++)
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p = shape.LoopPoint(side * shape.HalfWidth, l) + j0;
                    int hits = 0;
                    for (int i = 0; i < v.Length; i++)
                    {
                        if ((v[i] - p).sqrMagnitude > 1e-10f) continue;
                        hits++;
                        worstP = Mathf.Max(worstP, (v[i] - p).magnitude);
                        Vector3 want = shape.LoopNormal(side * shape.HalfWidth, l);
                        worstN = Mathf.Max(worstN, (n[i] - want).magnitude);
                    }
                    edgeBody++;
                    if (hits >= 2) matched++;
                }
            TestContext.WriteLine($"edge points {edgeBody}, found on both body and wing {matched}; worst normal gap {worstN:E2}");
            Assert.AreEqual(edgeBody, matched, "every body edge point is also a wing root point (one surface)");
            Assert.Less(worstN, 1e-4f, "the same normals on both sides of the seam");
            Object.DestroyImmediate(mesh);
        }

        [Test] public void Mesh_Rest_FacesWindOutward_NormalsUnit()
        {
            CreatureSpecies sp = Manta();
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = mesh.vertices, n = mesh.normals; int[] t = mesh.triangles;
            var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);
            int bad = 0, checkedTris = 0;
            for (int k = 0; k < t.Length; k += 3)
            {
                if (Mathf.RoundToInt(uv1[t[k]].x) != RayBody.PartRay) continue;
                Vector3 face = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
                if (face.sqrMagnitude < 1e-14f) continue;
                checkedTris++;
                Vector3 mean = n[t[k]] + n[t[k + 1]] + n[t[k + 2]];
                if (Vector3.Dot(face, mean) <= 0f) bad++;
            }
            for (int i = 0; i < n.Length; i++) Assert.AreEqual(1f, n[i].magnitude, 1e-3f, $"vertex {i} normal");
            TestContext.WriteLine($"{checkedTris} ray-skin triangles, {bad} wound against their normals");
            Assert.AreEqual(0, bad);
            Object.DestroyImmediate(mesh);
        }

        [Test] public void Wing_ThickAtTheRoot_ThinAtTheTip()
        {
            RayShape shape = RayShape.For(KitView.Parts(Manta()));
            float prev = float.MaxValue;
            for (int i = 0; i <= 20; i++)
            {
                float x = shape.HalfWidth + shape.Span * i / 20f;
                float t = shape.Thickness(x);
                Assert.LessOrEqual(t, prev + 1e-6f, $"thinner outward at {x:F2} m");
                prev = t;
            }
            Assert.GreaterOrEqual(shape.Thickness(shape.HalfWidth), 0.12f, "a thick root (a thick wing, not a membrane)");
            Assert.GreaterOrEqual(shape.Thickness(shape.HalfWidth + 0.5f * shape.Span), 0.03f, "still thick half way out");
            Assert.LessOrEqual(shape.Thickness(shape.HalfSpan), 0.01f, "a thin tip");
            Assert.Greater(shape.SectionThickness(shape.HalfWidth, 0.3f), shape.SectionThickness(shape.HalfWidth, 0.9f), "thickest a third back from the leading edge");
        }

        // ------------------------------------------------------------ chain rule

        // Lifts of every station's leading edge and of every chord row at mid span, over steady beats.
        static void Beat(CreatureSim sim, out List<float> theta, out List<float>[] span, out List<float>[] chord, out List<float> dihedral, int beats = 4)
        {
            SoarFlight soar = sim.LocomotionMode<SoarFlight>();
            RayWingRuntime w = Wing(sim);
            soar.Sustain = true;
            Vector3 far = Far(sim, 7f);
            for (int k = 0; k < 60 * 12; k++) sim.Tick(kDt, far);   // into a steady beat
            theta = new List<float>(); dihedral = new List<float>();
            span = new List<float>[w.Stations + 1];
            chord = new List<float>[w.Rows];
            for (int k = 1; k <= w.Stations; k++) span[k] = new List<float>();
            for (int r = 0; r < w.Rows; r++) chord[r] = new List<float>();
            int mid = w.Stations / 2 + 1;
            int ticks = Mathf.RoundToInt(beats / soar.StrokeHz / kDt);
            for (int k = 0; k < ticks; k++)
            {
                sim.Tick(kDt, far);
                theta.Add(soar.StrokePhase);
                dihedral.Add(w.Dihedral(1));
                for (int s = 1; s <= w.Stations; s++) span[s].Add(w.NodeLift(s, 0));
                for (int r = 0; r < w.Rows; r++) chord[r].Add(w.NodeLift(mid, r));
            }
        }

        static float[] SpanLags(CreatureSim sim, out float[] amp)
        {
            Beat(sim, out List<float> th, out List<float>[] span, out _, out _);
            int K = span.Length - 1;
            var lag = new float[K + 1]; amp = new float[K + 1];
            float p1 = Phase(span[1], th);
            for (int k = 1; k <= K; k++) { lag[k] = Lag(p1, Phase(span[k], th)); amp[k] = Amplitude(span[k]); }
            return lag;
        }

        [TestCase(0.9f)]
        [TestCase(1.1f)]
        public void Chain_SpanLag_GrowsRootToTip(float s)
        {
            CreatureSim sim = Fly(Manta(), s);
            float[] lag = SpanLags(sim, out float[] amp);
            TestContext.WriteLine("span lag (deg): " + string.Join(", ", System.Array.ConvertAll(lag, x => x.ToString("F1"))));
            TestContext.WriteLine("span lift amplitude (m): " + string.Join(", ", System.Array.ConvertAll(amp, x => x.ToString("F3"))));
            for (int k = 2; k < lag.Length; k++) Assert.Greater(lag[k], lag[k - 1] + 1f, $"station {k} lags station {k - 1}");
            Assert.Greater(lag[lag.Length - 1], 25f, "the tip lags the root by a good part of a beat (the travelling wave)");
            Assert.Less(lag[lag.Length - 1], 150f, "but stays a wave along one wing, not a counter-beat");
            for (int k = 2; k < amp.Length; k++) Assert.Greater(amp[k], amp[k - 1], "the beat grows out along the wing");
        }

        [Test] public void Chain_ChordLag_GrowsFrontToBack()
        {
            CreatureSim sim = Fly(Manta(), 1f);
            Beat(sim, out List<float> th, out _, out List<float>[] chord, out _);
            var lag = new float[chord.Length];
            float p0 = Phase(chord[0], th);
            for (int r = 0; r < chord.Length; r++) lag[r] = Lag(p0, Phase(chord[r], th));
            TestContext.WriteLine("chord lag (deg): " + string.Join(", ", System.Array.ConvertAll(lag, x => x.ToString("F1"))));
            for (int r = 1; r < lag.Length; r++) Assert.Greater(lag[r], lag[r - 1] + 0.5f, $"row {r} lags row {r - 1}");
            Assert.Greater(lag[lag.Length - 1], 8f, "the trailing edge visibly behind the leading edge");
        }

        // RED: a wing whose segments follow instantly (a rigid plate on a hinge) has no travelling wave.
        [Test] public void Red_Chain_StiffWing_HasNoSpanLag()
        {
            CreatureSpecies sp = Manta();
            var wings = new List<RayWing>();
            foreach (CreaturePart p in KitView.Parts(sp)) if (p is RayWing w) wings.Add(w);
            float keep = wings[0].spanHz;
            try
            {
                foreach (RayWing w in wings) w.spanHz = 400f;
                float[] lag = SpanLags(Fly(sp, 1f), out _);
                TestContext.WriteLine($"RED: stiff wing tip lag {lag[lag.Length - 1]:F2} deg");
                Assert.Less(Mathf.Abs(lag[lag.Length - 1]), 5f, "a stiff wing must not show the wave the lag test asks for");
            }
            finally { foreach (RayWing w in wings) w.spanHz = keep; }
        }

        // The body heaves opposite the stroke: up while the wings beat down. Body vertical velocity (the attach joint,
        // detrended over a beat) against the root's dihedral rate.
        static float HeaveCorrelation(CreatureSim sim)
        {
            Beat(sim, out _, out _, out _, out _, 1);   // settle (Sustain on)
            SoarFlight soar = sim.LocomotionMode<SoarFlight>();
            RayWingRuntime w = Wing(sim);
            Vector3 far = Far(sim, 7f);
            var body = new List<float>(); var root = new List<float>();
            float prevY = sim.Joints[3].y, prevD = w.Dihedral(1);
            int per = Mathf.RoundToInt(1f / soar.StrokeHz / kDt);
            var ys = new List<float>();
            for (int k = 0; k < per * 6; k++)
            {
                sim.Tick(kDt, far);
                ys.Add(sim.Joints[3].y);
                root.Add((w.Dihedral(1) - prevD) / kDt); prevD = w.Dihedral(1);
            }
            // vertical velocity minus its running mean over one beat (the climb taken out)
            for (int k = 1; k < ys.Count; k++) body.Add((ys[k] - ys[k - 1]) / kDt);
            root.RemoveAt(0);
            var bd = new List<float>(); var rd = new List<float>();
            for (int k = per; k < body.Count; k++)
            {
                float m = 0f; for (int j = k - per; j < k; j++) m += body[j]; m /= per;
                bd.Add(body[k] - m); rd.Add(root[k]);
            }
            float sab = 0f, saa = 0f, sbb = 0f, ma = 0f, mb = 0f;
            for (int i = 0; i < bd.Count; i++) { ma += bd[i]; mb += rd[i]; }
            ma /= bd.Count; mb /= rd.Count;
            for (int i = 0; i < bd.Count; i++) { sab += (bd[i] - ma) * (rd[i] - mb); saa += (bd[i] - ma) * (bd[i] - ma); sbb += (rd[i] - mb) * (rd[i] - mb); }
            return sab / Mathf.Sqrt(Mathf.Max(1e-12f, saa * sbb));
        }

        [Test] public void Body_HeavesOpposite_TheDownstroke()
        {
            float c = HeaveCorrelation(Fly(Manta(), 1f));
            TestContext.WriteLine($"correlation(body vertical velocity, root dihedral rate) = {c:F2}");
            Assert.Less(c, -0.5f, "the body rises while the wings beat down");
        }

        [Test] public void Red_Heave_Off_FailsTheHeaveTest()
        {
            CreatureSpecies sp = Manta();
            Soar soar = null;
            foreach (CreatureModule m in KitView.Modules(sp)) if (m is Soar so) soar = so;
            float keep = soar.heave;
            try
            {
                soar.heave = 0f;
                float c = HeaveCorrelation(Fly(sp, 1f));
                TestContext.WriteLine($"RED: no heave, correlation {c:F2}");
                Assert.Greater(c, -0.5f, "without heave the body must not pass as heaving");
            }
            finally { soar.heave = keep; }
        }

        [Test] public void Tail_LagsTheBody_MoreTowardTheTip()
        {
            CreatureSim sim = Fly(Manta(), 1f);
            Beat(sim, out _, out _, out _, out _, 2);   // steady beats (Sustain on)
            SoarFlight soar = sim.LocomotionMode<SoarFlight>();
            WhipTailRuntime tail = Tail(sim);
            Vector3 far = Far(sim, 7f);
            int N = tail.Points.Length - 1;
            int[] at = { 0, N / 3, 2 * N / 3, N };
            var th = new List<float>();
            var abs = new List<float>[at.Length];
            for (int i = 0; i < at.Length; i++) abs[i] = new List<float>();
            int ticks = Mathf.RoundToInt(5f / soar.StrokeHz / kDt);
            for (int k = 0; k < ticks; k++)
            {
                sim.Tick(kDt, far);
                th.Add(soar.StrokePhase);
                for (int i = 0; i < at.Length; i++) abs[i].Add(tail.Points[at[i]].y);
            }
            float p0 = Phase(abs[0], th);
            float l1 = Lag(p0, Phase(abs[1], th)), l2 = Lag(p0, Phase(abs[2], th)), l3 = Lag(p0, Phase(abs[3], th));
            TestContext.WriteLine($"tail lag behind its root (the body's last joint): {l1:F1}, {l2:F1}, {l3:F1} deg; root heave {Amplitude(abs[0]) * 1000f:F0} mm");
            Assert.Greater(l1, 2f, "the tail's first third lags its root");
            Assert.Greater(l2, l1 + 2f, "more at two thirds");
            Assert.Greater(l3, l2 + 2f, "most at the tip");
        }

        // ------------------------------------------------------------ flight

        struct FlightLog { public float minAlt, maxAlt, flapFrac, glideRatio, meanGlide; public int bouts, glides; }

        static FlightLog Log(CreatureSim sim, float seconds, float settle = 25f)
        {
            SoarFlight soar = sim.LocomotionMode<SoarFlight>();
            Vector3 far = new Vector3(0f, 0f, 100000f * sim.SizeScale);   // a ground point: it cruises
            for (int k = 0; k < (int)(settle * sim.TimeScale / kDt); k++) sim.Tick(kDt, far);
            var log = new FlightLog { minAlt = float.MaxValue, maxAlt = float.MinValue };
            int flap = 0, n = (int)(seconds * sim.TimeScale / kDt), bouts0 = soar.Bouts;
            float glideDist = 0f, glideDrop = 0f, glideTime = 0f, cur = 0f;
            bool wasGliding = false;
            float sinceManoeuvre = 100f;
            Vector3 last = sim.Joints[0];
            for (int k = 0; k < n; k++)
            {
                sim.Tick(kDt, far);
                Vector3 h = sim.Joints[0];
                if (soar.Manoeuvre != SoarManoeuvre.None) sinceManoeuvre = 0f; else sinceManoeuvre += kDt;
                if (sinceManoeuvre > 3f * sim.TimeScale) { log.minAlt = Mathf.Min(log.minAlt, soar.Altitude); log.maxAlt = Mathf.Max(log.maxAlt, soar.Altitude); }
                bool gliding = !soar.Flapping && soar.Manoeuvre == SoarManoeuvre.None;
                if (soar.Flapping) flap++;
                if (gliding && wasGliding)
                {
                    Vector3 d = h - last;
                    glideDist += new Vector2(d.x, d.z).magnitude; glideDrop -= d.y; cur += kDt;
                }
                if (!gliding && wasGliding) { log.glides++; glideTime += cur; cur = 0f; }
                wasGliding = gliding; last = h;
            }
            log.bouts = soar.Bouts - bouts0;
            log.flapFrac = flap / (float)n;
            log.glideRatio = glideDrop > 1e-4f ? glideDist / glideDrop : float.PositiveInfinity;
            log.meanGlide = log.glides > 0 ? glideTime / log.glides / sim.TimeScale : 0f;
            return log;
        }

        [TestCase(0.9f)]
        [TestCase(1f)]
        [TestCase(1.1f)]
        public void Flight_HoldsAltitude_FlapsAndGlides(float s)
        {
            CreatureSpecies sp = Manta();
            Soar f = null; foreach (CreatureModule m in KitView.Modules(sp)) if (m is Soar so) f = so;
            FlightLog g = Log(Fly(sp, s), 120f);
            TestContext.WriteLine($"x{s}: altitude {g.minAlt / s:F2}..{g.maxAlt / s:F2} (x s), bouts {g.bouts}, flapping {g.flapFrac:P0}, glides {g.glides} of {g.meanGlide:F1} s, glide ratio {g.glideRatio:F1}");
            Assert.GreaterOrEqual(g.minAlt, (f.cruiseAltitude - f.altitudeBand - 0.6f) * s, "never sinks far under its band");
            Assert.LessOrEqual(g.maxAlt, (f.cruiseAltitude + 2f * f.altitudeBand + 0.6f) * s, "never climbs far over it");
            Assert.GreaterOrEqual(g.bouts, 6, "it keeps beating");
            Assert.That(g.flapFrac, Is.InRange(0.2f, 0.7f), "flap bouts alternate with glides");
            Assert.GreaterOrEqual(g.meanGlide, 3f, "long glides");
            Assert.That(g.glideRatio, Is.InRange(f.glideRatio * 0.75f, f.glideRatio * 1.25f), "the glide's slope is its glide ratio");
        }

        // Froude: the 0.9x and 1.1x individuals fly the same path in body lengths and Froude time.
        [Test] public void Flight_FroudeSimilar()
        {
            CreatureSpecies sp = Manta();
            Vector3 Run(float s)
            {
                CreatureSim sim = Fly(sp, s, 5);
                float dt = kDt * Mathf.Sqrt(s);
                Vector3 far = new Vector3(30f * s, 0f, 100000f * s);
                for (int k = 0; k < 60 * 40; k++) sim.Tick(dt, far);
                return sim.Joints[0] / s;
            }
            Vector3 a = Run(0.9f), b = Run(1.1f);
            TestContext.WriteLine($"x0.9 {a}, x1.1 {b}");
            Assert.Less((a - b).magnitude, 0.01f * a.magnitude, "the same flight within 1 %");
        }

        // Rolls and loops: they roll / loop through, then come back upright, the body clear of the ground.
        [TestCase(SoarManoeuvre.Barrel, 0.9f)]
        [TestCase(SoarManoeuvre.Barrel, 1.1f)]
        [TestCase(SoarManoeuvre.Loop, 0.9f)]
        [TestCase(SoarManoeuvre.Loop, 1.1f)]
        public void Manoeuvres_RollAndLoop_Recover(SoarManoeuvre kind, float s)
        {
            CreatureSim sim = Fly(Manta(), s);
            SoarFlight soar = sim.LocomotionMode<SoarFlight>();
            Vector3 far = Far(sim, 7f);
            for (int k = 0; k < 60 * 25; k++) sim.Tick(kDt, far);
            bool started = false;
            for (int k = 0; k < 60 * 20 && !started; k++) { sim.Tick(kDt, far); if (!soar.Flapping) started = soar.ForceManoeuvre(kind); }
            Assert.IsTrue(started, "the manoeuvre starts (room under and over it)");
            float minUp = 1f, minAlt = float.MaxValue;
            int ticks = 0;
            while (soar.Manoeuvre != SoarManoeuvre.None && ticks < 60 * 20)
            {
                sim.Tick(kDt, far); ticks++;
                minUp = Mathf.Min(minUp, Vector3.Dot(sim.Rotations[3] * Vector3.up, Vector3.up));
                minAlt = Mathf.Min(minAlt, LowestPoint(sim));
            }
            Assert.Less(ticks, 60 * 20, "it ends");
            Assert.Less(minUp, -0.8f, "upside down on the way round");
            for (int k = 0; k < 60; k++) { sim.Tick(kDt, far); minAlt = Mathf.Min(minAlt, LowestPoint(sim)); }
            float up = Vector3.Dot(sim.Rotations[3] * Vector3.up, Vector3.up);
            TestContext.WriteLine($"{kind} x{s}: {ticks * kDt:F1} s, most inverted {minUp:F2}, upright after {up:F3}, lowest point {minAlt:F2} m");
            Assert.Greater(up, 0.9f, "upright again a second later");
            Assert.Greater(minAlt, 0.5f * s, "wing tips and tail clear of the ground");
        }

        // The lowest of the body joints, the wing lattice and the tail.
        static float LowestPoint(CreatureSim sim)
        {
            float lo = float.MaxValue;
            foreach (Vector3 j in sim.Joints) lo = Mathf.Min(lo, j.y);
            foreach (IPartRuntime r in sim.PartRuntimes)
            {
                if (r is RayWingRuntime w)
                    for (int k = 1; k <= w.Stations; k++) for (int q = 0; q < w.Rows; q++) lo = Mathf.Min(lo, w.Node(k, q).y);
                if (r is WhipTailRuntime t) foreach (Vector3 p in t.Points) lo = Mathf.Min(lo, p.y);
            }
            return lo;
        }

        // No skinned triangle inverts through flaps, glides, turns, a barrel roll and a loop, at both sizes.
        [TestCase(0.9f)]
        [TestCase(1.1f)]
        public void Mesh_NoInvertedTriangles_FlapTurnRoll(float s)
        {
            CreatureSpecies sp = Manta();
            Mesh mesh = CreatureKitAssets.BuildMesh(sp);
            Vector3[] v = mesh.vertices; int[] tri = mesh.triangles;
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            Object.DestroyImmediate(mesh);
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var world = new Matrix4x4[sp.BoneCount]; var skin = new Matrix4x4[sp.BoneCount];
            var pos = new Vector3[v.Length];
            CreatureSim sim = Fly(sp, s, 7);
            SoarFlight soar = sim.LocomotionMode<SoarFlight>();
            int frames = 0;
            bool rolled = false, looped = false;
            float maxBend = 0f;
            for (int t = 0; t < 60 * 90; t++)
            {
                float a = t * kDt * 0.35f;
                Vector3 target = sim.Joints[0] + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (6f * s) + Vector3.up * ((7f - sim.Joints[0].y / s) * s);
                sim.Tick(kDt, target);
                if (t > 60 * 30 && !rolled && soar.Manoeuvre == SoarManoeuvre.None) rolled = soar.ForceManoeuvre(SoarManoeuvre.Barrel);
                if (t > 60 * 50 && !looped && soar.Manoeuvre == SoarManoeuvre.None) looped = soar.ForceManoeuvre(SoarManoeuvre.Loop);
                for (int i = 1; i < sim.Joints.Length - 1; i++)
                    maxBend = Mathf.Max(maxBend, Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1]));
                if (t % 3 != 0) continue;
                sim.WriteBoneWorld(world, 0);
                for (int b = 0; b < skin.Length; b++) skin[b] = world[b] * bind[b];
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector4 q = uv2[i];
                    pos[i] = q.z * skin[(int)q.x].MultiplyPoint3x4(v[i]) + q.w * skin[(int)q.y].MultiplyPoint3x4(v[i]);
                }
                for (int k = 0; k < tri.Length; k += 3)
                {
                    int i0 = tri[k], i1 = tri[k + 1], i2 = tri[k + 2];
                    Vector3 rest = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                    if (rest.sqrMagnitude < 1e-16f) continue;
                    Vector3 now = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
                    Vector3 want = skin[(int)uv2[i0].x].MultiplyVector(rest);
                    Assert.Greater(Vector3.Dot(now, want), 0f, $"t={t * kDt:F2} s ({soar.Manoeuvre}, beat {soar.StrokeAmplitude:F2}): triangle {k / 3} (bones {uv2[i0]}, rest area {rest.magnitude:E2}) inverted");
                }
                frames++;
            }
            Assert.IsTrue(rolled && looped, "both manoeuvres flown");
            TestContext.WriteLine($"x{s}: {frames} frames, no inverted triangle; largest joint bend {maxBend:F1} deg (limit {sp.joints[3].bendLimitDeg})");
            Assert.LessOrEqual(maxBend, 1.3f * sp.joints[3].bendLimitDeg, "the stiff disc never kinks (path corners, the heave wave)");
        }

        // ------------------------------------------------------------ the school

        internal static string SchoolProblem(CreatureSpecies sp, int count, float seconds, out float nearest, out int manoeuvres, int seedBase = 1000, bool stopAtFirst = true, System.Action<string> report = null)
        {
            float big = CreatureLab.MaxScale(sp);
            EnclosureArea area = KitChecks.LabArea(sp);
            var sims = new CreatureSim[count]; var brains = new ICreatureBrain[count];
            for (int i = 0; i < count; i++)
            {
                int seed = seedBase + i;
                float jit = sp.look.sizeJitter;
                float s = sp.scale * (1f + jit * Mathf.Lerp(-1f, 1f, (i + 0.5f) / count));   // the jitter's spread
                float a = 2f * Mathf.PI * i / count;
                Vector3 at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.4f * area.radius);
                Vector3 dir = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                sims[i] = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, at, dir, s, seed) { Bounds = area };
                brains[i] = BrainFactory.Create(sp, area, seed);
            }
            nearest = float.MaxValue; manoeuvres = 0;
            float centres = float.MaxValue;
            var pts = new List<Vector3>[count];
            for (int i = 0; i < count; i++) pts[i] = new List<Vector3>(160);
            float span = 2f * Soar.HalfSpan(sp);
            for (int t = 0; t < (int)(seconds / kDt); t++)
            {
                for (int i = 0; i < count; i++) sims[i].Tick(kDt, brains[i].Target(sims[i], kDt));
                if (t < 60 * 15) continue;   // the take-off from the ground ring
                for (int i = 0; i < count; i++)
                {
                    float lo = LowestPoint(sims[i]);
                    if (lo < 0.5f) return $"manta {i} at {lo:F2} m above the ground at t={t * kDt:F1}";
                    foreach (IPartRuntime r in sims[i].PartRuntimes)
                        if (r is RayWingRuntime w)
                            for (int k = 1; k <= w.Stations; k++)
                            {
                                Vector3 p = w.Node(k, 0);
                                if (area.PlanarDistance(p) > area.radius) return $"manta {i} wing outside the aviary at t={t * kDt:F1}";
                                if (p.y > area.maxHeight + 0.2f) return $"manta {i} wing through the roof at t={t * kDt:F1}";
                            }
                }
                if (t % 4 != 0) continue;
                // the bodies themselves: every joint, wing node and tail point of one manta against the other's
                for (int i = 0; i < count; i++) Points(sims[i], pts[i]);
                for (int i = 0; i < count; i++)
                    for (int j = i + 1; j < count; j++)
                    {
                        float c = (sims[i].Joints[3] - sims[j].Joints[3]).magnitude;
                        float need = 0.5f * (sims[i].SizeScale + sims[j].SizeScale) * span;
                        centres = Mathf.Min(centres, c / need);
                        if (c > 1.5f * need) continue;
                        float d = Gap(pts[i], pts[j]);
                        float clear = 0.15f * 0.5f * (sims[i].SizeScale + sims[j].SizeScale);
                        nearest = Mathf.Min(nearest, d);
                        if (d < clear)
                        {
                            if (report != null) report($"t={t * kDt:F1} {i}-{j} gap {d:F2} m centres {c:F2} m");
                            if (stopAtFirst) return $"mantas {i} and {j} touch: {d:F2} m between their bodies (< {clear:F2}) at t={t * kDt:F1}";
                        }
                    }
            }
            TestContext.Out.WriteLine($"closest centres {centres:F2} spans");
            for (int i = 0; i < count; i++) manoeuvres += sims[i].LocomotionMode<SoarFlight>().Manoeuvres;
            return null;
        }

        static void Points(CreatureSim sim, List<Vector3> into)
        {
            into.Clear();
            into.AddRange(sim.Joints);
            foreach (IPartRuntime r in sim.PartRuntimes)
            {
                if (r is RayWingRuntime w)
                    for (int k = 1; k <= w.Stations; k++) for (int q = 0; q < w.Rows; q++) into.Add(w.Node(k, q));
                if (r is WhipTailRuntime t) for (int q = 0; q < t.Points.Length; q += 2) into.Add(t.Points[q]);
            }
        }

        static float Gap(List<Vector3> a, List<Vector3> b)
        {
            float best = float.MaxValue;
            for (int i = 0; i < a.Count; i++) for (int j = 0; j < b.Count; j++) best = Mathf.Min(best, (a[i] - b[j]).sqrMagnitude);
            return Mathf.Sqrt(best);
        }

        [Test] public void School_FiveMantas_ThreeMinutes_NoCollision()
        {
            string p = SchoolProblem(Manta(), 5, 180f, out float nearest, out int manoeuvres);
            TestContext.WriteLine($"closest gap between two mantas {nearest:F2} m, manoeuvres {manoeuvres}");
            Assert.IsNull(p);
            // a manoeuvre the school vetoes waits, then the member peels off for room: about one each, not by luck
            Assert.GreaterOrEqual(manoeuvres, 5, "rolls and loops happen in the school too");
        }

        // RED: without separation (and cohesion pulling them together) the school collides.
        [Test] public void Red_School_NoSeparation_Collides()
        {
            CreatureSpecies sp = Manta();
            School sc = null; foreach (CreatureModule m in KitView.Modules(sp)) if (m is School x) sc = x;
            float sep = sc.separation, coh = sc.cohesion, wp = sc.waypoint, al = sc.alignment;
            try
            {
                sc.separation = 0.01f; sc.cohesion = 3f; sc.waypoint = 0f; sc.alignment = 0f;
                string p = SchoolProblem(sp, 5, 120f, out float nearest, out _);
                TestContext.WriteLine("RED: " + p);
                Assert.IsNotNull(p, "a school with no separation must be caught colliding");
            }
            finally { sc.separation = sep; sc.cohesion = coh; sc.waypoint = wp; sc.alignment = al; }
        }

        [Test] public void School_Gc_ZeroAfterWarmup()
        {
            CreatureSpecies sp = Manta();
            EnclosureArea area = KitChecks.LabArea(sp);
            var sims = new CreatureSim[3]; var brains = new ICreatureBrain[3];
            var bones = new Matrix4x4[sp.BoneCount];
            for (int i = 0; i < 3; i++)
            {
                sims[i] = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, new Vector3(4f * i, 0f, 0f), Vector3.forward, 1f, 50 + i) { Bounds = area };
                brains[i] = BrainFactory.Create(sp, area, 50 + i);
            }
            for (int t = 0; t < 120; t++) for (int i = 0; i < 3; i++) { sims[i].Tick(kDt, brains[i].Target(sims[i], kDt)); sims[i].WriteBoneWorld(bones, 0); }
            GcAllocs.AssertCounterWorks();
            int allocs = GcAllocs.Count(() =>
            {
                for (int t = 0; t < 300; t++) for (int i = 0; i < 3; i++) { sims[i].Tick(kDt, brains[i].Target(sims[i], kDt)); sims[i].WriteBoneWorld(bones, 0); }
            });
            Assert.AreEqual(0, allocs);
        }
    }
}
