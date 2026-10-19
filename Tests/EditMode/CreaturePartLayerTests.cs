using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Creature kit T4.1: the look-layer table (Kit/PartLayers.cs, Shaders/Creature/CreatureLayers.hlsl). Offline:
    //   - the legacy mapping is the identity (every shipped species' table = the registry = what the old shader's
    //     part-id branches did, written out below from the old shader's code)
    //   - pixel equivalence on the CPU: the old fragment surface (part ids) and the new one (the table) evaluated in
    //     C# (CreatureShadeReference) for many fragments of every species, every legacy id: bit-equal
    //   - C# <-> HLSL constants in step; Creature.shader no longer branches on part ids
    // In Unity (CreatureShaderLayerTests): the GPU guard, the frozen old shader vs the new one within 1/255.
    public class CreaturePartLayerTests
    {
        // ------------------------------------------------------------ the old shader's part-id branches, as data

        struct OldDispatch { public bool membrane, eye; public int patternMode; public bool fineGrain; public TintSource tint; public float tintAmount; public bool sheen, shimmer; }

        // Read straight off the pre-table Creature.shader / CreaturePattern.hlsl (CreatureLegacyReference.shader):
        //   part == 8 -> membrane branch; part == 3 -> eye; pattern: 0 for eyes and feet, the limb branch for 1;
        //   grain: fine for 1 and 2; foot lerp 0.3 to _PatternColor; band (9) lerp 0.9 to _BandColor; sheen and
        //   shimmer on every non-eye fragment.
        static OldDispatch Old(int part)
        {
            var d = new OldDispatch
            {
                membrane = part == 8, eye = part == 3,
                patternMode = part == 3 || part == 2 ? 0 : part == 1 ? 2 : 1,
                fineGrain = part == 1 || part == 2,
                tint = part == 2 ? TintSource.Pattern : part == 9 ? TintSource.Band : TintSource.Own,
                tintAmount = part == 2 ? 0.3f : part == 9 ? 0.9f : 0f,
            };
            d.sheen = d.shimmer = !d.eye && !d.membrane;
            return d;
        }

        static OldDispatch FromRow(PartLayer r)
        {
            bool Has(LayerFlags f) => (r.flags & f) != 0;
            var d = new OldDispatch
            {
                membrane = r.model == ShadingModel.Membrane, eye = r.model == ShadingModel.Eye,
                patternMode = r.model == ShadingModel.Eye || Has(LayerFlags.NoPattern) ? 0 : Has(LayerFlags.LimbPattern) ? 2 : 1,
                fineGrain = Has(LayerFlags.FineGrain),
                tint = r.tintAmount > 0f ? r.tintSource : TintSource.Own, tintAmount = r.tintAmount,
                sheen = Has(LayerFlags.Sheen) && r.model != ShadingModel.Membrane, shimmer = Has(LayerFlags.Shimmer) && r.model != ShadingModel.Membrane,
            };
            if (d.membrane) { d.eye = false; d.patternMode = 1; d.fineGrain = false; }   // the membrane branch returns before any of it
            return d;
        }

        static string Diff(OldDispatch a, OldDispatch b)
        {
            if (a.membrane || b.membrane) return a.membrane == b.membrane ? null : $"membrane {a.membrane} vs {b.membrane}";
            if (a.eye != b.eye) return $"eye {a.eye} vs {b.eye}";
            if (a.patternMode != b.patternMode) return $"pattern {a.patternMode} vs {b.patternMode}";
            if (!a.eye && a.fineGrain != b.fineGrain) return $"fine grain {a.fineGrain} vs {b.fineGrain}";
            if (a.tint != b.tint || a.tintAmount != b.tintAmount) return $"tint {a.tint} {a.tintAmount} vs {b.tint} {b.tintAmount}";
            if (a.sheen != b.sheen || a.shimmer != b.shimmer) return $"sheen/shimmer {a.sheen}/{a.shimmer} vs {b.sheen}/{b.shimmer}";
            return null;
        }

        // The part ids a species restyles with its Layers look module (the GiantCentipede: a deliberate new look, so
        // those rows are the module's, not the registry's; every other row still is).
        static HashSet<int> Restyled(CreatureSpecies sp)
        {
            var ids = new HashSet<int>();
            foreach (CreatureModule m in KitView.Modules(sp.Compiled.Runtime))
                if (m is Layers l && l.rows != null) foreach (LayerDef d in l.rows) ids.Add(d.partId);
            return ids;
        }

        static IEnumerable<string> SpineSpecies()
        {
            foreach (string n in CreatureKitAssets.Species())
            {
                CreatureSpecies sp = CreatureKitAssets.Load(n);
                if (sp != null && sp.bodyPlan != CreatureBodyPlan.Cephalopod) yield return n;
            }
        }

        // ------------------------------------------------------------ the legacy mapping

        [Test]
        public void PartLayers_LegacyMapping_Identity()
        {
            // the registry rows ARE the old branches, every id 0..15 (ids past the registry shade as body skin, as before)
            for (int id = 0; id < PartLayers.MaxParts; id++)
            {
                if (id >= 4 && id <= 7) continue;   // octopus ids: their own shader family (in the spine shader: body skin)
                Assert.IsNull(Diff(Old(id), FromRow(PartLayers.LegacyRow(id))), $"part {id}");
            }
            for (int id = 4; id <= 7; id++) Assert.IsTrue(PartLayers.Registry[id].Octopus && PartLayers.LegacyRow(id).Equals(PartLayers.LegacyRow(0)), $"octopus id {id}");
            // every shipped species gets exactly the registry, except the rows its Layers module restyles (those are the
            // module's rows exactly)
            foreach (string n in CreatureKitAssets.Species())
            {
                CreatureSpecies sp = CreatureKitAssets.Load(n);
                PartLayer[] rows = PartLayers.Rows(sp);
                Assert.AreEqual(PartLayers.MaxParts, rows.Length);
                var own = new Dictionary<int, PartLayer>();
                foreach (CreatureModule m in KitView.Modules(sp.Compiled.Runtime))
                    if (m is Layers l && l.rows != null) foreach (LayerDef d in l.rows) own[d.partId] = d.ToRow();
                for (int id = 0; id < PartLayers.MaxParts; id++)
                    Assert.IsTrue(rows[id].Equals(own.TryGetValue(id, out PartLayer r) ? r : PartLayers.LegacyRow(id)), $"{n}: part {id} is {rows[id]}");
            }
        }

        // RED: the identity check bites - a foot that lost its fine grain, a band tinted 0.8, an eye with sheen.
        [Test]
        public void Red_PartLayers_ChangedRow_IsCaught()
        {
            PartLayer foot = PartLayers.LegacyRow(2); foot.flags &= ~LayerFlags.FineGrain;
            PartLayer band = PartLayers.LegacyRow(9); band.tintAmount = 0.8f;
            PartLayer eye = PartLayers.LegacyRow(3); eye.flags |= LayerFlags.Sheen;
            Assert.IsNotNull(Diff(Old(2), FromRow(foot)));
            Assert.IsNotNull(Diff(Old(9), FromRow(band)));
            Assert.IsNotNull(Diff(Old(3), FromRow(eye)));
        }

        [Test]
        public void PartLayers_Encode_RoundTrips_AndKeepsPaletteSourcesUndecoded()
        {
            Vector4[] t = PartLayers.Table(null, true);
            Assert.AreEqual(PartLayers.Length, t.Length);
            for (int id = 0; id < PartLayers.MaxParts; id++)
            {
                PartLayer r = PartLayers.LegacyRow(id);
                Assert.AreEqual((float)(int)r.model, t[2 * id].x, $"model {id}");
                Assert.AreEqual((float)(int)r.flags, t[2 * id].y, $"flags {id}");
                Assert.AreEqual(r.tintAmount, t[2 * id].z, $"tint {id}");
                Assert.AreEqual(1f, t[2 * id].w, $"smoothness {id}");
                Assert.AreEqual((float)(int)r.tintSource, t[2 * id + 1].w, $"source {id}");
                // the palette tints come from the shader's SetColor values: the table carries no colour for them
                Assert.AreEqual(Vector3.zero, new Vector3(t[2 * id + 1].x, t[2 * id + 1].y, t[2 * id + 1].z), $"colour {id}");
            }
        }

        // An Own tint is decoded sRGB -> linear exactly once (SetVectorArray does not; SetColor would).
        [Test]
        public void PartLayers_OwnTint_DecodedOnce()
        {
            var row = new PartLayer(ShadingModel.Claw, LayerFlags.NoPattern, TintSource.Own, 0.85f) { tint = new Color(0.8f, 0.5f, 0.2f, 1f) };
            var rows = new PartLayer[PartLayers.MaxParts];
            for (int i = 0; i < rows.Length; i++) rows[i] = PartLayers.LegacyRow(i);
            rows[10] = row;
            var lin = new Vector4[PartLayers.Length]; var raw = new Vector4[PartLayers.Length];
            PartLayers.Encode(rows, lin, true);
            PartLayers.Encode(rows, raw, false);
            Color want = row.tint.linear;
            Assert.AreEqual(want.r, lin[21].x, 1e-6f); Assert.AreEqual(want.g, lin[21].y, 1e-6f); Assert.AreEqual(want.b, lin[21].z, 1e-6f);
            Assert.AreEqual(0.8f, raw[21].x); Assert.Less(lin[21].x, 0.8f, "decoded");
            Assert.AreEqual(PartLayers.ClawSmoothness, lin[20].w, "Claw's default smoothness scale");
        }

        // ------------------------------------------------------------ pixel equivalence on the CPU

        static CreatureShadeReference.Palette PaletteOf(CreatureSpecies sp)
        {
            CreatureLook l = sp.look;
            Vector3 V(Color c) => new Vector3(c.r, c.g, c.b);
            float len = Mathf.Max(sp.BodyLength, 1e-3f);
            return new CreatureShadeReference.Palette
            {
                dorsal = V(l.dorsal), flank = V(l.flank), belly = V(l.belly), pattern = V(l.pattern), band = V(l.bandColor), membrane = V(l.membrane),
                patternType = (float)l.patternType, patternParams = new Vector4(l.patternScale, l.patternContrast, l.patternSoftness, l.spotScale),
                surfaceParams = new Vector4(l.smoothness, l.rimStrength, l.subsurface, 0f), bodyParams = new Vector4(len, 1f / len, 0.45f * len, 0f),
                sheenA = l.sheen, shimmerA = l.shimmer,
                gradientWarp = 0.25f, mottle = 0.08f, scaleSize = 0.0045f, scaleColumns = 96f, scaleTint = 0.07f, scaleGroove = 0.25f, scaleBump = 0.0003f, eyeSmoothness = 0.9f,
            };
        }

        static CreatureShadeReference.Fragment RandomFragment(System.Random r, int part, float bodyLength)
        {
            float F(float a, float b) => a + (float)r.NextDouble() * (b - a);
            return new CreatureShadeReference.Fragment
            {
                uv0 = new Vector2(F(0f, bodyLength), F(0f, 1f)), uv1 = new Vector2(part, F(-1f, 1f)),
                radius = F(0.002f, 0.12f), seed = F(-50f, 50f),
                // from sub-scale (grain visible) to many scales per pixel (grain faded)
                fwUv0 = new Vector2(F(1e-5f, 4e-3f), F(1e-4f, 0.03f)),
            };
        }

        [Test]
        public void Shading_Layered_EqualsLegacy_OnTheCpu_EveryLegacyId()
        {
            int fragments = 0, checkedSpecies = 0;
            foreach (string n in SpineSpecies())
            {
                CreatureSpecies sp = CreatureKitAssets.Load(n);
                HashSet<int> restyled = Restyled(sp);
                Vector4[] table = PartLayers.Table(sp, true);
                CreatureShadeReference.Palette baseP = PaletteOf(sp);
                var rnd = new System.Random(n.GetHashCode() & 0x7FFF ^ 0x2D1);
                for (int variant = 0; variant < 6; variant++)
                {
                    // the species' own pattern, then every pattern type; sheen / shimmer forced on in odd variants
                    CreatureShadeReference.Palette p = baseP;
                    if (variant > 0) p.patternType = variant - 1;
                    if ((variant & 1) == 1) { p.sheenA = Mathf.Max(p.sheenA, 0.5f); p.shimmerA = Mathf.Max(p.shimmerA, 0.5f); }
                    for (int part = 0; part <= 20; part++)
                    {
                        if (part >= 4 && part <= 7) continue;   // octopus ids never reach this shader
                        if (restyled.Contains(part)) continue;   // a deliberate new look (Layers module), not the legacy one
                        for (int k = 0; k < 24; k++)
                        {
                            CreatureShadeReference.Fragment f = RandomFragment(rnd, part, p.bodyParams.x);
                            CreatureShadeReference.Surface a = CreatureShadeReference.Legacy(p, f), b = CreatureShadeReference.Layered(p, table, f);
                            if (!a.BitEquals(b)) Assert.Fail($"{n} part {part} pattern {p.patternType} uv0 {f.uv0} uv1 {f.uv1}:\n  legacy  {a}\n  layered {b}");
                            fragments++;
                        }
                    }
                }
                checkedSpecies++;
            }
            Assert.Greater(checkedSpecies, 4);
            TestContext.Out.WriteLine($"{fragments} fragments over {checkedSpecies} species bit-equal");
        }

        // RED: the CPU comparison bites - a table whose foot row lost its tint differs on feet only.
        [Test]
        public void Red_Shading_WrongRow_Differs()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("DesertMonitor");
            Vector4[] table = PartLayers.Table(sp, true);
            table[2 * 2].z = 0f;
            CreatureShadeReference.Palette p = PaletteOf(sp);
            var r2 = new System.Random(11);
            for (int k = 0; k < 50; k++)
            {
                CreatureShadeReference.Fragment f = RandomFragment(r2, 0, 1f);
                Assert.IsTrue(CreatureShadeReference.Legacy(p, f).BitEquals(CreatureShadeReference.Layered(p, table, f)), "the body is untouched");
            }
            var r3 = new System.Random(13); int differ = 0;
            for (int k = 0; k < 50; k++)
            {
                CreatureShadeReference.Fragment f = RandomFragment(r3, 2, 1f);
                if (!CreatureShadeReference.Legacy(p, f).BitEquals(CreatureShadeReference.Layered(p, table, f))) differ++;
            }
            Assert.Greater(differ, 25, "feet differ once their row changes");
        }

        // The new models are real choices: a Chitin or Claw row shades differently from the skin it replaces.
        [Test]
        public void Shading_NewModels_DifferFromSkin()
        {
            CreatureSpecies sp = CreatureKitAssets.Load("Tarantula");
            CreatureShadeReference.Palette p = PaletteOf(sp);
            var rows = PartLayers.Rows(sp);
            rows[10] = new PartLayer(ShadingModel.Chitin, LayerFlags.Sheen);
            rows[11] = new PartLayer(ShadingModel.Claw, LayerFlags.NoPattern, TintSource.Own, 0.85f) { tint = new Color(0.1f, 0.08f, 0.06f, 1f) };
            var t = new Vector4[PartLayers.Length];
            PartLayers.Encode(rows, t, true);
            var rnd = new System.Random(3);
            for (int k = 0; k < 20; k++)
            {
                CreatureShadeReference.Fragment f = RandomFragment(rnd, 10, 1f);
                CreatureShadeReference.Surface skin = CreatureShadeReference.Layered(p, t, new CreatureShadeReference.Fragment { uv0 = f.uv0, uv1 = new Vector2(0f, f.uv1.y), radius = f.radius, seed = f.seed, fwUv0 = f.fwUv0 });
                CreatureShadeReference.Surface chitin = CreatureShadeReference.Layered(p, t, f);
                Assert.AreEqual(0f, chitin.bumpHeight, "chitin: no scale grain");
                Assert.GreaterOrEqual(chitin.smoothness, skin.smoothness, "chitin: glossier");
                f.uv1.x = 11;
                CreatureShadeReference.Surface claw = CreatureShadeReference.Layered(p, t, f);
                Assert.AreEqual(0f, claw.subsurface); Assert.AreEqual(1f, claw.dorsal);
            }
        }

        // ------------------------------------------------------------ C# <-> HLSL

        static string Read(string assetPath) => File.ReadAllText(CreatureKitAssets.FullPath(assetPath)).Replace("\r\n", "\n");

        static int Define(string src, string name)
        {
            Match m = Regex.Match(src, "#define\\s+" + name + "\\s+(\\d+)u?\\b");
            Assert.IsTrue(m.Success, name + " not defined");
            return int.Parse(m.Groups[1].Value);
        }

        [Test]
        public void Shader_LayerConstants_MatchCSharp()
        {
            string h = Read("Assets/TerrainGen/Shaders/Creature/CreatureLayers.hlsl");
            Assert.AreEqual(PartLayers.MaxParts, Define(h, "CREATURE_LAYER_PARTS"));
            Assert.AreEqual(PartLayers.Length, Define(h, "CREATURE_LAYER_LENGTH"));
            StringAssert.Contains("float4 " + PartLayers.ShaderProperty + "[CREATURE_LAYER_LENGTH];", h);
            foreach (ShadingModel m in Enum.GetValues(typeof(ShadingModel)))
                Assert.AreEqual((int)m, Define(h, "CREATURE_MODEL_" + m.ToString().ToUpperInvariant()), m.ToString());
            var flagNames = new Dictionary<LayerFlags, string>
            {
                { LayerFlags.LimbPattern, "LIMB_PATTERN" }, { LayerFlags.NoPattern, "NO_PATTERN" }, { LayerFlags.FineGrain, "FINE_GRAIN" },
                { LayerFlags.NoGrain, "NO_GRAIN" }, { LayerFlags.Sheen, "SHEEN" }, { LayerFlags.Shimmer, "SHIMMER" },
            };
            foreach (LayerFlags f in Enum.GetValues(typeof(LayerFlags)))
                if (f != LayerFlags.None) Assert.AreEqual((int)f, Define(h, "CREATURE_LAYER_" + flagNames[f]), f.ToString());
            foreach (TintSource s in Enum.GetValues(typeof(TintSource)))
                Assert.AreEqual((int)s, Define(h, "CREATURE_TINT_" + s.ToString().ToUpperInvariant()), s.ToString());
        }

        // The fragment shader switches on shading models: no branch on a part id is left in it (a new part needs no edit).
        [Test]
        public void Shader_Frag_SwitchesOnModels_NotPartIds()
        {
            string src = Read("Assets/TerrainGen/Shaders/Creature.shader");
            int a = src.IndexOf("half4 Frag(", StringComparison.Ordinal), b = src.IndexOf("ENDHLSL", a, StringComparison.Ordinal);
            Assert.Greater(a, 0);
            string frag = src.Substring(a, b - a);
            Assert.IsFalse(Regex.IsMatch(frag, "part\\s*==\\s*CREATURE_PART_"), "Frag still compares part ids");
            StringAssert.Contains("CreatureLayerOf(part", frag);
            StringAssert.Contains("#include \"Creature/CreatureLayers.hlsl\"", src);
            // the frozen reference is the old fragment (part ids) - the GPU guard compares against it
            string legacy = Read("Assets/TerrainGen/Tests/EditMode/Shaders/CreatureLegacyReference.shader");
            StringAssert.Contains("part == CREATURE_PART_FOOT", legacy);
            StringAssert.Contains("Hidden/TerrainGen/CreatureLegacyReference", legacy);
        }

        // ------------------------------------------------------------ the Layers module

        [Test]
        public void LayersModule_RestylesItsRows_ValidateCatches()
        {
            var ok = new Layers { rows = new[] { new LayerDef { partId = 10, model = ShadingModel.Claw, tintAmount = 0.8f, tint = new Color(0.2f, 0.15f, 0.1f, 1f) } } };
            var ctx = new SpeciesCtx(null, null, 10, false);
            Assert.IsNull(ok.Validate(ctx));
            PartLayer r = ok.rows[0].ToRow();
            Assert.AreEqual(ShadingModel.Claw, r.model);
            Assert.AreEqual(LayerFlags.Sheen | LayerFlags.Shimmer, r.flags, "sheen + shimmer on unless switched off");
            Assert.IsNotNull(new Layers { rows = new[] { new LayerDef { partId = 3 }, new LayerDef { partId = 3 } } }.Validate(ctx), "twice");
            Assert.IsNotNull(new Layers { rows = new[] { new LayerDef { partId = 16 } } }.Validate(ctx), "range");
            Assert.IsNotNull(new Layers { rows = new[] { new LayerDef { partId = 2, tintAmount = 1.5f } } }.Validate(ctx), "tint range");
        }
    }
}
