using System.Collections.Generic;
using UnityEngine;

namespace LSystems
{
    // A branching structure in space: nodes, the segments between them, and
    // markers dropped at points of interest.
    //
    // This is the pivot of the whole library. A word is a string of symbols
    // that means nothing geometric on its own; a mesh is a finished thing that
    // only one consumer wants. A skeleton is the useful middle: a plant mesher
    // sweeps generalized cylinders along the segments, a cave carver unions
    // capsule SDFs along the same segments into a DensityField, a river writes
    // them into a flow map. None of those need to know a production from a
    // turtle command, and none of them need their own interpreter.
    //
    // Everything here is world/local space as produced by the turtle: the
    // caller decides where the origin sits.
    public struct LSkeletonNode
    {
        public Vector3 Position;
        public Quaternion Orientation;   // turtle frame here: forward = heading
        public float Width;
        public int Parent;               // -1 for a root (start of a detached run)
        public int Depth;                // bracket nesting depth when created
    }

    public struct LSkeletonSegment
    {
        public int From, To;             // node indices
        public float WidthFrom, WidthTo;
        public int Depth;
        public float Length;
    }

    // A non-turtle symbol, recorded where the turtle stood when it hit it.
    //
    // This is the extension point: any symbol the turtle does not itself
    // interpret becomes a marker carrying its parameters, so a grammar can say
    // L(0.4) for a leaf or K for a crystal seed and the consumer decides what
    // that means. Adding a decoration type costs a line in a grammar and a
    // switch case in the consumer -- no change here, none in the turtle.
    public struct LSkeletonMarker
    {
        public char Symbol;
        public Vector3 Position;
        public Quaternion Orientation;
        public float Width;
        public int Depth;
        public int Node;                 // node the turtle was standing on
        public int ParamStart, ParamCount;
    }

    public sealed class LSkeleton
    {
        public readonly List<LSkeletonNode> Nodes = new List<LSkeletonNode>();
        public readonly List<LSkeletonSegment> Segments = new List<LSkeletonSegment>();
        public readonly List<LSkeletonMarker> Markers = new List<LSkeletonMarker>();

        // Flat parameter store for markers, same trick as LModuleString.
        public readonly List<float> MarkerParams = new List<float>();

        public Bounds Bounds;
        public float TotalLength;
        public int MaxDepth;

        public void Clear()
        {
            Nodes.Clear();
            Segments.Clear();
            Markers.Clear();
            MarkerParams.Clear();
            Bounds = new Bounds();
            TotalLength = 0f;
            MaxDepth = 0;
        }

        public float GetMarkerParam(in LSkeletonMarker marker, int index, float fallback = 0f)
        {
            if (index < 0 || index >= marker.ParamCount) return fallback;
            return MarkerParams[marker.ParamStart + index];
        }

        public float GetMarkerParam(int markerIndex, int index, float fallback = 0f)
            => GetMarkerParam(Markers[markerIndex], index, fallback);
    }
}
