using CreatureKit;
using UnityEngine;

// A point creatures with the kit's Flee behaviour run from (CreatureKit.CreatureThreats). Put it on anything that
// should scare them (a predator, the player). In play mode one is made automatically for the viewer: a hidden
// object that follows Camera.main, so walking up to the sand eel in the lab makes it flee. Allocation-free per
// frame; main thread. Unity-only (the offline harness drives CreatureThreats directly).
public sealed class CreatureThreatSource : MonoBehaviour
{
    [Tooltip("Follow Camera.main instead of this object's own transform (the viewer).")]
    public bool followMainCamera;

    int slot = -1;

    void OnEnable() => slot = CreatureThreats.Acquire();

    void OnDisable()
    {
        CreatureThreats.Release(slot);
        slot = -1;
    }

    void LateUpdate()
    {
        if (slot < 0) return;
        Transform t = transform;
        if (followMainCamera)
        {
            Camera cam = Camera.main;
            if (cam == null) { CreatureThreats.Release(slot); return; }   // no viewer this frame: no threat (Set re-takes the slot)
            t = cam.transform;
        }
        CreatureThreats.Set(slot, t.position);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateViewerThreat()
    {
        if (FindAnyObjectByType<CreatureThreatSource>() != null) return;
        var go = new GameObject("CreatureThreatSource (viewer)") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<CreatureThreatSource>().followMainCamera = true;
    }
}
