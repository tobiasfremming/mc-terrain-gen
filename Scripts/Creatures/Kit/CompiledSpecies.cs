// Creature kit T2.3 (design §2.1): the species after compose. Authoring sugar stays in the asset (mirror,
// Repeat, profile keys, gait patterns) and is expanded here, at load, into the flat parts the runtime sees:
//   1. Repeat -> count copies over a joint range (tapered), mirror -> <id>.L then <id>.R (the shipped leg order);
//      a RigidCone keeps `mirror` (it has no bones: one part emits the pair, as BodyAppendage does)
//   2. a Wing turns the limb it names into a WingArm (a mirrored wing names the mirrored arm)
//   3. Walk.pattern assigns gaitGroup to walking limbs left at -1
//   4. validation (ids, references, every part and module), then the bone layout (CreatureBoneLayout)
// Every part is cloned first: compose never changes the asset's objects. The result is cached on the species
// ([NonSerialized], invalidated in OnValidate); Runtime is the legacy-shaped species the systems read (T2.4).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public sealed class CompiledSpecies
    {
        public readonly string Name;
        public readonly bool Soft;
        public readonly List<CreaturePart> Parts = new List<CreaturePart>();     // flat, sugar expanded
        public readonly List<CreatureModule> Modules = new List<CreatureModule>();
        public readonly List<LimbPart> Limbs = new List<LimbPart>();             // = the runtime legs, in order
        public readonly List<WingPart> Wings = new List<WingPart>();
        public readonly List<string> Problems = new List<string>();
        public CreatureBoneLayout Layout { get; private set; }
        // The runtime view: the species whose legacy fields the systems read. A legacy asset (kitVersion 0) is
        // its own runtime view - the same objects, so it stays bit-identical by construction.
        public CreatureSpecies Runtime { get; internal set; }

        public string Error => Problems.Count > 0 ? Problems[0] : null;
        public SpeciesCtx Ctx => new SpeciesCtx(Parts, Modules, Body is SpineBody b ? b.JointCount : Body is ISpineChainBody cb ? cb.JointCount : 0, Soft);
        public CreaturePart Body { get; private set; }
        public T Module<T>() where T : CreatureModule { foreach (CreatureModule m in Modules) if (m is T t) return t; return null; }
        public CreaturePart Find(string id) { foreach (CreaturePart p in Parts) if (p.id == id) return p; return null; }

        CompiledSpecies(string name, bool soft) { Name = name; Soft = soft; }

        // ------------------------------------------------------------ compose

        public static CompiledSpecies Compose(string name, IList<CreaturePart> parts, IList<CreatureModule> modules)
        {
            bool soft = false;
            if (parts != null) foreach (CreaturePart p in parts) if (p is SoftBody) soft = true;
            var c = new CompiledSpecies(name, soft);
            if (modules != null) foreach (CreatureModule m in modules) { if (m == null) c.Problems.Add("a module is null (missing type?)"); else c.Modules.Add(m.Clone()); }
            if (parts != null)
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i] == null) { c.Problems.Add($"parts[{i}] is null (missing type?)"); continue; }
                    c.Expand(parts[i].Clone(), 0);
                }
            foreach (CreaturePart p in c.Parts)
            {
                if (p.Kind == PartKind.Body) { if (c.Body == null) c.Body = p; else c.Problems.Add($"{p.id}: a second Body part (exactly one SpineBody or SoftBody)"); }
                if (p is LimbPart l) c.Limbs.Add(l);
                if (p is WingPart w) c.Wings.Add(w);
            }
            if (c.Body == null) c.Problems.Add("no Body part (exactly one SpineBody or SoftBody)");
            c.WingArms();
            c.AssignGait();
            c.Validate();
            if (c.Problems.Count == 0) c.BuildLayout();
            return c;
        }

        void Expand(CreaturePart p, int depth)
        {
            if (depth > 4) { Problems.Add($"{p.id}: Repeat nested too deep"); return; }
            if (p is Repeat r)
            {
                if (r.template == null) { Problems.Add($"{r.id}: Repeat has no template"); return; }
                if (r.count < 1 || r.count > 64) { Problems.Add($"{r.id}: Repeat.count must be 1..64"); return; }
                for (int k = 0; k < r.count; k++)
                {
                    float f = r.count == 1 ? 0f : k / (r.count - 1f);
                    CreaturePart copy = r.template.Clone();
                    copy.id = r.id + k;
                    copy.at.joint = Mathf.RoundToInt(r.fromJoint + f * (r.toJoint - r.fromJoint));
                    copy.mirror = r.mirror || r.template.mirror;
                    Taper(copy, 1f + f * (r.lastScale - 1f));
                    Expand(copy, depth + 1);
                }
                return;
            }
            if (p.mirror && !(p is RigidCone) && !(p is FinStrip))
            {
                string arm = (p as WingPart)?.arm;
                for (int side = -1; side <= 1; side += 2)
                {
                    string suffix = side < 0 ? ".L" : ".R";
                    CreaturePart m = p.Clone();
                    m.mirror = false;
                    m.at.side = side;
                    m.id = p.id + suffix;
                    // a mirrored wing names the mirrored arm
                    if (m is WingPart w && arm != null && !(arm.EndsWith(".L") || arm.EndsWith(".R"))) w.arm = arm + suffix;
                    Parts.Add(m);
                }
                return;
            }
            Parts.Add(p);
        }

        // Repeat taper: lengths (and the stride that goes with them) x f. Times stay: the copies step together.
        static void Taper(CreaturePart p, float f)
        {
            if (f == 1f) return;
            switch (p)
            {
                case Leg2Bone l:
                    l.upperLength *= f; l.lowerLength *= f; l.upperRadius *= f; l.lowerRadius *= f; l.ankleHeight *= f; l.clawLength *= f;
                    break;
                case SprawlLeg sl:
                    sl.Taper(f);
                    break;
                case ArthropodLeg a:
                    for (int i = 0; i < a.segments.Length; i++) { a.segments[i].length *= f; a.segments[i].radiusStart *= f; a.segments[i].radiusEnd *= f; }
                    break;
                case RigidCone r:
                    r.length *= f; r.radiusStart *= f; r.radiusEnd *= f; r.fangLength *= f;
                    break;
            }
            if (p is LimbPart lp)
            {
                lp.restOffset.x *= f; lp.restOffset.z *= f;
                lp.stepThreshold *= f; lp.stepLift *= f; lp.overshoot *= f; lp.footLength *= f;
            }
        }

        void WingArms()
        {
            foreach (WingPart w in Wings)
                if (Find(w.arm) is LimbPart arm && arm.role == LimbRole.Walk) arm.role = LimbRole.WingArm;
        }

        // Pair = place among the walking limbs of its side, head first (attach joint, then part order: two
        // pairs may share a joint, as the tarantula's legs II and III do).
        void AssignGait()
        {
            Walk walk = Module<Walk>();
            var walking = new List<LimbPart>();
            foreach (LimbPart l in Limbs)
            {
                if (l.gaitGroup >= 0) continue;
                if (!l.Walks || walk == null || walk.pattern == GaitPattern.Explicit) { l.gaitGroup = 0; continue; }
                walking.Add(l);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var s = new List<LimbPart>();
                foreach (LimbPart l in walking) if (l.at.side == side) s.Add(l);
                // stable by attach joint (insertion sort keeps part order within a joint)
                for (int i = 1; i < s.Count; i++)
                    for (int j = i; j > 0 && s[j].at.joint < s[j - 1].at.joint; j--) { LimbPart t = s[j]; s[j] = s[j - 1]; s[j - 1] = t; }
                for (int pair = 0; pair < s.Count; pair++)
                {
                    int right = side > 0 ? 1 : 0;
                    s[pair].gaitGroup = GroupOf(walk.pattern, pair, right, walk.waveGroups, walk.waveTailToHead);
                }
            }
            foreach (LimbPart l in walking) if (l.at.side != -1 && l.at.side != 1) l.gaitGroup = 0;
        }

        public static int GroupOf(GaitPattern pattern, int pair, int right, int waveGroups, bool tailToHead = false)
        {
            switch (pattern)
            {
                case GaitPattern.Diagonal: case GaitPattern.Tripod: case GaitPattern.Tetrapod: return (pair + right) % 2;
                case GaitPattern.Alternate: return right;
                case GaitPattern.Wave:
                {
                    // the groups step in ascending order (waveGait): group k lifts the k-th pair of every wavelength
                    int g = Mathf.Clamp(waveGroups, 2, 15), k = (pair + right * (g / 2)) % g;
                    return tailToHead ? (g - k) % g : k;
                }
                default: return 0;
            }
        }

        void Validate()
        {
            SpeciesCtx ctx = Ctx;
            var ids = new HashSet<string>();
            var refs = new List<string>();
            foreach (CreaturePart p in Parts)
            {
                if (string.IsNullOrEmpty(p.id)) { Problems.Add($"a {p.Meta.Name} has no id"); continue; }
                if (!ids.Add(p.id)) Problems.Add($"duplicate id '{p.id}'");
                if (p.Kind == PartKind.Sugar) { Problems.Add($"{p.id}: {p.Meta.Name} was not expanded"); continue; }
                if (p.Meta.Status != "exists") Problems.Add($"{p.id}: part type {p.Meta.Name} is '{p.Meta.Status}' (not available yet)");
                refs.Clear();
                p.CollectReferences(refs);
                foreach (string r in refs) if (Find(r) == null) Problems.Add($"{p.id}: unknown part '{r}'");
                string e = p.Validate(ctx);
                if (e != null) Problems.Add(e);
            }
            var seen = new HashSet<Type>();
            foreach (CreatureModule m in Modules)
            {
                if (!seen.Add(m.GetType())) Problems.Add($"module {m.Meta.Name} listed twice");
                if (m.Meta.Status != "exists") Problems.Add($"module {m.Meta.Name} is '{m.Meta.Status}' (not available yet)");
                string e = m.Validate(ctx);
                if (e != null) Problems.Add(e);
            }
            int eyes = 0;
            foreach (CreaturePart p in Parts) if (p is Eyes) eyes++;
            if (!Soft && eyes != 1) Problems.Add($"a spine body needs exactly one Eyes part (the runtime writes two eye bones), found {eyes}");
            var armed = new HashSet<string>();
            foreach (WingPart w in Wings) if (!armed.Add(w.arm)) Problems.Add($"{w.id}: arm '{w.arm}' already carries a wing");
            foreach (LimbPart l in Limbs) if (l.gaitGroup < 0 || l.gaitGroup > 31) Problems.Add($"{l.id}: gaitGroup must be in 0..31");
            if (Soft)
            {
                TentacleSpec? first = null;
                foreach (CreaturePart p in Parts)
                    if (p is Tentacle t)
                    {
                        if (first == null) first = t.spec;
                        else if (t.spec.Segments != first.Value.Segments) Problems.Add($"{t.id}: every arm of a soft body has the same segment count");
                    }
                if (first == null) Problems.Add("a SoftBody needs Tentacle parts");
            }
        }

        void BuildLayout()
        {
            SpeciesCtx ctx = Ctx;
            var blocks = new List<BoneBlock>(Parts.Count);
            foreach (CreaturePart p in Parts) blocks.Add(new BoneBlock(p.Kind, p.id, p.BoneCount(ctx)));
            Layout = new CreatureBoneLayout(blocks, Soft ? CreatureBoneLayout.SoftOrder : CreatureBoneLayout.SpineOrder);
            var legStart = new int[Limbs.Count];
            for (int l = 0; l < legStart.Length; l++) legStart[l] = Layout.Start(Parts.IndexOf(Limbs[l]));
            int eyes = Layout.BoneCount, wings = -1;
            for (int i = 0; i < Parts.Count; i++)
            {
                if (Parts[i] is Eyes) eyes = Layout.Start(i);
                if (Parts[i] is WingPart && wings < 0) wings = Layout.Start(i);
            }
            var chains = new List<int>();
            for (int i = 0; i < Parts.Count; i++) if (Parts[i] is Antenna) chains.Add(Layout.Start(i));
            var wingStarts = new List<int>();
            for (int i = 0; i < Parts.Count; i++) if (Parts[i] is WingPart) wingStarts.Add(Layout.Start(i));
            Layout.SetLegacyStarts(legStart, wings < 0 ? eyes : wings, eyes, chains.ToArray(), wingStarts.ToArray());
        }

        // ------------------------------------------------------------ the legacy runtime fields

        // Writes the runtime fields of a spine or soft species from the compiled parts and modules (the inverse
        // of LegacySpeciesAdapter.ToParts). `scale` and the name stay the caller's.
        public void WriteLegacyFields(CreatureSpecies dst)
        {
            if (Error != null) throw new InvalidOperationException(Name + ": " + Error);
            var look = new CreatureLook();
            Module<Skin>()?.WriteTo(ref look);
            if (Module<Membrane>() is Membrane mem) { look.membrane = mem.colour; look.membraneTranslucency = mem.translucency; look.veinContrast = mem.veinContrast; look.membraneRim = mem.rim; look.membraneInner = mem.inner; }
            if (Module<Chitin>() is Chitin ch) { look.bandColor = ch.bandColor; look.sheen = ch.sheen; look.sheenColor = ch.sheenColor; }
            if (Module<Shimmer>() is Shimmer sh)
            {
                look.shimmerColor = sh.colour; look.shimmerColor2 = sh.colour2; look.shimmer = sh.strength;
                look.shimmerWavelength = sh.wavelength; look.shimmerPeriod = sh.period; look.shimmerGlow = sh.glow;
            }
            dst.look = look;
            if (Soft)
            {
                dst.bodyPlan = CreatureBodyPlan.Cephalopod;
                dst.rigidHeadJoints = 0;   // a soft body deforms everywhere: never a rigid head
                CephalopodSpec spec = ((SoftBody)Body).spec;
                int arms = 0;
                foreach (CreaturePart p in Parts) if (p is Tentacle t) { if (arms == 0) spec.arm = t.spec; arms++; }   // every arm the same (Validate)
                if (spec.ArmCount != arms) spec.armCount = arms;   // 0 = the default 8 stays 0
                dst.cephalopod = spec;
                return;
            }
            dst.bodyPlan = CreatureBodyPlan.Spine;
            if (Body is SpineBody body)
            {
                dst.spacing = body.spacing; dst.joints = body.BuildJoints(); dst.headCapLength = body.headCapLength; dst.tailTipLength = body.tailTipLength;
                dst.ringSegments = body.ringSegments; dst.ringsPerSegment = body.ringsPerSegment;
                dst.rigidHeadJoints = body.RigidHeadJoints;   // a spine body's head is rigid unless set to 0 (auto: its profile's head)
            }
            else
            {
                // another chain body (KitAirborne.cs: ISpineChainBody, e.g. RayBody) draws its own mesh
                var chain = (ISpineChainBody)Body;
                dst.spacing = chain.Spacing; dst.joints = chain.BuildJoints(); dst.headCapLength = chain.HeadCapLength; dst.tailTipLength = chain.TailTipLength;
                dst.rigidHeadJoints = chain.RigidHeadJoints;
            }
            dst.legs = new LegSpec[Limbs.Count];
            for (int i = 0; i < Limbs.Count; i++) dst.legs[i] = Limbs[i].ToLegSpec();
            Walk walk = Module<Walk>();
            if (walk != null)
            {
                dst.speed = walk.speed; dst.turnRateDeg = walk.turnRateDeg; dst.accel = walk.accel; dst.turnSlowdown = walk.turnSlowdown;
                dst.gaitSwayDeg = walk.gaitSwayDeg; dst.gaitRollDeg = walk.gaitRollDeg; dst.subSteps = walk.subSteps;
                dst.gaitBob = new GaitBob { amplitude = walk.bob.amplitude, hz = walk.bob.hz, damping = walk.bob.damping };
                dst.waveGait = walk.pattern == GaitPattern.Wave;
                dst.gaitBurst = new GaitBurst { factor = walk.burst.factor, every = walk.burst.every, seconds = walk.burst.seconds };
            }
            Slither sl = Module<Slither>();
            dst.undulationDeg = sl != null ? sl.undulationDeg : 0f; dst.undulationHz = sl != null ? sl.undulationHz : 0f;
            foreach (CreaturePart p in Parts)
                if (p is Eyes e)
                {
                    dst.eyeJoint = e.at.joint; dst.eyeOffset = e.at.offset; dst.eyeRadius = e.radius;
                    dst.eyeCluster = new EyeSpot[e.cluster != null ? e.cluster.Length : 0];
                    for (int k = 0; k < dst.eyeCluster.Length; k++) dst.eyeCluster[k] = new EyeSpot { offset = e.cluster[k].offset, radius = e.cluster[k].radius };
                }
            dst.climb = Module<Climb>() is Climb cl ? cl.ToRuntime() : default;
            dst.wings = new WingSpec[Wings.Count];
            for (int w = 0; w < Wings.Count; w++)
            {
                WingSpec s = KitTypes.Memberwise(Wings[w].spec);
                s.fingerLengths = (float[])s.fingerLengths?.Clone(); s.fingerSpreadDeg = (float[])s.fingerSpreadDeg?.Clone(); s.fingerFoldDeg = (float[])s.fingerFoldDeg?.Clone();
                s.leg = Limbs.IndexOf((LimbPart)Find(Wings[w].arm));
                dst.wings[w] = s;
            }
            dst.flight = Module<Fly>() is Fly fly ? KitTypes.Memberwise(fly.spec) : null;
            if (dst.flight != null) dst.flight.twist = Module<Fly>().twist;
            var cones = new List<BodyAppendage>();
            foreach (CreaturePart p in Parts) if (p is RigidCone r && !r.ownBone) cones.Add(r.ToRuntime());
            dst.appendages = cones.ToArray();
            var fins = new List<FinStripSpec>();
            foreach (CreaturePart p in Parts) if (p is FinStrip fs) fins.Add(fs.ToRuntime());
            dst.fins = fins.ToArray();
            var ant = new List<AntennaSpec>();
            foreach (CreaturePart p in Parts) if (p is Antenna an) ant.Add(an.ToRuntime());
            dst.antennae = ant.ToArray();
            foreach (CreatureModule m in Modules) m.Configure(dst);   // modules with runtime settings of their own (QuadLaunch, SkyHunt)
            dst.InvalidateLayout();
        }

        public override string ToString() => $"{Name}: {Parts.Count} parts, {Modules.Count} modules, " + (Error ?? Layout.ToString());
    }
}
