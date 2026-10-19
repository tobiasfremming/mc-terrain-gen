using System;
using UnityEngine;

// Shared, engine-free types of the tree octopus's behaviour (design §2.2, §2.5, §3).

// What the brain wants; arms read it and decide for themselves (nothing places tips centrally).
public enum OctoMode { Rest, Travel, Stalk, Strike, Retreat, Hold }

public struct OctoIntent
{
    public OctoMode mode;
    public Vector3 goal;          // Travel / Stalk / Retreat: where the body should go
    public float urgency;         // 0..1: how hard the arms pull and how readily they let go
    public IPreyTarget prey;      // Strike / Hold
    public Vector3 intercept;     // Strike: the predicted prey point at the strike's arrival
    public int strikers;          // Strike: how many arms may qualify themselves (2..4)
    public bool swing;            // (swinging, later) the brain wants to brachiate
    public Vector3 swingDir;      // (swinging, later) the swing's direction
    public Vector3 routeNext;     // (swinging, later) the next hint waypoint; zero = none
    public bool climb;            // Travel / Retreat by hand-over-hand pulling (back up from the ground) instead of swinging
    public bool feed;             // Hold: the mouth tentacles (MouthFeeder) take `prey` when it is within their reach
}

// What an arm is for (user decision 5): the octopus (ArmCoordinator) decides, the arm executes it itself.
//   Rest    curled at rest                  Anchor  hold on (or find a support hold); never pulls the body
//   Grip    grip on the route and pull      Feel    explore around, grips only when allowed (support low)
//   Hang    limp, draped down               Groom   strokes over the mantle or a neighbouring arm
//   Tuck    curled compactly under the body Strike  may strike / hold prey
//   Feed    (mouth tentacles only, MouthFeeder) reach for, grab and eat prey; never an arm's role
public enum ArmRole { Rest, Anchor, Grip, Feel, Hang, Groom, Tuck, Strike, Feed }

// What the arm says back about its role.
public enum ArmReport { None, Busy, Done, Failed }

// One arm's command (the coordinator writes it on the blackboard; the arm reads it every tick).
public struct ArmCommand
{
    public ArmRole role;
    public Vector3 dir;           // Grip / Feel: the direction to work in (unit; zero = the arm's own cone)
    public Vector3 point;         // Groom: (unused yet)
    public int groomArm;          // Groom: the neighbour to stroke, -1 = the mantle
    public int group;             // grip group id (swinging, later), -1 = none
    public float urgency;         // 0..1
    public bool allowGrip;        // Feel: may it commit to a hold it touches (support is low); swing: may it seek a hold now
    public bool swing;            // swinging traversal: the arm works in grip group `group` (ArmCoordinator)
    public bool letGo;            // swing: the body has swung over to the other group's holds - let this hold go
}

// Something the octopus can grab (design 2.5). The prey's own body supplies the grab volume.
public interface IPreyTarget
{
    Vector3 Position { get; }
    Vector3 Velocity { get; }
    // A capsule a..b of radius r around the prey's body.
    void GrabVolume(out Vector3 a, out Vector3 b, out float radius);
    bool CanBeGrabbed { get; }
    void OnGrabbed();
    void OnReleased();
}

// Planning hints from plant skeletons (perception only: every hint is confirmed by a probe before an arm
// commits). A null IBranchHints means probe-only (SpaceGame).
public struct BranchHint
{
    public Vector3 point;     // on the skeleton's centre line
    public Vector3 axis;      // the branch direction
    public float radius;      // the skeleton's branch radius there
    public float score;       // lower is better (distance-weighted)
}

public interface IBranchHints
{
    // Up to results.Length hints whose centre-line point lies in [minDist, maxDist] of origin, inside the
    // cone (unit axis, cos of the half angle) and with a radius in [minRadius, maxRadius], best first.
    int Query(Vector3 origin, Vector3 coneAxis, float coneCos, float minDist, float maxDist, float minRadius, float maxRadius, BranchHint[] results);
}

// Events of one octopus (the brain raises them; lab / game code listens).
public sealed class OctopusEvents
{
    public event Action<IPreyTarget> Strike, Grabbed, Released;
    // The prey was eaten (MouthFeeder, after the grab): the owner removes it. No release follows.
    public event Action<IPreyTarget> Eaten;
    internal void RaiseEaten(IPreyTarget p) => Eaten?.Invoke(p);
    internal void RaiseStrike(IPreyTarget p) => Strike?.Invoke(p);
    internal void RaiseGrabbed(IPreyTarget p) => Grabbed?.Invoke(p);
    internal void RaiseReleased(IPreyTarget p) => Released?.Invoke(p);
}
