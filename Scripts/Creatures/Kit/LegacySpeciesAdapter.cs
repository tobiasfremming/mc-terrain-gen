// Creature kit T2.4 (design §5, user decision 4): a legacy species (kitVersion 0: the CreatureSpecies fields)
// seen through the kit. Compile describes it as parts and modules (catalogue, layout, gait, roles) and keeps
// the asset itself as the runtime view: the systems read the same LegSpec / SpineJoint / WingSpec objects as
// before, so every legacy species stays bit-identical by construction. ToParts is also the converter for
// T5.11 (fold = true writes mirror and gait-pattern sugar where it reproduces the asset exactly); the round
// trip ToParts -> compose -> WriteLegacyFields is pinned field-equal and golden-equal for every asset.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public static class LegacySpeciesAdapter
    {
        public static CompiledSpecies Compile(CreatureSpecies sp)
        {
            CompiledSpecies c;
            try
            {
                List<CreaturePart> parts = ToParts(sp, false, out List<CreatureModule> modules);
                c = CompiledSpecies.Compose(sp.name, parts, modules);
            }
            catch (Exception e)
            {
                // a legacy species never fails to run because of its description
                c = CompiledSpecies.Compose(sp.name, null, null);
                c.Problems.Insert(0, "adapter: " + e.Message);
            }
            c.Runtime = sp;
            return c;
        }

        // ------------------------------------------------------------ legacy fields -> parts and modules

        public static List<CreaturePart> ToParts(CreatureSpecies sp, bool fold, out List<CreatureModule> modules)
        {
            var parts = new List<CreaturePart>();
            modules = new List<CreatureModule>();
            if (sp.bodyPlan == CreatureBodyPlan.Cephalopod) { Cephalopod(sp, parts, modules); return parts; }

            var joints = new JointDef[sp.JointCount];
            for (int i = 0; i < joints.Length; i++)
            {
                SpineJoint j = sp.joints[i];
                joints[i] = new JointDef { halfWidth = j.halfWidth, halfHeight = j.halfHeight, clearance = j.clearance, bendLimitDeg = j.bendLimitDeg, rigid = j.rigid };
            }
            parts.Add(new SpineBody
            {
                id = "body", spacing = sp.spacing, headCapLength = sp.headCapLength, tailTipLength = sp.tailTipLength, joints = joints,
                ringSegments = sp.ringSegments, ringsPerSegment = sp.ringsPerSegment, rigidHeadJoints = sp.rigidHeadJoints,
            });

            // legs: leg l -> its part id (folded pairs: <id>.L / <id>.R)
            int legs = sp.legs != null ? sp.legs.Length : 0;
            var legId = new string[legs];
            var limbs = new List<LimbPart>();
            for (int l = 0; l < legs; l++)
            {
                LimbPart p = Limb(sp.legs[l], "leg" + l);
                if (fold && l + 1 < legs && sp.legs[l].side == -1 && sp.legs[l + 1].side == 1 && Mirrors(sp.legs[l], sp.legs[l + 1]))
                {
                    p.id = "leg" + l;
                    p.mirror = true; p.at.side = 0;
                    legId[l] = p.id + ".L"; legId[l + 1] = p.id + ".R";
                    parts.Add(p); limbs.Add(p);
                    l++;
                    continue;
                }
                legId[l] = p.id;
                parts.Add(p); limbs.Add(p);
            }

            var walk = new Walk
            {
                speed = sp.speed, turnRateDeg = sp.turnRateDeg, accel = sp.accel, turnSlowdown = sp.turnSlowdown,
                gaitSwayDeg = sp.gaitSwayDeg, gaitRollDeg = sp.gaitRollDeg, subSteps = sp.subSteps,
                bob = new GaitBobDef { amplitude = sp.gaitBob.amplitude, hz = sp.gaitBob.hz, damping = sp.gaitBob.damping },
                pattern = GaitPattern.Explicit,
            };
            modules.Add(walk);
            if (sp.undulationDeg != 0f) modules.Add(new Slither { undulationDeg = sp.undulationDeg, undulationHz = sp.undulationHz });
            if (sp.climb.canClimb) modules.Add(Climb.From(sp.climb));

            // wings: on the arm's part id; a mirrored pair of equal wings on a mirrored arm pair folds too
            for (int w = 0; w < sp.WingCount; w++)
            {
                WingSpec s = sp.wings[w];
                string arm = s.leg >= 0 && s.leg < legs ? legId[s.leg] : "leg" + s.leg;
                if (fold && w + 1 < sp.WingCount && arm.EndsWith(".L") && Mirrors(s, sp.wings[w + 1], legId))
                {
                    parts.Add(new Wing { id = "wing" + w, mirror = true, arm = arm.Substring(0, arm.Length - 2), spec = Copy(s) });
                    w++;
                    continue;
                }
                parts.Add(new Wing { id = "wing" + w, arm = arm, spec = Copy(s) });
            }

            var cluster = new EyeSpotDef[sp.eyeCluster != null ? sp.eyeCluster.Length : 0];
            for (int k = 0; k < cluster.Length; k++) cluster[k] = new EyeSpotDef { offset = sp.eyeCluster[k].offset, radius = sp.eyeCluster[k].radius };
            parts.Add(new Eyes { id = "eyes", at = new PartSocket(sp.eyeJoint, 0, sp.eyeOffset), radius = sp.eyeRadius, cluster = cluster });
            if (sp.appendages != null) for (int a = 0; a < sp.appendages.Length; a++) parts.Add(RigidCone.From(sp.appendages[a], "cone" + a));
            if (sp.fins != null) for (int f = 0; f < sp.fins.Length; f++) parts.Add(FinStrip.From(sp.fins[f], "fin" + f));
            if (sp.antennae != null) for (int a = 0; a < sp.antennae.Length; a++) parts.Add(Antenna.From(sp.antennae[a], "antenna" + a));

            if (sp.WingCount > 0 && sp.flight != null) modules.Add(new Fly { spec = KitTypes.Memberwise(sp.flight) });
            modules.Add(new Wander());
            if (sp.climb.canClimb) modules.Add(new ClimbExcursion());
            if (sp.CanFly) modules.Add(new FlightExcursion());
            Look(sp.look, modules);

            // the gait pattern that reproduces the asset's groups (descriptive; folded: the groups become -1)
            walk.pattern = PatternOf(sp, parts, modules);
            if (fold && walk.pattern != GaitPattern.Explicit) foreach (LimbPart p in limbs) p.gaitGroup = -1;
            return parts;
        }

        static LimbPart Limb(LegSpec l, string id) => l.IsArthropod ? (LimbPart)ArthropodLeg.From(l, id) : Leg2Bone.From(l, id);

        static void Look(in CreatureLook l, List<CreatureModule> modules)
        {
            modules.Add(Skin.From(l));
            if (Set(l.membrane) || l.membraneTranslucency != 0f || l.veinContrast != 0f || l.membraneRim != 0f)
                modules.Add(new Membrane { colour = l.membrane, translucency = l.membraneTranslucency, veinContrast = l.veinContrast, rim = l.membraneRim });
            if (Set(l.bandColor) || l.sheen != 0f || Set(l.sheenColor))
                modules.Add(new Chitin { bandColor = l.bandColor, sheen = l.sheen, sheenColor = l.sheenColor });
            if (l.shimmer != 0f || Set(l.shimmerColor) || Set(l.shimmerColor2) || l.shimmerWavelength != 0f || l.shimmerPeriod != 0f || l.shimmerGlow != 0f)
                modules.Add(new Shimmer { colour = l.shimmerColor, colour2 = l.shimmerColor2, strength = l.shimmer, wavelength = l.shimmerWavelength, period = l.shimmerPeriod, glow = l.shimmerGlow });
        }

        // exact (Unity's Color == is approximate)
        static bool Set(Color c) => c.r != 0f || c.g != 0f || c.b != 0f || c.a != 0f;

        static void Cephalopod(CreatureSpecies sp, List<CreaturePart> parts, List<CreatureModule> modules)
        {
            CephalopodSpec c = sp.cephalopod;
            for (int a = 0; a < c.ArmCount; a++) parts.Add(new Tentacle { id = "arm" + a, at = new PartSocket { slot = a }, spec = c.arm });
            parts.Add(new SoftBody { id = "body", spec = c });
            parts.Add(new Eyes { id = "eyes", radius = c.EyeRadius });
            parts.Add(new RigidCone { id = "siphon", ownBone = true, length = c.SiphonLength, radiusStart = c.SiphonRadius, radiusEnd = c.SiphonRadius });
            modules.Add(new Ambush());
            Look(sp.look, modules);
            modules.Add(new Cloak());
            modules.Add(new Chromatophore());
        }

        static GaitPattern PatternOf(CreatureSpecies sp, List<CreaturePart> parts, List<CreatureModule> modules)
        {
            int walking = 0;
            if (sp.legs != null) foreach (LegSpec l in sp.legs) if (!l.feeler) walking++;
            if (walking == 0) return GaitPattern.Explicit;
            GaitPattern p = walking <= 4 ? GaitPattern.Diagonal : walking <= 6 ? GaitPattern.Tripod : GaitPattern.Tetrapod;
            // compose a copy with every group from the pattern and compare
            var trial = new List<CreaturePart>(parts.Count);
            foreach (CreaturePart part in parts)
            {
                CreaturePart c = part.Clone();
                if (c is LimbPart lp) lp.gaitGroup = -1;
                trial.Add(c);
            }
            var mods = new List<CreatureModule>();
            foreach (CreatureModule m in modules) mods.Add(m is Walk w0 ? new Walk { pattern = p, waveGroups = w0.waveGroups } : m);
            CompiledSpecies c2 = CompiledSpecies.Compose(sp.name, trial, mods);
            if (c2.Limbs.Count != sp.legs.Length) return GaitPattern.Explicit;
            for (int l = 0; l < sp.legs.Length; l++) if (c2.Limbs[l].gaitGroup != sp.legs[l].gaitGroup) return GaitPattern.Explicit;
            return p;
        }

        // R is L mirrored: equal except side (and the gait group, which the pattern assigns)
        static bool Mirrors(LegSpec l, LegSpec r)
            => KitCompare.FirstDifference(l, r, path => path == "side" || path == "gaitGroup") == null;

        static bool Mirrors(WingSpec l, WingSpec r, string[] legId)
        {
            if (r.leg < 0 || r.leg >= legId.Length || legId[l.leg] == null || legId[r.leg] == null) return false;
            if (legId[r.leg] != legId[l.leg].Substring(0, legId[l.leg].Length - 2) + ".R") return false;
            return KitCompare.FirstDifference(l, r, path => path == "leg") == null;
        }

        static WingSpec Copy(WingSpec s)
        {
            WingSpec c = KitTypes.Memberwise(s);
            c.fingerLengths = (float[])s.fingerLengths?.Clone(); c.fingerSpreadDeg = (float[])s.fingerSpreadDeg?.Clone(); c.fingerFoldDeg = (float[])s.fingerFoldDeg?.Clone();
            return c;
        }
    }
}
