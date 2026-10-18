using UnityEngine;

// A planned route over the branches to a goal (user, 2026-10-18: "it needs a goal and then check points in the
// branches it can go to"). A* over a graph built on the fly from the plant skeleton hints (IBranchHints:
// perception and planning only - every waypoint is confirmed by a probe before an arm commits to it):
//   nodes  hint points (on a branch's centre line), merged within MergeLengths x the arm length
//   edges  from a node to the hints MinStep..MaxStep arm lengths away (one swing: the next hold within an
//          arm's reach of the body hanging under the current one), at most MaxRise up or down
//          on branches no steeper than ~45 deg (a trunk is no swing hold)
//   cost   length + RisePenalty x |rise| (a level route swings; a steep one climbs) + a per-hold cost
//          (fewer, longer swings), heuristic = the distance to the goal
// The route is the chain of waypoints from the start to the node nearest the goal (within ArriveLengths, or
// the best reached when the hints run out). No hints (SpaceGame): no route - the arms fall back to searching
// along the straight line to the goal.
//
// Pure C#, no allocation after construction; the hint queries are counted (Queries) and bounded (MaxQueries).
public sealed class BranchRoute
{
    public const int Capacity = 128, MaxWaypoints = 24, MaxQueries = 48;
    public const float MinStep = 0.4f, MaxStep = 1.1f, MaxRise = 0.6f, RisePenalty = 1.5f, HoldCost = 0.3f, MergeLengths = 0.25f;
    public const float ArriveLengths = 0.6f;
    public const float MaxAxisRise = 0.7f;   // |branch axis . up| above this (steeper than ~45 deg) is a stem, not a hold

    readonly Vector3[] nodeP = new Vector3[Capacity];
    readonly float[] nodeG = new float[Capacity], nodeF = new float[Capacity];
    readonly int[] nodeParent = new int[Capacity];
    readonly bool[] nodeClosed = new bool[Capacity];
    readonly BranchHint[] scratch = new BranchHint[10], stems = new BranchHint[4];
    int nodeCount;

    public readonly Vector3[] Waypoints = new Vector3[MaxWaypoints];
    public int Count { get; private set; }
    public int Current;                       // the waypoint being worked toward
    public Vector3 Goal { get; private set; }
    public Vector3 Start { get; private set; }
    public bool Valid => Count > 0;
    public bool ReachesGoal { get; private set; }
    public int Queries { get; private set; }  // of the last plan
    public int Plans;                         // cumulative

    public bool Done => Current >= Count;
    public Vector3 Next => Count == 0 ? Goal : Waypoints[Mathf.Min(Current, Count - 1)];

    public void Clear() { Count = 0; Current = 0; ReachesGoal = false; }

    // Plans from start (the holds the body hangs from) to goal. False (and an empty route) without hints.
    public bool Plan(IBranchHints hints, Vector3 start, Vector3 goal, Vector3 up, float L, float minRadius)
    {
        Clear();
        Goal = goal; Start = start;
        Plans++;
        Queries = 0; StemChecks = 0;
        if (hints == null || !(L > 0f)) return false;
        nodeCount = 0;
        int s = AddNode(start, -1, 0f, goal);
        int best = s; float bestH = Level(goal - start, up).magnitude;
        float merge2 = MergeLengths * MergeLengths * L * L;
        while (Queries < MaxQueries)
        {
            // the open node with the lowest f
            int cur = -1; float cf = float.PositiveInfinity;
            for (int i = 0; i < nodeCount; i++) if (!nodeClosed[i] && nodeF[i] < cf) { cf = nodeF[i]; cur = i; }
            if (cur < 0) break;
            nodeClosed[cur] = true;
            Vector3 p = nodeP[cur];
            // (lazy: the swing onto a node is checked for stems only when the node is expanded - checking every
            // candidate edge cost ~200 queries a plan)
            if (cur > 0 && StemInPath(hints, nodeP[nodeParent[cur]], p, up, L, nodeParent[cur] == 0)) continue;
            float h = Level(goal - p, up).magnitude;
            if (h < bestH) { bestH = h; best = cur; }
            if (h < ArriveLengths * L) { best = cur; ReachesGoal = true; break; }
            Vector3 toGoal = OctoMath.Norm(goal - p, up);
            Queries++;
            int n = hints.Query(p, toGoal, -0.2f, MinStep * L, MaxStep * L, minRadius, 2f * L, scratch);
            for (int k = 0; k < n; k++)
            {
                Vector3 q = scratch[k].point;
                // a trunk (or any steep stem) is no swing hold: the body hanging under it would lean on it
                if (Mathf.Abs(OctoMath.Dot(scratch[k].axis, up)) > MaxAxisRise) continue;
                float rise = OctoMath.Dot(q - p, up);
                if (Mathf.Abs(rise) > MaxRise * L) continue;
                float g = nodeG[cur] + (q - p).magnitude + RisePenalty * Mathf.Abs(rise) + HoldCost * L;
                int dup = -1;
                for (int i = 0; i < nodeCount; i++) if ((nodeP[i] - q).sqrMagnitude < merge2) { dup = i; break; }
                if (dup >= 0)
                {
                    if (!nodeClosed[dup] && g < nodeG[dup]) { nodeG[dup] = g; nodeF[dup] = g + (goal - nodeP[dup]).magnitude; nodeParent[dup] = cur; }
                    continue;
                }
                if (nodeCount >= Capacity) break;
                AddNode(q, cur, g, goal);
            }
        }
        // the chain best -> start, reversed (the start itself is where the body already is)
        int len = 0;
        for (int i = best; i > 0 && nodeParent[i] >= 0; i = nodeParent[i]) len++;
        len = Mathf.Min(len, MaxWaypoints);
        int w = len - 1;
        for (int i = best; i > 0 && nodeParent[i] >= 0 && w >= 0; i = nodeParent[i]) Waypoints[w--] = nodeP[i];
        Count = len;
        Current = 0;
        return Count > 0;
    }

    public const float HangLengths = 0.7f, StemClearance = 0.35f;
    public int StemChecks;

    // fromStart: the body already hangs at p (beside a trunk, perhaps): only the hang under q is checked
    bool StemInPath(IBranchHints hints, Vector3 p, Vector3 q, Vector3 up, float L, bool fromStart)
    {
        StemChecks++;
        // the hang under the next hold, and halfway there
        for (int pass = 0; pass < (fromStart ? 1 : 2); pass++)
        {
            Vector3 at = (pass == 0 ? q : 0.5f * (p + q)) - up * (HangLengths * L);
            int n = hints.Query(at, up, -1f, 0f, StemClearance * L, 0f, 4f * L, stems);
            for (int i = 0; i < n; i++) if (Mathf.Abs(OctoMath.Dot(stems[i].axis, up)) > MaxAxisRise) return true;
        }
        return false;
    }

    int AddNode(Vector3 p, int parent, float g, Vector3 goal)
    {
        int i = nodeCount++;
        nodeP[i] = p; nodeParent[i] = parent; nodeG[i] = g; nodeF[i] = g + (goal - p).magnitude; nodeClosed[i] = false;
        return i;
    }

    static Vector3 Level(Vector3 v, Vector3 up) => v - up * OctoMath.Dot(v, up);

    // Moves Current on past every waypoint the holds have reached (within reachLengths x L of `at`, or no longer
    // ahead of it along the route). Returns how many were passed.
    public int Advance(Vector3 at, Vector3 up, float L, float reachLengths)
    {
        int passed = 0;
        while (Current < Count)
        {
            Vector3 w = Waypoints[Current];
            Vector3 next = Current + 1 < Count ? Waypoints[Current + 1] : Goal;
            Vector3 dir = Level(next - w, up);
            bool near = (w - at).magnitude < reachLengths * L;
            bool behind = dir.sqrMagnitude > 1e-6f && OctoMath.Dot(at - w, dir) > 0f && Level(at - w, up).magnitude < MaxStep * L;
            if (!near && !behind) break;
            Current++; passed++;
        }
        return passed;
    }
}
