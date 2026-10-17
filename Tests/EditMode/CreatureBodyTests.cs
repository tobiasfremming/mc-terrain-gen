using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TerrainGen.Tests
{
    // Tree octopus task 1: the ICreatureBody refactor of the renderer and world.
    public class CreatureBodyTests
    {
        readonly List<Object> trash = new List<Object>();
        readonly List<CreatureRenderer> renderers = new List<CreatureRenderer>();

        [TearDown]
        public void TearDown()
        {
            foreach (var r in renderers) r.Dispose();
            renderers.Clear();
            foreach (var o in trash) if (o != null) Object.DestroyImmediate(o);
            trash.Clear();
        }

        T Keep<T>(T o) where T : Object { trash.Add(o); return o; }

        CreatureRenderer NewRenderer(CreatureSpecies sp)
        {
            var r = new CreatureRenderer(sp, null);
            renderers.Add(r);
            return r;
        }

        internal static CreatureSpecies OctopusSpecies()
        {
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = "TestOctopus";
            sp.bodyPlan = CreatureBodyPlan.Cephalopod;
            sp.cephalopod = CephalopodSpec.Default;
            sp.scale = 1f;
            return sp;
        }

        // A bare octopus body (no agents, no brain) for the renderer.
        internal sealed class BareOctopus : ICreatureBody
        {
            public readonly OctopusBody Body;
            public BareOctopus(CreatureSpecies sp, Vector3 at, float scale) { Species = sp; Body = new OctopusBody(sp.cephalopod, at, Quaternion.identity, scale); }
            public CreatureSpecies Species { get; }
            public float SizeScale => Body.SizeScale;
            public void WriteBoneWorld(Matrix4x4[] dst, int offset) => Body.WriteBoneWorld(dst, offset);
            public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) => Body.Encapsulate(ref lo, ref hi);
            public void Tick(float dt) => Body.Tick(dt);
            public bool DynamicLook => true;
            public float Cloak;
            public void UpdateLook(ref CreatureInstance inst) { inst.cloak = Cloak; }
        }

        // Record-then-refactor, by construction: the former spine path recomputed here from scratch (sim bone
        // worlds x bind poses with the same operator; bounds over joints then feet, grown by 2 x reach x scale)
        // must equal the refactored renderer bit for bit, through a walk with turns.
        [Test] public void Renderer_SpinePath_BitIdenticalToFormerPath()
        {
            var sp = Keep(CreatureTestSpecies.Lizardish());
            var r = NewRenderer(sp);
            var sims = new List<CreatureSim>();
            for (int i = 0; i < 3; i++)
            {
                var s = new CreatureSim(sp, new FlatGroundProbe(), _ => Vector3.up, new Vector3(3f * i, 0f, 0f), Vector3.forward, 1f + 0.2f * i, i);
                sims.Add(s);
                r.Add(s, new CreatureInstance { seed = i, sizeScale = s.SizeScale });
            }
            Matrix4x4[] bind = CreatureRestPose.BindPoses(sp);
            var world = new Matrix4x4[sp.BoneCount];
            float halfHeight = 0f;
            for (int i = 0; i < sp.JointCount; i++) halfHeight = Mathf.Max(halfHeight, sp.joints[i].halfHeight);
            float reach = Mathf.Max(sp.MaxLateralReach, Mathf.Max(sp.headCapLength, sp.tailTipLength), halfHeight) + sp.eyeRadius + 0.05f;
            for (int f = 0; f < 240; f++)
            {
                float a = f / 60f;
                for (int i = 0; i < sims.Count; i++) sims[i].Tick(1f / 60f, new Vector3(3f * i + 2f * Mathf.Sin(a), 0f, 3f + 2f * Mathf.Cos(a)));
                if (f % 20 != 19) continue;
                r.Render();
                Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
                float maxScale = 0f;
                for (int i = 0; i < sims.Count; i++)
                {
                    sims[i].WriteBoneWorld(world, 0);
                    for (int b = 0; b < sp.BoneCount; b++)
                    {
                        Matrix4x4 want = world[b] * bind[b], got = r.SkinForTest[i * sp.BoneCount + b];
                        for (int k = 0; k < 16; k++) Assert.AreEqual(want[k], got[k], 0f, $"frame {f} sim {i} bone {b} [{k}]");
                    }
                    foreach (Vector3 j in sims[i].Joints) { lo = Vector3.Min(lo, j); hi = Vector3.Max(hi, j); }
                    foreach (LegState l in sims[i].Legs) { lo = Vector3.Min(lo, l.foot); hi = Vector3.Max(hi, l.foot); }
                    maxScale = Mathf.Max(maxScale, sims[i].SizeScale);
                }
                var bounds = new Bounds();
                bounds.SetMinMax(lo, hi);
                bounds.Expand(2f * reach * maxScale);
                Assert.AreEqual(bounds.min, r.LastBounds.min, $"frame {f}");
                Assert.AreEqual(bounds.max, r.LastBounds.max, $"frame {f}");
            }
            Assert.IsTrue(r.Remove(sims[1]));
            Assert.AreEqual(2, r.Count);
        }

        // The renderer builds the octopus mesh and bind poses from the species' plan, skins 201 bones, and its
        // culling bounds contain every skinned vertex, arms included, through a hanging swing.
        [Test] public void Renderer_Bounds_ContainArms()
        {
            var sp = Keep(OctopusSpecies());
            Assert.IsNull(sp.Validate());
            var r = NewRenderer(sp);
            Assert.AreEqual(201, r.BonesPerCreature);
            var body = new BareOctopus(sp, new Vector3(1f, 3f, -2f), 1.3f);
            body.Body.Arms[0].Pin(23, body.Body.Arms[0].X[23], Vector3.up);
            r.Add(body, new CreatureInstance { sizeScale = 1.3f });
            OctopusMeshBuilder.Data d = OctopusMeshBuilder.BuildData(sp.cephalopod);
            for (int f = 0; f < 300; f++)
            {
                body.Tick(1f / 60f);
                if (f % 30 != 29) continue;
                r.Render();
                Bounds b = r.LastBounds;
                Matrix4x4[] skin = r.SkinForTest;
                for (int i = 0; i < d.pos.Count; i++)
                {
                    Vector3 p = OctopusMeshTests.Skin(d, skin, i);
                    Assert.IsTrue(b.Contains(p), $"frame {f}: vertex {i} (part {d.uv1[i].x}) at {p} outside {b}");
                }
            }
        }

        // Dynamic look: a body that reports DynamicLook has its per-instance data refreshed every Render.
        [Test] public void Renderer_DynamicLook_UpdatesInstances()
        {
            var sp = Keep(OctopusSpecies());
            var r = NewRenderer(sp);
            var body = new BareOctopus(sp, Vector3.zero, 1f);
            r.Add(body, new CreatureInstance { sizeScale = 1f, seed = 5f });
            body.Cloak = 0.7f;
            r.Render();
            Assert.AreEqual(0.7f, r.InstancesForTest[0].cloak);
            Assert.AreEqual(5f, r.InstancesForTest[0].seed);
        }
    }
}
