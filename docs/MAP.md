# The sites

Three buildings, the same four ways in, the same five characters. What changes between
them is where the water sits, how fast it moves, and which of your defences it is
currently taking away.

| Site | Id | Unlocks | Palette | Emphasis |
|---|---|---|---|---|
| [Grotto Springs Family Fun Caverns](#grotto-springs-family-fun-caverns) | `grotto` | always | Limestone | Balanced |
| [Hollowmere Hydro Station](#hollowmere-hydro-station) | `hollowmere` | 2 nights cleared | Concrete | Water, relentless |
| [Sablefield Grain Terminal](#sablefield-grain-terminal) | `sablefield` | 4 nights cleared | Steel | Air, inverted water |

All three are authored in code under
[`Assets/Game/Scripts/Facility/Sites/`](../Assets/Game/Scripts/Facility/Sites/), through
the verbs in `LayoutAuthoring` — so every threshold sits next to the note explaining why
it has that value, and a map change is reviewable as a list of moved nodes rather than a
wall of changed YAML guids. `SiteCatalog` is the one place that knows which sites exist.

`python3 Tools/check_layouts.py` parses all three straight from the C# and checks node
ids, reachability at every water level, cast placement, approach coverage and gate
ordering. It runs in CI.

---

# Grotto Springs Family Fun Caverns

> Opened July 1979 in the Marrow Hollow limestone system: a show cavern, a mineral
> spring terrace and an arcade, ninety feet under a hillside. Closed in 1993 when the
> spring took the lower gallery back. The cast was never recovered — the insurers
> called it cheaper to leave them.

[`Sites/GrottoSpringsLayout.cs`](../Assets/Game/Scripts/Facility/Sites/GrottoSpringsLayout.cs).
*Tools → Grotto → Rebuild Settings Assets* bakes it into a ScriptableObject a designer
can then tweak in the inspector.

**Starts at 45% water. Water ×1.00, air ×1.00, fuel ×1.00.** The reference site: there
is a narrow band between the dig mark and the swim mark that shuts out both threats, and
holding it while everything else drifts is the game.

---

## The shape of it

```
                                    ▲ surface (welded shut)
                                    │
                              ┌───────────┐
                              │  INCLINE  │  CAM 08
                              └─────┬─────┘
                                    │
                              ┌───────────┐
              ┌───────────────┤   LOBBY   ├───────────────┐
              │               └─────┬─────┘               │
        ┌───────────┐               │               ┌───────────┐
        │   GIFT    │               │               │  LOCKER   │
        │  CAM 09   │               │               │  CAM 06   │
        └─────┬─────┘               │               └─────┬─────┘
              │               ┌───────────┐               │
   ┌──────────┴──────────┐    │  ADIT_N   │         ┌───────────┐
   │       MIDWAY        │    │  CAM 01   │         │ WORKSHOP  │
   │       CAM 10        │    └─────┬─────┘         │  CAM 05   │
   └────┬───────────┬────┘          │               └─────┬─────┘
        │           ▲          ══════╪══════ DOOR_N             │
   ┌─────────┐      │(one-way   ╔═══════════╗                   │
   │  DINE   │      │  drop)    ║  STATION  ║ ◄── you are here   │
   │ CAM 11  │      │           ╚═══╤═══╤═══╝                   │
   └────┬────┘  ┌───────┐           │   │                       │
        │       │CRAWL_A│      light│   │══════ DOOR_S          │
   ┌─────────┐  └───┬───┘       ┌───┴───┐   │              ┌───────────┐
   │  GRAND  │      │           │ CHASE │   │              │    GEN    │
   │ CAM 12  │  ┌───┴───┐       └───┬───┘   │              │  CAM 04   │
   └──┬───┬──┘  │CRAWL_B├───────────┘   ┌───┴───┐          └─────┬─────┘
      │   │     └───┬───┘               │ADIT_S │◄───────────────┘
 ┌────────┐         │                   │CAM 02 │                │
 │ STAGE  │    ┌────┴────┐              └───┬───┘          ╔═════╧═════╗
 │ CAM 13 │    │ CHIMNEY │                  │              ║   SUMP    ║ CAM 03
 └───┬────┘    └────┬────┘                  │              ║  (grate)  ║
     │              │                  ┌─────────┐         ╚═════╤═════╝
     │              └── GRAND (climb)  │  XING   │ CAM 14        │
     │                                 └────┬────┘          swim │ burrow
     │                                      │                    │
     │                                 ┌─────────┐               │
     └───── (walk, dry only) ──────────│  RIVER  │ CAM 15 ───────┘
                    │                  └────┬────┘
              ┌───────────┐                 │
              │   DEEP    │◄────────────────┘
              │ NO CAMERA │   swim only
              └───────────┘
```

Not to scale. The Deep Gallery is the lowest point at −9 m; the Incline head is the
highest at +5 m.

---

## The four approaches

The whole defensive design. Each is answered by a **different** resource, which is why
no single strategy survives a night.

| Approach | From | Answer | Costs |
|---|---|---|---|
| **North adit** | `ADIT_N` | `DOOR_N` blast door | 1.5 kW held, 2.2 kW moving, and it is loud |
| **South adit** | `ADIT_S` | `DOOR_S` blast door | the same again — both shut is most of your rating |
| **Sump grate** | `SUMP` | `GRATE` bolts, *or* flooding the basin | 0.7 kW, and the bolts are only odds against a swimmer |
| **Cable chase** | `CHASE` | the chase floodlight — there is no door | 0.55 kW, and light is no use against anything else |

A breaker trip drops the hold magnets and **both doors release.**

---

## Nodes

| Id | Name | Kind | Zone | Cam | Coupling | Notes |
|---|---|---|---|---|---|---|
| `STATION` | Pump House Control Room | Station | Station | — | 1.00 | You. Four ways in. |
| `ADIT_N` | North Adit | Adit | Station | 01 | 0.85 | Shotcrete, 1981 |
| `ADIT_S` | South Adit | Adit | Station | 02 | 0.85 | Service run to the genset |
| `SUMP` | Sump Basin | Sump | Station | 03 | 0.95 | Directly below the floor |
| `CHASE` | Cable Chase | Crawlway | Station | — | 0.90 | **No camera. No door.** |
| `GEN` | Generator Bay | Room | Service | 04 | 0.70 | 8kW Lister, day tank |
| `WORKSHOP` | Maintenance Workshop | Room | Service | 05 | 0.50 | Three cradles, two empty |
| `LOCKER` | Staff Lockers | Room | Service | 06 | 0.40 | Rota still pinned |
| `LOBBY` | Ticket Grotto | Cavern | Upper | 07 | 0.30 | Turnstiles chained |
| `INCLINE` | Incline Railway | Shaft | Upper | 08 | 0.15 | Surface door welded |
| `GIFT` | Gift Grotto | Room | Upper | 09 | 0.25 | Nothing has been taken |
| `MIDWAY` | The Midway | Cavern | Show | 10 | 0.22 | Two cabinets still draw power |
| `DINE` | Mineral Springs Terrace | Cavern | Show | 11 | 0.18 | Travertine pools, 94°F |
| `GRAND` | The Grand Gallery | Cavern | Show | 12 | 0.20 | Seats four hundred |
| `STAGE` | Songbird Stage | Cavern | Show | 13 | 0.16 | Four marks on the boards |
| `CHIMNEY` | Bell Chimney | Shaft | Show | — | 0.30 | Vertical, to the crawlways |
| `CRAWL_A` | Karst Crawlway A | Crawlway | Deep | — | 0.45 | **No camera** |
| `CRAWL_B` | Karst Crawlway B | Crawlway | Deep | — | 0.60 | **No camera** |
| `XING` | Crossing Bridge | Adit | Deep | 14 | 0.35 | Timber deck over the channel |
| `RIVER` | The Styx Channel | Watercourse | Deep | 15 | 0.50 | Depth is whatever you leave it |
| `DEEP` | The Deep Gallery | Cavern | Deep | — | 0.55 | **No camera, high coupling** |

**Coupling** is how much of a sound made in that node reaches the station's geophones
through solid rock. The Deep Gallery is the furthest node from the station and couples
*better* than the Incline, which is close. That is not an error — it is why the
geophone array exists, and why the four camera-less nodes are the ones with the highest
coupling values. You can hear the places you cannot see.

---

## Water gates

Three constants, defined once in `GrottoSpringsLayout` so the layout and the systems
cannot disagree:

| Constant | Value | Meaning |
|---|---|---|
| `SumpDiggable` | 0.35 | At or below, Marlow can tunnel **through the station floor** |
| `SumpWadeable` | 0.50 | At or below, he can enter and leave the basin, but not break through |
| `ChannelSwimmable` | 0.55 | At or above, the Styx Channel is deep enough to swim |
| `CrossingDrowned` | 0.75 | Above, the timber deck can no longer be walked |

The band between 0.50 and 0.55 is the only setting where neither Marlow nor Echo has a
route — and inflow climbs toward dawn, so holding it is a full-time job that costs
power and noise. `NavigationTests.Water_HasNoSettingThatShutsOutBothOfThem` asserts
that the bands never overlap.

Between 0.35 and 0.50 Marlow can sit in the basin, visible on CAM 03, unable to act.
That is deliberate: it is how the player learns the dial has a *shape* rather than a
switch.

---

## Links

| From ↔ To | Traversal | Time | Gate |
|---|---|---|---|
| `STATION` `ADIT_N` | Walk, Crawl | 3.0s | `DOOR_N` |
| `STATION` `ADIT_S` | Walk, Crawl | 3.0s | `DOOR_S` |
| `STATION` `SUMP` | Burrow, Swim | 5.0s | `GRATE` |
| `STATION` `CHASE` | Crawl, Climb | 4.0s | **light deters** |
| `ADIT_S` `GEN` | Walk | 4.0s | — |
| `GEN` `WORKSHOP` | Walk | 5.0s | — |
| `WORKSHOP` `LOCKER` | Walk | 4.0s | — |
| `LOCKER` `LOBBY` | Walk | 5.0s | — |
| `WORKSHOP` `CRAWL_B` | Crawl, Climb | 6.0s | — |
| `ADIT_N` `LOBBY` | Walk | 5.0s | — |
| `LOBBY` `INCLINE` | Walk, Climb | 6.0s | — |
| `LOBBY` `GIFT` | Walk | 3.0s | — |
| `LOBBY` `MIDWAY` | Walk | 6.0s | — |
| `GIFT` `MIDWAY` | Walk | 4.0s | — |
| `MIDWAY` `DINE` | Walk | 6.0s | — |
| `MIDWAY` `GRAND` | Walk | 7.0s | — |
| `DINE` `GRAND` | Walk | 7.0s | — |
| `GRAND` `STAGE` | Walk | 4.0s | — |
| `GRAND` `CHIMNEY` | Climb | 6.0s | — |
| `CHIMNEY` `CRAWL_A` | Crawl, Climb | 5.0s | — |
| `CRAWL_A` `CRAWL_B` | Crawl | 6.0s | — |
| `CRAWL_B` `CHASE` | Crawl | 5.0s | — |
| `CRAWL_A` → `MIDWAY` | Crawl, Climb | 3.0s | **one-way drop** |
| `ADIT_S` `XING` | Walk | 5.0s | — |
| `XING` `RIVER` | Walk | 4.0s | water ≤ 0.75 |
| `XING` `RIVER` | Swim | 5.0s | water ≥ 0.55 |
| `RIVER` `DEEP` | Swim | 7.0s | water ≥ 0.55 |
| `RIVER` `SUMP` | Swim | 6.0s | water ≥ 0.50 |
| `SUMP` `GEN` | Burrow | 8.0s | water ≤ 0.50 |
| `DEEP` `STAGE` | Walk | 10.0s | water ≤ 0.75 |

Two links join `XING` and `RIVER`: one walked while the deck is clear, one swum once it
is not. Modelling it as two links rather than one conditional link keeps the rule in
the data instead of in a special case.

---

# Hollowmere Hydro Station

> Commissioned 1931 on the Hollowmere reservoir: two 900kW Francis sets in a hall cut
> into the dam's toe. A visitor gallery and an animatronic show were added in 1968 to
> sell the place as a day out. Generation stopped in 1989. The dam did not.

[`Sites/HollowmereLayout.cs`](../Assets/Game/Scripts/Facility/Sites/HollowmereLayout.cs).

**Starts at 66% water. Water ×1.55, air ×0.85, fuel ×1.15.**
Gates: dig ≤ 0.22, wade ≤ 0.34, swim ≥ 0.42, drowned > 0.80.

Grotto Springs asks you to hold the water somewhere in the middle. Hollowmere takes the
dial away. You are inside the dam: the level is not a choice you make once an hour, it
is a thing that rises on its own and that your pump only slows. Every night starts two
thirds flooded and gets worse.

What that does to the map:

- **The dry routes close.** Marlow's burrow under the generator floor and the walk out
  along the draft tube both shut the moment the level passes 0.34, and they do not
  reopen without serious pump power.
- **The wet routes open.** The tailrace, the penstock and the wheel pit stitch the far
  end of the building to the sump the instant the level clears 0.42. Echo lives there.
- **The top of the dam drowns at 0.80**, which takes the long way round off the table
  for everybody — including you, if you were counting on Barty needing eleven seconds
  to come back.

So the night is a slide from one threat model to another, and the interesting decision
is when to stop fighting it. Pump hard early and you buy a dry hour you have to survive
with Marlow in the floor; let it go and you trade him for Echo, who is worse, but who
at least only uses two doors.

**Structure.** The station is the switch room; the visitor gallery and the cable gallery
are the two doors; the draft tube is the sump; the ventilation shaft is the chase. West
is the turbine hall, the penstock, the intake tower and the forebay, climbing to the dam
crest. East is the fitting shop, the generator floor and the cable duct. South-east, and
underwater most of the night, are the tailrace and the wheel pit — the blind room the
geophones exist for.

| Character | Home | Retreat | Enters by |
|---|---|---|---|
| Barty | `TURBINE` | `TURBINE` | `GALLERY`, `CABLEWAY` |
| Vesper | `PENSTOCK` | `TURBINE` | `AIRSHAFT` |
| Marlow | `FITTING` | `GENFLOOR` | `DRAFTTUBE` |
| Echo | `WHEELPIT` | `TAILRACE` | `DRAFTTUBE` |
| The Chorus | `WHEELPIT` | `WHEELPIT` | `GALLERY`, `CABLEWAY`, `DRAFTTUBE` |

---

# Sablefield Grain Terminal

> Built 1954 beside the Sablefield branch line: a hundred-and-ten-foot slipformed
> elevator, twenty bins, and a leg that could lift four thousand bushels an hour. The
> annex became a walk-through attraction in 1977 to keep the co-op solvent. A dust
> explosion in 1986 took the headhouse roof off. The cast was inside it.

[`Sites/SablefieldLayout.cs`](../Assets/Game/Scripts/Facility/Sites/SablefieldLayout.cs).

**Starts at 18% water. Water ×0.75, air ×1.85, fuel ×0.95.**
Gates: dig ≤ 0.30, wade ≤ 0.45, swim ≥ 0.58, drowned > 0.88.

This is the site where the water dial is **inverted**.

The building sits high and dry. It starts at 18%, which at any other site would be an
emergency and here is simply Tuesday. Every dry route is open by default — Marlow's
burrow, the pit walk, the whole south loop — and the only way to close them is to let
the sump fill, which means running the pump backwards from the habit two maps have
built.

And letting it fill costs you twice:

- Above 0.55 the sump well joins the dust pit and the pit, which is Echo's entire road
  in. You closed Marlow's door by opening Echo's.
- Air decays at ×1.85. A grain elevator full of settled dust has to be ventilated hard
  and continuously, and the fan is the single biggest draw on the board. Time spent
  thinking about water is time the fan was off.

So Sablefield is about the air, and the water is a lever you pull exactly when you have
the power budget for the consequences.

**Structure.** The station is the weighbridge office; the conveyor tunnel and the
elevator boot pit are the two doors; the dust collection pit is the sump; the dust
trunking is the chase. The silos are the vertical spine — everything climbs, from the
Harvest Hollow annex up Silo A to the headhouse and out along the belt gallery. Silo B
has no camera and bottoms out in the pit, so anything taking that route disappears from
the monitor entirely.

| Character | Home | Retreat | Enters by |
|---|---|---|---|
| Barty | `STAGE` | `ANNEX` | `TUNNEL`, `BOOTPIT` |
| Vesper | `SILO_A` | `HEADHOUSE` | `TRUNKING` |
| Marlow | `DRIER` | `PLANT` | `DUSTPIT` |
| Echo | `PIT` | `SUMPWELL` | `DUSTPIT` |
| The Chorus | `PIT` | `PIT` | `TUNNEL`, `BOOTPIT`, `DUSTPIT` |

Marlow starts the night with his burrow *open*, which is new: at both other sites he has
to wait for the pump. Here he has to be flooded out. And Echo needs 0.58 on a night that
starts at 0.18, so he is a threat the player creates. That is the whole joke of the site.

---

## Changing a map

1. Edit the site's `Populate` method under `Assets/Game/Scripts/Facility/Sites/`.
2. `python3 Tools/check_layouts.py` — catches typo'd node ids, unreachable rooms,
   attack nodes that are not approaches, and gates in the wrong order.
3. *Tools → Grotto → Rebuild Settings Assets*.

Geometry, the navigation graph, the fixtures, the camera rig, the cast and the monitor's
plan all follow automatically, because all of them read the same layout at load. The
scene does not need rebuilding for a map change — it names no room at all.

## Adding a map

1. Write `Sites/YourSiteLayout.cs` with a `public static void Populate(FacilityLayout)`,
   using the verbs in `LayoutAuthoring`. Declare the station first: `StationApproaches`
   wires `nodes[0]`.
2. Add one line to `SiteCatalog.Entries`.

That is the whole job. `SiteTests` will then hold your new map to the same contract as
the other three.
