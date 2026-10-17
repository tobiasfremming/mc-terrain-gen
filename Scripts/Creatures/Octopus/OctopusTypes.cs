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
    internal void RaiseStrike(IPreyTarget p) => Strike?.Invoke(p);
    internal void RaiseGrabbed(IPreyTarget p) => Grabbed?.Invoke(p);
    internal void RaiseReleased(IPreyTarget p) => Released?.Invoke(p);
}
