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
| S3 | The drawn rectangle **is** the collider. What you see is what blocks, what is clicked, and what holds ground. | ❌ #41 |
| S4 | The block never shrinks as men die. Casualties reduce how many can **fight**, not how much room the body takes up. | ⚠️ `FootprintAtFullStrength` exists and face capacity uses it; collision and frontage still use the live one |
| S5 | Real spacing is unchanged by any of this — the men are still a metre apart and ten deep. The rectangle is a visual and physical convenience, not a claim about ranks. | ✅ |

## Moving

| | Decision | Status |
|---|---|---|
| M1 | Friendly regiments never share ground. | ✅ |
| M2 | Contact of 5% or less is ignored, so a line can stand shoulder to shoulder — "glued", as in the Total War videos. | ✅ |
| M3 | A move faces its line of march, at **any** distance and any angle. No dead zone. Three attempts at a threshold were three bugs. | ✅ |
| M4 | Holding a front across a move is a real order and must be asked for, by drawing the bearing. | ✅ |
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

## How to work

| | Decision |
|---|---|
| W1 | *"Do not take everything i say as absolute law — you need to adapt it and make sense."* Adaptations are said out loud. |
| W2 | *"When you think you are misunderstanding something just ask me to confirm."* Two defensible readings means ask. |
| W3 | Verify a reported bug by reproducing it before changing anything. |
| W4 | Small, system-sized passes. Discuss the rule before writing the code. |
