using System;
using UnityEngine;

// Things the octopus can grab (design 2.5), adapted to IPreyTarget. The grab volume always comes from the
// prey's own body. Lab default: a grab holds 1.5 s, then releases; no damage. A grabbed creature struggles,
// then flees.

// Prey that need a per-frame sample (velocity from motion).
public interface IPreySampled { void Sample(float dt); }

// A creature's brain while it is prey: its own brain as usual; while held it struggles (short darting
// targets around where it is); after a release it flees directly away from the octopus for FleeSeconds.
// The sim's enclosure clamp still holds it inside its area.
public sealed class PreyBrain : ICreatureBrain
{
    public const float FleeSeconds = 4f;
    readonly ICreatureBrain inner;
    uint rng;
    float fleeUntil, time, nextDart;
    Vector3 dart, fleeFrom;
    public bool Held { get; internal set; }
    public bool Fleeing => time < fleeUntil;

    public PreyBrain(ICreatureBrain inner, int seed) { this.inner = inner; rng = (uint)seed * 2246822519u + 1u; }

    float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFF) / 65536f; }

    internal void Released(Vector3 from) { Held = false; fleeFrom = from; fleeUntil = time + FleeSeconds; }

    public Vector3 Target(CreatureSim sim, float dt)
    {
        time += dt;
        Vector3 head = sim.Joints[0];
        if (Held)
        {
            if (time >= nextDart)
            {
                nextDart = time + 0.15f + 0.2f * Rand();
                float a = Rand() * 2f * Mathf.PI;
                dart = head + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.4f * sim.SizeScale);
            }
            return dart;
        }
        if (Fleeing)
        {
            Vector3 away = head - fleeFrom; away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
            return head + away.normalized * (3f * sim.SizeScale);
        }
        return inner != null ? inner.Target(sim, dt) : head;
    }
}

// A creature of the world as prey: its spine is the grab capsule (head joint to last joint, the widest
// half-width as radius).
public sealed class CreaturePrey : IPreyTarget, IPreySampled
{
    public readonly CreatureSim Sim;
    public readonly PreyBrain Brain;
    public Func<Vector3> GrabberPosition;   // where it flees from (the octopus head)
    Vector3 last, vel;
    bool sampled;
    float cooldownUntil, time;
    readonly float radius;

    public CreaturePrey(CreatureSim sim, PreyBrain brain)
    {
        Sim = sim; Brain = brain;
        float r = 0f;
        CreatureSpecies sp = sim.Species;
        for (int i = 0; i < sp.JointCount; i++) r = Mathf.Max(r, sp.joints[i].halfWidth);
        radius = r * sim.SizeScale;
    }

    public Vector3 Position => Sim.Joints[Sim.Joints.Length / 2];
    public Vector3 Velocity => vel;

    public void GrabVolume(out Vector3 a, out Vector3 b, out float r)
    {
        Vector3[] j = Sim.Joints;
        a = j[0]; b = j[j.Length - 1]; r = radius;
    }

    public bool CanBeGrabbed => !Brain.Held && time >= cooldownUntil;
    public void OnGrabbed() { Brain.Held = true; }
    public void OnReleased()
    {
        Brain.Released(GrabberPosition != null ? GrabberPosition() : Position);
        cooldownUntil = time + PreyBrain.FleeSeconds + 2f;
    }

    public void Sample(float dt)
    {
        time += dt;
        Vector3 p = Position;
        if (sampled && dt > 0f) vel = (p - last) / dt;
        last = p; sampled = true;
    }
}

// The player as prey: the CharacterController's capsule. A grab only raises events (the lab logs them).
public sealed class PlayerPrey : IPreyTarget
{
    readonly Transform t;
    readonly CharacterController cc;
    public int Grabs, Releases;
    public PlayerPrey(Transform player) { t = player; cc = player != null ? player.GetComponent<CharacterController>() : null; }

    public Vector3 Position => t != null ? t.position + (cc != null ? cc.center : Vector3.up) : Vector3.zero;
    public Vector3 Velocity => cc != null ? cc.velocity : Vector3.zero;

    public void GrabVolume(out Vector3 a, out Vector3 b, out float r)
    {
        if (t == null) { a = b = Vector3.zero; r = 0f; return; }
        float h = cc != null ? cc.height : 1.8f, rad = cc != null ? cc.radius : 0.35f;
        Vector3 c = t.position + (cc != null ? cc.center : Vector3.up * 0.9f), up = t.up;
        float half = Mathf.Max(0f, 0.5f * h - rad);
        a = c - up * half; b = c + up * half; r = rad;
    }

    public bool CanBeGrabbed => t != null && t.gameObject.activeInHierarchy;
    public void OnGrabbed() { Grabs++; }
    public void OnReleased() { Releases++; }
}
