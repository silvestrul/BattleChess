# Open findings

Things that are known, reproducible and **not yet fixed**. Each entry names the
test that pins it, so the entry and the test can be deleted together once the fix
lands. Struck-through entries are closed and kept only until the ground around
them is finished.

**This file is scaffolding.** When it is empty, delete it. For how the game is
*meant* to work — which is permanent — see [DECISIONS.md](DECISIONS.md).

Raised by the manoeuvre sweep of 13 Aug 2026 (`c02380d`), which added 85 tests
over movement, rotation, grouping and attack orders.

---

## ~~1. A third regiment sent at one enemy does nothing~~ — placement FIXED

Closed **as a placement bug** by the two-per-face rule. The damage half of the
same rule turns out not to hold — see finding 4, which is the more important of
the two and was found by reviewing this entry rather than by any test.

**The rule, as decided.** A face is worth **one full frontage of fighting**
however many regiments are pushed onto it, because damage is dealt across the
ground two bodies genuinely share and two attackers divide that ground between
them. So a pair on the front deal together what one deals alone, each taking
half — and buy the defender's nerve rather than his blood. Four faces at two
apiece is six or eight regiments landing four frontages of damage.

**That is the intent. The code does not do it** — measured at 2.3× the damage
for two attackers rather than 1×. Finding 4 has the numbers.

Capacity is capped at two **and limited by what the face can physically hold**:
the 40 m front and rear take a pair, the 6 m flanks take one. A lone flanker
already fights across the enemy's whole width — nothing holds it off, so it
folds round — which is exactly the full frontage that face is worth.

Overflow is **never reassigned to another face**. It forms up behind the
attacking line on the face it was ordered at. Going round is an order somebody
gives; the game marching a regiment across a formed enemy's front to find room
is how regiments get cut up on their own side's initiative.

**Two things fell out of building it, both worth remembering.**

- Slots were handed out in **id order**, so three regiments abreast crossed each
  other to reach them, shoved, and arrived as one. They now queue in the order
  they already stand along the face. Men take the place nearest them.
- The first capacity rule asked whether the face was at least twice a regiment's
  half-width — a dead heat for two 40 m bodies. A defender's frontage narrows as
  its men are killed, so **the first casualty tipped the answer from two to one**
  and dropped a regiment that was already fighting into the reserve for the rest
  of the battle. This is the third time a threshold placed exactly on its own
  common case has caused a bug in this project. Put them well clear.
  **This was only half diagnosed at the time** — see finding 5.

Now pinned live by `AThirdRegimentSentAtTheSameEnemyMustNotMakeTheAttackWorse`,
`AThirdRegimentWaitsBehindTheLineRatherThanShovingIntoIt`,
`ARegimentOrderedAtAFullFrontDoesNotWanderRoundToTheFlankByItself` and
`AFaceDoesNotStopHoldingTwoJustBecauseTheDefenderHasLostMen`.

---

## ~~1a. A reserve does not walk into the place that opens for it~~ — FIXED

It was never a separate fault. It was finding 5 wearing a different face: the
defender had bled below the capacity threshold, so the face was declared full at
**one**, and with two attackers left one of them was still correctly a reserve —
standing there because the rule said to, not because it was stuck.

Fixing capacity to read a frontage that does not change fixed this at the same
time, with no further code. Frontage is now genuinely refilled from reserves
(decision F5, non-negotiable), and pinned live by
`AttackOrderScenarioTests.AReserveStepsIntoTheLineWhenTheRegimentInFrontOfItIsGone`.

**Worth remembering:** two turns were spent guessing at `FollowTarget` and
reverting, on a diagnosis built from one trace. The symptom — "it sits thirty
metres out and does not come in" — was real; the cause was one system away from
where it showed.

The test needed the defender held to its nerve to be meaningful at all. Left
alone it routs within four turns of meeting three regiments, and a reserve
chasing a fleeing enemy says nothing about whether it would have stepped into a
line. That is the fifth time in this sweep a placement question was nearly
answered by a rout.

---

## 1b. A four-sided attack gets two regiments in, not four

**Severity:** medium, and it is the uniform rectangle again rather than the
attack rules.

**Pinned by:** `AttackOrderScenarioTests.RegimentsSentFromFourSidesEachTakeTheirOwnFace`
(skipped).

Four regiments sent from four quarters: the pair on the front and rear span the
defender's whole 40 m width and cover the very corners a flanker needs to stand
on, so the two flankers are blocked out by their own side. The defender is only
6 m deep, so its flanks are barely ground at all.

Four faces is the right rule and the face-choosing machinery already does it —
`ChooseFace` picks one of four from where the attacker was ordered. What is
missing is depth. **Task #41** exaggerating the block to 2.5× makes the flank a
real face to stand against; nothing else needs to change for this to start
working.

---

## 2. A regiment in melee cannot be ordered to break off at all

**Severity:** medium. It is a missing feature rather than a fault, but it means
a mistake made with an attack order is unrecoverable, which is harsh for a game
about manoeuvre.

**Pinned by:** `MarchingScenarioTests.ARegimentInMeleeCanBeOrderedToBreakOffAndPayForIt`
(skipped).

**Behaviour.** Once contact is made, the order system puts the attack straight
back every tick. A move order given to a regiment in contact is overwritten
before it can be acted on, so the regiment stands there until one side breaks.

**Designed but not built.** The withdrawal rule was worked out and agreed but
deliberately sequenced after the movement passes: casualties multiplied by
`1 + engagedFraction`, and the order refused outright above about 85% gripped.
It needs the engaged fraction, which is the same number **#41** introduces.

**Related and correct.** A regiment halted 7 m from formed spearmen that is
then ordered to march away *is* cut up and does break. That is right — turning a
formation across a formed enemy at arm's length should be ruinous — and it is
now pinned by `MarchingScenarioTests.MarchingAwayAcrossAFormedEnemyAtArmsLengthGetsTheRegimentCutUp`.
The withdrawal rule should put a price on this, not remove it.

---

## ~~3. An explicit attack order has no pursuit leash~~ — DECIDED, left as it is

Settled by decision **O4**: pursuit happens when, and only when, a regiment was
ordered to attack. An ordered attack runs its enemy down with no leash, because
the player asked for it and a regiment that quietly abandons an order is worse
than one that obeys.

What changed instead is the case that was actually wrong: a regiment that was
*marching* and had to fight its way past now holds the ground it won. It neither
chases the men it broke nor resumes a march the situation has overtaken — that
judgement is the player's. `UnitInstance.ForcedIntoThisFight` carries the
distinction, and any fresh order clears it.

Pinned by `ARegimentThatFoughtItsWayPastSomebodyHoldsTheGroundItWon`,
`ARegimentActuallySentAtAnEnemyStillRunsItDown` and `AFreshOrderRestoresThePursuit`.
The separate leash on *unordered* engagement still applies and is pinned by
`ARegimentLookingForAFightIsNotBaitedOutOfPositionByOneItCannotCatch`.

---

## ~~4. Two attackers deal 2.3× the damage of one~~ — FIXED

Closed. Two attackers now bring **156** men to bear where one alone brings
**157**, and the defender answers with **154** where it answered **157** alone.
Total frontage is conserved, each attacker deals and takes half, and what the
second regiment buys is the defender's nerve — which is what it was always meant
to buy. Pinned by `SharedFrontageTests`, five tests.

**The rule, as you put it:** divide by the ground each enemy actually shares, not
by how many of them there are. Attackers on sixty and forty metres of a hundred
metre front are answered with sixty and forty. Because `SharedFrontage` is
symmetric, that *is* the geometric share, so the divisor simply goes; all that
remains is a cap for the case it was protecting — regiments stacked on the same
ground, whose claims sum past the frontage that exists, are scaled back in
proportion. See `UnitInstance.ClaimedFrontage`.

**Two bugs, not one.** Removing the divisor fixed the defender's side (108 → 154)
and left the attackers still bringing 220. The rest was a second fault in the
same function, found only by measuring again afterwards rather than declaring
victory on the first improvement:

`OffFront` — which every rule about being caught out of position reads — was the
bearing from one centre to the other. For a rectangle 40 m wide and 6 m deep that
says almost nothing. Two regiments drawn up shoulder to shoulder against a front
stand 22 m either side of its middle and 9 m ahead of it: **68° round**, past the
45° flanking arc. So a pair attacking a front squarely were each scored as
flankers — each collecting the envelop bonus, a 38% flanking multiplier, and a
quarter discount on its own line for "facing the wrong way" — while all four
rectangles stood square on to each other.

It is the point-versus-shape mistake again, in the last place it was still
hiding. `OffFront` now measures from the nearest point of the enemy's shape,
against how far the regiment reaches each way. Genuine flanking is untouched:
every flanking, rear-attack and envelopment test passes unchanged, and the sweep
is still 0 concerns.

---

## ~~4 (original entry, kept for the numbers)~~

Kept only because the before-and-after numbers are the clearest statement of what
the rule means. **Measured** before the fix, swordsmen against spearmen on
plains, seed 40200, at the moment of contact and then over three pulses:

| | Shared frontage each | Attacker fighting men | Defender answers each with | Defender lost | Attackers lost |
|---|---|---|---|---|---|
| 1 attacker | 39.6 m | 157 | 157 | 17 | 8 |
| 2 attackers | 20.1 m each | 110 each — **220 total** | 54 each — **108 total** | **39** | **6** |

So two regiments bring 40% more men to bear than one does, the defender answers
with 69% of what it managed against one, and the exchange goes from roughly even
to better than six to one. Sending the second regiment is not "the same damage
plus a morale effect" — it is overwhelmingly the right move on casualties alone.

And after:

| | Attacker fighting men | Defender answers with |
|---|---|---|
| 1 attacker | 157 | 157 |
| 2 attackers | 78 each — **156 total** | 77 each — **154 total** |

---

## ~~5. Face capacity flips mid-fight~~ — FIXED

`FaceCapacity` now measures both regiments by `FootprintAtFullStrength` — the
ground they covered when mustered — so a face that held two at the start holds
two at the end. Also closed 1a above.

**The lesson, restated properly.** The first version flipped from two to one at
the defender's first casualty; raising the margin moved it to 70% and I recorded
that as fixed. It was not. A live frontage slides from 100% to 0% over every
fight, so a yes-or-no answer fed by it crosses whatever threshold you pick.
**There is no safe constant — the input has to stop moving.**

This is also the first slice of decision S4: how much room a body of men takes up
should not collapse because it has taken casualties. Collision and fighting
frontage still read the live footprint; **#41** finishes the job.

---

## 6. A corner touch counts as contact but fights nobody

Found by the new combat logging on its first run, which is what it was added
for. A line in a recorded battle read:

```
140 Combat U1  Swordsmen (770 men) meets Spearmen (978 men) — front to front.
               Bringing 0 against 0.
```

Two regiments met, neither could bring a single man to bear, and the pair drifted
apart again having never exchanged a blow.

**Measured.** Two 40 m blocks nose to nose, slid sideways a step at a time:

| offset | in contact | shared frontage | each brings |
|---|---|---|---|
| 0 m | yes | 40 m | 160 |
| 20 m | yes | 20 m | 80 |
| 36 m | yes | 4 m | 13 |
| **40 m** | **yes** | **0 m** | **0** |

The degradation is smooth, so this is not a threshold placed badly — it is the
exact boundary where two blocks touch corner to corner. Contact is decided by the
gap between shapes; fighting is decided by how much frontage they share. At the
corner those two honestly disagree.

**Why it is worth fixing.** `EnemiesInContact` is not only a combat number. It
feeds `OutnumberedShock` and `SurroundedDisorderPerPulse`, so a regiment can be
frightened and pulled apart by an enemy it cannot fight and which cannot fight
it — and, with [F3](DECISIONS.md), a corner-brusher may be occupying a place on a
face that a regiment which could actually fight is queuing for.

**Not pinned by a test yet**, and deliberately not fixed here: this was a logging
pass, and the fix belongs with the flanking work where contact and frontage are
being reasoned about together anyway.

**Still open after F19** (four sides, four counts), and worth saying why, because
F19 was expected to absorb it and did not. F19 changed *how many men a face is
worth*; this is about *whether two shapes overlap at all*, which is still decided
by `SharedFrontage` projecting both rectangles onto one axis. The two zeros in the
recording had different causes and only one of them has been fixed:

| recorded line | cause | after F19 |
|---|---|---|
| `into its rear. Bringing 0 against 122` | rear counted as a flank, then gaps ate the remainder | fixed — the rear is a full frontage |
| `front to front. Bringing 0 against 0` | corner touch, genuinely no shared ground | **unchanged** |

The remaining half is the axis, not the count. `SharedFrontage` always projects
onto `enemy.Shape.Right`, which is the enemy's *frontage* direction — right for a
front or rear meeting and meaningless for a side one, where the face runs along
the enemy's `Forward` instead. Choosing the axis from the face being attacked
would make F15's asymmetry fall out of the geometry rather than needing the
`EngagedWidth` envelop branch to put it back. Measured while F19 was being built:
two regiments perpendicular report a shared frontage of **6.0 m one way and 0.0 m
the other**, from the same pair of rectangles.

---

## 7. An attacking regiment arrives at its destination every tick

Found by the collapsed logging on the run after finding 6, from the number in a
closing line rather than from anything anybody was looking for:

```
Move U0  Swordsmen reached its destination on Plains at 1,6 m/s.
Move U0  — and that held for 217 ticks (217 times over), ticks 115 to 331.
```

**Two hundred and seventeen arrivals in two hundred and seventeen ticks.** Every
tick, for three and a half turns.

**What is happening.** `MovementSystem.Finish` runs when a route is complete and
clears the route. For an attack order `FollowTarget` then lays a new one on the
next tick; if the quarry is already in contact that route is complete the moment
it is built, so it is finished, cleared, and built again — for as long as the
fight lasts.

**Why it matters beyond the log.** Route-building is the most expensive thing in
the rules and `RepathIntervalTicks = 5` exists specifically to keep it off the
per-tick path. This slips past that guard entirely. It also means `unit.Route`
churns between null and a one-step route every tick, which anything reading
`IsMarching` sees flickering.

**Harmless to the outcome as far as anything shows** — the suite and the sweep
are unchanged either side of the logging work. This is waste and noise rather
than a wrong answer, which is why it is recorded rather than rushed.

The fix is presumably to leave a completed route alone when the order is an
attack on somebody already in contact, but that is movement work and wants its
own pass.


**Reduced, not closed.** Reproduced in a test at last, and it is **shooters, not
chargers** — which is why it did not turn up sooner. A regiment that closes to
melee is held by contact and stops re-planning on its own; one told to attack from
a distance halts at its range ([O2](DECISIONS.md)), never enters contact, and so
re-plans the same route to the same place for ever.

Two halves. The order was re-issued although it was already carried out, and each
re-issue built a route of no length that completed on the tick it was made and
announced an arrival for it. Guarding on "already standing where the order wants
it" takes it from **221 re-plans in 360 ticks to 35**.

The remaining 35 are real and unexplained: something moves the aim point by more
than three metres about once every ten ticks, for a shooter that is standing still
shooting a stationary target. Not chased further here. The test bar is set to catch
a return of the every-tick behaviour rather than to claim the fault is closed.

**Also worth recording: throttling this behind the re-planning cadence is wrong**,
which was the obvious reading and was tried. A chase is a run of short routes each
completing almost at once, so one-tick-in-five made a pursuer move in bursts with
pauses — measured, a broken enemy took 24% losses whether it was chased or left
entirely alone. The repeated work was real; the cadence was the wrong lever.
---

## 8. The stall detector calls a detour a stall, and M13 cannot land without it

Found by building M13 (sidestep) and watching two tests fail. **Attempted, reverted,
not committed** — the change is right and the ground under it is not ready.

**What the change was.** A step is priced against the bearing to the next waypoint
and *only afterwards* deflected round whatever is in the way, so the deflected part
of a march is travelled at full speed in a direction nobody charged for.
Sidestepping is currently **free**. Pricing it against the direction actually
travelled is one small method, and it is plainly correct: it is the same fault as
the arrival bearing and the mid-accumulation log line — a quantity computed against
an assumption a later step invalidates.

**Why it cannot land alone.** Honest pricing puts a perpendicular detour at 0.4× of
pace. Clearing a 40 m regiment then takes about 62 ticks. `StallTicks` is 15 and
`ReplansBeforeGivingUp` is 3, so the march is given up at roughly tick 60 — just
before it would have got round. Two tests failed, both with the regiment stopped
dead rather than merely slowed:

```
ARegimentHeldUpByAFriendGoesOnceTheFriendMovesOff
  Once the way is clear it should walk through. It is still 231 m off.

AWingGetsRoundAnObstacleInItsPathAndClosesUpAgain
  U2 never got past the obstacle: it is at x=974 and the obstacle is at x=1000.
```

**The actual defect is the definition of progress.** `KeepTheMarchHonest` measures
distance to the goal, and a regiment going round an obstacle does not reduce it —
sideways is progress that does not look like progress. So the detector cannot tell
"working round a friend" from "thrashing", which is the case it was written for
(a regiment reversing twelve times in twenty-five ticks *was* moving).

**Why the obvious fix is wrong.** Gating on `unit.GoingRound.IsValid` looks right —
it is set exactly while a friend is being worked round, and cleared the moment none
is. But it is set even when *both* ways round are blocked, so a boxed-in regiment
would march forever and [M6](DECISIONS.md) ("an order always ends") would quietly
stop being true.

**Half addressed.** Progress is now measured along the route rather than as
distance to the destination, and the destination-relaxing retry is gone — but that
retry turned out to be *two* remedies wearing one symptom, and deleting both broke
a regiment sent onto its own troops. The ground it was sent to may be occupied, in
which case the destination has to move; or the destination is fine and something
stands halfway along, in which case the route has to change. How far the placement
search moves the aim is what says which, and it was measurably 0 m or measurably
tens of metres and never in between.

**The progress change is not yet exercised by anything.** With arching, the way
round is *in* the route, so a march never leaves it and the old measure would not
have stalled either — verified by reverting the line and watching the test pass
unchanged. It becomes load-bearing when crabbing makes a detour slow enough to
outlast the detector's patience, which is the original trigger and is still ahead.

**Closed.** Four spurious stall reports, then two, then none. Threading a gap
side-on now happens without anything claiming it is stuck.

The last two were not the stall detector at all, which is why guessing at its
patience kept failing. Printed, they read:

```
Swordsmen is hemmed in by its own Spearmen with no way round either flank
Swordsmen is not getting through and is trying another way round
```

The first is the **steering**. Squeezing through a gap puts friends close on both
sides, which is exactly what "hemmed in with no way round either flank" tests for
— so it stopped the regiment dead in the gap, and the stall detector then agreed
with it. Both were describing the manoeuvre working.

So a leg that names a front is now trusted by the steering, in the same way a leg
that has given up on keeping clear already was. The planner has checked that this
body fits along this line at this front; the steering knows only that friends are
close. When they disagree, the one that looked is right.

*Two earlier attempts are worth not repeating: excusing any coming-round rather
than only a crabbed leg breaks M6 outright, and having a crabbed route name the
front it ends on changed nothing, because the fault was never in the turn.*

**What this means for the plan.** M13 and the [M10](DECISIONS.md) stall rework are
*mutually* dependent, not sequential — the ordering in task #43 had it as sidestep
first, then the stall fix, and that is wrong in both directions. They are one pass,
and the design question to settle first is what "progress" means for a regiment that
is deliberately going the wrong way for a while.

---

## 9. Prediction works, and rung two cannot yet choose what to do with it

**Attempted, reverted, not committed.** Pinned by two skipped tests in
`PredictedPathsTests`.

**M16 itself was straightforward and did work.** Walking a friendly regiment along
its own route at its own pace, and comparing both bodies where they would be *at
the same moment* rather than where they stand when the order is given. A regiment
that will have marched on stopped being planned around, which is the case worth
having. Only friends are ever predicted, and [M16a](DECISIONS.md) costs nothing to
honour because enemies are not planning obstacles at all — there is nothing to
predict them *for*.

**Two things had to change together and only one did.** Prediction has to reach
`FirstBodyInTheWay` as well as `IsClearLine`, or the planner is certain it is
blocked and cannot name what by — it then falls through to shouldering past a
regiment that is not even there yet. Making both predictive fixed the third
scenario and broke crabbing.

**The cause is a rule I had implemented wrongly and not noticed until prediction
made it bite.** [M18](DECISIONS.md) rung two is *one* rung — "arching **or**
crabbing, whichever costs less by M17" — and the code tried arching first and took
it if it worked. With prediction finding blockers further out, the arch started
succeeding in cases where crabbing was the better answer, so a regiment walked a
hundred and sixty metres round a wall it could have slipped through the middle of.

Costing the two against each other was written and did not settle it either: the
arch measured cheaper than the crab (about 500 against 566, crabbed legs charged at
two fifths of pace) and *still* should not have been chosen, because the arch was
not walkable — both tangents put the body across the wall. So either `ArchAround`
is returning a way round that its own leg checks should have rejected, or those
checks disagree with the sampled `IsClearLine` they now share. **Half answered, and not where it was being looked for.** Asking the question as a
*property* rather than investigating the scenario — *any route the planner returns
must be walkable along every leg at the front that leg is walked on* — turned up a
real fault in the crabbed route, present before prediction and nothing to do with
it. The exit waypoint was placed by arithmetic that allowed for the body being gone
round and not for the body doing the going, which is twice as long side-on as it is
deep. The regiment was told to face front again while still inside the wall, so
every leg after that was a line nobody had checked. Now measured — walked out to
where the rest of the march is genuinely clear — and pinned by
`PlannedRoutesAreWalkableTests` across eight arrangements.

Whether that was also what made arching win cases crabbing should have had is
**not established**: prediction is still reverted, and the two were only ever seen
together. The property test is the thing to re-run first when it goes back in.

Reverted whole rather than left half-landed. Crabbing and rung three, which were
green before it, are untouched.

---

## 10. Steering vetoed a checked plan, and three more from the same play-test

From `logs/battle-20260814-102058.log` and a screenshot, 14 Aug 2026. Four faults,
one fixed.

**a. The freeze came back, by a different door — FIXED, not reproduced.**

```
1579  U4 is going round its own Spearmen          <- arch planned, 3 waypoints
1622  X hemmed in by its own Swordsmen            <- steering vetoes it
1637  > not getting through, trying another way round
1653  > not getting through, trying another way round
1669  > not getting through, trying another way round
1685  X cannot get to where it was sent, stopped 262 m short
```

The same fault fixed for crabbed legs one pass earlier, one rung over. Steering was
told to trust a leg only when it was a crab or a press-through; an **arched** leg
carries neither flag, so it was second-guessed exactly as the crab used to be. The
rule is now that **steering may adjust a step but may not veto one** — the planner
looked at the whole line, this rule sees only that friends are close right now.

**Two attempts at a synthetic reproduction both failed** — the `hemmed in` branch
fires zero times in either arrangement. The mechanism is certain from the log (that
message is emitted immediately before the return that stops the regiment) but no
test discriminates, so the next play-test is the verification. The behaviour guard
that exists says so in its own comment.

**b. Rung three fires far too readily — FIXED, by measurement.** Five press-throughs in 126 lines,
including on a 99 m march with a single regiment nearby. It is meant to be the last
resort and is behaving like the common case. Cause is almost certainly that
`ArchAround` is one obstruction deep and requires both its legs to be *entirely*
clear, which fails constantly in a crowded start area — so a way round that plainly
exists is never found and the ladder drops to shouldering past.

Confirmed exactly, and then repaired by comparison rather than by choosing. The
way round was made pluggable and three candidates run against six crowds of
increasing density:

| crowd | past the first thing | past everything | round, and round again |
|---|---|---|---|
| 1 | ✓ +7 m | ✓ +7 m | ✓ +7 m |
| 2 | **no** | ✓ +27 m | ✓ **+17 m** |
| 3 | **no** | ✓ +41 m | ✓ **+26 m** |
| 4 | **no** | ✓ +58 m | ✓ **+26 m** |
| 5–6 | no | no | no |

The old rule found a way round **once in six**. Both repairs found one four times,
so what separated them was [M17](DECISIONS.md) — bending twice around two bodies
adds about half what standing off further from one does. Shouldering through fell
from five crowds in six to two.

Crowds five and six defeat all three, and that is the honest limit of a cast: it is
what [M19b](DECISIONS.md)'s search trigger is for, and what rung three covers until
that exists. **All three candidates are kept**, with the table that chose between
them as a test that still runs.

**c. The wheels are enormous — open, and this is [T2](DECISIONS.md).** Wheels of
170°, 150°, 147°, 146° and 129° in one short game, each 24 to 42 ticks at 4°/s.
The designer has now seen it. T2 was parked pending exactly this judgement.

Now measured properly across a whole recording rather than sampled:
**1,432 of 2,644 ticks spent coming round — 54%** — over 36 marches averaging
**125°** at **40 ticks** each. Achieved pace 58–76% of nominal. The designer's
call on 14 Aug was to leave the rates alone and fix the planner first, so that
the change can be judged against a number rather than a feeling. T2 stays open
with its three candidates intact.

**d. The route line reads as nonsense for a cast — open, cosmetic.** `0 cells
reduced to 2 waypoints, 0 explored` is what a route that never touched the search
now prints. It should say it walked straight there.

---

## 11. Nothing knew what anything cost — FIXED

From `logs/battle-20260814-122229.log` and a play report, 14 Aug 2026. Three
complaints, one cause: no rule anywhere could price a march.

**a. Shouldering through its own was free — FIXED ([M20](DECISIONS.md)).**
`trustTheRoute` skipped the steering entirely and the step was taken at full
length; nothing charged for being inside a body of men. Cavalry walked clean
through an Archers regiment three times in that recording at 100% pace, which
made rung three cheaper than the rungs above it and inverted the whole ladder.
Now 60% of pace while overlapping anyone, charged on the overlap rather than on
the plan's intent. **Verified by disabling:** the same journey measures 290 ticks
free and 326 charged.

**b. The squeeze had seizures — FIXED ([M21](DECISIONS.md)).** The detour
commitment was released the first tick nobody was touching the regiment, so a
successful sidestep dropped it, the same friend was in the way again next tick,
and the side was re-derived from a slightly different position. **Verified by
restoring the old release:** the committed side reversed **240 times** through
full 180° swings, and a charge finished **320 m short** of the enemy it was sent
at. With the fix: 0 reversals, arrives 4 m off.

Reaching this at all took two wrong arrangements, which is the more useful
finding. A wall of stationary friends never gets near the steering — the planner
casts, sees them, and routes round first. Crossing traffic never gets near it
either, because regiments passing at an angle are wholly handled by the sliding
branch. What reaches it is an **attack**, which skips rung two by design. Both
wrong arrangements reported "0 detours" and would have passed on a flat zero,
which is also what a working commitment gives.

**c. The arrival line reported a number that could never change — FIXED
([W5](DECISIONS.md)).** Every arrival read the pace the ground allowed, asked of
the same code under suspicion; 22 of 27 said "4,8 m/s" while the regiments were
managing 2.6 to 3.6. It now reports seconds taken, the pace actually averaged,
and how much of it was spent shouldering through its own.

**d. "The arcs look too wide" — measurable now, not yet answered.** The
suspicion could not be confirmed or denied because nothing recorded what a detour
was worth. Rung two now costs its candidates in seconds ([M22](DECISIONS.md)),
compares arching against crabbing as [M18](DECISIONS.md) always said it should
([M22a](DECISIONS.md)), and says what it chose and what the straight line would
have cost: *"is going round its own Spearmen rather than through it — 332 s
against 314 s straight."* On the synthetic arrangements the detours run about 6%
over the straight line, which is not wide. **The next play-test is what settles
it**, and the log will now carry the number.

**e. The comparison arrangements are still synthetic — open.** Costing in
seconds did not change which way round wins: both repairs still find one in four
crowds of six and `round, and round again` is still shorter. That is expected —
these are arrangements invented to reproduce a fault, not battles. The live log
had press-throughs at 26% → 41% and every arch single-bend, meaning the
distinguishing feature of the chosen strategy never fires in real play. **The
default should not be changed again until `WaysRoundComparisonTests` is rebuilt
from real battles**, or it will be picked wrong a second time with equal
confidence.

---

## 12. It only wheels once — FIXED, and two of the four reports were one fault

From `logs/battle-20260814-125645.log` and a play report, 14 Aug 2026.

**a. A route that bends is walked holding one front — FIXED ([M23](DECISIONS.md)).**
The designer's report was *"rotation should be done every time it changes direction
… it only wheels once"*, and that is exactly what the code did. `FrontWhileMarching`
opened with `if (!unit.Order.Bearing.HasValue) return unit.OrderFacing;` under the
comment *"the front already is the line of march"* — true while every route was a
single line, and false the moment routes could bend. `OrderFacing` is fixed once in
`GiveOrder` as the bearing from where the regiment started to where it was sent, so
an arching regiment wheeled onto leg one and then crabbed every remaining leg.

Measured on a four-leg route bending 47°, 9°, −9°, −47°: it came round onto **2 of
4 legs** and spent **81 of 111 marching ticks** more than 25° off the leg it was
actually walking. **Verified by restoring the early return**, which puts both
numbers straight back.

**b. Going round sometimes clips what it went round — same fault, fixed with it.**
Three answers to one question: the planner checked each leg at the facing the
regiment held *when it planned*, the walk held the start-to-destination bearing,
and the leg's own bearing was neither. A 40 × 20 body is a different obstacle at
47°, so the shape that was checked was not the shape that travelled. Each leg is
now checked at the front it will be held on, entering on the one before it, both
ends of the turn (`Marching.IsClearLeg`). Rung-3 frequency is unchanged at 2 of 6
crowds, so the stricter check did not push marches down the ladder.

**c. Regiments still get pushed — FIXED, [M1a](DECISIONS.md) made unconditional.**
The exemption from the separation shuffle was written narrow, covering only a
declared press-through, on the grounds that an accidental overlap has no quarrel
with anybody's orders. It has one, and "accidental" covered most of the cases.
Whoever is marching now takes the whole correction. **Verified by disabling:** 1,5 m
of drift on a holding regiment against 0,0 with the rule in.

**Two regiments that are both stationary still share it, deliberately** — deployed
on top of each other they have an equal claim to the ground and no order to appeal
to. A march that *finishes* inside somebody therefore still shuffles both. Nothing
tracks who was there first, and inventing it is not worth a rule yet.

**d. "Sometimes it goes through units at no cost" — not reproduced.** Asked as a
property rather than hunted as a scenario: every tick a regiment is inside one of
its own, counted from outside the rule that does the charging, against what the
rule charged. They agree exactly — 12 ticks, 12 charged. Two things could still
produce the impression and neither is a defect: the charge is gated on the same
5% grazing tolerance as every other overlap question, so a corner clipped is free
by design; and cavalry slowed from 4,8 to 2,9 m/s still crosses a twenty-metre
regiment in seven seconds. The arrival line now names the number per march
(*"18 s of it shouldering through its own"*), so the next report can be answered
from the recording instead of argued about.

---

## 13. Cavalry goes right through units — FIXED ([M24](DECISIONS.md))

From `logs/battle-20260814-135004.log`, 14 Aug 2026. Eight lines, one march, and
the whole fault in it:

```
225 > Cavalry is pushing through its own Spearmen — no way round it and no gap to thread.
225   marching from (313,413) to (169,159) — that line is -120° ... 120° off at 3°/s
312   reached its destination in 87 s, averaging 3,3 m/s, 33 s of it shouldering through its own.
```

Open field, one regiment in the way, and rung two found nothing.

**Isolated by controlling the variable rather than by guessing at the arrangement.**
Two attempts to reproduce it from the recorded geometry both found rung two happily
— one blocker is not enough, and neither is a crowd. What settles it is holding the
regiment, the line and the ground fixed and varying only the front it happens to be
standing on when the order arrives:

| off the line | 0° | 30° | 60° | **90°** | 120° | 150° | 180° |
|---|---|---|---|---|---|---|---|
| rung | 2 | 2 | 2 | **3 — through its own** | 2 | 2 | 2 |

At 90° the body presents its whole forty-metre frontage broadside to the line, so
the corridor the planner sweeps is twice the one the regiment will actually occupy.
Rung one fails, rung two fails, rung three answers. Every question the planner asked
was asked of `unit.Facing` — the front it is standing on now, which it is already in
the act of shedding.

**Why it was always the cavalry.** At 3°/s with a 120° wheel it spends forty seconds
of every re-order badly misaligned, so it sits in the broadside window far more of
the time than infantry that is not being re-ordered constantly. Nothing about the
rule was cavalry-specific; the exposure was.

**Predates the M23 pass**, which is worth saying plainly — the leg-aware clearance
added then made the planner ask the *right* question about legs two onward while
still asking the wrong one about leg one. Fixing it needed M23 first: only once a
regiment genuinely comes round onto every leg is "the front it will hold" a thing
the planner can know.

Rung-3 frequency across the six comparison crowds is unchanged for the two repairs
(2 of 6) and improved for the baseline (5 → 4), so nothing was traded for it.

---

## 14. Cavalry goes right through units, again — FIXED ([M25](DECISIONS.md))

From `logs/battle-20260814-140444.log` and a screenshot, 14 Aug 2026. The tell was
in the coordinates: **all eight press-throughs set off from the same forty metres
of ground** — (156..213, 149..171) — and the thing in the way was always a regiment
the cavalry was drawn up beside. On screen that is the cluster where Horse Archers,
Archers, Cavalry and Swordsmen are printed on top of one another.

**a. The planner could not leave a formed line.** A line stands shoulder to
shoulder; that is what a line is and what [M2](DECISIONS.md) exists to permit.
Square a regiment onto a new bearing in the middle of one and its rectangle laps
its neighbours before it has moved a metre — a 40 × 20 body turned 50° reaches
21.7 m along the old axis where it reached 10. Every candidate leg then reported a
collision at distance zero, so rung two found nothing on either side and rung three
answered. It reproduces **at every bearing of the compass**, which is what marks it
out from finding 13's 90° hole.

**b. Excusing an overlap outright deadlocks a swap.** Written without its second
half, M25 let two regiments ordered onto each other's ground each decide the other
was merely where it was standing; both planned straight on, neither yielded, and
they leant together for the rest of the game. Caught by
`TwoRegimentsSwappingPlacesDoNotDeadlock`, which is much older than any of this —
**verified by stashing**, since it passes at `d30d5dc` and failed in the tree. The
line is which way the body lies: abreast or behind is ground you are leaving.

**c. The way round was chosen on arrangements nobody plays in — FIXED.** With the
planner able to see out of a line at all, the default still could not get out of
one. This is the evidence the last two passes said they were waiting for:

```
                           crowds  pressed  out of a line
  past the first thing        2       4          0
  past everything in the way  4       2          6
  round, and round again      4       2          1     <- was the default
  stand off, or bend again    4       2          6     <- is now
```

Swapping to *past everything* wholesale only moved the failure: among scattered
bodies, standing off far enough to clear the first lands on the third. Picking
either is picking which half of the game to be wrong in, which is what the table
was built to stop — so the fourth candidate runs both and takes the cheaper, and is
the only one that is never worst. The formed-line table is now a test that runs.

**What it bought.** A solid wall two blocks either side is now **gone round** rather
than shouldered through, and rung three is reached only when the wall runs to the
edge of the field. Four tests had to be given genuinely impassable arrangements
because their old ones were no longer impassable — which is the fix, not a
regression. Crabbing is **not** dead: measured across five gap-and-depth
arrangements it still wins wherever it is cheaper.

**d. A test was asserting arithmetic, not a rule.** `AShortCrabBeatsALongArch...`
named which way each of two walls should be passed. Both namings were guesses: on
one of them the two answers measure 332 s and 328 s, a margin of one per cent. It
is now `RungTwoTakesWhicheverOfArchingAndCrabbingIsCheaper`, which computes both
candidates and asserts the plan matches the cheaper — the rule M22a actually
states, rather than today's rounding.

---

## 15. Setting off from inside its own — FIXED. The stutter — NOT REPRODUCED

From `logs/battle-20260814-144010.log`, 14 Aug 2026. The turning is reported good.
Two things left.

**a. "It still goes right through archers" — FIXED ([M25a](DECISIONS.md)).** Five
press-throughs, four of them the same regiment against the same Archers, and each
set off from ground it had just finished a march on:

```
3388   reached its destination ... averaging 3,7 m/s
3398 > pushing through its own Archers — no way round it and no gap to thread.
3398   marching from (140,146) to (306,308) — that line is 44°.
```

A regiment that comes to rest lapping one of its own and is then sent off past it
cannot plan a way round, because every candidate leg starts inside the very body it
is trying to get round. Reproduced at an overlap of 0.13 of a regiment.

[M25](DECISIONS.md) excuses a lapped body **abreast or behind**, which is what stops
two regiments swapping places from walking through each other — and *ahead* is
exactly the case it declines to excuse. So the rungs now read it differently, which
they must: the straight line still refuses a lapped body ahead; the legs of a detour
ignore whatever is lapped at their start. The swap deadlock test still passes, which
is the guard that this did not simply undo M25.

**b. "They still stutter a bit when they would hit the edges of some units" — open,
and honestly so.** The suspicion was the berth, which is edge-triggered: it fires on
the step that would *newly* close inside it, so it looks like it should shimmy —
refuse the step in, edge out, be clear, step in again, every other tick.

A second threshold to hold the passage open until there was real daylight was
written and measured. On the only arrangement that squeezes past anything it gave
**116 ticks of sideways movement with 5 changes of direction, against 78 and 3
without it** — worse on both counts. It was thrown away rather than shipped on the
strength of a plausible story.

One real part of it is fixed. *"And that held for 15 ticks (9 times over)"* is a
decision **reported** nine times, not taken nine times; the detour now says itself on
the tick it is settled, as every other decision does. That alone may be most of what
was read as a stutter in the log, though not necessarily what was seen on screen.

`SqueezingPastACornerIsOneMovementRatherThanAStutter` is kept as a **behaviour guard
that does not discriminate** — it passes with the fix in and out, and says so in its
own comment. The next play-test is the verification. What would settle it is a note
of *which* regiment and roughly when, so the coordinates can be pulled out of the
log the way findings 13 and 14 were.

---

## 16. Two things the new logging measured on its first run

Not reports — [W6](DECISIONS.md) was built, and these fell out of watching it run
against `LogVolumeTests`' twelve-turn battle. Both are recorded because they are
cheap to lose and neither is in scope for the logging pass.

**a. A pair hovering at the grazing tolerance opens and closes a collision every
tick or two — and that is a candidate mechanism for finding 15b.** The shuffle eases
two overlapping bodies apart at 1.5 m/s each, [M2](DECISIONS.md) ignores 5% or less
of contact, and a march is pushing back in the whole time. So the overlap crosses the
threshold, the shuffle stops, the march closes it again, and the pair oscillates
across the line. In the recording it read as a burst of one-tick collisions where
there was in fact one fifty-tick shove.

The **log** was given hysteresis for it — a collision opens above the tolerance and
is not called closed until the two are genuinely apart — and that is reporting only:
the rule below it still turns on the tolerance alone, unchanged. Which means the
oscillation is still there in the movement, invisible again, and it is the right
shape for *"they still stutter a bit when they would hit the edges of some units"*.
The obvious next move is to give the **rule** the same hysteresis and measure it the
way the berth attempt was measured — remembering that the berth attempt measured
worse and was thrown away.

**b. An attack's final approach falls to the search on every re-plan.** Rung 2 is
skipped for `OrderKind.Attack` by design (closing with an enemy is [O5](DECISIONS.md)'s
business), so once the straight line fails there is nothing between it and the
pathfinder. Measured: one pair of swordsmen closing the last hundred metres called
the search ten times in twelve turns, at 308, 257, 229, 186, 159, 74 and 67 cells.
That is precisely what [M10](DECISIONS.md) exists to avoid, on the one manoeuvre
where it happens most. It was invisible until the fall-through learned to say itself.

---

## Older debts, not from this sweep

Tracked here only so this file is the one place to look. These are all in the
task list and none is a regression.

| # | Debt | Pinned by |
|---|---|---|
| 16 | Enveloping — a large regiment splitting to surround a small one | `NotYetBuiltTests` (3 skipped) |
| 25 | Blind artillery firing at remembered positions | `NotYetBuiltTests` |
| 26 | Special unit types | — |
| 29 | Splitting regiments into bodies of ~500 | — |
| 31 | Player-set formation depth at deployment | `NotYetBuiltTests` |
| 41 | Split the collision block from the fighting frontage | findings 1b, 2 and 5 above |
| 42 | Tiredness | `NotYetBuiltTests.MenTireOverALongEngagement` |

---

## How to close an entry

1. Un-skip the test named in the entry and watch it fail for the stated reason.
   Some entries name no test, because asserting either behaviour would settle a
   question nobody has answered — those want a decision first, then a test.
2. Fix it.
3. Delete the entry from this file.
4. When the file has no entries left, delete the file.

## What keeps going wrong

Not findings; the shape the findings keep taking. Worth reading before adding a
rule to this system.

- **A threshold placed on its own common case.** Three bearing dead zones and one
  face-capacity rule, all of which fired on exactly the situation they were meant
  to judge. Finding 5 is the current instance.
- **A discrete decision fed by a continuous, changing quantity.** The deeper
  version of the same thing, and the one that keeps being missed: moving the
  threshold does not help, because the input still crosses it eventually.
- **Counting the same thing twice in two different rules.** Finding 4: geometry
  divides the defender's frontage, and then the combat rule divides it again.
- **Asking a point-shaped question of a rectangle.** The founding idea of this
  codebase, and still the most common fault in it — centre-to-centre distance and
  centre-to-centre bearing both say almost nothing about a body 40 m wide and
  6 m deep. Finding 4's second half had `OffFront` scoring two regiments as
  flanking each other while they stood square on.
- **Stopping at the first improvement.** Removing the divisor took two attackers
  from 2.3× to 1.4× and looked like a fix. It was half of one. Measure again
  after the change, against the number you actually wanted.
- **Tests that measure after the battle has moved on.** Six times now, a test has
  read a state that only exists mid-fight — how many regiments are in contact —
  after the defender had broken and the pursuit had scattered everyone. Catch the
  moment with `RunUntil`, watch turn by turn, or hold the defender's nerve so the
  question being asked is the one you meant to ask.
- **Diagnosing from one trace and then guessing.** Finding 1a was written up as a
  fault in `FollowTarget` on the strength of a single run, two attempts were made
  at it, and it turned out to be finding 5 one system away. A symptom is real
  evidence; a cause needs more than one look.
