// Creature kit T3.2 (design §2.3): parts that move on their own and locomotion modes, as CreatureSim stages.
//
//   IPartRuntime     a part's own state, created once per individual by CreaturePart.CreateRuntime (the only
//                    place allowed to allocate) and stepped by CreatureSim at its stage:
//                      AfterChain   walking sub-step, after the chain step and the swing guard
//                      AfterGround  walking sub-step, after grounding, the length restore and the climb up field
//                      AfterLimbs   every sub-step of every mode, after the body and its limbs (antennae)
//                      Late         once per Tick, after all sub-steps (dt = the tick's)
//                      None         never stepped: bones, bounds and debug only (eyes)
//                    It writes its bones into the species layout (WriteBones, in part order after the spine, legs
//                    and wings), grows the culling bounds by its points, and draws itself in the F3 view.
//   ILocomotionMode  a way of moving that may claim a sub-step from walking (flight: CreatureFlight), created by
//                    CreatureModule.CreateMode. Modes are asked in priority order (highest first); Walk runs when
//                    none claims the step; Climb stays a Walk modifier.
//
// Systems, not one virtual call per bone: limbs stay in CreatureLegs (typed arrays, the walking path's cost is
// unchanged); a stage with no runtimes costs one array-length test. Zero GC: every runtime and mode is built in the
// sim's constructor; Step / WriteBones / EncapsulateBounds must not allocate (CreaturePartRuntimeTests.Kit_NoGC).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    public enum PartStage { AfterChain = 0, AfterGround = 1, AfterLimbs = 2, Late = 3, None = 4 }

    // What a runtime may read while it steps: the sim (joints, rotations, frames, legs, mode, size).
    public readonly struct StepCtx
    {
        public readonly CreatureSim Sim;
        public StepCtx(CreatureSim sim) { Sim = sim; }
    }

    // The F3 debug view's drawing calls (CreatureDebugView implements it; lines and points in world space).
    public interface ICreatureDebugDraw
    {
        void Line(Vector3 a, Vector3 b, Color c);
        void Point(Vector3 p, float radius, Color c);
    }

    public interface IPartRuntime
    {
        PartStage Stage { get; }
        void Step(in StepCtx c, float dt);
        void WriteBones(Matrix4x4[] dst, int offset);
        void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi);
        void DrawDebug(ICreatureDebugDraw d);
    }

    // A part runtime whose bones are built on the wing bones (see KitRuntime.AfterSpreadWings).
    public interface ISpreadWingsRest
    {
        void AfterSpreadWings(Matrix4x4[] bones, int offset);
    }

    // What a module gets to build a locomotion mode: the sim's ground and up field.
    public readonly struct ModeCtx
    {
        public readonly IGroundProbe Ground;
        public readonly Func<Vector3, Vector3> UpAt;
        public ModeCtx(IGroundProbe ground, Func<Vector3, Vector3> upAt) { Ground = ground; UpAt = upAt; }
    }

    public interface ILocomotionMode
    {
        // Higher first. Flight 100.
        int Priority { get; }
        // True: the mode moved the body this sub-step (walking does not run).
        bool SubStep(CreatureSim s, float dt, Vector3 target);
    }

    // The sim's runtimes and modes, built from the species' parts and modules (KitView: the parts that go with the
    // runtime fields). Construction only.
    public static class KitRuntime
    {
        public static IPartRuntime[] Create(CreatureSim sim, IReadOnlyList<CreaturePart> parts)
        {
            int[] ordinal = KitView.Ordinals(parts);
            var size = new SizeCtx(sim.SizeScale);
            var list = new List<IPartRuntime>();
            for (int i = 0; i < parts.Count; i++)
            {
                IPartRuntime r = parts[i].CreateRuntime(new PartCtx(sim.Species, i, ordinal[i], size, sim));
                if (r != null) list.Add(r);
            }
            return list.ToArray();
        }

        public static IPartRuntime[] OfStage(IPartRuntime[] all, PartStage stage)
        {
            int n = 0;
            for (int i = 0; i < all.Length; i++) if (all[i].Stage == stage) n++;
            var r = new IPartRuntime[n];
            n = 0;
            for (int i = 0; i < all.Length; i++) if (all[i].Stage == stage) r[n++] = all[i];
            return r;
        }

        // The bind pose of a winged species is the spread glide pose: a separate CreatureWings writes the arm, finger and
        // rib bones over the rest sim's (WingedMeshBuilder.RestBoneWorld). A runtime whose bones are built on those bones
        // (a pterosaur's wing-finger phalanges) re-poses them here on the spread wings, so its bind pose agrees.
        public static void AfterSpreadWings(CreatureSim sim, Matrix4x4[] bones)
        {
            IPartRuntime[] all = sim.PartRuntimes;
            if (all == null) return;
            for (int i = 0; i < all.Length; i++) if (all[i] is ISpreadWingsRest r) r.AfterSpreadWings(bones, 0);
        }

        public static ILocomotionMode[] Modes(CreatureSim sim, IReadOnlyList<CreatureModule> modules, in ModeCtx c)
        {
            var list = new List<ILocomotionMode>();
            for (int i = 0; i < modules.Count; i++)
            {
                ILocomotionMode m = modules[i].CreateMode(sim, c);
                if (m != null) list.Add(m);
            }
            // stable by priority, highest first
            for (int i = 1; i < list.Count; i++)
                for (int j = i; j > 0 && list[j].Priority > list[j - 1].Priority; j--) { ILocomotionMode t = list[j]; list[j] = list[j - 1]; list[j - 1] = t; }
            return list.ToArray();
        }
    }

    // The two eye bones (CreatureSpecies.EyeBone): at the eye joint plus the eye offset (x mirrored: eye 0 left),
    // oriented like the head (joint 1's frame). Written after the spine, legs and wings (the pre-kit order).
    public sealed class EyesRuntime : IPartRuntime
    {
        readonly CreatureSim sim;
        public EyesRuntime(CreatureSim sim) { this.sim = sim; }

        public PartStage Stage => PartStage.None;
        public void Step(in StepCtx c, float dt) { }

        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            CreatureSpecies sp = sim.Species;
            Vector3[] joints = sim.Joints;
            Quaternion[] rot = sim.Rotations;
            int n = joints.Length;
            Vector3 s = Vector3.one * sim.SizeScale;
            Quaternion hr = rot[Mathf.Min(1, n - 1)];
            Quaternion er = rot[sp.eyeJoint];
            for (int e = 0; e < 2; e++)
            {
                Vector3 eo = sp.eyeOffset; eo.x *= e == 0 ? -1f : 1f; // eye 0 left (-x), 1 right
                dst[offset + sp.EyeBone(e)] = Matrix4x4.TRS(joints[sp.eyeJoint] + er * eo * sim.SizeScale, hr, s);
            }
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }   // inside the renderer's reach

        public void DrawDebug(ICreatureDebugDraw d) { }   // the eyes are plain to see (the F3 line count stays the pre-kit one)
    }

    public sealed partial class Eyes
    {
        public override IPartRuntime CreateRuntime(in PartCtx c) => c.Sim != null ? new EyesRuntime(c.Sim) : null;
    }

    public sealed partial class Antenna
    {
        // One strand system (CreatureAntennae: flat arrays) for every antenna of the species, owned by the first.
        public override IPartRuntime CreateRuntime(in PartCtx c)
            => c.Sim != null && c.Ordinal == 0 && c.Species.AntennaCount > 0 ? new CreatureAntennae(c.Species, c.Sim.Joints, c.Sim.Rotations, c.Size.L) : null;
    }
}
