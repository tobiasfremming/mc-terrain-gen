using System.Collections.Generic;
using UnityEngine;

public struct NavAnchor { public Vector3 position; public float radius; public bool primary; }

public struct NavPatch
{
    public int id;
    public Vector3 origin;
    public Quaternion rotation;
    public Bounds localBounds;
}

// Decides where NavMesh instances live. See the integration plan, Task 9.
public sealed class NavPatchPlanner
{
    public float quantum = 32f;
    public float maxFlatExtent = 1024f;
    public float patchSize = 256f;
    public float edgeMargin = 32f;
    public float verticalHalfExtent = 256f;
    public bool radial;
    public Vector3 planetCenter;

    readonly List<NavPatch> _patches = new();
    const int kFlatId = 1;
    int _nextId = kFlatId + 1; // radial ids never collide with the flat patch if the mode flips

    public IReadOnlyList<NavPatch> Plan(IReadOnlyList<NavAnchor> anchors)
    {
        if (!radial) PlanFlat(anchors); else PlanRadial(anchors);
        return _patches;
    }

    void PlanFlat(IReadOnlyList<NavAnchor> anchors)
    {
        _patches.Clear();
        if (anchors.Count == 0) return;

        Vector3 primary = anchors[0].position;
        for (int i = 0; i < anchors.Count; i++) if (anchors[i].primary) { primary = anchors[i].position; break; }
        float half = maxFlatExtent * 0.5f;

        bool any = false;
        Vector3 min = default, max = default;
        foreach (var a in anchors)
        {
            Vector3 lo = a.position - Vector3.one * a.radius, hi = a.position + Vector3.one * a.radius;
            // cap: drop anchors whose sphere leaves the square around the primary
            if (Mathf.Abs(a.position.x - primary.x) + a.radius > half || Mathf.Abs(a.position.z - primary.z) + a.radius > half) continue;
            if (!any) { min = lo; max = hi; any = true; }
            else { min = Vector3.Min(min, lo); max = Vector3.Max(max, hi); }
        }
        if (!any) return;

        min.x = Mathf.Floor(min.x / quantum) * quantum; min.z = Mathf.Floor(min.z / quantum) * quantum;
        max.x = Mathf.Ceil(max.x / quantum) * quantum;  max.z = Mathf.Ceil(max.z / quantum) * quantum;
        float cy = Mathf.Round(primary.y / quantum) * quantum;
        min.y = cy - verticalHalfExtent; max.y = cy + verticalHalfExtent;

        var b = new Bounds((min + max) * 0.5f, max - min);
        _patches.Add(new NavPatch { id = kFlatId, origin = Vector3.zero, rotation = Quaternion.identity, localBounds = b });
    }

    void PlanRadial(IReadOnlyList<NavAnchor> anchors)
    {
        float half = patchSize * 0.5f;
        var next = new List<NavPatch>();
        var assigned = new bool[anchors.Count];

        // 1. keep an existing patch while it holds at least one anchor and none
        //    of its anchors has drifted into the edge margin (hysteresis)
        foreach (var patch in _patches)
        {
            bool holdsAny = false, nearEdge = false;
            var inv = Quaternion.Inverse(patch.rotation);
            for (int i = 0; i < anchors.Count; i++)
            {
                if (assigned[i]) continue;
                Vector3 local = inv * (anchors[i].position - patch.origin);
                float ax = Mathf.Abs(local.x), az = Mathf.Abs(local.z);
                if (ax > half || az > half) continue;           // not this patch's anchor
                holdsAny = true;
                if (ax > half - edgeMargin || az > half - edgeMargin) nearEdge = true;
            }
            if (!holdsAny || nearEdge) continue;                // dropped; its anchors get re-clustered below
            for (int i = 0; i < anchors.Count; i++)
            {
                if (assigned[i]) continue;
                Vector3 local = inv * (anchors[i].position - patch.origin);
                if (Mathf.Abs(local.x) <= half && Mathf.Abs(local.z) <= half) assigned[i] = true;
            }
            next.Add(patch);
        }

        // 2. greedy clusters for the rest
        for (int i = 0; i < anchors.Count; i++)
        {
            if (assigned[i]) continue;
            Vector3 sum = anchors[i].position; int n = 1; assigned[i] = true;
            for (int j = i + 1; j < anchors.Count; j++)
            {
                if (assigned[j]) continue;
                if (Vector3.Distance(anchors[j].position, sum / n) <= half - edgeMargin - anchors[j].radius)
                { sum += anchors[j].position; n++; assigned[j] = true; }
            }
            next.Add(MakeRadial(sum / n));
        }

        // 3. merge overlapping patches (their centres closer than patchSize along the surface)
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int a = 0; a < next.Count && !merged; a++)
            for (int b = a + 1; b < next.Count && !merged; b++)
            {
                if (Vector3.Distance(next[a].origin, next[b].origin) >= patchSize) continue;
                var m = MakeRadial((next[a].origin + next[b].origin) * 0.5f);
                next.RemoveAt(b); next[a] = m; merged = true;
            }
        }

        _patches.Clear();
        _patches.AddRange(next);
    }

    NavPatch MakeRadial(Vector3 worldPoint)
    {
        Vector3 rel = worldPoint - planetCenter;
        float r = rel.magnitude;
        Vector3 up = r > 1e-4f ? rel / r : Vector3.up;
        // quantise the direction so tiny moves reproduce the same frame
        float q = quantum / Mathf.Max(r, 1f);
        Vector3 qUp = new Vector3(Mathf.Round(up.x / q) * q, Mathf.Round(up.y / q) * q, Mathf.Round(up.z / q) * q).normalized;
        Vector3 origin = planetCenter + qUp * Mathf.Round(r / quantum) * quantum;
        var rot = Quaternion.FromToRotation(Vector3.up, qUp);
        // curvature: a patch of half-width h on radius R drops by h^2 / 2R at its edge
        float sag = (patchSize * 0.5f) * (patchSize * 0.5f) / (2f * Mathf.Max(r, 1f));
        var b = new Bounds(Vector3.zero, new Vector3(patchSize, 2f * (verticalHalfExtent + sag), patchSize));
        return new NavPatch { id = _nextId++, origin = origin, rotation = rot, localBounds = b };
    }
}
