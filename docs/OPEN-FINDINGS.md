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

**Mostly closed by crabbing, and the residue is specific.** Threading a gap side-on
went from *never getting through* to arriving with **two** spurious stall reports,
down from four. What is left is one turn, not a class of fault: turning **onto** a
crabbed leg is now excused, because the route says that leg wants a front and the
regiment is plainly coming round onto it; turning **off** it at the far side is
not, and that is the same ninety degrees against the same fifteen ticks of
patience.

Excusing *any* coming-round rather than only a crabbed leg was tried and **breaks
M6**. A regiment sent onto its own troops presses against them, asks which way the
waypoint under its feet lies, gets noise for an answer, and is forgiven for
standing there for ever — which is precisely the seizure that rule exists to end.
Guarding the bearing on distance did not rescue it either. Reverted rather than
tuned at; the honest shape of the fix is probably for a leg to name the front it
ends on as well as the one it holds.

**What this means for the plan.** M13 and the [M10](DECISIONS.md) stall rework are
*mutually* dependent, not sequential — the ordering in task #43 had it as sidestep
first, then the stall fix, and that is wrong in both directions. They are one pass,
and the design question to settle first is what "progress" means for a regiment that
is deliberately going the wrong way for a while.

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
