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
    bool _hasPlanned;
    bool _prevRadial;

    public IReadOnlyList<NavPatch> Plan(IReadOnlyList<NavAnchor> anchors)
    {
        // A stale patch from the other mode must never leak into this mode's
        // output -- PlanRadial's keep/merge logic only makes sense for
        // patches PlanRadial itself produced (see Task 9 review, finding 3).
        // PlanFlat already clears unconditionally, so this only matters
        // flat->radial, but clearing both ways keeps the rule symmetric.
        if (_hasPlanned && _prevRadial != radial) _patches.Clear();
        _prevRadial = radial;
        _hasPlanned = true;

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
        float minY = default, maxY = default;
        foreach (var a in anchors)
        {
            Vector3 lo = a.position - Vector3.one * a.radius, hi = a.position + Vector3.one * a.radius;
            // cap: drop anchors whose sphere leaves the square around the primary --
            // except the primary itself, which must always ground (Task 11's guard
            // assumes it always has a patch; see Task 9 review, finding 4).
            if (!a.primary && (Mathf.Abs(a.position.x - primary.x) + a.radius > half || Mathf.Abs(a.position.z - primary.z) + a.radius > half)) continue;
            if (!any) { min = lo; max = hi; minY = maxY = a.position.y; any = true; }
            else
            {
                min = Vector3.Min(min, lo); max = Vector3.Max(max, hi);
                minY = Mathf.Min(minY, a.position.y); maxY = Mathf.Max(maxY, a.position.y);
            }
        }
        if (!any) return;

        min.x = Mathf.Floor(min.x / quantum) * quantum; min.z = Mathf.Floor(min.z / quantum) * quantum;
        max.x = Mathf.Ceil(max.x / quantum) * quantum;  max.z = Mathf.Ceil(max.z / quantum) * quantum;
        // Vertical range spans every INCLUDED anchor's y, not just the primary's
        // (a grounded NPC anchor far above/below the primary must still fit;
        // see Task 9 review, finding 2).
        min.y = Mathf.Floor((minY - verticalHalfExtent) / quantum) * quantum;
        max.y = Mathf.Ceil((maxY + verticalHalfExtent) / quantum) * quantum;

        var b = new Bounds((min + max) * 0.5f, max - min);
        _patches.Add(new NavPatch { id = kFlatId, origin = Vector3.zero, rotation = Quaternion.identity, localBounds = b });
    }

    void PlanRadial(IReadOnlyList<NavAnchor> anchors)
    {
        float half = patchSize * 0.5f;
        var next = new List<NavPatch>();
        // Member anchor world positions per patch in `next`, same index --
        // NOT stored on the NavPatch struct, only used here to re-validate a
        // candidate merge (see step 3, and Task 9 review finding 1).
        var members = new List<List<Vector3>>();
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
            var mem = new List<Vector3>();
            for (int i = 0; i < anchors.Count; i++)
            {
                if (assigned[i]) continue;
                Vector3 local = inv * (anchors[i].position - patch.origin);
                if (Mathf.Abs(local.x) <= half && Mathf.Abs(local.z) <= half) { assigned[i] = true; mem.Add(anchors[i].position); }
            }
            next.Add(patch);
            members.Add(mem);
        }

        // 2. greedy clusters for the rest
        for (int i = 0; i < anchors.Count; i++)
        {
            if (assigned[i]) continue;
            Vector3 sum = anchors[i].position; int n = 1; assigned[i] = true;
            var mem = new List<Vector3> { anchors[i].position };
            for (int j = i + 1; j < anchors.Count; j++)
            {
                if (assigned[j]) continue;
                if (Vector3.Distance(anchors[j].position, sum / n) <= half - edgeMargin - anchors[j].radius)
                { sum += anchors[j].position; n++; assigned[j] = true; mem.Add(anchors[j].position); }
            }
            next.Add(MakeRadial(sum / n));
            members.Add(mem);
        }

        // 3. merge overlapping patches (their centres closer than patchSize along the
        //    surface), but only commit the merge if every member anchor of BOTH
        //    patches still fits -- with edgeMargin headroom -- inside the merged
        //    patch's box. A member sitting near one patch's edge can otherwise land
        //    outside a naive midpoint-recentred merge (Task 9 review, finding 1); in
        //    that case leave both patches unmerged instead of stranding it.
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int a = 0; a < next.Count && !merged; a++)
            for (int b = a + 1; b < next.Count && !merged; b++)
            {
                if (Vector3.Distance(next[a].origin, next[b].origin) >= patchSize) continue;

                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var m in members[a]) { sum += m; n++; }
                foreach (var m in members[b]) { sum += m; n++; }
                var candidate = RadialFrame(sum / n);
                var inv = Quaternion.Inverse(candidate.rotation);
                bool fits = true;
                foreach (var m in members[a]) { Vector3 l = inv * (m - candidate.origin); if (Mathf.Abs(l.x) > half - edgeMargin || Mathf.Abs(l.z) > half - edgeMargin) { fits = false; break; } }
                if (fits) foreach (var m in members[b]) { Vector3 l = inv * (m - candidate.origin); if (Mathf.Abs(l.x) > half - edgeMargin || Mathf.Abs(l.z) > half - edgeMargin) { fits = false; break; } }
                if (!fits) continue; // leave both patches as they are

                candidate.id = _nextId++;
                var mergedMembers = new List<Vector3>(members[a]);
                mergedMembers.AddRange(members[b]);
                next.RemoveAt(b); members.RemoveAt(b);
                next[a] = candidate; members[a] = mergedMembers;
                merged = true;
            }
        }

        _patches.Clear();
        _patches.AddRange(next);
    }

    // Origin/rotation/bounds for a radial patch centred at worldPoint -- no id
    // assigned, so a rejected merge candidate (see PlanRadial step 3) never
    // burns an id.
    NavPatch RadialFrame(Vector3 worldPoint)
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
        return new NavPatch { origin = origin, rotation = rot, localBounds = b };
    }

    NavPatch MakeRadial(Vector3 worldPoint)
    {
        var p = RadialFrame(worldPoint);
        p.id = _nextId++;
        return p;
    }
}
