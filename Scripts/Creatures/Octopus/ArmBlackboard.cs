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
    // what each arm is for (ArmCoordinator writes, the arms read) and what they report back
    public readonly ArmCommand[] Command;
    public readonly ArmRole[] Role;
    public readonly ArmReport[] Report;
    public readonly float[] RoleSince;
    public readonly int[] Group;
    // The body motion the octopus wants this tick (m/s; zero = hold still). Every anchored arm sets its run's
    // length from the same wanted root position (root + DesiredVelocity dt): the grip arms ahead shorten,
    // the holds behind pay out, and no two arms ask for different body positions (no tug-of-war).
    public Vector3 DesiredVelocity;
    public int Puller = -1;
    // Swinging (ArmCoordinator): the run length (x the rest spacing, as LengthTarget) each anchored swing arm
    // holds this tick - the pivot group's locked (and pumped) length, the catching group's slack-free follow -
    // 0 = not swinging; the group the body hangs from, the holds' centroid and the level route.
    public readonly float[] SwingLen;
    public int PivotGroup = -1;
    public Vector3 SwingPivot, SwingRoute;   // the pivot's front hold (along the route), the route direction (level)
    public Vector3 SwingCentre;              // the centroid of the holds the body hangs from
    public Vector3 SupportCentre;            // where the pivot group's own search for support centres (beside its holds)
    public Vector3 SwingTarget;              // the route's next waypoint (the catch the arms reach for)
    public bool HasSwingTarget;
    // Catches only at the waypoint (false: the swing has found nothing there for a while - any hold ahead on the
    // way to it will do, ArmCoordinator.StuckSeconds)
    public bool SwingTargetStrict = true;
    public readonly bool[] Pulling;                  // the one arm that pulls the body this tick (-1: none)
    public int CommittedSupport { get; private set; }   // anchored arms whose role keeps them holding (Anchor, Grip)
    public int AnchoredCount { get; private set; }
    public int StrikeClaims { get; private set; }
    public bool Grounded;                    // the body rests on a surface (set by the controller)
    public int MinAnchorsHanging = 2;        // support needed while hanging (off the ground)

    public ArmBlackboard(int arms)
    {
        Arms = arms;
        Anchored = new bool[arms]; Anchor = new Vector3[arms]; Load = new float[arms];
        Claiming = new bool[arms]; ClaimPoint = new Vector3[arms]; Striking = new bool[arms];
        Pulling = new bool[arms]; SwingLen = new float[arms];
        Command = new ArmCommand[arms]; Role = new ArmRole[arms]; Report = new ArmReport[arms]; RoleSince = new float[arms]; Group = new int[arms];
        for (int i = 0; i < arms; i++) { Group[i] = -1; Command[i].groomArm = -1; Command[i].group = -1; }
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
        int a = 0, s = 0, k = 0;
        for (int i = 0; i < Arms; i++)
        {
            if (Anchored[i]) { a++; if (Role[i] == ArmRole.Anchor || Role[i] == ArmRole.Grip) k++; }
            if (Striking[i]) s++;
        }
        AnchoredCount = a; StrikeClaims = s; CommittedSupport = k;
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
