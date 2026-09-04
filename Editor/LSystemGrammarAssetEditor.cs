using System.Collections.Generic;
using System.Text;
using LSystems;
using UnityEditor;
using UnityEngine;

// A readable inspector for a grammar.
//
// The asset is still one string, deliberately: cpfg text is the format the
// literature is written in, and splitting it into serialized fields would mean
// grammars from the book no longer paste in. So instead of restructuring the
// DATA, this restructures the VIEW -- monospace source, errors that name their
// line, every rule listed as it was written, and an inventory of the symbols
// the grammar actually uses.
//
// Lives in Assembly-CSharp-Editor (a predefined assembly, which auto-references
// every asmdef) so it needs no asmdef of its own, and so nothing consumer-shaped
// lands inside Scripts/LSystem.
[CustomEditor(typeof(LSystemGrammarAsset))]
public class LSystemGrammarAssetEditor : Editor
{
    // From the turtle command table in Scripts/LSystem/README.md. Anything a
    // grammar uses that is NOT here and has no rule of its own reaches the
    // skeleton as a marker -- which is exactly what a PlantProfile maps.
    const string kTurtleCommands = "FGfg+-&^\\/|$[]!\"";

    static GUIStyle _mono;
    static GUIStyle Mono
    {
        get
        {
            if (_mono == null)
            {
                _mono = new GUIStyle(EditorStyles.textArea) { wordWrap = false, richText = false };
                Font f = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New" }, 12);
                if (f != null) _mono.font = f;
            }
            return _mono;
        }
    }

    bool _showSource = true;
    bool _showRules = true;
    bool _showSymbols = true;
    Vector2 _sourceScroll;
    string _derivation;

    public override void OnInspectorGUI()
    {
        var asset = (LSystemGrammarAsset)target;
        serializedObject.Update();

        DrawStatus(asset);
        EditorGUILayout.Space(2);
        DrawSource(asset);
        EditorGUILayout.Space(4);
        DrawParsed(asset);
        EditorGUILayout.Space(4);
        DrawSymbols(asset);
        EditorGUILayout.Space(4);
        DrawTermination(asset);

        serializedObject.ApplyModifiedProperties();
    }

    void DrawStatus(LSystemGrammarAsset asset)
    {
        LSystemGrammar g = asset.Grammar;
        if (!asset.IsValid || g == null)
        {
            var errs = asset.Errors;
            if (errs != null && errs.Count > 0)
                foreach (var e in errs)
                    EditorGUILayout.HelpBox("line " + e.Line + ", col " + e.Column + "  —  " + e.Message, MessageType.Error);
            else
                EditorGUILayout.HelpBox("Grammar does not parse.", MessageType.Error);
            return;
        }

        var bits = new List<string>
        {
            g.Productions.Length + (g.Productions.Length == 1 ? " rule" : " rules"),
            "iterations " + asset.EffectiveIterations + (g.DefaultIterations >= 0 ? " (from #iterations)" : " (from the field below)"),
        };
        if (g.IsStochastic) bits.Add("stochastic");
        if (g.HasContext) bits.Add("context-sensitive");
        EditorGUILayout.HelpBox("Parses.  " + string.Join("  ·  ", bits.ToArray()), MessageType.Info);
    }

    void DrawSource(LSystemGrammarAsset asset)
    {
        SerializedProperty src = serializedObject.FindProperty("source");

        using (new EditorGUILayout.HorizontalScope())
        {
            _showSource = EditorGUILayout.Foldout(_showSource, "Grammar source", true, EditorStyles.foldoutHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Add rule", EditorStyles.miniButtonLeft, GUILayout.Width(70)))
                Append(src, "\n// new rule -- predecessor : condition : weight -> successor\nA(l,w) : l >= MIN -> F(l) A(l*SH, w*TP)\n");
            if (GUILayout.Button("Add define", EditorStyles.miniButtonMid, GUILayout.Width(80)))
                Append(src, "\n#define NAME 1.0\n");
            if (GUILayout.Button("Add branch", EditorStyles.miniButtonRight, GUILayout.Width(80)))
                Append(src, "\n// a branch: everything inside [ ] happens, then the turtle snaps back\n// [ &(ANGLE) /(137.5) A(l*SH, w*TP) ]\n");
        }
        if (!_showSource) return;

        // Height follows the content, so a long grammar is not read through a
        // four-line slot.
        int lines = 1;
        string text = src.stringValue ?? "";
        for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
        float h = Mathf.Clamp(lines * (Mono.lineHeight + 1f) + 8f, 120f, 620f);

        _sourceScroll = EditorGUILayout.BeginScrollView(_sourceScroll, GUILayout.Height(h));
        EditorGUI.BeginChangeCheck();
        string edited = EditorGUILayout.TextArea(text, Mono, GUILayout.ExpandHeight(true));
        if (EditorGUI.EndChangeCheck())
        {
            src.stringValue = edited;
            serializedObject.ApplyModifiedProperties();
            ((LSystemGrammarAsset)target).Invalidate();
            _derivation = null;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("iterations"));
    }

    void Append(SerializedProperty src, string snippet)
    {
        src.stringValue = (src.stringValue ?? "").TrimEnd() + "\n" + snippet;
        serializedObject.ApplyModifiedProperties();
        ((LSystemGrammarAsset)target).Invalidate();
        _derivation = null;
    }

    void DrawParsed(LSystemGrammarAsset asset)
    {
        LSystemGrammar g = asset.Grammar;
        if (g == null) return;

        _showRules = EditorGUILayout.Foldout(_showRules, "Rules, as parsed", true, EditorStyles.foldoutHeader);
        if (!_showRules) return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("axiom", asset.Grammar.Axiom != null ? asset.Grammar.Axiom.ToDisplayString(2) : "(none)");

            if (g.Defines != null && g.Defines.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var kv in g.Defines) { if (sb.Length > 0) sb.Append("   "); sb.Append(kv.Key).Append(" = ").Append(kv.Value.ToString("0.###")); }
                EditorGUILayout.LabelField("defines", sb.ToString());
            }

            EditorGUILayout.Space(2);

            // The parsed structure says WHAT each rule is; SourceLine gives the
            // text it was written as. Showing the original line rather than a
            // reconstruction means what you read is what you wrote -- no
            // pretty-printer to disagree with the source.
            string[] lines = (asset.source ?? "").Replace("\r\n", "\n").Split('\n');
            var byLine = new List<LProduction>(g.Productions);
            byLine.Sort((a, b) => a.SourceLine.CompareTo(b.SourceLine));

            foreach (var p in byLine)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var tag = new StringBuilder();
                    tag.Append(p.Symbol);
                    if (p.ParamCount > 0) tag.Append('(').Append(p.ParamCount).Append(')');
                    if (p.HasContext) tag.Append(" ctx");
                    if (p.Condition != null) tag.Append(" if");
                    if (p.Weight != 1f) tag.Append(" p=").Append(p.Weight.ToString("0.##"));

                    GUILayout.Label(tag.ToString(), EditorStyles.miniBoldLabel, GUILayout.Width(110));
                    string text = (p.SourceLine - 1) >= 0 && (p.SourceLine - 1) < lines.Length
                        ? lines[p.SourceLine - 1].Trim()
                        : "(line " + p.SourceLine + ")";
                    GUILayout.Label(text, Mono);
                }
            }
        }
    }

    void DrawSymbols(LSystemGrammarAsset asset)
    {
        LSystemGrammar g = asset.Grammar;
        if (g == null) return;

        _showSymbols = EditorGUILayout.Foldout(_showSymbols, "Symbols this grammar uses", true, EditorStyles.foldoutHeader);
        if (!_showSymbols) return;

        var produced = new SortedSet<char>();
        if (g.Axiom != null)
            for (int i = 0; i < g.Axiom.Count; i++) produced.Add(g.Axiom[i].Symbol);
        var rewritten = new SortedSet<char>();
        foreach (var p in g.Productions)
        {
            rewritten.Add(p.Symbol);
            if (p.Successor == null) continue;
            foreach (var m in p.Successor) produced.Add(m.Symbol);
        }

        var turtle = new List<char>();
        var nonTerminals = new List<char>();
        var markers = new List<char>();
        foreach (char c in produced)
        {
            if (rewritten.Contains(c)) nonTerminals.Add(c);
            else if (kTurtleCommands.IndexOf(c) >= 0) turtle.Add(c);
            else markers.Add(c);
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("turtle commands", Join(turtle), Mono);
            EditorGUILayout.LabelField("rewritten (non-terminal)", Join(nonTerminals), Mono);
            EditorGUILayout.LabelField("markers", Join(markers), Mono);
        }

        if (markers.Count > 0)
            EditorGUILayout.HelpBox("Markers reach the skeleton untouched. Give each one a row in a PlantProfile "
                + "(or handle it in your own consumer) to decide what it looks like: " + Join(markers), MessageType.Info);
    }

    static string Join(List<char> cs)
    {
        if (cs.Count == 0) return "—";
        var sb = new StringBuilder();
        foreach (char c in cs) { if (sb.Length > 0) sb.Append("  "); sb.Append(c); }
        return sb.ToString();
    }

    // The single most common way a grammar goes wrong, straight from
    // COOKBOOK.md: Rewrite(g, N) emits the N-th generation but never rewrites
    // it, so a grammar needing 7 shrink steps needs #iterations: 8. Left over
    // apex symbols then reach the turtle and turn into stray markers.
    void DrawTermination(LSystemGrammarAsset asset)
    {
        LSystemGrammar g = asset.Grammar;
        if (g == null) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Check termination", GUILayout.Width(140)))
                _derivation = Derive(asset, g);
            GUILayout.Label(_derivation ?? "derives the word and reports anything left unrewritten", EditorStyles.miniLabel);
        }
    }

    static string Derive(LSystemGrammarAsset asset, LSystemGrammar g)
    {
        var rewritten = new HashSet<char>();
        foreach (var p in g.Productions) rewritten.Add(p.Symbol);

        var rw = new LSystemRewriter();
        LModuleString word = rw.Rewrite(g, asset.EffectiveIterations, 12345u);

        var leftover = new SortedSet<char>();
        for (int i = 0; i < word.Count; i++)
        {
            char s = word[i].Symbol;
            if (rewritten.Contains(s)) leftover.Add(s);
        }

        string head = word.Count + " modules";
        if (rw.Truncated) return head + " — TRUNCATED at " + rw.MaxModules + ". Lower #iterations.";
        if (leftover.Count == 0) return head + " — terminates cleanly.";

        var sb = new StringBuilder(head + " — still unrewritten: ");
        foreach (char c in leftover) sb.Append(c).Append(' ');
        sb.Append(" → raise #iterations by one.");
        return sb.ToString();
    }
}
