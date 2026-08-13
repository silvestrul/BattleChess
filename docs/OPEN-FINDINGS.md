# Open findings

Things the test sweep turned up that are known, reproducible and **not yet
fixed**. Each entry names the test that pins it, so the entry and the test can
be deleted together once the fix lands.

**This file is scaffolding.** When it is empty, delete it.

Raised by the manoeuvre sweep of 13 Aug 2026 (`c02380d`), which added 85 tests
over movement, rotation, grouping and attack orders.

---

## 1. A third regiment sent at one enemy does nothing — and costs the attack

**Severity:** high. It inverts the point of concentrating force, and it
contradicts the design decision that several regiments ordered onto one should
share its front.

**Pinned by:** `AttackOrderScenarioTests.AThirdRegimentSentAtTheSameEnemyMustNotMakeTheAttackWorse`
(skipped). Its live counterpart `TwoRegimentsSentAtOneBothGetAHoldOfIt` passes.

**Measured**, three swordsmen regiments sent at one body of spearmen from 240 m,
fourteen turns, seed 30650:

| Attackers | Most ever in contact at once | Defender losses |
|---|---|---|
| 1 | 1 | 325 |
| 2 | 2 | broke and ran early |
| 3 | **1** | **326** |

**Mechanism.** `PlaceInTheAttackingLine` divides the defender's 40 m of front
among the attackers that agreed on the same face — three slots of about 13 m.
Every regiment in the game is 40 m wide, so each needs the whole front. The
friend-avoidance rules then push them apart, and the outer two settle 6–9 m from
the defender: past the slot they were given, but outside the 4 m contact range.
They stand there for the rest of the battle. Only the middle regiment fights, so
three attackers produce what one produces.

**Why it is not fixed here.** This is the uniform-rectangle problem, not an
attack-rules problem, and task **#41 (split the collision block from the fighting
frontage)** restructures exactly this ground. A patch to the slot spacing now
would be thrown away by it, and would probably do it by letting regiments
overlap — which is the thing the crowding rules exist to prevent.

**What the fix has to satisfy.** Reinforcing an attack must never reduce how many
regiments are in contact. That is the assertion in the skipped test; un-skip it
when #41 lands.

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
