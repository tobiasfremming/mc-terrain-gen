// Creature kit: ThreatDisplay - a posture part. Once the creature has stood still for idleDelay it rears into a
// threat display: the head and forebody rise (CreatureSim.JointLift: headLift at joint 0, fading to nothing over
// liftJoints joints) and CreatureSim.Display ramps 0 -> 1, which every part with a display pose follows - a
// Pincer spreads to its displayOpenDeg and lifts by its displayRaiseDeg (a centipede's forcipules spread wide, its
// ultimate legs raised like a scorpion's). It holds while the creature stands, and drops quickly when it walks off.
// The display weight is eased (smoothstep of a rate-limited ramp), the lift eased again by the grounding, so it never
// pops. Deterministic; Froude: times x sqrt s, the lift x s. Allocation-free after construction; main thread.
// No bones and no mesh: it only sets the sim's posture inputs (stage Late, once per tick).
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("ThreatDisplay", PartKind.Rigid, Bones = "0 (sets the sim's posture inputs)", Summary = "Posture: after standing still for idleDelay the creature rears into a threat display - head and forebody lifted (headLift fading over liftJoints), CreatureSim.Display ramped to 1 so parts with a display pose follow (Pincer.displayOpenDeg / displayRaiseDeg: forcipules spread, ultimate legs raised). Holds while it stands, drops when it walks.",
          System = "ThreatDisplayRuntime (part runtime, stage Late) + CreatureSim JointLift / Display", KitOnly = true,
          UseWhen = "a predator that threatens when it stops: centipedes, scorpions, mantises, cobras rearing, a crab raising its claws")]
    public sealed class ThreatDisplay : CreaturePart
    {
        [Param(ParamUnit.Seconds, "standstill before the display starts", Default = "0.6")] public float idleDelay = 0.6f;
        [Param(ParamUnit.Seconds, "time to rear fully into the display", Default = "0.5")] public float riseSeconds = 0.5f;
        [Param(ParamUnit.Seconds, "time to drop out of it when the creature walks off", Default = "0.25")] public float dropSeconds = 0.25f;
        [Param(ParamUnit.Metres, "lift of the head (joint 0) at the full display", Example = "scolopendra 0.006")] public float headLift;
        [Param(ParamUnit.Count, "joints the lift fades over (joint 0 full .. this joint none)", Min = 1, Max = 32, Default = "6")] public int liftJoints = 6;

        public override int BoneCount(in SpeciesCtx c) => 0;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a ThreatDisplay needs a SpineBody";
            for (int i = 0; i < c.Parts.Count; i++) if (c.Parts[i] is ThreatDisplay && !ReferenceEquals(c.Parts[i], this)) return $"{id}: only one ThreatDisplay part";
            if (idleDelay < 0f || !(riseSeconds > 0f) || !(dropSeconds > 0f)) return $"{id}: idleDelay >= 0, riseSeconds and dropSeconds > 0";
            if (headLift < 0f) return $"{id}: headLift must be >= 0";
            if (liftJoints < 1 || liftJoints > 32) return $"{id}: liftJoints must be 1..32";
            return null;
        }

        // The share of the head lift at joint i (1 at joint 0, a cosine fade to 0 at liftJoints).
        public float LiftShare(int i) => i >= liftJoints ? 0f : 0.5f * (1f + Mathf.Cos(Mathf.PI * i / liftJoints));

        public override IPartRuntime CreateRuntime(in PartCtx c) => c.Sim != null ? new ThreatDisplayRuntime(this, c.Sim) : null;
    }

    public sealed class ThreatDisplayRuntime : IPartRuntime
    {
        const float kMoving = 0.15f;   // walking: speed above this fraction of the cruise speed (as Pincer)

        readonly ThreatDisplay p;
        readonly CreatureSim sim;
        readonly float L, T;
        readonly float[] lift;
        float still, ramp;

        public ThreatDisplay Part => p;
        public float Weight => sim.Display;
        public float Still => still;

        public ThreatDisplayRuntime(ThreatDisplay display, CreatureSim sim)
        {
            p = display; this.sim = sim; L = sim.SizeScale; T = sim.TimeScale;
            lift = new float[sim.Joints.Length];
            sim.JointLift = lift;
            sim.Display = 0f;
        }

        public PartStage Stage => PartStage.Late;

        public bool Walking => sim.Speed > kMoving * sim.Species.speed * sim.TimeScale;

        public void Step(in StepCtx c, float dt)
        {
            if (Walking) still = 0f; else still += dt;
            bool on = !Walking && still >= p.idleDelay * T && !sim.Asleep && sim.Mode == FlightMode.Ground;
            ramp = on ? Mathf.Min(1f, ramp + dt / (p.riseSeconds * T)) : Mathf.Max(0f, ramp - dt / (p.dropSeconds * T));
            float w = ramp * ramp * (3f - 2f * ramp);
            sim.Display = w;
            float h = w * p.headLift * L;
            for (int i = 0; i < lift.Length; i++) lift[i] = h * p.LiftShare(i);
        }

        public void WriteBones(Matrix4x4[] dst, int offset) { }
        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }   // the lifted head stays within the body's reach
        public void DrawDebug(ICreatureDebugDraw d) { }
    }
}
