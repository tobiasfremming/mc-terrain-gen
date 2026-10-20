// Offline authoring tools (kit services 2026-10-20), run by creature_kit.py:
//   stats <Species>                         gait and stance numbers every animal agent used to write a scratch test for:
//                                           speed (m/s and Froude), steps/s, duty, lift and step time per leg (x leg
//                                           length, Froude units), body height and bob, rest stance per leg (reach share,
//                                           IK s and pitch), mesh size and build time, tick cost, the phases its own
//                                           brain reaches (the pose sweep's channels)
//   trace <Species> [--out f.csv] [--phase P] [--part ID] [--seconds S] [--seed N]
//                                           the bone trace: one CSV row per tick of the pose sweep's run (its own brain,
//                                           every phase): time, phase label, every spine joint's bend, and for the
//                                           part's bones (all kit-part bones without --part) position, forward, up,
//                                           scale and handedness (det < 0 = mirrored); --phase keeps the ticks whose
//                                           label contains P
//   pose-dump <Species> --out DIR [--phase P,Q] [--seconds S] [--seed N]
//                                           the skinned mesh as OBJ (v x y z r g b nx ny nz; colours from the species'
//                                           look and layer rows) at the first moment of each phase (a quarter second in;
//                                           every phase seen when none is named; "walk" = the first second of walking),
//                                           heading along +z, ground at y = 0: what Tools~/CreatureLook/render.py draws
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CreatureKit;
using TerrainGen.Tests;
using UnityEngine;

public static class HarnessTools
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    static string F(float x) => x.ToString("0.####", ci);

    static string Opt(List<string> a, string name, string def = null)
    {
        int i = a.IndexOf(name);
        return i >= 0 && i + 1 < a.Count ? a[i + 1] : def;
    }

    static CreatureSpecies Load(List<string> a, out string name)
    {
        name = a.Count > 0 && !a[0].StartsWith("--") ? a[0] : null;
        if (name == null) throw new ArgumentException("name a species");
        CreatureSpecies sp = CreatureKitAssets.Load(name);
        if ((object)sp == null) throw new ArgumentException("no species asset " + name);
        string err = sp.Validate();
        if (err != null) throw new ArgumentException(name + ": " + err);
        return sp;
    }

    public static int Run(string mode, List<string> a)
    {
        try
        {
            switch (mode)
            {
                case "stats": return Stats(a);
                case "trace": return Trace(a);
                case "pose-dump": return PoseDump(a);
            }
        }
        catch (ArgumentException e) { Console.WriteLine(mode + ": " + e.Message); return 2; }
        return 2;
    }

    // ------------------------------------------------------------------ stats

    static int Stats(List<string> a)
    {
        CreatureSpecies sp = Load(a, out string name);
        if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) { Console.WriteLine(name + ": cephalopod - not a CreatureSim (Octopus*Tests)"); return 0; }
        float s = sp.scale, T = Mathf.Sqrt(s);
        const float g = 9.81f;
        var sb = new StringBuilder();
        sb.AppendLine($"{name}: scale {F(s)}, {sp.JointCount} joints, body length {F(sp.BodyLength * s)} m, {sp.BoneCount} bones, legs {sp.legs?.Length ?? 0}, wings {sp.WingCount}");

        // straight walk, 15 s (x sqrt s) after a 2 s start
        var sim = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, s, 1);
        float dt = 1f / 60f;
        int warm = Mathf.CeilToInt(2f * T / dt), n = Mathf.CeilToInt(15f * T / dt);
        Vector3 far = new Vector3(0f, 0f, 1000f * s);
        int legs = sp.legs?.Length ?? 0;
        var steps = new int[legs]; var stepTicks = new int[legs]; var was = new bool[legs]; var lift = new float[legs];
        var lift0 = new float[legs];
        float hMin = float.MaxValue, hMax = float.MinValue, hSum = 0f;
        for (int t = 0; t < warm; t++) sim.Tick(dt, far);
        Vector3 start = sim.Joints[0];
        for (int t = 0; t < n; t++)
        {
            sim.Tick(dt, far);
            float h = sim.Joints[0].y;
            hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h); hSum += h;
            for (int l = 0; l < legs; l++)
            {
                LegState st = sim.Legs[l];
                bool air = st.stepping && st.stepT >= 0f;
                if (air && !was[l]) { steps[l]++; lift0[l] = st.foot.y; }
                if (air) { stepTicks[l]++; lift[l] = Mathf.Max(lift[l], st.foot.y - lift0[l]); }
                was[l] = air;
            }
        }
        float secs = n * dt, dist = Vector3.ProjectOnPlane(sim.Joints[0] - start, Vector3.up).magnitude, v = dist / secs;
        float L = sp.BodyLength * s;
        sb.AppendLine($"walk: {F(v)} m/s ({F(v / Mathf.Sqrt(g * L))} Froude of the body length, cruise {F(sp.speed * T)}), head height {F(hSum / n)} m, bob {F(hMax - hMin)} m");
        if (legs > 0)
        {
            sb.AppendLine("  leg            len(m)  steps/s  duty   lift/L  step(Froude)  rest reach  IK s   pitch");
            for (int l = 0; l < legs; l++)
            {
                LegSpec q = sp.legs[l];
                float len = q.TotalLength * s;
                float sps = steps[l] / secs, duty = 1f - stepTicks[l] / (float)n;
                float stepT = steps[l] > 0 ? stepTicks[l] * dt / steps[l] : 0f;
                float froude = stepT * Mathf.Sqrt(g / Mathf.Max(1e-4f, len));
                Vector3 rest = sim.LegRestPoint(l), sh = sim.Legs[l].shoulder;
                float reach = (rest - sh).magnitude / Mathf.Max(1e-4f, len);
                string ik = "-", pitch = "-";
                if (q.IsArthropod) { try { RestReport r = RestStance.Report(sp, l); ik = r.s.ToString("0.0", ci); pitch = r.pitchDeg.ToString("0.0", ci); } catch (Exception) { } }
                sb.AppendLine($"  {KitPoseSweep.BonePart(sp, sp.LegBone(l, 0)).Replace(" bone 0", ""),-14} {F(len),6}  {F(sps),7}  {F(duty),5}  {F(lift[l] / Mathf.Max(1e-4f, len)),6}  {F(froude),12}  {F(reach),10}  {ik,5}  {pitch,5}{(q.feeler ? "  feeler" : "")}");
            }
        }
        // mesh and cost
        var w = Stopwatch.StartNew();
        Mesh mesh = CreatureKitAssets.BuildMesh(sp);
        double meshMs = w.Elapsed.TotalMilliseconds;
        sb.AppendLine($"mesh: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles, built in {meshMs:F1} ms (first build, uncached)");
        UnityEngine.Object.DestroyImmediate(mesh);
        sb.AppendLine($"tick + bones: {KitChecks.TickMedianMs(sp) * 1000.0:F1} us median (budget {CreatureKitAssets.PerfBudgetMs(sp) * 1000f:F0} us)");
        // its own phases
        KitPoseSweep.Result r2 = KitPoseSweep.Run(sp, s, 1, 0f, false);
        sb.AppendLine($"phases its own brain reaches in {r2.Seconds:F0} s: {(r2.Seen.Count > 0 ? string.Join(", ", r2.Seen) : "walk only")}" +
                      (r2.Missing.Count > 0 ? $"; never: {string.Join(", ", r2.Missing)}" : ""));
        sb.AppendLine($"pose sweep: {(r2.Defects.Count == 0 ? "clean" : r2.Defects.Count + " defect(s), first: " + r2.Defects[0])}");
        Console.Write(sb.ToString());
        return 0;
    }

    // ------------------------------------------------------------------ trace

    static int Trace(List<string> a)
    {
        CreatureSpecies sp = Load(a, out string name);
        string outPath = Opt(a, "--out", name + "_trace.csv"), phase = Opt(a, "--phase"), part = Opt(a, "--part");
        float seconds = float.Parse(Opt(a, "--seconds", "0"), ci);
        int seed = int.Parse(Opt(a, "--seed", "1"), ci);
        var bones = new List<int>(); var names = new List<string>();
        foreach (BoneRange r in sp.Layout.Ranges)
        {
            if (part != null ? r.Id != part && !r.Id.StartsWith(part + ".", StringComparison.Ordinal) : r.Start < sp.JointCount) continue;
            for (int k = 0; k < r.Count; k++) { bones.Add(r.Start + k); names.Add($"{r.Id}.{k}"); }
        }
        if (part != null && bones.Count == 0) throw new ArgumentException($"no part '{part}' with bones (ids: {string.Join(", ", Array.ConvertAll(sp.Layout.Ranges, r => r.Id))})");
        var sb = new StringBuilder();
        sb.Append("t,phase");
        for (int i = 1; i < sp.JointCount - 1; i++) sb.Append(",bend").Append(i);
        foreach (string b in names) sb.Append($",{b}.x,{b}.y,{b}.z,{b}.fx,{b}.fy,{b}.fz,{b}.ux,{b}.uy,{b}.uz,{b}.scale,{b}.det");
        sb.Append('\n');
        int rows = 0;
        var labels = new SortedSet<string>(StringComparer.Ordinal);
        KitPoseSweep.Result res = KitPoseSweep.Run(sp, sp.scale, seed, seconds, false, (sim, label, m) =>
        {
            labels.Add(label);
            if (phase != null && !label.Contains(phase)) return;
            sb.Append(F(sim.Time)).Append(',').Append(label);
            for (int i = 1; i < sp.JointCount - 1; i++) sb.Append(',').Append(F(Vector3.Angle(sim.Joints[i - 1] - sim.Joints[i], sim.Joints[i] - sim.Joints[i + 1])));
            foreach (int b in bones)
            {
                Matrix4x4 x = m[b];
                Vector3 p = x.GetColumn(3), fz = x.GetColumn(2), uy = x.GetColumn(1), rx = x.GetColumn(0);
                float det = Vector3.Dot(Vector3.Cross(rx, uy), fz);
                sb.Append(',').Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z));
                Vector3 f = fz.normalized, u = uy.normalized;
                sb.Append(',').Append(F(f.x)).Append(',').Append(F(f.y)).Append(',').Append(F(f.z));
                sb.Append(',').Append(F(u.x)).Append(',').Append(F(u.y)).Append(',').Append(F(u.z));
                sb.Append(',').Append(F(fz.magnitude)).Append(',').Append(F(det));
            }
            sb.Append('\n');
            rows++;
        });
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"wrote {Path.GetFullPath(outPath)}: {rows} rows, {bones.Count} bones, {res.Seconds:F0} s; phases: {string.Join(" | ", labels)}");
        foreach (string d in res.Defects) Console.WriteLine("  defect " + d);
        return 0;
    }

    // ------------------------------------------------------------------ pose dump

    static int PoseDump(List<string> a)
    {
        CreatureSpecies sp = Load(a, out string name);
        string dir = Opt(a, "--out") ?? throw new ArgumentException("--out DIR");
        Directory.CreateDirectory(dir);
        string[] want = (Opt(a, "--phase") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        float seconds = float.Parse(Opt(a, "--seconds", "0"), ci);
        int seed = int.Parse(Opt(a, "--seed", "1"), ci);
        Mesh mesh = CreatureKitAssets.BuildMesh(sp);
        Vector3[] v = mesh.vertices, nrm = mesh.normals; int[] tri = mesh.triangles;
        var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);
        var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
        UnityEngine.Object.DestroyImmediate(mesh);
        Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
        Color[] col = Colours(sp, uv1);
        var done = new HashSet<string>(); var since = new Dictionary<string, int>();
        var written = new List<string>();
        int quarter = Mathf.Max(1, Mathf.RoundToInt(0.25f * Mathf.Sqrt(sp.scale) * 60f));
        KitPoseSweep.Result res = KitPoseSweep.Run(sp, sp.scale, seed, seconds, false, (sim, label, m) =>
        {
            // the names a frame answers to: its label, each "channel=phase" in it, and "walk" for a label of walking
            foreach (string key in Keys(label))
            {
                if (done.Contains(key) || (want.Length > 0 && Array.IndexOf(want, key) < 0 && Array.IndexOf(want, key.Substring(key.IndexOf('=') + 1)) < 0)) continue;
                since[key] = since.TryGetValue(key, out int c) ? c + 1 : 1;
                if (since[key] < (key == "walk" ? 60 : quarter)) continue;
                done.Add(key);
                string file = Path.Combine(dir, $"{name}_{key.Replace('=', '-').Replace(' ', '_')}.obj");
                WriteObj(file, sim, m, bind, v, nrm, tri, uv2, col, $"{name} {key} t={sim.Time:F2} s");
                written.Add(file);
            }
        });
        foreach (string f in written) Console.WriteLine("wrote " + Path.GetFullPath(f));
        var miss = new List<string>();
        foreach (string w in want) { bool hit = false; foreach (string d in done) if (d == w || d.EndsWith("=" + w, StringComparison.Ordinal)) hit = true; if (!hit) miss.Add(w); }
        if (miss.Count > 0) Console.WriteLine($"never reached: {string.Join(", ", miss)} (seen: {string.Join(", ", res.Seen)})");
        return miss.Count > 0 && written.Count == 0 ? 1 : 0;
    }

    static IEnumerable<string> Keys(string label)
    {
        if (label == "walk") { yield return "walk"; yield break; }
        foreach (string p in label.Split(' ')) yield return p;
        if (label.IndexOf(' ') > 0) yield return label;
    }

    // Albedo per vertex from the species' look: the dorsal / flank / belly blend on the dorsal channel (uv1.y), then the
    // vertex's layer row (uv1.x; the species' Layers on top of the registry) tints it. A sketch of Creature.shader's
    // colour, not its lighting.
    static Color[] Colours(CreatureSpecies sp, List<Vector2> uv1)
    {
        CreatureLook L = sp.look;
        PartLayer[] rows = PartLayers.Rows(sp);
        var c = new Color[uv1.Count];
        for (int i = 0; i < c.Length; i++)
        {
            float d = Mathf.Clamp(uv1[i].y, -1f, 1f);
            Color b = d >= 0f ? Mix(L.flank, L.dorsal, d) : Mix(L.flank, L.belly, -d);
            int id = Mathf.Clamp(Mathf.RoundToInt(uv1[i].x), 0, rows.Length - 1);
            PartLayer r = rows[id];
            if (r.model == ShadingModel.Eye) b = new Color(0.04f, 0.03f, 0.03f);
            else if (r.model == ShadingModel.Membrane) b = L.membrane.a > 0f || L.membrane.r + L.membrane.g + L.membrane.b > 0f ? L.membrane : b;
            Color tint = r.tintSource switch
            {
                TintSource.Own => r.tint,
                TintSource.Pattern => L.pattern,
                TintSource.Band => L.bandColor,
                TintSource.Dorsal => L.dorsal,
                TintSource.Belly => L.belly,
                TintSource.Membrane => L.membrane,
                _ => b,
            };
            c[i] = Mix(b, tint, Mathf.Clamp01(r.tintAmount));
        }
        return c;
    }

    static Color Mix(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f);

    static void WriteObj(string file, CreatureSim sim, Matrix4x4[] m, Matrix4x4[] bind, Vector3[] v, Vector3[] nrm, int[] tri, List<Vector4> uv2, Color[] col, string title)
    {
        var skin = new Matrix4x4[m.Length];
        for (int b = 0; b < m.Length; b++) skin[b] = m[b] * bind[b];
        // heading along +z (yaw only), centred over the body, ground (y = 0) kept
        Vector3 fwd = Vector3.ProjectOnPlane(sim.Rotations[0] * Vector3.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        Quaternion inv = Quaternion.Inverse(Quaternion.LookRotation(fwd.normalized, Vector3.up));
        Vector3 o = sim.Joints[sim.Joints.Length / 2]; o.y = 0f;
        var sb = new StringBuilder();
        sb.Append("# ").Append(title).Append('\n');
        for (int i = 0; i < v.Length; i++)
        {
            Vector4 s = uv2[i];
            Matrix4x4 a = skin[(int)s.x], b = skin[(int)s.y];
            Vector3 p = s.z * a.MultiplyPoint3x4(v[i]) + s.w * b.MultiplyPoint3x4(v[i]);
            Vector3 n = (s.z * a.MultiplyVector(nrm[i]) + s.w * b.MultiplyVector(nrm[i])).normalized;
            p = inv * (p - o); n = inv * n;
            sb.Append("v ").Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z)).Append(' ')
              .Append(F(col[i].r)).Append(' ').Append(F(col[i].g)).Append(' ').Append(F(col[i].b)).Append(' ')
              .Append(F(n.x)).Append(' ').Append(F(n.y)).Append(' ').Append(F(n.z)).Append('\n');
        }
        for (int k = 0; k < tri.Length; k += 3) sb.Append("f ").Append(tri[k] + 1).Append(' ').Append(tri[k + 1] + 1).Append(' ').Append(tri[k + 2] + 1).Append('\n');
        File.WriteAllText(file, sb.ToString());
    }
}
