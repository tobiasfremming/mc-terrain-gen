using System;
using System.Collections.Generic;
using UnityEngine;

namespace LSystems
{
    // Defaults the turtle falls back on when a module carries no parameter,
    // which is what makes non-parametric textbook grammars ("F[+F]F") work:
    // every F is stepLength long, every + is angleDegrees.
    [Serializable]
    public struct TurtleSettings
    {
        [Tooltip("Length of an F with no parameter.")]
        public float stepLength;
        [Tooltip("Angle in degrees for a rotation symbol with no parameter.")]
        public float angleDegrees;
        [Tooltip("Width at the base, before any ! has run.")]
        public float initialWidth;
        [Tooltip("What a bare '!' multiplies the current width by.")]
        public float widthFactor;
        [Tooltip("What a bare '\"' multiplies the current step length by.")]
        public float lengthFactor;

        public Vector3 origin;
        [Tooltip("Direction the turtle faces at the start. Plants usually want world up.")]
        public Vector3 heading;
        [Tooltip("The turtle's initial up vector. Must not be parallel to heading.")]
        public Vector3 up;

        public static TurtleSettings Default => new TurtleSettings
        {
            stepLength = 1f,
            angleDegrees = 25f,
            initialWidth = 0.1f,
            widthFactor = 0.7f,
            lengthFactor = 0.9f,
            origin = Vector3.zero,
            heading = Vector3.up,
            up = Vector3.back,
        };
    }

    // Word -> LSkeleton, using the turtle commands of "The Algorithmic Beauty
    // of Plants". The interpreter half of the library: it decides what symbols
    // *mean*, while the rewriter only decides what they *become*. Swap this for
    // another interpreter and the same grammar drives something else entirely.
    //
    // Recognised symbols (n = first parameter, or the settings default):
    //
    //   F(n) G(n)   move forward n, laying down a segment
    //   f(n) g(n)   move forward n without a segment (starts a detached run)
    //   +(a) -(a)   turn left / right   (about the turtle's up)
    //   &(a) ^(a)   pitch down / up     (about the turtle's left)
    //   \(a) /(a)   roll left / right   (about the turtle's heading)
    //   |           turn 180 degrees
    //   $           roll so the turtle's up is as close to world up as it can
    //               be without changing heading -- the standard trick for
    //               keeping leaves and branch planes from spiralling
    //   [ ]         push / pop turtle state
    //   !(w)        set width to w; bare ! multiplies width by widthFactor
    //   "(s)        set step length to s; bare " multiplies it by lengthFactor
    //
    // ANY OTHER SYMBOL becomes a marker at the current position and
    // orientation, carrying its parameters. That is deliberate: leaves,
    // flowers, fruit, tunnel widenings and prop anchors need no support here.
    //
    // Handedness note, since this is the one place it can silently go wrong:
    // Unity is left-handed, so "turn left" is a NEGATIVE rotation about the
    // turtle's local up. Each case below is written against the English name,
    // not against a book's matrix convention.
    public static class TurtleInterpreter
    {
        struct State
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float Width;
            public float Step;
            public int Node;
            public int Depth;
        }

        [ThreadStatic] static Stack<State> _stack;

        public static LSkeleton Build(LModuleString word, TurtleSettings settings, LSkeleton reuse = null)
        {
            LSkeleton skel = reuse ?? new LSkeleton();
            skel.Clear();
            if (word == null || word.Count == 0) return skel;

            var stack = _stack ??= new Stack<State>(32);
            stack.Clear();

            Vector3 heading = settings.heading.sqrMagnitude > 1e-8f ? settings.heading.normalized : Vector3.up;
            Vector3 up = settings.up.sqrMagnitude > 1e-8f ? settings.up.normalized : Vector3.back;
            // LookRotation silently fails on parallel inputs; pick any
            // perpendicular rather than handing the caller a broken frame.
            if (Mathf.Abs(Vector3.Dot(heading, up)) > 0.999f)
                up = Mathf.Abs(heading.y) > 0.9f ? Vector3.back : Vector3.up;
            up = Vector3.ProjectOnPlane(up, heading).normalized;

            var state = new State
            {
                Position = settings.origin,
                Rotation = Quaternion.LookRotation(heading, up),
                Width = settings.initialWidth,
                Step = settings.stepLength,
                Node = 0,
                Depth = 0,
            };

            skel.Nodes.Add(new LSkeletonNode
            {
                Position = state.Position,
                Orientation = state.Rotation,
                Width = state.Width,
                Parent = -1,
                Depth = 0,
            });

            var bounds = new Bounds(state.Position, Vector3.zero);
            float defaultAngle = settings.angleDegrees;

            for (int i = 0; i < word.Count; i++)
            {
                LModule m = word[i];
                char c = m.Symbol;
                bool hasParam = m.ParamCount > 0;
                float p0 = hasParam ? word.GetParam(i, 0) : 0f;

                switch (c)
                {
                    case 'F':
                    case 'G':
                        Move(skel, ref state, ref bounds, hasParam ? p0 : state.Step, true);
                        break;

                    case 'f':
                    case 'g':
                        Move(skel, ref state, ref bounds, hasParam ? p0 : state.Step, false);
                        break;

                    case '+': Turn(ref state, Vector3.up, -(hasParam ? p0 : defaultAngle)); break;
                    case '-': Turn(ref state, Vector3.up, +(hasParam ? p0 : defaultAngle)); break;
                    case '&': Turn(ref state, Vector3.right, +(hasParam ? p0 : defaultAngle)); break;
                    case '^': Turn(ref state, Vector3.right, -(hasParam ? p0 : defaultAngle)); break;
                    case '\\': Turn(ref state, Vector3.forward, +(hasParam ? p0 : defaultAngle)); break;
                    case '/': Turn(ref state, Vector3.forward, -(hasParam ? p0 : defaultAngle)); break;
                    case '|': Turn(ref state, Vector3.up, 180f); break;

                    case '$': RollHorizontal(ref state); break;

                    case '[':
                        stack.Push(state);
                        state.Depth++;
                        if (state.Depth > skel.MaxDepth) skel.MaxDepth = state.Depth;
                        break;

                    case ']':
                        // An unbalanced ']' is a grammar bug the parser already
                        // reported; drawing something is better than throwing.
                        if (stack.Count > 0) state = stack.Pop();
                        break;

                    case '!':
                        state.Width = hasParam ? p0 : state.Width * settings.widthFactor;
                        // A '!' before anything has been drawn is the axiom
                        // setting the base width, so it has to reach back into
                        // the root node -- otherwise the trunk's first segment
                        // tapers from settings.initialWidth, which no grammar
                        // asked for. After the first segment exists, '!' only
                        // affects what comes next, which is what makes
                        // "F(l) !(w) F(l)" read as a taper.
                        if (skel.Segments.Count == 0)
                        {
                            LSkeletonNode root = skel.Nodes[state.Node];
                            root.Width = state.Width;
                            skel.Nodes[state.Node] = root;
                        }
                        break;

                    case '"':
                        state.Step = hasParam ? p0 : state.Step * settings.lengthFactor;
                        break;

                    default:
                    {
                        var marker = new LSkeletonMarker
                        {
                            Symbol = c,
                            Position = state.Position,
                            Orientation = state.Rotation,
                            Width = state.Width,
                            Depth = state.Depth,
                            Node = state.Node,
                            ParamStart = skel.MarkerParams.Count,
                            ParamCount = m.ParamCount,
                        };
                        for (int q = 0; q < m.ParamCount; q++)
                            skel.MarkerParams.Add(word.GetParam(i, q));
                        skel.Markers.Add(marker);
                        break;
                    }
                }
            }

            skel.Bounds = bounds;
            return skel;
        }

        static void Move(LSkeleton skel, ref State state, ref Bounds bounds, float length, bool draw)
        {
            Vector3 from = state.Position;
            Vector3 to = from + state.Rotation * Vector3.forward * length;

            int parent = draw ? state.Node : -1;
            skel.Nodes.Add(new LSkeletonNode
            {
                Position = to,
                Orientation = state.Rotation,
                Width = state.Width,
                Parent = parent,
                Depth = state.Depth,
            });
            int newNode = skel.Nodes.Count - 1;

            if (draw)
            {
                skel.Segments.Add(new LSkeletonSegment
                {
                    From = state.Node,
                    To = newNode,
                    // Node widths, not the current width twice: a '!' between
                    // two F's then reads as a taper along the branch rather
                    // than a step change no mesher can see.
                    WidthFrom = skel.Nodes[state.Node].Width,
                    WidthTo = state.Width,
                    Depth = state.Depth,
                    Length = Mathf.Abs(length),
                });
                skel.TotalLength += Mathf.Abs(length);
            }

            state.Position = to;
            state.Node = newNode;
            bounds.Encapsulate(to);
        }

        static void Turn(ref State state, Vector3 localAxis, float degrees)
        {
            if (degrees == 0f) return;
            state.Rotation *= Quaternion.AngleAxis(degrees, localAxis);
        }

        // "$" in the book: keep the heading, spin about it until the turtle's
        // up is as close to world up as possible. Undefined when the turtle
        // points straight up, in which case any roll is as good as any other
        // and doing nothing is the least surprising.
        static void RollHorizontal(ref State state)
        {
            Vector3 h = state.Rotation * Vector3.forward;
            Vector3 u = Vector3.ProjectOnPlane(Vector3.up, h);
            if (u.sqrMagnitude < 1e-8f) return;
            state.Rotation = Quaternion.LookRotation(h, u.normalized);
        }
    }
}
