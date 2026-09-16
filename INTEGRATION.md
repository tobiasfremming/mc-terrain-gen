# Game integration guide

Checklist for wiring a game (Rigidbodies, NavMeshAgents) onto this terrain. Written from the
implemented code on `feat/game-integration` (Tasks 1–11, 14). Tasks 12–13 (globe NavMesh
validation, radial patches in production) are **not done** — see "Known limitations".

## 1. Add the package

- Add `Assets/TerrainGen` as a git submodule of the game repo.
- Required packages (already in this repo's `Packages/manifest.json`, add to the game's if missing):
  - `com.unity.ai.navigation` `2.0.9`
  - `com.unity.render-pipelines.universal` (URP; `17.2.0` here)
- Game asmdefs that use terrain types (player, NPCs, spawners) add a reference to
  `TerrainGen.Runtime` (the asmdef name declared in
  `Assets/TerrainGen/Scripts/TerrainGen.Runtime.asmdef`). It already references
  `Unity.AI.Navigation`, so a game asmdef referencing it gets `NavMeshAgent`/`NavMesh` transitively
  through Unity's own package asmdef, but add `Unity.AI.Navigation` directly too if the game code
  calls `UnityEngine.AI` APIs itself.
- Do not reference `TerrainGen.Editor`, `TerrainGen.Tests` or `TerrainGen.LSystem` from game code —
  only `TerrainGen.Runtime` is the public surface.

## 2. Scene setup

One `MCChunkManager` per scene, with three sibling components **on the same GameObject**:

| Component | Role |
|---|---|
| `MCChunkManager` | chunk prefab, `worldConfig`, compute shaders — the clipmap itself |
| `TerrainCollisionStreamer` | collider-only LOD0 tiles around `TerrainAnchor`s outside the LOD0 box |
| `TerrainNavMesh` | builds/retires NavMesh patches from terrain colliders + tiles |

Add `FloraObstacles` there too (or anywhere in the scene — it only needs to find `PlantScatter` and
`MCChunkManager.Instance`) if trees should block bodies and carve the NavMesh.

`WorldConfig` fields the game must set (exact names in `Assets/TerrainGen/Scripts/WorldConfig.cs`,
`[Header("Game Integration")]`):

| Field | Set to |
|---|---|
| `demoBootstrap` | `false` — the game brings its own player, camera and lighting. `true` (the default) makes `PlayerBootstrap` spawn the demo capsule and `DesertAtmosphere` set fog/far-plane, which a real game does not want. |
| `terrainLayer` | the index of a `Terrain` layer the game creates in its TagManager. The package cannot add layers for you. Every chunk, collision tile and flora-obstacle collider is put on this layer (`ApplyLevelSettings`, `TerrainCollisionStreamer.StartBuild`, `FloraObstacles.RefreshColliders`). NavMesh, ground probes and the Rigidbody/agent guards all read it indirectly through colliders on this layer. |
| `navAgentTypeIds` | the game's NavMesh agent type ids (Navigation window > Agents). `{ 0 }` (Humanoid) is the default; one NavMesh instance per patch is built per id. |
| `navGameLayers` | building/prop layers whose physics colliders should be baked into the terrain NavMesh alongside terrain geometry. Leave `0` if the game has no static buildings yet. Automatically skipped: colliders attached to a `Rigidbody` (kinematic or not — players, crates, moving platforms), colliders with a `NavMeshAgent` or `CharacterController` on their own GameObject or any parent, and the terrain's own colliders (even if this mask includes `terrainLayer`), so nothing moving is baked and nothing is double-baked. `NavMeshModifier` markups are **not** honoured — every remaining collider bakes as walkable area 0; put anything that must not bake on a layer outside this mask. |
| `maxTileBuildsPerFrame` | collision-tile builds started per frame (default `2`, range 1–16). |
| `maxCollisionTiles` | hard cap on live collision tiles (default `512`); nearest-to-an-anchor tiles win over the cap. |
| `navUpdateInterval` | minimum seconds between NavMesh rebuild batches while terrain keeps changing (default `0.5`). |
| `navBuildHeightMesh` | `true` (default) bakes a height mesh so agents' feet follow bumpy marching-cubes ground instead of flat nav polygons. Leave on unless profiling shows it's too expensive. |

## 3. Player

- Add `TerrainTarget` to the player (or camera rig) — it finds the scene's `MCChunkManager` in
  `Awake` and sets `manager.target = transform`. This is the only thing the clipmap reads to decide
  which chunks to stream and at what LOD (`manager.target.position`).
- Add `TerrainAnchor` too, with **`physicsRadius = 0`** and `navRadius` set to how far the player's
  NavMesh should reach (default `40`). The player's own ground comes from the clipmap's LOD0 box, so
  it needs no physics radius; `navRadius` drives the player's NavMesh patch and, because NavMesh is
  baked from colliders, also the collision tiles that patch needs wherever it reaches past the LOD0
  box face (tiles are built out to `max(physicsRadius, navRadius)`; see §7 — the nearest box face can
  be only ~16 m from the player). The target is always the *primary* anchor for `TerrainNavMesh`'s
  flat-patch planning (`NavAnchor.primary`), never dropped from the flat NavMesh box even under the
  anchor cap; with no `TerrainAnchor` on it, or `navRadius <= 0`, its NavMesh radius falls back to
  `40`.
- **Flat world:** normal Rigidbody/CharacterController gravity. The demo player
  (`SimplePlayerController` + `GravityAligner` + `CharacterGravity`) is a working reference if the
  game wants its own controller shaped the same way; none of the terrain integration code requires
  it specifically.
- **Globe:** add `PlanetGravityBody` to any Rigidbody-driven player. It sets `Rigidbody.useGravity =
  false` in its own `Awake` — do not also set it elsewhere, and do not rely on ordering, since the
  component does this itself regardless of when it runs relative to other `Awake`s. For a
  `CharacterController`-driven player use `GravityAligner` + `CharacterGravity` instead (mirrors
  `PlanetGravityBody`'s role but for controller-driven movement); both read "up" from the same
  `PlanetGravity.UpAt(position)`.

## 4. NPC prefab checklist

Per NPC that should physically simulate and/or navigate:

1. `Rigidbody` — Interpolate, Collision Detection = Continuous Speculative.
2. `TerrainRigidbodyGuard` — holds the body kinematic while the density field has ground within
   `probeDistance` (60 m) below it but no collider has streamed in there yet; with no field ground in
   range the body is simply falling and is left free (a body dropped from higher than
   `probeDistance` falls normally). The first check runs on the body's first `FixedUpdate`. It also
   rescues the body if an edit or a streaming gap leaves its **centre of mass** buried deeper than
   `insideMargin` (density `0.25`, so ordinary contact with the 0.5 m collider approximation never
   triggers it): the body is moved to the nearest solid→air exit straight above that point
   (`TerrainQuery.TryFindExitAbove`, up to 50 m) plus `rescueLift` — out through the rock overhead,
   never onto the top of an overhang or cave roof. Its "is there a collider below" probe counts
   **any** non-trigger collider that isn't part of the body itself — not just terrain. A building or platform holds the body exactly
   as well as terrain does; the check is deliberately not filtered to `terrainLayer`, so a body
   resting on a non-terrain support is never frozen waiting for terrain that was never going to be
   under it.
3. `NavMeshAgent` — **leave the component disabled in the prefab.** `TerrainNavAgentGuard.Awake`
   disables it defensively too, but sibling `Awake` order between components on the same GameObject
   is not guaranteed, so Unity's own `OnEnable` could try to place the agent (and warn/fail on an
   absent NavMesh) before the guard's `Awake` runs if it starts enabled.
4. `TerrainNavAgentGuard` — owns the agent's entire lifecycle: samples `NavMesh.SamplePosition`
   until a patch exists under the NPC, `Warp`s it on, and re-samples if the NavMesh under it is ever
   removed (patch retired, agent-type-id change). **Game code must never touch the `NavMeshAgent`
   directly** — reading `hasPath`/`destination`/calling `SetDestination` on an enabled-but-off-mesh
   agent throws `"can only be called on an active agent that has been placed on a NavMesh"`, which is
   exactly the state the guard has to pass through on spawn and on every re-placement. Use only:
   - `TrySetDestination(Vector3)` — records the destination (the guard is the single source of
     truth for it) and applies it immediately if `Ready`; returns `false` otherwise. The stored
     destination survives a suspend/resume or a re-placement and is re-applied automatically once
     `Ready` fires again (event `BecameReady`).
   - `ClearDestination()` — call on arrival or when the goal is abandoned. The guard does not detect
     arrival itself.
   - `SuspendForPhysics()` / `ResumeFromPhysics()` — around knockback/ragdoll. Suspend disables the
     agent and hands the Rigidbody back to `TerrainRigidbodyGuard`; resume re-samples the NavMesh
     under the body's current position and warps back onto it on the next `Update`.
   - `Ready` (`bool`) — `true` only when the agent is enabled and on a NavMesh.
5. `TerrainAnchor` — required for any NPC that must keep simulating physically and/or navigating
   *away* from the player (outside the clipmap's LOD0 box). Collision tiles are built out to
   `max(physicsRadius, navRadius)` around it; `navRadius` also includes it in the NavMesh patch plan.
   An NPC's NavMesh therefore reaches `max(physicsRadius, navRadius)` (rounded out to whole 16 m
   tiles) — as far as its tiles do, inside the patch box. Remember the LOD0 box is **not** centred on
   the player (§7): an unanchored NPC 16 m from the player can already be outside it. NPCs without an
   anchor only have ground/NavMesh inside the player's LOD0 box — pair them with
   `TerrainRigidbodyGuard` regardless (cheap, and it also rescues them if an edit ever drops them into
   rock inside the box).
6. **Globe:** also add `PlanetGravityBody`.

Kinematic ownership between `TerrainRigidbodyGuard` and `TerrainNavAgentGuard` changes hands in a
fixed order so the two guards never fight over the flag (see `TerrainNavAgentGuard`'s class
comment): taking control disables the rigidbody guard **first** (its `OnDisable` restores whatever
kinematic value *it* was holding), then forces `isKinematic = true`; parking on physics restores the
Rigidbody's **original** `isKinematic` (captured in `Awake`, not a hardcoded `false` — a body that is
kinematic by design stays kinematic) and only then re-enables the rigidbody guard.

## 5. Spawning

```csharp
if (TerrainQuery.TryFindGround(spawnPos, searchUp: 50f, searchDown: 200f, out Vector3 p, out Vector3 n))
    body.position = p + n * halfHeight;
```

`TerrainQuery.TryFindGround` samples the density field directly (no collider required), so it works
before any collider has streamed in — the overload without an explicit `DensityField`/`up`
(`TryFindGround(Vector3, float, float, out, out)`) resolves both from `MCChunkManager.Instance`
automatically (`PhysicsField`, `PlanetGravity.UpAt`). Spawn even if the ground hasn't cooked yet: the
guards (`TerrainRigidbodyGuard`, `TerrainNavAgentGuard`) handle "ground not cooked yet" on their own
— a spawned body is simply held kinematic (or the agent left unplaced) until it is.

## 6. Edits

```csharp
MCChunkManager.Instance.Modifications.CarveSphere(center, radius, affectsPhysics: true);
MCChunkManager.Instance.Modifications.StampSphere(center, radius, depth, affectsPhysics: true);
```

Use `affectsPhysics: true` for gameplay holes/craters — the edit is baked into collision, and
`TerrainNavMesh` and `TerrainCollisionStreamer` both listen to `RegionModified` and rebuild whatever
overlaps. Sleeping bodies over the affected region are woken automatically
(`TerrainPhysicsWaker`, subscribed since `AfterSceneLoad`) so nothing floats over ground that just
changed shape.

`affectsPhysics: false` (`StampSphere`'s default) is visual only — e.g. footprints — and is ignored
by collision, NavMesh and the waker. It never re-cooks a collider.

## 7. Budgets

Defaults as shipped (`WorldConfig`, `TerrainAnchor`, `NavPatchPlanner`, `FloraObstacles`):

- **Collision tiles per anchor**: every 16 m tile (`TerrainCollisionStreamer.s_tileSize`) whose box
  touches a sphere of radius `R = max(physicsRadius, navRadius)` — a full 3D sphere, not a ground
  layer, so ≈ `1 + 6·(R/16) + 3π·(R/16)² + (4π/3)·(R/16)³` tiles on average. At the defaults
  (`physicsRadius 20`, `navRadius 40` → `R = 40`) that is **≈ 140 tiles (120–160)** per anchor; with
  `navRadius` at or below 20, `R = 20` gives ≈ 31 (26–38). Tiles that come out all air or all rock
  mesh to nothing and never bake, but they still take a slot. Keep `Σ(tiles per anchor)` under
  `WorldConfig.maxCollisionTiles` (default `512`, so only **~3–4 anchors at default radii** before
  the cap starts dropping the farthest tiles; ~16 at `R = 20`). Overlapping anchors share tiles. The
  clipmap target is not exempt: only chunks >= 2 chunks inside the LOD0 box (its central 2x2x2) count
  as covered, so a player with the default `navRadius 40` still asks for most of its ~140 tiles,
  duplicating clipmap colliders over much of the box. Budget for it, or lower the player's
  `navRadius`. Tiles at the
  cap are ranked by nearest-anchor distance — the tiles nearest a moving anchor always win, so a
  body over a dropped tile is caught by `TerrainRigidbodyGuard` instead of falling.
- **NavMesh flat cap**: `NavPatchPlanner.maxFlatExtent` = 1024 m (a code default, **not** currently
  exposed on `WorldConfig` — Task 13, deferred, is what was planned to wire `navPatchSize`/
  `navEdgeMargin` through). One flat NavMesh box covers every anchor within reach of the primary
  (player) anchor, quantised to `quantum = 32` m; an anchor whose square distance from the primary
  exceeds `maxFlatExtent / 2` on either axis is dropped from the box (the primary anchor itself is
  never dropped). Vertical range is `[min included anchor y − 256, max included anchor y + 256]`,
  also quantised.
- **NavMesh rebuild throttle**: `WorldConfig.navUpdateInterval` (default `0.5` s) between rebuild
  batches; only one batch runs at a time across all patches (`TerrainNavMesh._ops`).
- **Collision-tile build throughput**: `WorldConfig.maxTileBuildsPerFrame` (default `2`) new/dirty
  tile builds started per frame, and at most `clamp(SystemInfo.processorCount − 2, 1, 6)` tile tasks
  (mesh or bake) running at once, shared by new tiles and dirty rebuilds — a burst of new anchors
  queues rather than crowding the clipmap's own meshing tasks out of the thread pool.
- **Flora physics colliders**: `FloraObstacles.maxColliders` (default `512`), refreshed every
  `refreshInterval` (default `0.5` s, unscaled) across all anchors' `physicsRadius`; nearest-to-an-
  anchor trunks win over the cap.
- **LOD0 box**: `chunksPerLevel (6) × chunkSize (cellsPerChunk.x × cellSize = 32 × 0.5 = 16 m)` =
  96 m across, but **not centred** on `MCChunkManager.target`: it snaps to 32 m (2-chunk) steps
  (`MCChunkManager.ComputeBoxes`), spanning 32 m below to 64 m above the 32 m grid line nearest the
  player on each axis. The player is 16–48 m from the near face and 48–80 m from the far face, so
  the **nearest face can be ~16 m away**, and the box jumps 32 m at a time as the player moves.
  Anything outside it has no clipmap ground — that's exactly the gap
  `TerrainCollisionStreamer`/`TerrainAnchor` exist to fill for anchored NPCs. Tiles treat a chunk as
  clipmap-covered only when it is ≥ 2 chunks inside the box on every axis, so one 32 m box step can
  never uncover a chunk that has no tile ready under a resting NPC.

## 8. Known limitations

- **Flat NavMesh only within `maxFlatExtent` (1024 m) of the primary anchor.** An anchor further
  than that on either local axis is silently excluded from the flat NavMesh box (still gets
  collision tiles out to `max(physicsRadius, navRadius)`, just no NavMesh).
- **Trees only obstruct near `PlantScatter`'s eye** (the player, via `MCChunkManager.target`).
  `FloraObstacles.GetObstacles` reads already-built plots and never requests new ones, so a
  `TerrainAnchor` or NavMesh patch far from the player gets no trunk colliders and no carved
  trunks — distant NPCs walk through trees nobody is looking at, and plots the player has left can
  be evicted once `maxCachedPlots` is exceeded. Only species with `PlantSpecies.obstacleRadius > 0`
  and `obstacleHeight > 0` block anything; every shipped species defaults to `0` (no obstacle) —
  set these per species before flora obstruction does anything (suggested: tree trunks ~0.3–0.6 m
  radius, 2–4 m height at scale 1). A NavMesh patch built before its plots finish carries no trunk
  holes until the patch's next rebuild (physics colliders catch up within `refreshInterval`
  regardless). Trunk colliders sit on `terrainLayer`, so anything masking that layer hits trunks
  too. A capsule stands upright along local up and ignores the plant instance's own lean.
- **Unanchored NPCs outside the player's LOD0 box are held, frozen, by `TerrainRigidbodyGuard`**
  while the density field has ground within `probeDistance` below them but no collider exists there —
  they do not fall through, but they also do not move. The box is offset and steps 32 m at a time
  (§7), so "outside" can start ~16 m from the player on one side. Give any NPC that must simulate or
  navigate away from the player a `TerrainAnchor`.
- **Globe: physics works, globe NavMesh does not yet exist in production.** `PlanetGravityBody` /
  `GravityAligner` + `CharacterGravity` give correct radial gravity and orientation on the globe
  today. `TerrainNavMesh`/`NavPatchPlanner` already have a radial-patch code path
  (`NavPatchPlanner.PlanRadial`, exercised by `NavPatchPlannerTests`), but **it has not been
  validated end-to-end in the Editor** — Task 12 (globe NavMesh spike: rotated-instance sampling,
  agent orientation on a curved patch, re-plant hitch, curvature limits at the patch corners) and
  Task 13 (wiring the spike's decision into production, including `navPatchSize`/`navEdgeMargin` on
  `WorldConfig`) are both deferred until the Editor is available. Do not ship globe NavMesh without
  running Task 12 first; see `VERIFICATION.md` for the exact steps. Until then, treat globe NPC
  navigation as unsupported — physics-only (`TerrainRigidbodyGuard` + `PlanetGravityBody`) is the
  known-good path on a globe world.
- **`TerrainCollisionStreamer`/`TerrainNavMesh` are effectively singletons via static/instance
  state** (`TerrainCollisionStreamer.s_tiles` is a static dictionary; `TerrainNavMesh.Instance` is a
  single static reference) — one of each per running scene, not per split-screen player or per
  sub-scene.
- **`NavMeshModifier` markups are not honoured** by `navGameLayers` baking (`TerrainNavMesh` collects
  physics colliders with an empty markup list). Per-object exclusion is only the automatic
  Rigidbody/agent/controller filter; override areas and "ignore from build" need a separate layer.
