using System;
using UnityEngine;

// Which species grow where, and how thickly.
//
// Deliberately separate from PlantPrototypeSet: a set is "what this species
// looks like", this is "where it belongs". The same tree can be dense in one
// world and absent from another without rebaking a single mesh.
[Serializable]
public class PlantSpecies
{
    public string label = "species";
    public PlantPrototypeSet prototypes;

    [Header("How many")]
    [Tooltip("Instances per plot before any filter rejects them. The plot is plotSize metres square, so this is density, not a world total.")]
    [Range(0, 64)] public int perPlot = 6;
    [Tooltip("Chance a given plot grows this species at all. Below 1 it clumps into stands instead of spreading evenly.")]
    [Range(0f, 1f)] public float plotChance = 0.7f;

    [Header("Where")]
    [Tooltip("Ground must be at least this upright. 1 = flat only, 0 = anything. Cos of the max slope angle.")]
    [Range(0f, 1f)] public float minUpness = 0.75f;
    public float minHeight = -1000f;
    public float maxHeight = 1000f;

    [Header("Look")]
    public Vector2 scaleRange = new Vector2(0.85f, 1.2f);
    [Tooltip("Tilt away from the surface normal, degrees. A little stops a stand looking like a pin cushion.")]
    [Range(0f, 30f)] public float leanDegrees = 6f;
    [Tooltip("Sink the base into the ground so it never floats on a slope.")]
    public float sink = 0.15f;

    [Header("Draw distance")]
    [Tooltip("Off means this species always draws LOD0, whatever the distance. Costs triangles, but nothing ever swaps mesh in front of you -- keepFraction removes whole branches, so a swap is a silhouette change, not a subtle one. Culling and the fade band still apply.")]
    public bool useLod = true;
    public float lod1Distance = 40f;
    public float lod2Distance = 90f;
    [Tooltip("Beyond this the species is not drawn at all. Plants pop at this distance -- there is deliberately no fade, because the only fade available without shader support is scaling, and a tree that is half its height at 90 m and grows as you walk up is exactly the 'looks different depending on where I stand' bug. Set it far enough that a plant of this species is a few pixels when it pops: tall species far, ground cover near. PlantWorld.radius follows the largest of these automatically.")]
    public float cullDistance = 160f;

    public bool enabled = true;
}

[CreateAssetMenu(fileName = "PlantWorld", menuName = "Marching Cubes/Plant World")]
public class PlantWorld : ScriptableObject
{
    [Tooltip("Independent of the terrain seed, so vegetation can be reshuffled without moving the ground.")]
    public int seed = 5150;

    [Tooltip("Side of one scatter plot, metres. Placement is a pure function of (seed, plotX, plotZ) -- the same trick LSystemGroveField uses to tile deterministically.")]
    public float plotSize = 24f;

    [Tooltip("How far from the player plots are populated. Kept at least two plots LARGER than every species' cullDistance (OnValidate raises it): a plot has to exist before its plants can be drawn, so if the two were equal a plant would be built at the exact moment it became visible and pop in late while the budget catches up.")]
    public float radius = 160f;


    [Tooltip("Plots kept built. Exceeding it clears the cache; plots are deterministic so nothing is lost but the work.")]
    public int maxCachedPlots = 4096;

    public PlantSpecies[] species = new PlantSpecies[0];

    // A plant cannot be drawn before its plot exists, so the populated radius
    // must exceed every draw distance. The draw distance is the number an
    // author actually chooses, so the radius follows it (with a two-plot
    // margin so plots are built out of sight) rather than clamping it.
    void OnValidate()
    {
        plotSize = Mathf.Max(2f, plotSize);
        float maxCull = 0f;
        if (species != null)
            foreach (var s in species)
            {
                if (s == null) continue;
                s.cullDistance = Mathf.Max(0f, s.cullDistance);
                s.lod2Distance = Mathf.Min(s.lod2Distance, s.cullDistance);
                s.lod1Distance = Mathf.Min(s.lod1Distance, s.lod2Distance);
                if (s.enabled) maxCull = Mathf.Max(maxCull, s.cullDistance);
            }
        radius = Mathf.Max(radius, maxCull + plotSize * 2f, plotSize * 2f);
    }
}
