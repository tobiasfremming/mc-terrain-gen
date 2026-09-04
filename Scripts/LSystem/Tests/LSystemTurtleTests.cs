using NUnit.Framework;
using UnityEngine;

namespace LSystems.Tests
{
    public class LSystemTurtleTests
    {
        // Facing +Z with +Y up and identity rotation, so every expected
        // position below is readable without doing quaternion algebra.
        static TurtleSettings Settings(float angle = 90f) => new TurtleSettings
        {
            stepLength = 1f,
            angleDegrees = angle,
            initialWidth = 1f,
            widthFactor = 0.5f,
            lengthFactor = 0.5f,
            origin = Vector3.zero,
            heading = Vector3.forward,
            up = Vector3.up,
        };

        static LSkeleton Build(string word, float angle = 90f)
        {
            Assert.IsTrue(LSystemParser.TryParseModuleString(word, out LModuleString parsed, out var errors),
                "test word did not parse: " + (errors.Count > 0 ? errors[0].ToString() : ""));
            return TurtleInterpreter.Build(parsed, Settings(angle));
        }

        static void AssertAt(Vector3 expected, Vector3 actual, string what)
        {
            Assert.Less(Vector3.Distance(expected, actual), 1e-4f, what + ": expected " + expected + " got " + actual);
        }

        [Test]
        public void ForwardLaysDownOneSegment()
        {
            LSkeleton s = Build("F");
            Assert.AreEqual(2, s.Nodes.Count);
            Assert.AreEqual(1, s.Segments.Count);
            AssertAt(Vector3.zero, s.Nodes[0].Position, "root");
            AssertAt(new Vector3(0, 0, 1), s.Nodes[1].Position, "tip");
            Assert.AreEqual(0, s.Segments[0].From);
            Assert.AreEqual(1, s.Segments[0].To);
            Assert.AreEqual(1f, s.Segments[0].Length, 1e-5f);
            Assert.AreEqual(1f, s.TotalLength, 1e-5f);
        }

        [Test]
        public void ParameterOverridesTheDefaultStep()
        {
            AssertAt(new Vector3(0, 0, 3), Build("F(3)").Nodes[1].Position, "F(3)");
        }

        [Test]
        public void RotationSymbolsMatchTheirNames()
        {
            // Pins the handedness convention. Unity is left-handed, so these
            // are the assertions that catch a sign flip in TurtleInterpreter.
            AssertAt(new Vector3(-1, 0, 0), Build("+F").Nodes[1].Position, "+ turns left");
            AssertAt(new Vector3(1, 0, 0), Build("-F").Nodes[1].Position, "- turns right");
            AssertAt(new Vector3(0, -1, 0), Build("&F").Nodes[1].Position, "& pitches down");
            AssertAt(new Vector3(0, 1, 0), Build("^F").Nodes[1].Position, "^ pitches up");
            AssertAt(new Vector3(0, 0, -1), Build("|F").Nodes[1].Position, "| turns around");
        }

        [Test]
        public void RollTurnsTheFrameWithoutChangingHeading()
        {
            // Roll is invisible in the position of the next F, so check the
            // frame a marker captured instead.
            LSkeleton s = Build("\\(90) K");
            Assert.AreEqual(1, s.Markers.Count);
            AssertAt(new Vector3(-1, 0, 0), s.Markers[0].Orientation * Vector3.up, "roll left tips 'up' to the left");
            AssertAt(new Vector3(0, 0, 1), s.Markers[0].Orientation * Vector3.forward, "heading unchanged");
        }

        [Test]
        public void DollarRollsTheFrameBackUpright()
        {
            LSkeleton s = Build("\\(90) $ K");
            AssertAt(Vector3.up, s.Markers[0].Orientation * Vector3.up, "$ undoes the roll");
            AssertAt(new Vector3(0, 0, 1), s.Markers[0].Orientation * Vector3.forward, "$ keeps the heading");
        }

        [Test]
        public void OppositeTurnsCancel()
        {
            AssertAt(new Vector3(0, 0, 1), Build("+-F").Nodes[1].Position, "+ then - is a no-op");
        }

        [Test]
        public void BracketsSaveAndRestoreTheTurtle()
        {
            LSkeleton s = Build("[+F]F");
            Assert.AreEqual(2, s.Segments.Count);
            Assert.AreEqual(1, s.MaxDepth);
            AssertAt(new Vector3(-1, 0, 0), s.Nodes[1].Position, "branch tip");
            AssertAt(new Vector3(0, 0, 1), s.Nodes[2].Position, "trunk continues from the saved state");
            Assert.AreEqual(0, s.Segments[1].From, "the trunk segment starts back at the root");
        }

        [Test]
        public void NestedBracketsReportTheirDepth()
        {
            LSkeleton s = Build("F[+F[+F]]");
            Assert.AreEqual(2, s.MaxDepth);
        }

        [Test]
        public void LowercaseFMovesWithoutDrawing()
        {
            LSkeleton s = Build("fF");
            Assert.AreEqual(3, s.Nodes.Count);
            Assert.AreEqual(1, s.Segments.Count, "only the F draws");
            Assert.AreEqual(-1, s.Nodes[1].Parent, "a jump starts a detached run");
            AssertAt(new Vector3(0, 0, 2), s.Nodes[2].Position, "but it still moved the turtle");
        }

        [Test]
        public void UnrecognisedSymbolsBecomeMarkers()
        {
            // The extension point: a grammar can invent L for leaf or K for
            // crystal and the turtle records it without needing to be changed.
            LSkeleton s = Build("F K(0.5, 7)");
            Assert.AreEqual(1, s.Markers.Count);
            LSkeletonMarker mk = s.Markers[0];
            Assert.AreEqual('K', mk.Symbol);
            Assert.AreEqual(2, mk.ParamCount);
            Assert.AreEqual(0.5f, s.GetMarkerParam(mk, 0), 1e-5f);
            Assert.AreEqual(7f, s.GetMarkerParam(mk, 1), 1e-5f);
            AssertAt(new Vector3(0, 0, 1), mk.Position, "marker sits where the turtle stood");
            Assert.AreEqual(1, mk.Node);
            Assert.AreEqual(0f, s.GetMarkerParam(mk, 5, 0f), 1e-5f, "missing params read as the fallback");
        }

        [Test]
        public void WidthSetBeforeAnythingIsDrawnAppliesToTheRoot()
        {
            // "!(w) F..." in an axiom must not produce a trunk that tapers from
            // the settings' default width to the grammar's.
            LSkeleton s = Build("!(0.25) F");
            Assert.AreEqual(0.25f, s.Nodes[0].Width, 1e-5f);
            Assert.AreEqual(0.25f, s.Segments[0].WidthFrom, 1e-5f);
            Assert.AreEqual(0.25f, s.Segments[0].WidthTo, 1e-5f);
        }

        [Test]
        public void WidthChangedBetweenSegmentsTapers()
        {
            LSkeleton s = Build("F !(0.5) F");
            Assert.AreEqual(2, s.Segments.Count);
            Assert.AreEqual(1f, s.Segments[0].WidthFrom, 1e-5f);
            Assert.AreEqual(1f, s.Segments[0].WidthTo, 1e-5f);
            Assert.AreEqual(1f, s.Segments[1].WidthFrom, 1e-5f);
            Assert.AreEqual(0.5f, s.Segments[1].WidthTo, 1e-5f);
        }

        [Test]
        public void BareBangAndQuoteUseTheSettingsFactors()
        {
            LSkeleton s = Build("F ! \" F");
            Assert.AreEqual(0.5f, s.Segments[1].WidthTo, 1e-5f, "bare ! scales width by widthFactor");
            Assert.AreEqual(0.5f, s.Segments[1].Length, 1e-5f, "bare \" scales step by lengthFactor");
        }

        [Test]
        public void BoundsCoverEveryNode()
        {
            LSkeleton s = Build("F [ +F ] F");

            // The invariant, stated directly. Probing hardcoded corners instead
            // is a trap: a rotated node lands at -0.99999994, not -1, because
            // sin(45) in float does not normalize to exactly 1 -- so an exact
            // corner probe sits a hair OUTSIDE the bounds it is meant to be in.
            foreach (var n in s.Nodes)
                Assert.IsTrue(s.Bounds.Contains(n.Position), "bounds must contain node at " + n.Position);

            // ...and that the extremes land where the word says, to float tolerance.
            Assert.AreEqual(-1f, s.Bounds.min.x, 1e-4f, "branch reaches one step left");
            Assert.AreEqual(0f, s.Bounds.max.x, 1e-4f, "nothing goes right");
            Assert.AreEqual(0f, s.Bounds.min.z, 1e-4f, "root at the origin");
            Assert.AreEqual(2f, s.Bounds.max.z, 1e-4f, "trunk reaches two steps forward");
        }

        [Test]
        public void UnbalancedCloseBracketDoesNotThrow()
        {
            // The parser reports it; the turtle still has to survive being
            // handed a word built in code.
            var word = new LModuleString();
            word.Add(']');
            word.Add('F');
            LSkeleton s = TurtleInterpreter.Build(word, Settings());
            Assert.AreEqual(1, s.Segments.Count);
        }

        [Test]
        public void EmptyWordProducesAnEmptySkeleton()
        {
            LSkeleton s = TurtleInterpreter.Build(new LModuleString(), Settings());
            Assert.AreEqual(0, s.Nodes.Count);
            Assert.AreEqual(0, s.Segments.Count);
        }

        [Test]
        public void ReusedSkeletonIsFullyReset()
        {
            LSkeleton s = TurtleInterpreter.Build(Word("F [ +F ] K"), Settings());
            int before = s.Segments.Count;
            TurtleInterpreter.Build(Word("F"), Settings(), s);
            Assert.AreEqual(2, before);
            Assert.AreEqual(1, s.Segments.Count);
            Assert.AreEqual(0, s.Markers.Count);
            Assert.AreEqual(0, s.MarkerParams.Count);
            Assert.AreEqual(0, s.MaxDepth);
            Assert.AreEqual(1f, s.TotalLength, 1e-5f);
        }

        static LModuleString Word(string source)
        {
            LSystemParser.TryParseModuleString(source, out LModuleString w, out _);
            return w;
        }

        [Test]
        public void EndToEndFromTheShippedGrammar()
        {
            // The whole pipeline in one call: text -> word -> skeleton.
            var asset = ScriptableObject.CreateInstance<LSystemGrammarAsset>();
            try
            {
                asset.source = LSystemGrammarAsset.DefaultSource;
                asset.Invalidate();
                Assert.IsTrue(asset.IsValid, "shipped grammar must parse");

                LSkeleton s = asset.BuildSkeleton(TurtleSettings.Default, 7, 12345u);
                Assert.Greater(s.Segments.Count, 20, "should produce a branching structure");
                Assert.Greater(s.Markers.Count, 0, "should hang leaves on the twigs");
                Assert.Greater(s.MaxDepth, 2, "should actually branch");
                Assert.Greater(s.Bounds.size.magnitude, 1f, "should occupy space");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
