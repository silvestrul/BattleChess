# Open findings

Things the test sweep turned up that are known, reproducible and **not yet
fixed**. Each entry names the test that pins it, so the entry and the test can
be deleted together once the fix lands.

**This file is scaffolding.** When it is empty, delete it.

Raised by the manoeuvre sweep of 13 Aug 2026 (`c02380d`), which added 85 tests
over movement, rotation, grouping and attack orders.

---

## ~~1. A third regiment sent at one enemy does nothing~~ — FIXED

Closed by the two-per-face rule. Kept here only until the two leftovers below
are closed too, because they are the same piece of ground.

**The rule, as decided.** A face is worth **one full frontage of fighting**
however many regiments are pushed onto it, because damage is dealt across the
ground two bodies genuinely share and two attackers divide that ground between
them. So a pair on the front deal together what one deals alone, each taking
half — and buy the defender's nerve rather than his blood. Four faces at two
apiece is six or eight regiments landing four frontages of damage.

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

Now pinned live by `AThirdRegimentSentAtTheSameEnemyMustNotMakeTheAttackWorse`,
`AThirdRegimentWaitsBehindTheLineRatherThanShovingIntoIt`,
`ARegimentOrderedAtAFullFrontDoesNotWanderRoundToTheFlankByItself` and
`AFaceDoesNotStopHoldingTwoJustBecauseTheDefenderHasLostMen`.

---

## 1a. A reserve does not walk into the place that opens for it

**Severity:** medium. The queue is right; the regiment does not act on it.

**Pinned by:** `AttackOrderScenarioTests.AReserveStepsIntoTheLineWhenTheRegimentInFrontOfItIsGone`
(skipped).

The queue is rebuilt every re-plan out of the regiments still fighting, so when
one of the two in the line is destroyed the reserve's slot genuinely moves up.
The regiment does not follow it. It sits about thirty metres out and stays there,
because a unit that is neither marching nor in contact has nothing that triggers
a fresh approach.

Scoping the `HeldUpBy` guard in `FollowTarget` so a halted, non-fighting regiment
re-plans on the interval was tried and is **not** the blocker — the behaviour was
unchanged, so that change was reverted rather than shipped on spec. Wants its own
pass and a trace, not another guess.

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

## 3. An explicit attack order has no pursuit leash

**Severity:** low, and it is a design question rather than a defect. **Needs a
decision before it is worth changing.**

**Not pinned by any test**, deliberately — asserting either behaviour would lock
in an answer nobody has chosen yet.

**Behaviour.** `PursuitLeashMetres` (200 m) is checked in `TryEngageNearby`, the
path an Aggressive regiment takes when it goes looking for a fight on its own.
`FollowTarget`, which carries out an attack order the player actually gave, does
not check it. Measured: a swordsmen regiment ordered onto a scout followed it
552 m across the field and was still going.

**The argument for leaving it.** The player gave the order. A regiment that
quietly abandons an order because it decided the chase was too long is worse
than one that obeys.

**The argument for capping it.** One scout can walk any regiment out of the
battle for free, and the order never ends — which is the one thing
`OrdersAlwaysEndTests` exists to prevent. The player who issued it has usually
stopped watching.

**Middle option.** Keep following, but report it: a regiment past its leash on
an explicit order says so in the log, so the player finds out while it still
matters.

The leash that *does* work is pinned by
`AttackOrderScenarioTests.ARegimentLookingForAFightIsNotBaitedOutOfPositionByOneItCannotCatch`.

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
| 41 | Split the collision block from the fighting frontage | findings 1 and 2 above |
| 42 | Tiredness | `NotYetBuiltTests.MenTireOverALongEngagement` |

---

## How to close an entry

1. Un-skip the test named in the entry and watch it fail for the stated reason.
2. Fix it.
3. Delete the entry from this file.
4. When the file has no entries left, delete the file.
