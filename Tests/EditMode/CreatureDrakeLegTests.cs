using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // The kit's DrakeLeg part (Apex redesign §5.5): a slim dragon hind leg on the shared two-bone gait. The fixture: the
    // FlowBody fixture (CreatureFlowBodyTests.Body) with a mirrored DrakeLeg pair at joint 10 (three toes and a hallux).
    // Invariants: it walks as a two-bone leg (the kit's Gait check, drawn feet included); planted, its lowest talon is on
    // the ground (the gait's foot point is the toes' contact); no triangle inverts over the walking circle; only its own
    // mesh draws the leg (no Leg2Bone limb as well); the haunch roots inside the FlowBody's drawn section. Each has a RED.
    public class CreatureDrakeLegTests
    {
        const int kAttach = 10;

        static SpeciesCtx Ctx(int joints) => new SpeciesCtx(new CreaturePart[0], new CreatureModule[0], joints, false);

        internal static DrakeLeg Leg(float hipX = 0.09f) => new DrakeLeg
        {
            id = "hind", at = new PartSocket(kAttach, 0, new Vector3(hipX, -0.05f, 0f)), mirror = true,
            restOffset = new Vector3(0.2f, 0f, 0.03f), upperLength = 0.24f, lowerLength = 0.26f, upperRadius = 0.034f, lowerRadius = 0.02f,
            pole = new Vector3(0.3f, 0.2f, 0.9f), ankleHeight = 0.14f, footLength = 0.12f, hallux = true,
            stepThreshold = 0.22f, stepDuration = 0.18f, stepLift = 0.06f,
        };

        static CreatureSpecies Species(DrakeLeg leg) =>
            FlowKit.Build("DrakeLegFixture", 1f, new List<CreaturePart> { CreatureFlowBodyTests.Body(), leg, CreatureKitComposeTests.TwoEyes() },
                          new CreatureModule[] { new Walk { speed = 0.5f, turnRateDeg = 160f, accel = 2f, turnSlowdown = 0.5f } });

        // ------------------------------------------------------------ data

        [Test] public void DrakeLeg_Validate_RejectsBadData()
        {
            DrakeLeg ok = Leg(); ok.at = new PartSocket(kAttach, -1, ok.at.offset);
            Assert.IsNull(ok.Validate(Ctx(24)));
            DrakeLeg Bad(System.Action<DrakeLeg> f) { var b = (DrakeLeg)ok.Clone(); f(b); return b; }
            Assert.IsNotNull(Bad(b => b.lowerLength = 0f).Validate(Ctx(24)));
            Assert.IsNotNull(Bad(b => b.upperRadius = 0f).Validate(Ctx(24)));
            Assert.IsNotNull(Bad(b => b.ankleHeight = -0.01f).Validate(Ctx(24)));
            Assert.IsNotNull(Bad(b => b.ankleHeight = 0.45f).Validate(Ctx(24)), "a hock as high as the leg");
            Assert.IsNotNull(Bad(b => b.toes = 1).Validate(Ctx(24)));
            Assert.IsNotNull(Bad(b => b.toes = 5).Validate(Ctx(24)));
            Assert.IsNotNull(Bad(b => b.toeCount = 3).Validate(Ctx(24)), "toes are counted in `toes`");
            Assert.IsNotNull(Bad(b => b.footLength = 0f).Validate(Ctx(24)));
            Assert.IsNotNull(Bad(b => b.role = LimbRole.WingArm).Validate(Ctx(24)));
            Assert.IsNotNull(Bad(b => b.haunch = 0.5f).Validate(Ctx(24)));
        }

        [Test] public void DrakeLeg_LegSpec_IsATwoBoneLeg()
        {
            DrakeLeg p = Leg(); p.at = new PartSocket(kAttach, 1, p.at.offset);
            LegSpec l = p.ToLegSpec();
            Assert.IsFalse(l.IsArthropod);
            Assert.IsFalse(l.digitigrade, "the Leg2Bone digitigrade mesh must never draw a DrakeLeg");
            Assert.AreEqual(0.24f, l.upperLength); Assert.AreEqual(0.26f, l.lowerLength);
            Assert.AreEqual(0.034f, l.upperRadius); Assert.AreEqual(0.02f, l.lowerRadius);
            Assert.AreEqual(0.14f, l.AnkleHeight, 1e-6f, "the hock is the gait's ankle");
            Assert.AreEqual(4, l.toeCount, "three toes and the hallux");
            Assert.AreEqual(0f, l.kneeBlend);
            p.ankleHeight = 0f;
            Assert.AreEqual(0.3f * 0.5f, p.ToLegSpec().AnkleHeight, 1e-6f, "auto hock: 0.3 x the leg's length");
        }

        // ------------------------------------------------------------ gait

        [Test] public void DrakeLeg_WalksLikeATwoBoneLeg()
        {
            CreatureSpecies sp = Species(Leg());
            Assert.IsNull(sp.Validate());
            Assert.AreEqual(2, sp.legs.Length);
            Assert.IsNull(KitChecks.GaitProblem(sp));
        }

        // ------------------------------------------------------------ talons on the ground

        // Walking a weave for 6 s, every 2nd tick: each planted leg's lowest talon vertex (layer 2, rigid to its foot
        // bone), skinned, is within 1 cm x s of the flat ground (y = 0).
        static string TalonProblem(CreatureSpecies sp, out float worst)
        {
            worst = 0f;
            var skinned = new CreaturePteroWingTests.Skinned(sp);
            int n = sp.legs.Length;
            var foot = new int[n];
            for (int l = 0; l < n; l++) foot[l] = sp.LegBone(l, 2);
            var low = new float[n];
            CreatureSim sim = FlowKit.Sim(sp, sp.scale);
            float tol = 0.01f * sp.scale;
            int samples = 0;
            string where = null;
            for (int t = 0; t < 360; t++)
            {
                float a = t / 60f * 1.5f;
                sim.Tick(FlowKit.kDt, new Vector3(Mathf.Sin(a) * 0.6f, 0f, 2f + t / 60f * 0.6f));
                if (t < 30 || t % 2 != 0) continue;
                skinned.Pose(sim);
                for (int l = 0; l < n; l++) low[l] = float.MaxValue;
                for (int i = 0; i < skinned.P.Length; i++)
                {
                    if (skinned.Part(i) != PartMeshEmitter.PartFoot) continue;
                    int b = (int)skinned.Uv2[i].x;
                    for (int l = 0; l < n; l++) if (b == foot[l]) low[l] = Mathf.Min(low[l], skinned.P[i].y);
                }
                for (int l = 0; l < n; l++)
                {
                    if (sim.Legs[l].stepping) continue;
                    if (low[l] == float.MaxValue) return $"leg {l}: no talon vertices (layer {PartMeshEmitter.PartFoot}) on its foot bone";
                    samples++;
                    if (Mathf.Abs(low[l]) > Mathf.Abs(worst)) { worst = low[l]; where = $"leg {l} t={sim.Time:F2}"; }
                }
            }
            TestContext.Out.WriteLine($"{sp.name}: planted lowest talon worst {worst * 1000f:F2} mm ({where}), {samples} samples");
            if (samples < 100) return $"only {samples} planted samples: the check does not bite";
            return Mathf.Abs(worst) <= tol ? null : $"a planted talon is {worst * 1000f:F1} mm off the ground ({where}; tolerance {tol * 1000f:F0} mm)";
        }

        [Test] public void DrakeLeg_TalonsOnTheGround()
        {
            Assert.IsNull(TalonProblem(Species(Leg()), out _));
        }

        // RED: the gait without the hock (the runtime spec's ankle height < 0: the shin's end drawn on the contact point)
        // while the part still hangs its foot a hock's height below it - the talons sink, and the check catches it.
        [Test] public void Red_DrakeLeg_Floating()
        {
            CreatureSpecies sp = Species(Leg());
            for (int l = 0; l < sp.legs.Length; l++) sp.legs[l].ankleHeight = -1f;
            Assert.AreEqual(0f, sp.legs[0].AnkleHeight);
            string p = TalonProblem(sp, out float worst);
            Assert.IsNotNull(p);
            Assert.Less(worst, -0.05f, "the foot sinks by about the hock's height");
        }

        // ------------------------------------------------------------ max bend

        [Test] public void DrakeLeg_MaxBend_NoInvertedTriangles()
        {
            Assert.IsNull(KitChecks.MaxBendProblem(Species(Leg())));
        }

        // ------------------------------------------------------------ drawn once

        // The mesh vertices skinned to leg l's bones (either influence) - every limb mesh drawn for it.
        static int LegVertices(Mesh mesh, CreatureSpecies sp, int l)
        {
            var uv2 = new List<Vector4>(); mesh.GetUVs(2, uv2);
            int b0 = sp.LegBone(l, 0), b2 = sp.LegBone(l, 2), n = 0;
            foreach (Vector4 s in uv2)
            {
                bool x = s.x >= b0 && s.x <= b2 && s.z > 0f, y = s.y >= b0 && s.y <= b2 && s.w > 0f;
                if (x || y) n++;
            }
            return n;
        }

        // What the DrakeLeg itself emits for limb ordinal l (a fresh emitter on the species' bind rest).
        static int OwnVertices(CreatureSpecies sp, int l, bool alsoLeg2Bone)
        {
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            int[] ord = KitView.Ordinals(parts);
            PartMeshEmitter m = PartMeshEmitter.ForBind(sp);
            for (int i = 0; i < parts.Count; i++)
            {
                if (!(parts[i] is DrakeLeg d) || ord[i] != l) continue;
                var ctx = new PartCtx(sp, i, l, SizeCtx.One, m.Rest);
                d.EmitMesh(m, ctx);
                if (alsoLeg2Bone) Leg2Bone.From(d.ToLegSpec(), d.id).EmitMesh(m, ctx);   // RED: the shared limb path drawing it too
            }
            return m.VertexCount;
        }

        static string DrawnOnceProblem(int inMesh, int own, int l) =>
            own > 0 && inMesh == own ? null : $"leg {l}: {inMesh} mesh vertices on its bones, its DrakeLeg draws {own}";

        [Test] public void DrakeLeg_DrawnOnce()
        {
            CreatureSpecies sp = Species(Leg());
            Mesh mesh = FlowKit.Mesh(sp);
            try
            {
                for (int l = 0; l < sp.legs.Length; l++)
                    Assert.IsNull(DrawnOnceProblem(LegVertices(mesh, sp, l), OwnVertices(sp, l, false), l));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // RED: a Leg2Bone limb drawn for the same leg as well is caught (the check compares a double draw with the part's own).
        [Test] public void Red_DrakeLeg_DrawnTwice()
        {
            CreatureSpecies sp = Species(Leg());
            Mesh mesh = FlowKit.Mesh(sp);
            try
            {
                int both = OwnVertices(sp, 0, true);
                Assert.Greater(both, OwnVertices(sp, 0, false));
                Assert.IsNotNull(DrawnOnceProblem(both, OwnVertices(sp, 0, false), 0));
                Assert.IsNotNull(DrawnOnceProblem(LegVertices(mesh, sp, 0), both, 0));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // ------------------------------------------------------------ the haunch in the body

        // The haunch's buried pole (the leg's first vertex) lies inside the FlowBody's drawn section at the attach joint,
        // also when the hip is set out on the flank (x 0.15 m: past the trunk's 0.1 m half width there).
        static string HaunchProblem(CreatureSpecies sp)
        {
            IReadOnlyList<CreaturePart> parts = KitView.Parts(sp);
            int[] ord = KitView.Ordinals(parts);
            FlowBody body = DrakeLeg.Body(sp);
            for (int i = 0; i < parts.Count; i++)
            {
                if (!(parts[i] is DrakeLeg d)) continue;
                PartMeshEmitter m = PartMeshEmitter.ForBind(sp);
                d.EmitMesh(m, new PartCtx(sp, i, ord[i], SizeCtx.One, m.Rest));
                Vector3 pole = m.Position(0);
                if (!DrakeLeg.InsideBody(m, body, sp.legs[ord[i]].attachJoint, pole, 1f)) return $"{d.id}: the haunch's root {pole} is outside the body";
            }
            return null;
        }

        [Test] public void DrakeLeg_HaunchRootsInTheBody()
        {
            Assert.IsNull(HaunchProblem(Species(Leg())));
            Assert.IsNull(HaunchProblem(Species(Leg(0.15f))));
        }

        // RED: the fixed root depth (no body query) leaves a flank-set hip's haunch outside the body.
        [Test] public void Red_DrakeLeg_HaunchIgnoringTheBody()
        {
            DrakeLeg.IgnoreBodyForTest = true;
            try { Assert.IsNotNull(HaunchProblem(Species(Leg(0.15f)))); }
            finally { DrakeLeg.IgnoreBodyForTest = false; }
        }
    }
}
