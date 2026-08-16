# Changelog

What changed, by the requirement it implements. Requirement numbers refer to
[DECISIONS.md](DECISIONS.md), which is the permanent record of how the game is meant
to work. Findings refer to [OPEN-FINDINGS.md](OPEN-FINDINGS.md).

---

## Movement requirements, 14 August 2026

A play-test loop over movement. Five successive reports, all wearing the same symptom
("it goes through units"), and four distinct causes underneath it.

### M27, a gap between two regiments is a corridor with an axis of its own

Threading a gap means going through the way the gap faces, not the way the march was
heading. The regiment squares to the pair it is passing between, presents its depth to
the walls either side, and comes back onto its line afterwards.

Recorded: Archers and Swordsmen 50 m apart, both 20 m deep, so 30 m of daylight, and a
cavalry regiment 20 m across side on standing in the gap. It shouldered through and
reported "no way round it and no gap to thread". Every aiming point the planner could
generate was tied to one body's centre, so the midpoint between two neighbours was
never a candidate. Projecting that gap onto the line of march also makes it 20 m,
exactly the regiment's own depth, which is why the corridor only exists when it is
measured along its own axis.

Closes finding 18. Pinned by `GapInTheLineTests.AGapWideEnoughToThreadIsThreaded`.

### M28, where a route may bend is the corners of what is in the way

Each body is grown by the mover's own reach, the corners of what that leaves become the
only places a route may bend, and the cheapest walk through them is found. This
replaces guessing a single aiming point perpendicular to the line of march, a
construction that had been patched three times with each fix uncovering the next
arrangement it could not describe.

What ended it was the simplest arrangement in the game: one regiment alone in open
ground, halfway along a 71 m march, which was shouldered through on five orders out of
ten. Measured there, the second leg of a one bend detour was blocked at every stand off
from 46 m to 186 m while the destination itself was clear. The route that works is two
bends, and one waypoint cannot express two bends at any parameter value.

The corner walk and the aiming rules compete in seconds, as arching and crabbing
already do.

Closes finding 19. Pinned by `GapInTheLineTests.AShortHopPastOneCornerGoesRoundTheCorner`
and `OneRegimentAloneInOpenGroundIsWalkedRound`.

### M29, a leg is not walked into until the front it was checked at has arrived

A plan is a claim about a shape, and a regiment cannot adopt a front instantly. It
reaches the tight part of its route still coming round, in a body nothing ever measured.
Recorded: inside a 30 m corridor measured against a 20 m body while holding a front 121
degrees off, where the same body spans 44 m, three ticks before a 121 degree wheel at
5 degrees a second could possibly have finished.

The waiting happens on the step that would have hit rather than at the mouth of the leg,
which is the designer's call and the better one. A regiment that would clear the gap
anyway is never delayed. A body it is already standing in never holds it, or M25a's
reason for existing would be undone.

This is the requirement that addressed the half of the problem the earlier work had
missed. Across four recordings, 14 of 37 collisions happened on routes that were never
press throughs at all, meaning the planner had already declared them clear. Measured on
a fixed set of six marches: 15 ticks of walking into people, down to 0.

Closes finding 20. Pinned by
`PlanAgainstWalkTests.ARouteThePlannerCalledClearIsWalkedClear` and
`TheSweepAgreesWithSteppingAlongTheSameLine`.

### T2, turn rates settled

Cavalry raised from 3 to 5 degrees a second, horse archers from 4 to 6. Recorded:
twelve orders in one game, every one an about face of 121 to 179 degrees, which at 3
degrees a second is 40 to 60 ticks of wheel against marches lasting 63 to 96 seconds.
Achieved pace had fallen to 2.1 m/s where the ground allowed 4.8. A reversal now takes
36 seconds rather than 60.

What remains open in T2 is the shape of the charge rather than its size: a flat rate
per second still bills a change of front against the clock rather than against the
ground.

### W6, a collision is written down, and a decision names the line it chose

Two of its own sharing ground was completely silent. The overlap cost, the shuffle
apart and the yield rule all ran without a word, so every play test report had to be
reproduced from scratch against a recording that held no evidence it had happened.

A collision now records who, where, facing where, how deeply overlapping, what each was
doing, who gives ground and why, and how long it lasted. Every rung of the routing
ladder now prints its waypoints, which side a detour passes, and how far off the
straight line it swings. The fall through to the search said nothing at all before.

Both are said once per event rather than once per tick. Said unconditionally the move
rules alone were 218 of 297 lines in a twelve turn battle, and the existing volume gate
failed the build for it, correctly.

Pinned by `CollisionRecordTests` and the existing `LogVolumeTests`.

---

### Also recorded, not yet fixed

- **Finding 16a.** A pair hovering at the grazing tolerance oscillates across it. The
  log was given hysteresis; the rule was not, and it is the right shape for the
  reported stutter at unit edges.
- **Finding 16b.** An attack's final approach falls through to the pathfinder on every
  re-plan, which is what M10 exists to avoid.
- **Finding 17a.** The pathfinder takes no unit and no battle state, so it cannot see a
  friendly body at all, and the route it returns is not flagged as pressing through.
- **Finding 19, residue.** The corner walk searches in metres while the ladder chooses
  in seconds, so it can return a route that is short and badly wheeled.
- **M26**, a costed comparison between going round and shouldering through, is built and
  measured but not committed. At the current pace inside your own it makes shouldering
  win everywhere, and the pace is a designer's number.

---

### Gates at the end of this work

568 passing, 20 skipped, balance sweep unchanged at its one recorded concern, Unity
compiles clean.
