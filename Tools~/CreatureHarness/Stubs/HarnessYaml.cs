// A reader for the subset of Unity's YAML that ScriptableObject assets use, and a binder that fills an object
// by Unity's serialization rules (public or [SerializeField] instance fields, not [NonSerialized]; nested
// [Serializable] classes/structs; arrays and List<T>; Vector*/Quaternion/Color as {x: .., y: ..}; bools 0/1;
// enums as ints; object references -> null). Fields missing from the file keep their C# initialiser, as in
// Unity. Keys with no matching field are reported (Unknown) - the asset is stale or misspelt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

public static class HarnessYaml
{
    // ------------------------------------------------------------------ public API

    public sealed class Report { public readonly List<string> Unknown = new List<string>(); public readonly List<string> Notes = new List<string>(); }

    static readonly Dictionary<string, Report> reports = new Dictionary<string, Report>(StringComparer.OrdinalIgnoreCase);

    // Keys in the file that no serialized field takes, as "path.to.key" (empty when the asset is clean).
    public static IReadOnlyList<string> UnknownKeys(string fullPath) => reports.TryGetValue(Path.GetFullPath(fullPath), out Report r) ? r.Unknown : (IReadOnlyList<string>)Array.Empty<string>();

    public static object Load(string fullPath, Type type, Report report)
    {
        report = report ?? new Report();
        reports[Path.GetFullPath(fullPath)] = report;
        Map doc = ParseDocument(File.ReadAllLines(fullPath));
        if (doc == null || doc.Items.Count == 0) throw new InvalidDataException(fullPath + ": empty YAML document");
        // the single root key: MonoBehaviour
        Map body = doc.Items[0].Value as Map ?? throw new InvalidDataException(fullPath + ": root is not a mapping");
        object target = New(type);
        foreach (var kv in body.Items)
        {
            if (kv.Key == "m_Name")
            {
                // RealUnity mode: Object.name is engine-side; the object stays nameless (tests reading it report NATIVE)
                if (target is UnityEngine.Object uo) { try { uo.name = Scalar(kv.Value); } catch (Exception e) when (IsNative(e)) { report.Notes.Add("name not set (native)"); } }
                continue;
            }
            if (kv.Key.StartsWith("m_", StringComparison.Ordinal)) continue;   // Unity bookkeeping
            FieldInfo f = Field(type, kv.Key);
            if (f == null) { report.Unknown.Add(kv.Key); continue; }
            f.SetValue(target, Bind(f.FieldType, kv.Value, kv.Key, report, f.GetValue(target)));
        }
        return target;
    }

    // The C# type of the asset's MonoBehaviour: m_Script's GUID looked up in the .cs.meta files under Scripts/.
    public static Type ScriptType(string fullPath)
    {
        string guid = null, ident = null;
        foreach (string line in File.ReadLines(fullPath))
        {
            if (guid == null) { Match m = Regex.Match(line, @"^\s*m_Script:\s*\{.*guid:\s*([0-9a-f]+)"); if (m.Success) guid = m.Groups[1].Value; }
            if (ident == null) { Match m = Regex.Match(line, @"^\s*m_EditorClassIdentifier:\s*\S*::(\S+)"); if (m.Success) ident = m.Groups[1].Value; }
            if (guid != null && ident != null) break;
        }
        string cls = null;
        if (guid != null && ScriptGuids().TryGetValue(guid, out string file)) cls = file;
        cls = cls ?? ident;
        if (cls == null) return null;
        foreach (Type t in typeof(HarnessYaml).Assembly.GetTypes()) if (t.Name == cls && t.Namespace == null) return t;
        foreach (Type t in typeof(HarnessYaml).Assembly.GetTypes()) if (t.Name == cls) return t;
        return null;
    }

    static Dictionary<string, string> guidToClass;
    static Dictionary<string, string> ScriptGuids()
    {
        if (guidToClass != null) return guidToClass;
        guidToClass = new Dictionary<string, string>();
        foreach (string meta in Directory.GetFiles(Path.Combine(HarnessPaths.TerrainGenRoot, "Scripts"), "*.cs.meta", SearchOption.AllDirectories))
            foreach (string line in File.ReadLines(meta))
            {
                Match m = Regex.Match(line, @"^guid:\s*([0-9a-f]+)");
                if (m.Success) { guidToClass[m.Groups[1].Value] = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meta)); break; }
            }
        return guidToClass;
    }

    // ------------------------------------------------------------------ binding

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static FieldInfo Field(Type t, string name)
    {
        for (; t != null && t != typeof(object); t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null && Serialized(f)) return f;
        }
        return null;
    }

    static bool Serialized(FieldInfo f)
    {
        if (f.IsStatic || f.IsInitOnly || f.IsLiteral) return false;
        if (f.IsDefined(typeof(NonSerializedAttribute), false)) return false;
        return f.IsPublic || f.IsDefined(typeof(UnityEngine.SerializeField), false);
    }

    internal static object New(Type t)
    {
        if (t.IsValueType) return Activator.CreateInstance(t);
        try { return Activator.CreateInstance(t, true); }
        catch (Exception e) when (IsNative(e))
        {
            // RealUnity mode: UnityEngine.Object constructors call into the engine. The object is created without
            // its constructor (field initialisers do not run, so a field missing from the file reads 0).
            return RuntimeHelpers.GetUninitializedObject(t);
        }
    }

    internal static bool IsNative(Exception e)
    {
        for (; e != null; e = e.InnerException)
            if (e is System.Security.SecurityException || e is MissingMethodException || e is EntryPointNotFoundException
                || e is DllNotFoundException || e is MethodAccessException || e is TypeInitializationException
                || e.GetType().Name == "ECallMethodNotFoundException" || (e.Message ?? "").Contains("ECall")) return true;
        return false;
    }

    static string Scalar(object node)
    {
        if (node is string s) return s;
        if (node == null) return "";
        throw new InvalidDataException("expected a scalar, got " + node.GetType().Name);
    }

    static object Bind(Type t, object node, string path, Report rep, object existing)
    {
        if (t == typeof(string)) return node is string s0 ? s0 : "";
        if (t == typeof(float)) return ParseFloat(Scalar(node));
        if (t == typeof(double)) return (double)ParseFloat(Scalar(node));
        if (t == typeof(int)) return int.Parse(Scalar(node), Inv);
        if (t == typeof(uint)) return uint.Parse(Scalar(node), Inv);
        if (t == typeof(long)) return long.Parse(Scalar(node), Inv);
        if (t == typeof(short)) return short.Parse(Scalar(node), Inv);
        if (t == typeof(byte)) return byte.Parse(Scalar(node), Inv);
        if (t == typeof(bool)) return Scalar(node) != "0";
        if (t.IsEnum) return Enum.ToObject(t, long.Parse(Scalar(node), Inv));
        if (typeof(UnityEngine.Object).IsAssignableFrom(t))
        {
            if (node is Map rm && rm.Get("fileID") is string id && id != "0") rep.Notes.Add(path + ": object reference not resolved offline (null)");
            return null;
        }
        if (t.IsArray)
        {
            Type e = t.GetElementType();
            List<object> items = Seq(node, path);
            Array a = Array.CreateInstance(e, items.Count);
            for (int i = 0; i < items.Count; i++) a.SetValue(Bind(e, items[i], $"{path}[{i}]", rep, null), i);
            return a;
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            Type e = t.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(t);
            List<object> items = Seq(node, path);
            for (int i = 0; i < items.Count; i++) list.Add(Bind(e, items[i], $"{path}[{i}]", rep, null));
            return list;
        }
        // a [Serializable] class or struct (Vector3, Color, ... included: their fields are x/y/z/w and r/g/b/a)
        Map map = node as Map;
        object o = existing ?? New(t);
        if (map == null)
        {
            if (node is string s && s.Length == 0) return o;   // "key:" with nothing under it
            throw new InvalidDataException($"{path}: expected a mapping for {t.Name}, got '{node}'");
        }
        foreach (var kv in map.Items)
        {
            FieldInfo f = Field(t, kv.Key);
            if (f == null) { rep.Unknown.Add(path + "." + kv.Key); continue; }
            f.SetValue(o, Bind(f.FieldType, kv.Value, path + "." + kv.Key, rep, f.FieldType.IsValueType ? null : f.GetValue(o)));
        }
        return o;
    }

    static List<object> Seq(object node, string path)
    {
        if (node is List<object> l) return l;
        if (node is string s && (s.Length == 0)) return new List<object>();
        throw new InvalidDataException($"{path}: expected a sequence, got '{node}'");
    }

    static float ParseFloat(string s)
    {
        switch (s)
        {
            case "Infinity": case ".inf": case "+Infinity": return float.PositiveInfinity;
            case "-Infinity": case "-.inf": return float.NegativeInfinity;
            case "NaN": case ".nan": return float.NaN;
        }
        return float.Parse(s, NumberStyles.Float, Inv);
    }

    // ------------------------------------------------------------------ YAML reader (block style + flow {}, [])

    public sealed class Map
    {
        public readonly List<KeyValuePair<string, object>> Items = new List<KeyValuePair<string, object>>();
        public object Get(string k) { foreach (var kv in Items) if (kv.Key == k) return kv.Value; return null; }
    }

    struct Line { public int indent; public string text; public int no; }

    static Map ParseDocument(string[] raw)
    {
        var lines = new List<Line>();
        for (int i = 0; i < raw.Length; i++)
        {
            string r = raw[i];
            if (r.StartsWith("%") || r.StartsWith("---")) continue;
            string tt = r.TrimEnd();
            if (tt.Trim().Length == 0 || tt.TrimStart().StartsWith("#")) continue;
            int ind = 0; while (ind < tt.Length && tt[ind] == ' ') ind++;
            lines.Add(new Line { indent = ind, text = tt.Substring(ind), no = i + 1 });
        }
        int pos = 0;
        return ParseMap(lines, ref pos, 0);
    }

    static readonly Regex KeyRx = new Regex(@"^([^\s:'""{}\[\]-][^:]*?|-[^\s][^:]*?):(?:\s+(.*))?$");

    static Map ParseMap(List<Line> L, ref int pos, int indent)
    {
        var map = new Map();
        while (pos < L.Count && L[pos].indent == indent && !IsSeqItem(L[pos].text))
        {
            Match m = KeyRx.Match(L[pos].text);
            if (!m.Success) throw new InvalidDataException($"line {L[pos].no}: cannot read '{L[pos].text}'");
            string key = m.Groups[1].Value.Trim();
            string rest = m.Groups[2].Success ? m.Groups[2].Value : "";
            pos++;
            map.Items.Add(new KeyValuePair<string, object>(key, Value(L, ref pos, indent, rest)));
        }
        return map;
    }

    // The value after "key:" (rest) or under it.
    static object Value(List<Line> L, ref int pos, int indent, string rest)
    {
        if (rest.Length > 0) return InlineScalarOrFlow(L, ref pos, indent, rest);
        if (pos < L.Count && IsSeqItem(L[pos].text) && L[pos].indent >= indent) return ParseSeq(L, ref pos, L[pos].indent);
        if (pos < L.Count && L[pos].indent > indent) return ParseMap(L, ref pos, L[pos].indent);
        return "";
    }

    static bool IsSeqItem(string t) => t == "-" || t.StartsWith("- ");

    static List<object> ParseSeq(List<Line> L, ref int pos, int indent)
    {
        var list = new List<object>();
        while (pos < L.Count && L[pos].indent == indent && IsSeqItem(L[pos].text))
        {
            string after = L[pos].text.Length > 1 ? L[pos].text.Substring(2) : "";
            int itemIndent = indent + 2;
            if (after.Length == 0) { pos++; list.Add(Value(L, ref pos, indent, "")); continue; }
            Match m = KeyRx.Match(after);
            if (m.Success && !after.StartsWith("{") && !after.StartsWith("'") && !after.StartsWith("\""))
            {
                // "- key: v" opens a mapping whose further keys sit at indent + 2
                L[pos] = new Line { indent = itemIndent, text = after, no = L[pos].no };
                list.Add(ParseMap(L, ref pos, itemIndent));
            }
            else { pos++; list.Add(InlineScalarOrFlow(L, ref pos, indent, after)); }
        }
        return list;
    }

    static object InlineScalarOrFlow(List<Line> L, ref int pos, int indent, string rest)
    {
        string t = rest.Trim();
        if (t.StartsWith("{")) { int i = 0; return Flow(t, ref i); }
        if (t.StartsWith("[")) { int i = 0; return Flow(t, ref i); }
        if (t.StartsWith("'") || t.StartsWith("\""))
        {
            // quoted, possibly continued on deeper lines
            var sb = new StringBuilder(t);
            char q = t[0];
            while (!ClosedQuote(sb.ToString(), q) && pos < L.Count && L[pos].indent > indent) { sb.Append(' ').Append(L[pos].text.Trim()); pos++; }
            return Unquote(sb.ToString());
        }
        // a plain scalar may fold onto deeper lines
        var plain = new StringBuilder(t);
        while (pos < L.Count && L[pos].indent > indent && !IsSeqItem(L[pos].text) && !KeyRx.IsMatch(L[pos].text)) { plain.Append(' ').Append(L[pos].text.Trim()); pos++; }
        return plain.ToString();
    }

    static bool ClosedQuote(string s, char q)
    {
        if (s.Length < 2) return false;
        if (q == '\'')
        {
            int i = 1;
            while (i < s.Length) { if (s[i] == '\'') { if (i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; } return i == s.Length - 1; } i++; }
            return false;
        }
        for (int i = 1; i < s.Length; i++) { if (s[i] == '\\') { i++; continue; } if (s[i] == '"') return i == s.Length - 1; }
        return false;
    }

    static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '\'') return s.Substring(1, s.Length - 2).Replace("''", "'");
        if (s.Length >= 2 && s[0] == '"') return Regex.Unescape(s.Substring(1, s.Length - 2));
        return s;
    }

    static object Flow(string s, ref int i)
    {
        Skip(s, ref i);
        if (s[i] == '{')
        {
            var map = new Map(); i++;
            while (true)
            {
                Skip(s, ref i);
                if (s[i] == '}') { i++; return map; }
                int c = s.IndexOf(':', i);
                string key = s.Substring(i, c - i).Trim(); i = c + 1;
                map.Items.Add(new KeyValuePair<string, object>(key, Flow(s, ref i)));
                Skip(s, ref i);
                if (s[i] == ',') i++;
            }
        }
        if (s[i] == '[')
        {
            var list = new List<object>(); i++;
            while (true)
            {
                Skip(s, ref i);
                if (s[i] == ']') { i++; return list; }
                list.Add(Flow(s, ref i));
                Skip(s, ref i);
                if (s[i] == ',') i++;
            }
        }
        int start = i;
        while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']') i++;
        return Unquote(s.Substring(start, i - start).Trim());
    }

    static void Skip(string s, ref int i) { while (i < s.Length && s[i] == ' ') i++; }
}
