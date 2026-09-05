using UnityEditor;
using UnityEngine;

// Shows PlantScatter's live stats without them being serialized fields.
//
// They used to be [SerializeField] purely so the default inspector would draw
// them, and that was the bug: rewriting them every frame dirtied the component
// every frame, which made Unity re-sync the serialized object and fire
// OnValidate constantly. Reading them through properties here costs nothing and
// keeps the scene clean.
[CustomEditor(typeof(PlantScatter))]
public class PlantScatterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var s = (PlantScatter)target;

        EditorGUILayout.Space(6);
        string status = s.Status;
        if (string.IsNullOrEmpty(status)) status = "(not run yet)";
        EditorGUILayout.HelpBox(status,
            status == "OK" ? MessageType.Info :
            status.StartsWith("OK") || status.StartsWith("disabled") ? MessageType.Warning : MessageType.Error);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("plots visible", s.VisiblePlots.ToString("N0")
                + (s.PlotsPending > 0 ? "   (" + s.PlotsPending + " still to build)" : ""));
            EditorGUILayout.LabelField("instances", s.Instances.ToString("N0"));
            EditorGUILayout.LabelField("plots built (total)", s.PlotsBuiltTotal.ToString("N0")
                + "   <- must STOP rising when you stand still");
            EditorGUILayout.LabelField("draw calls", s.DrawCalls.ToString("N0")
                + (s.Instances > 0 ? "   (" + (s.Instances / Mathf.Max(1, s.DrawCalls)) + " instances per call)" : ""));
        }

        if (GUILayout.Button("Clear plot cache")) s.ClearCache();

        // Live numbers need a repaint every frame to be worth showing.
        if (Application.isPlaying || s.drawInEditMode) Repaint();
    }
}
