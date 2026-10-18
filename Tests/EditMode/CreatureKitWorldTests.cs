using System.Collections.Generic;
using CreatureKit;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Creature kit T3.3/T3.4 in the world (Editor: CreatureWorld is a MonoBehaviour): SpawnCreature takes every
    // body plan through one entry - a spine species with the brain its modules ask for, the octopus with its own.
    public class CreatureKitWorldTests
    {
        GameObject go;
        CreatureWorld world;
        ClimbTestMeshes geo;
        readonly List<Object> trash = new List<Object>();

        [SetUp] public void SetUp()
        {
            geo = new ClimbTestMeshes();
            go = new GameObject("KitWorldTest");
            world = go.AddComponent<CreatureWorld>();
            world.creatureMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/TerrainGen/Config/Creatures/Creature.mat");
            world.SetGround(new FlatGroundProbe(), _ => Vector3.up);
            world.SetSurfaces(geo.Registry);
        }

        [TearDown] public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
            geo.Dispose();
            foreach (Object o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        [TestCase("DesertMonitor", typeof(WanderBrain))]
        [TestCase("Tarantula", typeof(WanderBrain))]
        [TestCase("WingedSerpent", typeof(FlightBrain))]
        [TestCase("SkyDragon", typeof(FlightBrain))]
        public void SpawnCreature_Spine_BrainFromModules(string name, System.Type brain)
        {
            CreatureSpecies asset = CreatureKitAssets.LoadAsset(name);
            CreatureSpecies sp = asset.Compiled.Runtime;
            EnclosureArea area = EnclosureArea.For(Vector3.zero, Vector3.up, CreatureLab.EnclosureRadius(sp, CreatureLab.MaxScale(sp)), sp, CreatureLab.MaxScale(sp));
            area.maxHeight = 10f * sp.scale;
            ICreatureBody body = world.SpawnCreature(asset, Vector3.zero, Quaternion.identity, area, 5);
            Assert.IsInstanceOf<SpineBody>(body);
            Assert.AreEqual(1, world.Sims.Count);
            Assert.AreSame(world.Sims[0], ((SpineBody)body).Sim);
            Assert.AreSame(body, world.BodyOf(world.Sims[0]));
            Assert.AreEqual(brain, world.BrainOf(0).GetType());
            for (int f = 0; f < 30; f++) world.TickAll(1f / 60f);
            Assert.IsTrue(world.Despawn(world.Sims[0]));
            Assert.AreEqual(0, world.Sims.Count);
        }

        [Test] public void SpawnCreature_Octopus_OwnBody()
        {
            CreatureSpecies sp = CreatureOctopusWorldTests.Octopus();
            ICreatureBody body = world.SpawnCreature(sp, new Vector3(0f, 0.5f, 0f), Quaternion.identity, null, 3);
            Assert.IsInstanceOf<OctopusCreature>(body);
            Assert.AreEqual(1, world.Bodies.Count);
            Assert.AreEqual(0, world.Sims.Count);
            for (int f = 0; f < 30; f++) world.TickAll(1f / 60f);
            Assert.IsTrue(world.RemoveBody(body));
        }
    }
}
