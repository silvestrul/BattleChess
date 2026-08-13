# Decisions

How Battle Chess is meant to work, as settled by the designer. This is the
checklist the code is audited against — if the code and this file disagree, the
code is wrong.

Unlike [OPEN-FINDINGS.md](OPEN-FINDINGS.md), **this file is permanent**. Entries
are superseded and rewritten, never deleted for being done.

**Non-negotiable** marks a decision that must not be quietly adapted. Everything
else is subject to the standing instruction: *"do not take everything i say as
absolute law you need to adapt it and make sense"* — but an adaptation gets said
out loud, not assumed.

Status: ✅ in the code · ⚠️ partly · ❌ not yet.

---

## The shape of a regiment

| | Decision | Status |
|---|---|---|
| S1 | A regiment is a rectangle on the ground with a free bearing, not a token. Every question about nearness asks the rectangle. | ✅ |
| S2 | **Drawn 2:1** — width to depth — whatever the real depth is. A regiment 40 m wide is drawn 20 m deep. Replaces "2.5× depth", which replaced two rounds of "double the thickness". Stated as a shape so a change to the real depth cannot silently redraw the army. | ✅ |
| S3 | The drawn rectangle **is** the collider — the whole of it. What you see is what blocks, what is clicked, and what holds ground. It makes no sense in metres and is the better answer anyway, because the alternative is a regiment that looks flush with its neighbour while its collider is 14 m away. **Non-negotiable.** | ✅ |
| S4 | The rectangle never shrinks as men die — it is the same size on the last turn of a battle as the first, unless the regiment is wiped out entirely. Casualties reduce how many can **fight**, not how much room the body takes up. | ✅ |
| S5 | Real spacing is unchanged by any of this — the men are still a metre apart and ten deep. The rectangle is a visual and physical convenience, not a claim about ranks. | ✅ |
| S5a | Two shapes, and only two. The **block** (`Footprint`) is 2:1 and constant: collision, blocking, zones of control, selection, drawing. The **space** (`Space`) is the real ground the men stand on, 40 m by 6 m, and it shrinks: it is what the fighting rules measure. A rule that reads the block for a question about men will silently answer with a drawing convention. | ✅ |
| S6 | **Names.** A regiment's real ground is its *space* — say 100 m by 10 m, being 100 men a metre apart in 10 ranks. Its **frontage** is the line the first rank makes, which is the long side of the rectangle. The short ends are its **sides**. "Block", "footprint" and "fighting frontage" are not the vocabulary; frontage and sides are. | ⚠️ code still says footprint |

## Moving

| | Decision | Status |
|---|---|---|
| M1 | Friendly regiments never share ground. | ✅ |
| M2 | Contact of 5% or less is ignored, so a line can stand shoulder to shoulder — "glued", as in the Total War videos. | ✅ |
| M3 | A move faces its line of march, at **any** distance and any angle. No dead zone. Three attempts at a threshold were three bugs. | ✅ |
| M4 | A drawn bearing is the front to **arrive on**. The regiment marches the way it is going, at full pace, and comes round at the end — the same manoeuvre an attack makes on its last hundred metres. *Supersedes "holding a front across a move must be asked for by drawing the bearing", which held the drawn front from the first step: a regiment sent 167 m travelled rear-first the whole way at 29% pace, which reads as a fault rather than an order.* | ✅ |
| M4a | Crabbing along holding a front regardless of the line of march — a fighting withdrawal facing the enemy — is therefore no longer reachable. It was only ever a side effect of M4's old form. Wants its own control if it is wanted at all. | ❌ open |
| M5 | Turning on the spot is never blocked by collision — otherwise a flanked regiment can never answer. Movement is still blocked if the path is. | ✅ |
| M6 | An order always ends: a regiment that cannot reach the exact point walks to the nearest usable ground and says so. | ✅ |
| M7 | Bound regiments move as one, at the pace of whichever is on the worst ground. | ✅ |
| M8 | Box-selecting several regiments groups them temporarily, with no button pressed. | ✅ |
| M9 | Each regiment pathfinds individually, so a wing does not walk into itself. | ✅ |

## Fighting

| | Decision | Status |
|---|---|---|
| F1 | Two arrangements only: square on to the enemy's front, or perpendicular against a flank. Nothing between. | ✅ |
| F2 | Flanking is earned by getting round the side first, not by approaching at an angle. | ✅ |
| F3 | **Two regiments to a face, four faces.** A third on a full face waits behind it. | ✅ |
| F4 | Overflow is **never** reassigned to another face. Going round is an order the player gives. | ✅ |
| F5 | **Frontage is refilled from reserves.** A regiment waiting behind the line steps into a place the moment one opens — because the man in front fell, or the regiment in front broke. **Non-negotiable.** | ✅ |
| F6 | A face is worth **one full frontage** however many press it. Frontage is divided by the ground each enemy actually shares, not by how many there are: attackers on 60 m and 40 m of a 100 m front deal and take 60 and 40. | ✅ |
| F7 | What a second regiment buys is the defender's nerve, not his blood — more morale inflicted, less taken for having company. | ✅ |
| F8 | The first rank fights fully; ranks 2–3 contribute less; the rest are there to resupply the front. | ✅ |
| F9 | A dead soldier is never replaced. Losses are permanent. | ✅ |
| F10 | Cavalry breaks through infantry, but never through cavalry and never through spearmen. | ✅ |
| F11 | A regiment can withdraw from a fight unless fully encircled, taking casualties in proportion to how much of it is gripped, and refused outright past ~85%. | ❌ finding 2 |
| F12 | Tiredness from 0 to 1, up to −75% damage, worst in the front rank, some for waiting under stress. Lowers morale too. | ❌ #42 |
| F13 | **Ranks refill the front rank.** A man who falls in the first rank is replaced from the second, and the second from the third. Not instantly — it takes ticks, so a line under heavy fire is genuinely thinner than its headcount says — but as fast as it can be. The regiment-level twin of [F5](#fighting). | ✅ |
| F14 | **Casualties must not round away.** Damage below half a man killed nobody, so a weak attack did literally nothing however long it lasted — twenty archers fought a spear block for thirty turns and killed **none**. The remainder is now carried on the regiment being hit and paid on a later pulse, in melee and shooting alike. Finer pulses turned out not to be needed. | ✅ |
| F15 | **A flank engages a side, not a frontage.** The defender answers with its **side** — its depth, ten men for a ten-rank body. The attacker brings **twice that**, capped by its own frontage: twenty against ten. Not its whole hundred, because a side is not a frontage; not merely ten, because ten men doing a tenth of a frontal attack is not what being taken in the flank feels like. **Counted in men, never in drawn metres** — the rectangle is 2:1 for the eye ([S2](#the-shape-of-a-regiment)) and says nothing about ranks ([S5](#the-shape-of-a-regiment)), so a rule that read its depth would make a flank as strong as a front the moment the drawing changed. | ⚠️ the defender's half is in — `MenTurnedThatWay` caps it at its ranks. The attacker still brings its own frontage rather than twice the defender's ranks. |
| F16 | **Flanking is shock, and shock wears off.** At the moment of contact the flanked regiment deals **0.25×** and takes **2.0×**. Both slide back to **1.0×** as it recovers. This is what makes a flank a moment to exploit rather than a permanent state — the reward is for getting round *and* finishing the job. | ❌ new |
| F17 | **Turning to face cancels the shock outright**, resetting to 1.0× rather than waiting out the recovery. Coming about takes many ticks, so it is a real decision under fire and not a free escape — but it is always the right answer, and the rules should reward a player who spots the flank in time. | ❌ new |
| F18 | A **shallow** regiment taken in the flank suffers worse **morale**, not worse casualties. Few ranks means little to absorb the blow: the men can see there is nothing behind them. | ❌ new, threshold to pick |

## Orders and pursuit

| | Decision | Status |
|---|---|---|
| O1 | Attack orders align centres — the attacker's middle goes for the defender's middle. | ✅ |
| O2 | Archers and horse archers told to attack stop at shooting range. They shoot; they do not charge home. | ✅ |
| O3 | Defenders do not turn to face attackers unless told to. Neither do attackers. | ✅ |
| O4 | **Pursuit only when ordered to attack.** A regiment that was marching and got cut off halts where the fight ends, even though its order was to go further — it neither chases nor resumes the march. Re-ordering it is the player's job. An ordered attack still runs its enemy down, with no leash. | ✅ |

## Commanding

| | Decision | Status |
|---|---|---|
| C1 | Total War mouse layout: left drags a selection box, right orders, middle pans. | ✅ |
| C2 | A bind button ties regiments into a wing that keeps its shape. | ✅ |
| C3 | Right-drag draws a line; the regiment arrives facing perpendicular to it. | ✅ |
| C4 | The player can set formation depth at deployment. | ❌ #31 |

## Later — balance

Not decisions yet. Raised deliberately as things to settle in one balance pass
rather than piecemeal, so the numbers can be moved against each other.

| | To settle |
|---|---|
| B1 | Spearmen hit for less per man, but the **second rank attacks too**, and they may form tighter than ordinary foot. Three knobs on one unit — worth doing together, and worth doing after [F13](#fighting) exists, since "the second rank fights" only means something once ranks are modelled. |
| B2 | How much ranks 2 and 3 contribute in general. Currently a flat 0.3 for two supporting ranks. |
| B3 | Spearmen's steadiness per man lost sits at 0.894 of swordsmen's against a bar of 0.85, since [F13](#fighting). The margin was always thin. Its test is skipped rather than nudged; settle it with B1. |
| B5 | Archers against horse archers no longer resolves — 28% against 10% and still going at the fifteen-turn cap. Two shooting units with no reason to close now grind honestly, where before the duel was settled by rounding artefacts: one side's chip damage landed and the other's evaporated. Wants ammunition, or a reason to close, or an accepted draw. Surfaced by [F14](#fighting), not caused by it. |
| B4 | Depth only tells at the cliff. A regiment with one rank left cannot refill at all; one with two refills exactly as well as one with ten. That is a discrete answer to a continuous question, which is a shape this project has been bitten by four times. Wants [F18](#fighting)'s threshold thinking. |

## How to work

| | Decision |
|---|---|
| W1 | *"Do not take everything i say as absolute law — you need to adapt it and make sense."* Adaptations are said out loud. |
| W2 | *"When you think you are misunderstanding something just ask me to confirm."* Two defensible readings means ask. |
| W3 | Verify a reported bug by reproducing it before changing anything. |
| W4 | Small, system-sized passes. Discuss the rule before writing the code. |
