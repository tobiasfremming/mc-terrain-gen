using System;
using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // VesperLab (Vesper step 1, task 5): the "M" menu (VesperLabMenu, plain C#) and the lab's layout (VesperLabLayout).
    //   Menu_Toggle_OpenClose           M toggles the menu: open, closed, open ... (pressing M while open closes it)
    //   Menu_Pick_SendsCommand          a picked command reaches the brain (Current, WalkTo's point) and the creature acts
    //                                   on it (Fly: in the air within the CommandBrain tests' bound); Status is "Doing: " +
    //                                   the brain's Doing
    //   Lab_Respawn_KeepsMenuAndCommand a respawn (a new sim and CommandBrain handed to Attach, as VesperLab does when the
    //                                   species asset changes in play mode) keeps the menu open and the picked command: the
    //                                   new brain's Current is it and the new individual does it (Circle: airborne); a
    //                                   WalkTo that had already arrived comes back as Stand. RED: Attach forgets.
    //   Lab_Spawns_OnFlatGround_InsideArea the aviary is VesperLabLayout.AviaryFactor x the Creature Lab's radius; the rocks
    //                                   are the CommandBrain tests' perch rock (rock 0 exactly), inside the safe disc, apart;
    //                                   Vesper starts off every rock's keep-out and, ticked 3 s x sqrt s, stands on the flat
    //                                   ground (planted feet on y = 0) with every joint inside the aviary; the player's spot
    //                                   is inside the aviary, off the rocks.
    //   Lab_Rock_IsTheTestedBox         VesperLab's rock mesh (VesperLabLayout.RockGeometry, which CreatureSurfaceMesh turns
    //                                   into the perch surface) is exactly the CommandBrain tests' box: its bounds are the
    //                                   box's, its top flat at the box's height, every face outward (Unity winding); rock 0's
    //                                   box is CreatureCommandBrainTests.Rock's; the obstacle disc holds the footprint. RED:
    //                                   reversed triangles fail the outward check.
    //   Menu_Texts_Cached               the IMGUI texts (title, buttons, the closed box) are the same strings frame after frame
    //                                   until the command or Doing changes (OnGUI builds none)
    //   Lab_Perch_NeverStalls           Perch in the lab's layout (three rocks, as obstacles) ends perched on a rock or, after
    //                                   the brain's attempts, on open ground saying so - never stuck in between.
    public class CreatureVesperLabTests
    {
        const float kDt = 1f / 60f;
        const float kFlyBound = 10f;      // s x sqrt s (CreatureCommandBrainTests.kFlyBound)
        const float kWalkToBound = 30f;   // a walk from the ground (CreatureCommandBrainTests: WalkTo from the ground 8 s x sqrt s on every seed)
        const float kPerchBound = 160f;   // two perch attempts of 30 s x sqrt s each, the flights between, an open-ground landing

        static CreatureSpecies Vesper() => CreatureVesperTests.Vesper();

        sealed class Lab
        {
            public readonly CreatureSpecies sp;
            public readonly float s, T;
            public readonly EnclosureArea area;
            public readonly WingedTestSurfaces ground;
            public CreatureSim sim;
            public CommandBrain brain;

            public Lab(CreatureSpecies sp, int rocks)
            {
                this.sp = sp; s = sp.scale; T = Mathf.Sqrt(s);
                area = VesperLabLayout.Area(sp, s, Vector3.zero, rocks);
                ground = new WingedTestSurfaces();
                // VesperLab's rocks: VesperLabLayout.RockBox (the box its RockGeometry mesh fills)
                for (int i = 0; i < rocks; i++)
                {
                    Bounds b = VesperLabLayout.RockBox(area.center, i, rocks, s);
                    ground.AddRock(b.center - Vector3.up * b.extents.y, b.size, 11 + i);
                }
            }

            // VesperLab's spawn: a new individual at the start spot with a CommandBrain, handed to the menu
            public void Spawn(VesperLabMenu menu, int seed = 1)
            {
                VesperLabLayout.Start(area, out Vector3 at, out Vector3 fwd);
                sim = new CreatureSim(sp, ground, _ => Vector3.up, at, fwd, s, seed) { Bounds = area };
                brain = new CommandBrain(area, seed);
                menu.Attach(brain);
            }

            public void Tick() => sim.Tick(kDt, brain.Target(sim, kDt));
            public void Run(float seconds) { int n = Mathf.CeilToInt(seconds * T / kDt); for (int i = 0; i < n; i++) Tick(); }
            public float Until(Func<bool> done, float seconds)
            {
                float t0 = sim.Time;
                int n = Mathf.CeilToInt(seconds * T / kDt);
                for (int i = 0; i < n; i++) { Tick(); if (done()) return sim.Time - t0; }
                return -1f;
            }
            public string State => $"t={sim.Time:F1} s {sim.Mode}, doing \"{brain.Doing}\"";
        }

        [Test] public void Menu_Toggle_OpenClose()
        {
            var m = new VesperLabMenu();
            Assert.IsFalse(m.Open, "closed at the start");
            m.Toggle(); Assert.IsTrue(m.Open, "M opens");
            m.Toggle(); Assert.IsFalse(m.Open, "M while open closes");
            for (int i = 0; i < 5; i++) m.Toggle();
            Assert.IsTrue(m.Open, "five presses from closed: open");
            Assert.AreEqual(7, VesperLabMenu.Commands.Length, "every command has a button");
            foreach (CreatureCommand c in Enum.GetValues(typeof(CreatureCommand)))
                Assert.GreaterOrEqual(Array.IndexOf(VesperLabMenu.Commands, c), 0, $"{c} has a button");
            StringAssert.Contains("F3", VesperLabMenu.Hint);
        }

        [Test] public void Menu_Pick_SendsCommand()
        {
            var lab = new Lab(Vesper(), 3);
            var m = new VesperLabMenu();
            lab.Spawn(m);
            Assert.AreSame(lab.brain, m.Brain);
            lab.Run(0.5f);
            Assert.AreEqual("Doing: " + lab.brain.Doing, m.Status);

            m.Pick(CreatureCommand.Fly);
            Assert.AreEqual(CreatureCommand.Fly, lab.brain.Current, "the brain got Fly");
            float took = lab.Until(() => lab.sim.Mode == FlightMode.Air, kFlyBound);
            TestContext.Out.WriteLine($"Fly: {took:F1} s; {lab.State}; {m.Status}");
            Assert.GreaterOrEqual(took, 0f, "Fly from the menu: not airborne within the bound (" + lab.State + ")");
            Assert.AreEqual("Doing: " + lab.brain.Doing, m.Status);

            var p = new Vector3(-6f, 0f, -8f);
            m.Pick(CreatureCommand.WalkTo, p);
            Assert.AreEqual(CreatureCommand.WalkTo, lab.brain.Current, "the brain got WalkTo");
            Assert.AreEqual(p, lab.brain.Point, "WalkTo's point");
            Assert.AreEqual(CreatureCommand.WalkTo, m.Selected);
        }

        // null = the respawned individual has the menu and the command
        static string RespawnProblem(bool forget)
        {
            var lab = new Lab(Vesper(), 3);
            var m = new VesperLabMenu { forgetOnAttach = forget };
            lab.Spawn(m);
            m.Toggle();
            m.Pick(CreatureCommand.Circle);
            lab.Run(1f);
            // respawn (the species asset changed): a new individual and brain
            CommandBrain old = lab.brain;
            lab.Spawn(m, 2);
            if (!m.Open) return "the menu closed on the respawn";
            if (m.Brain != lab.brain || m.Brain == old) return "the menu still commands the old brain";
            if (m.Selected != CreatureCommand.Circle) return $"the menu's command is {m.Selected}, not Circle";
            if (lab.brain.Current != CreatureCommand.Circle) return $"the new brain does {lab.brain.Current}, not Circle";
            float took = lab.Until(() => lab.sim.Mode == FlightMode.Air, kFlyBound);
            TestContext.Out.WriteLine($"Circle after the respawn: airborne after {took:F1} s; {lab.State}");
            if (took < 0f) return "the new individual never took off for its Circle (" + lab.State + ")";
            return null;
        }

        [Test] public void Lab_Respawn_KeepsMenuAndCommand()
        {
            string e = RespawnProblem(false);
            Assert.IsNull(e, e);

            // a WalkTo that had arrived comes back as Stand (it does not walk to the point again)
            var lab = new Lab(Vesper(), 3);
            var m = new VesperLabMenu();
            lab.Spawn(m);
            m.Pick(CreatureCommand.WalkTo, new Vector3(-4f, 0f, -10f));
            float took = lab.Until(() => lab.brain.Arrived, kWalkToBound);
            TestContext.Out.WriteLine($"WalkTo: arrived after {took:F1} s; {lab.State}");
            Assert.GreaterOrEqual(took, 0f, "WalkTo never arrived (" + lab.State + ")");
            lab.Spawn(m, 2);
            Assert.AreEqual(CreatureCommand.Stand, m.Selected);
            Assert.AreEqual(CreatureCommand.Stand, lab.brain.Current);
        }

        [Test] public void Red_Respawn_ForgetsCommand()
        {
            string e = RespawnProblem(true);
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e, "an Attach that forgets the command must fail the respawn check");
        }

        [Test] public void Lab_Spawns_OnFlatGround_InsideArea()
        {
            CreatureSpecies sp = Vesper();
            float s = sp.scale;
            var lab = new Lab(sp, 3);
            EnclosureArea a = lab.area;
            float labRadius = CreatureLab.EnclosureRadius(sp, CreatureLab.MaxScale(sp));
            TestContext.Out.WriteLine($"aviary r {a.radius:F1} m (lab {labRadius:F1} m), safe {a.safeRadius:F1} m, roof {a.maxHeight:F1} m, keepOut {a.keepOut:F1} m");
            Assert.AreEqual(VesperLabLayout.AviaryFactor * labRadius, a.radius, 1e-3f, "the aviary is AviaryFactor x the lab's");
            Assert.AreEqual(1.5f, VesperLabLayout.AviaryFactor);
            Assert.Greater(a.maxHeight, 0f, "a roof");

            // the rocks: the CommandBrain tests' rock, inside the safe disc, apart
            Assert.AreEqual(new Vector3(2.5f, 0f, 1.25f) * s, VesperLabLayout.RockAt(a.center, a.up, 0, 3, s), "rock 0 where the tests put it");
            Assert.AreEqual(new Vector3(1.2f, 0.5f, 1.2f) * s, VesperLabLayout.RockSizeAt(s), "the tested rock size");
            for (int i = 0; i < 3; i++)
            {
                Vector3 r = a.obstacleCenters[i];
                Assert.AreEqual(0f, r.y, 1e-5f, "on the ground");
                Assert.LessOrEqual(a.PlanarDistance(r) + a.obstacleRadii[i], a.safeRadius, $"rock {i} inside the safe disc");
                for (int j = 0; j < i; j++)
                    Assert.Greater((r - a.obstacleCenters[j]).magnitude, a.obstacleRadii[i] + a.obstacleRadii[j] + 2f * a.keepOut, $"rocks {j} and {i} apart");
            }

            VesperLabLayout.Start(a, out Vector3 at, out _);
            Assert.Less(a.ObstacleAt(at), 0, "Vesper starts off every rock's keep-out");
            Vector3 player = VesperLabLayout.PlayerSpot(a);
            Assert.Less(a.PlanarDistance(player), a.radius, "the player stands inside the aviary");
            for (int i = 0; i < 3; i++)
                Assert.Greater((player - a.obstacleCenters[i]).magnitude, a.obstacleRadii[i] + 1f, "the player is off the rocks");

            var m = new VesperLabMenu();
            lab.Spawn(m);
            lab.Run(3f);
            CreatureSim sim = lab.sim;
            Assert.AreEqual(FlightMode.Ground, sim.Mode, "on the ground");
            float maxR = 0f, lowJoint = float.MaxValue;
            foreach (Vector3 j in sim.Joints) { maxR = Mathf.Max(maxR, a.PlanarDistance(j)); lowJoint = Mathf.Min(lowJoint, j.y); }
            Assert.Less(maxR, a.radius, "every joint inside the aviary");
            Assert.GreaterOrEqual(lowJoint, -0.01f * s, "no joint below the ground");
            int planted = 0; float worst = 0f;
            foreach (LegState l in sim.Legs)
                if (!l.stepping) { planted++; worst = Mathf.Max(worst, Mathf.Abs(l.foot.y)); }
            TestContext.Out.WriteLine($"{planted} planted feet, worst |y| {worst:F4} m; joints within {maxR:F1} m, lowest {lowJoint:F2} m; {lab.State}");
            Assert.Greater(planted, 0, "feet planted");
            Assert.Less(worst, 0.01f * s, "planted feet on the flat ground (y = 0)");
        }

        // null = every triangle of the mesh faces out of its box (Cross(b - a, c - a) away from the box's centre)
        static string OutwardProblem(System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<int> tris, Vector3 centre)
        {
            for (int k = 0; k < tris.Count; k += 3)
            {
                Vector3 a = v[tris[k]], b = v[tris[k + 1]], c = v[tris[k + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, (a + b + c) / 3f - centre) <= 0f) return $"triangle {k / 3} faces into the rock";
            }
            return null;
        }

        [Test] public void Lab_Rock_IsTheTestedBox()
        {
            float s = Vesper().scale;
            var v = new System.Collections.Generic.List<Vector3>(); var tris = new System.Collections.Generic.List<int>();
            Vector3 size = VesperLabLayout.RockSizeAt(s);
            VesperLabLayout.RockGeometry(size, v, tris);
            Assert.AreEqual(24, v.Count); Assert.AreEqual(36, tris.Count);
            Vector3 min = v[0], max = v[0];
            foreach (Vector3 p in v) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            Vector3 lo = new Vector3(-0.6f, 0f, -0.6f) * s, hi = new Vector3(0.6f, 0.5f, 0.6f) * s;
            Assert.Less((min - lo).magnitude, 1e-5f, $"base corner {min} (pivot at the base centre)");
            Assert.Less((max - hi).magnitude, 1e-5f, $"top corner {max}");
            int top = 0; foreach (Vector3 p in v) if (Mathf.Abs(p.y - hi.y) < 1e-5f) top++;
            Assert.AreEqual(12, top, "a flat top at the box's height: the top face's 4 vertices and each side face's upper 2");
            string e = OutwardProblem(v, tris, new Vector3(0f, 0.5f * size.y, 0f));
            Assert.IsNull(e, e);

            // rock 0's box is the CommandBrain tests' Rock
            Bounds b = VesperLabLayout.RockBox(Vector3.zero, 0, 3, s);
            var tested = CreatureCommandBrainTests.Rock(s).Boxes[0];
            Assert.Less((b.min - tested.min).magnitude, 1e-4f, $"min {b.min} vs the tests' {tested.min}");
            Assert.Less((b.max - tested.max).magnitude, 1e-4f, $"max {b.max} vs the tests' {tested.max}");
            // the obstacle disc holds the whole footprint
            Assert.GreaterOrEqual(VesperLabLayout.RockRadius(s) + 1e-4f, 0.5f * new Vector2(size.x, size.z).magnitude);
        }

        [Test] public void Red_Rock_InsideOut()
        {
            float s = Vesper().scale;
            var v = new System.Collections.Generic.List<Vector3>(); var tris = new System.Collections.Generic.List<int>();
            VesperLabLayout.RockGeometry(VesperLabLayout.RockSizeAt(s), v, tris);
            for (int k = 0; k < tris.Count; k += 3) { int x = tris[k + 1]; tris[k + 1] = tris[k + 2]; tris[k + 2] = x; }
            string e = OutwardProblem(v, tris, new Vector3(0f, 0.25f * s, 0f));
            TestContext.Out.WriteLine("RED: " + e);
            Assert.IsNotNull(e);
        }

        [Test] public void Menu_Texts_Cached()
        {
            var lab = new Lab(Vesper(), 3);
            var m = new VesperLabMenu();
            lab.Spawn(m);
            string title = m.Title, b0 = m.ButtonLabel(0), closed = m.ClosedText;
            StringAssert.Contains("Doing: ", closed);
            StringAssert.Contains("<", b0, "Stand is the picked one");
            for (int i = 0; i < 3; i++)
            {
                Assert.AreSame(title, m.Title); Assert.AreSame(b0, m.ButtonLabel(0)); Assert.AreSame(closed, m.ClosedText);
            }
            m.Pick(CreatureCommand.Fly);
            Assert.AreNotSame(title, m.Title, "a new command relabels");
            StringAssert.Contains("<", m.ButtonLabel(3)); StringAssert.DoesNotContain("<", m.ButtonLabel(0));
            lab.Run(1f);   // Doing changes (taking off)
            Assert.AreNotEqual(closed, m.ClosedText, "a new Doing rebuilds the closed box");
            StringAssert.Contains(lab.brain.Doing, m.ClosedText);
        }

        [Test] public void Lab_Perch_NeverStalls()
        {
            var lab = new Lab(Vesper(), 3);
            var m = new VesperLabMenu();
            lab.Spawn(m);
            lab.Run(0.5f);
            m.Pick(CreatureCommand.Perch);
            bool Ended() => lab.sim.Mode == FlightMode.Ground && !lab.sim.Flight.Landing && !lab.sim.Flight.Crouching
                            && (lab.brain.OnPerch || lab.brain.PerchGaveUp || lab.brain.NoPerch);
            float took = lab.Until(Ended, kPerchBound);
            TestContext.Out.WriteLine($"Perch in the lab layout: {(took >= 0f ? $"{took:F1} s ({took / lab.T:F1} s x sqrt s)" : "never")}; on the perch {lab.brain.OnPerch}, gave up {lab.brain.PerchGaveUp}, no perch {lab.brain.NoPerch}, misses {lab.brain.PerchMisses}; {lab.State}");
            Assert.GreaterOrEqual(took, 0f, "Perch neither perched nor gave up (" + lab.State + ")");
            Assert.IsFalse(lab.brain.NoPerch, "the scan found the lab's rocks");
            Assert.IsTrue(lab.brain.OnPerch ? lab.brain.Doing == "perched" : lab.brain.Doing.StartsWith("could not reach the perch"), "Doing says how it ended: " + lab.brain.Doing);
        }
    }
}
