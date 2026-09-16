# Pending Editor verification

Every check in this file was deferred during the game-integration work (Tasks 1–11, 14; see
`INTEGRATION.md`) because the Unity Editor / Unity MCP was not available. All code passed the
offline compile gate (`compile.py`) for its task, and again after the final-review fix wave
(F1–F7: guard hold/rescue, NavMesh source filter, streamer in-flight cap, 2-chunk clipmap margin,
tile radius `max(physicsRadius, navRadius)`, waker/NavMeshData cleanup); none of this has run against
the real Editor yet.
Grouped by feature, in build order. Check items off as they're run.

## How to run

1. Open this project in Unity 6000.2.6f2 (URP) with Unity MCP running.
2. Over MCP, read `mcpforunity://instances` and confirm the active instance says
   `MarchingCubes@…` — **not** `SpaceGame` or anything else (port 8080 is shared risk; see
   `CLAUDE.md`). `set_active_instance` on it if more than one instance is connected.
3. `refresh_unity`, then `read_console` (errors only). Expect none — in particular no `CS0234`
   for `TerrainGen.Runtime`/`TerrainGen.Editor`/`TerrainGen.Tests`.
4. Run the EditMode Test Runner for `TerrainGen.Tests` (`unityMCP.run_tests`, mode `EditMode`) and
   for `TerrainGen.LSystem.Tests`. Test classes, with test counts as currently written:

   `TerrainGen.Tests` (`Assets/TerrainGen/Tests/EditMode/`):
   - `AssemblySmokeTests` (1)
   - `WorldConfigTests` (1)
   - `TerrainQueryTests` (9)
   - `RigidbodyGuardLogicTests` (4)
   - `CollisionTilePlannerTests` (4)
   - `NavPatchPlannerTests` (11)
   - `FloraObstacleTests` (4)

   `TerrainGen.LSystem.Tests` (`Assets/TerrainGen/Scripts/LSystem/Tests/`, pre-existing, not part
   of this plan but must not regress):
   - `LSystemParserTests` (22)
   - `LSystemRewriterTests` (21)
   - `LSystemTurtleTests` (18)

   Expected: all green, 95 total across both assemblies. Any failure here blocks everything below.

---

## Task 1 — Assemblies, test scaffold, flat config

- [ ] **Console after refresh.** Setup: none beyond "How to run" step 3. Action: `refresh_unity`
      then `read_console`. Expected: no errors; `TerrainGen.Runtime`, `TerrainGen.Editor`,
      `TerrainGen.Tests` and the existing `TerrainGen.LSystem` all compile.
- [ ] **Smoke test.** Setup: none. Action: run `AssemblySmokeTests.RuntimeTypesAreVisibleFromTests`.
      Expected: pass.
- [ ] **Flat config asset.** Setup: none. Action: open `Assets/TerrainGen/Config/Test/
      WorldConfig_Flat.asset` and `Assets/TerrainGen/Config/WorldConfig.asset` in the Inspector (or
      `find_in_file`). Expected: `WorldConfig_Flat.useGlobe == false`, `WorldConfig.useGlobe ==
      true` (unchanged).
- [ ] **Play-mode regression.** Setup: open `Assets/Scenes/SampleScene.unity`. Action: enter Play
      mode, wait ~10 s, read console, take a screenshot. Expected: same `[PlayerBootstrap] Player
      ready at …` log as before Task 1, no new errors, terrain renders and the player stands
      (this is the one check the compile gate can't see — e.g. a MonoBehaviour losing its script
      reference when Unity re-serializes it into a different assembly after the asmdef split).
- [ ] **Meta GUID sanity.** Setup: none. Action: let Unity import the 8 new GUIDs this task and
      Task 15 wrote by hand offline. Expected: no "GUID collision" warnings in the console.

## Task 2 — `demoBootstrap` / `terrainLayer`

- [ ] **WorldConfigTests.** Action: run `WorldConfigTests.TerrainLayerMaskMatchesLayer`. Expected:
      pass; no regression in `AssemblySmokeTests`/`TestFields`-based tests.
- [ ] **demoBootstrap = true (default).** Setup: SampleScene with its default `WorldConfig`
      (`demoBootstrap = true`). Action: enter Play mode. Expected: demo capsule spawns,
      `[PlayerBootstrap] Player ready at …` logs, `[DesertAtmosphere] Fog enabled …` logs if the
      scene uses the desert biome.
- [ ] **demoBootstrap = false.** Setup: temporarily point the scene's `MCChunkManager.worldConfig`
      at `WorldConfig_Flat` with `demoBootstrap` set to `false`. Action: enter Play mode. Expected:
      no `[PlayerBootstrap]` log, no capsule spawned, `RenderSettings.fog`/camera far-clip
      unchanged, no `[DesertAtmosphere]` log. Revert the scene's `worldConfig` reference afterward;
      do not save.
- [ ] **terrainLayer propagation.** Setup: a `WorldConfig` with `terrainLayer` set to a spare layer
      index. Action: enter Play mode, inspect spawned `MarchingChunk` GameObjects (`find_gameobjects`
      or Inspector). Expected: chunks report that layer index; `generateCollider` still true only
      for level-0 chunks.

## Task 3 — `MarchingChunk.ColliderChanged`

- [ ] **Event fires, in-box query.** Setup: SampleScene, enter Play mode, wait ~10 s for the
      clipmap to settle. Action (`execute_code`):
      ```csharp
      MarchingChunk.ColliderChanged += c => Debug.Log($"[ColliderChanged] {c.name} mesh={(c.ColliderMesh != null)}");
      var m = MCChunkManager.Instance;
      var c = m.Level0Coord(m.target.position);
      return $"inBox={m.IsInLevel0Box(c)} ready={m.IsLevel0ColliderReady(c)} farInBox={m.IsInLevel0Box(c + new Vector3Int(50,0,0))}";
      ```
      Expected: `inBox=True ready=True farInBox=False`.
- [ ] **Event fires on streaming.** Action: move the player 40 m (set `target.position`), wait 3 s,
      `read_console`. Expected: several `[ColliderChanged]` lines, some `mesh=True` (new ring
      bound), some `mesh=False` (released/pooled chunks, or a real enabled→disabled transition —
      not every rebuild of a recycled coarse chunk, per the Task 3 ruling).

## Task 4 — `TerrainQuery`

- [ ] **NUnit.** Action: run `TerrainQueryTests` (9: five `TryFindGround`, four `TryFindExitAbove`
      incl. `ExitAboveLeavesThroughTheNearestRoofNotTheGround` on `TestFields.TwoLayerField`).
      Expected: all green.
- [ ] **Live ground query.** Setup: a running scene (flat and, separately, globe). Action:
      `MCChunkManager.Instance` non-null; call `TerrainQuery.TryFindGround(pos, searchUp, searchDown,
      out p, out n)` via the convenience overload (resolves `PhysicsField`/`PlanetGravity.UpAt`
      automatically). Expected: `p` matches the visible terrain surface under the query position;
      `n` roughly matches the visual slope; works in both flat and globe scenes.

## Task 5/6 — `TerrainRigidbodyGuard` / `TerrainPhysicsWaker`

- [ ] **NUnit.** Action: run `RigidbodyGuardLogicTests` (4, incl. `NoColliderAndNoFieldGroundFrees`).
      Expected: all green.
- [ ] **Held over unstreamed ground.** Setup: SampleScene (flat), get a ground point from
      `TerrainQuery.TryFindGround(playerPos, …)`, spawn a cube with `Rigidbody` +
      `TerrainRigidbodyGuard` (no `TerrainAnchor`) 120 m from the player, 30 m above that point.
      Action: read `guard.IsHeld` after 0.05 s and the cube's position after 3 s. Expected:
      `IsHeld == true` already after 0.05 s (first check is immediate, not staggered), position
      within 0.1 m of spawn (no terrain collider has streamed in there yet, but the field has ground
      30 m below, inside `probeDistance`).
- [ ] **Released once ground streams in.** Setup: continue from the above. Action: move the player
      next to the cube's spawn point, wait 5 s, re-read. Expected: `IsHeld == false`, cube resting
      within 1 m of `TerrainQuery.TryFindGround` at that XZ.
- [ ] **Fall from > 60 m is free.** Setup: same spot as above, but spawn the cube 100 m above the
      field ground (beyond `probeDistance`). Action: sample `IsHeld` and y every 0.1 s for 2 s.
      Expected: `IsHeld` never `true`; y decreasing under gravity. Then (anchor the cube or bring the
      player over) it lands rather than freezing in mid-air forever.
- [ ] **Rescue from inside rock.** Setup: spawn a second cube 2 m below ground (inside solid rock)
      near the player. Action: wait 1 s, read position. Expected: cube now sits ~`rescueLift` above
      the surface directly over it (the `Rescued` branch fired).
- [ ] **Rescue under an overhang exits upward through the nearest rock, not onto the roof.** Setup:
      a canyon/overhang biome in `Assets/Scenes/Biomes/`, or `StampSphere(…, affectsPhysics: true)` a
      floating slab ~10 m thick 8 m above flat ground. Place a cube with its centre of mass 1 m
      inside the ground under the slab. Action: wait 1 s. Expected: cube ends up just above the
      ground, in the air gap under the slab — **not** on top of the slab. Then bury one inside the
      slab itself: it exits at the slab's top surface.
- [ ] **Resting contact never rescues.** Setup: 10 guarded cubes (1 m) and a guarded capsule with
      its pivot at the feet, resting on LOD0 ground for 30 s on rough terrain. Action: log every
      teleport (position jump > 0.3 m between FixedUpdates). Expected: zero — the centre-of-mass
      sample plus `insideMargin` (0.25) keeps the field's few-cm bulge above the 0.5 m collider from
      counting as "inside".
- [ ] **Held on a non-terrain platform (Important-fix regression).** Setup: place a plain
      `BoxCollider` (no `Rigidbody`) platform with no terrain anywhere nearby, drop a guarded cube
      onto it. Expected: `IsHeld` goes `false` — the "any non-trigger collider below" probe, not a
      terrain-layer-only mask, is what makes this pass; this is the scenario the offline compile
      gate cannot exercise.
- [ ] **Edit wakes a sleeping crate.** Setup: drop a crate, let it sleep. Action:
      `Modifications.CarveSphere(center, radius, affectsPhysics: true)` under it. Expected: crate
      wakes and falls onto the new surface.
- [ ] **Waker is inert in edit mode, ignores terrain colliders.** Action: exit Play mode, trigger an
      edit-mode chunk rebuild (tweak a biome asset), profile. Expected: no
      `FindFirstObjectByType` from `TerrainPhysicsWaker.EnsureBound` in the profile, no errors. In
      Play, a large-region `Wake` (e.g. a 60 m `CarveSphere`) still wakes a sleeping crate at the
      far end of the region (terrain colliders no longer fill the 256-hit buffer first).
- [ ] **Wake survives a scene reload.** Setup: reload `SampleScene` once in the Editor
      (`EditorSceneManager.LoadScene`, or Play → Stop → Play). Action: repeat the crate-wake check
      above. Expected: it still fires — confirms `TerrainPhysicsWaker`'s post-reload `RegionModified`
      rebind actually runs.

## Task 7 — `TerrainAnchor` / `CollisionTilePlanner`

- [ ] **NUnit.** Action: run `CollisionTilePlannerTests` (4). Expected: all green (includes the
      corrected `NegativeCoordinatesFloorCorrectly` test data — sphere at `(-0.25,-8,-24)` r=0.1 →
      exactly tile `(-1,-1,-2)`).
- [ ] **No duplicate anchors across a domain reload.** Setup: attach `TerrainAnchor` to a
      GameObject in a scene. Action: enter/exit Play mode, including a scripted domain reload while
      the component is enabled. Expected: `TerrainAnchor.All` never contains a duplicate entry for
      that instance.

## Task 8 — `TerrainCollisionStreamer`

- [ ] **Geometry parity.** Setup: flat `WorldConfig` (`useGlobe: 0`), `TerrainAnchor` on the
      player, `TerrainCollisionStreamer` on the `Terrain` object with `ignoreClipmapCoverage`
      ticked, enter Play mode. Action: for `coord = Level0Coord(player.position)`, compare
      `MeshCollider.bounds` of the L0 chunk vs. `CollisionTile_x_y_z`; raycast straight down from
      100 m at 20 random XZ points inside the tile against each collider. Expected: centres/sizes
      within 0.01 m, hit distances equal within 0.001 m. Untick `ignoreClipmapCoverage` afterward.
- [ ] **Distant NPC grounds.** Setup: 200 m from the player, spawn a capsule with `Rigidbody`,
      `TerrainRigidbodyGuard`, `TerrainAnchor(physicsRadius 12, navRadius 0)`, 20 m above
      `TerrainQuery` ground. (With the default `navRadius 40` tiles reach 40 m instead — see the
      radius check below.)
      Expected: within 3 s it rests within 1.2 m of ground, not held, no exceptions; Profiler shows
      no frame over 33 ms attributable to `TerrainCollisionStreamer.Update`.
- [ ] **Forward hand-off (player walks toward NPC).** Action: teleport the player toward the NPC
      in 8 m steps, 0.5 s apart; sample NPC y every 0.1 s. Expected: no drop > 0.1 m in any sample;
      the NPC's tile is released once its coord's clipmap collider is ready.
- [ ] **Reverse hand-off (player walks away).** Setup: an anchored NPC resting inside the LOD0 box
      near its centre. Action: teleport the player away in 8 m steps, 0.5 s apart, until the NPC is
      ~200 m outside the box. Expected: NPC y never drops > 0.1 m between samples;
      `CollisionTile_*` appears under it before its coord leaves the box.
- [ ] **NPC 16–32 m behind a player crossing a box boundary never drops.** Setup: an anchored NPC
      (`physicsRadius 12, navRadius 0`) resting 16 m, then separately 24 m and 32 m, behind the player along x.
      Action: move the player forward along x in 1 m steps, one step per frame, across several
      32 m box steps (watch `Level0BoxChanged`); sample NPC y every frame. Expected: no drop > 0.1 m
      at any box step — the 2-chunk coverage margin means a tile already exists under the NPC for
      both chunk layers a single 32 m step uncovers.
- [ ] **Tiles reach `max(physicsRadius, navRadius)`.** Setup: one anchor far from the player with
      `physicsRadius 0, navRadius 40`. Expected: `CollisionTile_*` built out to ~40 m around it
      (≈ 120–160 tiles incl. empty ones), and `NavMesh.SamplePosition` succeeds 35 m from it on
      ground. With `physicsRadius 0, navRadius 0` no tiles are built.
- [ ] **Burst of 40 anchors doesn't delay player ground.** Setup: note how long the clipmap takes to
      report `IsLevel0ColliderReady` for the player's coord after a 200 m teleport with no other
      anchors. Action: spawn 40 anchored capsules (defaults) 150–300 m from the player on the same
      frame as another 200 m player teleport. Expected: player's coord collider ready within ~1.5×
      the baseline; never more than `clamp(processorCount − 2, 1, 6)` tile tasks running at once
      (instrument `s_tiles` with a debugger or a temporary counter); frame time stays under 33 ms.
- [ ] **Edit rebuild without a gap.** Setup: an NPC resting on a tile outside the LOD0 box. Action:
      `Modifications.StampSphere(center, radius, depth, affectsPhysics: true)` beside (not under)
      the NPC. Expected: NPC y never drops > 0.1 m during the rebuild; `TileChanged(added=true)`
      fires again for that tile; Profiler shows no inline `MeshCollider` cook in
      `TerrainCollisionStreamer.Update` (confirms the collider stays bound to the old cook while the
      mesh is rewritten underneath it).
- [ ] **Cap ranking.** Setup: `maxCollisionTiles = 8`, one anchor far from the player, a second
      farther still. Action: move the first anchor across several tiles. Expected: tiles near the
      moving anchor keep being built, the farthest are released, live tile count never exceeds 8.
- [ ] **Disable/enable.** Action: disable the streamer. Expected: `TileChanged(added=false)` once
      per bound tile; all `CollisionTile_*` children removed except pooled ones. Re-enable: tiles
      rebuild with no exceptions.

## Task 9 — `NavPatchPlanner`

- [ ] **NUnit.** Action: run `NavPatchPlannerTests` (11). Expected: all green, matching the
      Python-replica results recorded in `task-9-report.md`.
- [ ] **Radial gizmo, tangent + no jitter.** Setup: globe world with `TerrainNavMesh` consuming the
      planner. Action: inspect a radial patch's oriented `Bounds` gizmo in the Scene view. Expected:
      sits tangent to the sphere at the anchor cluster's quantised centre; no rotation jitter for
      small anchor moves.
- [ ] **Merge/reject gizmos.** Action: cause two patches to nearly merge, once where every member
      anchor fits the merged box and once where one doesn't. Expected: a valid merge shows one box
      containing every anchor it claims; a rejected merge shows two separate, non-overlapping
      gizmos rather than one recentred box that strands an anchor.

## Task 10 — `TerrainNavMesh`

- [ ] **Flat play mode.** Setup: SampleScene on `WorldConfig_Flat`, `demoBootstrap` on,
      `TerrainNavMesh` + `TerrainCollisionStreamer` on `Terrain`. Action: enter Play, wait 10 s,
      `execute_code`:
      ```csharp
      var p = MCChunkManager.Instance.target.position;
      bool near = UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas);
      var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
      return $"near={near} d={(hit.position-p).magnitude:F2} verts={tri.vertices.Length}";
      ```
      Expected: `near=True`, `d < 2.1`, `verts > 1000`; no "agent type id" warning for id 0. Take a
      Scene-view screenshot with the AI Navigation overlay on.
- [ ] **Incremental rebuild timing.** Action: log `Time.realtimeSinceStartup` when `IsBuilding` goes
      true→false for the first build, then again for the update from one
      `CarveSphere(…, affectsPhysics: true)` near the player. Expected: the edit update takes < 25%
      of the first build. If not, shrink `UpdateNavMeshDataAsync`'s `localBounds` to the accumulated
      dirty region intersected with the patch.
- [ ] **Paths.** Action: `NavMesh.CalculatePath` to a point 60 m away (found via
      `TerrainQuery.TryFindGround`) on gentle dunes; separately, across a canyon wall in
      `Assets/Scenes/Biomes/`. Expected: `PathComplete` on dunes; `PathPartial` or a path around the
      wall, never through rock, on the canyon.
- [ ] **Async source safety.** Setup: start a rebuild (edit terrain so `IsBuilding == true`).
      Action: while it's running, call `CarveSphere(…, affectsPhysics: true)` every frame for ~2 s
      at the same spot. Expected: no crash, no console errors; NavMesh overlay shows no stray/spiked
      triangles once builds settle; `CalculateTriangulation` vertices stay within the patch's world
      bounds (validates the "sources are read synchronously the same frame" assumption in
      `TerrainNavMesh.Build`).
- [ ] **Agent-type validation.** Action: set `navAgentTypeIds = {0, 99}`. Expected: exactly one
      warning for id 99, NavMesh built for id 0. At runtime, change to `{0}` then back to `{0, 99}`.
      Expected: no errors, `PatchVersion` bumps once per landed rebuild that actually removes data
      (see next item), no duplicate warning.
- [ ] **PatchVersion bump semantics.** Action: change `navAgentTypeIds` from `{0, 99}` to `{0}`
      (data removed) and separately disable `TerrainNavMesh` mid-build. Expected: `PatchVersion`
      bumps exactly once for the removal and once on disable; it does **not** bump for an ordinary
      rebuild that lands without removing anything.
- [ ] **Disable during a running build.** Action: disable `TerrainNavMesh` mid-build. Expected:
      `NavMesh.CalculateTriangulation().vertices.Length == 0` and no errors when the in-flight op
      finishes; re-enabling brings the NavMesh back.
- [ ] **Globe hand-off.** Setup: `useGlobe: 1`. Action: move the player > 128 m. Expected: a new
      radial patch builds before the old one disappears — `SamplePosition` under the player never
      fails during the hand-off.
- [ ] **Game layers, no double-bake.** Action: set `navGameLayers` to include `Default` while
      `terrainLayer = 0`. Expected: roughly the same triangle count as `navGameLayers = 0` (terrain
      not counted twice), plus carve-outs around a test box collider on that layer.
- [ ] **Steady walk, no hitches.** Action: walk the player steadily in one direction for 60 s on the
      flat world. Record frame times / `Time.unscaledDeltaTime` spikes, count `IsBuilding` rising
      edges, log `PatchVersion`. Expected: no hitches from full-patch rebuilds as the flat patch
      bounds move; `PatchVersion` unchanged throughout (no patch is ever removed by this). If
      rebuilds hitch, fall back to the accumulated dirty region as `localBounds`. Repeat over dense
      forest with trunk obstacles on and several anchors spread out: profile
      `FloraObstacles.OnCollectSources` and the capsule source count per rebuild (they cover the
      whole flat patch).
- [ ] **Agent capsule doesn't punch a hole.** Setup: a `NavMeshAgent` capsule collider on a layer in
      `navGameLayers`, and a second rig whose capsule collider sits on a **child** of the
      `NavMeshAgent`/`CharacterController` GameObject. Action: force a rebuild. Expected: no hole in
      the NavMesh under either (confirms `IsMovingCollider`'s parent lookup).
- [ ] **Rigidbody player and crates not carved.** Setup: a Rigidbody-driven player (no
      `NavMeshAgent`/`CharacterController`) and a stack of Rigidbody crates on a layer in
      `navGameLayers`, plus a static `BoxCollider` building on the same layer. Action: force a
      rebuild. Expected: no NavMesh hole under the player or crates; the building is still carved.
- [ ] **Retired NavMeshData is destroyed.** Action: toggle `navAgentTypeIds` between `{0}` and `{0, 1}`
      (a second valid agent type) a few times, and on a globe world walk far enough for radial patches
      to retire; count `Resources.FindObjectsOfTypeAll<NavMeshData>()`
      before and after, and disable `TerrainNavMesh` mid-build then re-enable. Expected: the count
      returns to the number of live (patch × agent type) slots once builds settle; no errors from
      destroying data under a running build.

## Task 11 — `TerrainNavAgentGuard`

- [ ] **Cold start, 10 NPCs.** Setup: 10 NPC capsules (`Rigidbody` + `TerrainRigidbodyGuard` +
      `NavMeshAgent` (disabled) + `TerrainNavAgentGuard` + `TerrainAnchor(physics 12, nav 30)`) at
      random points 30–150 m from the player, before any NavMesh exists. Action: call
      `TrySetDestination(playerPos)` on each. Expected within 15 s: every guard's `Ready == true`,
      every agent's `remainingDistance` decreasing, zero console errors containing `"can only be
      called on an active agent"`.
- [ ] **Physics hand-off.** Action: on one NPC, `SuspendForPhysics()`, `AddForce(Vector3.up * 8,
      ForceMode.VelocityChange)`, wait 2 s, `ResumeFromPhysics()`. Expected: becomes `Ready == true`
      again and resumes toward its pending/previous destination without an error.
- [ ] **Zero-warp regression on a moving flat patch.** Setup: several agents already `Ready` and
      walking. Action: walk the player 200 m across `WorldConfig_Flat` so the flat patch re-centers
      repeatedly (instrument `BecameReady`). Expected: zero re-warps for any agent that stayed
      `Ready` throughout — a moving `PatchVersion`-unrelated patch bounds change must not re-warp a
      live, on-mesh agent.
- [ ] **Kinematic-by-design body preserved.** Setup: a Rigidbody prefab-configured
      `isKinematic = true` at `Awake`. Action: `SuspendForPhysics()` / fall off NavMesh. Expected:
      `_rb.isKinematic` stays `true` across the suspend/resume cycle (not forced dynamic).
- [ ] **Disabled-prefab NavMeshAgent race.** Setup: prefab with `NavMeshAgent` disabled (the
      recommended setup). Action: enter Play. Expected: no Unity placement attempt at all. Separately,
      leave it enabled by mistake once: expected no console error either (defensive `Awake` disable
      covers it, though sibling `Awake` order is not guaranteed).
- [ ] **Patch removal under a walking agent.** Setup: flat or globe world with an adjacent patch
      available. Action: remove the NavMesh patch directly under a `Ready`, walking agent (force a
      rebuild that drops it, or shrink update bounds). Expected: zero `"can only be called on an
      active agent"` errors; agent re-placed (via the adjacent patch, or parked on physics) with its
      stored `_destination` reapplied once `Ready` again.
- [ ] **Suspend during the off-mesh window.** Action: call `SuspendForPhysics()` between a patch
      removal and re-placement (`_agent.enabled == false`, mid-poll). Expected: no console errors;
      `ResumeFromPhysics()` afterward re-samples and warps normally.

## Task 14 — `FloraObstacles`

- [ ] **NUnit.** Action: run `FloraObstacleTests` (4, includes `CapsuleDistanceMeasuresToTheSurface`).
      Expected: all green.
- [ ] **GetObstacles against real plots.** Setup: set `obstacleRadius = 0.4`, `obstacleHeight = 3`
      on one tree species in a biome flora list (play-mode edit, restore after). Action: enter Play,
      wait for `PlantScatter.PlotsPending == 0`, call `GetObstacles(new Bounds(player.position,
      Vector3.one*60), list)`. Expected: non-empty list; every `basePos` matches a drawn instance of
      that species only; `radius / 0.4` falls within the species' `scaleRange`; `height / radius ==
      7.5`. With all values back at 0, expect an empty list.
- [ ] **NavMesh carve, correct hole radius.** Setup: `FloraObstacles`, `TerrainNavMesh`, a player
      `TerrainAnchor`, the species above set to obstruct. Action: stand next to a grove, force a
      patch rebuild. Expected: round holes at each trunk on the NavMesh overlay screenshot, hole
      radius ≈ `obstacleRadius × instanceScale + agentRadius` at ground level (not narrower toward
      the base) — measure via the overlay or `NavMesh.FindClosestEdge` from the trunk base. A
      `NavMeshAgent` sent across the grove gets a path (`agent.path.corners`) that bends around
      trunks.
- [ ] **Physics.** Setup: same as above. Action: drop/roll a Rigidbody sphere at a trunk within
      `physicsRadius`. Expected: it bounces off; `FloraObstacles.ActiveColliders > 0`; child
      `FloraObstacle` objects sit on trunks (Scene-view gizmos) on `terrainLayer`.
- [ ] **Pool behaviour.** Action: walk 100 m away from the grove. Expected: after 0.5 s colliders
      move to new trunks or deactivate; `ActiveColliders` never exceeds `maxColliders`; standing
      still, child transforms stop changing.
- [ ] **Game-layer double-bake guard.** Setup: `navGameLayers` includes `terrainLayer`. Action:
      rebuild a patch. Expected: trunks are still holes, with no walkable disc on top of them
      (`IsTerrainCollider` correctly excludes `FloraObstacles`' own colliders from the game-layer
      source list).
- [ ] **Planet mode.** Setup: globe world. Action: inspect trunk capsules and NavMesh holes near a
      grove. Expected: capsules align with radial up (Scene view); NavMesh holes are circular on the
      tangent patch.

---

## Reference scene (brief Step 2)

- [ ] **Build `Assets/Scenes/GameIntegration.unity`.** Flat config, `demoBootstrap` off. Player
      wired by hand the way the game will (no new controller code): capsule with
      `CharacterController`, `SimplePlayerController` (auto-adds `GravityAligner` +
      `CharacterGravity`), `TerrainTarget`, `TerrainAnchor`, camera child. In the Inspector, set
      `GravityAligner.terrain` = the manager and `CharacterGravity.densityField` = the config's
      effective density. Add 20 NPCs wired as in the Task 11 checklist above (Task 11 Step 2 setup)
      and a stack of 10 Rigidbody crates.
- [ ] **60 s play test.** Action: enter Play mode, run for 60 s, sample every 1 s. Expected: no
      console errors; all 20 NPC guards `Ready == true`; no NPC below `TerrainQuery` ground by more
      than 1 m at any sample.

## Full test run (brief Step 3)

- [ ] **All EditMode tests.** Action: MCP `run_tests`, mode `EditMode`, filters
      `TerrainGen.Tests` and `TerrainGen.LSystem.Tests` (see "How to run" for the class list).
      Expected: all pass, 95 tests total, 0 failures.

## Task 12 — Globe NavMesh spike (decision gate, from the plan)

Not started this session (deferred per ledger ruling). Run before any globe NavMesh work; write the
result into a new "Globe NavMesh findings" section of `INTEGRATION.md` when done, per the plan.

- [ ] **Step 1 — scene.** `Assets/TerrainGen/Scenes/Spikes/GlobeNavSpike.unity` (throwaway, not
      committed unless it passes): manager on `Config/WorldConfig.asset` (globe), demo player,
      `TerrainCollisionStreamer`, `TerrainNavMesh`. Teleport the player to a point ~45° between two
      cube-face axes (worst triplanar-blend zone) using `TryGetPlanetSpawnPoint`.
- [ ] **Step 2 — rotated instance works.** After 15 s, `NavMesh.SamplePosition(player, 3)` succeeds;
      `NavMeshData.rotation`'s up is within 1° of `PlanetGravity.UpAt(player)`.
- [ ] **Step 3 — agent orientation.** Spawn an agent with `PlanetGravityBody (alignToSurface)` +
      the Task 11 guard stack; `agent.updateUpAxis` at its default. Walk it 100 m. Record whether
      the transform stays upright relative to radial up (angle < 10° at 100 m on radius 1000). If
      `updateUpAxis` fights `PlanetGravityBody`, set `updateRotation = false` and let
      `PlanetGravityBody` + look-at-velocity own rotation; record which works.
- [ ] **Step 4 — re-plant hitch.** Walk the agent across a patch edge. Measure frames the agent is
      not `Ready` (target ≤ 3 at a 0.25 s check interval — reduce to 0.05 s for this test) and the
      build time of the new 256 m patch.
- [ ] **Step 5 — curvature limits.** At `radius = 1000`, check the NavMesh covers the patch corners
      (expected sag = 16 m at a 256 m patch). If corner slopes exceed the agent's `maxSlope`, reduce
      `patchSize` to 160.
- [ ] **Step 6 — decision gate.** Write results into `INTEGRATION.md` under "Globe NavMesh
      findings":
      - **Pass (Steps 2–4 all OK):** proceed to Task 13 (radial patches in production) as planned.
      - **Fail on agent orientation:** Task 13 needs a custom mover — `agent.updatePosition = false`,
        `updateRotation = false`, body moved by `Rigidbody.MovePosition(agent.nextPosition)` and
        rotated by `PlanetGravityBody`.
      - **Fail on rotated instances entirely:** drop NavMesh for the globe; Task 13 becomes a
        steering layer (NavMesh-free, density-field line-of-sight + local avoidance) — write a
        separate plan for it rather than improvising inside this one.
