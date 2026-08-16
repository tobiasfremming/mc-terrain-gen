using UnityEngine;

// Auto-wires a playable first-person character when entering play mode, so the
// scene doesn't need manual setup: adds a CharacterController + controller to
// the "Player" object (creating one if missing), parents the camera to it,
// points the chunk manager's target at it and spawns it on the terrain surface.
//
// It does nothing if the scene already contains a SimplePlayerController or a
// TerrainTarget (e.g. on your own player controller), so you can wire things
// up by hand -- in the editor, or with your own character -- and this steps aside.
public static class PlayerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsurePlayer()
    {
        if (Object.FindFirstObjectByType<SimplePlayerController>() != null) return;
        if (Object.FindFirstObjectByType<TerrainTarget>() != null) return;

        var manager = Object.FindFirstObjectByType<MCChunkManager>();
        if (manager == null) return; // not a terrain scene

        DensityField density =
            manager.worldConfig && manager.worldConfig.defaultDensity ? manager.worldConfig.defaultDensity :
            manager.chunkPrefab ? manager.chunkPrefab.densityField : null;

        // --- player object ---
        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
        }

        // The CharacterController is the player's collider; disable any others.
        foreach (var col in player.GetComponents<Collider>())
            if (!(col is CharacterController)) col.enabled = false;

        var rend = player.GetComponent<MeshRenderer>();
        if (rend) rend.enabled = false; // hide the capsule in first person

        var cc = player.GetComponent<CharacterController>();
        if (cc == null) cc = player.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.45f;
        cc.center = Vector3.zero;
        cc.slopeLimit = 50f;
        cc.stepOffset = 0.4f;

        // --- camera ---
        Camera cam = player.GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        cam.transform.SetParent(player.transform, false);
        cam.transform.localPosition = new Vector3(0f, 0.7f, 0f); // eye height
        cam.transform.localRotation = Quaternion.identity;
        cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, 0.1f);

        // --- controller + footprint trail + digging ---
        var ctrl = player.AddComponent<SimplePlayerController>();
        ctrl.cameraTransform = cam.transform;
        ctrl.densityField = density;
        if (!player.GetComponent<FootprintEmitter>())
            player.AddComponent<FootprintEmitter>();
        if (!player.GetComponent<TerrainDigger>())
        {
            var digger = player.AddComponent<TerrainDigger>();
            digger.aimSource = cam.transform;
        }

        // --- spawn on the surface, upright ---
        Vector3 p = player.transform.position;
        float surfaceY = SimplePlayerController.SampleSurfaceHeight(p.x, p.z, density);
        player.transform.SetPositionAndRotation(
            new Vector3(p.x, surfaceY + 2f, p.z),
            Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f));

        // terrain follows the player from now on
        manager.target = player.transform;

        Debug.Log($"[PlayerBootstrap] Player ready at {player.transform.position} " +
                  $"(surface {surfaceY:F1}). WASD move, mouse look, Space jump, Shift sprint, Esc frees cursor.");
    }
}
