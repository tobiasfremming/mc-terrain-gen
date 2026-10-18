using System.Collections.Generic;
using UnityEngine;

// Dressing for a flying species' enclosure in the creature lab (design decision 4: an aviary, radius 14 m x
// height 10 m with trees and rock perches): sandstone rock perches (render meshes made climbable/perchable by
// CreatureSurfaceMesh) and slim posts marking the aviary's edge up to its roof. Pure mesh generation plus a
// few GameObjects; built once when the lab places the enclosure.
public static class AviaryDressing
{
    // A boulder mesh (AviaryRock.Geometry), pivot at the base centre.
    public static Mesh RockMesh(Vector3 size, int seed)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        AviaryRock.Geometry(size, seed, verts, tris);
        var m = new Mesh { name = "Aviary rock" };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // A rock perch at `at` (on the ground, up), registered as a perchable surface; its mesh is owned by the
    // returned object (destroy both with it).
    public static GameObject Rock(Transform parent, Vector3 at, Vector3 up, Vector3 size, int seed, Material material, out Mesh mesh)
    {
        mesh = RockMesh(size, seed);
        var go = new GameObject("Rock perch");
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(at - up * (0.08f * size.y), Quaternion.AngleAxis(57f * seed, up) * Quaternion.FromToRotation(Vector3.up, up));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<CreatureSurfaceMesh>();
        return go;
    }

    // Slim posts round the aviary's edge, up to its roof (decoration: no collider).
    public static void Posts(Transform parent, Vector3 center, Vector3 up, float radius, float height, int count, Material material, IGroundProbe ground)
    {
        Vector3 t0 = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 t1 = Vector3.Cross(up, t0);
        for (int k = 0; k < count; k++)
        {
            float a = 2f * Mathf.PI * k / count;
            Vector3 p = center + (t0 * Mathf.Cos(a) + t1 * Mathf.Sin(a)) * (radius + 0.2f);
            if (ground != null && ground.Probe(p + up * 2f, up, 8f, out Vector3 g, out _)) p = g;
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "Aviary post";
            var col = post.GetComponent<Collider>();
            if (col != null) { if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col); }
            post.transform.SetParent(parent, true);
            post.transform.SetPositionAndRotation(p + up * (0.5f * height), Quaternion.FromToRotation(Vector3.up, up));
            post.transform.localScale = new Vector3(0.1f, 0.5f * height, 0.1f);
            if (material != null) post.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
