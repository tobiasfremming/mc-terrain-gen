// Creature kit (2026-10-20, the GolfballStrider): Stumble - a clumsy walker. Every so often (gaps 0.5..1.5 x every,
// seeded per individual) the body lurches down - quickly, front first (lurch: the share of the dip the tail end does
// NOT take) - and springs back up over `seconds`; for the first haltShare of it the creature stops (CreatureSim.Halt)
// so its legs settle and readjust before it walks on. Posture only: it adds to CreatureSim.JointLift (its own share,
// so it composes with other posture parts) and sets Halt; the grounding eases the dip in. Clocks run in Froude time
// (dt / sqrt s), so 0.9x and 1.1x stumble on the same frames; depths x s. Kit-only, off unless a species names it.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("Stumble", PartKind.Rigid, Bones = "0 (posture: the sim's JointLift and Halt)", Summary = "A clumsy walker: every so often (seeded per individual) the body lurches down front first and springs back, stopping for part of it to readjust; adds to CreatureSim.JointLift and sets Halt. Froude-timed.",
          System = "StumbleRuntime (part runtime, stage Late) + CreatureSim JointLift / Halt", KitOnly = true,
          UseWhen = "goofy, top-heavy or stilt-legged walkers, a tired or injured animal, anything that should look uncoordinated")]
    public sealed class Stumble : CreaturePart
    {
        [Param(ParamUnit.Seconds, "mean time between stumbles (each gap 0.5..1.5 x this, per individual)", Example = "golfball strider 6")] public float every = 6f;
        [Param(ParamUnit.Seconds, "one stumble: the quick dip and the springy recovery", Example = "golfball strider 0.7")] public float seconds = 0.7f;
        [Param(ParamUnit.Metres, "how far the front of the body drops", Example = "golfball strider 0.07")] public float depth = 0.06f;
        [Param(ParamUnit.Fraction, "lurch: the share of the dip the tail end does not take (0 = level, 1 = only the front)", Min = 0f, Max = 1f, Default = "0.6")] public float lurch = 0.6f;
        [Param(ParamUnit.Fraction, "the share of a stumble it stops walking to readjust (0 = walks through it)", Min = 0f, Max = 1f, Default = "0.5")] public float haltShare = 0.5f;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: a Stumble needs a SpineBody";
            if (!(every > 0f) || !(seconds > 0f)) return $"{id}: every and seconds must be > 0";
            if (seconds >= 0.5f * every) return $"{id}: a stumble must be shorter than the shortest gap (0.5 x every)";
            if (depth < 0f) return $"{id}: depth must be >= 0";
            if (lurch < 0f || lurch > 1f || haltShare < 0f || haltShare > 1f) return $"{id}: lurch and haltShare must be in 0..1";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c) => c.Sim != null ? new StumbleRuntime(this, c.Sim) : null;

        // The dip over one stumble, 0..1 at u = 0..1: a quick drop (the first fifth), a slower springy recovery.
        public static float Dip(float u)
        {
            if (u <= 0f || u >= 1f) return 0f;
            if (u < 0.2f) { float a = u / 0.2f; return a * a * (3f - 2f * a); }
            float b = (u - 0.2f) / 0.8f;
            return 1f - b * b * (3f - 2f * b);
        }
    }

    public sealed class StumbleRuntime : IPartRuntime
    {
        const float kSnap = 1e-4f;   // Froude-time comparisons: 0.9x and 1.1x accumulate dt / sqrt s with different rounding
        readonly Stumble p;
        readonly CreatureSim sim;
        readonly float[] applied;    // what this runtime added to JointLift, per joint
        float clock, next, u = -1f;
        int count;
        bool halting;

        public bool Active => u >= 0f;
        public int Count => count;

        public StumbleRuntime(Stumble part, CreatureSim sim)
        {
            p = part; this.sim = sim;
            int n = sim.Joints.Length;
            applied = new float[n];
            if (sim.JointLift == null || sim.JointLift.Length != n) sim.JointLift = new float[n];
            next = p.every * (0.3f + 0.7f * Hash01(0));
        }

        float Hash01(int k) => 0.5f + 0.5f * BendyLeg.Hash(sim.Seed, 7919 + k);

        public PartStage Stage => PartStage.Late;

        public void Step(in StepCtx c, float dt)
        {
            if (!(dt > 0f)) return;
            float s = sim.SizeScale, fdt = dt / Mathf.Sqrt(Mathf.Max(1e-4f, s));
            clock += fdt;
            if (u < 0f && clock >= next - kSnap && !(sim.Halt && !halting))   // a creature someone else halted does not stumble
            {
                u = 0f;
                count++;
                next = clock + p.seconds + p.every * (0.5f + Hash01(count));
            }
            else if (u >= 0f)
            {
                u += fdt / p.seconds;
                if (u >= 1f - kSnap) u = -1f;
            }
            float f = u >= 0f ? Stumble.Dip(u) : 0f;
            float[] lift = sim.JointLift;
            int n = applied.Length;
            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? (float)i / (n - 1) : 0f;
                float want = -p.depth * s * f * (1f - p.lurch * t);
                lift[i] += want - applied[i];
                applied[i] = want;
            }
            bool halt = u >= 0f && u < p.haltShare;
            if (halt) { sim.Halt = true; halting = true; }
            else if (halting) { sim.Halt = false; halting = false; }
        }

        public void WriteBones(Matrix4x4[] dst, int offset) { }
        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }
        public void DrawDebug(ICreatureDebugDraw d) { }
    }
}
