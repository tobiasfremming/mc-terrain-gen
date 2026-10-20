// Creature kit service (2026-10-20, kit services): the phases an animal moves through, as channels the pose sweep
// (CreatureKitTests.PoseSweep), the pose dump and the bone trace label their frames with.
//
//   IPosePhases   the seam: a part runtime, a locomotion mode or a brain behaviour with discrete phases implements it
//                 (read-only, no effect on the motion) and the sweep must see the animal through every phase it marks
//                 required. A new system with phases gets the sweep by implementing it.
//   PosePhases    collects the channels of one individual: the shipped systems through adapters here (no edit to the
//                 systems themselves: flight, Soar, SkyHunt, Burrow, Tuck, ThreatDisplay, Stumble; Tongue, Jaw and the
//                 first Tendril as labels only), then every runtime,
//                 mode and behaviour that implements IPosePhases. A behaviour's channel reads -1 while it is not the
//                 brain's current one.
// Tools and tests only: collection allocates (once); reading the current phases does not.
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public interface IPosePhases
    {
        string PhaseChannel { get; }
        int PhaseCount { get; }
        string PhaseName(int phase);
        // -1: none of its phases now (the system is idle or not in charge)
        int CurrentPhase { get; }
        // the sweep fails when it never sees this phase in its run (else the check would not bite for it)
        bool PhaseRequired(int phase);
    }

    public static class PosePhases
    {
        // A channel over a shipped system: names, required flags, the reader.
        sealed class Adapter : IPosePhases
        {
            readonly string channel;
            readonly string[] names;
            readonly bool[] required;
            readonly System.Func<int> current;
            public Adapter(string channel, string[] names, bool[] required, System.Func<int> current) { this.channel = channel; this.names = names; this.required = required; this.current = current; }
            public string PhaseChannel => channel;
            public int PhaseCount => names.Length;
            public string PhaseName(int p) => p >= 0 && p < names.Length ? names[p] : "-";
            public int CurrentPhase => current();
            public bool PhaseRequired(int p) => p >= 0 && p < required.Length && required[p];
        }

        // A behaviour's channel: its phase only while the brain runs it.
        sealed class WhileCurrent : IPosePhases
        {
            readonly IPosePhases inner; readonly ModularBrain brain; readonly int index;
            public WhileCurrent(IPosePhases inner, ModularBrain brain, int index) { this.inner = inner; this.brain = brain; this.index = index; }
            public string PhaseChannel => inner.PhaseChannel;
            public int PhaseCount => inner.PhaseCount;
            public string PhaseName(int p) => inner.PhaseName(p);
            public int CurrentPhase => brain.Current == index ? inner.CurrentPhase : -1;
            public bool PhaseRequired(int p) => inner.PhaseRequired(p);
        }

        // Flight (legacy wings + Fly): on the ground a flyer wing-walks; in the air it beats or glides.
        public static readonly string[] FlightNames = { "wing-walk", "launch", "beat", "glide", "flare", "touchdown" };
        public static readonly string[] SoarNames = { "beat", "glide", "barrel", "loop" };
        public static readonly string[] HuntNames = { "circle", "stalk", "stoop", "pull-up" };
        public static readonly string[] BurrowNames = { "idle", "to-hole", "dig-in", "dive", "under", "lunge", "rise" };
        public static readonly string[] TuckNames = { "active", "curling", "curled", "peeking", "uncurling" };
        public static readonly string[] DisplayNames = { "rearing", "display" };
        public static readonly string[] StumbleNames = { "stumble" };
        public static readonly string[] TongueNames = { "hidden", "out", "flick", "in" };
        public static readonly string[] JawNames = { "closed", "opening", "open", "closing" };
        public static readonly string[] TendrilNames = { "hang", "reach", "hold", "probe", "lash" };

        static int FlightPhase(CreatureFlight f)
        {
            switch (f.Mode)
            {
                case FlightMode.Ground: return 0;
                case FlightMode.Launch: return 1;
                case FlightMode.Air: return f.Model.Gliding ? 3 : 2;
                case FlightMode.Flare: return 4;
                default: return 5;
            }
        }

        static int SoarPhase(SoarFlight s)
            => s.Manoeuvre == SoarManoeuvre.Barrel ? 2 : s.Manoeuvre == SoarManoeuvre.Loop ? 3 : s.Flapping ? 0 : 1;

        // The channels of one individual (brain: its brain, or null). Allocates: call once per run.
        public static List<IPosePhases> Collect(CreatureSim sim, ICreatureBrain brain)
        {
            var list = new List<IPosePhases>();
            CreatureFlight fl = sim.Flight;
            if (fl != null)
                list.Add(new Adapter("flight", FlightNames, new[] { true, true, true, true, true, true }, () => FlightPhase(fl)));
            SoarFlight soar = sim.LocomotionMode<SoarFlight>();
            if (soar != null)
                list.Add(new Adapter("soar", SoarNames, new[] { true, true, false, false }, () => SoarPhase(soar)));
            ILocomotionMode[] modes = sim.LocomotionModes;
            for (int i = 0; i < modes.Length; i++) if (modes[i] is IPosePhases p) list.Add(p);

            IPartRuntime[] rt = sim.PartRuntimes;
            for (int i = 0; i < rt.Length; i++)
            {
                IPartRuntime r = rt[i];
                if (r is BurrowRuntime b)
                    list.Add(new Adapter("burrow", BurrowNames, new[] { false, false, true, true, true, false, true }, () => b.State == BurrowRuntime.Phase.Idle ? -1 : (int)b.State));
                else if (r is TuckRuntime t)
                    list.Add(new Adapter("tuck", TuckNames, new[] { false, true, true, false, true }, () => t.State == TuckRuntime.Phase.Active ? -1 : (int)t.State));
                else if (r is ThreatDisplayRuntime d)
                    list.Add(new Adapter("display", DisplayNames, new[] { false, true }, () => d.Weight >= 0.98f ? 1 : d.Weight > 0.02f ? 0 : -1));
                else if (r is StumbleRuntime s)
                    list.Add(new Adapter("stumble", StumbleNames, new[] { true }, () => s.Active ? 0 : -1));
                // postures that are not locomotion: labelled (where a defect shows), not required
                else if (r is TongueRuntime tg)
                    list.Add(new Adapter("tongue", TongueNames, new bool[4], () => tg.State == TongueRuntime.Phase.Hidden ? -1 : (int)tg.State));
                else if (r is JawRuntime jw)
                    list.Add(new Adapter("jaw", JawNames, new bool[4], () => jw.State == JawRuntime.Phase.Closed ? -1 : (int)jw.State));
                else if (r is TendrilRuntime td && !HasChannel(list, "tendril"))
                    list.Add(new Adapter("tendril", TendrilNames, new bool[5], () => td.State == TendrilRuntime.Phase.Hang ? -1 : (int)td.State));
                if (r is IPosePhases own) list.Add(own);
            }

            if (brain is ModularBrain mb)
                for (int i = 0; i < mb.Behaviours.Count; i++)
                {
                    IBehaviour beh = mb.Behaviours[i];
                    if (beh is SkyHuntBehaviour h)
                        list.Add(new WhileCurrent(new Adapter("hunt", HuntNames, new[] { false, false, true, true }, () => (int)h.Current), mb, i));
                    if (beh is IPosePhases own) list.Add(new WhileCurrent(own, mb, i));
                }
            return list;
        }

        static bool HasChannel(List<IPosePhases> list, string channel)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].PhaseChannel == channel) return true;
            return false;
        }

        // "flight=glide hunt=stoop" (channels at -1 left out; "walk" when every channel is idle). Allocates: reports only.
        public static string Label(List<IPosePhases> channels)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < channels.Count; i++)
            {
                int p = channels[i].CurrentPhase;
                if (p < 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(channels[i].PhaseChannel).Append('=').Append(channels[i].PhaseName(p));
            }
            return sb.Length > 0 ? sb.ToString() : "walk";
        }
    }
}
