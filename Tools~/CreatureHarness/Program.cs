// Offline creature harness runner: discovers the NUnit tests compiled in (MiniNUnit attributes) and runs them.
//   test [filter ...] [-v] [--exclude-category C]   run tests whose "Class.Method" (or "Class(arg).Method")
//                                                   contains a filter (all when none); -v prints test output
//   list [filter ...]                               list the tests that would run
//   species                                         the species assets the kit sees, with unknown YAML keys
//   catalogue [--write]                             Config/Creatures/CATALOGUE.md + catalogue.json: fresh or stale
//                                                   (exit 1), or write them (the Unity menu writes the same bytes)
//   kit-convert <Species> <NewName>                 write Config/Creatures/<NewName>.asset: the legacy species as
//                                                   kit parts + modules (kitVersion 1, mirror/pattern sugar)
//   stats <Species> | trace <Species> ... | pose-dump <Species> --out DIR ...
//                                                   authoring tools (HarnessTools.cs): gait / stance numbers, the bone
//                                                   trace (CSV per tick, per phase), the skinned mesh per phase (OBJ)
// Exit code: the number of failures (0 = green). Statuses: PASS, FAIL, SKIP (Inconclusive/Ignore, or the
// harness skip list below), NATIVE (RealUnity mode: the test reached engine code the harness cannot run),
// XFAIL / XPASS (kKnown: a known offline divergence or a recorded finding).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

public static class Program
{
    // Tests that compile offline but cannot run here, with the reason. Keep this short: prefer making the
    // harness able to run a test over listing it.
    // Key = "Class.Method" or "Class." (the whole class).
    static readonly Dictionary<string, string> kSkip = new Dictionary<string, string>
    {
    };

    // Known failures, each reported XFAIL (not counted; XPASS once it passes, so the entry can go):
    //  "offline": passes in Unity, fails here because .NET 8's MathF rounds differently from Mono and a long or
    //             chaotic run drifts onto another path (needs a Unity result);
    //  "finding": a genuine defect the kit found in a species or the code - it fails in Unity too and is fixed
    //             in the species or the code, never by loosening the check.
    static readonly Dictionary<string, string> kKnown = new Dictionary<string, string>
    {
        // (2026-10-19, rigid head: the monitor's two near-zero-triangle / drift entries and their kit copies pass offline now
        // and were removed; the dragon's Size moved the other way - float chaos, not the head. On merge/next the wyvern's MaxBend and Size pass offline again and were removed)
        // Seeded samples of chaotic runs: the rigid head moves which seeds land in a failure mode the integration branch
        // has too (measured 2026-10-19 on more seeds, integration 310a026+ vs this branch; findings for their owners)
        // (R38 climbing fix 2026-10-19: CreatureClimbWhipTests.LabCycle_SlopedBuriedFoot_NoWhip passes offline and in Unity again - entry removed)
        // (dragon v3 2026-10-19, four limbs + wing-walk: SkyDragon Size (steps 75 vs 77) and Brain_Manoeuvres(1,0.94) pass offline - entries removed)
        // feat/wyvern-trees (32795f7: racetrack laps green on seeds 1,2); seeds 3,4 still fail offline - findings for the flight owner
        { "WingedSerpentTreeTests.Perch_TreeCrown_Brain(0.9,3)",
          "finding: no perch within 3 minutes at x0.9 seed 3 (approaches rejected 14, room 13, aborted 2, 2 flights); known open on feat/wyvern-trees (32795f7), seen again on merge/next with the rigid head" },
        { "WingedSerpentTreeTests.Perch_TreeCrown_Brain(1.1,4)",
          "finding: x1.1 seed 4 puts a joint 6 cm into the bark on the way (bound 5.5 mm); known open on feat/wyvern-trees (32795f7), seen again on merge/next with the rigid head" },
        // (2026-10-20, arthropod archetype: the king crab on the crab plan, walking sideways) CreatureTendrilTests.Tendril_Size_FroudeSimilar
        // passes offline again - its entry (a chaos-sensitive instantaneous hang on the old tripod walk) was removed
        { "CreatureKitTests(TestHexapod).Size",
          "finding: stop-go walk (throttle 0 on 27 % of a straight walk): 0.9x/1.1x distance 1.4 % apart (test fixture, Task 10); fails in Unity 2026-10-18 too (5.1733 vs 5.3894, 4.2 % apart)" },
        // (2026-10-20, kit services: check 13 PoseSweep and the Gait check on the drawn feet are new; what they found in the
        // shipped species on their first run - findings for the species' owners, full lists in the kit-services report)
        { "CreatureKitTests(Pterodactyl).PoseSweep",
          "finding: launch - wing.R bone 4 (a single-bone membrane triangle) inverts: the wing bone's matrix turns mirrored (det < 0) in the launch unfold; then a flipped normal on wing.R bone 5" },
        { "CreatureKitTests(Delver).PoseSweep",
          "finding: burrow dive / lunge / rise - triangles between spine joints 5 and 6 (the neck/fore-shell blend) invert and flip their normals as the body pitches into and out of the ground; once walking (joint 2/3, t=85 s)" },
        { "CreatureKitTests(Hexadrake).PoseSweep",
          "finding: launch - wingarm.R bone 1 (elbow blend) inverts; beat / glide / flare / touchdown / wing-walk - triangle 8179 (spine joints 6/7, the shoulder) inverts again and again; a 64 deg kink at spine joint 15 in a later launch" },
        { "CreatureKitTests(KomodoDragon).PoseSweep",
          "finding: tongue retracting ('in') - a sliver at the tongue's fork (tongue bone 0/1) inverts; the walk-only MaxBend never sees the tongue move" },
        { "CreatureKitTests(SkyDragon).PoseSweep",
          "finding: launch - the shoulder blend (spine joint 40 with arm bone 78) inverts; beat / flare (spine 14 with arm bones 72/75), touchdown (spine 11/12, 18/19), glide (spine 5/4) invert or flip normals; wing.R bone 13 normal flips in the beat" },
        { "CreatureKitTests(WingedSerpent).PoseSweep",
          "finding: launch / beat - the shoulder blend (spine joint 6 with arm bone 36) inverts; touchdown - an accordion kink of 75 deg at spine joint 2 (limit 18) with an inverted triangle at joint 1/2" },
        { "CreatureKitTests(Pterodactyl).Gait",
          "finding: wing-walk - the planted hand's thumb claw (CreatureWings.ThumbTip, what stands on the ground) skates 3.3 % of the arm's length over a plant while the sim foot stays put" },
        { "CreatureKitTests(WingedSerpent).Gait",
          "finding: wing-walk - the planted hand's thumb claw skates 3.8 % of the arm's length over a plant (SkyDragon 0.99 %, under the 1 % bound)" },
        // check 14 Group (the roster's groups): the walkers' brains do not know their group mates (no separation) -
        // KitGroup + Separation are the service a fix would use
        { "CreatureKitTests(RockCrab).Group",
          "finding: 5 crabs walk into each other within seconds (t=6.3 s: leg0.R tip into another's crusher claw, 11 mm); WanderBrain has no separation" },
        { "CreatureKitTests(Delver).Group",
          "finding: 3 delvers overlap at the fore-body (spine joint 6 on spine joint 6, 8 mm at t=11.4 s): Follow trails a member with no keep-out" },
        { "CreatureKitTests(ShaggyMonitor).Group",
          "finding: the pair walks through each other (a fore leg 116 mm into the other's tail at t=52.9 s); WanderBrain has no separation" },
    };

    sealed class Case
    {
        public string Name; public Type Fixture; public object[] FixtureArgs; public MethodInfo Method; public object[] Args;
        public string[] Categories;
    }

    public static int Main(string[] args)
    {
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        string mode = args.Length > 0 ? args[0] : "test";
        var filters = new List<string>(); var excludeCats = new List<string>(); bool verbose = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "-v") verbose = true;
            else if (args[i] == "--exclude-category" && i + 1 < args.Length) excludeCats.Add(args[++i]);
            else filters.Add(args[i]);
        }
        switch (mode)
        {
            case "list":
                foreach (Case c in Discover(filters, excludeCats)) Console.WriteLine(c.Name);
                return 0;
            case "species": return Species();
            case "kit-convert": return KitConvert(filters);
            case "catalogue": return Catalogue(filters.Contains("--write"));
            case "test": return Run(Discover(filters, excludeCats), verbose);
            case "stats": case "trace": case "pose-dump": return HarnessTools.Run(mode, filters);
            default:
                Console.WriteLine("usage: CreatureHarness test [filter ...] [-v] [--exclude-category C] | list [filter ...] | species");
                return 2;
        }
    }

    static int Species()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:CreatureSpecies", new[] { "Assets/TerrainGen/Config/Creatures" });
        foreach (string g in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
            CreatureSpecies sp;
            try { sp = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>(path); }
            catch (Exception e) when (HarnessYaml.IsNative(e)) { Console.WriteLine($"{path}: NATIVE ({e.Message})"); continue; }
            var unknown = HarnessYaml.UnknownKeys(HarnessPaths.Full(path));
            Console.WriteLine($"{path}: {((object)sp == null ? "NOT LOADED" : sp.bodyPlan + ", " + sp.BoneCount + " bones, Validate " + (sp.Validate() ?? "ok"))}" +
                              (unknown.Count > 0 ? "; unknown keys: " + string.Join(", ", unknown) : ""));
        }
        return 0;
    }

    // Config/Creatures/CATALOGUE.md + catalogue.json, the same bytes as the Unity menu (Editor/CreatureKitCatalogue).
    // Without --write: report whether the checked-in files are fresh (exit 1 when stale).
    static int Catalogue(bool write)
    {
        CreatureKit.CatalogueWriter.Build(TerrainGen.Tests.CreatureKitAssets.CatalogueSpecies(), out string md, out string json);
        int stale = 0;
        foreach (var (asset, text) in new[] { (CreatureKit.CatalogueWriter.MarkdownPath, md), (CreatureKit.CatalogueWriter.JsonPath, json) })
        {
            string full = HarnessPaths.Full(asset);
            bool fresh = File.Exists(full) && File.ReadAllText(full).Replace("\r\n", "\n") == text;
            if (!fresh && write) { File.WriteAllText(full, text); Console.WriteLine($"wrote {full} ({text.Split('\n').Length} lines)"); }
            else Console.WriteLine($"{full}: {(fresh ? "fresh" : "STALE")}");
            if (!fresh && !write) stale++;
        }
        return stale > 0 ? 1 : 0;
    }

    static int KitConvert(List<string> a)
    {
        if (a.Count != 2) { Console.WriteLine("usage: CreatureHarness kit-convert <Species> <NewName>"); return 2; }
        CreatureSpecies sp = UnityEditor.AssetDatabase.LoadAssetAtPath<CreatureSpecies>("Assets/TerrainGen/Config/Creatures/" + a[0] + ".asset");
        if ((object)sp == null) { Console.WriteLine("no species " + a[0]); return 1; }
        if (sp.kitVersion > 0) { Console.WriteLine(a[0] + " is already a kit species"); return 1; }
        var parts = CreatureKit.LegacySpeciesAdapter.ToParts(sp, true, out var modules);
        var c = CreatureKit.CompiledSpecies.Compose(a[1], parts, modules);
        if (c.Error != null) { Console.WriteLine("does not compose: " + c.Error); return 1; }
        string guid = null;
        foreach (string line in File.ReadLines(Path.Combine(HarnessPaths.TerrainGenRoot, "Scripts", "Creatures", "CreatureSpecies.cs.meta")))
            if (line.StartsWith("guid:")) guid = line.Substring(5).Trim();
        string yaml = HarnessKitYaml.Emit(a[1], sp.scale, guid, parts, modules);
        string path = Path.Combine(HarnessPaths.TerrainGenRoot, "Config", "Creatures", a[1] + ".asset");
        File.WriteAllText(path, yaml);
        Console.WriteLine($"wrote {path}: {parts.Count} parts, {modules.Count} modules, {yaml.Split('\n').Length} lines (from {a[0]})");
        return 0;
    }

    // ------------------------------------------------------------------ discovery

    static bool Matches(string name, List<string> filters)
    {
        if (filters.Count == 0) return true;
        foreach (string f in filters)
        {
            if (name.Contains(f)) return true;
            // "Class" also matches "Class(arg).Method"
            int p = name.IndexOf('(');
            if (p > 0 && name.Substring(0, p) == f) return true;
        }
        return false;
    }

    static List<Case> Discover(List<string> filters, List<string> excludeCats)
    {
        var cases = new List<Case>();
        var types = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace != null && t.Namespace.StartsWith("TerrainGen.Tests"))
            .OrderBy(t => t.Name, StringComparer.Ordinal);
        foreach (Type t in types)
        {
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.IsDefined(typeof(TestAttribute)) || m.IsDefined(typeof(TestCaseAttribute)) || m.IsDefined(typeof(TestCaseSourceAttribute)))
                .OrderBy(m => m.MetadataToken).ToList();
            if (methods.Count == 0) continue;
            string[] classCats = t.GetCustomAttributes<CategoryAttribute>().Select(c => c.Name).ToArray();
            foreach (object[] fargs in FixtureArgs(t))
            {
                string fname = fargs == null ? t.Name : $"{t.Name}({string.Join(",", fargs.Select(Show))})";
                foreach (MethodInfo m in methods)
                {
                    string[] cats = classCats.Concat(m.GetCustomAttributes<CategoryAttribute>().Select(c => c.Name)).ToArray();
                    if (cats.Any(excludeCats.Contains)) continue;
                    foreach (object[] margs in MethodArgs(t, m))
                    {
                        string name = fname + "." + m.Name + (margs == null ? "" : $"({string.Join(",", margs.Select(Show))})");
                        if (!Matches(name, filters)) continue;
                        cases.Add(new Case { Name = name, Fixture = t, FixtureArgs = fargs, Method = m, Args = margs, Categories = cats });
                    }
                }
            }
        }
        return cases;
    }

    static string Show(object o) => o == null ? "null" : o is string s ? s : o is float f ? f.ToString("R") : o.ToString();

    static IEnumerable<object[]> FixtureArgs(Type t)
    {
        var srcs = t.GetCustomAttributes<TestFixtureSourceAttribute>().ToList();
        if (srcs.Count == 0) { yield return null; yield break; }
        foreach (var s in srcs)
            foreach (object o in Source(s.SourceType ?? t, s.SourceName))
                yield return o as object[] ?? new[] { o };
    }

    static IEnumerable<object[]> MethodArgs(Type t, MethodInfo m)
    {
        var rows = m.GetCustomAttributes<TestCaseAttribute>().ToList();
        var srcs = m.GetCustomAttributes<TestCaseSourceAttribute>().ToList();
        if (rows.Count == 0 && srcs.Count == 0) { yield return null; yield break; }
        foreach (var r in rows) yield return Convert(r.Arguments, m.GetParameters());
        foreach (var s in srcs)
            foreach (object o in Source(s.SourceType ?? t, s.SourceName))
                yield return Convert(o as object[] ?? new[] { o }, m.GetParameters());
    }

    static object[] Convert(object[] a, ParameterInfo[] ps)
    {
        var r = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            object v = i < a.Length ? a[i] : ps[i].DefaultValue;
            Type pt = ps[i].ParameterType;
            if (v != null && !pt.IsInstanceOfType(v) && v is IConvertible) v = pt.IsEnum ? Enum.ToObject(pt, v) : System.Convert.ChangeType(v, pt, System.Globalization.CultureInfo.InvariantCulture);
            r[i] = v;
        }
        return r;
    }

    static IEnumerable Source(Type t, string name)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        object holder = null;
        MemberInfo mem = t.GetMember(name, all).FirstOrDefault() ?? throw new MissingMemberException(t.Name, name);
        bool isStatic = mem is FieldInfo fi0 ? fi0.IsStatic : mem is PropertyInfo pi0 ? pi0.GetGetMethod(true).IsStatic : ((MethodInfo)mem).IsStatic;
        if (!isStatic) holder = Activator.CreateInstance(t);
        object v = mem is FieldInfo fi ? fi.GetValue(holder) : mem is PropertyInfo pi ? pi.GetValue(holder) : ((MethodInfo)mem).Invoke(holder, null);
        return (IEnumerable)v;
    }

    // ------------------------------------------------------------------ running

    static int Run(List<Case> cases, bool verbose)
    {
        int pass = 0, fail = 0, skip = 0, native = 0, xfail = 0, xpass = 0;
        var failed = new List<string>();
        TextWriter console = Console.Out;
        var total = Stopwatch.StartNew();
        // one fixture instance per (fixture, args), like NUnit; OneTimeSetUp once each
        var fixtures = new Dictionary<string, object>();
        var oneTimeFailed = new Dictionary<string, string>();
        foreach (Case c in cases)
        {
            string reason = SkipReason(c.Name);
            if (reason != null) { skip++; console.WriteLine($"SKIP {c.Name}: {reason}"); continue; }
            string fkey = c.Fixture.FullName + "|" + (c.FixtureArgs == null ? "" : string.Join(",", c.FixtureArgs.Select(Show)));
            var log = new StringWriter();
            Console.SetOut(log);
            var sw = Stopwatch.StartNew();
            string status, msg = null;
            try
            {
                if (!fixtures.TryGetValue(fkey, out object fx))
                {
                    fx = Activator.CreateInstance(c.Fixture, c.FixtureArgs ?? Array.Empty<object>());
                    fixtures[fkey] = fx;
                    try { Invoke<OneTimeSetUpAttribute>(fx); }
                    catch (Exception e) { oneTimeFailed[fkey] = Describe(Unwrap(e)); }
                }
                if (oneTimeFailed.TryGetValue(fkey, out string otf)) throw new AssertionException("OneTimeSetUp failed: " + otf);
                try
                {
                    Invoke<SetUpAttribute>(fx);
                    c.Method.Invoke(fx, c.Args);
                }
                finally { Invoke<TearDownAttribute>(fx); }
                status = "PASS";
            }
            catch (Exception e0)
            {
                Exception e = Unwrap(e0);
                if (e is InconclusiveException || e is IgnoreException) { status = "SKIP"; msg = (e is IgnoreException ? "ignored: " : "inconclusive: ") + e.Message; }
                else if (HarnessYaml.IsNative(e)) { status = "NATIVE"; msg = Describe(e); }
                else { status = "FAIL"; msg = Describe(e); }
            }
            Console.SetOut(console);
            long ms = sw.ElapsedMilliseconds;
            if (Known(c.Name) is string why)
            {
                if (status == "FAIL") { status = "XFAIL"; msg = msg + " [known " + why + "]"; }
                else if (status == "PASS") { status = "XPASS"; msg = "passes now: remove it from kKnown (" + why + ")"; }
            }
            switch (status) { case "PASS": pass++; break; case "FAIL": fail++; failed.Add(c.Name); break; case "NATIVE": native++; break; case "XFAIL": xfail++; break; case "XPASS": xpass++; pass++; break; default: skip++; break; }
            console.WriteLine(msg == null ? $"{status} {c.Name} ({ms} ms)" : $"{status} {c.Name} ({ms} ms): {msg}");
            string output = log.ToString();
            if ((verbose || status == "FAIL" || status == "XFAIL") && output.Length > 0)
                foreach (string line in output.TrimEnd().Split('\n')) console.WriteLine("    | " + line.TrimEnd('\r'));
        }
        foreach (object fx in fixtures.Values) { try { Invoke<OneTimeTearDownAttribute>(fx); } catch { } }
        console.WriteLine($"{pass} passed, {fail} failed, {skip} skipped, {native} native, {xfail} known ({total.Elapsed.TotalSeconds:F1} s)" +
                          (xpass > 0 ? $"; {xpass} known entries pass now" : ""));
        if (failed.Count > 0) console.WriteLine("failed: " + string.Join(" ", failed));
        return fail;
    }

    static string SkipReason(string name)
    {
        foreach (var kv in kSkip)
            if (name == kv.Key || name.StartsWith(kv.Key.EndsWith(".") ? kv.Key : kv.Key + "(") || (kv.Key.EndsWith(".") && name.StartsWith(kv.Key))) return kv.Value;
        // a class-qualified key also covers parameterised fixtures: "Class.Method" matches "Class(x).Method"
        int p = name.IndexOf('('), d = name.IndexOf(").");
        if (p > 0 && d > p)
        {
            string plain = name.Substring(0, p) + name.Substring(d + 1);
            foreach (var kv in kSkip) if (plain == kv.Key || plain.StartsWith(kv.Key + "(")) return kv.Value;
        }
        return null;
    }

    static string Known(string name)
    {
        int p = name.IndexOf('('), d = name.IndexOf(").");
        string plain = p > 0 && d > p ? name.Substring(0, p) + name.Substring(d + 1) : name;
        return kKnown.TryGetValue(name, out string a) ? a : kKnown.TryGetValue(plain, out string b) ? b : null;
    }

    static void Invoke<T>(object fx) where T : Attribute
    {
        foreach (MethodInfo m in fx.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (m.IsDefined(typeof(T))) m.Invoke(fx, null);
    }

    static Exception Unwrap(Exception e)
    {
        while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        return e;
    }

    static string Describe(Exception e)
    {
        if (e is AssertionException) return e.Message;
        string at = e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains(" in ") || l.Contains("at "))?.Trim() ?? "";
        return $"{e.GetType().Name}: {e.Message} {at}";
    }
}
