# Grotto Springs Family Fun Caverns

> Opened July 1979 in the Marrow Hollow limestone system: a show cavern, a mineral
> spring terrace and an arcade, ninety feet under a hillside. Closed in 1993 when the
> spring took the lower gallery back. The cast was never recovered — the insurers
> called it cheaper to leave them.

The map is authored in code, in
[`GrottoSpringsLayout.cs`](../Assets/Game/Scripts/Facility/GrottoSpringsLayout.cs), so
every threshold sits next to the note explaining why it has that value.
*Tools → Grotto → Rebuild Settings Assets* bakes it into a ScriptableObject a designer
can then tweak in the inspector.

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

## Changing the map

1. Edit `GrottoSpringsLayout.Populate`.
2. *Tools → Grotto → Rebuild Settings Assets*.
3. *Tools → Grotto → Build Facility Scene*.

Geometry, the navigation graph, the camera placements and the monitor's plan all follow
automatically, because all four read the same asset. `map.validate` in the console and
*Tools → Grotto → Validate Project* will report orphaned nodes, impossible links and
attack nodes with no route to the station.
