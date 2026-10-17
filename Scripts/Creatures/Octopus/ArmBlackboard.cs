using UnityEngine;

// What the arms know about each other (tree octopus design 2.2): where the others are anchored, how loaded
// they are, what they are reaching for, who is striking. Arms read it to score their own choices (spacing,
// support); nothing on it tells an arm what to do.
public sealed class ArmBlackboard
{
    public readonly int Arms;
    public readonly bool[] Anchored;
    public readonly Vector3[] Anchor;        // centroid of the arm's pins
    public readonly float[] Load;            // how taut the arm is (root-to-anchor distance / its arc)
    public readonly bool[] Claiming;         // reaching for ClaimPoint
    public readonly Vector3[] ClaimPoint;
    public readonly bool[] Striking;
    public int AnchoredCount { get; private set; }
    public int StrikeClaims { get; private set; }
    public bool Grounded;                    // the body rests on a surface (set by the controller)
    public int MinAnchorsHanging = 2;        // support needed while hanging (off the ground)

    public ArmBlackboard(int arms)
    {
        Arms = arms;
        Anchored = new bool[arms]; Anchor = new Vector3[arms]; Load = new float[arms];
        Claiming = new bool[arms]; ClaimPoint = new Vector3[arms]; Striking = new bool[arms];
    }

    // resting on a surface still keeps one hold: touching a thin branch with the body is not standing on it
    public int MinAnchors => Grounded ? 1 : MinAnchorsHanging;
    // Support margin: anchors beyond the minimum.
    public int SupportMargin => AnchoredCount - MinAnchors;

    // Local inhibition (like the gait-group rule): an anchored arm may let go only if the support that
    // remains still meets the minimum.
    public bool CanRelease(int arm) => !Anchored[arm] || AnchoredCount - 1 >= MinAnchors;

    public void Recount()
    {
        int a = 0, s = 0;
        for (int i = 0; i < Arms; i++) { if (Anchored[i]) a++; if (Striking[i]) s++; }
        AnchoredCount = a; StrikeClaims = s;
    }

    // How crowded p is by the other arms' anchors and claims within minDist (0 = free, 1 = on top of one).
    public float Crowding(int arm, Vector3 p, float minDist)
    {
        float worst = 0f, inv = 1f / Mathf.Max(minDist, 1e-4f);
        for (int i = 0; i < Arms; i++)
        {
            if (i == arm) continue;
            if (Anchored[i]) worst = Mathf.Max(worst, 1f - (Anchor[i] - p).magnitude * inv);
            if (Claiming[i]) worst = Mathf.Max(worst, 1f - (ClaimPoint[i] - p).magnitude * inv);
        }
        return Mathf.Clamp01(worst);
    }
}
