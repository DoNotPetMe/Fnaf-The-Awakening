# Architecture

## Assemblies

Eleven assembly definitions with a strictly acyclic graph. The shape is enforced by
`Tools/validate_project.py`, which walks the asmdef references and fails on a cycle —
Unity rejects circular assembly references, and finding out at import time is slower
than finding out in CI.

```
Grotto.Core            no dependencies
Grotto.Facility        Core
Grotto.Procedural      Core, Facility
Grotto.AI              Core, Facility, Procedural
Grotto.Audio           Core, Facility, AI
Grotto.Player          Core, Facility, Input System
Grotto.Rendering       Core, Facility, URP
Grotto.UI              Core, Facility, AI, Player, Rendering, Audio, uGUI
Grotto.DevTools        everything above
Grotto.Editor          everything (Editor platform only)
Grotto.Tests.EditMode  Core, Facility, AI, Procedural (Editor only)
```

The one non-obvious edge is `Procedural → Facility`: the geometry builder reads
`FacilityLayout`, which is the whole point — the cave's shape *is* the navigation
graph's shape.

### How gameplay reads debug state without depending on dev tools

`DebugFlags` lives in `Grotto.Core`. Dev tools write to it; gameplay only reads. That
one placement decision is what keeps the graph acyclic and lets the entire
`Grotto.DevTools` assembly be excluded from a shipping build without touching a line of
gameplay code.

---

## Three decisions that do most of the work

### 1. Facility systems are plain classes

`PowerGrid`, `Generator`, `VentilationSystem`, `WaterSystem`, `NoiseField` and
`SurveillanceSystem` are not MonoBehaviours. One MonoBehaviour — `FacilityRuntime` —
owns them and ticks them in an order written down in one place:

```csharp
Ventilation.Tick(...);    // 1. environment moves
Water.Tick(...);
Power.Tick(...);          // 2. the grid sees the loads that movement implies
Surveillance.Tick(...);   // 3. cameras wear against the supply they just got
PublishContinuousNoise();
Noise.Tick(...);          // 4. acoustics last, with every source accounted for
```

Three consequences worth the trade:

- Update order is explicit rather than an emergent property of script execution
  settings that nobody dares change.
- The whole resource model can be stepped from an edit-mode test with no scene, no
  frame loop and no Play Mode. `SimulationTests` exercises a breaker trip in
  microseconds.
- There is exactly one place to look when asking what happens each frame.

MonoBehaviours are reserved for things that are genuinely *in the world* — a door, a
camera, a light — and those talk to the systems rather than being them.

### 2. Animatronic position is discrete

Characters navigate a graph of named nodes. They **are** at `ADIT_N`; transit is
presentation with a duration and a progress value. There is no NavMesh.

This is a design requirement, not a shortcut. The game is about a player reading
positions off a screen, so positions must be discrete, nameable, loggable and forceable
from a console. `ai.move vesper CRAWL_A` is only possible because "where it is" is a
value rather than a coordinate. The map widget, the camera feeds, the geophone array,
the debug overlay and the tests all agree, because they all read the same field.

Mesh-level movement is then layered on top: `TickTransit` interpolates the transform
between node positions while `CurrentNode` stays at the origin until arrival.

### 3. One layout drives everything, and the scene names no room

`FacilityLayout` describes nodes (id, kind, world position, size, camera, coupling),
links (traversal mask, water gates, barrier, traverse time, noise transmission), which
node plays which structural role, where each character lives, and what the site is made
of. From it, at load:

| Consumer | Builds |
|---|---|
| `FacilityGraph` | What the AI navigates |
| `FacilityGeometrySpawner` → `FacilityBuilder` | The cavern, the connectors, the lighting rig, the water table |
| `SiteFixtureSpawner` | The blast doors, the grate, the floodlights, the camera rig |
| `CastSpawner` | The five characters, at this site's home rooms |
| `MapWidget` | The plan on the monitor |

Move a room and all five follow.

The important consequence is that **`Facility.unity` contains no room names at all.**
It holds the seat, the camera rig, the interface, the dev tools and a handful of spawner
components. That is what makes three maps possible without three scenes — and it is also
why the scene file stays small, text-only and reviewable in a pull request, which is not
something you can usually say about a level.

Which site loads is decided before anything is built. `SiteCatalog` is the one place
that knows which sites exist; code is the source of truth and a baked asset in
`Resources` is an override, so a fresh clone works with no setup and a designer's
tweaked asset is never silently ignored.

### Changing site means reloading the scene

`SessionRequest` carries "play night N at site S" across a scene reload, and the front
end goes through it rather than rebuilding live. That is deliberate: the geometry, the
fixtures, the camera rig and the cast are all built at `Awake` from the chosen layout,
and tearing that down while half the scene holds references into it is a worse answer
than a one-second reload — which is also exactly what a loading beat in this kind of
game is for.

---

## The traversal rule

Every "can it get there" question in the game routes through one place, so the AI, the
gizmos, the console and the tests can never disagree about what the map currently
permits.

The pure part — capability mask and water gates — is
[`TraversalRules.IsPermitted`](../Assets/Game/Scripts/Facility/TraversalRules.cs),
a static function with no dependencies, which is what the tests exercise. The part that
needs live objects — a shut door, a lit node — stays in `FacilityRuntime.CanTraverse`,
which calls the pure function first.

```csharp
public bool CanTraverse(FacilityLink link, NodeId from, NodeId to,
    TraversalMask capability, bool respectsBarriers = true, bool lightAverse = false)
```

Behaviours **route optimistically and move conservatively**: pathfinding ignores doors
and light, so a character still walks up to a shut door rather than concluding the
station is unreachable and going home. The individual step is then validated, and a
refusal calls `OnStepBlocked` so the behaviour can react — which is how Vesper counts
her rebuffs and eventually drops out of the ceiling.

---

## Night flow

```
GameBootstrap  (-1000)  process settings, profile, command line
NightController (-900)  briefing → active → resolving → complete
FacilityRuntime (-850)  owns and ticks the simulation
FacilityGeometry(-800)  generates the cave
AIDirector      (-700)  ticks the cast in a fixed order
PhantomCotton   (-690)
StationController(-600) input
PostFx / Atmos  (-500)
AudioDirector   (-400)
StationHud      (-300)
Menus           (-200)
DevConsole      (-100)
```

`NightController` knows nothing about animatronics, power or water. It owns the clock
and publishes signals; everything else subscribes. That is what lets the whole night
flow be unit tested and lets `night.end flooded` work without reaching into the AI.

---

## Events

`EventBus` carries **cross-cutting** signals only — things a *different subsystem* needs
to know about. Tight chatter inside one subsystem uses plain C# events on the system
itself. The bus is not a dumping ground.

Signals are `readonly struct`, so publishing does not allocate. Handlers live in a flat
`Dictionary<Type, Delegate>` rather than in static generic fields, specifically because
a flat dictionary can be *cleared* — which matters when Domain Reload is disabled and
stale handlers from the previous Play session would otherwise fire into destroyed
objects.

---

## Determinism

Nothing in the AI touches `UnityEngine.Random`. Every roll comes from a seeded
`RandomSource` (xorshift32) created by `NightController` and forked per character:

```csharp
var rng = nightRng.Fork(i * 101 + 7);
```

Forking by index means retuning one character does not reshuffle everybody else's
rolls. `AIDirector` ticks the cast in list order rather than letting Unity choose. The
result is that `night.seed 12345; night.restart` reproduces a bug report frame for
frame, and `RandomSource.DrawCount` lets two supposedly identical runs be diffed.

---

## Generated content

Nothing binary is committed, and nothing binary is written into the scene file.

| What | Where | Why runtime |
|---|---|---|
| Cave geometry | `FacilityGeometrySpawner` → `FacilityBuilder` | Unity serialises any referenced non-asset mesh *into the scene*; a generated cavern would make it a multi-megabyte blob |
| Fixture meshes | `ProceduralMeshSpawner` | Same, at smaller scale |
| Doors, grate, floodlights, cameras | `SiteFixtureSpawner` | Their *positions* depend on the site, which is chosen at the menu |
| The cast | `CastSpawner` | Which rooms they start in is a property of the building |
| The render pipeline | `RenderPipelineBuilder` (editor) | Three URP tiers, every setting a decision this game has an opinion about |
| Character models | `AnimatronicModelSpawner` → `AnimatronicFactory` | Same, plus the model stays in step with the definition automatically |
| Surfaces | `TextureFactory` → `MaterialLibrary` | Overridden by `Resources/Art/<name>_Albedo` when present |
| Audio | `ProceduralAudio` → `AudioDirector` | Overridden by `Resources/Audio/<name>` when present |
| Post FX profile | `PostFxController` | A volume profile is YAML nobody can review and nobody can merge |
| UI | `UIFactory` | Prefabs are opaque; this interface is a fixed instrument panel |

The spawners offer an editor preview built under `HideFlags.DontSave`, so a designer can
look at the geometry without it ever reaching the scene file.

### Two shading decisions worth knowing about

**Vertex occlusion.** `MeshBuilder.BakeVertexOcclusion` writes a curvature-derived
occlusion term into the vertex alpha: for each vertex, which side of its tangent plane
its neighbours sit on, accumulated as bare dot products over a sum of edge lengths. The
quotient is a curvature in reciprocal metres rather than an angle per edge — and that
distinction is the whole correctness of it, because without it a twenty-metre chamber
and a forty-centimetre crevice tessellated with the same ring count come out identically
occluded. Screen-space occlusion covers the last few centimetres; this covers the
metre-scale gradient where a wall meets a floor, which is most of what makes generated
rock read as rock.

**Detail normals from derivatives.** Neither shader ships a normal map, because a normal
map is a binary asset. Instead the luminance of whatever the albedo sampler produced is
treated as a height field and its screen-space gradient projected onto the surface. It
is scale-correct for free, it works over triplanar seams exactly as well as the albedo
does, and it costs two derivatives.

---

## Animatronics are rigid, on purpose

`AnimatronicFactory` builds a bone hierarchy with rigid shell parts parented to it — not
a skinned mesh. An animatronic *is* a rigid mechanism: moulded shell over a steel frame
does not deform, it pivots. A hierarchy of rigid parts reproduces that exactly, costs
nothing to build and nothing to skin, and is the physically correct model rather than a
compromise.

`ServoAnimator` then drives it with **quantised** head tracking — the target angle is
rounded to discrete servo steps and arrives with a small overshoot that settles. That
single detail does more for the uncanniness than any amount of smooth interpolation,
and it is precisely what smooth interpolation destroys.

The animator knows nothing about nodes or states. The controller hands it three
numbers — speed, agitation, and whether the lamps are lit — which is what keeps
`Grotto.Procedural` free of any dependency on `Grotto.AI`.
