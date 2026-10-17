using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The colliders creatures climb on, and the physics scene they live in.
//   MarchingCubes (CreateLocal, play mode): a private local physics scene that holds only these surfaces - the
//     player's and the terrain chunks' colliders are not in it, no layer or project setting changes. Render-only
//     geometry (a plant's bark, any mesh with a CreatureSurfaceMesh) gets a MeshCollider of its own render mesh
//     there (CreateProxy): the real triangles, no capsule or simplified stand-in.
//   Another game (ForScene): its existing physics scene and a layer mask; bounded = false makes every collider
//     in the mask a surface (TerrainColliders, MeshColliders, buildings), with no registration at all.
// Registered surfaces keep their world bounds (the per-creature gate), matrices and collision-mesh data (smooth
// normals, front/back faces). Version changes whenever a surface is added, moved or removed (probe caches key
// on it). Main thread only.
public sealed class CreatureSurfaces : IDisposable
{
    public sealed class Surface
    {
        public Collider collider;
        public int id;
        public Bounds bounds;                       // world AABB
        public Matrix4x4 toWorld, normalToWorld;    // collider transform; inverse transpose for normals
        public SurfaceMeshCache.Entry mesh;         // null for primitive colliders
        public bool proxy;                          // its GameObject belongs to this registry
        public bool registered;                     // false: resolved lazily from a hit (unbounded mode)
    }

    // The registry render-only surfaces register with (set by CreatureWorld).
    public static CreatureSurfaces Current { get; set; }

    public readonly PhysicsScene Scene;
    public readonly int Mask;
    public readonly bool Bounded;
    public readonly int ProxyLayer;

    readonly Scene local;
    readonly bool ownsScene;
    readonly List<Surface> list = new List<Surface>();
    readonly Dictionary<int, Surface> byId = new Dictionary<int, Surface>();
    readonly Collider[] overlap = new Collider[1];
    bool transformsDirty;
    static int sceneCounter;

    public int Version { get; private set; } = 1;
    public bool IsLocal => ownsScene;
    public int Count => list.Count;
    public Surface this[int i] => list[i];

    CreatureSurfaces(PhysicsScene scene, int mask, bool bounded, int proxyLayer, Scene local, bool ownsScene)
    {
        Scene = scene; Mask = mask; Bounded = bounded; ProxyLayer = proxyLayer;
        this.local = local; this.ownsScene = ownsScene;
    }

    // A private physics scene for the surfaces (play mode only: SceneManager.CreateScene throws in edit mode).
    public static CreatureSurfaces CreateLocal(string name = "CreatureSurfaces")
    {
        Scene s = SceneManager.CreateScene(name + " " + (++sceneCounter), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        return new CreatureSurfaces(s.GetPhysicsScene(), Physics.AllLayers, true, 0, s, true);
    }

    // An existing physics scene: the surfaces are the colliders in `mask`. bounded: only registered ones (their
    // AABBs gate the probes); otherwise all of them (the gate asks the scene). Proxies go on proxyLayer.
    public static CreatureSurfaces ForScene(PhysicsScene scene, int mask, bool bounded, int proxyLayer = 0)
        => new CreatureSurfaces(scene, mask, bounded, proxyLayer, default, false);

    // ---- registration ----

    public Surface Register(Collider c, SurfaceMeshCache.Entry mesh = null)
    {
        if (c == null) return null;
        int id = c.GetInstanceID();
        if (byId.TryGetValue(id, out Surface s) && s.registered) { Refresh(s); return s; }
        if (s == null) s = new Surface { collider = c, id = id };
        s.registered = true;
        s.mesh = mesh ?? (c is MeshCollider mc ? SurfaceMeshCache.Get(mc.sharedMesh) : null);
        Refresh(s);
        byId[id] = s;
        list.Add(s);
        Version++;
        return s;
    }

    public void Unregister(Collider c)
    {
        if (c == null) return;
        int id = c.GetInstanceID();
        if (!byId.TryGetValue(id, out Surface s)) return;
        byId.Remove(id);
        list.Remove(s);
        Version++;
    }

    // After a registered collider moved or changed its mesh: new matrices and bounds. Physics sees the move at
    // the next SyncTransforms (CreatureWorld calls SyncIfDirty before ticking).
    public void UpdateTransform(Collider c, SurfaceMeshCache.Entry mesh = null)
    {
        if (c == null || !byId.TryGetValue(c.GetInstanceID(), out Surface s)) return;
        if (mesh != null) s.mesh = mesh;
        Refresh(s);
        transformsDirty = true;
        Version++;
    }

    public void SyncIfDirty()
    {
        if (!transformsDirty) return;
        transformsDirty = false;
        Physics.SyncTransforms();
    }

    void Refresh(Surface s)
    {
        Transform t = s.collider.transform;
        s.toWorld = t.localToWorldMatrix;
        s.normalToWorld = s.toWorld.inverse.transpose;
        if (s.mesh != null && s.mesh.vertices != null) s.bounds = TransformBounds(s.toWorld, s.mesh.bounds);
        else s.bounds = s.collider.bounds;
    }

    static Bounds TransformBounds(Matrix4x4 m, Bounds b)
    {
        Vector3 c = m.MultiplyPoint3x4(b.center), e = b.extents;
        Vector3 x = m.MultiplyVector(new Vector3(e.x, 0f, 0f)), y = m.MultiplyVector(new Vector3(0f, e.y, 0f)), z = m.MultiplyVector(new Vector3(0f, 0f, e.z));
        var ext = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(c, 2f * ext);
    }

    // The surface a hit belongs to: a registered one, or (unbounded mode) one resolved and kept from then on.
    // Lazily resolved surfaces re-read their transform at every hit (another game's colliders may move).
    internal Surface Resolve(in RaycastHit h)
    {
        int id = h.colliderInstanceID;
        if (byId.TryGetValue(id, out Surface s))
        {
            if (!s.registered && s.collider != null) Refresh(s);
            return s;
        }
        Collider c = h.collider;
        if (c == null) return null;
        s = new Surface { collider = c, id = id, mesh = c is MeshCollider mc ? SurfaceMeshCache.Get(mc.sharedMesh) : null };
        Refresh(s);
        byId[id] = s;
        return s;
    }

    // ---- proxies: render-only geometry made collidable with its own mesh ----

    public MeshCollider CreateProxy(string name, SurfaceMeshCache.Entry mesh, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (mesh == null || mesh.mesh == null) return null;
        var go = new GameObject(name) { layer = ProxyLayer };
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        if (ownsScene && local.IsValid()) SceneManager.MoveGameObjectToScene(go, local);
        SurfaceMeshCache.Bake(mesh);
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh.mesh;
        Surface s = Register(mc, mesh);
        s.proxy = true;
        return mc;
    }

    public void MoveProxy(MeshCollider mc, SurfaceMeshCache.Entry mesh, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (mc == null) return;
        Transform t = mc.transform;
        t.SetPositionAndRotation(position, rotation);
        t.localScale = scale;
        if (mesh != null && mc.sharedMesh != mesh.mesh) { SurfaceMeshCache.Bake(mesh); mc.sharedMesh = mesh.mesh; }
        UpdateTransform(mc, mesh);
    }

    public void DestroyProxy(Collider c)
    {
        if (c == null) return;
        Unregister(c);
        if (Application.isPlaying) UnityEngine.Object.Destroy(c.gameObject); else UnityEngine.Object.DestroyImmediate(c.gameObject);
    }

    // ---- queries ----

    // Could a surface lie within b?
    public bool AnySurfaceNear(Bounds b)
    {
        if (Bounded)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].bounds.Intersects(b)) return true;
            return false;
        }
        return Scene.OverlapBox(b.center, b.extents, overlap, Quaternion.identity, Mask, QueryTriggerInteraction.Ignore) > 0;
    }

    // Registered surfaces whose bounds meet b, appended to result.
    public int FindClimbables(Bounds b, List<Surface> result)
    {
        int found = 0;
        for (int i = 0; i < list.Count; i++)
            if (list[i].bounds.Intersects(b)) { result?.Add(list[i]); found++; }
        return found;
    }

    public void Dispose()
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            Surface s = list[i];
            if (s.proxy && s.collider != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(s.collider.gameObject);
                else UnityEngine.Object.DestroyImmediate(s.collider.gameObject);
            }
        }
        list.Clear(); byId.Clear();
        Version++;
        if (ownsScene && local.IsValid() && local.isLoaded) SceneManager.UnloadSceneAsync(local);
        if (Current == this) Current = null;
    }
}
