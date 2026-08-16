using UnityEngine;

// Sets up desert atmosphere when entering play mode in a terrain scene:
// distance fog (sells the scale AND hides far clipmap ring pop-in) and a far
// clip plane that actually reaches the clipmap horizon. Values are only
// applied if the scene hasn't already enabled fog itself, so hand-tuned
// lighting settings win.
public static class DesertAtmosphere
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        var manager = Object.FindFirstObjectByType<MCChunkManager>();
        if (manager == null) return;

        if (!RenderSettings.fog)
        {
            RenderSettings.fog = true;
            // ExpSq haze reads far more naturally than linear at these scales:
            // ~25% at 500m, ~65% at 1km, terrain fully merged by the horizon.
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.001f;
            // Keep the haze close to the horizon sky so distant dunes blend
            // into the sky instead of showing a hard silhouette edge.
            RenderSettings.fogColor = new Color(0.80f, 0.76f, 0.78f);
            Debug.Log("[DesertAtmosphere] Fog enabled (ExpSq, density 0.001). Tune in this script, or set fog yourself in Lighting > Environment and this will back off.");
        }

        var cam = Camera.main;
        if (cam != null)
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 2500f);
    }
}
