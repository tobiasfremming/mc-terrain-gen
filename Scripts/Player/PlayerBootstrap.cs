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
            manager.worldConfig && manager.worldConfig.EffectiveDensity ? manager.worldConfig.EffectiveDensity :
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
        var ctrl = player.AddComponent<SimplePlayerController>(); // RequireComponent auto-adds GravityAligner + CharacterGravity
        ctrl.cameraTransform = cam.transform;
        var charGravity = player.GetComponent<CharacterGravity>();
        charGravity.densityField = density;
        var gravity = player.GetComponent<GravityAligner>();
        gravity.terrain = manager;
        if (!player.GetComponent<FootprintEmitter>())
            player.AddComponent<FootprintEmitter>();
        if (!player.GetComponent<TerrainDigger>())
        {
            var digger = player.AddComponent<TerrainDigger>();
            digger.aimSource = cam.transform;
        }

        // --- spawn ---
        // Planet mode: drop from well above the tallest possible terrain
        // instead of trying to find the exact surface -- the wrapped field
        // isn't necessarily a pure heightfield (canyon/alien carve caves and
        // overhangs), so landing exactly on "the surface" along one ray risks
        // spawning inside a mountain's flank or under an overhang. Falling
        // onto it is simple and robust; CharacterGravity's existing "no
        // collider below yet -> hold position" check keeps this safe even
        // before nearby chunks have streamed in.
        //
        // Position AND orientation are set directly here so the player is
        // correctly oriented from frame one; gravity.ResetOrientation() below
        // resyncs GravityAligner's persisted forward to match (otherwise its
        // first Apply() would rebuild rotation from whatever Awake captured
        // instead of the spawn rotation just set here).
        Vector3 p = player.transform.position;
        string surfaceDesc;
        if (manager.TryGetPlanetSpawnPoint(out Vector3 center, out float spawnRadius))
        {
            Vector3 dir = (p - center).sqrMagnitude > 1e-6f ? (p - center).normalized : Vector3.up;
            player.transform.SetPositionAndRotation(center + dir * spawnRadius, Quaternion.FromToRotation(Vector3.up, dir));
            surfaceDesc = $"dropping from radius {spawnRadius:F1}";
        }
        else
        {
            float surfaceY = CharacterGravity.SampleSurfaceHeight(p.x, p.z, density);
            player.transform.SetPositionAndRotation(
                new Vector3(p.x, surfaceY + 2f, p.z),
                Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f));
            surfaceDesc = $"surface {surfaceY:F1}";
        }
        gravity.ResetOrientation(); // transform.forward was just set directly above; resync GravityAligner to it

        // terrain follows the player from now on
        manager.target = player.transform;

        Debug.Log($"[PlayerBootstrap] Player ready at {player.transform.position} " +
                  $"({surfaceDesc}). WASD move, mouse look, Space jump, Shift sprint, Esc frees cursor.");
    }
}
