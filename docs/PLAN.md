# Battle Chess — Architecture & Build Plan

*Revised after the free-movement / trusted-host decisions.*

## Context

A turn-based tactical wargame with chess-like symmetric armies on a terrain battlefield, where units are regiments with stats, strength, morale and per-unit fields of view ("battleships" hidden information). Plays offline (hotseat + AI) and online.

### Two audiences, one engine

**The game**, eventually released on Steam: play battles out properly, with orders, fog and everything below.

**A battle referee** for friends running map games on Facebook, where outcomes are currently settled by whoever argues most persuasively. They set up two armies, resolve automatically, and get a report.

These are two modes of one product, not two products — auto-resolve needs the same combat, morale and AI the played-out game does. The important consequence is that **determinism stops being a testing convenience and becomes the fairness guarantee**: publish the setup and the seed, and any participant can re-run it and get byte-identical results without having to trust the moderator.

It also changes what the output is. For a map game "who won" is nearly useless — survivors carry into the next battle, so the deliverable is a casualty report and post-battle army state. That makes withdrawing early a legitimate strategic choice rather than a loss.

| Axis | Decision |
|---|---|
| Combat | Attrition on regiment strength + morale and rout |
| Turn model | Simultaneous orders (WEGO) — both sides commit, then resolve |
| Online | Async correspondence; **trusted host** now, dedicated server later |
| Board | **Continuous free movement** — real (x, y) position and facing, no grid governs it |
| Unit occupancy | Rectangle footprint (width × depth) at a continuous position/facing; oriented-rectangle collision |

### Framing note

This is a hex wargame wearing chess clothes. Keep from chess: symmetric armies, memorable unit identities, a commander whose loss matters, bounded match length. Discard: perfect information, piece trading as the core loop, and binary alive/captured units.

---

## The engine question, answered

**Use Unity, but make the engine nearly irrelevant.** A turn-based tactical game uses almost none of what Unity's presets provide — physics, animation rigs, NavMesh. What Unity genuinely gives you is the asset pipeline, UI Toolkit, tilemaps and cross-platform builds.

**All game rules live in a plain C# `netstandard2.1` library with zero `UnityEngine` references.** Unity, the host, the server, the tests, the AI and a headless CLI all consume that same library.

This matters more than any engine feature because it means offline and online run identical rules, the resolver can be relocated from host to server without a rewrite, and mechanics are testable in milliseconds outside the editor.

Note that Unity's NavMesh and PhysX are **unusable for gameplay** here regardless — gameplay must run headless in tests and on a server. Movement and collision are hand-written. The fine-grid choice is what keeps that cheap.

### Unity version

Installed `2022.1.4f1` is an end-of-life tech-stream build — do not build a multi-month project on it. Install **Unity 6 LTS** via Unity Hub (Hub isn't currently installed). API compatibility level: .NET Standard 2.1. **Unity work does not start until M7.**

### Toolchain status

- .NET 10 SDK `10.0.302` — installed ✅ (was .NET 5 SDK only, which is EOL)
- git, node, python — present

---

## Physics is continuous; grids are internal tools, not gameplay

Movement, facing and collision are **pure continuous physics** — no grid governs any of them. A unit has a real-valued `WorldPos` (x, y), a free-angle `Facing`, and a rectangular `Footprint` (width × depth). It moves by integrating `position += direction × speed(terrain underfoot) × dt` each tick, and collision is an oriented-rectangle overlap test between two footprints. None of this touches a grid, and none of it will need to change if formations later become multi-body shapes — `Footprint` is already a shape, not a point.

Two hex grids still exist, but strictly as internal calculation aids that never surface in gameplay, in the physics, or to the player:

| | Pathfinding grid | Vision grid |
|---|---|---|
| Purpose | A\* search space for route planning | Fog-of-war bookkeeping (Unknown / Remembered / Visible per cell) |
| Runs | Once per new order (or when blocked) | Every 5 ticks, for every unit with sight |
| Why a grid at all | Terrain-cost-aware routing without hand-building a navmesh pipeline (mesh generation + funnel algorithm) from scratch, headless, with no engine to lean on | Fog *state* is a standing memory that must persist and update; grid-cell booleans are exact and reproducible, where continuous polygon unions over time are a classic source of floating-point nondeterminism — which would break golden-replay testing |
| How its rawness is hidden | Raw A\* output is smoothed into a natural curve before being handed to the continuous movement system as waypoints | Raw line-of-sight is a geometric raycast against terrain occlusion; the grid only stores the *result* per region |

Two things this is deliberately **not** doing:
- **No broad-phase spatial index for collision.** Naive pairwise rectangle checks every tick are cheap at the unit counts in play (tens per side). Add a spatial hash only if profiling actually shows it's needed — premature optimization here would be unjustified complexity.
- **No relationship enforced between the two grids.** Earlier drafts of this plan worried about hex grids not tiling cleanly into each other. That concern is now moot: since neither grid holds a unit's authoritative position, they never need to agree with each other — each is just an independent resolution over the same underlying terrain data, converted to and from continuous world position as needed.

Terrain is authored once, at the pathfinding grid's (finer) resolution — this is naturally what the ASCII map format's per-character cells become — and the vision grid derives its coarser occlusion/elevation view by sampling that same data.

---

## Core architecture

### Authority abstraction — the trusted-host decision

You want a trusted host now and the option of a dedicated server later. That's a transport swap, not a rewrite, **provided the client never touches full state even when it is the host.**

```
IMatchAuthority
  PlayerView   GetView(PlayerId)
  void         SubmitOrders(PlayerId, OrderSet)
  TurnReplay   GetReplay(PlayerId, int turn)

  LocalMatchAuthority   -> holds MatchState in-process (offline, hotseat, host)
  RemoteMatchAuthority  -> HTTP client to a dedicated server (later)
```

The Unity client codes against `IMatchAuthority` and nothing else. Enforce it **mechanically with assembly separation**, not discipline:

- `BattleChess.Contracts` — `PlayerView`, `Order`, `OrderSet`, `GameEvent`, `TurnReplay`, content defs. Public.
- `BattleChess.Rules` — `MatchState`, `TurnResolver`, vision, combat. References Contracts.
- Unity view asmdef references **Contracts only**, plus a factory for local mode.

The client literally cannot compile a reference to `MatchState`. Flipping to a dedicated server later means writing `RemoteMatchAuthority` and hosting `BattleChess.Rules` — the client is untouched.

Honest limitation: with a trusted host, the hosting player *can* see through fog by inspecting their own process. That's acceptable among friends and is exactly what the dedicated-server path fixes later.

### Command → resolve → event

```
PlayerView (fogged) -> player issues OrderSet -> authority validates
  -> TurnResolver runs ticks over full MatchState
  -> emits TurnReplay (ordered GameEvents with tick stamps)
  -> projects a per-player fogged replay + new PlayerView
```

`PlayerView` and `TurnReplay` are the only types that ever reach a client. One place to audit, one place to write leak tests.

### Determinism — corrected

My earlier "no floats anywhere" rule was stricter than needed. Bit-exact cross-platform determinism is required for **lockstep** netcode, where every client recomputes the simulation. Here only the authority resolves; clients receive an event log and animate it. So:

- **Floats are permitted** in the rules layer. The requirement is that the authority reproduces its own results, which same-machine float math does.
- Still required: a single seeded PRNG consumed in a strictly defined order; units processed in ascending id order, always; **never iterate a `Dictionary`/`HashSet` in rules logic**; no LINQ whose result depends on hash order.
- Golden tests must run on a consistent platform. If host and server platforms ever diverge, that's when to revisit fixed-point — not before.

### WEGO resolution: tick loop

A turn is **60 ticks** (read as ~60 seconds of battle time). Per tick, each unit moving under a standing order advances continuously toward the next waypoint of its (already-smoothed) path, at its speed stat modulated by the terrain currently underfoot — cavalry genuinely outpaces infantry within the turn, which is what makes simultaneous turns feel alive. This replaced an earlier grid-stepping model; only the movement math changed, the tick cadence below did not.

Calibration (a 1 km reference map, adjustable per scenario): cavalry ≈ 4.76 m/s (~17 km/h, crossing in ~3.5 turns) and infantry at a third of that, ≈ 1.59 m/s (~5.7 km/h, crossing in ~10–11 turns) — both plausible battlefield paces, not just convenient numbers.

- Movement + collision: every tick
- Combat + morale pulses: every 10 ticks (6 pulses per turn)
- Vision recompute: every 5 ticks

Contention: two units converging stop at contact distance (footprints near-touching) and engage. Deterministic tie-break on (initiative, unit id). **Zone of control** — coming within ZOC range of a known enemy halts the advance, which creates real front lines instead of units sprinting past each other.

### Turn model under reconsideration: reactive playback *(parked, to be detailed)*

An alternative to pure simultaneous turns, raised late and worth taking seriously.

Each player takes a turn, but the **playback of their movement is not merely a replay** — the opposing player can act during it. Movement being watched becomes an interactive window rather than a cutscene, so a flank march can be answered *while it is happening* rather than only after the fact.

This is a genuinely different model from WEGO, not a variation on it:

| | Simultaneous (current plan) | Reactive playback |
|---|---|---|
| Orders | Both sides commit blind, then resolve | One side acts; the other reacts as it unfolds |
| Tension | Guessing what they will do | Judging when to spend your reaction |
| Async fit | Excellent — sealed orders, no waiting | Poor — the reacting player must be present |
| Fog | Orders given on stale information | Reactions given on live information |

The tick loop already supports either: playback is just running ticks, and a reaction is an order injected mid-run. Nothing built so far forecloses the choice.

Two things to settle when this is picked up. What a reaction may actually *be* — a full order, or something narrower like a stance change or a facing change, which is where the slow turn rates would bite hardest. And how it survives correspondence play, since it wants both players watching, which is exactly what the async model was chosen to avoid. A likely answer is that reactive playback is the live mode and sealed simultaneous orders remain the correspondence mode, sharing one resolver.

### Stances — the answer to "the plan didn't survive contact"

The core UX problem of WEGO-plus-fog is committing orders against stale information. Every order carries a **stance** acting as a standing contingency:

`Aggressive` (pursue and engage) · `Advance` (continue, fight if blocked) · `Hold` (do not leave position) · `Fire at will` (stationary, engage in range) · `Evade` (withdraw from contact, never counter-charge)

Stances are what make blind simultaneous orders feel like commanding an army rather than gambling. **Prototype them in the CLI at M2/M3** — this is the assumption most likely to be wrong and the cheapest place to find out.

### Routing: the player and the AI want different things

A player's order and an AI's plan are answering different questions, so they use different pathfinders behind the same `IPathfinder` interface.

**Players get `DirectPathfinder`** — shortest route, detouring only around what genuinely cannot be crossed. Cost-optimising a human's order means the computer quietly refuses the line they drew and goes a longer way because the going is better. You pointed at a spot; the unit goes to that spot. Marching into a swamp is a decision the commander is allowed to make.

**The AI gets `HexPathfinder`** with real terrain costs, minimising travel *time* — which is exactly the judgement an AI should be exercising.

Terrain still slows the march either way; only the *choice* of route ignores cost, never its consequences. The reported travel time is always costed against real terrain, so the estimate stays honest — on the valley map the same order reads 15.8 turns direct through swamp and forest against 9.9 turns by road.

**Units route as a point at their centre.** A ~2 m margin exists only to stop a line touching the exact corner of an obstacle, where rounding decides which side of a shoreline a sample falls on; against 80–110 m regiments that is a point for every practical purpose. Width-aware routing is implemented and available, but off by default: a 110 m line refusing to approach a shoreline it could obviously stand beside reads as broken. Formations may also overhang the edge of the battlefield — the map running out is not a wall.

### Randomness

Attrition plus morale implies dice, but high variance combined with hidden information and blind orders reads as *unfair* rather than exciting — the player can't distinguish being outplayed from being unlucky. Keep variance low: deterministic core damage with ~±10% spread; morale against thresholds, not open rolls.

---

## Game systems (v1)

**Stat block:** `Strength` (current/max men) · `Attack` · `Defense` · `Armor` · `Range` · `Speed` · `Vision` · `Morale` · `Cohesion` · `Initiative` · `Footprint` (width × depth) · `MovementType` (Foot/Horse/Wheeled) · `Class`

**Counters:** Spear/Pike beats Cavalry frontally, loses to it flanked · Cavalry beats Archers and Artillery · Archers beat Infantry at range, lose in melee · Artillery outranges all, helpless in melee · Scouts see far and move fast, negligible combat · Commander grants a morale aura and is a win condition.

### Formations, and why they cost something *(planned)*

A regiment can be **reshaped** — trading frontage for depth. A 300-strong cavalry regiment in three ranks is 110 m wide; in eight it is barely 40 m and can be threaded into ground a line could never occupy. Wide presents more men to the enemy and holds more ground; deep is manoeuvrable, punches through, and fits.

This falls out of work already done: `Footprint` is computed from strength and formation rather than stored, so making formation a property of the *unit* rather than its *type* is most of the feature. It also gives the existing width-aware routing a real purpose — a column genuinely fits through gaps a line cannot, and the choice becomes tactical rather than an annoyance.

**Reshaping must not be free**, or every unit simply adopts whichever shape currently suits and the decision is hollow. The cost lands on **organization**, below.

### Enveloping: splitting to surround *(planned, to be detailed)*

A regiment that engages a much smaller one may **split into three or four bodies and wrap around it**, closing on the enemy from every side at once rather than meeting it face to face.

The reason it needs to exist is already visible in the frontage numbers. Six hundred spearmen in line hold 90 m; two hundred hold 30 m. Meeting head on, two thirds of the larger regiment stands idle with nobody in front of it — all that extra strength contributes nothing. Splitting is how a numerical advantage becomes an actual advantage.

The payoff is not merely that more men reach the fighting. Surrounding means attacking from flank and rear, which the facing system already treats as far more dangerous than a frontal attack, and being surrounded ought to be ruinous for morale — which connects straight into the rout and pursuit design, since a unit that breaks while enveloped has nowhere to run.

The cost should be real. Three small bodies are individually fragile, out of position, and slow to re-form; if the isolated enemy turns out to be supported, the splitter is caught divided. Splitting and rejoining should both cost organization.

*Architecturally this fits unusually well:* `Footprint` is already computed from strength, so sub-units size themselves correctly with no extra work, and `BattleState.AddUnit` already issues ids. The genuinely new parts are tracking parentage so bodies can re-form, and placing them around a target's perimeter.

Worth settling when detailed: what strength ratio permits it; whether the parts take orders independently or move as one; whether they re-form automatically once the fight ends; and whether the surrounded unit gets any chance to break out before the ring closes.

### Morale and organization are per unit, not per type *(planned)*

Two separate per-instance stats, both starting from the unit type's rating and diverging from the moment the battle begins:

- **Morale** — willingness to keep fighting. Falls with casualties, a commander's death, being flanked, watching neighbours rout. Governs whether a unit breaks.
- **Organization** — how well the formation is holding together. Spent by reshaping, by moving through broken ground, by charging, by being charged. Governs how well the unit fights while it is still fighting.

The point is that three spearmen regiments are not interchangeable. One that has been shelled, force-marched and reshaped twice is a different proposition from a fresh one, even at identical strength — which is what makes reserves, rotation and picking your moment matter.

*Architecturally:* `Formation` moves from `UnitDef` to `UnitInstance` (the type supplies the default); `UnitInstance` already carries `Cohesion`, which becomes `Organization`; morale becomes per-instance alongside it. Combat (M3) reads both. Small refactor, since `FootprintAt` already takes strength and everything downstream reads `unit.Footprint`.

**Terrain:** Plains · Forest (slow, conceals, blocks sight) · Hills (slow, +defense, +vision, sees over forest) · Mountains (impassable to Wheeled) · River/Ford (heavy crossing penalty) · Road (fast, no defensive bonus) · Marsh (slow, −defense) · Village/Fort (+defense, objective). Each vision-grid cell carries `Elevation` 0–3.

**Vision:** per-unit radius, LOS on the vision grid, blocked by elevation and sight-blocking features. Three tile states — `Unknown` / `Remembered` (terrain known, stale ghost markers) / `Visible`. Units in forest are detected only at close range.

**The battleships mechanic proper:** artillery may fire at a *remembered* or wholly unobserved location and receives only hit/miss feedback. This is the literal Battleship analogue and the most distinctive idea in the design — fog becomes something you interrogate rather than merely suffer.

**Win conditions:** army breaks and surrenders · objectives held N turns · voluntary withdrawal · annihilation · points at turn limit.

**Morale collapse, not decapitation.** Killing a commander is *not* an instant win. It inflicts a severe army-wide morale shock; units below their threshold rout; routing units panic their neighbours; and if enough of the army breaks it surrenders. Defeat is the consequence of a morale cascade, which makes the commander worth protecting without making the whole battle a single assassination puzzle.

**Pursuit decides who actually dies.** Routers flee the field but survive unless they are run down — historically where most battle casualties happened. This makes pursuit a real decision (commit cavalry to destroy the enemy permanently, at the cost of breaking your own formation while the battle may not be over), makes cavalry decisive *after* the fighting as well as during it, and makes early withdrawal a way to preserve an army rather than merely lose slower. Reports must therefore distinguish **killed** from **scattered** from **captured**.

---

## Testing strategy

Deliberately load-bearing: with attrition × morale × simultaneity × fog interacting, you cannot verify balance by playing. You need the harness.

| Layer | What it is | Speed |
|---|---|---|
| **1. Unit tests** | xUnit over hex math, pathfinding, LOS, combat formulas, morale | ms |
| **2. CLI harness** | ASCII board + fog as text; type orders, watch tick-by-tick resolution | interactive |
| **3. Scenario tests** | Hand-authored small maps + scripted orders + asserted outcome. Verifies *design intent*: "cavalry charging spearmen frontally loses" | ms |
| **4. Golden replay tests** | Recorded order log + hash of final state. Any rules change altering outcomes fails loudly; you review the diff | ms |
| **5. Reproducibility tests** | Same seed + orders twice → identical state hash. Catches RNG-order and hash-iteration bugs | ms |
| **6. Fog leak tests** | Serialize `PlayerView`, assert zero data about hidden enemies. A *security* test | ms |
| **7. AI soak tests** | 500 headless AI-vs-AI matches; no crashes, no infinite games, win rates and matchup stats to CSV | minutes |
| **8. Authority integration** | Two fake clients play a full async match through `IMatchAuthority`, local and remote impls both | seconds |
| **9. Unity PlayMode** | Thin — only "does this click produce that order". Slow, keep few | slow |

Layer 2 deserves emphasis: **an ASCII renderer lets you play and feel the game before any art exists.** A few hundred lines, and it's how you answer "is this fun?" months before Unity is involved. On a fine grid it renders downsampled to the vision grid.

---

## Milestones

Each has an explicit gate. Do not proceed past a gate.

### M0 — Foundations
Repo, solution, Contracts/Rules assembly split, content format, seeded PRNG.
**Gate:** `dotnet test` green.

### M1 — World space, terrain and the pathfinding grid
Axial/cube hex coords, distance, neighbours, rings, hex line drawing (**done**). `WorldPos`/`HexLayout` (hex ↔ continuous-position conversion). Terrain defs, elevation, per-movement-type costs. Maps authored as text at a coarse human-readable resolution (25 m per character), independent of the finer pathfinding resolution that samples them. A* over that grid, with output smoothed into a natural path. The vision grid is *not* built yet — it isn't needed until M4.

*Content format: a minimal `key = value` / `[section]` text format rather than JSON. Avoids a dependency both the .NET build and Unity's package manager would have to resolve identically, and — more usefully — is schema-less, so a content file can carry a setting the loader has never heard of. That is what makes new terrain attributes a zero-code change.*
**Gate (1, 2):** property tests (distance symmetry, neighbour round-trips — done); pathfinding tests on known maps, including that smoothed paths stay off blocked terrain; CLI renders a map.

### M2 — Units, orders, continuous movement (no combat)
Stat blocks, regiment strength, `Footprint`/`WorldPos`/`Facing`, oriented-rectangle collision, order types, stances, the 60-tick loop integrating continuous movement along smoothed paths, ZOC.
**Gate (3):** converging units stop at contact, not by grid adjacency; cavalry outruns infantry within one turn at the calibrated speeds; ZOC halts advance; rectangle collision correctly separates overlapping footprints. CLI prints tick-sampled ASCII frames (positions downsampled for display only).

### M3 — Combat, morale, rout
Attrition formula, armor, class matchups, terrain modifiers, geometric flanking off `Facing`, charge bonus. Morale, cohesion, rout, rally. Ranged fire and artillery.
**Gate (3, 5):** scenario tests asserting design intent for every counter relationship; generated unit-vs-unit win matrix to eyeball.

### M4 — Fog of war
Per-unit vision, elevation-aware LOS on the vision grid, concealment and detection, three-state knowledge, ghost markers, `PlayerView` projection, blind artillery with hit/miss feedback.
**Gate (1, 6):** LOS tests over elevation maps; **fog leak tests**; CLI renders each player's fogged view separately. **Gate met** — pass 4 (blind artillery) is a mechanic the gate does not depend on.

**What a sighting tells you.** Body, not spirit. Position, bearing, what kind of troops, a banded headcount, and whether they have broken — all things visible from across a field. Not morale, cohesion, ammunition or orders, because *"is that line about to crack?"* is the judgement the whole game turns on, and it has to be bought by pressing the attack rather than read off a bar. The headcount is banded rather than noised: a wrong number that jitters each turn reads as the game lying, a coarse one reads as distance.

**Sightings are frozen whole.** A remembered position with a live headcount attached would be fog wearing a ghost's clothes, so `VisionState` stores strength and facing alongside the place and the tick.

**Still open:** unit ids are handed out across both armies, so seeing one numbered nine reveals that ten regiments took the field. A side channel rather than a leaked field, and the reflective sweep cannot catch it. Fixed by minting per-viewer opaque handles — pointless before M8, since until orders travel over a wire the client is the authority anyway. Written down as a skipped test in `FogLeakTests`.

**Also still open:** Unity's asmdef references `BattleChess.Rules`, so the client holds live state and its fog is a rendering choice rather than a guarantee. That is not a fog bug — the Unity host *is* the authority today. It closes at M5 with `LocalMatchAuthority`, when the client stops running the simulation and starts asking for a view.

### M5 — Full match loop, offline playable ⭐
Win conditions, full turn pipeline, save/load, replay playback, `LocalMatchAuthority`.
**Gate (2, 4):** complete hotseat match end to end in the CLI; golden replays locked in. **This is the "is it fun?" checkpoint and it lands before a single line of Unity code.** If the answer is no, you've lost weeks rather than a year.

### M6 — AI opponent
Consumes the same fogged `PlayerView` a human gets, so it cannot cheat. Utility scoring, threat maps, objective pull. Difficulty scales via memory decay and evaluation noise — never extra information.
**Gate (7):** 500-match soak, no crashes or infinite games, sane win-rate band, balance CSV.

### M6b — Auto-resolve and battle reports ⭐
The first thing the map-game audience can actually use. Army setup files (side, unit types, strengths, broad plan), a one-shot resolve driven by the M6 AI, and a readable battle report: outcome, casualties split into killed / scattered / captured, survivors carried forward, and the seed for verification.
**Gate:** two people can settle a battle from setup files alone, and independently re-run the published seed to identical results.

### M7 — Unity client
Unity 6 LTS consuming the core as a local UPM package, referencing **Contracts only**. Continuous-looking unit movement interpolated from tick events, fog overlay, drawn move orders, replay playback. Offline vs AI and hotseat through `LocalMatchAuthority`.
**Gate (9):** input→order PlayMode tests; replay the M5 match in Unity with the same seed and orders, assert an identical final state hash to the CLI.

*Deferred to here: 2D top-down is the recommended default — fastest path, reads well, and the core is renderer-agnostic so 2.5D/3D stays open.*

### M8 — Host-based online (async correspondence)
Sealed orders — the authority never reveals one side's orders until both arrive. Match persistence, invite codes, host migration if cheap. NAT traversal or a thin relay.
**Gate (8):** two clients play a full match host-to-host; fog leak test asserted at the wire boundary.

### M9 — Dedicated server (required before public multiplayer)
`RemoteMatchAuthority` + ASP.NET Core minimal API + EF Core/SQLite hosting `BattleChess.Rules`. Accounts, matchmaking. Closes the trusted-host hole.

*Was "optional". A Steam release makes it mandatory: the trusted-host model lets the hosting player inspect their own process and see through fog, which is tolerable among friends and not tolerable among strangers. The client only ever talks to `IMatchAuthority`, so this is a transport swap, not a rewrite.*

**Gate (8):** same integration suite passes unchanged against the remote authority.

### M10 — Content, polish, balance
Full roster, faction asymmetry, maps. Live mode = async + turn timer + presence.

### DLC ideas *(parked)*

- **Warships.** Naval units fighting on the water the land army currently treats as a hard edge. Fits the existing seams unusually well: a `Boat` movement type is one enum value plus a `speed.boat` line per terrain, which inverts passability for free — deep water becomes the only passable ground and land the obstacle. Broadside arcs would be the genuinely new mechanic, since facing already exists but firing arcs do not. Worth deciding whether naval battles are separate engagements or ships support a land battle from a coastline.

### Special unit types *(to be detailed)*

Beyond the six line units. Nothing specified yet — the shape of the request is "more kinds of thing", and what matters is that the content system already takes them without code changes: a new `[unit ...]` block is a new unit, and a genuinely new *behaviour* is one `Define(...)` line in `UnitAttributes` plus a line per unit that wants it.

Worth settling when specifying: whether these are variants of existing classes (heavy cavalry, crossbowmen, militia — cheap, all content) or new `UnitClass` values with their own counter relationships (which touches `attackVs` tables everywhere and is the expensive kind); and whether any need a rule that does not exist yet, since that is the only part that cannot be data.

The **commander** already parked below is the first of these and the one the win condition depends on.

### More randomness *(open question — partly done)*

Variance was deliberately kept low: with hidden information and blind orders, high spread reads as *unfair* rather than exciting, because a player cannot tell being outplayed from being unlucky. That reasoning still holds, but the battles were reading as arithmetic.

**Done:** exchange and volley spread widened 10% → 15%, and a charge now rolls at 35% — the one moment worth gambling on, because the instant of impact is where battles turn and a charge that always does exactly what the sum says is a charge nobody holds their breath over.

**Proposed, in order of how much character they add per unit of unfairness:**

1. **Regiment quality at muster.** Each unit rolls ±10% on its morale rating when raised, from the battle seed. Two spearmen regiments stop being interchangeable, reserves and rotation start to matter, and it is *visible* — so it reads as character rather than luck. This is the best of the four.
2. **A breaking point that is not a round number.** Units break somewhere in 0.15–0.25 rather than exactly 0.20, rolled per regiment. Stops the rout threshold being computable, so committing the reserve becomes a judgement instead of a calculation.
3. **Rally that is not guaranteed.** A broken regiment that gets clear rolls to come back rather than always rallying. Makes pursuit worth more and gives a routed flank real weight.
4. **Wider melee spread generally.** Simplest, and the one I would do last — it adds noise without adding story.

Any of these stays reproducible: everything rolls off the battle seed, so a published seed still replays exactly.

### Terrain fog *(parked — designed, not built)*

The original M4 design had knowledge of the *ground* as well as of the enemy: every map cell in one of three states — `Unknown` (never seen), `Remembered` (terrain known, any units on it stale), `Visible` (live). Settled for now as **units only**: both players are assumed to have a map of the field, which is what a real commander has and which removes a large class of tedium.

Kept because it may still earn its place — for a campaign layer, for scenarios where one side genuinely does not know the ground, or as a difficulty setting. Two things keep the door open at no cost:

- Vision is asked as *"can army X see regiment Y"* rather than baked into a map. Terrain state is an additional layer, not a rewrite of the existing one.
- `PlayerView` is a projection built per player. Withholding terrain is another thing the projection declines to copy, and the fog-leak tests already assert at that boundary.

Related: hidden passages below, which is this idea reduced to the one case that clearly pays for itself.

### Hidden passages *(parked — needs M4 first)*

Terrain that is passable but not known to be passable: a track through a mountain range that does not appear on the map until somebody finds it, or is known only to one side from the start.

The exception to *"you already know the map"*. Fog was deliberately settled as hiding **units only** — both players are assumed to have a map of the ground, which is how a real commander goes into a battle and removes a whole class of tedium. A hidden pass is the one case where terrain knowledge is worth fighting over, and it is worth a lot: an army that knows a way through a range can appear on a flank that the enemy believes is anchored on impassable ground.

Needs deciding: whether a pass is revealed by scouting it, known to one side by scenario, or found by chance; and whether the map shows a *wrong* answer (the range looks solid) rather than an absence. Wants the same seam as everything else — a per-terrain-cell flag rather than a special map feature.

### M11 — Map tooling *(to be detailed)*

Three related tools, deliberately left vague until specified:

- **Map generator** — produce battlefields procedurally rather than by hand.
- **Map extractor** — pull a battlefield out of some existing source. Likely relevant to the map-game audience, who already have maps of their world and would want to fight on the actual contested ground rather than a generic field.
- **Map creator** — author maps directly instead of editing text by hand.

All three are well served by the existing seam: `ITerrainMap` only promises *"which terrain is at this point"*, so a generator, an extractor and a hand-drawn image can each back it without pathfinding, movement, vision or combat noticing any difference. Whatever these turn out to be, they should produce something that satisfies that interface — nothing downstream needs to change.

Worth settling when specifying them: whether output is the current text format, images, or a new format; and whether the creator lives inside Unity (editor tooling, richer) or stands alone (usable by map-game moderators who do not own the game).

---

## Risks

1. **WEGO + fog is the hardest combination in turn-based design.** Great when it lands; the order-versus-reality problem *is* the game. Stances are the mitigation — prototype them at M2/M3 before building anything expensive on top.
2. **Four interacting systems means balance is hard.** Attrition × morale × simultaneity × hidden information produces outcomes you will not predict. The M6 soak harness is your only real instrument.
3. **Two collision-adjacent systems are still hand-built without an engine's help.** Oriented-rectangle collision and path smoothing are standard, well-understood techniques, but they're being written from scratch (no NavMesh/PhysX headless). Watch for units clipping through each other under fast conflicting orders, and for path smoothing cutting corners through terrain it shouldn't.
4. **Scope.** Several months part-time to M5, again as much to M8. The CLI-first ordering exists precisely so the expensive parts come after you know the game is fun.

## Rough effort (solo, part-time)

M0–M2 ≈ 4–6 weeks · M3–M4 ≈ 5–7 weeks · M5–M6 ≈ 3–5 weeks · M7 ≈ 4–8 weeks · M8 ≈ 3–5 weeks. Shape, not schedule; M3 and M7 have the widest error bars.
