using UnityEngine;

// Makes a render-only object climbable with its own render mesh: a MeshCollider of MeshFilter.sharedMesh (or
// only its submesh 0, e.g. a plant's bark) in the creatures' surface registry, following the object when it
// moves. No authoring beyond adding the component. An object that already has its own collider in a registry
// that uses the object's own physics scene (another game: ForScene) is registered as it is instead.
[DisallowMultipleComponent]
public sealed class CreatureSurfaceMesh : MonoBehaviour
{
    [Tooltip("Climb only submesh 0 (a plant's bark: its leaves are not climbable). Off: the whole mesh.")]
    public bool firstSubmeshOnly;

    CreatureSurfaces registry;
    MeshCollider proxy;
    Collider own;
    Matrix4x4 placed;

    void OnEnable() => TryRegister();

    void LateUpdate()
    {
        if (registry != CreatureSurfaces.Current) { Release(); TryRegister(); return; }
        if (proxy == null && own == null) { TryRegister(); return; }
        Matrix4x4 m = transform.localToWorldMatrix;
        if (m == placed) return;
        placed = m;
        if (proxy != null) registry.MoveProxy(proxy, null, transform.position, transform.rotation, transform.lossyScale);
        else registry.UpdateTransform(own);
    }

    void TryRegister()
    {
        registry = CreatureSurfaces.Current;
        if (registry == null) return;
        if (!registry.IsLocal && TryGetComponent(out Collider c) && c.enabled && !c.isTrigger)
        {
            own = c;
            registry.Register(c);
            placed = transform.localToWorldMatrix;
            return;
        }
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        SurfaceMeshCache.Entry e = firstSubmeshOnly ? SurfaceMeshCache.BarkOnly(mf.sharedMesh) : SurfaceMeshCache.Get(mf.sharedMesh);
        proxy = registry.CreateProxy(name + " (surface)", e, transform.position, transform.rotation, transform.lossyScale);
        placed = transform.localToWorldMatrix;
    }

    void Release()
    {
        if (registry != null)
        {
            if (proxy != null) registry.DestroyProxy(proxy);
            if (own != null) registry.Unregister(own);
        }
        proxy = null; own = null; registry = null;
    }

    void OnDisable() => Release();
}
