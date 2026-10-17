using System.Collections.Generic;
using LSystems;
using UnityEngine;

// Branch hints from plant skeletons (tree octopus design §3, user decision 1: perception and planning only).
// Trees come from two places:
//   - explicit plants (the lab's grove, a CreatureSurfaceMesh tree): Register(set, variant, trs);
//   - the PlantScatter around the octopus: Refresh(bounds) re-reads GetClimbables every RefreshSeconds and
//     takes each instance's skeleton (PlantScatter.GetSkeleton, cached per variant).
// Each tree's skeleton segments are transformed to world space once (when it is first seen) into flat
// arrays; Query scans the trees whose bounds come near the query (allocation-free).
public sealed class BranchHints : IBranchHints
{
    public const float RefreshSeconds = 0.5f;

    sealed class Tree
    {
        public PlantPrototypeSet set; public int variant; public Matrix4x4 trs; public bool registered;
        public Vector3[] a, b; public float[] r; public Bounds bounds; public bool seen;
    }

    readonly List<Tree> trees = new List<Tree>();
    readonly List<PlantScatter.Climbable> scratch = new List<PlantScatter.Climbable>();
    readonly PlantScatter scatter;
    float nextRefresh;

    public BranchHints(PlantScatter scatter = null) { this.scatter = scatter; }

    public int TreeCount => trees.Count;

    // An explicit plant (a lab tree): its skeleton in world space.
    public bool Register(PlantPrototypeSet set, int variant, Matrix4x4 trs)
    {
        LSkeleton skel = PlantSkeletons.For(set, variant);
        if (skel == null) return false;
        trees.Add(Build(set, variant, trs, skel, set.source, true));
        return true;
    }

    // Scatter instances near `area`; trees from the scatter that are no longer reported are dropped.
    public void Refresh(Bounds area, float time)
    {
        if (scatter == null || time < nextRefresh) return;
        nextRefresh = time + RefreshSeconds;
        scratch.Clear();
        scatter.GetClimbables(area, scratch);
        for (int i = 0; i < trees.Count; i++) trees[i].seen = trees[i].registered;
        for (int c = 0; c < scratch.Count; c++)
        {
            PlantScatter.Climbable cl = scratch[c];
            int found = -1;
            for (int i = 0; i < trees.Count; i++)
                if (!trees[i].registered && trees[i].variant == cl.variant && (trees[i].trs.GetColumn(3) - (Vector4)cl.trs.GetColumn(3)).sqrMagnitude < 1e-6f) { found = i; break; }
            if (found >= 0) { trees[found].seen = true; continue; }
            LSkeleton skel = scatter.GetSkeleton(cl.species, cl.variant);
            if (skel == null) continue;
            Tree t = Build(null, cl.variant, cl.trs, skel, scatter.ProfileOf(cl.species), false);
            t.seen = true;
            trees.Add(t);
        }
        for (int i = trees.Count - 1; i >= 0; i--) if (!trees[i].seen) trees.RemoveAt(i);
    }

    // Segment radius as PlantMeshBuilder sweeps it: max(minStemRadius, 0.5 x width x stemRadiusScale).
    static Tree Build(PlantPrototypeSet set, int variant, Matrix4x4 trs, LSkeleton skel, PlantProfile profile, bool registered)
    {
        int n = skel.Segments.Count;
        var t = new Tree { set = set, variant = variant, trs = trs, registered = registered, a = new Vector3[n], b = new Vector3[n], r = new float[n] };
        float scale = trs.lossyScale.x;
        Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
        for (int i = 0; i < n; i++)
        {
            LSkeletonSegment s = skel.Segments[i];
            t.a[i] = trs.MultiplyPoint3x4(skel.Nodes[s.From].Position);
            t.b[i] = trs.MultiplyPoint3x4(skel.Nodes[s.To].Position);
            float w = 0.5f * (s.WidthFrom + s.WidthTo);
            float rad = profile != null ? Mathf.Max(profile.minStemRadius, 0.5f * w * profile.stemRadiusScale) : 0.5f * w;
            t.r[i] = rad * scale;
            lo = Vector3.Min(lo, Vector3.Min(t.a[i], t.b[i])); hi = Vector3.Max(hi, Vector3.Max(t.a[i], t.b[i]));
        }
        t.bounds = new Bounds(); t.bounds.SetMinMax(lo, hi);
        return t;
    }

    public int Query(Vector3 origin, Vector3 coneAxis, float coneCos, float minDist, float maxDist, float minRadius, float maxRadius, BranchHint[] results)
    {
        int count = 0, cap = results.Length;
        for (int ti = 0; ti < trees.Count; ti++)
        {
            Tree t = trees[ti];
            if (t.bounds.SqrDistance(origin) > maxDist * maxDist) continue;
            for (int i = 0; i < t.a.Length; i++)
            {
                float r = t.r[i];
                if (r < minRadius || r > maxRadius) continue;
                Vector3 a = t.a[i], ab = t.b[i] - a;
                float l2 = ab.sqrMagnitude;
                float u = l2 > 1e-12f ? Mathf.Clamp01(Vector3.Dot(origin - a, ab) / l2) : 0f;
                Vector3 p = a + ab * u, d = p - origin;
                float dist = d.magnitude;
                if (dist < minDist || dist > maxDist || dist < 1e-6f) continue;
                if (Vector3.Dot(d, coneAxis) < coneCos * dist) continue;
                float score = dist * (2f - Vector3.Dot(d / dist, coneAxis));
                if (count == cap && score >= results[cap - 1].score) continue;
                int at = count < cap ? count++ : cap - 1;
                while (at > 0 && results[at - 1].score > score) { results[at] = results[at - 1]; at--; }
                results[at] = new BranchHint { point = p, axis = l2 > 1e-12f ? ab / Mathf.Sqrt(l2) : Vector3.up, radius = r, score = score };
            }
        }
        return count;
    }
}
