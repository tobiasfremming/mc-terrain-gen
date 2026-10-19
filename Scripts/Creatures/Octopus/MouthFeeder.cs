using UnityEngine;

// The mouth tentacles' behaviour (two-armed swinger design, 2026-10-19): they only feed. They never hold the body
// and never take an arm role; the arms keep hanging on while they work.
//
//   Idle    curled at rest around the beak
//   Reach   the intent says feed (OctoIntent.feed) and the prey lies within Reach of the mouth: the tentacles
//           nearest it reach for the nearest point of its grab volume, slowly (a creeping animal's last move);
//           a tip that touches it grabs (OctopusController.NotifyGrab: brain, events, prey)
//   Eat     every tentacle closes on the prey and draws it to the beak for EatSeconds x TimeScale; then the
//           Eaten event fires (the owner removes the prey: a grab-and-eat, no gore) and the tentacles curl again
//
// Roles for the debug readout: Feed while reaching or eating, Rest otherwise. Pure C#, no allocation, no queries.
public sealed class MouthFeeder
{
    public enum State { Idle, Reach, Eat }

    public const float ReachFraction = 1f;         // x mouth length x MaxStretch: how far from the mouth centre prey can be taken
    public const float Reachers = 3;               // tentacles that reach for the prey (the nearest)
    public const float EatSeconds = 2.5f;          // x TimeScale
    public const float GrabMargin = 0.015f;        // metres x sizeScale beyond the prey's radius
    public const float ReachTone = 2f, ReachPull = 0.12f;
    // 1/s at scale 1: idle, a tentacle's velocity relative to the body's motion is damped this hard (TentacleChain.
    // CalmDamping); at the chain's 1.2/s the light tentacles flapped as the body swung (tip p95 16 L/s, 160 reversals/min)
    public const float IdleDamping = 8f;

    readonly OctopusBody body;
    readonly float[] dist;
    readonly int[] order;
    public State Mode { get; private set; }
    public IPreyTarget Prey { get; private set; }
    public int Grabber { get; private set; } = -1;
    public int Grabs, Eats;                        // cumulative
    float eatUntil;

    public MouthFeeder(OctopusBody body)
    {
        this.body = body;
        dist = new float[body.MouthCount]; order = new int[body.MouthCount];
    }

    public int Count => body.MouthCount;
    public bool Feeding => Mode != State.Idle;
    public ArmRole RoleOf(int m) => Mode == State.Idle ? ArmRole.Rest : ArmRole.Feed;

    // Metres: the farthest prey (its grab volume) the mouth can take from its centre.
    public float Reach
    {
        get
        {
            TentacleChain m = body.Mouth[0];
            return ReachFraction * m.Length * m.Spec.MaxStretch;
        }
    }

    // The mouth's centre: the mean of the tentacle roots (the beak).
    public Vector3 Centre
    {
        get
        {
            float x = 0f, y = 0f, z = 0f;
            for (int m = 0; m < body.MouthCount; m++) { Vector3 p = body.Mouth[m].X[0]; x += p.x; y += p.y; z += p.z; }
            float i = 1f / Mathf.Max(1, body.MouthCount);
            return new Vector3(x * i, y * i, z * i);
        }
    }

    // Distance from the mouth's centre to the prey's grab volume (its surface).
    public float DistanceTo(IPreyTarget p)
    {
        p.GrabVolume(out Vector3 a, out Vector3 b, out float r);
        Vector3 c = Centre;
        return (c - Closest(c, a, b)).magnitude - r;
    }

    public bool InReach(IPreyTarget p) => p != null && p.CanBeGrabbed && DistanceTo(p) <= Reach;

    public void Tick(float dt, OctopusController c)
    {
        if (body.MouthCount == 0) return;
        OctoIntent it = c.Intent;
        switch (Mode)
        {
            case State.Idle:
                if (it.feed && InReach(it.prey)) { Mode = State.Reach; Prey = it.prey; }
                break;
            case State.Reach:
                if (!it.feed || it.prey != Prey || Prey == null || !Prey.CanBeGrabbed || DistanceTo(Prey) > 1.3f * Reach) { Relax(); break; }
                ReachFor(c);
                break;
            case State.Eat:
                Close();
                if (c.Time >= eatUntil)
                {
                    Eats++;
                    IPreyTarget p = Prey;
                    Relax();
                    c.NotifyEaten(p);
                }
                break;
        }
        if (Mode != State.Idle) body.Wake();
        // idle, the tentacles hold themselves curled about the beak and move with the body (no flapping as it swings)
        float calm = Mode == State.Idle ? IdleDamping : 0f;
        for (int m = 0; m < body.MouthCount; m++) body.Mouth[m].CalmDamping = calm;
    }

    void ReachFor(OctopusController c)
    {
        Prey.GrabVolume(out Vector3 a, out Vector3 b, out float r);
        float s = body.SizeScale;
        for (int m = 0; m < body.MouthCount; m++)
        {
            Vector3 tip = body.Mouth[m].X[body.Mouth[m].N - 1];
            dist[m] = (tip - Closest(tip, a, b)).magnitude;
            order[m] = m;
        }
        for (int i = 1; i < order.Length; i++)
        {
            int v = order[i], j = i - 1;
            while (j >= 0 && dist[order[j]] > dist[v]) { order[j + 1] = order[j]; j--; }
            order[j + 1] = v;
        }
        for (int i = 0; i < order.Length; i++)
        {
            int m = order[i];
            TentacleChain ch = body.Mouth[m];
            if (i < Reachers)
            {
                Vector3 tip = ch.X[ch.N - 1];
                ch.SetTargetShape(Closest(tip, a, b), 1f, true);
                ch.ShapePull = ReachPull; ch.Tone = ReachTone;
                if (dist[m] < r + GrabMargin * s)
                {
                    Grabber = m; Grabs++;
                    Mode = State.Eat;
                    eatUntil = c.Time + EatSeconds * body.TimeScale;
                    c.NotifyGrab(Prey);
                    return;
                }
            }
            else { ch.ClearTargetShape(); ch.ShapePull = 0f; ch.Tone = 1f; }
        }
    }

    // Eating: every tentacle closes on the prey (they draw it in as the body holds still).
    void Close()
    {
        if (Prey == null) return;
        Vector3 p = Prey.Position;
        for (int m = 0; m < body.MouthCount; m++)
        {
            TentacleChain ch = body.Mouth[m];
            ch.SetTargetShape(p, 1f, false);
            ch.ShapePull = ReachPull; ch.Tone = ReachTone;
        }
    }

    static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
        return a + ab * t;
    }

    void Relax()
    {
        Mode = State.Idle; Prey = null; Grabber = -1;
        for (int m = 0; m < body.MouthCount; m++) { TentacleChain ch = body.Mouth[m]; ch.ClearTargetShape(); ch.ShapePull = 0f; ch.Tone = 1f; }
    }
}
