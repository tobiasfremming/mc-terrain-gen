using NUnit.Framework;
using UnityEngine;

namespace TerrainGen.Tests
{
    // Tree octopus task 9: the brain (intent and look), prey, perception, strike / hold, stalking, falling
    // and containment, on analytic surfaces.
    public class OctopusBrainTests
    {
        const float Dt = 1f / 60f;

        // A prey that moves on its own (a capsule body), recording what happens to it.
        internal sealed class TestPrey : IPreyTarget
        {
            public Vector3 Pos, Vel;
            public float Radius = 0.12f, Half = 0.3f;
            public bool Grabbable = true, Held;
            public int Grabbed, Released;
            public Vector3 Position => Pos;
            public Vector3 Velocity => Vel;
            public void GrabVolume(out Vector3 a, out Vector3 b, out float r) { a = Pos - Vector3.right * Half; b = Pos + Vector3.right * Half; r = Radius; }
            public bool CanBeGrabbed => Grabbable && !Held;
            public void OnGrabbed() { Grabbed++; Held = true; }
            public void OnReleased() { Released++; Held = false; }
            public void Step(float dt) { if (!Held) Pos += Vel * dt; }
        }

        internal static OctopusController Rig(AnalyticSurfaces s, Vector3 at, out OctopusBody body, out OctopusBrain brain, uint seed = 1)
        {
            body = new OctopusBody(CephalopodSpec.Default, at, Quaternion.identity);
            var c = new OctopusController(body, s, null, seed);
            brain = new OctopusBrain(seed);
            c.Brain = brain;
            return c;
        }

        // A canopy of two parallel bars at 2.5 m (the octopus hangs there) over ground at 0.
        static AnalyticSurfaces Canopy() =>
            new AnalyticSurfaces().AddCapsule(new Vector3(-0.3f, 2.5f, -3f), new Vector3(-0.3f, 2.5f, 3f), 0.06f)
                                  .AddCapsule(new Vector3(0.3f, 2.5f, -3f), new Vector3(0.3f, 2.5f, 3f), 0.06f)
                                  .AddGround(0f);

        [Test] public void Perception_LoS_Occluded()
        {
            foreach (bool wall in new[] { true, false })
            {
                var s = Canopy();
                if (wall) s.AddCapsule(new Vector3(-3f, 1.2f, 1.3f), new Vector3(3f, 1.2f, 1.3f), 0.6f);   // a thick log between
                OctopusController c = Rig(s, new Vector3(0f, 2.0f, 0f), out OctopusBody body, out OctopusBrain brain);
                c.AnchorNearby();
                var prey = new TestPrey { Pos = new Vector3(0f, 0.3f, 3.0f), Grabbable = false };
                brain.Prey.Add(prey);
                for (int f = 0; f < 120; f++) c.Tick(Dt);
                TestContext.Out.WriteLine($"wall {wall}: sightings {brain.Sightings}, mode {brain.Mode}");
                if (wall) Assert.AreEqual(0, brain.Sightings, "seen through the log");
                else Assert.Greater(brain.Sightings, 0, "not seen in plain view");
            }
        }

        // A still prey in reach below: the brain stalks, ambushes, strikes; an arm grabs it, holds 1.5 s and
        // lets go; events fire.
        [Test] public void Strike_ReachesStillPrey()
        {
            OctopusController c = Rig(Canopy(), new Vector3(0f, 2.0f, 0f), out OctopusBody body, out OctopusBrain brain);
            c.AnchorNearby();
            for (int f = 0; f < 120; f++) c.Tick(Dt);
            var prey = new TestPrey { Pos = body.HeadCentre + new Vector3(0.2f, -1.2f, 0.5f) };
            brain.Prey.Add(prey);
            int strikes = 0, grabbed = 0, released = 0;
            c.Events.Strike += _ => strikes++; c.Events.Grabbed += _ => grabbed++; c.Events.Released += _ => released++;
            float grabTime = -1f, releaseTime = -1f;
            for (int f = 0; f < 60 * 8; f++)
            {
                c.Tick(Dt);
                if (grabTime < 0f && prey.Grabbed > 0) grabTime = c.Time;
                if (releaseTime < 0f && prey.Released > 0) releaseTime = c.Time;
            }
            TestContext.Out.WriteLine($"strikes {strikes}, grabbed at {grabTime:F2} s, released at {releaseTime:F2} s, brain {brain.Mode}");
            Assert.Greater(strikes, 0, "no strike");
            Assert.Greater(prey.Grabbed, 0, "the strike never reached the prey");
            Assert.AreEqual(prey.Grabbed, grabbed); Assert.AreEqual(prey.Released, released);
            Assert.Greater(releaseTime, grabTime);
            Assert.AreEqual(1.5f, releaseTime - grabTime, 0.3f, "hold time");
        }

        // A prey that bolts the moment the strike starts: the strike misses, the octopus flashes, cloaks and
        // retreats to the farthest canopy region.
        [Test] public void Strike_Miss_Retreats()
        {
            OctopusController c = Rig(Canopy(), new Vector3(0f, 2.0f, 0f), out OctopusBody body, out OctopusBrain brain);
            brain.CanopyRegions.Add(new Vector3(-0.3f, 2.3f, -2.5f)); brain.CanopyRegions.Add(new Vector3(0.3f, 2.3f, 2.5f));
            c.AnchorNearby();
            for (int f = 0; f < 120; f++) c.Tick(Dt);
            var prey = new TestPrey { Pos = body.HeadCentre + new Vector3(0.2f, -1.2f, 0.5f) };
            brain.Prey.Add(prey);
            c.Events.Strike += _ => { prey.Pos += new Vector3(0f, 0f, 3f); prey.Vel = new Vector3(0f, 0f, 4f); };
            float maxFlash = 0f, maxCloak = 0f; bool retreated = false;
            for (int f = 0; f < 60 * 8; f++)
            {
                prey.Step(Dt);
                c.Tick(Dt);
                maxFlash = Mathf.Max(maxFlash, brain.Flash);
                maxCloak = Mathf.Max(maxCloak, brain.Cloak);
                if (brain.Mode == OctopusBrain.State.Retreat) retreated = true;
            }
            TestContext.Out.WriteLine($"missed {brain.MissedStrikes}, grabbed {prey.Grabbed}, flash {maxFlash:F2}, cloak {maxCloak:F2}");
            Assert.Greater(brain.MissedStrikes, 0, "no miss");
            Assert.AreEqual(0, prey.Grabbed);
            Assert.IsTrue(retreated, "did not retreat");
            Assert.Greater(maxFlash, 0.9f, "no deimatic flash");
            Assert.Greater(maxCloak, 0.8f, "no cloak while retreating");
        }

        // A held prey that keeps moving: the holding arm's tip stays wrapped around it.
        [Test] public void Hold_FollowsMovingPrey()
        {
            OctopusController c = Rig(Canopy(), new Vector3(0f, 2.0f, 0f), out OctopusBody body, out OctopusBrain brain);
            c.AnchorNearby();
            for (int f = 0; f < 120; f++) c.Tick(Dt);
            var prey = new TestPrey { Pos = body.HeadCentre + new Vector3(0.2f, -1.2f, 0.5f) };
            brain.Prey.Add(prey);
            float worst = 0f; int held = 0;
            for (int f = 0; f < 60 * 6; f++)
            {
                // once held, the prey drags itself slowly away
                if (prey.Grabbed > 0 && prey.Released == 0) prey.Pos += new Vector3(0.25f, 0f, 0f) * Dt;
                c.Tick(Dt);
                foreach (TentacleAgent a in c.Agents)
                {
                    if (!a.Holding) continue;
                    held++;
                    prey.GrabVolume(out Vector3 pa, out Vector3 pb, out float r);
                    Vector3 tip = a.Arm.X[a.Arm.N - 1];
                    Vector3 ab = pb - pa;
                    Vector3 q = pa + ab * Mathf.Clamp01(Vector3.Dot(tip - pa, ab) / ab.sqrMagnitude);
                    worst = Mathf.Max(worst, (tip - q).magnitude - r);
                }
            }
            TestContext.Out.WriteLine($"held frames {held}, worst tip gap {worst:F3} m");
            Assert.Greater(held, 30, "never held");
            Assert.Less(worst, 0.08f, "the holding arm lost the prey");
        }

        // Stalking a prey while the player looks straight at the octopus: it freezes (rest intent) and half-
        // cloaks; looking away lets it move again.
        [Test] public void Stalk_FreezesWhenWatched()
        {
            OctopusController c = Rig(Canopy(), new Vector3(0f, 2.0f, 0f), out OctopusBody body, out OctopusBrain brain);
            c.AnchorNearby();
            for (int f = 0; f < 60; f++) c.Tick(Dt);
            var prey = new TestPrey { Pos = body.HeadCentre + new Vector3(0f, -1.8f, 3.5f), Vel = new Vector3(0.3f, 0f, 0f), Grabbable = false };
            brain.Prey.Add(prey);
            brain.ViewerValid = true;
            brain.ViewerPosition = body.HeadCentre + new Vector3(0f, -1f, 6f);
            brain.ViewerForward = (body.HeadCentre - brain.ViewerPosition).normalized;
            int frozen = 0, stalking = 0;
            for (int f = 0; f < 60 * 4; f++)
            {
                prey.Step(Dt);
                c.Tick(Dt);
                if (brain.Mode == OctopusBrain.State.Stalk) { stalking++; if (c.Intent.mode == OctoMode.Rest && brain.Watched) frozen++; }
            }
            TestContext.Out.WriteLine($"stalking {stalking} frames, frozen {frozen}, cloak {brain.Cloak:F2}");
            Assert.Greater(stalking, 30, "never stalked");
            Assert.Greater(frozen, 30, "did not freeze while watched");
            Assert.That(brain.Cloak, Is.InRange(0.45f, 0.7f), "partial cloak while watched");
            brain.ViewerForward = -brain.ViewerForward;   // the player looks away
            for (int f = 0; f < 60; f++) { prey.Step(Dt); c.Tick(Dt); }
            Assert.IsFalse(brain.Watched);
            Assert.AreEqual(OctoMode.Stalk, c.Intent.mode, "still frozen after the player looked away");
        }

        // Dropped beside a tree onto the ground: it lands (impact squash), crawls to the trunk and climbs back
        // into the canopy.
        [Test] public void Fall_LandsAndReturns()
        {
            var s = new AnalyticSurfaces()
                .AddCapsule(new Vector3(1.0f, -0.5f, 0f), new Vector3(1.0f, 3.2f, 0f), 0.16f)   // trunk
                .AddCapsule(new Vector3(1.0f, 2.2f, 0f), new Vector3(-0.8f, 2.6f, 0f), 0.07f)   // a limb
                .AddCapsule(new Vector3(1.0f, 1.4f, 0f), new Vector3(1.0f, 1.6f, 1.6f), 0.06f)  // a lower limb
                .AddGround(0f);
            OctopusController c = Rig(s, new Vector3(-0.4f, 1.2f, 0f), out OctopusBody body, out OctopusBrain brain);
            brain.CanopyRegions.Add(new Vector3(0f, 2.2f, 0f));
            float minY = float.MaxValue, maxYAfterLanding = float.MinValue; bool landed = false, fallen = false;
            for (int f = 0; f < 60 * 60; f++)
            {
                c.Tick(Dt);
                float y = body.HeadCentre.y;
                minY = Mathf.Min(minY, y);
                if (c.Board.Grounded) landed = true;
                if (brain.Mode == OctopusBrain.State.Fallen) fallen = true;
                if (landed) maxYAfterLanding = Mathf.Max(maxYAfterLanding, y);
            }
            TestContext.Out.WriteLine($"lowest head {minY:F2} m, highest after landing {maxYAfterLanding:F2} m, final {body.HeadCentre.y:F2} m, anchors {c.Board.AnchoredCount}, brain {brain.Mode}");
            Assert.IsTrue(landed, "never landed");
            Assert.IsTrue(fallen, "the brain never noticed the fall");
            Assert.Less(minY, 0.6f, "did not reach the ground");
            Assert.Greater(maxYAfterLanding, 1.4f, "did not climb back up");
        }

        // A minute of exploring between canopy regions: every particle stays inside the enclosure.
        [Test] public void Containment_Octopus_60s()
        {
            var s = Canopy();
            s.AddCapsule(new Vector3(-3f, 2.5f, 1.5f), new Vector3(3f, 2.5f, 1.5f), 0.06f).AddCapsule(new Vector3(-3f, 2.5f, -1.5f), new Vector3(3f, 2.5f, -1.5f), 0.06f);
            OctopusController c = Rig(s, new Vector3(0f, 2.0f, 0f), out OctopusBody body, out OctopusBrain brain);
            const float R = 4.5f;
            brain.EnclosureCentre = Vector3.zero; brain.EnclosureRadius = R; brain.HeightCap = 3.5f;
            brain.CanopyRegions.Add(new Vector3(-2.5f, 2.2f, 1.5f)); brain.CanopyRegions.Add(new Vector3(2.5f, 2.2f, -1.5f));
            brain.CanopyRegions.Add(new Vector3(9f, 2.2f, 0f));   // outside: clipped to the enclosure
            c.AnchorNearby();
            float worst = 0f;
            for (int f = 0; f < 60 * 60; f++)
            {
                c.Tick(Dt);
                if (f % 10 != 0) continue;
                for (int i = 0; i < SoftLattice.Count; i++) worst = Mathf.Max(worst, new Vector2(body.Lattice.X[i].x, body.Lattice.X[i].z).magnitude);
                foreach (TentacleChain ch in body.Arms) for (int k = 0; k < ch.N; k++) worst = Mathf.Max(worst, new Vector2(ch.X[k].x, ch.X[k].z).magnitude);
            }
            TestContext.Out.WriteLine($"farthest particle {worst:F2} m from the centre (enclosure {R} m)");
            Assert.Less(worst, R, "left the enclosure");
        }
    }
}
