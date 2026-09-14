# Developer tools

The goal: **any state the game can reach on its own, you can reach in one line.**

Debugging a horror game is otherwise miserable. The interesting failures happen at 5 AM
on night five with the water at a particular level and two characters in specific
rooms, and playing to that point takes six minutes per attempt.

```
night.seed 12345      # every subsequent night uses this seed
night.start 5         # night five
night.hour 5          # jump the clock, firing every intervening hour
env.water 0.52        # park the water in the narrow safe band
ai.move echo SUMP     # put Echo at the threshold
```

Four lines, and it reproduces exactly. `RandomSource.DrawCount` even lets two
supposedly identical runs be diffed.

---

## Availability

| Context | Console | Overlay |
|---|---|---|
| Editor | yes | yes |
| Development build | yes | yes |
| Release build | only with `-grotto-devtools` | only with `-grotto-devtools` |

```
TheAwakening.exe -grotto-devtools -grotto-seed 12345 -grotto-night 4
```

So QA can reproduce a report on a real build without shipping the console to players.

Both are drawn with IMGUI rather than uGUI, deliberately. A debug tool has to work when
the thing it is debugging is broken — including when the Canvas, the event system or the
whole UI layer has fallen over. IMGUI needs none of them. It is the wrong choice for a
shipping interface and the right one for this.

---

## The console — `` ` ``

Tab completes command names, up/down walks history, and an unrecognised command
suggests the nearest match by edit distance. `help` lists everything by category;
`help ai` narrows it.

Commands are declared by attribute next to the code they operate on rather than in one
central switch — a debug command that lives beside its system gets updated when the
system changes, and a central registry of eighty commands does not:

```csharp
[DevCommand("env.water", Category = "env",
    Help = "Sets the water level, 0 to 1.", Usage = "env.water <0-1>")]
private static string EnvWater(CommandArgs args) { ... }
```

Boolean arguments accept `on/off`, `true/false`, `1/0`, `yes/no` — and **with no
argument they toggle**, because that is what anyone typing `ai.freeze` actually wants.

### night

| Command | Effect |
|---|---|
| `night.start <1-7> [seed]` | Start a night, optionally seeded |
| `night.restart` | Restart with the same seed |
| `night.hour <0-6>` | Jump the clock, firing every intervening hour change |
| `night.time <scale>` | Clock multiplier. `night.time 10` runs a night in 36 seconds |
| `night.seed [value]` | Force the seed for all subsequent nights; no argument clears |
| `night.end [outcome]` | End as survived / killed / flooded / suffocated / aborted |
| `night.info` | Night, phase, clock, seed, draw count, environment scales |
| `night.event <kind>` | Force a disturbance: `surge`, `brownout`, `vent`, `tremor`, `feed`, or `none` to clear |
| `night.events` | What is running or inbound, and how long is left of it |

### survey

| Command | Effect |
|---|---|
| `survey.info` | Readings filed, current target, dwell progress, litres released |
| `survey.target <node>` | Point the survey at a room |
| `survey.file` | File the current reading immediately |

Between them these make the survey testable without sitting through a four-and-a-half
second dwell per reading: `survey.target GRAND` then `survey.file` puts fourteen litres
in the tank and moves it on.

### ai

| Command | Effect |
|---|---|
| `ai.list` | Every character: level, state, node, next roll, **current odds**, behaviour note |
| `ai.level <id> <0-20>` | One character's AI level |
| `ai.levels <0-20>` | Everybody at once |
| `ai.move <id> <node>` | Teleport |
| `ai.state <id> <state>` | Force a state machine transition |
| `ai.freeze [on\|off]` | Stop the cast moving and attacking |
| `ai.attack <id>` | Put a character at a threshold and let it strike |

### power

| Command | Effect |
|---|---|
| `power.info` | State, generator, load **broken down per consumer**, breaker, battery |
| `power.fuel <litres>` / `power.cans <n>` | Set the tank and the spares |
| `power.infinite [on\|off]` | Fuel and battery never deplete |
| `power.trip` / `power.kill` | Open the breaker / stop the engine |
| `power.restore` | Refill, restart and begin a breaker reset |

### env

| Command | Effect |
|---|---|
| `env.info` | Air, water, **both gate states in words**, noise, pump condition |
| `env.air <0-1>` / `env.water <0-1>` | Set them directly |
| `env.fan <off\|low\|purge>` / `env.pump [on\|off]` | Drive the machinery |
| `env.freeze [on\|off]` | Stop air decaying and water rising |
| `env.noise <node> [0-1]` | Emit a noise, to watch the AI react |

`env.water` reports the consequence rather than just the number:

```
> env.water 0.4
Water at 0.400. Marlow cannot dig, Echo cannot swim.
```

### facility

| Command | Effect |
|---|---|
| `cam.list` | Every camera with condition and online state |
| `cam.select <node>` / `cam.monitor [on\|off]` | Drive the monitor |
| `cam.repair` / `cam.always [on\|off]` | Restore condition / force everything online |
| `door <n\|s> <open\|close\|buckle\|repair>` | Including buckling one, to test the Chorus outcome |
| `grate [on\|off]` | Reports the swimmer pass chance too |

### map

| Command | Effect |
|---|---|
| `map.nodes` | Every node: kind, zone, camera, hops to station, coupling, live noise |
| `map.links [node]` | Every link with traversal mask, times and gates |
| `map.path <from> <to> [mask]` | **Route as the AI would find it, at the current water level** |
| `map.validate` | Orphaned nodes, impossible links, bad thresholds |

`map.path` is the one that answers "why is Echo just standing there":

```
> map.path DEEP STATION swim
No swim route from DEEP to STATION at the current water level (0.31).
```

### fx and global

| Command | Effect |
|---|---|
| `fx.scare [0-1]` / `fx.jumpscare <id>` | Trigger presentation without ending the night |
| `fx.hallucinate [kind]` | Force one of Cotton's four effects |
| `fx.nojumpscares [on\|off]` | Suppress the presentation entirely |
| `god [on\|off]` | Attacks log a near-miss instead of ending the night |
| `show <overlay\|graph\|noise\|paths\|audio> [on\|off]` | Visualisations |
| `show.as <mask>` | Which capability the graph gizmo colours for |
| `flags` / `reset` | Print every override / clear them all |
| `stats` | FPS, frame time, managed heap, GC counts, resolution |
| `teleport [node]` | Detach the camera and fly |
| `log.channels [...]` / `log.verbose [...]` / `log.tail [n]` | Channel-filtered logging |

---

## The overlay — `F3`

```
PERF  142 fps   mean 7.0 ms   worst 18.3 ms   heap 214 MB

NIGHT 5  Active  4 AM  67%  seed 12345  x1

POWER Online   5.40 kW (68%)   fuel 41.2 L   cans 1   batt 100%

AIR   38%  fan Low  halluc 0.12
WATER 0.512  RISING +0.084/h  pump off
      gates: sump wet   channel shallow
NOISE loudest GEN 0.44   total 2.1

CAST
  barty    L13 Threshold ADIT_N                strike 62%
           approach ADIT_N for 31s
  vesper   L12 Stalk     CRAWL_B→CHASE 40%     roll 3.1s @60%
           rebuffs 1/3
  echo     L11 Stranded  RIVER                 roll 1.2s @0%
           beached (0.51 < 0.55)
  cotton   pressure 0.12

OVERRIDES seed:12345
```

Two details that matter:

**Worst frame, not just the mean.** A 7 ms average with an 18 ms spike is a stutter the
player felt and the average hid.

**The gate line.** `gates: sump wet / channel shallow` is the single most useful row on
the panel, because it says in words which of the two threats is currently live.

---

## Jumpscare test picker — `J`

Jumpscares are the one thing in the game that is genuinely hard to iterate on:
reaching one honestly means surviving to the point where a specific character
breaches, which takes most of a night and cannot be aimed at a chosen character.

`J` opens a picker; `←` and `→` choose; `Enter` fires that character's jumpscare
immediately. While the picker is open the character is moved in front of the camera
first, so a dormant one standing across the cave still frames properly.

It fires the **presentation only** — no `AttackSignal` — so the night carries on and
you can fire the next one straight away. `fx.jumpscare <id>` does the same thing from
the console.

Turn it off with `enableJumpscareTester` on the `OverlayController` component, or
rebind it with `testerToggleKey`.

## Scene view

Runtime drawing uses `Debug.DrawLine`, so it appears in the Scene view while the game
runs.

| Toggle | Draws |
|---|---|
| `show graph` | Every link, coloured by whether it is passable **right now** for the capability set by `show.as` |
| `show noise` | A vertical bar per node whose height is the current noise level |
| `show paths` | Each character's live route to the station, in its map colour |

Link colours: **green** passable, **amber** water-gated shut, **red** blocked by a
barrier, **grey** not this capability's route at all.

Out of play mode, `AiDebugGizmos` draws the layout from the asset with node labels and
size boxes, so the map can be inspected without entering Play.

---

## Editor menu

| Item | Does |
|---|---|
| **Build Facility Scene** | Rebuilds settings, then constructs the whole playable scene |
| **Rebuild Settings Assets** | Regenerates config, layout, tuning, nights and the cast from code |
| **Asset Fetcher** | Downloads curated CC0 packs |
| **Validate Project** | Layers, tags, render pipeline, shaders, layout, cast reachability, build settings |
| **Build → …** | Player builds; refuses when validation reports a blocking problem |

`ProjectValidator` catches what fails silently: a missing layer makes every generated
object land on Default, an unassigned render pipeline makes every custom shader fall
back, and an attack node with no link to the station makes a character walk there and
give up, forever.

The `AnimatronicDefinition` inspector answers balancing questions by reading rather than
playtesting — reachability across the whole water range, and exactly what stands in the
way on the final step:

```
ADIT_N -> station: DOOR_N
SUMP   -> station: GRATE, needs water <= 0.35
CHASE  -> station: light
```

---

## Adding a command

```csharp
[DevCommand("env.silt", Category = "env",
    Help = "Sets silt depth.", Usage = "env.silt <0-1>")]
private static string EnvSilt(CommandArgs args)
{
    Facility.Water.DebugSetSilt(args.Float(0));
    return $"Silt at {Facility.Water.Silt01:0.00}.";
}
```

Any `static` method taking one `CommandArgs` and returning `string` or `void`, in any
`Grotto.*` assembly. The registry finds it by reflection at startup — scanning only
`Grotto.*` assemblies, because reflecting over the whole domain including UnityEngine
and mscorlib costs hundreds of milliseconds for nothing.
