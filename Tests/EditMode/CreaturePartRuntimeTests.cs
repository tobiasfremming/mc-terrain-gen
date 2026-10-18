using System;
using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // A part type that only exists in the tests (not in namespace CreatureKit: the catalogue never lists it): a
    // runtime at a chosen stage that counts its steps and, with bones, writes them.
    [Serializable]
    [Part("TestStage", PartKind.Chain, Bones = "= bones")]
    public sealed class StageProbePart : CreaturePart
    {
        public PartStage stage;
        public int bones;
        public override int BoneCount(in SpeciesCtx c) => bones;
        public override IPartRuntime CreateRuntime(in PartCtx c) => c.Sim != null ? new StageProbe(this, c) : null;
    }

    public sealed class StageProbe : IPartRuntime
    {
        public readonly string Id;
        public readonly int FirstBone, Bones;
        readonly CreatureSim sim;
        public int Steps;
        public float Seconds;
        public int WalkingSteps, AirSteps;

        public StageProbe(StageProbePart p, in PartCtx c)
        {
            Id = p.id; Stage = p.stage; Bones = p.bones; sim = c.Sim;
            FirstBone = c.Species.Layout.Start(c.Index);
        }

        public PartStage Stage { get; }
        public void Step(in StepCtx c, float dt)
        {
            Steps++; Seconds += dt;
            if (c.Sim.Mode == FlightMode.Air) AirSteps++; else WalkingSteps++;
        }
        public void WriteBones(Matrix4x4[] dst, int offset)
        {
            for (int k = 0; k < Bones; k++) dst[offset + FirstBone + k] = Matrix4x4.TRS(sim.Joints[0], sim.Rotations[0], Vector3.one * sim.SizeScale);
        }
        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }
        public void DrawDebug(ICreatureDebugDraw d) { }
    }

    // Creature kit T3.2: part runtimes are stepped once per sub-step at their stage (Late once per tick), write
    // their bones into the layout, cost no GC; flight is a locomotion mode from the Fly module.
    public class CreaturePartRuntimeTests
    {
        const float kDt = 1f / 60f;
        readonly List<Object> trash = new List<Object>();
        [TearDown] public void TearDown() { foreach (Object o in trash) if (o != null) Object.DestroyImmediate(o); trash.Clear(); }

        // A kit asset's parts and modules plus the probes (the asset is not changed).
        CreatureSpecies With(string asset, params CreaturePart[] extra)
        {
            CreatureSpecies a = CreatureKitAssets.LoadAsset(asset);
            Assert.IsNotNull(a, asset);
            Assert.Greater(a.kitVersion, 0, asset + " is a kit asset");
            var k = ScriptableObject.CreateInstance<CreatureSpecies>();
            trash.Add(k);
            k.name = asset + "+probes"; k.scale = a.scale; k.kitVersion = 1;
            k.parts = new List<CreaturePart>(a.parts); k.parts.AddRange(extra);
            k.modules = new List<CreatureModule>(a.modules);
            Assert.IsNull(k.Validate(), k.name);
            return k.Compiled.Runtime;
        }

        static StageProbePart Probe(string id, PartStage stage, int bones = 0) => new StageProbePart { id = id, stage = stage, bones = bones, at = new PartSocket(0, 0, Vector3.zero) };

        static StageProbe Find(CreatureSim sim, string id)
        {
            foreach (IPartRuntime r in sim.PartRuntimes) if (r is StageProbe p && p.Id == id) return p;
            Assert.Fail("no runtime " + id);
            return null;
        }

        static CreatureSim Sim(CreatureSpecies sp) => new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, Vector3.zero, Vector3.forward, sp.scale);

        [Test] public void Stages_CalledOncePerSubStep()
        {
            CreatureSpecies sp = With("KitMonitor", Probe("chain", PartStage.AfterChain), Probe("ground", PartStage.AfterGround),
                                      Probe("limbs", PartStage.AfterLimbs), Probe("late", PartStage.Late), Probe("none", PartStage.None));
            CreatureSim sim = Sim(sp);
            int sub = Mathf.Max(1, sp.subSteps), ticks = 90;
            for (int t = 0; t < ticks; t++) sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * 2f);
            sim.Tick(0f, sim.Joints[0]);   // a zero tick steps nothing
            Assert.AreEqual(ticks * sub, Find(sim, "chain").Steps, "AfterChain: once per walking sub-step");
            Assert.AreEqual(ticks * sub, Find(sim, "ground").Steps, "AfterGround: once per walking sub-step");
            Assert.AreEqual(ticks * sub, Find(sim, "limbs").Steps, "AfterLimbs: once per sub-step");
            Assert.AreEqual(ticks, Find(sim, "late").Steps, "Late: once per tick");
            Assert.AreEqual(0, Find(sim, "none").Steps, "None: never stepped");
            Assert.AreEqual(ticks * kDt, Find(sim, "late").Seconds, 1e-4f, "Late gets the tick's dt");
            Assert.AreEqual(ticks * kDt, Find(sim, "limbs").Seconds, 1e-4f, "sub-steps add up to the ticks");
        }

        // In the air the walking stages do not run; AfterLimbs still steps every sub-step.
        [Test] public void Stages_Flight_OnlyAfterLimbsInTheAir()
        {
            CreatureSpecies sp = With("SkyDragon", Probe("chain", PartStage.AfterChain), Probe("limbs", PartStage.AfterLimbs));
            CreatureSim sim = Sim(sp);
            Assert.IsNotNull(sim.Flight, "the Fly module's mode");
            for (int t = 0; t < 30; t++) sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * 5f);
            Assert.IsTrue(sim.Flight.Launch(5f * sp.scale));
            int sub = Mathf.Max(1, sp.subSteps), ticks = 300;
            for (int t = 0; t < ticks; t++) sim.Tick(kDt, sim.Joints[0] + sim.Rotations[0] * Vector3.forward * 5f);
            StageProbe chain = Find(sim, "chain"), limbs = Find(sim, "limbs");
            TestContext.WriteLine($"chain {chain.Steps} (air {chain.AirSteps}), limbs {limbs.Steps} (air {limbs.AirSteps}), mode {sim.Mode}");
            Assert.AreEqual((30 + ticks) * sub, limbs.Steps, "AfterLimbs every sub-step, every mode");
            Assert.Greater(limbs.AirSteps, 0, "flew");
            Assert.AreEqual(0, chain.AirSteps, "no walking stage in the air");
            Assert.Less(chain.Steps, limbs.Steps);
        }

        // A runtime with bones: they extend the layout, the renderer's bone count and the bind poses, and are written.
        [Test] public void PartRuntime_Bones_InLayoutAndWritten()
        {
            CreatureSpecies plain = CreatureKitAssets.Load("KitMonitor");
            CreatureSpecies sp = With("KitMonitor", Probe("strand", PartStage.AfterLimbs, 3));
            Assert.AreEqual(plain.BoneCount + 3, sp.BoneCount);
            Assert.IsNull(sp.Layout.Check());
            CreatureSim sim = Sim(sp);
            StageProbe p = Find(sim, "strand");
            Assert.AreEqual(plain.BoneCount, p.FirstBone, "after every legacy block");
            var bones = new Matrix4x4[sp.BoneCount];
            sim.WriteBoneWorld(bones, 0);
            for (int k = 0; k < 3; k++) Assert.AreEqual(sim.Joints[0].x, bones[p.FirstBone + k].m03, 1e-6f, "bone written");
            Assert.AreEqual(sp.BoneCount, CreatureRestPose.BindPoses(sp).Length);
            // the mesh of the existing parts is unchanged by a part that emits nothing
            Assert.IsNull(MeshBytes.Compare(MeshBytes.Current(plain), MeshBytes.Current(sp)));
        }

        [Test] public void Kit_NoGC_WithStubRuntime()
        {
            CreatureSpecies sp = With("KitMonitor", Probe("chain", PartStage.AfterChain), Probe("late", PartStage.Late, 2));
            GcAllocs.AssertCounterWorks();
            int allocs = KitChecks.TickAllocs(sp, false);
            Assert.AreEqual(0, allocs, "brain/tick/bones with part runtimes allocated");
        }

        // Flight is the Fly module's locomotion mode; walkers have none.
        [Test] public void Modes_FlightFromFlyModule()
        {
            CreatureSim walker = Sim(CreatureKitAssets.Load("DesertMonitor"));
            Assert.IsNull(walker.Flight);
            CreatureSim flyer = Sim(CreatureKitAssets.Load("WingedSerpent"));
            Assert.IsNotNull(flyer.Flight);
            Assert.IsTrue(flyer.Flight is ILocomotionMode);
            Assert.AreEqual(100, ((ILocomotionMode)flyer.Flight).Priority);
        }

        // Eyes and antennae are part runtimes now; their bones are where the layout says.
        [Test] public void Eyes_And_Antennae_AreRuntimes()
        {
            CreatureSim dragon = Sim(CreatureKitAssets.Load("SkyDragon"));
            int eyes = 0, strands = 0;
            foreach (IPartRuntime r in dragon.PartRuntimes) { if (r is EyesRuntime) eyes++; if (r is CreatureAntennae) strands++; }
            Assert.AreEqual(1, eyes);
            Assert.AreEqual(1, strands, "one strand system for every antenna");
            Assert.AreSame(dragon.Antennae, Array.Find(dragon.PartRuntimes, r => r is CreatureAntennae));
            Assert.AreEqual(PartStage.AfterLimbs, dragon.Antennae.Stage);
        }
    }
}
