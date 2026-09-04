using System;
using System.Collections.Generic;
using LSystems;
using UnityEngine;

// What a grammar MEANS, as data.
//
// The grammar says F, +, [, ], L, K. This says what those become: how thick a
// stem is, whether L is a leaf quad or a sphere, what colour a flower is.
//
// The two are deliberately separate assets. A grammar is a shape; a profile is
// an appearance; swapping either leaves the other valid. And because the turtle
// treats EVERY symbol it does not recognise as a marker, adding a new kind of
// part costs one character in the grammar and one row in `parts` -- no code.
//
// Lives in Assembly-CSharp rather than Scripts/LSystem for the reason the
// README gives: the grammar engine must not grow consumers. It only needs
// UnityEngine, so it could physically live there; keeping it out is what stops
// that folder accumulating them.
public enum PlantPartShape
{
    None,
    Capsule,
    Sphere,
    Cube,
    Cylinder,
    Quad,
    Prefab,
}

// How a marker's own parameter feeds the part's size. A grammar that writes
// L(0.4) is saying something about that leaf; this decides whether anyone
// listens.
public enum PlantPartSizeSource
{
    Fixed,          // scale only
    MarkerParam,    // scale * the marker's parameter at paramIndex
    TurtleWidth,    // scale * the turtle's width where the marker was dropped
}

[Serializable]
public class PlantPart
{
    [Tooltip("The grammar symbol this row describes. One character. Any symbol the turtle does not recognise arrives here as a marker.")]
    public string symbol = "L";

    public PlantPartShape shape = PlantPartShape.Sphere;

    [Tooltip("Used when shape is Prefab.")]
    public GameObject prefab;

    public Color color = new Color(0.35f, 0.65f, 0.25f);

    [Tooltip("Base size in metres before any parameter scaling.")]
    public Vector3 scale = Vector3.one * 0.25f;

    public PlantPartSizeSource sizeFrom = PlantPartSizeSource.MarkerParam;
    [Tooltip("Which of the marker's parameters to read, when sizeFrom is MarkerParam. L(0.4) has one, at index 0.")]
    public int paramIndex = 0;
    [Tooltip("Multiplies whatever sizeFrom produces.")]
    public float sizeMultiplier = 1f;

    [Tooltip("Face the way the turtle was facing. Off leaves the part world-axis aligned, which reads better for hanging fruit.")]
    public bool alignToHeading = true;
    public Vector3 eulerOffset;
    [Tooltip("Offset along the turtle's own axes, so it follows the branch.")]
    public Vector3 localOffset;

    public bool enabled = true;
}

[CreateAssetMenu(fileName = "PlantProfile", menuName = "Marching Cubes/Plant Profile")]
public class PlantProfile : ScriptableObject
{
    [Header("Shape")]
    [Tooltip("The grammar that grows this plant. Edit its source and the plant rebuilds as you type.")]
    public LSystemGrammarAsset grammar;

    [Tooltip("-1 uses the grammar's own #iterations directive.")]
    [Range(-1, 12)] public int iterations = -1;

    [Tooltip("Same grammar, different plant. Every instance is a pure function of this.")]
    public uint seed = 12345;

    [Tooltip("Defaults for turtle symbols that carry no parameter of their own.")]
    public TurtleSettings turtle = TurtleSettings.Default;

    [Header("Stems (whatever F draws)")]
    public PlantPartShape stemShape = PlantPartShape.Capsule;
    public Color stemColor = new Color(0.42f, 0.31f, 0.18f);
    [Tooltip("Skeleton width is a diameter, so radius = 0.5 * width * this.")]
    public float stemRadiusScale = 0.5f;
    public float minStemRadius = 0.005f;
    [Tooltip("Taper each stem from its start width to its end width. Off draws a uniform tube, which is cheaper to read while tuning angles.")]
    public bool taperStems = true;
    [Tooltip("Skip stems thinner than this. Useful when a grammar goes many generations deep.")]
    public float hideStemsBelowRadius = 0f;

    [Header("What the other symbols represent")]
    [Tooltip("One row per grammar symbol. Symbols with no row here are listed in the builder's inspector so you can see what a grammar is offering.")]
    public List<PlantPart> parts = new List<PlantPart>
    {
        new PlantPart { symbol = "L", shape = PlantPartShape.Quad, color = new Color(0.36f, 0.66f, 0.24f), scale = Vector3.one * 0.6f },
    };

    public bool TryGetPart(char symbol, out PlantPart part)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            PlantPart p = parts[i];
            if (p != null && p.enabled && !string.IsNullOrEmpty(p.symbol) && p.symbol[0] == symbol)
            {
                part = p;
                return true;
            }
        }
        part = null;
        return false;
    }

    // Bumped on any inspector edit so PlantBuilder knows to rebuild without
    // having to diff every field.
    public int Version { get; private set; }

    void OnValidate()
    {
        Version++;
        if (stemRadiusScale < 0f) stemRadiusScale = 0f;
        if (minStemRadius < 0f) minStemRadius = 0f;
        for (int i = 0; i < parts.Count; i++)
        {
            PlantPart p = parts[i];
            if (p == null) continue;
            // One character: module symbols are single chars, which is what
            // lets textbook grammars paste in verbatim (see the L-system README).
            if (!string.IsNullOrEmpty(p.symbol) && p.symbol.Length > 1) p.symbol = p.symbol.Substring(0, 1);
            if (p.sizeMultiplier < 0f) p.sizeMultiplier = 0f;
        }
    }
}
