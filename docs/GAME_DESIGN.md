# Design notes

## The premise

Every defence is also an invitation.

That is the whole game. It is not a difficulty curve made of bigger numbers; it is a
set of resources whose *correct* use creates the next threat. A player who understands
the systems is not safe, they are merely choosing which danger to accept for the next
ninety seconds.

---

## Three axes

### Power — attrition you can see coming

An 8kW diesel genset with a day tank, a load-dependent burn rate and a limited number
of jerry cans. Not a percentage bar, because a percentage bar has one failure mode and
this has four:

- **Fuel exhaustion** — slow, visible, and your own fault
- **Overload trip** — fast, and it *releases both blast doors*
- **Battery exhaustion** after a trip — the countdown inside the countdown
- **Total blackout** — the cap lamp is all that is left

The breaker is the interesting one. Exceeding the continuous rating starts a four
second timer with a visible amber bar. Resetting it costs four and a half seconds of
holding a lever with the monitor down. So "just close both doors and run the pump" is a
plan that works right up until it catastrophically does not, which is the point.

Refuelling and cranking take the player's hands: the monitor drops, and cranking is the
loudest thing in the facility.

### Air — the instrument that lies

Bad air does not kill quickly. It makes you *see things*.

Below 42% quality, hallucination pressure starts climbing. Cotton begins producing
apparitions in camera feeds, corrupting cameras (a real six second reboot cost), and
faking geophone contacts. Below 8% you are actively suffocating, with 26 seconds to fix
it.

This exists because of a problem most horror games have: once a player learns the
rules, the cameras become a spreadsheet and stop being frightening. Here the instrument
degrades. The fix — the fan — is the loudest thing in the building after the starter
motor.

### Water — the dial with two edges

The spine. The only resource where **both directions are dangerous**:

```
0.00 ─────────── 0.35 ────── 0.50 ── 0.55 ─────────────── 1.00
     Marlow digs      │  Marlow in   │      Echo swims      │
     through the      │  the basin   │      the channel     │
     sump floor       │  but stuck   │                      │
                      │              │              CONTROL ROOM
                 DIG line                              SUMP FLOODS
                                  SWIM line
```

Inflow climbs toward dawn — 2.4× by 6 AM — so a setting that held at 1 AM will not hold
at 5. There is a narrow band between 0.50 and 0.55 where neither route exists, and
holding it is a full-time job that costs power and makes noise.

The band between 0.35 and 0.50 is where Marlow sits in the basin on CAM 03, visible and
unable to act. That is the teaching moment: the dial has a *shape*, not a switch.

Running the pump with the basin already empty cavitates it, permanently reducing its
efficiency for the rest of the night. Panic has a price.

---

## The cast

Five characters, five different gates. Adding a sixth would be one behaviour subclass.

| Character | Gate | The trap |
|---|---|---|
| **Barty** | A shut blast door | He is drawn to noise, and the door is noise |
| **Vesper** | Light, in a corridor with no door | She is *faster when quiet*, so the fan that defends against her invites Barty |
| **Marlow** | A dry sump | He is the price of running the pump |
| **Echo** | A deep channel | She is the price of not running it; the grate is odds, not a wall |
| **The Chorus** | Nothing | Only a completely dark, silent station loses it — which costs air and water level |

### Barty

The one who plays fair, and the one who teaches the lesson. He walks public routes,
respects doors absolutely, and moves toward noise. He commits to one adit for 25–55
seconds rather than re-deciding every hop, so a player who checks CAM 01 and sees him
gone can reasonably conclude he is coming round the south. **Reading him is supposed to
be possible.**

### Vesper

The karst crawlways have no cameras at all — the geophones are the only warning. Doors
are irrelevant. Light is the only answer, and light is no use against anything else.

Her signature inversion is noise: she navigates by ear, so ambient noise *confuses*
her. A silent facility roughly doubles her rate; one running the fan and the pump barely
lets her move. Turned back three times she drops out of the ceiling into the Midway and
starts the climb again, so holding the light is a genuine reprieve rather than a stall.

### Marlow and Echo

A matched pair, and the reason water is a dial. Marlow tunnels through silt, which must
be dry. Echo swims a channel, which must be deep. Their gates are constructed to never
overlap — `NavigationTests.Water_HasNoSettingThatShutsOutBothOfThem` asserts it at
every level in 0.05 steps.

Echo treats the grate as a probability rather than a barrier, and a full basin floats
the grate off its seat — so the condition that lets her reach the station is the one
that weakens the thing meant to stop her.

### The Chorus

Night 5 onward. It does not get past a blast door; it leans on one until the door stops
being a door, at 13 units of pressure per second against a capacity of 100. A buckled
door is gone for the rest of the night.

It tracks running machinery, so the only counter is to stop *everything* — fan, pump,
monitor, lights, the generator itself — for six seconds. Doing that costs air quality
and lets the water rise, and is meant to be the worst few minutes in the game.

### Cotton

Not an animatronic. There never was one; Cotton was the walk-around mascot. What comes
out of bad air wearing her head degrades your instruments and cannot kill you.

---

## The survey — a reason to look

Every system above makes the monitor a **cost**. It draws power. It makes noise. It
parks your head so you cannot see the doors. A player who works that out and plays
optimally ends up staring at a blank wall with the monitor down, which is correct play
and the least interesting version of the game. A surveillance horror game in which the
correct move is not to use the surveillance has a hole in the middle of it.

So the reclamation survey wants condition readings, and the office releases your fuel
allowance in stages as you file them. Hold a camera on the room it asks for for four
and a half seconds and fourteen litres appear in the day tank.

Three choices make it work:

- **The target is never one of your four approaches.** Filing always means several
  seconds looking somewhere that cannot hurt you, while something you cannot see closes
  on somewhere that can. A reading you could file while already watching the north door
  would not be a decision.
- **Progress decays rather than resets** when you glance away. Resetting would punish
  exactly the door-checking the rest of the game spends five nights teaching.
- **A snowy feed files more slowly**, which quietly makes camera maintenance worth doing
  rather than something you only notice when a feed dies completely.

Targets rotate on the hour whether or not you filed the last one, so a missed reading is
a missed payment rather than a checklist you fail. The survey is optional pressure.

## Night events — the shape of a night

Without them a night is a monotone: five systems drifting at fixed rates for six hours,
with the only variation coming from where the cast happens to be. A player settles into
a loop within about ninety seconds and executes it until dawn. That is a system, not a
night.

An event breaks the loop by making one resource briefly, sharply wrong:

| Event | What it does | For |
|---|---|---|
| **Inflow surge** | Water rises 2.4× as fast | 55–80s |
| **Brownout** | The set's available rating drops to 62% | 38–55s |
| **Duct fault** | Air decays 2.2× as fast | 45–70s |
| **Multiplexer fault** | Every camera degrades 3.5× as fast | 50–75s |
| **Tremor** | A real noise burst from the deep gallery | an instant |

A brownout cuts the **rating**, not by adding phantom load. The player sees the same
kilowatts on the board and the headroom shrink underneath them, which is what a
labouring genset actually does and is far more legible than a load appearing from
nowhere.

Each is announced six seconds before it lands, because **an unannounced event is a tax
and an announced one is a decision**. Six seconds is enough to shut a door, drop the
fan or pour a can, and not enough to do all three.

Scheduling is deterministic from the night's seed, one per hour from the second onward,
scaled by the night number — so night one gets two disturbances and night six gets five,
and a seeded replay runs the same night.

## Difficulty — a separate axis from the campaign

Three presets, none of which touch the AI:

| Preset | Water | Air | Fuel | Events |
|---|---|---|---|---|
| **Survey** | ×0.70 | ×0.70 | ×0.75 | ×0.50 |
| **Standard** | ×1.00 | ×1.00 | ×1.00 | ×1.00 |
| **Reclamation** | ×1.35 | ×1.30 | ×1.25 | ×1.40 |

The night number decides who is awake and how aggressive they are. This decides how
much slack the *resources* give you, which is the part players most often want to adjust
without also giving up the campaign's pacing. Keeping them separate means a player can
see night five's cast on a night five's fuel budget they can actually survive.

The three multipliers — night, site and preset — are deliberately kept separate in
`FacilityRuntime` rather than folded into one number, because collapsing them makes any
one of them impossible to tune without disturbing the other two.

## Pacing

`AIDirector` holds the whole cast back for the first 12% of a night at half pressure,
then ramps to 1.35× by 6 AM. A night that opens at full aggression is not frightening,
it is just short.

Attacks are serialised through a single claim, and before the 80% mark a character
already at a threshold suppresses everyone else to 55%. Five characters independently
deciding to attack at once is not five times as scary; it is unreadable, and it feels
unfair because it *is* unfair. After 80%, the suppression lifts. Six AM should be chaos.

## The campaign

| Night | Cast | Fuel | Cans | Water | Air | Teaches |
|---|---|---|---|---|---|---|
| 1 | Barty 3 | 145 L | 2 | ×0.85 | ×0.85 | Doors cost power |
| 2 | + Vesper 4 | 135 L | 2 | ×0.95 | ×1.0 | A route with no door |
| 3 | + Marlow 5, Echo 4 | 125 L | 2 | ×1.1 | ×1.1 | The water dial, both ends live |
| 4 | 10/9/8/8 | 110 L | **1** | ×1.3 | ×1.2 | Fuel is finite |
| 5 | + Chorus 4 | 105 L | 1 | ×1.45 | ×1.3 | Sometimes you go dark |
| 6 | 16/15/14/14/10 | 95 L | 1 | ×1.6 | ×1.45 | — |
| Custom | you choose | 120 L | 2 | ×1.0 | ×1.0 | — |

Nights 3 onward add hourly AI-level ramps, so the second half of a night is not the
first half again.

The same six nights run at any of the three sites, and the site's own multipliers stack
on top — so night three at Hollowmere is night three's cast against 1.55× water, and
night three at Sablefield is night three's cast against 1.85× air. Night progression is
shared: clearing night three anywhere opens night four everywhere. The sites are
alternative *places* to play a night, not three campaigns to grind in parallel.

---

## Accessibility

Three settings, none of which touches the simulation:

- **Photosensitive mode** caps flashing, strobing, jumpscare shake and post-FX contrast
  to roughly a third.
- **Reduced jumpscare audio** drops the sting to 35%.
- **Subtitles** for the phone briefings.

The **difficulty preset** is a separate control and is *not* an accessibility setting:
it changes the outcome, and it says so.

They are accessibility settings, not difficulty settings. The AI, the resources and the
outcome are identical with them on.

---

## What is deliberately absent

- **No walking.** Every verb is a switch on the desk. That is what makes the resource
  layer the entire game rather than a system wrapped around exploration.
- **No jumpscare video files.** The scare is a camera move onto the attacking
  character's real rigged head, so it frames differently every time.
- **No combat, no inventory, no crafting.** The tension is scheduling.
