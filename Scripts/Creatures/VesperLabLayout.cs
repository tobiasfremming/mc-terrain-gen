using UnityEngine;

// VesperLab's layout, pure geometry (also run offline): the aviary, the perch rocks and where Vesper and the player
// start. VesperLab (MonoBehaviour) builds the scene from it; CreatureVesperLabTests and CreatureVesperTests check it.
//   Aviary: AviaryFactor x the Creature Lab's aviary radius for the same individual (CreatureLab.EnclosureRadius at
//           the largest size: 40.7 m for Vesper at scale 4, safe radius 31.5 m) and the lab's roof (AviarySizing).
//           In the lab's own radius (27.1 m) Vesper under CommandBrain is on the wall 5-10 % of its air time; here
//           0.13-0.52 % (Task E's measurements: CreatureVesperTests.Vesper_Aviary_WallContact).
//   Rocks:  the CommandBrain tests' perch rock (CreatureCommandBrainTests.Rock: 1.2 m square, 0.5 m high at scale 1,
//           x the scale) - other sizes still show visible pops on the touchdown. Rock 0 sits exactly where the tests put
//           it (2.5, 0, 1.25) x the scale from the centre; the others are that spot turned by 360/count deg about up.
//           They are the area's obstacles (walked round; the CommandBrain's perch scan probes their centres first).
//   Vesper: starts at the centre facing +Z (as the CommandBrain tests' sim).
public static class VesperLabLayout
{
    // the aviary radius over the Creature Lab's (Task E: wall contact 0.5 % here, 5-10 % in the lab's)
    public const float AviaryFactor = 1.5f;

    // the perch rock, m at scale 1 (CreatureCommandBrainTests.Rock): footprint centre offset from the aviary centre, size
    public static readonly Vector3 RockOffset = new Vector3(2.5f, 0f, 1.25f);
    public static readonly Vector3 RockSize = new Vector3(1.2f, 0.5f, 1.2f);

    // the size scale of the largest individual at `scale` (CreatureWorld draws it from the seed)
    public static float MaxScale(CreatureSpecies sp, float scale) => CreatureLab.MaxScale(sp, scale);

    // The aviary radius for species sp (its runtime view) at scale.
    public static float Radius(CreatureSpecies sp, float scale) => AviaryFactor * CreatureLab.EnclosureRadius(sp, MaxScale(sp, scale));

    // Rock i of count: the centre of its footprint (on the ground) and its size, m.
    public static Vector3 RockAt(Vector3 center, Vector3 up, int i, int count, float scale)
        => center + Quaternion.AngleAxis(360f * i / Mathf.Max(1, count), up) * (RockOffset * scale);
    public static Vector3 RockSizeAt(float scale) => RockSize * scale;

    // The rocks' keep-out (the clearance the walking head's path keeps from them), body lengths. Not the lab's
    // EnclosureArea.KeepOutFor (11.5 m for Vesper: its folded wings' lateral reach + 0.25 L), whose discs would cover the
    // centre and most of the disc between three rocks. Measured offline, WalkAround 480 s, seeds 1-3: 0.25 L walks
    // 309-316 m with a spine joint over a rock 0.4-4 % of the time; 0.12 L 174-311 m, over a rock 0.3-23 %; no
    // obstacles 17-66 m (it stalls against a rock).
    public const float KeepOutLengths = 0.25f;

    // The aviary as the CommandBrain and the sim see it: the disc, the roof, the rocks as obstacles.
    public static EnclosureArea Area(CreatureSpecies sp, float scale, Vector3 center, int rocks)
    {
        float big = MaxScale(sp, scale);
        EnclosureArea a = EnclosureArea.For(center, Vector3.up, Radius(sp, scale), sp, big);
        a.maxHeight = AviarySizing.Height(sp, big, CreatureLab.kAviaryHeight);
        if (rocks > 0)
        {
            a.obstacleCenters = new Vector3[rocks]; a.obstacleRadii = new float[rocks];
            Vector3 size = RockSizeAt(scale);
            for (int i = 0; i < rocks; i++)
            {
                a.obstacleCenters[i] = RockAt(center, Vector3.up, i, rocks, scale);
                a.obstacleRadii[i] = 0.5f * Mathf.Max(size.x, size.z);
            }
            a.keepOut = KeepOutLengths * sp.BodyLength * big;
        }
        return a;
    }

    // Where Vesper starts: the centre, facing +Z.
    public static void Start(in EnclosureArea area, out Vector3 at, out Vector3 forward)
    {
        at = area.center; forward = Vector3.forward;
    }

    // Where the player starts: inside the aviary on the far side from the rocks' first one, kPlayerShare of the radius out,
    // facing the centre (eye height is the controller's).
    public const float kPlayerShare = 0.7f;
    public static Vector3 PlayerSpot(in EnclosureArea area) => area.center - Vector3.forward * (kPlayerShare * area.radius);
}
