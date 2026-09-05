using UnityEditor;
using UnityEngine;

// The builder already knew when a plant had silently stopped changing -- it
// wrote "Truncated at 250000 modules" into a string field. But a string field
// near the bottom of a default inspector is not communication: raising
// iterations past the cap renders something IDENTICAL to the previous setting,
// which reads as "it refuses to rebuild", and the one line that explained it
// was easy to scroll past.
//
// So the status is a HelpBox at the TOP, coloured by severity, and the counts
// that make the cost obvious sit next to it.
[CustomEditor(typeof(PlantBuilder))]
[CanEditMultipleObjects]
public class PlantBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var b = (PlantBuilder)target;

        DrawStatus(b);
        EditorGUILayout.Space(2);
        DrawStats(b);
        EditorGUILayout.Space(4);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Rebuild")) foreach (var t in targets) ((PlantBuilder)t).Rebuild();
            if (GUILayout.Button("Reroll seed")) foreach (var t in targets) ((PlantBuilder)t).RerollSeed();
            if (GUILayout.Button("Select profile") && b.profile != null) Selection.activeObject = b.profile;
        }
        EditorGUILayout.Space(4);

        DrawDefaultInspector();
    }

    void DrawStatus(PlantBuilder b)
    {
        if (b.profile == null) { EditorGUILayout.HelpBox("No profile assigned.", MessageType.Warning); return; }

        if (b.Truncated)
        {
            EditorGUILayout.HelpBox(
                "TRUNCATED.\n\nThe grammar produced more than " + b.profile.maxModules + " modules, so the "
                + "rewriter fell back to the last COMPLETE generation. Raising iterations past this point "
                + "renders exactly the same plant -- which looks like the builder has stopped working.\n\n"
                + "Either lower the profile's iterations, or raise its maxModules (and accept roughly one "
                + "GameObject per drawn symbol).",
                MessageType.Warning);
            return;
        }

        string s = b.Status;
        if (!string.IsNullOrEmpty(s) && s != "OK")
        {
            EditorGUILayout.HelpBox(s, MessageType.Error);
            return;
        }

        int iters = b.profile.iterations >= 0
            ? b.profile.iterations
            : (b.profile.grammar != null ? b.profile.grammar.EffectiveIterations : 0);
        string src = b.profile.iterations >= 0 ? "profile" : "grammar's #iterations";
        EditorGUILayout.HelpBox("Built. " + iters + " iterations (from the " + src + ").", MessageType.Info);
    }

    void DrawStats(PlantBuilder b)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            int objects = b.SegmentCount + b.PartCount;
            EditorGUILayout.LabelField("word", b.WordModules.ToString("N0") + " modules"
                + (b.profile != null ? "   (cap " + b.profile.maxModules.ToString("N0") + ")" : ""));
            EditorGUILayout.LabelField("stems / parts", b.SegmentCount.ToString("N0") + "  /  " + b.PartCount.ToString("N0"));
            EditorGUILayout.LabelField("GameObjects", objects.ToString("N0")
                + (objects > 40000 ? "   — heavy; expect a sluggish hierarchy" : ""));

            var un = b.UnmappedSymbols;
            if (un != null && un.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < un.Count; i++) { if (i > 0) sb.Append("  "); sb.Append(un[i]); }
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox("Markers with no row in the profile: " + sb
                    + "\nThese are drawn as nothing. Add a row to give them a look.", MessageType.Info);
            }
        }
    }
}
