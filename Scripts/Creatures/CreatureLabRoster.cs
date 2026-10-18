using System;
using System.Collections.Generic;
using UnityEngine;

// The creature lab's animals (creature kit T3.5): one entry per enclosure, in arc order, each with an optional
// size override. The habitat comes from the species' own modules (CreatureKit.LabHabitat: Fly -> aviary, Climb
// -> enclosure with a climbable tree, a soft body / Ambush -> the octopus grove, else a plain enclosure with a
// shade tree), so adding an animal to the lab is one entry here - the last step of the new-animal workflow
// (creature-kit skill). CreatureLab places the enclosures and aviaries; OctopusLab the grove. Rebuild the scene
// with Marching Cubes / Rebuild Creature Lab.
[CreateAssetMenu(menuName = "Marching Cubes/Creature Lab Roster")]
public sealed class CreatureLabRoster : ScriptableObject
{
    public const string AssetPath = "Assets/TerrainGen/Config/Creatures/CreatureLabRoster.asset";

    [Serializable]
    public struct Entry
    {
        public CreatureSpecies species;
        [Tooltip("Size override for this enclosure's individual (0 = the species' own scale).")]
        public float scaleOverride;
    }

    public List<Entry> entries = new List<Entry>();

    // The entries a habitat is placed for (in roster order), and the first of another.
    public void Split(out List<Entry> enclosures, out CreatureSpecies grove)
    {
        enclosures = new List<Entry>();
        grove = null;
        foreach (Entry e in entries)
        {
            if (e.species == null) continue;
            if (CreatureKit.LabHabitat.For(e.species) == CreatureKit.Habitat.Grove) { if (grove == null) grove = e.species; }
            else enclosures.Add(e);
        }
    }
}
