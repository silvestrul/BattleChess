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

**Requirement types.** `S` shape · `M` movement · `C` combat · `O` orders ·
`GUI` interface and controls · `B` balance · `T` technique · `W` how to work.

**Priority.** *Mandatory* — the code is wrong if it disagrees. *Preferred* — the
rule is right but the number, threshold or exact form is the designer's to move.

**Status.** ✅ in the code · ⚠️ partly · ❌ not yet · 🔜 next · ⏸ dormant ·
🟡 measured, not yet fixed.

---

## Shape

| | Requirement | Priority | Status |
|---|---|---|---|
| S1 | A regiment is a rectangle on the ground with a free bearing, not a token. <sup>[why](#s1)</sup> | Mandatory | ✅ |
| S2 | Drawn 2:1 <sup>[why](#s2)</sup> | Mandatory | ✅ |
| S3 | The drawn rectangle **is** the collider — the whole of it. <sup>[why](#s3)</sup> | Mandatory | ✅ |
| S4 | The rectangle never shrinks as men die — it is the same size on the last turn of a battle as the first, unless the regiment is wiped out entirely. <sup>[why](#s4)</sup> | Mandatory | ✅ |
| S5 | Real spacing is unchanged by any of this — the men are still a metre apart and ten deep. <sup>[why](#s5)</sup> | Mandatory | ✅ |
| S5a | Two shapes, and only two. <sup>[why](#s5a)</sup> | Mandatory | ✅ |
| S6 | Names. <sup>[why](#s6)</sup> | Mandatory | ⚠️ code still says footprint |

## Moving

| | Requirement | Priority | Status |
|---|---|---|---|
| M1 | Friendly regiments **should not** share ground — and this is a strong preference, not an invariant. <sup>[why](#m1)</sup> | Preferred | ✅ demoted, and [M18](#moving) rung 3 is what replaces it. |
| M1a | A regiment that was not told to move does not move. <sup>[why](#m1a)</sup> | Mandatory | ✅ |
| M2 | Contact of 5% or less is ignored, so a line can stand shoulder to shoulder — "glued", as in the Total War videos. | Mandatory | ✅ |
| M3 | A move faces its line of march, at **any** distance and any angle. <sup>[why](#m3)</sup> | Mandatory | ✅ |
| M4 | A drawn bearing is the front to **arrive on**. <sup>[why](#m4)</sup> | Mandatory | ✅ |
| M4a | Crabbing along holding a front regardless of the line of march — a fighting withdrawal facing the enemy — is therefore no longer reachable. <sup>[why](#m4a)</sup> | Mandatory | ❌ open |
| M5 | Turning on the spot is never blocked by collision — otherwise a flanked regiment can never answer. <sup>[why](#m5)</sup> | Mandatory | ✅ |
| M6 | An order always **resolves**. <sup>[why](#m6)</sup> | Mandatory | ⚠️ gives up rather than waits |
| M7 | Bound regiments move as one, at the pace of whichever is on the worst ground. | Mandatory | ✅ |
| M8 | Box-selecting several regiments groups them temporarily, with no button pressed. | Mandatory | ✅ |
| M9 | Each regiment pathfinds individually, so a wing does not walk into itself. | Mandatory | ✅ |
| M10 | A march is a cast, not a search. <sup>[why](#m10)</sup> | Mandatory | ❌ new |
| M11 | Routes are rebuilt on a cadence, never every tick, and never only once. <sup>[why](#m11)</sup> | Mandatory | ⚠️ `RepathIntervalTicks = 5` exists and is being bypassed — finding 7 |
| M12 | The rectangle is what travels, not a point. <sup>[why](#m12)</sup> | Mandatory | ⚠️ the cast asks the whole body; the fallback search still uses 2 m. |
| M13 | A regiment can crab. <sup>[why](#m13)</sup> | Mandatory | ✅ |
| M14 | Full width first, crabbing second. <sup>[why](#m14)</sup> | Mandatory | ✅ |
| M15 | An obstacle is whatever will not get out of your way. <sup>[why](#m15)</sup> | Mandatory | ❌ new |
| M15a | Stance decides whether an enemy is a wall. <sup>[why](#m15a)</sup> | Mandatory | ⚠️ **adapted, and further than this says.** In practice enemies are not planning obstacles at *any* stance. As walls they had charges arrive by walking round the regiment they were sent to break — five tests at once — but the real reason is [M4](#moving)'s: a route that quietly avoids an enemy overrules the line the player drew, and whether to cross a formed enemy's front is the last decision that should be taken out of their hands. |
| M16 | Ask where a body will be, not where it is. <sup>[why](#m16)</sup> | Mandatory | ❌ new |
| M16a | Friends are predicted from their orders; enemies only from what has been seen of them. <sup>[why](#m16a)</sup> | Mandatory | ❌ new |
| M16b | Only one of any two regiments gives way. <sup>[why](#m16b)</sup> | Mandatory | ❌ new |
| M17 | The best line, judged on what the player did not ask for. <sup>[why](#m17)</sup> | Mandatory | ❌ new |
| M18 | Four things to try, in order, and only in order. <sup>[why](#m18)</sup> | Mandatory | ⚠️ rungs 1–3 in. Rung 4 waits on nothing being built: with rung 3 available, a march only fails on ground or enemies, and [M6](#moving)'s relocation already answers that. |
| M19 | Cast the whole ray, then look at what is along it. <sup>[why](#m19)</sup> | Mandatory | ❌ new |
| M19a | Room to spare when aiming at a tangent. <sup>[why](#m19a)</sup> | Mandatory | ❌ new |
| M19b | The search is called after a full fan fails twice running. <sup>[why](#m19b)</sup> | Mandatory | ❌ new |
| M20 | Being inside your own men costs pace. <sup>[why](#m20)</sup> | Mandatory | ✅ |
| M21 | A detour is committed until the thing it went round is behind you. <sup>[why](#m21)</sup> | Mandatory | ✅ |
| M22 | A route is costed in seconds, not metres. <sup>[why](#m22)</sup> | Mandatory | ✅ |
| M22a | Rung 2 compares its two answers; the ladder itself does not. <sup>[why](#m22a)</sup> | Mandatory | ✅ |
| M23 | A march comes round onto every leg, not once onto the whole route. <sup>[why](#m23)</sup> | Mandatory | ✅ |
| M24 | The planner asks about the shape that will travel, not the one standing there. <sup>[why](#m24)</sup> | Mandatory | ✅ |
| M25 | A body you are already standing in is not in your way — unless it is in front of you. <sup>[why](#m25)</sup> | Mandatory | ✅ |
| M25a | Being inside something is why a way round is wanted, not a reason there cannot be one. <sup>[why](#m25a)</sup> | Mandatory | ✅ |
| M27 | A gap between two regiments is a corridor, and it has an axis of its own. <sup>[why](#m27)</sup> | Mandatory | ✅ |
| M28 | Where a route may bend is the corners of what is in the way — not a point guessed perpendicular to the line of march. <sup>[why](#m28)</sup> | Mandatory | ✅ |
| M29 | A leg checked at a front is not walked into until that front has arrived — and the waiting happens on the step that would have hit, not at the mouth of the leg. <sup>[why](#m29)</sup> | Mandatory | ✅ |
| M30 | [M29](#moving) applies to every leg, not only the ones that name a front. <sup>[why](#m30)</sup> | Mandatory | ✅ |
| M32 | A body earns candidate places by refusing a leg — built, measured twice, and switched off. <sup>[why](#m32)</sup> | Preferred | ⏸ dormant |
| M33 | The ladder is a floor under the search, never a ceiling over it. <sup>[why](#m33)</sup> | Mandatory | ✅ |
| M34 | What a march costs is legs priced, and legs priced is places squared. <sup>[why](#m34)</sup> | Mandatory | 🔜 |
| M36 | Tangency prunes legs at corners, and only at corners. <sup>[why](#m36)</sup> | Mandatory | ✅ |
| M37 | What a plan costs is not what a plan searched. <sup>[why](#m37)</sup> | Mandatory | ✅ |
| M38 | A halted attacker re-plans every tick, and that is the freeze. <sup>[why](#m38)</sup> | Mandatory | ✅ |
| M39 | Re-plan when the answer could have changed, and otherwise on a cadence the route sets itself. <sup>[why](#m39)</sup> | Preferred | 🔜 |
| M40 | The frames were stopping for litter, not for work. <sup>[why](#m40)</sup> | Mandatory | ✅ |
| M41 | A second, independent route planner: Hybrid A* over (x, y, heading) — tried, fixed, and found worse. <sup>[why](#m41)</sup> | Mandatory | ✅ (tried, not adopted) |
| M42 | A review of M41 found the sweep was checking poses the body never occupied — and "90/90 found" was never "90/90 legal". <sup>[why](#m42)</sup> | Mandatory | ✅ |
| M43 | "Which kind of slow" was the right question and neither answer was the right one — and half the gate's angles were never a speed problem at all. <sup>[why](#m43)</sup> | Mandatory | ✅ (measured, not adopted) |
| M44 | A profiler capture could not answer where the lag is, because nothing in the project was named for it. <sup>[why](#m44)</sup> | Mandatory | ✅ |
| M45 | A regiment's size ceiling belongs to the battle, not only to the troop type. <sup>[why](#m45)</sup> | Mandatory | ✅ |
| M46 | Three fixes for the stall, two above every planner and one beneath all of them. <sup>[why](#m46)</sup> | Mandatory | ✅ |
| M47 | Three fields built to be dear in three different ways, and a stopwatch on every step of a plan. <sup>[why](#m47)</sup> | Mandatory | ✅ |
| M48 | Eighty orders at once cost 2 499 ms, and two thirds of it is one loop. <sup>[why](#m48)</sup> | Mandatory | 🟡 measured, not yet fixed |
| M49 | The finding is the family's, not the planner's — and the one planner that escapes it is the one the profiler cannot see. <sup>[why](#m49)</sup> | Mandatory | 🟡 measured |
| M50 | The perf review merged, and four of its five search fixes were already here. <sup>[why](#m50)</sup> | Mandatory | ✅ merged |
| M51 | The merge roughly halved planning, and the loop it did not reach is now the biggest one left. <sup>[why](#m51)</sup> | Mandatory | ✅ measured |
| M54 | Thinning the candidate places was already done, and there is nothing left in it. <sup>[why](#m54)</sup> | Mandatory | ❌ no effect |
| M55 | A planner is judged on whether its route can be walked, not on whether it returned one. <sup>[why](#m55)</sup> | Mandatory | ✅ measured |
| M56 | The estimate has to know what turning costs, because turning is most of what a route costs. <sup>[why](#m56)</sup> | Mandatory | ✅ measured |
| M57 | Whether a body can be touched at all is asked once for a whole sweep, not once for every pose in it. <sup>[why](#m57)</sup> | Mandatory | ✅ measured |
| M58 | The lattice bins headings three times finer than any route needs, and paid for it in expansions. <sup>[why](#m58)</sup> | Mandatory | ✅ measured |
| M59 | A cheap route already in hand is refused only for something measured about it, never for its shape. <sup>[why](#m59)</sup> | Mandatory | ✅ measured |
| M60 | The estimate's fill is ordered by buckets, because its costs take three values. <sup>[why](#m60)</sup> | Mandatory | ✅ measured |
| M61 | A tube round the cheap route does not help, because the cheap route is the press-through. <sup>[why](#m61)</sup> | Mandatory | ❌ no effect |
| M62 | Eighty orders given at once are eighty independent questions, and are planned as such. <sup>[why](#m62)</sup> | Mandatory | ✅ measured |
| M63 | Five further ways to make the lattice cheaper, measured and none kept. <sup>[why](#m63)</sup> | Preferred | ❌ no effect |
| M64 | The click that orders a wing works out every route at once and gives every order afterwards. <sup>[why](#m64)</sup> | Mandatory | ✅ measured |
| M65 | A way round is preferred to a press until it costs three times the press. <sup>[why](#m65)</sup> | Mandatory | ✅ measured |
| M66 | Every route the default planner hands out is cast ahead and smoothed, whichever search made it. <sup>[why](#m66)</sup> | Mandatory | ✅ measured |
| M67 | Comparing every planner is a tool for reading one order, not a setting to play with. <sup>[why](#m67)</sup> | Preferred | ✅ measured |
| M68 | The pose search may spend twenty thousand expansions, not a hundred thousand. <sup>[why](#m68)</sup> | Mandatory | ✅ measured |
| M69 | A player's order is worked out off the drawing thread, and the clock waits for it. <sup>[why](#m69)</sup> | Mandatory | ✅ built |
| M70 | Six ways to make the lattice search less, measured and catalogued rather than chosen. <sup>[why](#m70)</sup> | Preferred | 📐 a menu |
| M71 | The search is held near the order, over twelve headings, on a twenty-thousand budget. <sup>[why](#m71)</sup> | Mandatory | ✅ measured |
| M72 | A plan is waited on where it is used, never once a frame. <sup>[why](#m72)</sup> | Mandatory | ✅ built |
| M73 | Casting to the blockers' own corners answers most orders, and the lattice answers no better. <sup>[why](#m73)</sup> | Preferred | 📐 measured, not built |
| M74 | The cast aims eight metres past a corner, and fences nothing in. <sup>[why](#m74)</sup> | Preferred | 📐 measured |
| M75 | The cast and the lattice succeed on the same orders, so neither can bound the other. <sup>[why](#m75)</sup> | Mandatory | ✅ measured |
| M76 | The lattice is bound by the clock, not by a count of expansions. <sup>[why](#m76)</sup> | Mandatory | ✅ shipped |
| M77 | A cell that holds a regiment at any facing costs no heading, and the field is only 2 700 of them. <sup>[why](#m77)</sup> | Preferred | ✅ shipped as a stage |
| M78 | One field for the whole order of battle, and a cell is what its samples say it is. <sup>[why](#m78)</sup> | Preferred | ✅ shipped |
| M79 | The lattice is cut on a count of expansions, not a clock, and a way round is priced against the empty field. <sup>[why](#m79)</sup> | Preferred | ✅ shipped |
| M80 | A route nobody is waiting for any more is thrown away, not finished. <sup>[why](#m80)</sup> | Designer | ✅ shipped |
| M81 | Crabbing is a manoeuvre at a gap, so a crab that runs the journey is declined. <sup>[why](#m81)</sup> | Designer | ✅ shipped |
| M82 | The route preview draws what the planner would walk, straightened, not the raw grid path. <sup>[why](#m82)</sup> | Designer | ✅ shipped |
| M83 | The whole bill, asked of every planner and every lever at once. <sup>[why](#m83)</sup> | Mandatory | ✅ measured |
| M84 | The index asked once a leg instead of once a sample, and the turn field at half the resolution. <sup>[why](#m84)</sup> | Mandatory | ✅ measured |
| M85 | An order costs four microseconds or sixty milliseconds, and the mean describes neither. <sup>[why](#m85)</sup> | Mandatory | ✅ measured |
| M86 | The grid is asked before the tangent search, and the tangent stage is off. <sup>[why](#m86)</sup> | Designer | ✅ shipped |
| M87 | The grid answers at a regiment’s width first, and only the orders it cannot answer pay for a finer one. <sup>[why](#m87)</sup> | Designer | ✅ shipped |
| M88 | A way round may cost up to **three times** the route that presses through — a multiple, never a number of seconds. <sup>[why](#m88)</sup> | Designer | ✅ pinned |
| M89 | If there is room for the body, it fits. <sup>[why](#m89)</sup> | Designer | ⚠️ stated, being measured |
| M90 | A grid route is walked from where the regiment stands, and held ground is priced rather than refused. <sup>[why](#m90)</sup> | Mandatory | ✅ shipped |
| M91 | A leg is walked on the line of march if it can be, and sidewalked if it cannot — priced either way. <sup>[why](#m91)</sup> | Designer | ✅ shipped |
| M92 | A regiment touching one of its own may set off, on the same licence as one overlapping it. <sup>[why](#m92)</sup> | Mandatory | ✅ shipped |
| M93 | A search that spends the frame’s time charges the frame, whether or not it produced a route. <sup>[why](#m93)</sup> | Mandatory | ✅ shipped |
| M94 | A regiment may arrive in the contact it is allowed to stand in. <sup>[why](#m94)</sup> | Designer | ⚠️ built, ships **off** — it loosens the gate |
| M94a | The arriving licence forgives ending in contact and nothing else. <sup>[why](#m94a)</sup> | Mandatory | ✅ shipped |
| M94b | The licence is a last resort, it may not escape the ceiling, and it may never make an answer dearer. <sup>[why](#m94b)</sup> | Mandatory | ✅ shipped — and it is why the fourth cause is closed |
| M95 | The index's halo stays at 128 m: buckets cost more than the bodies the slack lets in. <sup>[why](#m95)</sup> | Mandatory | ✅ measured, unchanged |
| M96 | A pose builds the box it turns out to need, not both of them. <sup>[why](#m96)</sup> | Mandatory | ✅ shipped |
| M97 | `DetourRoomFraction` stays at 0,5. The field it sizes is built on one bench field of four. <sup>[why](#m97)</sup> | Mandatory | ✅ measured, unchanged |
| M88a | The way-round ceiling is **3,1x** a press. <sup>[why](#m88a)</sup> | Designer | ✅ shipped |
| M98 | A press-through is a legitimate answer. What is a defect is walking through somebody **undeclared**. <sup>[why](#m98)</sup> | Designer | ✅ shipped — the approach-angle gate is green |
| M99 | A regiment faces **the direction it moves in the most**. A leg shorter than two of its own body-lengths is a shuffle, not a march, and does not get to name a front. <sup>[why](#m99)</sup> | Designer | ✅ shipped |

## Combat

| | Requirement | Priority | Status |
|---|---|---|---|
| C1 | Two arrangements only: square on to the enemy's front, or perpendicular against a flank. <sup>[why](#c1)</sup> | Mandatory | ✅ |
| C2 | Flanking is earned by getting round the side first, not by approaching at an angle. | Mandatory | ✅ |
| C3 | Two regiments to a face, four faces. <sup>[why](#c3)</sup> | Mandatory | ✅ |
| C4 | Overflow is **never** reassigned to another face. <sup>[why](#c4)</sup> | Mandatory | ✅ |
| C5 | Frontage is refilled from reserves. <sup>[why](#c5)</sup> | Mandatory | ✅ |
| C6 | A face is worth **one full frontage** however many press it. <sup>[why](#c6)</sup> | Mandatory | ✅ |
| C7 | What a second regiment buys is the defender's nerve, not his blood — more morale inflicted, less taken for having company. | Mandatory | ✅ |
| C8 | The first rank fights fully; ranks 2–3 contribute less; the rest are there to resupply the front. | Mandatory | ✅ |
| C9 | A dead soldier is never replaced. <sup>[why](#c9)</sup> | Mandatory | ✅ |
| C10 | Cavalry breaks through infantry, but never through cavalry and never through spearmen. | Mandatory | ✅ |
| C11 | A regiment can withdraw from a fight unless fully encircled, taking casualties in proportion to how much of it is gripped, and refused outright past ~85%. | Mandatory | ❌ finding 2 |
| C12 | Tiredness from 0 to 1, up to −75% damage, worst in the front rank, some for waiting under stress. <sup>[why](#c12)</sup> | Mandatory | ❌ #42 |
| C13 | Ranks refill the front rank. <sup>[why](#c13)</sup> | Mandatory | ✅ |
| C14 | Casualties must not round away. <sup>[why](#c14)</sup> | Mandatory | ✅ |
| C15 | A flank engages a side, not a frontage. <sup>[why](#c15)</sup> | Mandatory | ✅ both halves, the defender's as the side case of [C19](#combat) and the attacker's as [C19b](#combat). |
| C16 | Flanking is shock, and shock wears off. <sup>[why](#c16)</sup> | Mandatory | ❌ new |
| C17 | Turning to face cancels the shock outright <sup>[why](#c17)</sup> | Mandatory | ❌ new |
| C18 | A **shallow** regiment taken in the flank suffers worse **morale**, not worse casualties. <sup>[why](#c18)</sup> | Mandatory | ❌ new, threshold to pick |
| C19 | Four sides, and each side knows how many men stand along it. <sup>[why](#c19)</sup> | Mandatory | ✅ |
| C19a | Holes in the line are charged as a share of the face, not as a count of men. <sup>[why](#c19a)</sup> | Mandatory | ✅ |
| C19b | Enveloping brings twice the face, off the same lookup. <sup>[why](#c19b)</sup> | Mandatory | ✅ |
| C20 | A rear attack is a flank attack that is full width. <sup>[why](#c20)</sup> | Mandatory | ❌ needs [C16](#combat) first |
| C20a | `OutOfArcPenalty` belongs to C20 and is currently in the wrong place. <sup>[why](#c20a)</sup> | Mandatory | ❌ with C20 |

## Orders

| | Requirement | Priority | Status |
|---|---|---|---|
| O1 | Attack orders align centres — the attacker's middle goes for the defender's middle. | Mandatory | ✅ |
| O2 | Archers and horse archers told to attack stop at shooting range. <sup>[why](#o2)</sup> | Mandatory | ✅ |
| O3 | Defenders do not turn to face attackers unless told to. <sup>[why](#o3)</sup> | Mandatory | ✅ |
| O4 | Pursuit only when ordered to attack. <sup>[why](#o4)</sup> | Mandatory | ✅ |
| O5 | Two attackers on one face go for the centre, then share it. <sup>[why](#o5)</sup> | Mandatory | ❌ new |

## Interface and controls

| | Requirement | Priority | Status |
|---|---|---|---|
| GUI1 | Total War mouse layout: left drags a selection box, right orders, middle pans. | Mandatory | ✅ |
| GUI2 | A bind button ties regiments into a wing that keeps its shape. | Mandatory | ✅ |
| GUI3 | Right-drag draws a line; the regiment arrives facing perpendicular to it. | Mandatory | ✅ |
| GUI4 | The player can set formation depth at deployment. | Mandatory | ❌ #31 |

## Balance

Not decisions yet. Raised deliberately as things to settle in one balance pass
rather than piecemeal, so the numbers can be moved against each other.

| | Requirement | Priority | Status |
|---|---|---|---|
| B1 | Spearmen hit for less per man, but the **second rank attacks too**, and they may form tighter than ordinary foot. <sup>[why](#b1)</sup> | Preferred |  |
| B2 | How much ranks 2 and 3 contribute in general. <sup>[why](#b2)</sup> | Preferred |  |
| B3 | Spearmen's steadiness per man lost sits at 0.894 of swordsmen's against a bar of 0.85, since [C13](#combat). <sup>[why](#b3)</sup> | Preferred |  |
| B5 | Archers against horse archers no longer resolves — 28% against 10% and still going at the fifteen-turn cap. <sup>[why](#b5)</sup> | Preferred |  |
| B4 | Depth only tells at the cliff. <sup>[why](#b4)</sup> | Preferred |  |

## Later — technique

Not decisions, and not balance either: directions for how the machinery might
be built, recorded so they are not re-derived from scratch later.

| | Requirement | Priority | Status |
|---|---|---|---|
| T1 | ~~**Ray-like collision instead of A\*.**~~ **Adopted** — this is now [M10](#moving) and no longer a "later". <sup>[why](#t1)</sup> | Preferred |  |
| T2 | Turn rates, revisited after the movement pass. <sup>[why](#t2)</sup> | Preferred | **Settled 14 Aug, second recording:** twelve orders in one game, every one an about-face of 121 to 179° — at 3°/s that is 40 to 60 ticks of wheel against marches lasting 63 to 96 s, and achieved pace fell to 2.1 m/s where the ground allowed 4.8. The designer's call is **raise the rate**: cavalry 3 → **5**, horse archers 4 → **6**. A reversal now takes 36 s rather than 60. The shape of the charge is unchanged — still °/s against the clock, so the objection above still stands and is now the only part of T2 left open. |

## How to work

| | Requirement | Priority | Status |
|---|---|---|---|
| M52 | The merge dropped a fix, and the counter that would have caught the wrong explanation was never asked. <sup>[why](#m52)</sup> | Mandatory | ✅ fixed |
| M53 | Every row of the per-planner table had been a single draw, and one of the findings drawn from it was not there. <sup>[why](#m53)</sup> | Mandatory | ✅ in the bench |
| W1 | Adaptations are said out loud. <sup>[why](#w1)</sup> | Mandatory |  |
| W2 | Two defensible readings means ask. <sup>[why](#w2)</sup> | Mandatory |  |
| W3 | Verify a reported bug by reproducing it before changing anything. | Mandatory |  |
| W4 | Small, system-sized passes. <sup>[why](#w4)</sup> | Mandatory |  |
| W5 | A log line reports what happened, not what the code would predict. <sup>[why](#w5)</sup> | Mandatory |  |
| W6 | Two of its own sharing ground is an event and gets written down <sup>[why](#w6)</sup> | Mandatory |  |
| W7 | A diagnostic must work from the recording alone. <sup>[why](#w7)</sup> | Mandatory |  |
| W8 | A real bug keeps its reproduction as a gate, and its arrangement joins the bench. <sup>[why](#w8)</sup> | Mandatory |  |
| W9 | A check must be able to say what would have made it fail. <sup>[why](#w9)</sup> | Mandatory |  |
| W10 | A cheaper number is not a better route, and a press-through is not automatically either. <sup>[why](#w10)</sup> | Mandatory |  |
| W11 | A measurement across two builds proves the build, not just the number. <sup>[why](#w11)</sup> | Mandatory |  |


---

## Rationale and measurements

The evidence behind each requirement — what was measured, what was tried and
rejected, and what an earlier form of the rule got wrong. Nothing here is a
requirement; the tables above are.

<a id="s1"></a>
**S1** — Every question about nearness asks the rectangle.

<a id="s2"></a>
**S2** — — width to depth — whatever the real depth is. A regiment 40 m wide is drawn 20 m deep. Replaces "2.5× depth", which replaced two rounds of "double the thickness". Stated as a shape so a change to the real depth cannot silently redraw the army.

<a id="s3"></a>
**S3** — What you see is what blocks, what is clicked, and what holds ground. It makes no sense in metres and is the better answer anyway, because the alternative is a regiment that looks flush with its neighbour while its collider is 14 m away. **Non-negotiable.**

<a id="s4"></a>
**S4** — Casualties reduce how many can **fight**, not how much room the body takes up.

<a id="s5"></a>
**S5** — The rectangle is a visual and physical convenience, not a claim about ranks.

<a id="s5a"></a>
**S5a** — The **block** (`Footprint`) is 2:1 and constant: collision, blocking, zones of control, selection, drawing. The **space** (`Space`) is the real ground the men stand on, 40 m by 6 m, and it shrinks: it is what the fighting rules measure. A rule that reads the block for a question about men will silently answer with a drawing convention.

<a id="s6"></a>
**S6** — A regiment's real ground is its *space* — say 100 m by 10 m, being 100 men a metre apart in 10 ranks. Its **frontage** is the line the first rank makes, which is the long side of the rectangle. The short ends are its **sides**. "Block", "footprint" and "fighting frontage" are not the vocabulary; frontage and sides are.

<a id="m1"></a>
**M1** — *Demoted deliberately. As an absolute it produced deadlock: a regiment holding its position across the only line stopped whatever was behind it for ever, and with the stall detector gone nothing would end that. Overlapping your own men is bad; standing still until the battle ends is worse.* What replaces it is [M18](#moving)'s ladder, where sharing ground is the last thing tried rather than the thing forbidden.

<a id="m1a"></a>
**M1a** — Nothing walking past may carry a holding regiment along with it — being shouldered through costs a body its order, not its ground. This is the limit on [M1](#moving)'s demotion: sharing ground is permitted as a last resort, and displacing somebody who was standing there is not the same thing and is never permitted. **Unconditional, and it was not.** The first form exempted only a declared press-through from the shuffle that pulls two overlapping bodies apart, on the grounds that an accidental overlap has no quarrel with anybody's orders. It has one: half a step per tick still lands on a regiment that was told to stand there, and once rung 2 could clip a corner and rung 3 could be entered by steering rather than by plan, "accidental" covered most of it. **Whoever is marching takes the whole correction; only when neither is marching is it shared.**

<a id="m3"></a>
**M3** — No dead zone. Three attempts at a threshold were three bugs.

<a id="m4"></a>
**M4** — The regiment marches the way it is going, at full pace, and comes round at the end — the same manoeuvre an attack makes on its last hundred metres. *Supersedes "holding a front across a move must be asked for by drawing the bearing", which held the drawn front from the first step: a regiment sent 167 m travelled rear-first the whole way at 29% pace, which reads as a fault rather than an order.*

<a id="m4a"></a>
**M4a** — It was only ever a side effect of M4's old form. Wants its own control if it is wanted at all.

<a id="m5"></a>
**M5** — Movement is still blocked if the path is.

<a id="m6"></a>
**M6** — a regiment that cannot reach the exact point walks to the nearest usable ground and says so, and one that cannot move at all waits *visibly* rather than quietly stopping. *Waiting is now a real outcome ([M18](#moving) rung 4) rather than a failure, because what walls a regiment in is enemies and ground, and both of those move or are taken. The thing this rule exists to forbid is a regiment that has silently stopped taking orders — not one that is plainly stuck and saying so.*

<a id="m10"></a>
**M10** — A regiment asks one question — *from here, along this bearing, how far can my rectangle go before it hits something, and what?* — and marches if the answer reaches the destination. The pathfinder is the **escape hatch**, not the standing method: it is for concave ground a fan of rays cannot see out of. *Supersedes the first form of M10, which said friendly regiments were never a reason to re-plan. That was inferred from the word "terrain" and the designer has since said the opposite.*

<a id="m11"></a>
**M11** — Re-planning is the most expensive thing the rules do, so a regiment that re-plans continuously is slow and unsteady. But one that plans once and never looks again is worse: whatever blocked it has moved on, and the way round it chose is no longer the way it would choose. Re-cast every few ticks, to leave a bad line as much as to find a better one.

<a id="m12"></a>
**M12** — A line is only usable if the whole body sweeps along it clear — the traced collider, not its centre. Planning at 2 m of clearance for a body 40 m wide finds gaps that do not exist and then blames whatever is standing in one.

<a id="m13"></a>
**M13** — It may travel along its own frontage without turning, at whatever the angle between its facing and its line of travel is worth. Moving square to its front a 40 m by 9 m body sweeps 9 m rather than 40, which is what lets a line pass a hole narrower than it is.

<a id="m14"></a>
**M14** — Look for a line the regiment fits along facing forwards. Only if none exists, look for one it fits along crabbing — and then **turn to set the crab up**, because presenting the narrow side means facing square to the way you are going. Paid for twice, in the turn and in the reduced pace, so threading a gap is a decision rather than a shortcut. *Supersedes "plan wide, crab locally", which deferred this.*

<a id="m15"></a>
**M15** — That is the whole of the difference, and it is why the two kinds are not treated alike — not preference, but whether the thing moves. **Enemies are walls** and a march arches round them. **Friendly regiments are very bad ground**: costly to cross, never forbidden, because they will have moved by the time you arrive. A route may therefore be planned straight through a friend; the march still never overlaps one, since [M1](#moving) is kept by steering at the moment rather than by the plan.

<a id="m15a"></a>
**M15a** — On Move or Evade it is, and the regiment goes round. On Advance or Aggressive it is not — going through it *is* the order, which is what `TryFightWhatBlocks` exists for. Making enemies impassable to everyone would quietly delete the difference between telling a regiment to go somewhere and telling it to fight its way there.

<a id="m16"></a>
**M16** — A regiment that will have walked on by the time you arrive is not in your way, and one that is nowhere near you now may be exactly in your way in twenty seconds. Trajectories are tested against each other in **space and time**, not as static shapes.

<a id="m16a"></a>
**M16a** — A friendly regiment's route is known exactly, so use it. An enemy's is not, and the rules layer must not read it — it holds the true state of both armies, so predicting an enemy from its actual orders would let a regiment sidestep a manoeuvre its own player cannot see. That is a fog leak in behaviour rather than in data, and no `PlayerView` test would catch it. Enemies are predicted from **observed motion only**, and only while they are seen or remembered. **Non-negotiable.**

<a id="m16b"></a>
**M16b** — Both predicting each other and both re-planning is how reciprocal avoidance oscillates — each invalidates the other's prediction, at the cadence instead of at tick rate. Resolved by precedence, which already exists: a regiment standing where it means to stand is not shoved aside for one still walking, and otherwise the lower unit id plans first and is taken as fixed by the other. Falls out of the ascending-id iteration determinism already requires.

<a id="m17"></a>
**M17** — Among the lines that work, take the one that loses least time to *avoidable* things — how far round it goes, how much of it is crabbed, how much of it squeezes past friends. It stays blind to terrain speed, exactly as today: marching into a swamp is a decision the player made and the route must not quietly overrule it ([M4](#moving)).

<a id="m18"></a>
**M18** — (1) The **straight line**, if the body sweeps it clear. (2) **Round it** — arching or crabbing, whichever costs less by [M17](#moving). *Both in: arching first, then crabbing.* (3) **Through its own** — sharing ground with friendly regiments, as a last resort and never a first one, which is what stops a held position walling the army in behind it. (4) **Wait**, if even that is impossible, because what is in the way is enemies or ground and both of those change. Each rung is tried only once the one above it has failed, so the ordinary march is still a straight line and nothing above costs anything to skip.

<a id="m19"></a>
**M19** — Not "cast until the first thing hit" — a ray stopped at its first obstacle says nothing about whether going round it leads anywhere, which is how a regiment walks confidently into a bay and only discovers it turn by turn. Extend to the full length and read the geometry, so concave ground is seen before it is entered rather than after.

<a id="m19a"></a>
**M19a** — A line grazing the corner of what it is going round is a line that fails on the first metre of drift. Aim past the corner, not at it.

<a id="m19b"></a>
**M19b** — One failure is an obstacle; two is ground a fan cannot see out of. **Recorded as a number to revisit** rather than as a truth — it is the seam between the cheap method and the thorough one, and if casting turns out to handle concave ground well ([M19](#moving)) the search may be needed far more rarely than this, or not at all.

<a id="m20"></a>
**M20** — A regiment overlapping a friendly formation moves at **60%** of what the ground would otherwise allow, for exactly as long as it is inside one. Charged on the overlap itself and not on the plan's intent, so [M18](#moving) rung 3, a crab through a gap and an accidental scrum are all priced by the same rule and cannot disagree. Flat rather than per body, by the designer's call: noticeable, never a trap. *Rung 3 was free — a recorded game had cavalry walk clean through an Archers regiment three times at full pace, which is the last resort costing less than the detour above it.*

<a id="m21"></a>
**M21** — The commitment is to a **direction**, not to a body: while it holds, whichever friend is in the way now does not get to re-open the question. Released only when the body it was decided against has fallen behind the line of march, or is gone, or the order changed. *The old release — the first tick nobody is touching us — is what produced the seizure. A sidestep succeeds, the commitment is dropped, the same regiment is in the way again next tick, and the side is re-derived from a slightly different position. That is the designer's `isSqueezing`, and its point is the word "until".*

<a id="m22"></a>
**M22** — Every bend is a wheel, every wheel is time spent at a fraction of pace, and a planner blind to that will buy a 26 m saving with a 90° turn worth forty. So the cost of a line is the time to walk it *including the turn onto each leg* — which is what makes [M17](#moving) and [M18](#moving) rung 2's "arching or crabbing, whichever costs less" a comparison that can actually be made. *A recorded game spent 1,432 of 2,644 ticks wheeling — 54% — and no rule anywhere could see it.*

<a id="m22a"></a>
**M22a** — Arching and crabbing are both rung 2 and the cheaper in seconds wins, as [M18](#moving) always said. The rungs stay in strict order — a way round that exists beats pressing through even when pressing through is quicker — because that ordering is the designer's rule and not an optimisation. Now that [M20](#moving) prices rung 3, turning the ladder into a comparison is a one-line change if it is ever wanted.

<a id="m23"></a>
**M23** — A regiment that bends twice wheels twice. The front to hold while walking a leg is *that leg's* bearing — [M3](#moving) said so from the start, and it only ever meant the whole route because every route used to be a single line. *An order given without a drawn bearing held the start-to-destination bearing for the entire march, so an arching regiment walked its second leg still pointing along the first.* And because the plan checked each leg at the facing the regiment had when it planned, while the walk held a third front entirely, the two disagreed about the shape that was travelling — which is what clipped the corners of what it had just gone round. One bearing per leg, used by the plan and by the walk.

<a id="m24"></a>
**M24** — Every question a route asks — is this line clear, what is in the way, how far past that body must I aim — is asked of the regiment squared to the line it is about to walk. `unit.Facing` at the moment an order arrives is a transient it is already shedding ([M3](#moving), and [M23](#moving) now makes it true of every leg); the wheel at the start is the *steering's* business, not the plan's. *Measured: the same regiment, same line, same ground, only the front it was left on varied. At 0°, 30°, 60°, 120°, 150° and 180° off the line it walked round its own; at **90°** — presenting its whole 40 m frontage broadside, so the corridor swept is twice the one it will occupy — every rung failed and it shouldered straight through. Which is why it was always the cavalry: at 3°/s it spends most of its life badly misaligned.*

<a id="m25"></a>
**M25** — A line stands shoulder to shoulder ([M2](#moving)), so squaring a regiment onto a new bearing in the middle of one laps its neighbours before it has moved a metre: a 40 × 20 body turned 50° reaches 21.7 m along the old axis where it reached 10. The sweep then reported a collision at distance zero on every candidate, rung 2 found nothing on either side, and rung 3 answered — *eight press-throughs in one recording, every one setting off from the same forty metres of ground.* The steering has always known this; the planner never did. **Both halves matter.** Excusing an overlapping body outright deadlocked two regiments ordered to swap places: each decided the other was merely where it was standing and both planned straight on. Abreast or behind is ground you are leaving; ahead is a regiment you are walking into, and already touching it does not make that untrue.

<a id="m25a"></a>
**M25a** — The rungs read it differently and must. The **straight line** still refuses a body it laps ahead — otherwise two regiments swapping places walk through each other. The **legs of a detour** ignore whatever the regiment laps at the leg's start, because every candidate leg out of a lapped position collides on metre zero and rung 2 finds nothing on any side. *Recorded: a regiment came to rest lapping its own Archers, was ordered off past them, and shouldered through — four times in one game, always the same pair.*

<a id="m27"></a>
**M27** — Threading it means going through *the way the gap faces*, not the way the march was heading — the regiment squares to the pair it is passing between, presents its depth to the walls either side, and comes back onto its line afterwards. Recorded 14 Aug: Archers and Swordsmen 50 m apart, both 20 m deep, **30 m of daylight**, a cavalry regiment 20 m across side-on standing *in* the gap — and it shouldered through, reporting *"no way round it and no gap to thread"*. Every aiming point the planner could generate was tied to one body's centre; the midpoint between two neighbours was never a candidate. And projecting that gap onto the line of march makes it **20 m** — exactly the regiment's own depth, no room at all — so the corridor only exists if it is measured and walked along its own axis. Nearest corridor to the drawn line wins, by [M4](#moving).

<a id="m28"></a>
**M28** — Grow each body by the mover's own reach, take the corners of what that leaves, and find the cheapest walk from here to there through them. Aiming one point at a blocker's flank has been patched three times ([M19a](#moving)'s margin, standing off in steps, [M27](#moving)'s corridors) and each fix uncovered the next arrangement it cannot describe — finally **one regiment alone in open ground**, halfway along a 71 m march, which shouldered through on five orders out of ten. Measured there: the second leg of a one-bend detour blocked at every stand-off from 46 m to 186 m, while the destination itself was clear. The route that works is two bends, and one waypoint cannot express two bends at any parameter value. The corner walk and the aiming rules **compete in seconds**, as arching and crabbing do — the walk searches in metres, so a short arc chosen for its wheel can still beat it.

<a id="m29"></a>
**M29** — *"Route should wait for the turn right at the point where it would collide."* A plan is a claim about a shape; the regiment reaches the tight part still coming round, in a body that was never measured. Recorded: inside a 30 m corridor measured against a 20 m body while holding a front **121° off**, where the same body spans 44 m — three ticks before a 121° wheel at 5°/s could have finished. Waiting *where it would hit* rather than *before the leg* is the designer's call and the better one: a regiment that would clear the gap anyway is never delayed, and one that would not stops on the step that would have hit rather than standing in the open on the chance of it. A body it is **already** standing in never holds it — leaving is the whole point ([M25a](#moving)).

<a id="m30"></a>
**M30** — An ordinary leg is checked at the line of march, and a regiment comes round onto that just as slowly as onto a corridor's. Recorded 16 Aug: twelve collisions in one game and **not one declared press-through**, so nothing charged for any of them and no rule had agreed to any of them, which is what *"goes through units without colliding"* means. The plain rule (never step into one of your own unless you said you would) was written, measured and **thrown away**: it deadlocks, failing `TwoRegimentsSwappingPlacesDoNotDeadlock` and `ARegimentHeldUpByAFriendGoesOnceTheFriendMovesOff`, which is the same designer's other complaint arriving by a different door. What survives is **wait only while waiting can change the answer**: a regiment still coming round will be a different shape in a moment, one already square to its leg will not, so the second walks on and the shuffle sorts it out.

<a id="m32"></a>
**M32** — The rule is sound (a bend round something never in the way can only lengthen a route) and it is 7× cheaper on a sparse field. It is also 4 to 6× dearer on everything else: growth on against growth off, same build, three runs each, sixteen regiments ordered at once went **19 ms and 1,140 legs → 112 ms and 15,849**, and **not one route in the whole bench differed**. Two causes, both measured. Rounds restart the search, and while every leg is cached across a restart the *frontier* is not, so expansions tripled. And the corridor filter it replaced was already good pruning — the fault was never which bodies were considered, it was [M31](#moving)'s place cap being spent before the useful ones were reached. Left in the tree with `MostRounds = 1`, so growth is dormant rather than deleted: it is the right shape for reaching *outside* the corridor and deserves one more try with the frontier carried across rounds.

<a id="m33"></a>
**M33** — Its answer costs about a millisecond and, when clean, is a real route — so a march takes whichever of the two is cheaper, both priced in the same seconds. Recorded 17 Aug, one click: the ladder 82 m and 28 s against the search's 153 m and 40 s, because the search was optimal over places it had invented and the ladder's bends were not among them. A true answer to the wrong question. **Feeding the price in as the search's starting bound was also built, and lost:** it is sound, but the bound is what lets the search *stop*, so a search told to beat a route cannot take the first one it finds and keeps buying places to try again — one arrangement went from 10 places and 180 legs to 34 and 1,260. The prune it was meant to buy never arrived either, because the fields where the search is dear are exactly the fields where the ladder presses through and offers no price. Compare at the end, never during.

<a id="m34"></a>
**M34** — Measured across four sweeps: `geometry per leg` is **3.06** in every row at every setting, and time tracks legs priced at about 6 µs each. Three things that looked like the bottleneck were built and were not — a heap frontier cut the frontier walk **113×** (3,425,849 steps → 30,284) for ~10% of the time; switching terrain sampling off bought 25% on one row and **nothing** on a whole-army order; and cutting the front resolution from 0.18° to 10° took states per place from 74 to 14 for ~8%. None of them touch the fact that the graph is **complete** — every place joined to every other on three fronts, so at forty-eight places the search prices 5,379 of the 6,912 legs there are. The heap is kept anyway, for the O(states²) tail it removes. **The two levers left are both about legs, not bookkeeping:** a standing check depends only on (place, front) and a rectangle at θ covers the ground it covers at θ+180°, so the reverse leg asks an identical question and pays again — two thirds of all geometry is standing checks. And joining every pair is a choice, not a necessity.

<a id="m36"></a>
**M36** — A shortest route among convex bodies bends only at their corners and only tangentially, so a leg leaving a corner into its own body's wedge, or across it, cannot be on any route and is not worth naming, pricing or sweeping. Measured: 17 of 19 approach angles either way, 563 passing and 11 failing either way, and **2.2× quicker on sixty-four regiments ordered at once for 2.9× fewer legs** (70,539 → 24,330). Two halves of the idea were tried and only one survived. **Corners-only places lost six angles** (17 → 10): the face projections are not there because routes bend at them but because a regiment can legally *stand* there, which is a constraint a point-robot visibility graph does not have. **Filtering face places lost five more** (17 → 12), and all five came back when they were let alone — the rings are bodies padded by what the mover needs, padded by more than it needs on the presentation it is not using, so a leg that cuts the ring can be well clear of the regiment inside it. A corner's three-quarter cone survives that padding; a face's half-circle does not. Costs two tests, both where the mover starts *inside* the cluster, which is the case the taut-path argument never covered.

<a id="m37"></a>
**M37** — The clock went into the recording and stopped agreeing with the counters at once: a plan with **two places and three legs priced came to 13,3 ms** while plans several times larger came in under four. Asking the ladder for a second opinion roughly doubles a plan (26,1 ms against 13,7) but was asked 6 times in 33, so it is real and secondary. The whole session's planning came to about 530 ms against 282 frames over 33 ms — planning was never the thing being felt. **Legs were a stand-in for a clock and a bad one**, and five optimisations this pass were chosen against them.

<a id="m38"></a>
**M38** — `!unit.IsMarching` short-circuits past the five-tick cadence, and "not marching" is exactly what a regiment in contact is — so every regiment held up by somebody plans a fresh route sixty times a second of battle time. Measured at x12: worst frames 336–608 ms, ticks 330–392, 5 to 7 regiments marching, against `halted by` lines for four of them in the same window. Invisible in the log because the Cost lines are written by the Unity controller at order time and these are made inside the simulation. Worst at a rout, because routing enemies count as quarry, so a broken line hands every nearby regiment a target to close with, halt against, and re-plan at. **Throttling it to the same cadence was already tried and lost** — pursuers moved in bursts and a broken enemy took 24% losses chased or ignored — so the cadence is the wrong lever and always was.

<a id="m39"></a>
**M39** — The designer's rule, four parts. A route is re-planned when something happens that could make it wrong: **the first leg meets a different blocker than the one it was planned against**, or **a friendly body comes to intersect the route**. Failing an event, a regiment that **cannot reach its destination** re-plans on a cadence of its own **first leg's travel time divided by four** — a short first leg asks often, a long one rarely, and a regiment that is getting where it is going does not ask at all. *"Dont treat them as rules set in stone because design might change — i actually plan on giving more responsibility to the player in the future, so the player will have to do some commands rather than assuming envelopment maneuvers."* Recorded as provisional: this is the re-planning a game that decides for the player needs, and a game that hands manoeuvre back to the player needs less of it.

<a id="m40"></a>
**M40** — One route plan allocated **205 kB**; a whole simulation tick allocated **1,1**. So an ordinary session churned megabytes through a stop-the-world collector and stopped for 40–50 ms at a time, evenly spread and uncorrelated with how many regiments were marching, which is exactly what the recording showed and exactly why no amount of making the *search* cheaper had ever been felt. Three causes, all litter: the ledger's arrays, rebuilt for every march (58 kB) — now a scratchpad the battle keeps and every march borrows; the search's own lists, dictionary, heap and heuristic table, rebuilt for every march — now borrowed from the same scratchpad; and `BattleState.UnitsOnField()`, an iterator method, so **every clearance test allocated an enumerator** and a plan asks thousands (the remaining 65 kB) — now a struct enumerator, which `foreach` takes by shape, so all sixty-four callers are untouched. **205,5 kB → 9,9 kB a plan, and a fighting tick from 1,3 kB to 0,1.** Per battle rather than static, because two battles in one process is every test run.

<a id="m41"></a>
**M41** — Built deliberately without touching any code the existing search uses (its own oriented-box overlap test, own open list, own motion primitives), so a difference between the two would mean something. Registered as `RoutePlanners.TheHybridAStar`, in `RoutePlanners.All`, **not** the default. First pass shipped four real bugs, found by testing rather than by inspection: the arrival heading silently defaulted to the mover's *own current facing* and was enforced as a hard requirement, failing ~80% of an open-field zero-obstacle sweep; the expansion cap (6,000) was too small even for the single-obstacle case (needed 9,428); a pivot produced a duplicate waypoint at the same position; and the heuristic had no notion that turning costs anything, so any destination roughly behind the mover failed outright regardless of budget. All four fixed: heading now only constrains the finish when a caller actually asks for one; the cap raised to 20,000; duplicate waypoints collapsed on reconstruction; and the heuristic strengthened with a coarse obstacle-aware grid (h2, a point robot's shortest hop count round the same bodies) and a proven-admissible turn-cost floor. Verified: the open-field sweep that found ~20% before now finds 90/90, and the single-obstacle case that failed at the 6,000 cap now finds a clean, non-duplicate route at 9,428–10,304. Fixing those four surfaced a fifth, structural rather than a slip: a mover placed exactly nose-to-tail against a body — which the project's own approach-angle gate does on purpose — had **no legal first move at all**, because the clearance margin turned an exact, legal touch into a manufactured overlap, and even once that was exempted, the fixed-length forward-only primitive set (march, wheel, pivot-about-centre) genuinely cannot escape a dead-ahead contact. Fixed by grace-exempting an obstacle the mover already started within margin of, and by adding a short step-back primitive — the one maneuver a boxed-in regiment actually has. **Measured against the project's own correctness gate after every fix above: 0 of 19 approach angles, against the tangent search's 17 of 19** — every angle in that gate starts the mover close to or touching a body by design, and the search either fails outright or (once reversing removes the only heuristic term that made "which way is behind me" cheap to prune) burns the full budget hunting for an escape a visibility-graph-style search finds for free by construction. Open ground away from that gate: sound and complete, but priced in thousands of lattice expansions where the tangent search prices tens of legs. **Verdict: not better, and not close, for this project's purposes as shipped.** The bugs were real and are gone; what remains is that fixed-length primitives generated the same way everywhere are the wrong shape for a game whose defining cases are tight quarters — the existing search's obstacle-derived candidates are the reason it doesn't have this problem, and matching that would mean changing how candidates are generated, not another round of bug fixes. Kept in the tree as `RoutePlanners.TheHybridAStar` for whoever wants to pick the idea back up, default unchanged.

<a id="m42"></a>
**M42** — Second review pass, all verified against the code before acting. **Illegal routes, four causes.** A pivot has zero advance, and sample count was derived from advance, so a pivot was collision-checked at *one* pose — its final heading — while a 25 m half-width formation swings its outer corner ~8,7 m through the arc unchecked; wheels had the same root cause, spacing measured on the centre while the corner travels `\|turn\| × circumradius` further, so the 2 m spacing promise held only for a point robot. Both fixed by measuring travel as `max(\|advance\|, corner arc)` and never sampling once. `ApplyTo` stepped along the chord at the half-turn heading, an approximation that was never needed — constant advance at a constant turn rate is a circular arc with a closed form — and because the sweep used the same formula, **the collision check was clearing poses the body never passes through**; replaced with the exact arc. And `Reconstruct` appended the exact goal whenever the last state was more than 5 cm away, against a 2 m goal tolerance, so **every plan ended with up to 2 m no primitive proposed and no sweep examined**; that hop is now swept like any other and the route stops short rather than claiming it. **Wheel turn was uncapped** at rate × time, which for a slow unit that turns well (archers, 1,59 m/s at 5°/s) reaches 52° in one primitive — past `RouteSearch.WalkingCapDegrees = 45f`, the angle above which the executor halts and pivots instead, so the planner was planning wheels nothing would walk; now clamped to that cap. **Measurement, two causes.** The goal node's `G` — the seconds the search actually minimised — was computed and thrown away, so the wrapper reconstructed a polyline length and the one planner here that prices in seconds reported metres; `G` now rides out on `Outcome`. And the cost model is this planner's own invention (`WheelPaceFraction`, `PivotRateMultiplier`, `PivotPenaltySeconds`) rather than the executor's (`AlignmentPenalty`, `PaceWhileInsideItsOwn`, `PivotBonusWhileHalted`, the 45° cap) — measured on one 90°-off march at **39,5 s planned against 29,0 s as the executor would actually charge it, 36% adrift**. Independent *geometry* is what makes the comparison meaningful (a shared SAT bug would contaminate both answers); independent *costing* makes it meaningless. `Marching.SecondsToWalk` already exists and is the executor's own model — that, not either planner's internal number, is what any quality ranking must go through. **Also**: heading bins were coarser (15°) than the goal heading tolerance (11,5°), so a state that would have satisfied the goal could be pruned by a bin-mate that would not — now 48 bins; the obstacle-field heuristic only covered the start-to-goal box plus four cells, leaving any real detour invisible and the search unguided exactly where it needed guidance most — now widened by half the straight-line distance, with out-of-grid queries reading the nearest edge cell instead of giving up; the clearance grace was granted for the whole plan from one start-pose test, so a body graced at the start stayed graced 500 m away — now scoped to one body-length from the start; the turning term is provably dead while reversing is legal (`min(d, π−d) ≤ π/2` always) and is now gated behind a `const bool` rather than evaluated every expansion; and the heuristic's doc claimed a lower bound it does not have — the 8-connected hop count overshoots Euclidean by up to ~8% and `Max` preserves that, while dividing by top speed undershoots a wheel-heavy remainder by up to 40%, neither bounding the other — now stated plainly as a greedy heuristic tuned for expansion count, not an admissible or ε-bounded one. **What this changes about M41's numbers: the open-field sweep drops from a reported "90/90 found" to 83/90 — and the seven lost are the honest ones**, routes that had been resting on an unchecked pivot, an unswept goal hop, or a pose the body never occupied. Found and legal now agree exactly (83 of each, checked against the executor's own `IsClearLine`), which the earlier number never established because it only ever asked whether a route came back. **The gate is unchanged at 0/19, and so is the verdict.** Not adopted; still not the default.

<a id="m43"></a>
**M43** — Two counters added before anything was touched, sweep samples and `HybridBox.Overlap` calls, on the reasoning that *overlaps per expansion* separates "the search explores too much" from "each expansion costs too much", and those want opposite fixes. **It was neither of the two suspects.** The sweep fix was not the regression: these formations have a circumradius of 22,4 m, not the ~100 m a wide block suggests, so it cost about 3× the samples, not 10×, and the whole geometry bill came to 63,5 overlap tests per expansion against *two* bodies. Hoisting the cull to the state — once per expansion instead of once per sampled pose — took that to 8 and **barely moved the clock**, which is the finding, not a disappointment: the geometry was never the bill. The expansion count was. One 90 m hop across the gate cost **373 228 expansions**, against a shipped cap of 20 000. **Then the counters found something the clock could not.** The mover's own opening pose overlaps a spearmen block at **every angle from 5° to 55° — eleven of the nineteen** — and this planner had no leaving rule, so those angles were refused at the first state and answered "no route exists" after one expansion. Not slow: wrong. `Marching.IsClearLine` has taken a `leaving` flag since M25 for exactly this. Added here **stricter than the incumbent's**, which excuses such a body for the whole leg outright: separation may hold or widen across a sweep, never narrow, so the excuse cannot become a licence to march through — you cannot come out the far side of a convex body without going deeper in first. (One hole, stated rather than papered over: separation holding exactly constant is allowed, so a mover already inside a body may slide along it lengthwise.) **And a second thing the gate itself did not say.** At **5° to 40° there is no facing at all in which the mover can stand on the destination** — none of forty-eight — and at 0° exactly one; the destination is inside a spearmen block. So those angles are passable only by a planner willing to *finish* inside another regiment, which the incumbent is, by the same leaving excuse applied to the last leg. The gate's claim that "the gap admits it at every angle" is true of the gap and not of the destination. **The heuristic was the real cost, and the arithmetic says why.** For these units a 90° change of front costs about twenty seconds and ninety metres of marching costs nineteen — so a heuristic silent about turning is not slightly optimistic on a route needing three such turns, it is out by a factor of four, and A* answers that by expanding every state cheaper than the truth. The grid now carries a direction as well as a distance (downhill across its own hop counts) and the turn onto it is priced through the same pivot chain the primitives are charged for: **0° from over 400 000 expansions to 159 000**. Obstacles in that grid are now marked twice, at true size and inflated by the mover's in-radius, and the larger estimate wins — inflation *alone* was worse than none, because it swallows the mover's own starting ground and starves exactly the states that needed guidance most. Weighted A* on top, swept rather than guessed: ε of 1 / 1,5 / **2** / 3 / 5 gives 159k / 64k / **35k** / 37k / 38k expansions at 94,0 / 95,1 / **98,2** / 98,5 / 160,8 s of route, so two is where the curve bottoms out — four and a half times fewer states for four percent more route, and past it the states stop falling while the routes visibly rot. Plus the analytic goal shot every write-up of Hybrid A* has and this one did not: pivot, march, pivot, swept and priced in the primitives' own currency so it wins on merit rather than on being costed by a kinder rule. **Tried and useless, recorded so nobody repeats it:** breaking f-ties on lower h, which collapses the march plateau in theory and moved 386k to 390k in fact — h is a quantised grid hop count, so f never actually plateaus. **A route of bare points threw away most of what this planner knows.** Its whole subject is (x, y, heading) and it was handing back the first two, leaving the walker to guess the third from the shape of the line; three angles read as walking through a body on routes where every pose the planner checked was clear. Naming one front per leg made it *worse* — five — because a wheel has no single front. A `Plan` cannot say "arc", which is the same wall the design notes hit when they dropped curves: a plan has to be expressed in the terms the walk uses. So a turning primitive is now handed back as the chain of poses its own sweep struck, every leg straight and at a front, and a pivot no longer relabels the leg that arrived with a facing only taken up after it finished. **Gate: 0 of 19 → 13 of 19**, at a budget of 100 000. Five of the six that remain are the destinations no facing can stand on; the sixth (0°) is chord against arc. Route quality is now genuinely competitive — 47–87 s where the tangent search spends 32–99 — but **the price did not come down**: 56,1 s to order 64 regiments against the tangent search's 2,2, and correctness needed five times the cap that performance can afford. That tension, not the bug list, is what a decision about adopting this has to be made on. Still not the default.

<a id="m44"></a>
**M44** — A 1,15 GB capture was recorded against a live battle and it could say `BattlefieldController.Update` and very little else: there was not one `ProfilerMarker` anywhere in `Assets`, `packages` or `core`, so every phase of a frame arrived as one undifferentiated block, and Deep Profile — the only alternative — distorts absolute numbers far enough that they cannot be set beside the bench. Meanwhile the harness had **already** been printing the answer to its own console since M40: a `Frame` line for anything over 33 ms, split into sim, views, tracking, gui and rest, with how many regiments were marching, how many collections ran and how far the heap moved. Two instruments, and the one that was read was the one that could not answer. **One real gap, now closed.** Planning happens inside the tick, so `sim` covered both the simulation's ordinary work and every route worked out during it, and a frame that stopped while a wing was on the move could not say which — the one split that decides what to fix. `Marching.PlanTo` is the single door every plan comes through, by hand or on the tick's own cadence, and it already counted them there (M38); it now times them there too, on `BattleState.RoutePlanningTicks`, in raw stopwatch ticks so a thousand short plans do not each round to nothing, and on the battle rather than static for the same reason the count is. The frame line reports it as a share of sim — *"sim 41,2 (3 ticks, of which planning 38,9 for 12 routes)"* — rather than as a column beside it that would not add up. **And the profiler now agrees with the console**: five markers, `BattleChess.Simulation` / `.Views` / `.Tracking` / `.Interface` / `.PlanRoute`, named for the same phases the frame line splits, so a capture taken when something is *felt* rather than reproduced lands on the same vocabulary. `Unity.Profiling` had to be written `global::Unity.Profiling` — the file's own namespace is `BattleChess.Unity`, which swallows it, and the Unity scripts do not compile in `dotnet test`, so this was caught by compiling `Assets/Scripts` against the editor's own reference assemblies with Roslyn. That check is worth keeping: it is the only thing in this repo that compiles the Unity half without opening Unity.

<a id="m45"></a>
**M45** — Asked for two armies of forty thousand in regiments of two thousand, and the content said no: `units.cfg` caps swordsmen at 1 600, horse archers at 800 and **cavalry at 700**, and `BattleSetupReader` clamped to those caps *in silence*. A battle file could state a 2 000-man cavalry regiment, deploy 700, and read as though it had fielded the army it described — the order of battle on the page and the one on the field disagreeing with nothing anywhere to say so. Working from the caps, forty thousand men could not be packed into fewer than 32 regiments a side however they were arranged. **The fix is not a bigger number in shared content.** `maxStrength` there stays what it always meant: what a regiment of that type normally is. A battle may now set its own ceiling, and an `[army]` may set one again; three scopes, tightest wins, and a scenario built round unusual bodies no longer has to edit a catalogue every other battle and the balance harness read from. **And silence is no longer an option**: a deploy asking for more men than its ceiling allows is a `FormatException` naming both numbers, not a quiet trim. Asking for fewer than the type's minimum still rounds up, because a body too small to be a regiment is a different question and `ford.battle.txt` already leans on it. **What it bought:** `greatfield`, twenty regiments a side at two thousand each — 3 cavalry, 2 horse archers, 8 spearmen, 7 swordsmen — on a new 72 × 96 map at 25 m, 1 800 × 2 400 m, with small mountains anchoring the north, the river anchoring the south, one ford, and two knolls a real march from either start line. Sizes and positions were computed from the built footprints rather than estimated, and three checks hold the scenario honest: forty thousand men in twenty regiments a side, nothing deployed inside anything else, and every regiment able to stand where it was put. **Worth seeing before it is copied:** two thousand cavalry in one body is **229 m across the front and 114 m deep**, a quarter of a kilometre of horse in a single rectangle, which is precisely why the 700 cap existed. The ceiling is now the scenario's decision, and this scenario has made an unusual one deliberately.

<a id="m46"></a>
**M46** — The recording said planning was **87% of all slow-frame time** — drawing 2%, the collector 4% — and that the worst frame planned **41 routes in 1 652 ms**. The cadence was not the fault: about one re-plan per marching regiment every three or four ticks, exactly as M39 intended. The fault was that a frame which has fallen behind runs up to **eight** ticks to catch up and each one does its own share of re-planning, so fourteen marching regiments produce thirty routes inside one frame — and that frame's slowness feeds the accumulator that makes the next one run eight ticks too. **The catch-up cap was not a safety valve, it was the multiplier.** **(1) One re-plan per regiment per frame.** Three answers across eight catch-up ticks throws two away unseen: nothing was drawn between them. Refusing them costs nothing. **(2) An allowance for the whole frame**, four routes or eight milliseconds, whichever runs out first. Past it a regiment keeps the route it has and asks next frame — which is what the cadence has it doing on most ticks anyway, so a deferred re-plan is an ordinary state rather than a degraded one. Both live in `PlanningBudget`, above `IRoutePlanner`, so every planner gets them; both are **off** unless a host calls `OpenFrame`, so the CLI, the benches and the suite are untouched; and **orders a person just gave are never refused** — a regiment that does not move when told to is a bug and no frame budget is worth that. **Deferral had to be a queue, and finding out cost a test.** A `HashSet` of deferred regiments was built first and starved two thirds of the field: callers ask in a fixed order, so among equally-deferred units the ones early in that order won every frame and the run settled into serving the same eight of forty for ever. FIFO, with each frame's allowance promised to the front of it: 40 of 40 get a turn. **(3) Beneath all of them, the ground.** Terrain cannot be swept, only sampled, and `FormationFits` tests the whole footprint on a grid of points every ten metres along a leg — on a 1 800 × 2 400 m map with legs running hundreds of metres that is over a thousand lookups a leg, and the default planner had reached **61 µs a leg** against the six measured on the old map. `PassableGround` is a summed-area table of impassable cells per movement type, built once per battle, so "is any of the rectangle this leg sweeps closed to me" is four array reads whatever its size. When the answer is no — most legs, most battlefields — the entire sampling loop is skipped. **It changes no answer**: anything the table cannot clear falls through to the original check untouched, and the gate confirms it, every planner scoring exactly what it scored before. **Measured, per plan on the Great Field:** ladder 22,6 → 24,7 ms, search 126,0 → 101,2, corners 97,9 → 65,3, **tangents 67,7 → 47,2 (−30%)**; the hybrid is unmoved at ~5 300 ms because it owns its geometry and never asks. **Asking the same question per step was built, measured and thrown away**: a regiment 229 m across has a 128 m bounding circle, so a step's own rectangle is a quarter of a kilometre wide and catches a mountain nearly everywhere the whole leg did — the default went back up to 52,8 ms and the ladder to 30,4. Left in the source as a comment so it is not retried. **Together, on the harness that reproduces the stall: worst frame 84 routes / 2 009 ms → 4 routes / 88 ms.** The 88 is one route costing ~70 ms, which an allowance cannot refuse before it has been paid — the remaining lever there is the leg graph itself, O(places²) → O(places·k), which changes which routes come back and so wants its own pass.

<a id="m47"></a>
**M47** — Forty regiments a side on each — 16 spearmen and 12 swordsmen at 800, 4 archers at 700, 6 cavalry at 600, 2 horse archers at 700, 30 200 men — the **identical** order of battle on all three, every strength inside the catalogue's own ceiling, so the army is never the variable and no scenario has to raise a cap (contrast M45). **The Crucible**, 56 x 72 at 25 m, front lines 775 m apart, makes *the crowd* dear. **The Long March**, 100 x 76 at 30 m, 2 370 m apart, makes *the distance* dear. **Broken Country**, 76 x 68 at 25 m, sixteen patches of wood, jungle, hill and marsh with two winding streams, makes *the ground* dear. Orders are deliberately awkward — every regiment sent diagonally to the far flank so that eighty marches cross rather than run parallel, which is what a player produces by box-selecting an army and clicking once. **`PlanningProfile` times fifteen steps and counts three more**, reporting *inclusive* and *self* time, because steps nest and a plain total counts the same microsecond a dozen times; self time is the column that sums to the whole. The innermost geometry is counted rather than timed — two `GetTimestamp` calls around a rectangle test cost more than the test. Off by default: disabled it is one static bool read, and the bench reports what the instrumentation cost by running the same work with it off. **Measured at 0,9 to 2,5% on top**, over three bare passes with the median reported, because a single pass wandered far enough to show the probes running *faster* than no probes at all — a negative overhead, which is only ever noise wearing a number.

<a id="m48"></a>
**M48** — The three scenarios of M47, median of three: Crucible **2 499 ms** (31,2 ms an order), Broken Country **2 347 ms** (29,3), Long March **764 ms** (9,6); all eighty routed on every field, 18 to 26 of them pressing through. **`BodyScan` — the walk over every body on the field inside `IsClearLine` — is 67 to 71% of all planning time on all three**, at ~25 µs a call across 71 712 calls on the Crucible. Everything else is small by comparison: `StandCheck` 17%, `GrazeAlong` 6%, the search itself (`Hunt`) 4%, the ladder 2%. **The ground is no longer the bill** — `GroundClear` is 0,6% at 0,81 µs a call and `PassableTable` 0,2% at 0,17, which is M46's summed-area table doing exactly what it was built for and is the clearest confirmation it worked. **Twenty-five microseconds to walk eighty bodies is 320 ns each**, for what ought to be a distance compare — so the cost is not the geometry it guards but the guard: `UnitsOnField()` is a yield method allocating an iterator on every one of those 71 712 calls, `DistanceToSegment` takes a square root per body, and `WhereItIsStanding` runs a full `OrientedRect.Overlaps` before anything has been ruled out. **This independently confirms the broad-phase finding from the review worktree** — which reached the same conclusion from reading and is not yet merged — and puts a number on it that reading could not. The lever is the same one M46 named and deferred, approached from the other end: not fewer legs, but a cheaper question per body.

<a id="m49"></a>
**M49** — M48 measured `RoutePlanners.Default` only, which is one of five, so the same eighty orders were run again on all three fields against every planner in `RoutePlanners.All`. **`BodyScan` is 59 to 90% of self time for all four planners of the visibility-graph family, on all three fields** — the ladder 83 to 90% (highest, because it has no search to dilute it and prices zero legs), tangents 67 to 72%, over corners 61 to 63%, places-and-fronts 59 to 60%. So M48's conclusion is a property of the shared `Marching.IsClearLine` all four call, not of any one planner's strategy, and the lever it names is worth four times what M48 could claim. **`GroundClear` never exceeds 2,3% for any planner on any field**, which is M46's summed-area table confirmed a second time and across approaches. Cost per order, cheapest first and stable in that order on every field: ladder 5,2/13,3/15,1 ms, tangents 8,7/30,4/28,7, over corners 12,7/62,3/57,2, places-and-fronts 15,8/63,5/66,0. **The hybrid of [M41](#moving) is the exception in three ways.** It costs 1 034 to 2 310 ms an order, 35 to 267× tangents; it is the only planner that fails to route (62, 72 and **48** of 80, where every other returns 80 of 80); and it is the only one that gets *dearer* with distance — worst on the Long March, where every other planner is cheapest — because it searches a lattice over ground while a visibility graph only counts the bodies in the way. That inversion is the strongest evidence yet that "the crowd is what costs" is a property of the graph family rather than of the problem. **And its every row reads 0,0%, which is a hole in the instrumentation, not a result.** The hybrid owns its geometry and calls none of the probed code — zero clearance checks recorded — so `PlanningProfile` can say what it costs and not one thing about where. Anyone reading that row as "the hybrid spends nothing" would be reading the profiler's blindness. Probing it is deferred: on these numbers it is not a candidate to play with.

<a id="m50"></a>
**M50** — `claude/pathfinding-perf-review-a1eb01` branched off `main` and was five commits: a cached `OrientedRect.Forward`/`Right` and `BoundingRadius`, `Sweep.Touches` (85% of sweep axis tests were halving steps refining a distance no caller read), a heuristic asked of the movement model rather than the terrain catalogue — the two disagree whenever the model is not the plain one, and A* had degenerated toward Dijkstra, 70 129 cells against 466 — a spatial index behind "who is near this line", and five fixes inside `RouteSearch`. **The last of those had largely been reached twice.** This branch had independently arrived at the binary-heap frontier, at a leg cache shared across both `mayPress` passes and reused across plans through `battle.PlanningScratch`, and at hoisting pace and turn rate out of `Seconds` into `Going`. So `RouteSearch` resolved to this branch's version, which also carries [M36](#moving)'s tangency pruning that the other never had, and only the one genuinely absent fix was ported: **face-on is priced first and the two flank presentations are asked about only where it will not serve** — they exist to put the narrow dimension through a gap the front will not fit, so where the front fits they are two more fronts to the same place, each a state and three geometry questions. **One improvement was deliberately not taken.** Pricing the press with a `ShoulderTollSeconds` charged once on top of the pace rate, replacing the two-search gate, is a change to [M18](#moving) and [M26](#moving) rather than to their speed: it changes which routes come back, and the toll's value moves the approach gate between 15 and 17 of 19. That is the designer's number ([W4](#how-to-work)), so it stays unmerged and unasked-for rather than arriving inside a merge. **Gates.** Approach angles 17 of 19 on the default planner before and after, twice each, and unchanged for all five planners (ladder 7, places-and-fronts 17, corners 10, tangents 17, hybrid 13). Suite 581 passing, 20 skipped, **the same 15 failures**. Re-benching against M48 and M49 is still owed — no speed claim is made here that this branch has measured itself.

<a id="m51"></a>
**M51** — [M50](#moving) re-benched against [M48](#moving) and [M49](#moving), same three fields, same eighty orders. Per order, before → after: tangents **31,2 → 12,7 ms** on Broken Country and **9,6 → 4,0** on the Long March, the ladder **15,1 → 3,8** and **5,2 → 1,8**, over corners **57,2 → 26,3**, places-and-fronts **66,0 → 30,1** — between 1,9× and 4× everywhere. **`BodyScan` fell from 25 µs a call to 8,7**, and with it from 67–72% of planning to 38–42% for tangents and from 89% to 69% for the ladder. **Routes did not change**: pressed-through counts are identical to M49 on all twelve non-hybrid rows (32/26/32/26, 30/24/32/24, 4/6/6/18), which is the strongest check available that a 2× came from asking the same questions faster. Legs priced fell 17–23% and states 18–25% from the presentation gate alone, with candidate places identical at 1 964. **And the finding turns over.** For over corners and places-and-fronts, `StandCheck` is now the *largest* step at 39–41%, above `BodyScan`; for tangents it is 33% against 38%. It did not get faster — 12,9 µs a call, unchanged. **The stated cause was wrong and is corrected in [M52](#moving):** the review branch *had* indexed `RouteSearch.CanStandHere`, and the merge dropped it. `UnitsOnField()` was also not the allocating iterator by then — [M40](#moving) had already made it a struct enumerator — so the defect was the scan and the square root only. `GrazeAlong` is third at 29 µs a call. **Two honest caveats.** The Crucible's median-of-three spread was **53%** this run (1 057 / 1 126 / 1 622 ms) and its two measurements of the same work disagree by 42%, so that field's absolutes are not usable — Broken Country and the Long March agreed to within 2%, and they are what the numbers above are read from. And the hybrid came out 3–15% *dearer* on all three fields, which has no mechanism: it shares no code with anything merged, builds no `OrientedRect`, and never calls a clearance check. Recorded as unexplained rather than attributed.

<a id="m55"></a>
**M55** — The bench asked only the default planner, never passed `arriveOn` though the game always does, and counted a route as good if it came back `Found`. A conformance harness now asks **every** planner the same eighty orders on all three fields with `arriveOn` set, and proves every leg against the rectangle that will travel it (M12) on the front it will hold (M23). **What that showed, first pass:** the hybrid was the only planner with **zero unwalkable routes and zero press-throughs on all three fields**; every other planner returned **28–34 unwalkable routes of 80**. Unwalkable minus pressed — routes nothing declares — was 8–10 for tangents and places-and-fronts, 0–2 for the ladder and corners, **0** for the hybrid. That is the seizure report: the plan comes back found, the executor refuses the leg, the regiment stands still. **Why the cheap planners press.** Route cost on the Crucible was ~486 s for everyone except the hybrid at 644 s. Shouldering through is genuinely faster, and M18/M26 price it as a peer — so 40% of orders chose it. `Mx2c` says a press is Priority 3, a last resort, which the cost model does not express. **The fix, and it is the designer's own:** the staged planner asks the pose search before it is allowed to press. **0 unwalkable and 0 pressed on all three fields**, 80 of 80 routed, at a cost of 16,8 / 98,0 / 141,7 ms an order — correct, and far too slow. **Levers measured and rejected:** `MarginMetres` 4→8→14→20 (Crucible and Broken Country stay at 32–36 unwalkable at every setting); `HeuristicWeight` 2→4→8→16 (worse on both axes — 93→186→253→277 ms, and unwalkable rises 0→1→11→12); a corridor drawn round the tangent route (win rate 0 of 34 at 40 m, rising only to 18 of 34 at 400 m — the hybrid's answers genuinely lie hundreds of metres off, and a *failed* bounded search burns the whole budget, so 400 m is slower than no bound at all); and a bounded-then-widen escalation (146 ms against 93, because widening fired on 28 of 28 Long March orders). **The one that half-worked:** a corridor traced from the hybrid's own obstacle grid, which knows the way round — 24 of 32 won on Broken Country at 11,7 ms an order, 14 of 34 on the Crucible, **0 of 28 on the Long March**. Kept as a lever (`CorridorHalfWidthMetres`), defaulted off. **Two things the harness found that nothing was looking for:** `TryStageForDirectRun` never fires once in 240 orders, and the pose search wins **100%** of the orders it is asked — there is no case where the cheap planners fail and the lattice also fails. **Still open:** speed. A hundred simultaneous orders would cost 1,7 to 14 seconds.

<a id="m56"></a>
**M56** — [M55](#moving) left the pose search correct and unaffordable at 141,7 / 98,0 / 16,8 ms an order. Measured, the cost was one function: **`HybridClear` at 72–75% of all planning time**, ~158 000 clearance sweeps an order at 1,20 µs each. The sweep is already circle-rejected, axis-cached and culled once an expansion — the cost was the **~18 300 states expanded**. **The diagnosis.** Pure Dijkstra costs 181–261 ms an order against 76–126 with the heuristic, so the heuristic bought 2–3× where A* over a space this size should buy one to two orders of magnitude — and raising `HeuristicWeight` made it *worse on both axes*, which a weak heuristic does not do and a misleading one does. The mechanism was written in the heuristic's own remarks: it "charges one change of front where a route round two bodies needs three", while for these units a 90° change of front costs about twenty seconds and ninety metres of marching nineteen. It was roughly right about ground and blind to the thing that decides the bill. **The fix — `HybridTurnField`.** Solve the relaxed problem properly instead of estimating it: states of (cell, direction of travel) over eight directions, edges of one cell of march or a pivot onto an adjacent direction priced through the lattice's own `SecondsToPivot`, Dijkstra outward from the goal. A point robot carrying a swollen body, so it stays optimistic — the direction a heuristic is allowed to be wrong in. **And two smaller cuts.** The shorter of the two step lengths now contributes a march and no wheels, since 30 m and 22 m gave the search two ways to say the same thing at nine primitives an expansion; and the hop-count field is no longer built at all when nothing reads it, which was six milliseconds an order spent on an estimate the turn field had replaced. **Measured, least of three runs, `arriveOn` set:** Crucible **98,0 → 17,6** ms an order, the Long March **141,7 → 63,7**, Broken Country **16,8 → 5,7** — 2,2 to 5,6×, with **0 unwalkable and 0 pressed on all three fields** unchanged and route cost slightly better (643,4 → 632,4 s on the Crucible). **Sharing was tried and does not pay.** A field is a function of the goal, the distance to it and every body's arrangement, and a mover must be left out of its own clearance set — so no two regiments of one army have the same set. Including the mover to make the sets match, and centring the grid on the goal so different starts could share, gave **built 80, reused 0** when orders cross the field, **80 and 0** for a wing sent to one block on two fields and **76 and 4** on the third, while the goal-centred grid it needed cost Broken Country 5,9 ms an order against 17,5. Reverted; recorded in the class so it is not re-derived. **Still open:** the Long March at 63,7 ms an order is a hundred orders in 6,4 seconds, so the target is not met there. Gate unchanged at 7 / 17 / 10 / 17 / 5 / 16; suite 598 passing, 13 failing against a 15 baseline.

<a id="m57"></a>
**M57** — The designer's question: if the ladder decides by casting a ray and then asking whether the body fits, why is every other planner building and testing orientations instead of asking the same O(1) fit? **Half of it was already there and half was the win.** `PoseIsClear` already had the *certainly clear* half — two circumscribed circles too far apart to touch, turned back by one subtraction. What it lacked was the same question asked one level up. Clearance cost `samples × nearby`, because `Standing.Nearby` is chosen once per **expansion** and so has to bound the furthest-reaching primitive there is — while a pivot moves the centre nowhere and a step back eight metres, and both were paying the thirty-metre march's bill at every sample. **The fix.** The mover's centre travels a circular arc, so the chord from first pose to last, widened by how far the arc leans off it — its midpoint, since that is where a circular arc leans furthest — contains every position the sweep will visit. A body further from that chord than the two circumscribed radii cannot be reached by any pose along it whichever way either is pointing, so it is dropped **once for the whole primitive**. **Measured, least of three, `arriveOn` set:** the Long March **63,7 → 47,0** ms an order, the Crucible **17,6 → 13,5**, Broken Country 5,7 → 4,9; worst single order 361 → 249, 218 → 140, 97 → 62. Route seconds **identical to the decimal** on all three fields (1 233,1 / 632,4 / 786,0), which is the strongest available check that this removed cost and not answers. **The other half was built and does not pay.** Inside the sum of the two *inscribed* radii two boxes overlap however either is turned, so that pair could be refused with no axis projected — but it fires almost never, because a lattice rarely proposes a pose buried inside a body, and it costs a compare at every pose that is not. Measured four ways on each field: neither test 63,2 / 16,2 / 5,4 ms an order, the cull alone 46,8 / 13,7 / 6,1, the inscribed test alone 58,5 / 16,4 / 5,6, both 50,5 / 13,4 / 5,7 — so the cull is the whole effect and the inscribed test is a small loss on the field where there is most to gain. Removed, and recorded where it was so it is not rebuilt. Gate unchanged at 7 / 17 / 10 / 17 / 5 / 16; suite 598 passing, 13 failing.

<a id="m64"></a>
**M64** — [M62](#moving) made a plan safe to work out beside other plans; this is the click actually doing it. `BattlefieldController.PlanRoute` did four separable things in one method — worked out where the regiment can stand, planned the route, said what it had done, and gave the order — and only the first two read the battle without writing to it. Split into `WorkOutRoute`, which reads and returns, and `ApplyRoute`, which does everything that touches the console, the overlay, the status line or the regiment. `MarchSelection` now works the wing out at once and applies the results in the wing's own order, so nothing about what a player reads or sees changes. **Measured on eighty regiments box-selected and sent to one block, least of three, twelve cores**, and this is the whole click including the placement search the bench never ran: the Long March **308,5 → 86,9 ms** (3,5×), the Crucible **610,7 → 140,9** (4,3×), Broken Country **653,2 → 96,8** (6,7×). **Three things the split needed, each measured rather than reasoned about.** The planner's log had to stop being the console — eighty plans appending to one list together is a corrupt list, not an interleaved one — so each plan talks into a `HeldBattleLog` that is replayed on the main thread in the wing's order, which keeps every line and its position. The lazily-built passable-ground table had to stop being a plain dictionary; a read during another thread's resize does not reliably throw, it returns nonsense. And the profiler marker moved out of the per-regiment path, because a marker begun on a worker thread has no scope to close on the main one. **Two behaviours deliberately left alone:** instant movement teleports each regiment as its order is applied, so a later plan in the same wing really does depend on the earlier ones, and that debug option keeps its queue; and a wing of one is still planned the old way. **What proves it is a comparison, not an assertion about locks.** `WingOrderTests` plans the same wing both ways and compares every destination, every found-or-not and every route's seconds, and separately checks the assumption the split rests on — that giving one regiment its order changes nothing about the next one's route — by running the old interleaved order of operations against the new one. **One attribution corrected in passing:** a real click costs more an order than the bench does, and it is not the placement search. Narrowing `CanStandThere` by the spatial index was tried on that reading and measured at 536,6 ms against 555,4 for eighty, which is noise; the profile puts that whole step at 11,5 ms of 630. The real difference is that a wing sent to one block reaches the lattice **forty-six** times where a wing sent across the field reaches it thirty-two. The change was reverted and the measurement left at the site. **And the Unity-side check from [M44](#moving) had quietly stopped working.** It compiles `Assets/Scripts` against the editor's reference assemblies with Roslyn, and the Roslyn it pointed at is `Tools/Roslyn/csc.exe` at version 2.9 - a C# 7.3 compiler, which cannot parse a switch expression and so reported seventy-six syntax errors in files nobody had touched. A check that fails the same way whatever you change is not a check. Pointed at the installed editor's own compiler instead - `Hub/Editor/6000.5.6f1/Editor/Data/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll`, run through `dotnet`, against that editor's `Managed/UnityEngine` and `NetStandard/ref/2.1.0` - and the Unity half compiles clean, which is what made this change checkable at all.

<a id="m65"></a>
**M65** — [M55](#moving) read `Mx2c` — "if clean movement of the blocking regiment is not possible, a press-through is initiated" — as an ordering with no price attached: any clean route beats any press, however dear. That killed the seizure bug and bought a worse one. **Recorded in play, tick 651:** a regiment ordered **239 m** across open ground with **one** of its own on the line walked **1325 m in 847 s** to avoid a press the ladder priced at **151 s**. Five and a half times round the houses. A player does not read that as good order; they read it as the regiment refusing the order, which is the same complaint the seizure bug produced by the opposite route. **So the priority holds, but not past a multiple.** Below the ceiling the way round wins however much dearer it is, which is `Mx2c` intact for every ordinary order; above it the press is taken and declared, which is what making a press *visible* was always for. Both sides are priced by `Marching.SecondsToWalk` with their own held fronts, because that is the executor's model and the only currency the planners share — a planner's own idea of what its route cost is not comparable with another's, and comparing metres against seconds is how a route cheaper on paper turns out five times longer to walk. **Where the line goes, swept on eighty orders a field, one click sending the whole wing to one block:**

| ceiling | Crucible press / worst detour | Broken Country | Long March |
|---|---|---|---|
| off | 10 / **4,6x** | 7 / **4,6x** | 0 / **3,5x** |
| 2 | 22 / 2,0x | 16 / 1,9x | 2 / 2,6x |
| **3** | **16 / 2,7x** | **11 / 3,0x** | **1 / 2,8x** |
| 5 | 10 / 4,6x | 7 / 4,6x | 0 / 3,5x |

**Three is the value taken, and the reasoning is about the worst route rather than the average.** Nobody remembers the mean; they remember the one regiment that went round the houses. Three cuts the worst detour from 4,6x to 2,7x for six extra presses in eighty orders, and mean route cost falls with it (446,9 to 416,8 s on the Crucible). **Five would have fixed the reported bug and changed nothing on any bench fixture** — the recorded case was 5,6x and every bench worst is under 4,7x — which makes it the zero-risk choice and also the one that leaves a 4,6x detour standing. **Below two the ceiling stops discriminating:** at 1,25 it presses thirty of eighty on the Crucible, which is `Mx2c` inverted rather than priced. **What the bench does not settle** is whether twenty per cent of a wing shouldering through reads worse on the field than one regiment going the long way. That is a play-test question, and the value is one static.

<a id="m66"></a>
**M66** — The lattice returns its motion primitives at their sample spacing, and that was going out as the route. **Recorded in play:** a route of **154 waypoints** from a search that expanded **120 states** — more points than the search had thoughts. They are not decisions; they are the shape of the sampling. Every one is a heading the walker turns onto, so they cost seconds as well as reading as a wobble, and the two recorded way-rounds both finished **92° and 132° off the front they were given**. The smoothing pass was in the original design (step 5) and was never built. It casts ahead from each anchor, **furthest point first** — stopping at the first clear one keeps most of the wobble it exists to remove — and takes a shortcut only when two things hold: the rectangle that will travel it is proved clear by `Marching.IsClearLine` **on the new front**, because dropping a point drops its heading too; and the straight leg costs fewer seconds than the stretch it replaces, because a straight line is always the shorter distance and can still be the dearer walk. **The first leg is never touched.** A regiment beginning inside one of its own is allowed out of the overlap it starts in, and that licence belongs to the leg the search planned, not to a longer one drawn through the same crowd afterwards. **Measured on the conformance harness: no route became unwalkable on any field, and route cost fell slightly everywhere** (Long March 1155,8 to 1155,2 s, Crucible 633,3 to 627,3, Broken Country 786,4 to 780,3). The gain is in the shape rather than the clock, which is what it was for. **Corrected the same day, and the first placement was wrong.** Put inside the lattice, because the lattice was where the 154-waypoint route came from. Then a recording showed a **four-waypoint ladder route walking left, up and left again** to a destination one diagonal away — `(321,1755) → (249,1749) → (241,1959) → (39,1948)`, priced 340 s against 233 s straight — and the pass that would have straightened it was never asked, because that route came from a different planner. **A route's shape is not the property of whichever search happened to produce it**; it belongs to whoever hands it to the executor, which is `StagedRoutePlanner`. Moved to `RouteSmoothing` and applied to every route that planner returns, proved afterwards against the same walkability gate the route itself had to pass, with the wound route standing if the straightened one fails it. **Two things the move paid for that the first placement did not.** A press-through is never smoothed — two points and a declared decision. And the lattice's route is no longer smoothed *before* [M65](#m65) may throw it away: on the recorded frozen order that alone took the plan from **150,5 ms to 102,2**, because the dearest pass was being run on the answer least likely to be used.

<a id="m67"></a>
**M67** — `ComparePlannersOnOrders` planned all five planners on every real order, not only on previews, and it shipped on. **Every frame over 90 ms that the game itself caused was a five-route frame for one regiment** — 547 ms at tick 651, of which 520 was planning; 221 ms at 2037; 169 at tick 6. The comparison is worth its cost when reading one route and worth nothing while playing, and leaving it on made the planner look like it cost four times what it costs. Defaulted off. **What this also settles:** the three genuinely huge frames in the same recording — 12 063 090 ms, 10 366 ms, 2 620 ms — all report `planning 0,0 for 0 routes` with the whole cost in `rest`, so they are the editor pausing and not the game. A frame number is not a measurement of anything until it is split by where the time went.

<a id="m68"></a>
**M68** — [M65](#m65) made the lag worse before it made it better, and this is the measurement that says why. The lattice's hundred-thousand cap was a safety valve set where nothing was expected to reach it. Recorded in play on the Great Field, orders were reaching **31 640 expansions and costing 888 to 1080 ms** — and then having the answer refused for being five times dearer than the press. **A search whose answer is going to be thrown away should be allowed to give up.** The diagnostic is in the recording itself: every order that logged `found no lattice route` cost **17 to 113 ms**, and every order that did *not* cost **888 to 1080**. Failing was already cheap; succeeding was the expensive case, and succeeding was the case whose answer nobody took. **Two things were tried first and neither worked.** A seconds limit passed into the search, pruning any node already past what the ceiling would allow: measured at 179,8 ms against 169,5 unbounded, because the prune only bites late and a bounded search that *fails* explores everything under the bound. And smoothing the lattice's route before the ceiling could reject it — the dearest pass run on the answer least likely to be used, worth 150,5 ms to 102,2 when moved, but not the bulk of it. **What the sweep says**, both fixtures, three fields:

| cap | Crucible one click | Broken Country one click | Long March one click |
|---|---|---|---|
| none | 55,08 ms — 17 unwalkable | 36,28 — 11 | 4,85 — 1 |
| **20 000** | **15,93 — 17** | **12,91 — 11** | **4,28 — 2** |
| 10 000 | 13,65 — 17 | 9,65 — 11 | 2,98 — **5** |
| 2 000 | 6,41 — **30** | 5,92 — **16** | 1,26 — 5 |

Twenty thousand is where quality stops being free: the Crucible and Broken Country are **byte-identical in outcome** — same unwalkable, same pressed, same route seconds, same worst detour — at three and a half times cheaper. At ten thousand the Long March starts pressing routes it used to walk. On the three recorded frozen orders the final route is identical at *every* cap down to two thousand, which is the point: those searches were spending a second to produce an answer the ceiling refused. **What this does not do is remove the freeze.** On the Great Field the same cap takes the recorded worst order from 140,5 ms to 101,5, not to nothing. A single order costing a tenth of a second is a hitch whatever the cap is, and the answer to that is to stop planning it on the frame that asked — [M64](#m64) already proved a plan can be worked out off the main thread, but wired it only for a wing, and a lone order is still planned where the player is waiting.

<a id="m69"></a>
**M69** — [M68](#m68) shrank the dear orders and did not remove the hitch, because **no cap removes it**: a single order that costs a tenth of a second is a dropped frame, and mid-game the recorded worst was 888 ms. [M64](#m64) had already proved a plan can be worked out beside other plans and wired it for a wing; a lone right-click was still planned on the frame that gave it. Now every order goes to a worker and is applied when it lands. **The rule that makes it safe is that the clock waits.** Working a route out reads the whole battle — every position, every shape, the spatial index — and a tick writes to all three. The wing order got away without a rule because it began and finished inside one frame with nothing in between to move anybody; across frames that is no longer true. So `AdvanceClock` returns while a plan is out, and the accumulated time is dropped rather than banked, or the battle would fast-forward through every tick it owed the moment the plan landed and undo the point of waiting. **What that trades is a fraction of a tick of simulation for a frame that never stops.** Drawing, the camera, the panel and the selection keep their sixty a second through an order that used to hang the game for a second. **Every other writer settles first**, and there are three: the mouse handler, which covers the attack, the preview and the march in one place; the formation and stance keys; and instant movement, which teleports and so cannot be worked out beside anything and is left exactly as it was. A second order given while the first is still out waits for it — the one place the old freeze survives, and the right place for it, because the alternative is planning against a field another order is changing. **Two races the move exposed, both real and neither visible.** `PlanningBudget.Spent` is called from the worker while `OpenFrame` runs on the main thread, and between them they write and clear four collections; a read during another thread's resize does not reliably throw, it returns nonsense, which is how the last bug of this shape took four attempts to find. One lock over the whole class, because contention is a handful of calls a frame. And `battle.RoutesPlanned++` and `RoutePlanningTicks +=` are a read, an add and a write, so two workers finishing together lose a count — both interlocked, because a diagnostic that quietly undercounts is worse than none. **What is not covered:** the tick's own re-planning still runs on the main thread inside the tick, governed by the budget as before, so a stutter *while* a wing is marching is a different thing from a stutter when it is ordered, and this changes only the second.

<a id="m70"></a>
**M70** — [M68](#m68) capped the search at twenty thousand expansions and left the obvious question unasked: **why does an order 315 m long reach twenty thousand at all?** The lattice bins ground at 20 m and heading into 16, so the Great Field holds **172 800 states**, and instrumenting where the search actually went answers it — an order asking for **282 m** explored **841 m sideways and 666 m past its own ends**, a box some 1700 m across, for 31 640 expansions. **Nothing bounded the search to the ground the order was about**, and the route it came back with was the five-fold detour [M65](#m65) then threw away. The wandering and the silly routes are one fault seen twice. **The metrics had to be rebuilt before any of this could be read.** Earlier sweeps scored a press-through as damage, which contradicts M65 — a press is the right answer when the way round is too dear — and counting it as damage made every cheapening lever look worse than it was. What is measured now is the **worst single order in milliseconds**, which is the freeze; **unwalkable**, which is the only genuine failure because it is a regiment that stands still; and the **worst detour**, which is the silly route. Presses are reported and not judged. **Two fixtures, because one of them cannot see cost:** the seven orders recorded as dear in play all press whatever the setting, so they measure speed alone and a lever can look free there purely by failing sooner; the bench's eighty one-click orders are where the lattice wins some, so a lever is only free if it is free there. **Six levers measured, full tables in `docs/pathfinding-levers.md`.** The headline: `StrayMultiple` 1,5 plus `Headings` 12 plus a ten-thousand budget takes the recorded orders from **135,7 ms to 4,0** and the Crucible's worst order from **322,7 to 37,4**, while unwalkable stays at 17, the worst detour improves from 2,65x to 2,63x, route cost improves from 33 294 s to 32 937, and Broken Country's unwalkable improves from 11 to 10 — **equal or better on every quality metric measured**, which is the only reason to prefer it to the cheaper-looking rows. **Two findings worth more than the recommendation.** The heading count is the largest effect and the least understood: sixteen bins to fourteen is a sixteen-fold cut on the recorded orders, which no state-space argument predicts, and Broken Country's unwalkable runs 13, 10, **20**, 12, 16 as bins fall — so no value here may be interpolated, only measured. And the expansion cap, the lever already shipped, is **the crudest of the six**: at a thousand it buys 14,5 ms for 31 unwalkable against 17, where the geometry levers buy more for nothing. It is kept because it is the only one that bounds *worst-case* time. **Recorded as a menu, not a decision** — nothing is switched on, and what none of these fix is the detour: the worst route stays near 2,6x at every setting that keeps quality, because that is the M65 ceiling and not the search.

<a id="m71"></a>
**M71** — The menu from [M70](#m70), chosen. `StrayMultiple = 1,5`, `HeadingBins = 12`, `PoseExpansionBudget = 20 000`. Against the search as it was with none of them: the orders recorded as dear in play go from **135,7 ms to 4,0** and the Crucible's worst order from **322,7 to 37,4**, while unwalkable stays at 17, the worst detour improves from 2,65x to 2,63x, route cost improves from 33 294 s to 32 937, and Broken Country's unwalkable improves from 11 to 10. **Equal or better on every quality metric measured**, which is the only reason to take it over the cheaper-looking rows. The three do different jobs and none of them substitutes for another: the stray bound stops the search wandering — an order asking for 282 m was exploring 841 m sideways — but does not bound how long it may take inside the bound; twelve headings is the largest single effect and the least understood, sixteen to twelve being a sixteen-fold cut that no state-space argument predicts; and the expansion budget is the only one of the three that bounds **worst-case** time, because it is the only one that stops a search that would otherwise run to exhaustion. **What was rejected on re-measurement, and this is the finding worth keeping.** M70's table said a 30 m ground bin looked free — 16 unwalkable against 17 on the Crucible, and the total down a quarter. Swept again on all four maps *on top of the settings above*, it is not: the Crucible holds at 17 but Broken Country goes **10 to 13** and the Long March **1 to 4**, and 40 m and coarser is bad everywhere (31, 29, 3). **On the Great Field the bin now changes nothing at all** — 4,0 to 4,4 ms at every value from 20 to 60 — because the other three levers have already taken that map to four milliseconds and there is nothing left for it to buy. So the bin stays at 20 m, and the lesson is that **a lever measured against one baseline says nothing about another**: every row in M70 was taken against the unbounded sixteen-heading search, and re-running the promising one against the chosen baseline reversed it.

<a id="m72"></a>
**M72** — [M69](#m69) put a player's order on a worker and made the clock wait for it, and the lag did not go away. The reason was one line in `Update`: the formation and stance keys can rewrite a regiment's footprint or its stance while a worker is reading it, so a settle was placed in front of them — **outside** the test for whether a key had been pressed. It therefore blocked on the worker **every frame**, which is the whole of the off-thread pass undone: the plan was computed elsewhere and then immediately waited for, one frame later, as if it had never left the drawing thread. The rule is that **a plan is settled where its result is consumed, never on the path that merely might consume it**. Both handlers now settle inside themselves, after the key test — formation once a number key is actually down, stance once `chosen` is non-null — which is the same protection at the same instant, without the per-frame wait. The right-click settle in `TrackOrders` was already correct and is unchanged, being inside the button test. **The lesson to carry:** a correctness guard placed one level too early does not read as a bug, because the guard itself is right and the code is right; only the cost is wrong, and cost does not fail a test.

<a id="m73"></a>
**M73** — The designer's proposal, measured: build no mesh, because the rectangles are already on the field; cast from the start to the corners of every body near the drawn line, pushed out by the mover's own radius; from each corner that can be reached, cast on to the destination; a clear pair is a route, and its cost prices it. Asked before the lattice on 240 one-click orders across three fields, with the lattice then run anyway so both answers could be compared. **Three findings, and the second is the one that matters.** *First, it is as cheap as hoped:* 0,8 to 2,9 ms at worst against tens of milliseconds of lattice, 17 to 45 ms total against 2 100 to 4 200. Raycasts really are nearly free. *Second, when it says yes it is never wrong — <b>0 of 132</b> false accepts across every field and both lever settings — and the route it hands back is <b>cheaper than the lattice's on 128 of those 132</b>, averaging 0,96 to 1,00 times the cost.* The lattice, on the orders where a two-leg cast succeeds, is spending tens of milliseconds to find nothing better than two straight lines. *Third, it is a poor refusal.* It discards 21, 25 and 31 of 80 routes the lattice would have found and the ceiling would have kept, and 8, 19 and 1 of those were found in under 20 ms — the explicit condition the proposal set for itself. The reason is visible in the numbers: in every discarded case **no** pair was clear at all, not merely a dear one, because the cheapest way round genuinely bends three or four times and no two-leg cast can see one of those. Allowing a second bend, over candidate points merged at 15 m and stopping at the first route under the ceiling, recovers **two** of twenty-one on the Crucible and none at all on the other two fields, for three times the cast cost. **So it is not a gate, it is a stage**: taken as an accept-only rule it removes 55% of lattice calls at no quality cost and a small quality gain, and taken as a refusal it costs a quarter of all way-round routes. The freeze is in the other 45%, where the cast says nothing and the lattice still runs to exhaustion, so this does not replace a bound on the search — see [M72](#m72) and the note there about where cost hides.

<a id="m74"></a>
**M74** — [M73](#m73) tuned, and its obvious extension refused. **The parameters barely matter.** Aiming **8 m** past a corner rather than 4 is the only gain and it is small — one more order accepted on the Crucible, one fewer discarded, and a third off the cast's own cost, because a wider aim needs fewer surviving candidates. Sixteen metres is worse on every field (37 accepted against 41, and one Broken Country order spikes to 14,8 ms), and two is indistinguishable from four. Merging candidates within **15 m** is right: turning merging off doubles the points and changes not one verdict, and merging at 30 halves the points but costs an acceptance. **Adding the middle of each side to the corners buys nothing at all** — identical accepts on all three fields for sixty percent more points and time — which says the corners already name every aim that matters, so a gap's mouth is found by aiming at the corners that form it rather than at the space between them. **Fencing the lattice into the candidate points is a clear loss, and this is the finding worth keeping.** The idea is sound on its face: the corners name the only ground a way round could use, so the search has no business at the map edge. Swept at 40 to 220 m, both alongside the stray box and instead of it, it **never won on any field at any width**. At its most generous it keeps 56, 60 and 58 clean ways round against **61, 69 and 79** unfenced, while costing more total time than the box it would replace, plus 190 to 320 ms to build. The cause is exactly the one that makes the cast a poor refusal in [M73](#m73): the candidates come from bodies near the *drawn line*, and the routes worth having swing wide round bodies that are not near it — one fault, showing up twice. And both halves of what the fence was for are already covered: the map edge by [M71](#m71)'s stray bound, and the inside of a body by the collision test that rejects the pose. **The general lesson: a bound derived from a cheap answer inherits that answer's blind spot, and applying it to a search that does not share the blind spot removes the very cases the search was kept for.**

<a id="m75"></a>
**M75** — The third and last way of making the cast help the lattice, and the reason all three failed. [M74](#m74) refused the cast's *geometry* as a fence; this tried its *cost*, which cannot fail the same way — a clear pair of casts is a route in hand, so nothing dearer is worth finding, and a ceiling can only refuse routes worse than one already held, never exclude ground. The ceiling today is `press x 3` from [M65](#m65), sitting at 1 800 to 3 000 s against routes that come in at 600 to 1 000, so there was a large gap to close. **Closing it does nothing at all**: handed the cast's own cost, the cast's cost less five percent, or less twenty, the lattice expands 220 380 states against 220 995, and total time moves inside the noise on all three fields. **Why, and this closes the whole line of enquiry.** Split the orders by what the cast found: where the cast finds a route the lattice expands **4 to 15 states and takes about 5 ms**; where the cast finds nothing it expands **3 030 to 5 650 states and takes 54 to 93 ms**. Four hundred times the work, on exactly the orders the cast cannot see. So the ceiling only ever tightens on orders that were already trivial, and the runaway searches never receive a bound because there is nothing to derive one from. **The cast and the lattice are correlated, not complementary** — they succeed and fail together, for the same reason, which is whether a simple way round exists. That also explains [M73](#m73)'s "0 of 132 false accepts" without appealing to the cast being clever: an order a two-leg cast can answer is one the lattice answers in four expansions. **The rule to carry: a cheap oracle that agrees with an expensive one cannot bound it — usefulness requires disagreement, and this pair has none.** The freeze therefore still wants a bound on the axis that actually terminates these searches, which is time.

<a id="m76"></a>
**M76** — The freeze, bound at last, on the only axis that tracks it. Splitting one plan by stage across the seven orders a recording caught: on the two that froze the lattice is **96% and 97%** of the whole plan — 442,6 ms of 461,0 and 386,1 of 399,4 — while on the five that did not it is 13% to 45%, with the tangent graph and the ladder the same size or bigger. Nothing else in the cascade moves between a 12 ms order and a 461 ms one, so there is one place to put a bound and this is it. **Why time and not a count.** [M70](#m70)'s whole menu of levers capped expansions, and every one of them missed, because an expansion is not proportional to anything a player feels: the same **11 947 expansions cost 387 ms in play and about 20 ms on a bench**, since each one sweeps against however many bodies happen to be near. A number that swings a hundredfold against the clock cannot be capped to a time. So the search now reads a stopwatch, every 64 expansions — cheap, and a fraction of a millisecond of granularity against a limit measured in tens — and the deadline is computed once before the loop so a lever changed mid-flight cannot move the finish line under a search already running. **Why giving up is safe.** A refused search is not a regiment standing still: the staged planner falls back on the press-through the ladder already holds, and [M65](#m65) says a press is a legitimate answer rather than a failure. What is lost is the nicer way round on an order that was going to take a fifth of a second to find one — and on that recording the lattice won **none** of the seven anyway. Out of time reports as `SearchBudgetExhausted` rather than a reason of its own, because both mean the search gave up before exhausting the map and the cascade treats them alike; the log names which it was, since the two want opposite fixes. **The ceiling is 15 ms a search and that figure is a guess the play-test is meant to calibrate** — it is a lever, not a finding. **What it does not fix:** the 10 to 20 ms every order costs before the lattice is asked at all, and a wing that is still N plans in sequence with N ceilings.

<a id="m77"></a>
**M77** — The designer's proposal: a mesh sized to one regiment, and plain A* over it. It works, and the reason is [S2](#shape). **A regiment collides as a 2:1 block** — `Formation.FootprintFor` with `BlockWidthToDepth = 2`, not the 40 m by 6 m *space* the `units.cfg` header describes, which is `SpaceFor` and is a different number for a different purpose. The bounding circle of a 2:1 rectangle is **√5⁄2 = 1,118 times its long side**, and measured across all three bench fields it is 1,12x for every unit type without exception, because they are all the same rectangle scaled. So a cell that holds the circle is **twelve percent wider** than one that holds the frontage, and for that twelve percent **the heading dimension the lattice pays for disappears entirely**: a cell holding the circle holds the regiment however it is turned. *(An earlier draft of this entry said one percent, from the 40 by 6 in the content header. That is the ground the men stand on, not the shape anything collides against; the argument survives at 12% but it is an order of magnitude less free than first written down.)* That is the whole of it: [M43](#m43)'s search carries (x, y, heading) because at a 20 m bin a regiment's front decides whether it fits; at a cell sized to the regiment it does not. **The arithmetic.** The Great Field is 1800 by 2400 m, about 2 700 cells, so exhausting the *entire battlefield* is 2 700 pops against the 4 193 expansions x 8 primitives — 33 500 swept-rectangle tests — that one bad order costs the lattice. Measured at one regiment to a cell, 80 orders a field, least of three runs: **worst single order 3,5 to 4,8 ms and about 1,5 ms an order**, against the 200 to 400 ms [M76](#m76) had to bound with a stopwatch. The grid's worst case is set by the map; the lattice's is set by the arrangement, which is why one freezes and the other cannot. **Why this is not [M74](#m74) and [M75](#m75) again.** Those killed the two-leg cast as a bound on one rule — a cheap oracle that agrees with an expensive one cannot bound it — and the cast's blind spot was that it only looked at bodies near the drawn line. **A grid sees the whole field and has no such blind spot.** It fails on fine detail where the lattice fails on breadth, which is the disagreement M75 said was required and the cast did not have. **Regiments are not all one size, which the cell spacing has to follow.** Measured: spearmen 32 x 16 m, swordsmen and archers 40 x 20, cavalry 68,8 x 34,4, horse archers 70,4 x 35,2 — a **2,2x spread** in frontage. So the spacing is derived per mover from its own footprint rather than written down anywhere: a spearman's grid has 35,8 m cells and a cavalry grid 76,9 m cells, over the same field, and each is right for the regiment asking. **The size is the designer's, and the sweep confirms it.** Swept at 0,75x / 1x / 1,5x / 2x the bounding diameter, counting routes that pass `WalksCleanly`: 48/53/74, **42/52/73**, 40/46/64, 31/46/46. Coarser finds *more* routes and holds *fewer* — at 2x only 21 cells on the Crucible read as blocked against 156 at 0,75x, so the field looks emptier than it is and the search sails through gaps that are not there. Finer holds more but the Crucible's worst order goes to **22 ms**, consistently across three runs. **One regiment to a cell is where quality stops being free**, and it is exactly what was asked for. **Smoothing belongs inside the stage, before the gate.** A hex route is a chain of cell centres and zigzags by construction, so gating the raw line asks the swept rectangle to walk a staircase and it refuses one — a verdict on the grid's shape, not on whether the regiment can get there. Straightening first is **42 routes held against 33** on the Crucible and 52 against 49 on Broken Country, and nothing at all on the open field, which is already at its ceiling. **How big the halo has to be, and one argument of mine that the measurement refuted.** A cell is blocked where the mover's *own circle* would overlap a body, so every body carries a halo of the mover's radius — a body of W by W/2 grows to about 2,1 W by 1,6 W, **roughly seven times the area of the rectangle it came from**. I expected that to be needlessly cautious, on the reasoning that this grid sits behind `WalksCleanly` and a cell wrongly called clear is caught there while a cell wrongly called blocked loses a route with nothing to catch it. Swept at 1,00 / 0,75 / 0,50 / 0,25 / 0,00 of the radius, routes held were **42/45/34/35/33**, **52/55/54/50/47** and **73/73/73/58/43**. Shrinking the halo finds *more* routes and holds *fewer*, and the reason kills the argument: **A\* returns one route and not a menu.** An optimistic grid does not hand back a route that might work — it confidently threads a gap the regiment cannot use, the gate refuses the whole thing, and there is no second-best behind it. A false clear poisons the single answer, so the usual asymmetry does not apply to a search that commits. **0,75 wins on all three fields and is also cheaper** — the Crucible's worst order 8,4 ms against 21,9 — and below 0,5 it falls off a cliff. **Why a lone regiment sits in a blob of blocked cells, which is correct and looks alarming.** Marking is the Minkowski one: a cell is blocked where the mover's *own circle* would overlap a body, so every body carries a halo of the mover's radius. A swordsman needs 44,7 m of circle; on the Crucible the nearest neighbour's edge is **18 m away**. Two bodies that close, each haloed by 22 m, merge into one solid mass — and that is true rather than pessimistic, since the regiment genuinely cannot walk between its own line-mates. 273 cells of about 2 400 are blocked, and all of them are inside the two deployment lines. Selecting a regiment standing in its own line and finding it surrounded is the grid stating the one thing it is most confident about. **The mover's rectangle instead of its circle, built and refused.** The circle is isotropic and therefore wasteful: it reserves the same distance on every axis when the regiment is twice as wide as it is deep. The rectangle form reserves what the mover actually casts on each of the body's own two axes — `OrientedRect.ProjectedRadius`, which is the separating-axis form of the same sum and is never larger — with the mover squared to the line it is about to walk, per [M24](#m24), so it is still one facing per order and still no heading dimension in the search. **It is cheaper and it is worse.** Building falls 79,0 ms to 71,9 and the worst order 10,6 to 9,1, but routes held go **45 to 41** on the Crucible and **73 to 64** on the Long March, gaining only Broken Country's 55 to 56. Through the whole cascade it loses everywhere: the grid answers 10, 11 and 5 orders as a circle against **7, 9 and 4** as a rectangle, and presses rise 15 to 17 and 9 to 11. **The reason is the one thing a rectangle has to assume and a circle does not.** A rectangle is sized for a facing, and the facing it is given is the straight line from start to destination — but **the routes that need this grid at all are exactly the ones that bend**, and on a bent leg the assumed facing is wrong and the halo was sized for the wrong orientation. The circle is right precisely because it assumes nothing about facing, which is the same property that let the heading dimension be dropped in the first place. Kept as a lever at `HaloShape.Circle`, so the idea is not re-derived. **What it is worth in the cascade, which is the only measurement that settles it.** Everything above weighs the grid on its own; this runs the whole staged planner over the same 80 orders a field with the grid in each of its three places. **As a stage it wins on every axis that matters.** Orders reaching the lattice at all fall **37 to 27, 33 to 22 and 6 to 1**; orders that shoulder through fall **20 to 15, 11 to 10 and 4 to 1**; clean routes rise 50 to 55, 64 to 65 and 75 to 79; total time falls 21%, 6% and 24%, and the Crucible's worst single order falls **86,3 ms to 55,4**. The wall clock in [M76](#m76) fires on 24 orders instead of 30. **The price is route seconds, and it is the price [M65](#m65) says to pay**: 27 593 s to 32 711 on the Crucible, +18,5%, because five regiments that used to press now go round, and going round costs more than going through. Fewer presses at more seconds is the trade Mx2c asks for, up to the 3x ceiling that is now enforced. **The corridor is dead, and cleanly so.** Handing the grid route to the lattice as its tube produced **byte-identical outcomes to not running the grid at all** — same lattice calls, same presses, same 27 593 route-seconds — for 20 to 30% more time and *more* clock timeouts. So a grid route is no better a tube than the tangent route [M62](#m62) refused, and the reason that idea failed was never the press-through it was drawn round; the lattice simply does not want guidance. Recorded so it is not derived a third time. **Replacement is a real option and not the right default.** It is the fastest of the four — the Crucible 1 086 ms against 1 572 as a stage and 1 990 with no grid, worst order 37,0 ms, and no timeouts at all because nothing can time out — and on the Crucible it matches the stage for quality. But on Broken Country it presses **17 orders against 10** and holds 58 clean against 65, which is the lattice's gap-threading going missing exactly where the blind spot predicts. **How much of the map one search touches**, since the answer decides whether searching a whole field is a real cost: **459, 417 and 469 cells on average**, 11 to 19% of the field, because A* is goal-directed and settles what it needs. The worst case is 2 141, 2 782 and 1 462 — **90%, 91% and 36% of the map** — and those are the orders where no route exists at all, so the search exhausts everything reachable before saying so. That is the structural point restated: exhausting the whole field is the worst this can ever do, and the whole field is 2 400 to 4 100 cells. **It is a stage, not a replacement.** It holds 167 of 240 orders — 91% on the open Long March, 53% on the cluttered Crucible — so the lattice is still what threads a gap, exactly as the blind spot predicts. **And a cheap search is not a licence to skip the ceiling.** Wired without one it promptly handed U19 a route costing **1 316 s against a 177 s straight line, 7,4x**, which is the regiment-refuses-the-order failure [M65](#m65) exists to stop. It is priced against the press like every other stage; being fast only lets it reach the ceiling sooner. Built as three arrangements behind one lever — stage, corridor for the lattice, and outright replacement — because the tube in [M62](#m62) was turned off for a reason a grid route specifically does not have: the cheap route it was drawn round was a press-through on 74 of 94 orders, and a grid route cannot be a press-through, since cells holding a body are not enterable and going round them is the only thing it can express.

<a id="m78"></a>
**M78** — Two of the designer's refinements to [M77](#m77), both measured, one a large win and one a small one that arrives pointing the other way from the proposal. **Keep the field instead of rebuilding it.** Laying the grid and marking the bodies was about half of what an order cost, and it was being done from scratch for every order of a wing over a field that does not move between them. Nothing about the marking depends on *which* regiment asks except its footprint, and there are only **four distinct footprints** in the whole order of battle — so one field per footprint answers everyone, and the mover takes its own body off again. Coverage is therefore **counted rather than flagged**: each sample of each cell records how many bodies cover it, which is what makes the subtraction correct where two bodies overlap the same ground, as they constantly do in a line. Measured on 80 orders: **117 fields built becomes 4 built and 113 reused**, the grid's own cost falls **5,2 ms to 0,95 ms an order** on the Crucible (3,2 to 0,75 and 3,5 to 1,0 elsewhere), the Crucible's worst order falls **110,6 ms to 72,7**, and **not one route changes** — 45, 55 and 73 held before and after. A pure win, which is rare enough here to be worth saying plainly. **Staleness is not left to discipline**: the field is keyed on a hash of every unit's position and facing, so one built before anything moved is simply not found afterwards. A kept field gone stale is a route through a regiment that is no longer there, and that is the class of bug this project has already spent four attempts on; the hash costs microseconds against the milliseconds it saves. **A cell is what its samples say it is.** The proposal was that a cell a body is five percent into should not be as blocked as one it fills, with partly-filled cells subdivided into smaller hexes. Sampling a cell at seven points instead of one gets both halves of that without a second graph: a better estimate of how much is really covered, **and a node placed at the middle of whatever is free rather than at the cell's centre** — which is what subdividing would really have bought, since what a finer cell gives a route is a place to stand that the coarse centre could not name. Swept at seven samples, routes held at 75 / 50 / **35** / 25 / 14 percent covered: 40/48/**48**/41/33 on the Crucible, 46/51/**57**/50/47 on Broken Country, 71/74/**76**/73/70 on the Long March. Against 45, 55 and 73 for a single centre sample that is **181 against 173, and a win on every field rather than a net one**. **The direction is the surprise.** The proposal put the cut at about three-quarters gone; the measurement puts it at a third. Seven samples do two things that pull opposite ways — they let a cell a body barely clips stay open, and they close a cell whose centre happens to be free while its edges are not — and a third is where those balance. Nineteen samples buy nothing over seven, 49 held against 48 for half again the cost, so the ring is enough and the second ring is not. **Nor does relocation alone do the work**, which is the obvious next question and was measured too: set the cut at a cell with no free sample left at all, so that every partly-covered cell is entered off-centre instead of refused, and the grid answers **every one of the 80 orders on all three fields** — and **29, 36 and 43 of them hold**. Against 48, 57 and 76 at a third. A free centroid is a place the regiment may legally stand, not a place it can be marched into: the legs either side of it have to clear as well, and in a cell nine-tenths covered they do not. Half of what the permissive setting produces is a route the walk check then throws out, and the order falls through to the lattice anyway, so the cost is paid twice. The threshold is not really measuring how covered a cell is; it is measuring how much slack is left before the free part stops being somewhere a regiment can march through. It is not monotonic either — at a seventh gone the count falls back to 33, 47 and 70 — so it is a genuine optimum rather than a safety dial. **What an order costs now**, whole cascade, per order: **26 ms on the Crucible, 24 on Broken Country, 4,6 on the Long March**, of which the grid is about one. The rest is the lattice on the orders that still reach it.

<a id="m79"></a>
**M79** — A recording of ordinary play, read rather than guessed at, and three things in it that a screenshot could not have shown. **The lattice was winning nothing.** Eleven searches over one session, **none successful**, seven of them giving up on [M76](#m76)'s wall clock — and the give-up counts piled on 128 expansions, which is two readings of a clock checked every 64. M76's own note said "the lattice won none of the seven anyway", which was measured with the clock already in force and was therefore a fact about the clock rather than about the lattice. **Cut on a count instead.** Swept with the clock off, routes the lattice wins go 1, 3, **9**, 9, 9 on the Crucible at 128, 2048, **4096**, 8192, 16384 expansions and 6, 13, **14**, 14, 14 on Broken Country; four thousand is where it meets what an unlimited search wins, and press-throughs fall 14 to 7 and 9 to 5. The price is the worst order, about 103 ms becoming 179, which is a frame — the mitigation belongs in `MayPlan`, which charges its ration before a plan runs rather than after, and 2048 buys the worst order back to 92 ms for six of the nine routes. **The reason for a count rather than a clock is reproducibility, not threads.** The Crucible's win count wobbled 7, 7, 7, 6, 7 across five identical runs, and a lever that cannot be swept twice to the same answer cannot be tuned at all. **What it does not fix**: the guess that the wall clock was behind `WingOrderTests` reporting routes that change between planning a wing at once and planning it one order at a time was **wrong**, and is recorded as wrong — with the clock at zero all four still fail, so whatever is shared on the planning path is something else and is still unfound. **Narrowed since, and it is not the planner.** Run on its own the whole set passes **5 of 5, three times over**; run inside the suite it fails 2 or 3, and *which* variants fail moves between two consecutive full runs of identical code. So the thing that is shared is shared with the rest of the suite, not within the planning path — which also means the green count is a weaker guarantee than it looks, and that a change must be judged by diffing failure *lists* either side of it rather than counting them. **A way round nothing had priced.** The staged planner's terminal line hands back the tangent graph's route, and every cost ceiling above it is gated on the ladder having found a press-through to compare against; an order given from a pose the previous order left behind has no press to find, so nothing priced the way out. The recording has one at 686 m for a 188 m hop — 3,6x, ten waypoints, opening with a 177 degree wheel — against every other way round that session at 1,0x to 1,5x. `StraightLineCostCeiling` gives the ceiling a yardstick when there is no press: the same walk on an empty field. **Four, not three**, because a way round is legitimately dearer against empty ground than against a press and the bench says by how much — over 240 orders the worst honest route costs 2,8x, 2,7x and 2,9x its own straight line, so three would refuse those. **It is a guard, not a cure**, and does not catch the recorded 3,6x, which falls between the bench's honest worst and the ceiling; a single ratio cannot separate a long way round from a bad one, because what distinguishes them is the pose the order started from and not the number. Tuning to 3,25x to make one observed case land inside it, against 240 legitimate orders and a third of a multiple of margin, is how the next bug gets bought. **And the hole was bigger than the two it was first patched into.** A later recording has a way round at **7,7x** — 763 m walked for a 103 m hop, 406 m west and 345 m back east — out of `Marching`'s rung two, which weighs arching against threading and **never against `straight`**, a number it computes three lines earlier and uses only to print. Its own log line read *"837 s against 109 s straight"* while it handed the route back. Returning it also short-circuits the whole cascade below: on that same order the grid had a two-waypoint answer and was never asked. The rung now declines rather than returns, so the stages beneath it get their turn. **Verified as far as it can be**: on a short hop past one of its own the check fires, the arch is declined and the rung falls from two to four — but the bench tops out at 2,8x and squeezing the ceiling to 1,5x refuses none of its 22 way-rounds, so the 7,7x itself is **not reproduced in a test** and the fix stands on inspection plus absence of regression, not on a red test turned green. Note the trade a declined arch makes: the next thing down is often the press-through, which is why the ceiling sits at four and not at two. **The recording now carries the arrangement.** `ReportScene` sat behind the preview toggle, so a fault seen in ordinary play left a route, a cost, and no way to rebuild what caused it — and asking the designer to reproduce it was answered, fairly, with *"i cant i closed my simulation ... do not expect user to have app open"*. Every real order now writes the whole field, unfiltered, for the same reason the original line gives: a filter tuned for planning is exactly the kind of thing that turns out to be the bug. **Found, 26 Aug 2026, and it was never in the planner.** The levers are plain statics — `RouteSearch.MostPlaces`, `StagedRoutePlanner.AcceptBentLadder`, `RegimentGrid.SpacingMultiple` and a dozen more — and xUnit runs test *classes* in parallel. **Twelve classes write them**, four with tests that were never skipped. Every such write lands on whatever else is planning a route at that instant, which is exactly the observed shape: `WingOrderTests` plans one wing twice and compares, so a lever flipped between the halves makes them disagree about a route neither planned wrongly — passing alone, failing in company, and moving run to run because it is a thread race. `PlanningProfile` had already been made thread-local for this same reason and the lesson was not carried across. Fixed by a collection: `PlannerLevers`, `DisableParallelization = true`, and every mutating class joins it skipped or not. **Three consecutive runs now give byte-identical failure lists** — ten, all pre-existing — where before the count moved between eleven and thirteen and the members moved with it. A lock was the wrong answer: it would serialise the writes without making a read see a consistent set, and these are read millions of times a plan and written once a test.

<a id="m80"></a>
**M80** — The designer's rule, stated plainly: *"if i click to give an order and then i click somewhere else, discard the planning that is running and instead replan the new route."* **What happens today is the opposite.** `WorkOutRoutes` hands a wing to a background worker and returns; the first thing the *next* order does is `SettleRoutes`, which waits for that worker, **applies its routes**, and only then plans the new ones. So a superseded plan is not discarded — it is waited for and obeyed, briefly, before being overwritten. Two costs follow. The player sees a regiment set off towards the place they stopped asking for, and the second click stalls behind work whose answer is already worthless, which [M79](#m79) has just made as dear as 179 ms for one regiment and a multiple of that for a wing. **The join cannot simply be skipped.** The worker reads the battle state while it plans, and the comment above `SettleRoutes` names the hazard exactly: two plans reading while one may finish and give an order. So the rule is not "abandon the task" but "make it stop quickly, then throw its answer away", which is two separate pieces — a result discarded on collection, and a cancellation the search itself polls. The lattice already checks a condition every 64 expansions, which is where the poll costs nothing. **Only the regiments the new order re-targets are discarded.** A click that orders one wing must not silently drop an order given to another a moment earlier; what supersedes a plan is a new order *for that regiment*, not a new order anywhere. **Built in three pieces.** `Marching.GiveUpNow` is a per-thread question the lattice asks at the same sixty-fourth expansion it already reads its own conditions at, so the poll costs a null check; `RouteWork` carries which regiment each result is for and a `volatile bool[]` of the ones dropped, marked on the drawing thread and read on the workers; and a wing skips outright any regiment superseded before its turn came, which is most of them. Measured on an order that ran the lattice to its full budget: **4096 expansions and 313 ms becomes 64 expansions and 29 ms**. The 29 ms that remains is the rest of the cascade — ladder, tangents, grid — which is not polled and is cheap enough not to want to be. The join itself stays: the worker still reads the battle that the next order writes to, so what changed is how quickly it can be joined and that its answer is discarded rather than obeyed.

<a id="m81"></a>
**M81** — The designer, on the order of 25 August: *"last army order didn't rotate it instead slided sideways for the whole distance. rotating, moving and rotating again would have been more efficient. i think i asked you to price rotation too."* **Rotation was priced, and correctly.** [M22](#moving) charges the wheel as pace lost, the crab was costed as the crab it was, and the planner printed its own verdict in the recording — *"645 s against 285 s straight"*. Nothing was mismeasured; [`StraightLineCostCeiling`](#m79) is 4 and 2,26x walked straight through a gate built to catch detours of a different order. **The fault is the shape, not the price.** `CrabThrough` exists to make a body its own depth wide instead of its own frontage, and its own comment says what that means: walk up to the squeeze front-on, thread it side-on, come back onto the march — because crabbing the whole way *"would arrive at the far end still side-on and at two fifths pace for a journey that never needed it."* It then offered exactly that as its fallback whenever it could not find where the squeeze began and ended. U5, 80x40 m, held 81° against a march on -9° for **404 m**, arrived **90° off its ordered front**, and averaged **0,6 m/s of the 1,0 the ground allowed**. A crab that runs the whole way is threading nothing, so the premise of the rung is false. **Declined, not returned**, for [M79](#m79)'s reason: a return short-circuits everything below it, and below it here the search had a 348 s answer nobody asked for. **The share, not the branch** — a crab covering 95% of a route is the same fault as one covering all of it, and one number covers both shapes `CrabThrough` can draw. **Three quarters, and it costs nothing.** The whole-way crab is rare and ruinous, which is the profile worth refusing: over 160 bench orders on two fields it fires *not once*, so 0,90 and 0,75 decline nothing and leave all 29 and 26 genuine crabs standing — worst 2,8x and 3,0x, mean 1,31x and 1,28x, 63 and 70 held, every figure unmoved. Only at 0,50 does a real crab start being refused, and it costs: broken country's worst goes 3,0x to 3,1x and its mean 1,28x to 1,30x. The two safe values are indistinguishable by measurement, so the rule picks between them. On the recorded order: **645 s becomes 348 s, 2,3x becomes 1,2x, the rung falls from 3 to 5, and no leg is walked side-on at all.** **Reproduced before it was changed**, from the `Scene` block [M79](#m79) made unconditional — positions, facings and footprints straight out of the recording, reproducing 645 against 285 to the second, and kept as a gate that fails on the old code. **What the reproduction also cost:** the footprints were a quarter of their true area on the first attempt, because the test content's default strength is 500 and the recording's regiments are 2000. Every clearance question was being answered about a different field, and the fixture *passed* — a fixture built from a recording has to check the bodies it raised are the bodies that were there.

<a id="w7"></a>
**W7** — *"i cant i closed my simulation but that happened in real order too, next time do the debug properly do not xpect user to have app open."* A diagnostic that needs the player to reproduce the fault with the app open is not a diagnostic. `ReportScene` — the one thing that makes an arrangement rebuildable — sat behind a preview toggle, so a fault in ordinary play left a route, a cost, and nothing to reconstruct it from. Every real order now writes the whole field: each ordered regiment's position, facing, footprint and destination, then every unit on the field, **unfiltered**, because a neighbourhood filter tuned for planning is exactly the kind of thing that turns out to be the bug being chased. It paid for itself on the next order recorded — [M81](#m81) was rebuilt from one `Scene` block and reproduced to the second.

<a id="m82"></a>
**M82** — The designer, on three screenshots: *"2nd image is 0.1 cell and halo set to 1x (should have just walked straight)"*, and of the third, *"i dont understand why it looped around"*. **The teal line is not the route.** It is `RegimentGrid.TryRoute`'s answer drawn raw — a path of hex centres — while every route the planner hands out has been through `RouteSmoothing.Applied` first, and the grid stage *inside* the cascade straightens its own answer before it will even offer it. The preview could not reach that pass: `RouteSmoothing` is internal to the rules and the drawing code is a Unity assembly outside them, so it drew what it could get. **What the gap is worth**, measured on the recorded field at the tenth-of-a-regiment cell size the screenshots were taken at: the preview draws **2 864 points across eighteen routes**, 159 a route, where the planner would walk **98**, five a route, over **8,7% less ground**. At the default cell size it is 353 against 63 and 14% less ground. So the zigzag the designer was reading as a bad route is an artefact of the picture, and the picture is the only evidence a play-test leaves. **Same shape as [W5](#w5)** and as the scene report that had to be pulled out from behind a preview toggle: a diagnostic that reports something other than what happens sends every investigation that trusts it to the wrong place, and this one had been trusted twice. `Marching.Straightened` is the public door, on the class that already hosts `GiveUpNow` for exactly the same reason. **What this does not answer.** The looping in the *first* screenshot is only partly the picture: at one regiment to a cell the grid refuses whole cells around a body and the route it finds is genuinely wide, which is the halo work still outstanding. And the last order in that recording really did walk 422 m for a 226 m hop — 1,5x, 122 m off the straight line, opening with a **179 degree wheel** — which passed the 4x ceiling honestly and is a separate question from what was drawn. **And the Unity check had been reading a stale assembly.** It references the *Release* build of the rules; every check this session had built only Debug, so it would have compiled clean against yesterday's DLL whatever was written. Caught only because this change adds public surface Unity calls. [M64](#moving) fixed this check once already by pointing it at the right compiler; a check that passes regardless of what you change is the same defect wearing different clothes.

<a id="w8"></a>
**W8** — The designer's rule: *"any found bug that is an actual bug (in route calculation whatever)"* — keep the reproduction as a permanent gate, **and** put the arrangement into the bench sweep. Two halves, and they do different work. **W3** already said reproduce before changing anything, but said nothing about keeping the reproduction, so every fault so far was re-derived from scratch and nothing stopped it coming back. The gate half: a fixture built from the recording rather than invented, which **fails on the code as it stood** — red-first is what separates a gate from a test that was always going to pass. The bench half: the arrangement becomes a field, so what the route *costs* is swept on every lever change rather than only passed or failed once; correctness gates catch a route that breaks, and only the sweep catches one that quietly gets dearer. First applied to [M81](#m81), which is `TheSidewaysMileTests` plus `content/battles/sidewaysmile.battle.txt`, and immediately to [M82](#m82). **Three things the first application cost**, all of which would have gone unnoticed in an invented fixture: the test content's default strength is 500 where the recording's regiments are 2 000, so the first attempt raised bodies a quarter of their true area and *passed*; the bench loader assumed a battle file's map shares its key, which held only because every field so far had been authored as a pair; and three of the bench's assertions hard-coded eighty regiments, which was a fact about the three designed fields read as a law. A fixture built from a recording has to check that the bodies it raised are the bodies that were there.

<a id="w9"></a>
**W9** — The Unity-side check has now passed for the wrong reason twice. [M64](#moving) found it running a C# 7.3 compiler, so it reported seventy-six syntax errors in files nobody had touched and would have done so whatever was written; [M82](#m82) found it referencing a *Release* assembly that had not been rebuilt, so it would have compiled clean against yesterday's rules whatever was written. Two instances of one defect: **a check that passes regardless of what you change is not a check**, and the same rule the tests already keep as non-vacuity guards — `routes >= 10`, `Assert.Equal(Authored(key), ...)`, the bench refusal threshold — had never been asked of the toolchain around them. `tools/unity-check.sh` is that rule applied. **Every input is discovered, never named.** The editor comes from the project's own `ProjectVersion.txt`; the compiler is globbed out of that editor's `DotNetSdk`, which is M64's correction made permanent rather than re-typed each session; the reference assemblies are **built by the check** rather than found, so there is no build for it to be stale against; and the client is globbed from `Assets/`, which immediately added `Assets/Editor/` — `BattlefieldSceneBuilder` and `UnitArtImporter`, two files that had been outside every check ever run, because the old response file listed its nine sources literally. **It self-tests on every run.** A probe reaching `RouteSmoothing` — the exact internal [M82](#m82) tripped over — is compiled and must be rejected *on CS0122 specifically*, because a check that accepts any non-zero exit would read a typo in its own probe as a healthy boundary; it caught precisely that on its first run. When the main compile has failed the self-test reports **inconclusive** rather than passing, since a rejection proves nothing if everything was rejected. **All three guards were shown red before being trusted**: a Unity script touching an internal fails on CS0122, a broken file under `Assets/Editor/` fails at all, and a backdated reference assembly is *refused* rather than compiled against. **What it is not** is what Unity does. The editor compiles `Assets/Scripts` and `Assets/Editor` as two assemblies and the rules from source via the asmdef; this compiles all eleven files as one library against a netstandard build. That approximation is why `-langversion:9.0` is pinned to match `Directory.Build.props` and why `-nullable:enable` was *removed* after being added by mistake — the flag lives in `Rules/csc.rsp` and so applies to that asmdef, not to `Assets/`, and a stricter compile is a different compile, not a better one. The honest check is `Unity.exe -batchmode`, which costs minutes and a licence; this one runs in seconds, and an approximation that does not admit it is one is how both earlier faults survived. **The first run against the editor's own output found a third instance of the same defect.** The designer pasted four warnings from the console; the check could not have shown three of them, because the response file it inherited carried `-nowarn:CS0649,CS0414,CS0169,CS8632` — and measured with the line removed, **CS8632 was the only one of the four that ever fired**, so the suppression was hiding real output and nothing else. It is gone, and warnings now **fail** the check: the count is zero, so the bar is free to hold, and the alternative is what had already happened. The three were `string?` and `RouteWork?` annotations in `BattlefieldController` — enforced by nothing, since `Assets/` has no nullable context — replaced by doc comments that say the same thing without claiming a contract the compiler is not keeping. **The fourth warning the check still cannot see**, and this is a stated limit rather than an assumed coverage: `UAC1001`, Unity's serialization analyzer, on `DebugOverlay.Options` — a public field of a type with no `[Serializable]`, which the editor skips. Not a bug, because `BattlefieldController` assigns it at startup, so it is now `[System.NonSerialized]`, which says that outright. The analyzer lives in `Unity.Analyzers.Common.dll` and was tried with `-analyzer:`; it loads and never fires, wanting the editor's pipeline around it. So the editor console remains the only place `UAC*` appears, and the script header says so.

<a id="w10"></a>
**W10** — The designer, on reading the profile: *"be careful how you interpret test results, sometimes pressing through is faster that doesnt mean the route is better. other times pressing through is desired over looping around, it depends how big is the looping around."* Both halves are corrections to how the tables in [M83](#m83) were read. **The first**: a setting that turns press-throughs on will nearly always show as cheaper, because pressing through *is* the cheap answer — the cascade stops early and the dear rungs are never asked. `PoseSearchBeforePressing` off reads as 23 to 36% quicker on three fields and it is not an optimisation; it is nine or ten regiments walking through their own line. Any row whose ms/order improved while its press-through count rose has bought the time with route quality and must be read as a trade, never as a win. **The second, and it is the one that stops this becoming a rule against pressing at all**: a press-through is not a defect either. [M26](#moving) already priced shouldering through as an edge with an honest cost, and a regiment that shoulders past a corner of its own for a second beats one that walks 400 m round the outside — the sideways mile ([M81](#m81)) was exactly that mistake in the other direction, a route that avoided contact at 2,3 times the cost. **So neither count settles anything alone.** The pair that has to be read together is the press-through count *and* the seconds of marching planned: pressing that saves a long detour shows as fewer seconds, pressing that saves nothing shows as the same seconds with worse contact. `grid cells x0,5` on the sideways mile is the honest example — press-throughs 5 to 2 and unwalkable 6 to 3, for **8% more marching time**, which is a trade worth taking and would read as a regression on either number by itself. **What this asks of the benches** is that a comparison always carry both: `FullProfileTests` prints routed, pressed, unwalkable and total route seconds on every row, and a row that moves only one of them is a question rather than an answer.

<a id="m83"></a>
**M83** — The designer asked for the cost of *everything*: every algorithm, every configuration, in milliseconds or microseconds a step. `PlanningProfile` already had the shape of the answer — twenty-five timed steps with self, inclusive and calls, plus four counted-only ones too hot to time — and two benches already drove it, but neither asked the whole question: `WhatEachPlannerSpendsItOn` prints six steps of twenty-five as a one-line summary, and `LeverBenchTests` sweeps ten settings on one planner. `FullProfileTests` asks both in full: **six planners over four fields with a complete step table each, then thirty-six levers one at a time against the defaults.** Both ship skipped, because the lever sweep turns global planner settings over while it runs and the runner's classes are parallel — the same hazard `PlanningProfile` was made thread-local for. **One step is the whole problem.** `BodyScan` is the heaviest step for five of six planners on all four fields, from **33%** of the default's self time to **72%** of the ladder's, and it is *already indexed* — the probe sits around `WhereEverybodyIs.Near`, running at 0,68 to 3,04 µs a call. So the bill is call count, not per-call speed: 105 159 calls for eighty orders on the Crucible. **The lever that follows from that is `MostPlaces`.** Halved from 48 to 24 it costs **25 to 29% less on three fields and 10% on the fourth**, and every quality figure comes back identical — same total route seconds to the second, same pressed-through, same unwalkable, on all four fields — with the *worst* single order falling too, 17,5 to 14,2 ms on the Crucible and 11,4 to 7,5 on Broken Country. Identical totals are strong evidence and not proof, so it is filed as an open finding against `RouteFingerprintTests` rather than switched. **The hybrid's bill is the field it builds, not the search it runs**: `HybridField` is 79,9% of its self time on Broken Country and 88,6% on the sideways mile, and on the Long March the shape flips to `HybridClear` at 42% while the planner costs 6,989 ms an order — twenty times the default. **Three settings already off were confirmed off**: `AskCorners` +58 to +104%, `AskRings` +84 to +140%, the corridor +37 to +123%, all for byte-identical routes; and two earlier decisions held up, the dial queue worth 17 to 62% over a heap and the turn-aware heuristic 19 to 70%. The one quality worth re-deciding deliberately is `PoseSearchBeforePressing`, which *is* 23 to 36% of the default's cost on three fields and is what buys zero press-throughs. **And two measurement faults were found in the taking.** `WhatEachPlannerSpendsItOn` divided by a hard-coded eighty, so every per-order figure it ever printed for the forty-regiment sideways mile was **double** the truth — the same defect as the three bench assertions [W8](#w8) corrected, a fact about three fields read as a law. And the first row of the lever sweep was cold at 3,067 ms against a warmed 2,396, which would have flattered every row beneath it by about a fifth; a whole discard round now runs before the table. **The newest rung had no probes at all.** `RegimentGrid` was invisible, its cost absorbed into `Plan`'s self time; instrumented as `GridField` and `GridSearch` it is 2,2 to 5,0% of the bill at 134 to 394 µs a search, asked on 32 of 80 orders, with the field itself nearly free because it is shared across the wing. `HexSearch` stays at zero calls on every field, which is correct — that is the 25 m terrain pathfinder, the escape hatch [M10](#moving) always said it was. **And the lever it recommended was switched, after the route-by-route comparison the finding asked for.** `MostPlaces` 48 to 24: all 280 orders on all four fields planned at both caps and compared waypoint by waypoint, identical, for **25 to 29% off three fields and 10% off the fourth**. `Ledger.Reset` clears `places * places * 3` entries across five arrays before every search, so the cap is a quadratic tax paid whether or not the places exist — 6 912 slots an array at 48 against 1 728 at 24 — which is why the saving is larger than a graph half the size would suggest. **What the comparison needed was a non-vacuity guard**, because without one it passes at a cap of *six*, and at two. The reason is worth more than the lever: **the tangent stage wins nothing.** It is asked on 51 of 280 bench orders, costs about 2,5 ms a search against the regiment grid's 0,16 to 0,39, and its answer is refused every single time — 66 of them because the search pressed through, 14 for a leg that will not walk — after which the grid answers 56 and the lattice the rest. So nothing downstream of the cap can notice the cap. The guard asserts the search really did pass 24 places, which it does on all 51, 26 of them reaching 48 exactly; the saving is real and it rests on a fact about the bench, and `HalvingTheGraphChangesNoRoute` is what will say so the moment that fact changes.

<a id="m84"></a>
**M84** — The designer, on [M83](#m83)'s tables: *"bodyscan feels like it takes too long for no reason"*, *"hybridclear seems to take a bit too much... also hybridfield i have some ideas"*, and a proposal — pre-generate one mesh for the whole map instead of one per unit, since *"i dont think the moving mesh actually improves performance that much since its still quite large"*. **Two steps were split before anything was changed**, because "make it faster" means nothing until it is known which half the time is in. `BodyScan` became `BodyScan` plus `NearQuery`, and the hybrid's field became the fill plus `HybridRaster`. **Both splits contradicted the plan they were meant to serve.** The raster is **0,2 to 0,5%** of the field — 6,7 µs against 1 613 — so pre-computing a static map-wide occupancy grid would have saved a third of one per cent. The remaining 1 600 µs is a Dijkstra over cells × eight heading layers *counted from this order's goal*, which no static mesh can be. **And the class the idea was aimed at is never built.** `HybridObstacleField` runs only when the turn-aware heuristic is off or a corridor was asked for, so in the shipped configuration every millisecond under `HybridField` belongs to `HybridTurnField` — the first probe went on the dead one and reported zero calls, which is the only reason it was found. **What did pay was the resolution.** The turn field's cell size had never been swept and the fill is quadratic in it. `MinCellMetres` **10 to 20**: the Crucible **-16%**, the sideways mile -5%, Broken Country -3%, the Long March -2%, and under [W10](#w10) the half that decides it — not one press-through, unwalkable route or second of marching moved on any field. Forty was faster still and is refused: it buys -16% with two press-throughs and two unwalkable routes on Broken Country. **`UnitIndex.Near` was the other half.** It sampled the line every half bucket and gathered a square of buckets at each, with the halo carrying half a bucket of slack so nothing fell between samples: at 128 m buckets and a regiment's 45 m reach, **128 bucket visits to cover about fourteen distinct buckets** down a 400 m leg. The marks array made the repeats cheap rather than wrong. Now one pass over the corridor's bounding buckets, each taken only if its own square can reach the segment — which is *tighter* as well as single-pass. Measured: the query 0,36 to 0,32 µs a call, the `BodyScan` pair 3 to 15% cheaper, **and the effect on ms/order inside run-to-run noise on the dearest field**, because the query is only 15 to 28% of the bill. Said plainly because it is the honest result: the rewrite is right and it is not the win the step's 33-to-72% share suggested. **Making `BodyScan` cheaper is the small half of `BodyScan`.** The hybrid already shows the large one — `TakeStock` asks which bodies are near **once per expansion** and reuses the answer across every primitive from that node, 25 503 queries serving 172 021 clearance tests, a **6,7 to 1** reuse. The clearance path asks once per leg, 1 to 1, no reuse at all. That is finding 23. **Together, with quality untouched everywhere**: the Crucible -11%, the sideways mile -6%, Broken Country -3%, the Long March flat.

<a id="m85"></a>
**M85** — The designer, on [M84](#m84)'s tables: *"shouldnt worst single order be some microseconds which is ladder's straight-line -> if it works it works?"* **Two populations were tangled in one number, and only the wrong one had ever been reported.** The *worst* order is by construction never the straight-line case — it is the order where the straight line failed and the whole cascade ran. But the question underneath is right and nothing anywhere could answer it: what does an order cost when the cheap rung answers? Mean and worst cannot say, because the mean is smeared across populations three orders of magnitude apart. **Measured, by which stage counter moved, stopwatch only and the profiler off** — at this scale two timestamp reads would be a visible share of what they claim to measure:

<a id="m86"></a>
**M86** — The designer, on [M85](#m85)'s tables and the drawing of the cascade: *"do the reorder, ask the grid before tangents and then verify if we even need tangents at all"*. Both halves done, and the second answered more completely than expected. **The reorder.** `StagedRoutePlanner.Choose` asked the tangent search fourth and the regiment grid seventh; the grid now goes above the whole tangents/corners/rings block. The tangent plan has four callers — the attack path, the stage, the tube the lattice may be bounded to, and the terminal fallback — so it is now drawn through a local `Tangents()` that runs the search **at most once an order and only if something reaches it**, where before it ran eagerly whether or not any caller was live. **The verification.** From its old position the stage answered **0 of 280** bench orders while drawing the graph 83 times. Moved below the grid it is reached by only **27** of them — 9, 0, 10 and 8 on the four fields, the Long March never reaching it at all — and answers **0 of those too**. Turned off outright: **not one of the 280 routes moves**, total marching time is identical to the tenth of a second on every field, and **no order falls through to the terminal fallback**, because the lattice answers every one the grid could not. The orders that reach this stage are exactly the ones it was always going to refuse. So `AskTangentStage` ships at **off** — a lever on the stage, not a deletion of the search, which an attack order still goes straight to and which is still what stands between a regiment and no route at all. **Measured paired**, four alternating builds each, least of four, because this machine's Crucible run is bimodal between about 106 and 150 ms and an unpaired comparison of it means nothing:

| field | before | after | |
|---|---|---|---|
| the Crucible | 141,2 ms | 116,8 ms | **-17,3%** |
| the Long March | 24,9 ms | 19,7 ms | **-20,9%** |
| the sideways mile | 38,0 ms | 26,9 ms | **-29,2%** |
| Broken Country | 117,4 ms | 80,2 ms | **-31,7%** |

M86 wins **every one of the sixteen paired field-runs**, and [finding 22](OPEN-FINDINGS.md)'s estimate of 13% was low because it priced only the search and not the ledger reset and clearance work hung off it. **Quality is not traded for any of it**: 280 routes byte-identical against the pre-reorder build, same routed, same refused, same five pressed through on the sideways mile, same marching seconds. **Two gates had to be repaired to stay honest.** `HalvingTheGraphChangesNoRoute` compared routes at 48 places against 24 through the shipping cascade — which after this reaches the tangent search on no field at all, and its own non-vacuity guard correctly reported that halving a cap nothing reaches proves nothing. It now turns the grid off and the stage on, so it exercises the search it names. And nothing anywhere wrote down what the *cascade* returns — the fingerprint theory runs the three planners singly, none of which is what a battle reaches — so a change to the order of its stages had no record to be checked against. `EveryShippingRouteWrittenOut` is that record, and it is what the byte-identical claim above rests on.

<a id="m87"></a>
**M87** — The designer, on the decision register: *"Do 01 two-tier grid (if it is purely to add benefit). sidewaysmile is slower but all the others are good"*. A grid cell holds a whole regiment, so the coarse grid **cannot express a way round narrower than one**, and the orders it fails on are exactly the ones that fell to the pose lattice at tens of milliseconds each. Asking a finer grid of *every* order is a loss, because a quarter cell is sixteen times the cells and all thirty-two orders pay for it. So the grid is asked in tiers: a regiment’s width, then half, then a quarter, and each tier is reached **only by the orders the one above could not answer**, so the sixteen-fold field is paid on the eight to fifteen orders a field that get there.

Measured on the shipping arrangement rather than on one spacing applied globally — `FullProfileTests.TheTwoTierGridAsShipped`, four bench fields, 280 orders, least of four passes, three separate runs:

| field | coarse only | + half, quarter | lattice | press | marching |
|---|---|---|---|---|---|
| the Crucible | 107,8–149,3 ms | **56,6–59,0 ms** | 9 → **0** | 0 → 0 | -0,2% |
| Broken Country | 71,6–75,9 ms | **64,0–66,2 ms** | 10 → **0** | 0 → 0 | +1,3% |
| the Long March | 15,9–16,4 ms | 15,7–16,9 ms | 0 → 0 | 0 → 0 | unchanged |
| the sideways mile | 22,4–22,7 ms | 22,9–25,1 ms | 3 → 3 | **5 → 1** | +11,7% |

**The pose lattice stops running altogether** on the two fields that were paying for it. Nothing gets dearer in wall clock: the sideways mile is flat inside this machine’s spread, and an earlier reading of it at +31% was a single unpaired draw against a bimodal baseline — [W11](#w11) again, in its milder form. What the sideways mile actually buys is **four of its five press-throughs removed**, for 4 122 s of extra marching, every one of those trades individually inside [M88](#m88)’s ceiling. Two tiers beat one: a quarter alone leaves a press on the sideways mile and runs 3–5 ms dearer on the other two fields, because a half-cell grid answers nine of the crucible’s fifteen for a quarter of the fill.

**Re-taken after [M90](#m90)**, which changes what the grid finds and so re-bases every row — three runs, least of four:

| field | coarse only | two tiers | lattice | press | marching |
|---|---|---|---|---|---|
| the Crucible | 127–177 ms | **74,8–78,0 ms** | 7 → **0** | 0 → 0 | +0,3% |
| Broken Country | 83,4–89,4 ms | **73,7–80,4 ms** | 6 → **0** | 0 → 0 | +1,1% |
| the Long March | 20,8–22,6 ms | 20,3–21,5 ms | 0 → 0 | 0 → 0 | unchanged |
| the sideways mile | 27,9–29,7 ms | 28,0–31,2 ms | 3 → 3 | **4 → 1** | +8,6% |

The shape holds — the Crucible −45%, Broken Country −12%, the lattice off both, the sideways mile flat in wall clock and buying presses. M90 costs some absolute time on every row, including the baselines, and its own accounting is at [M90](#m90).

<a id="m88"></a>
**M88** — The designer, asked what a press-through is worth: *"a press-through should be worth about 3x normal route (or 2.5 minimum). it’s better because it’s not dependent on speed"*. This settles what [W10](#w10) left open and what the register raised against [M87](#m87)’s sideways mile. `StagedRoutePlanner.WayRoundCostCeiling` already stood at **3**, so no code moves — what changes is that the number is now a decision with a reason instead of a default nobody had defended.

**Why a multiple and not a number of seconds.** The alternative on the table was *"a press is worth up to N seconds of detour"*. It was refused because seconds are not comparable between regiments: the same detour is N seconds for cavalry and three times N for a foot column on the same ground, so a fixed budget would let horse round anything and pin infantry to shouldering through. A multiple of the regiment’s **own** pressed route is invariant to its pace, its ground and the length of the order, which is what makes one number govern the whole game.

**The floor is 2,5.** Below that the ceiling starts refusing detours the game wants — a way round a single body is routinely twice the straight line. Anything from 2,5 to 3 is defensible; 3 ships. It applies only where the ladder actually pressed, so a route that was never going to shoulder through is not measured against this at all.

<a id="m89"></a>
**M89** — The designer, on the crab and on the grid’s halo in the same breath: *"if there is space for an unit to fit it should fit"*, and *"this should apply to every code which verifies a crab"*.

**What it rules out.** Two ways of asking about clearance are now wrong by rule. The first is **enumerating poses to discover a fit**: if two bodies ahead are far enough apart to admit the mover’s width — not its length — and the way beyond them is clean, that is already a crab candidate, and the answer is arithmetic on the gap rather than a search over placements. Take a default pose and compute the rotation from the gap **only if one is needed at all**. The second is **inflating a body by a halo and calling the result blocked**: a cell whose halo is touched but whose ground is free is a cell the regiment can stand in, and refusing it is refusing a fit that exists.

**What it does not say.** It does not say clearance is free to check, and it does not repeal [M12](#moving) — the rectangle is still what travels. It says the test is *does the body fit in the space*, asked of the body and the space, and never of a proxy for either.

**Its consequences are measured, not assumed.** Dropping the grid’s halo makes cells passable that the router must then cross off-centre, which is a different search, not a cheaper one; the designer asked for a performance review of that specifically before it ships. Hence the status above.

### The halo review, 28 Aug 2026

*"Can’t you just leave it with no halo (so the hex is yellow instead of red) and just compute for the actual space?"* — asked, measured, and the answer is in three parts.

**What the halo is.** `RegimentGrid` reserves `HalfDepth + (BoundingRadius − HalfDepth) × ClearanceFraction + MarginMetres` round every body — for a forty by twenty regiment, 10 + 12,4×0,75 + 2 = **21,3 m**. Two spearmen fifty metres apart therefore have their thirty metres of daylight **closed completely**, because 21,3 from each side overlaps in the middle.

**Half of what was asked for is already there.** The cell is not simply red or not: seven samples measure how much of it is really covered, `FillToBlock` refuses it only once a third is gone, and `NodeAt` then enters the cell **at the middle of whatever of it is free rather than at its centre**. That is the *"go left-right"* half, and it was built for exactly this reason. Its limit, found this session, is that the centroid of the free samples is not necessarily a place the rectangle fits — sample coverage is a test on points, not on the body.

**Removing the halo was already swept, and it loses.** At clearance fractions 1,00 / 0,75 / 0,50 / 0,25 / 0,00, routes **held** were 42/45/34/35/33, 52/55/54/50/47 and 73/73/73/58/43. Shrinking finds more routes and keeps fewer, because **A\* returns one route and not a menu**: an optimistic grid does not offer a route that might work, it commits to threading a gap the regiment cannot use, and the gate then refuses the whole thing. Pricing held ground at eight times an open step — nearly no halo, in effect — reproduces this exactly: the Crucible went to **285,7 ms** with the coarse grid holding **not one route**.

**But the instinct behind the question was right, and it found a real bug.** The halo’s serious cost is not optimism, it is that a regiment **standing against a body is inside the halo and cannot get out** — and worse the finer the cells, which is what made the fine tier useless on the arrangements it was added for. The fix is not to remove the halo but to make it a price rather than a wall, which is what [M90](#m90) does. The halo stays; the regiment can now leave.

<a id="m90"></a>
**M90** — Two holes in the regiment grid, both found by taking [open finding 24](OPEN-FINDINGS.md) apart, both instances of [M89](#m89).

**The route left from somewhere the regiment was not.** `Reconstruct` replaced the start and goal cells with the true start and destination, which reads as tidy: the ends of a route should be its real ends. What it actually produced was a first leg running from wherever the regiment stood to the node of the **second** cell, a cell and a half away, cutting the corner of the cell between — and a regiment under orders to move is usually standing against the very body it has been told to get round, so that corner is that body. The grid never checked the leg, because it is not a grid edge, and `WalksCleanly` then threw the whole route away. `KeepEndCells` keeps both end nodes, so every consecutive pair of points is at most one cell apart and the only hops the grid does not vouch for are two sub-cell ones, which are unavoidable. Measured on the bench it is **free to faster** — the Crucible 85,9 → 68,6 ms, because smoothing given more points to work with finds a shorter route than a re-search would — and every field's marching comes out slightly shorter.

**And the finer the cells, the worse the grid got.** A regiment touching a body stands inside that body's halo, which reaches [ClearanceFraction](#m90) of its circumscribed radius plus the margin — 21 m for a forty by twenty regiment. A\* refused every held cell, so the search was walled in at the start and again at the destination, and it was walled in **worse the finer the cells were**: at a regiment's width one 45 m step happens to clear the halo, at a quarter of it no chain of 11 m steps ever does. So the fine tier that [M87](#m87) added returned **no route at all** on exactly the arrangements it exists for. `BlockedStepPenalty` prices a held step instead of refusing it, which is [M89](#m89) said as arithmetic: a regiment that legally stands somewhere must be able to leave it.

**The price is sixty, because that is where it stops mattering.** Swept at 0 / 8 / 25 / 40 / 60 / 80 / 120 / 200:

| penalty | Crucible | Broken Country | what happens |
|---|---|---|---|
| refuse | 85,9 ms | 72,3 ms | the fine tier finds nothing where it is needed |
| 8 | **285,7 ms** | **254,2 ms** | cuts *through* bodies; the coarse grid holds **0**, the lattice runs 18 times |
| 25 | 83,6 ms | 84,3 ms | held steps still taken as short cuts |
| **60** | 77,2 ms | 63,7 ms | held ground used only to escape and to arrive |
| 200 | 67,4 ms | 64,2 ms | **identical routes to 60** |

From sixty upward not one route moves — 57 632,6 s and 71 664,1 s of marching at 60, 80, 120 and 200 alike — so sixty is where held ground has stopped being a short cut, and paying more buys nothing. Eight is [ClearanceFraction](#m90)'s warning about an optimistic grid arriving on schedule: *A\* returns one route and not a menu*, so a grid that thinks it fits threads a gap it cannot use and the gate refuses the whole thing. At sixty the coarse grid answers **more** orders than refusal did, 25 and 26 against 23 and 22, so the fine tier is asked eleven and ten times rather than fifteen and eighteen — for 0,7% and 1,9% more marching.

**What it did not fix.** Four of the nineteen approach angles were already clear; two more hold a grid route now; the six that press through still do, on a third fault that needs a rule rather than a repair — [finding 24](OPEN-FINDINGS.md), part (c).

<a id="m91"></a>
**M91** — The designer, asked how short a leg has to be before a regiment holds its front instead of wheeling onto it, and refusing the question: *"if a shuffle (call it sidewalk) is like a crabbing, then it should be costed. just like with crabbing, its preferred to walk straight but if that’s not possible then it should be priced, just like with crabbing. so always verify if a non-sidewalk option is possible and do it, otherwise sidewalk can be done"*.

**The name.** A **sidewalk** is a leg the regiment covers without turning to face it: it keeps the front already in hand and translates. A **wheel** is [M3](#m3)'s default, turning until the front points along the leg. Every leg of every route was a wheel until now, which is [finding 24](OPEN-FINDINGS.md) part (c) and is why six approach angles pressed through.

**The rule is [M14](#moving) said per leg.** Full width first, crabbing second — so the line of march is tried on every leg, always, and the front in hand is only asked about on a leg that will not walk wheeled. It is an ordering and not a comparison, which is deliberate: a regiment that *can* face where it is going should, and the cheapest route is not the argument for turning it sideways.

**Two proposals were refused to get here, and both were mine.** A **length threshold** — shorter than N metres, hold the front — needs a number nobody can defend, and cannot tell a 5° turn from a 93° one at the same length. A **cost comparison** — price it both ways and take the cheaper — would let a regiment turn sideways merely because it was faster, which is [M81](#m81)'s 404 m crab arriving 90° off its ordered front, invited back in through the front door. Preference with a price is neither.

**The pricing already existed and needed nothing.** `Marching.SecondsToWalk` takes the per-leg `Hold` array and charges a held leg at `MovementSystem.AlignmentPenalty` of the angle between the front and the bearing — which is exactly how a crab has always been priced. `CostsMoreThan` already passes `Hold` through, so [M88](#m88)'s ceiling prices a sidewalk honestly against a press without a line changing. What was missing was never the cost model; it was that a grid route came back with `Hold` **null**, so no leg could ask.

**Built as a repair against the gate itself**, rather than as a second opinion about clearance. The route is handed to `FirstBadLeg`, and the leg it names is given the front in hand and asked again — so what decides a sidewalk is the same code that decides whether the executor will walk it, and the two cannot drift apart, which is [W5](#w5) applied to a plan instead of a log line.

<a id="m92"></a>
**M92** — Two rules for one question, found in [finding 24](OPEN-FINDINGS.md). `RouteSmoothing` asks a route's first leg with the leaving licence unconditionally; `StagedRoutePlanner.FirstBadLeg` grants it only where the regiment laps one of its own by more than `AllowedContactFraction`. A regiment **shoulder to shoulder** laps it by *less* than that — which is exactly what [M2](#moving) exists to permit — so it got a route smoothed under one rule and refused under a stricter one, and a regiment under orders is very often shoulder to shoulder.

Measured across the nineteen approach angles: the six that fail lap body 0 by **0,0% to 4,4%**, every one of them under the 5% allowance, and every one has a first leg that is **clear with the licence and blocked without it**.

| approach | lap on body 0 | leg 1 with the licence | leg 1 without |
|---|---|---|---|
| 0° | 0,000 | clear | **blocked** |
| 5° | 0,019 | clear | **blocked** |
| 15° | 0,040 | clear | **blocked** |
| 25° | 0,044 | clear | **blocked** |
| 30° and beyond | 0,041 → 0 | clear | clear |

Extending the licence to contact does **not** wave the leg through: `EscapesWithoutDeepening` still refuses a leg that enters a body it was clear of or deepens one it was lapping. It only stops contact *itself* being the refusal. On the bench the sideways mile's coarse grid holds **10 orders instead of 8** and the lattice runs **once instead of three times**; no other field moves and no route gets worse.

**It does not close the approach-angle gate on its own**, and that is worth writing down: the six still fail, because the leg the grid draws at those angles genuinely moves *into* the body. The licence was a real inconsistency and a real fix; it was not this bug's cause.

<a id="m93"></a>
**M93** — Decision 14, closed. `OrderSystem` asks `MayPlan` before the placement search as well as before the plan, deliberately — *"that search is itself geometry this frame has no allowance left for"* — but only `Marching.PlanTo` charges `Spent`, and the branch where `TryFindPlacement` finds nowhere to stand returns without ever reaching the planner. A permission was granted, real geometry ran, and the ration was never touched.

**Charged as milliseconds and not as a route**, because only the milliseconds are real: spending one of the frame's *routes* on a route that does not exist changes where a batch of orders runs out, and it fails `WingOrderTests` on every run — correctly. It would also mark the regiment as having planned this frame when it has not.

**The first attempt at this was reverted on a mis-diagnosis**, which is written up in [finding 25](OPEN-FINDINGS.md) and is the more useful half of the episode: the failure appeared only in the whole suite and was read as a wall-clock fault, when the cause was two test classes missing from the `PlannerLevers` collection. *A failure that appears only in the whole suite is a claim about the suite, not about the change.* With them serialised, this charge is clean over four runs.

<a id="m94"></a>
**M94** — The designer, on the fourth cause of [finding 24](OPEN-FINDINGS.md): *"yes build the arriving license"*. Built as asked, in four pieces, and **it ships off**, which is the part of this entry worth reading.

**The rule, and it is sound.** `ArrivesWithoutDeepening` is `EscapesWithoutDeepening` walked backwards from the destination — bodies lapped where the regiment arrives may only be less lapped the further back you look, and bodies clear at the destination may not be entered anywhere on the way in. It is gated on `CouldStandAt`, so it licenses **contact and never overlap**: without that gate the backwards sweep takes the arrival overlap as its baseline and would happily let a route finish a quarter of the way inside a regiment.

**Three things had to come with it**, each a real defect found on the way:

| | |
|---|---|
| the arrival front is never chosen | a destination touching one of its own accepts **2 of 24** fronts; `AlongTheLine` hands the last leg one of the other twenty-two |
| a node the body does not fit | [M90](#m90)'s `KeepEndCells` kept `NodeAt`, which averages **free sample points** — and at the failing approach that average sat **inside body 0** |
| smoothing broke walkable routes | it casts with the leaving licence unconditionally while the gate is stricter, so a cast it believes clear can be one the executor refuses |

**It worked, on the thing it was built for.** With all four in, the half-cell route at 0° came back `bad leg 0` — walking in full, every leg, for the first time.

<a id="m94a"></a>
**M94a** — Todo 01, and it is the half of the diagnosis that was right. `ArrivesWithoutDeepening` inherited `EscapesWithoutDeepening`'s allowance, `AllowedContactFraction` — five per cent of a body. But a leg *without* a licence is judged by `Marching.IsClearLine`, which refuses on `Sweep.Touches`, which is **any overlap whatever**. So granting the licence moved the last leg from the stricter test to the looser one, and the looser one let it barge five per cent into somebody on the way in. Measured before the fix: three of seven sampled angles went to *walks through somebody*, and one went from clear to a 68 s route that clipped.

**The two ends are not symmetrical, and that is the whole entry.** Where a regiment **starts**, the contact is ground it already occupies and nobody chose; where it **stops**, the contact is a place the planner picked. So the leaving licence keeps its allowance and the arriving one gets none — `ArrivalContactFraction` is **0**. A body lapped at the destination may only be less lapped the further back you look; a body clear at the destination may not be entered anywhere on the way in. All the licence now forgives is the contact **at the destination itself**, which is the one thing it was built to forgive. Verified: after the fix the only contact anywhere on those legs is at the final waypoint, 1,1% to 4,3% deep, and never earlier.

<a id="m94b"></a>
**M94b** — The other half, and M94a alone made the gate **worse** — 10 of 19 against 6 — which is what said the diagnosis was incomplete. Three rules, and all three are the designer's own [M88](#m88) and crabbing rules read onto the arrival:

| | |
|---|---|
| **last resort** | the licence is withheld on the first ask; only an order that presses or finds nothing is planned a second time with it — *"always verify if a non-sidewalk option is possible and do it, otherwise sidewalk can be done"* |
| **no escaping the ceiling** | a way round that exists only because the arrival was licensed is still a way round, and still costs at most [M88](#m88)'s three times the press |
| **no dearer** | against anything that is not a press it has to win on the clock, because the pass that found it was the pass that could not find anything better |

**The flag is `[ThreadStatic]`**, not global. Orders are planned several at once, and a flag one order sets while it retries would otherwise decide what a different order on another thread may do — the same fault `UnitIndex.Marks` was built to avoid, which produced routes that differed depending on how many orders were given together.

**And with all of it in, the licence earns nothing anywhere it has been asked**, which is the finding. Over the nineteen approach angles it changes **not one route**: at 0°, 5°, 20° and 25° the licensed pass finds nothing *with the ceiling lifted entirely*, and at 15° and 30° it finds a way round costing **3,51× and 4,09×** the press. On the bench it fires **twice in four fields, keeps neither**, and costs sidewaysmile **+22% to +49%** for it — crucible, longmarch and Broken Country never reach it at all, and total marching seconds are identical to the digit on every field.

**So the fourth cause is not a cause.** The six approach angles that remain are not waiting on the arrival; they are presses that no way round beats at the price [M88](#m88) sets, and four of them have no way round at any price. The lever stays, off, with its price written down.

<a id="m95"></a>
**M95** — Todo 02, and [finding 23](OPEN-FINDINGS.md)'s halo branch, closed against its own prediction. The complaint was arithmetically true: a clearance query widens by `reach + widest reach`, takes any bucket whose centre lies within half a diagonal of that, and a body may then sit half a diagonal outside the bucket it was filed in — **181 m of slack around a reach of about ninety**, which is why a query hands back 14,1 bodies. The finding concluded that the first move was to ask a smaller question. Measured over a **twenty-four-fold** range of bucket widths, it is the wrong move.

| bucket | bodies a query | buckets a query | `NearQuery` | `BodyScan` |
|---|---|---|---|---|
| 768 m | 37,2 | 1,9 | 19,5 | 31,8 |
| 512 | 29,8 | 2,7 | 18,3 | 26,7 |
| 256 | 20,3 | 4,4 | 17,7 | 24,8 |
| **128 — shipped** | **13,6** | **8,5** | **21,6** | **21,4** |
| 64 | 9,7 | 20,5 | 28,6 | 16,3 |
| 32 | 7,9 | 58,8 | 52,7 | 14,3 |

*(the Crucible; the other three fields have the same shape)*

**A bucket is dearer than the bodies the slack lets in.** Halving the width cuts the bodies by about a quarter and multiplies the buckets by four, and `NearQuery` **doubles by 32 m** while `BodyScan` falls only a quarter over the same range. Everything from 128 to 512 is one flat basin with no ms-an-order reading outside the noise on any field; both ends are worse. So 128 stays, now for a measured reason rather than a plausible one, and it is a lever with a counter (`NearBuckets`) under it.

**And it half-answers the hoist too.** Finding 23 computed a break-even of "about 27 bodies" for a hoisted per-order query, on the assumption that a query costs what it costs. It does not: at 512 m a query already returns 29,8 bodies and the whole `BodyScan + NearQuery` bill is no worse than at 128. The break-even is not a fixed number, and quoting it as one would repeat the mistake the finding was rewritten to correct.

<a id="m96"></a>
**M96** — Todo 04. `TakeStock` and `PoseIsClear` each built the mover's true-sized box *and* its margined box at every pose, and a box is a cosine, a sine and a square root. Two things were wrong with that: the two boxes share a centre and a heading, so the second one's trigonometry is the first one's; and most poses do not need both.

**Sized before it was built**, which is the part worth keeping. Under the cascade as it ships the lattice is reached on **one field of four**, for **124 poses and 0,0 ms** — so this is worth *nothing* where the game actually plans, and saying so is the honest headline. Asked directly the lattice runs 123 637 to 2 223 777 poses at **0,69 to 1,05 overlap tests a pose**, so about a third of poses take no branch at all and paid for two boxes to decide it.

**Built as a lever so both could be weighed in one process** — [W11](#w11), and it is the reason this reading can be trusted at all. Lazy is faster in **eleven of twelve paired readings**: least of three, **−7,0%** on the Crucible, **−8,8%** on the Long March, **−2,2%** on Broken Country, **−8,3%** on sidewaysmile. Poses and overlap tests are **identical to the digit** on every field, which is what says nothing about the search moved.

<a id="m97"></a>
**M97** — Todo 03, and register decision 05: *re-measure `DetourRoomFraction`, then decide it or drop it*. Lowering it from 0,5 to 0,3 once roughly halved the turn field build, and the suspicion was that [M87](#m87) had eaten the saving. It has, and by more than suspected.

`TheTurnFieldsOwnGeometry`, swept 0,5 → 0,2:

| field | expansions | turn field | routes moved |
|---|---|---|---|
| crucible | **0** | 0,0 ms | 0 |
| longmarch | **0** | 0,0 ms | 0 |
| brokencountry | **0** | 0,0 ms | 0 |
| sidewaysmile | 124 | 0,4–0,7 ms | 0 |

**The lattice is not reached at all on three fields of four**, so the number governs a field that is never built; where it is built it is **under a millisecond**, and no route moves at any setting. There is nothing to win.

**And a reason not to lower it anyway.** The fraction is the margin around the straight line the field is drawn over, so a smaller one is a narrower box — which cannot make a route worse but can make it *absent*, on an arrangement wanting a wide detour. `moved 0` says these four fields do not want one; it does not say none does. Trading a route that exists for a tenth of a millisecond on one field is the wrong side of [W10](#w10). Left at 0,5, and the entry is closed rather than carried.

<a id="m88a"></a>
**M88a** — The designer, asked what a press is worth now that it is priced rather than refused: *"i dont know the limit for press through but i think it should be around 3.1x, 2.5x is too little"*. Both halves check out, and the tenth is not decoration.

Swept 2,0 to 4,0 on all four fields, both order patterns, with `WhereTheCeilingSitsAroundThree`. The crowded pattern — a whole wing sent to one block, which is what the ceiling exists for — is where it shows:

| ceiling | Crucible presses | Broken Country presses | refused as *too dear* | worst detour |
|---|---|---|---|---|
| 2,0 | 21 | 16 | 2 and 3 | 2,0x |
| 2,5 | 18 | 13 | 2 and 3 | 2,4x |
| 3,0 | 16 | 10 | **1** and 0 | 3,0x |
| **3,1** | **15** | **8** | **0 and 0** | **3,1x** |
| 3,5 | 15 | 8 | 0 and 0 | 3,2x |
| 4,0 | 13 | 7 | 1 and 0 | 3,9x |

**3,1 is the knee, and that is a stronger reason than the round number was.** It is the smallest ceiling in the sweep at which *no way round anywhere is refused as too dear* — at 3,0 the Crucible still refuses one — and it converts one more press to a route there and two on Broken Country. Above it nothing further is bought: 3,5 is identical row for row, and 4,0 buys two more routes at the price of a worst detour of 3,9x, which is most of the way back to the five-fold march [M88](#m88) exists to stop. The designer's rejection of 2,5 is the same table read downwards: it presses 18 and 13 times with two and three way rounds refused outright.

**The scattered pattern does not move at all** between 2,5 and 4,0 on any field, which is the right shape for this lever — it should be invisible until regiments are in each other's way.

**And the default is named now.** `ShippedWayRoundCostCeiling`, because four separate test sites restored the lever by writing `3f` out again and all four were still on the old value after this change. A default that lives in five places drifts the first time it moves, and this one did.

<a id="m98"></a>
**M98** — The designer, closing the last question in [finding 24](OPEN-FINDINGS.md): *"a press is a legitimate answer"*.

`ApproachAngleTests.EveryApproachAngleFindsAWayThroughTheGap` was written when a press was an unpriced escape hatch, and it counted one as a failure alongside a route that walked through two regiments unflagged. **Those are not the same thing.** The measurement that raised the whole *(place, front)* redesign found twelve of nineteen angles *"returning a straight line through two regiments, unflagged, so nothing charges it and no rule agreed to it"* — the fault named there is the **undeclared** one. Since [M88](#m88) a press is an answer with a price, taken only when every way round costs more than the ceiling and declared when taken.

**So the gate now asks what it always meant to ask**: no angle may walk through one of its own without saying so, and none may fail to answer at all. It is **green at 13 clear and 6 pressed**.

**It has teeth, and this is the part that matters.** Left there it would pass if the planner pressed all nineteen — a worse planner and a green test, which is exactly the failure mode this repository keeps finding in its own checks. So there are two more assertions: fewer than nineteen presses, and **every press re-asked with the ceiling lifted entirely**. A press stands only where the uncapped pass finds no clean way round *at any price*, or one dearer than the ceiling allows. All six stand on the first of those.

**What this closes.** [Finding 24](OPEN-FINDINGS.md), entirely — four causes, three fixed in code and the fourth measured to be inert, and the six that remained were never a fault at all. It is the oldest open defect in the file.

<a id="m99"></a>
**M99** — The designer, on the 179-degree opening wheel: *"it would be weird for an army to move sideways facing the opposite way it's going towards like it should flip to the other face"*, and *"the face should face the direction it moves in the most even if it has to turn back after"*.

**`Marching.AlongTheLine` was never wrong about facing the way it travels. It was wrong about where it reads that from** — the first waypoint, whatever that waypoint happens to be. Measured over 280 orders on the four bench fields: **62 open with a wheel over 90°** and 40 over 150°, and on nineteen of the twenty where the wheel is plainly wasted the regiment is *already* within 1° to 26° of where the march is going overall. The worst is a **107° wheel to walk one metre**, on a march of 1494 m whose bearing is 2° off the front it was standing on. Then it turns back.

**The stub is not removed, because it is a real waypoint.** A one-metre sidestep exists precisely because the straight line is refused, so [`RouteSmoothing`](../packages/com.battlechess.core/Runtime/Rules/Battle/RouteSmoothing.cs)'s long cast fails and correctly keeps the point. The route is right and only the front on it is wrong. `RouteFronts.Applied` therefore **moves no waypoint whatsoever**, and that is asserted rather than asserted-in-prose: 0 routes moved on all four fields.

**The 90-degree cap was proposed and dropped, by the designer and by the code both.** A cap makes a regiment walk with its frontage across the line of march, and [M24](#m24) records that arrangement as the one that broke routing outright — held broadside a body sweeps its full width, rung one and rung two both failed, and it shouldered through its own. The front is an argument to `Marching.IsClearLine`, so a cap would have changed *which routes exist*. This changes none.

**Where the threshold sits, and there is no free lunch in it.** Every degree not turned is walked crabwise instead, at `MovementSystem.AlignmentPenalty`. Sized by share of route alone the pass caught legs of 39 m to 120 m — stubs by share and nothing like stubs on the ground — and bought a 13–30% cut in turning for **+1,0% to +1,6% on the march clock**. Trading walking time for turning time is not what this is for, and a hundred-metre sidestep held square is the same thing the designer called weird. So the measure is the regiment itself:

| stub is | turning | march clock | crabbed metres |
|---|---|---|---|
| 1 body (35 m) | −3,5% to −4,7% | free | 23–62 |
| **2 bodies (70 m)** | **−10% to −17%** | **+0,2%** | **713–834** |
| 4 bodies (141 m) | −20% to −25% | +0,8% to +1,5% | 1522–2216 |
| a tenth of the route | −13% to −30% | +1,0% to +1,6% | 1507–2409 |

One body is nearly free and nearly nothing — it leaves 23 of the Crucible's 23 opening wheels over 90° exactly where they were. Four crabs twenty to twenty-eight metres an order, which is a regiment visibly sliding. **Two is the knee**: a third to a half of the available turning for two tenths of one per cent, and about nine metres of crabbing per order — well under a body length, so not something the eye picks out. It also states cleanly, which a tuned fraction never does: *a leg shorter than two regiments end to end is a shuffle, not a march.*

**What it costs to run: nothing measurable.** One extra `Marching.IsClearLine` per stub leg, against a step that is 9,3% of the Crucible. Least of three, three separate runs: −3,1%/−0,5%/−3,7% on the Crucible, +1,6%/−0,9%/+2,1% on Broken Country. **The sign flips per field between runs**, so it is inside the noise and no claim is made either way.

**And the halt-and-turn the designer also asked for was already there.** `MovementSystem.PivotBonusWhileHalted` prices a halted pivot at 1,6× the walking rate, and [M30](#m30) holds the ground and keeps turning on the step that would have hit. Nothing was needed for it.

**And it is off, because the licence itself loosens the gate.** The gate stayed at **13 of 19** either way. Bisected properly — the three levers moved **at runtime**, in one process, rather than by rebuilding between cases, which is how [finding 27](OPEN-FINDINGS.md) came to be got wrong the first time:

| turned on alone | seven angles sampled |
|---|---|
| `RefuseSmoothingThatBreaks` | **nothing changes** — identical to baseline |
| `DropUnstandableNodes` | **nothing changes** — identical to baseline |
| **`LicenceOnArrival`** | **three angles become `walks through somebody`**, and 10° goes from **clear** to a 68 s route that clips |

So two of the four pieces are inert on this arrangement and one is the fault. A declared press-through is honest; an undeclared clip is the original bug, and on a project whose whole premise is *"cavalry goes through units"* that is disqualifying.

**Why it loosens.** `EscapesWithoutDeepening` tolerates overlap up to `AllowedContactFraction`, and `Marching.IsClearLine` tolerates less. Routing a leg through the licence therefore moves it from the stricter test to the looser one — which is what a licence is *for* at the first leg, where the regiment is genuinely already in the contact, and is not obviously right at the last, where the contact is a place the planner **chose**. Sampled finely, the routes it admits carry overlaps of 10% to 23%, well past the 5% [M2](#moving) permits.

**What would make it shippable.** The arrival sweep asking `Marching.IsClearLine`'s tolerance rather than `AllowedContactFraction`, so the licence forgives *ending* in contact without also forgiving passing through anything on the way in. That touches `EscapesWithoutDeepening`'s shared body and so changes the first leg too, which is why it is not being done on the way past.

**Levers**: `LicenceOnArrival`, `RefuseSmoothingThatBreaks`, `GridRoutePlanner.DropUnstandableNodes` all off; the arrival-front choice rides inside `AllowSidewalk`'s repair and is reached only on a last leg that has already failed.

<a id="w11"></a>
**W11** — A measurement that compares two builds must prove it built them. The first paired run for [M86](#m86) reverted only `StagedRoutePlanner.cs` to HEAD, which no longer compiled against tests referring to the new lever; `dotnet build` was writing to `/dev/null` inside the loop, `dotnet test --no-build` then ran the assembly still sitting in `bin`, and the table showed the two builds performing identically — which is exactly what it *should* show, because they were the same build. Third instance of one defect: [M64](#m64) measured with the wrong compiler, [M82](#m82) read a stale Release DLL, this read a stale test DLL. [W9](#w9) fixed it for the Unity check by discovering every input and self-testing; the same standard applies to any measurement loop. **In a loop that rebuilds, the build's exit status is a gate and its output is evidence — never silence it**, and revert whole working trees rather than single files, because a partial revert is a different program from either side being compared.

**Fourth instance, 28 Aug 2026**, in a new disguise, so the rule wants widening. A lever bisect edited source and rebuilt between cases; its last line restored the levers **in source and did not rebuild**, and the next command ran `dotnet test --no-build`. The reading taken after it belonged to the previous case's binary. It was then written up as [open finding 27](OPEN-FINDINGS.md) — a claim that the planner depended on what had run before it, which would have invalidated every measurement in this area — and withdrawn only when both call patterns were finally run **inside one process**, where they proved byte-identical. So: **a source edit is not a change until something builds it**; `--no-build` is safe only on the line after a build that succeeded; and *two isolated runs of one binary cannot disagree — when they seem to, doubt the binary before doubting the program*. And where a lever can be moved at runtime, move it at runtime: a bisect that never rebuilds cannot make this mistake at all.

| answered by | orders | median | worst | share of all planning |
|---|---|---|---|---|
| straight line | **30 of 80** | **21,5 µs** | 31,0 µs | **0,1%** |
| bent ladder | 18 | 2 354 µs | 4 557 µs | 7,1% |
| regiment grid | 23 | 11 308 µs | 38 518 µs | 45,8% |
| pose lattice | **9** | 29 173 µs | 57 927 µs | **47,0%** |

That is the Crucible, and the other three fields have the same shape: the straight line answers **38 to 50% of every order given** at 4,4 to 23,6 µs a time for **0,1 to 1,2%** of the bill, while nine orders — **eleven per cent** — take forty-seven. *If it works it works* is exactly right, and the planner's cost is not spread across orders at all; it is a tail. **What that changes.** Work spread evenly over every order is worth far less than it looks: [M84](#m84) made the index query 12% cheaper and moved the total inside noise, and this is why — it is charged to the 38% of orders that cost nothing anyway as much as to the 11% that cost everything. **The lever that matters is how often the tail is reached, and what the cascade pays on the way.** A stage's own price is not what an order answered by it pays: a grid answer costs 219 µs of grid and **11 308 µs of order**, because everything above the grid was tried first. Which puts a number on finding 22 — on the Crucible the tangent search costs 731 µs a call over 32 calls, **23,4 ms of a 128 ms field, for a stage that answers nothing**, immediately before a grid that costs 219 µs and answers 23. Asking the grid first would save about **13%** on the Crucible and Broken Country alike, and it is the same 13% on both because it is the same mistake.

<a id="m59"></a>
**M59** — The staged planner took the ladder's route when it was a straight cast and refused it the moment it had a bend in it, on the reasoning that a bend leaves the mover to arrive on a new front while other regiments are still moving. That reasoning is true of a route nobody checked, and [M55](#moving) had since made every route checked: `WalksCleanly` proves every leg against the rectangle that will travel it, on the front it will be held on, which *is* the objection. So the refusal was costing orders their answer and buying nothing. **Accepting a bent ladder route once it is proved, measured least of three:** the Long March **14,3 → 3,1 ms** an order, and its routes got *shorter* — **1 232,9 s → 1 155,8** — because thirty-six orders that were being handed to a pose search now walk a route the ladder had already drawn and paid for. The Crucible and Broken Country neither gain nor lose (6,2 → 6,0 and 3,8 → 3,7), which is what a proof of a route already in hand should cost. **The counter that made it findable:** the last cheap opinion's failure was split into first leg, later leg, and pressed — the first leg never fails, so nothing is stuck in its own crowd, and what reaches the lattice is 4 / 26 / 24 orders on which the *tangent search itself pressed through*, not orders it could not answer.

<a id="m60"></a>
**M60** — The estimate from [M56](#moving) settles some seventy-five thousand states through a binary heap, and its relaxation takes exactly three values: one cell of march, one diagonal cell, or one eighth of a turn. Everything in the queue at any moment therefore lies within one edge of the value just taken, which is the precondition for a ring of buckets indexed by cost — take and put become O(1) where the heap paid a sift over a hundred thousand entries. Ordering inside a bucket is arbitrary and can settle a state a hair early; the ordinary relax check catches it and re-queues, so what comes out is exact. **Measured least of three, and it is the rare change that costs nothing to be wrong about — every route identical to the decimal on all three fields:** the Long March **14,3 → 12,6 ms** an order, the Crucible **6,2 → 4,6**, Broken Country **3,8 → 2,3**. With [M59](#moving): **3,1 / 4,5 / 2,4**.

<a id="m61"></a>
**M61** — The lattice's cost is its expansion count and its expansions go on ground no sensible route would touch, so bounding it to a tube around a route some cheaper planner already drew should have been most of the answer. It is not, and the counters say why rather than leaving it a mystery. **Swept at half-widths of 45, 90 and 150 m and budgets of 4 000, 20 000 and 40 000 expansions:** the bounded search had to be re-run unbounded on **92 of the 94 orders** it was asked, so nearly every order paid for two searches, and every field went the wrong way — the Long March 14,3 → 18,7 ms an order, the Crucible 6,2 → 9,9, Broken Country 3,8 → 7,3. Widening the tube and raising the budget made it worse, not better. **The reason is the split in [M59](#moving):** on 74 of those 94 orders the cheap route *is* a press-through, so the tube was being drawn round a line that runs through the middle of a regiment. There is no answer near it to find. Kept as a lever at nought so the idea is not re-derived, and it is the second time a plausible saving has turned out to be paying twice for the same search.

<a id="m62"></a>
**M62** — Every measurement in this file prices an order one at a time, and no player gives one order. A wing is box-selected and clicked once, and eighty routes are wanted before the next frame — eighty independent questions over a battle nothing is writing to. **Two things stopped that, and both were found by running it rather than by reading it.** The first threw: `battle.PlanningScratch` was one `Ledger` per battle, on the reasoning that a battle only ever plans one march at a time, and two marches sharing it indexed a list built for one with an offset belonging to the other. Every other scratchpad in planning was already `[ThreadStatic]`; this was the single thing making a plan un-shareable. **The second did not throw, which is worse.** `UnitIndex` dedups buckets with a stamp per bucket and a counter per query — shared between threads, one query's counter marks another query's buckets as already emptied, and *a bucket skipped is a body the caller is never told about*. The routes came back different from the same eighty given one at a time — 1 179,1 s against 1 155,8 — and one of them walked through a regiment. Per-thread marks and a guarded first filing fixed it, and the test for it is not an assertion about locks: the parallel batch is priced and counted exactly as the serial one is, and now returns **the same seconds to the decimal on all three fields, 0 unwalkable, 0 pressed, 80 of 80**. **Measured least of three, twelve cores:** the Long March **3,10 → 0,89 ms** an order (3,5×), the Crucible **4,47 → 1,09** (4,1×), Broken Country **2,43 → 0,36** (6,7×). The speed-up is short of the core count and the reason is in the distribution, not in contention — most orders are a fraction of a millisecond and a few are sixty, so the batch cannot finish before its longest single order. **A hundred orders: 89 ms, 109 ms, 36 ms.** One caveat recorded rather than papered over: a regiment's shape is cached behind a flag that a move clears, and a lazy write to a struct read by another thread is a torn read. Nothing moves while a batch is planned, and the batch works every shape out before it starts — but that is a precondition of planning in parallel, not a property of it. **Not yet wired.** `BattlefieldController.MarchSelection` still walks its selection one regiment at a time, and that loop is where the saving lands; what is done is the part that made the loop unsafe to change. One at a time, a hundred orders now cost 310 / 447 / 243 ms against the 1 432 / 620 / 377 they cost at the top of this pass.

<a id="m63"></a>
**M63** — Five more attempts on the lattice, all measured, none kept, recorded so they are not tried again. **The goal shot was a suspect and is cleared:** it is not timed by `HybridSearch`'s children, so its cost was hiding in that step's self time where it could have been anything — probed, it is 27,3 ms of an order's 496 on the Long March, 21,1 of 633 on the Crucible, 2,2 of 299 on Broken Country, and it ends searches early. **Bounding the estimate's fill is not merely unprofitable, it is backwards:** stopping the fill at 1,3 times the straight run cost the Crucible 6,5 → 14,9 ms an order and Broken Country 3,4 → 14,1, because the fill is not overhead sitting beside the search — it *is* the search's guidance, and the ground cut from it is exactly the ground the lattice then gropes over itself at ten times the price a cell of fill costs. At 2,0 the bound never binds. **A longer stride for open ground** (a 60 m march on top of the 30 and 22) buys the Long March 4,4 → 4,3 and Broken Country 3,4 → 3,1, costs the Crucible 6,5 → 8,4, and is paid for in route quality at every length — 633,3 → 643,2 s on the Crucible — because a stride that only fits where the ground is empty makes the lattice prefer empty ground to the short way round. **Growing the cheap graph harder changes nothing at all:** rounds 1 → 2 → 3 against 48 and 96 places, six settings, and the number of orders the tangent search pressed on is *identical* in all six — 4 / 26 / 24. It is not running out of candidates; a route with one front per leg cannot express the turn inside the gap that these orders need. **The two that would have worked, and why they are still off:** four-metre sweep spacing is 8% everywhere with routes unmoved and nothing unwalkable across 240 orders — but what it spends is the margin between the poses the planner checks and the ground the body occupies, and the only reason nothing unwalkable came out is that [M55](#moving) proves every route afterwards, which is a guard and not a licence. A heuristic weight of 3 buys the Crucible 6,3 → 5,7 and costs the Long March 4,1 → 7,0; at 4 the split is wider. A number that helps one field and hurts another by the same factor is two problems, not a setting.

<a id="m58"></a>
**M58** — With [M57](#moving) done the profile had moved and the next cut was not where the last one was. Per order: the Long March at `HybridClear` 54,8% and `HybridSearch`'s own loop 30,5%; the Crucible 40,1% and 27,1%; Broken Country at `HybridField` **40,5%**, three different bottlenecks on three fields. **Two suspects cleared by measurement.** The bin key was a `(int, int, int)` tuple hashed and compared twice per successor — packed into a long, it changed nothing (47,0 against 47,0 ms an order), so the tuple was never the cost. And probing the estimate showed it at 8,1%, 2,44 million calls at 0,33 µs, so `HybridSearch`'s remaining self time is heap and loop, not arithmetic. **What it actually was: `HeadingBins`.** The lattice binned headings into 48, one every 7,5°, when its own pivot slice is 20° and its wheels are capped near 30 — three states kept apart for a difference no primitive can express, each one expanded and each one swept. **48 → 16, measured least of three:** the Long March **50,9 → 14,1** ms an order, the Crucible **14,6 → 6,3**, Broken Country **5,3 → 3,8**, with route cost unmoved (1 232,9 / 633,5 / 786,7 against 1 233,1 / 632,4 / 786,0) and 0 unwalkable and 0 pressed still. **Sixteen and not twelve**, because twelve costs the gate: the hybrid falls 5 of 19 to **0**, while 24 and 16 hold it at 5. Route cost also starts to slip at twelve — 639,5 s on the Crucible against 633,5. **Rejected by measurement:** a wider sweep spacing (2 → 3 m) is inside noise and buys it with less collision fidelity; a finer estimate grid is worse on both axes (`TargetCellsAcross` 96 cost the Crucible 24,3 ms an order against 14,6); a coarser one costs routes (32 and 24 give the Crucible 645,3 and 671,4 s against 633,5); and trimming the estimate's margin **breaks correctness** — `DetourRoomFraction` 0,5 → 0,35 put an unwalkable route back on Broken Country, which is the whole thing [M55](#moving) exists to prevent. **Kept though it barely reads:** the field borrows its hundred thousand floats and its heap instead of allocating them. Throughput moves inside noise, but a plan allocating a field at a time is [M40](#moving)'s defect returning, and in Unity that is a frame spike rather than a slower average. **Where this leaves it:** a hundred orders in 1,4 s on the Long March, 0,63 on the Crucible, 0,38 on Broken Country — against a target of 0,1. Gate 7 / 17 / 10 / 17 / 5 / 16; suite 598 passing, 13 failing.

<a id="m54"></a>
**M54** — The graph prices legs as places squared, so fewer places is the one lever the cost model endorses — but `ConsiderCorner` has rejected any place within `ApartEnoughMetres` of an existing one since the generator was written, and the measurement says the survivors are genuinely spread out. Widening the filter 1 m → 2 → 3 → 5 moved candidate places **1 964 → 1 956 → 1 940 → 1 912** on the Crucible and **1 760 → 1 752 → 1 738 → 1 718** on Broken Country — **2,6% at five metres** — and the Long March did not move a single place at any setting. Legs priced fell 2% to match. Every apparent timing change (1,64 / 1,60 / 1,47 / 1,72 ms an order) is noise on a 1% change in the work, and reading a 10% win off the 3 m row would have been [M53](#how-to-work)'s mistake again with a different knob. **And it is not free at the top of the range:** at 5 m the approach gate falls from 17 of 19 to 14 for places-and-fronts and 15 for tangents, because a metre of separation near a gap is the difference between a place that clears it and one that does not. Left at 1 m. **What the counts actually say:** ~24,5 places an order yielding ~475 legs priced of the ~1 800 the pairs allow, so the ladder-seeded pruning already discards three quarters of the graph unmeasured. The remaining cost is not duplicate nodes and not unpruned legs; it is `BodyScan` at ~50%, where [M53](#how-to-work) left it.

<a id="c1"></a>
**C1** — Nothing between.

<a id="c3"></a>
**C3** — A third on a full face waits behind it.

<a id="c4"></a>
**C4** — Going round is an order the player gives.

<a id="c5"></a>
**C5** — A regiment waiting behind the line steps into a place the moment one opens — because the man in front fell, or the regiment in front broke. **Non-negotiable.**

<a id="c6"></a>
**C6** — Frontage is divided by the ground each enemy actually shares, not by how many there are: attackers on 60 m and 40 m of a 100 m front deal and take 60 and 40.

<a id="c9"></a>
**C9** — Losses are permanent.

<a id="c12"></a>
**C12** — Lowers morale too.

<a id="c13"></a>
**C13** — A man who falls in the first rank is replaced from the second, and the second from the third. Not instantly — it takes ticks, so a line under heavy fire is genuinely thinner than its headcount says — but as fast as it can be. The regiment-level twin of [C5](#combat).

<a id="c14"></a>
**C14** — Damage below half a man killed nobody, so a weak attack did literally nothing however long it lasted — twenty archers fought a spear block for thirty turns and killed **none**. The remainder is now carried on the regiment being hit and paid on a later pulse, in melee and shooting alike. Finer pulses turned out not to be needed.

<a id="c15"></a>
**C15** — The defender answers with its **side** — its depth, ten men for a ten-rank body. The attacker brings **twice that**, capped by its own frontage: twenty against ten. Not its whole hundred, because a side is not a frontage; not merely ten, because ten men doing a tenth of a frontal attack is not what being taken in the flank feels like. **Counted in men, never in drawn metres** — the rectangle is 2:1 for the eye ([S2](#shape)) and says nothing about ranks ([S5](#shape)), so a rule that read its depth would make a flank as strong as a front the moment the drawing changed.

<a id="c16"></a>
**C16** — At the moment of contact the flanked regiment deals **0.25×** and takes **2.0×**. Both slide back to **1.0×** as it recovers. This is what makes a flank a moment to exploit rather than a permanent state — the reward is for getting round *and* finishing the job.

<a id="c17"></a>
**C17** — , resetting to 1.0× rather than waiting out the recovery. Coming about takes many ticks, so it is a real decision under fire and not a free escape — but it is always the right answer, and the rules should reward a player who spots the flank in time.

<a id="c18"></a>
**C18** — Few ranks means little to absorb the blow: the men can see there is nothing behind them.

<a id="c19"></a>
**C19** — A rectangle is attacked on one of its four faces, and the face decides the count. Front and **rear** are both a full frontage — the back rank is as wide as the first. The two **sides** are the depth, a man per rank ([C15](#combat)). This is the general rule that C15 was a special case of.

<a id="c19a"></a>
**C19a** — A flat subtraction assumed every face was a full frontage: a body eight ranks deep answers a flank with eight men, so eight gaps anywhere in a regiment of eight hundred erased its entire flank contribution and the fight resolved to nothing. Against a front the two forms agree exactly, so this changes the common case not at all.

<a id="c19b"></a>
**C19b** — An attacker whose enemy is not facing it folds round whichever face is showing and brings **twice** the men standing on it, capped by its own frontage. A side is ten men, so a flank envelops with twenty; a back is a hundred, so coming in behind envelops with two hundred. This is [C15](#combat)'s attacker half and [C20](#combat)'s width, and it falls out of one rule because the face lookup is shared with the defender's side of [C19](#combat). Measured: **a rear attack now costs the defender 9.5× what a flank costs**, where before both brought the attacker's whole frontage and the ratio was 1.4×.

<a id="c20"></a>
**C20** — Same shock as [C16](#combat) — the defender deals 0.25× and takes 2.0×, decaying to 1.0× — but full frontage against full frontage rather than frontage against ranks. Being taken from behind is therefore far worse than being taken in the flank, which is right: it is the same panic with ten times as many men able to act on it.

<a id="c20a"></a>
**C20a** — It cuts the *width* a regiment brings when it is caught facing away — a quarter of it, when taken squarely from behind. That is C20's "deals 0.25×" wearing a different hat, and applying both would charge the same penalty twice. Deliberately left alone while C19 landed: moving it is a balance change that wants setting against C16's numbers rather than guessed at on its own. **Until it moves, the rear defender answers with a quarter of its frontage** — 40 men of 800 rather than the 100-against-100 the rule describes. That makes a rear attack currently 9.5:1 rather than the 4:1 the designer's arithmetic gives, so it errs on the side of too lethal, not too little.

<a id="o2"></a>
**O2** — They shoot; they do not charge home.

<a id="o3"></a>
**O3** — Neither do attackers.

<a id="o4"></a>
**O4** — A regiment that was marching and got cut off halts where the fight ends, even though its order was to go further — it neither chases nor resumes the march. Re-ordering it is the player's job. An ordered attack still runs its enemy down, with no leash.

<a id="o5"></a>
**O5** — Both aim at the defender's middle, as [O1](#orders) says. Having arrived, they *sidestep* ([M13](#moving)) along the front until each holds half of it — they do not compute an offset destination and march to it. The shuffle is visible, costs the sideways penalty, and looks like men making room; a pre-computed half-frontage aiming point looks like the regiments were never going for the same place. Two to a face is the ceiling, so "half and half" is the whole rule ([C3](#combat)).

<a id="b1"></a>
**B1** — Three knobs on one unit — worth doing together, and worth doing after [C13](#combat) exists, since "the second rank fights" only means something once ranks are modelled.

<a id="b2"></a>
**B2** — Currently a flat 0.3 for two supporting ranks.

<a id="b3"></a>
**B3** — The margin was always thin. Its test is skipped rather than nudged; settle it with B1.

<a id="b5"></a>
**B5** — Two shooting units with no reason to close now grind honestly, where before the duel was settled by rounding artefacts: one side's chip damage landed and the other's evaporated. Wants ammunition, or a reason to close, or an accepted draw. Surfaced by [C14](#combat), not caused by it.

<a id="b4"></a>
**B4** — A regiment with one rank left cannot refill at all; one with two refills exactly as well as one with ten. That is a discrete answer to a continuous question, which is a shape this project has been bitten by four times. Wants [C18](#combat)'s threshold thinking.

<a id="t1"></a>
**T1** — Kept for the reasoning that got there. The suspicion is that grid search is the wrong instrument for this game: regiments are rectangles moving in the open over mostly-passable ground, and what they actually need to answer is "is the line to there clear, and if not, where does it first stop being clear" — a cast, not a search. A* is currently the most expensive thing in the rules (4,138 cells explored on one 464 m march) and it plans in a resolution the regiment does not move in. If casting replaced it, [M14](#moving)'s compromise dissolves: orientation becomes something the cast reports rather than a dimension the search has to carry, and planning the crabbing stops being expensive. Wants a real look before the movement machinery hardens further.

<a id="t2"></a>
**T2** — A flat °/s charges a change of front against the clock, so it lands hardest on the units that spend least time marching: cavalry pays ~1.7× on a short reversed march where swordsmen pay 1.15×, which inverts the rule it was meant to express. Deliberately *not* being fixed alongside the movement work — [M10](#moving) and [M13](#moving) change how often a regiment has to reverse at all, and the number is only judgeable once marches have stopped freezing. Same shape as [C13](#combat)'s first attempt: a rate where a latency was wanted. **Measured, 14 Aug:** across 36 marches by one cavalry regiment the average change of front was **125°** costing **40 ticks**, and wheeling took **1,432 of 2,644 ticks — 54% of the recording**. Achieved pace ran 58–76% of nominal. Deliberately left alone in the [M22](#moving) pass so the planner change can be judged against it; the three candidates remain *leave it*, *raise cavalry 3→5°/s and horse archers 4→6*, or *charge the wheel against ground rather than the clock*.

<a id="m52"></a>
**M52** — [M51](#moving) said `StandCheck` stayed dear because the index had never reached `RouteSearch.CanStandHere`. It had: the review branch of [M50](#moving) indexed both it and `CanTurnHere`, with their own thread-local buffers and a squared compare, and resolving `RouteSearch.cs` to this branch threw both away. Ported now — **12,1 µs a call → 4,7**, `StandCheck` from 33,7% to 15,0% of the Crucible and 25,4% to 8,3% of the Long March, whole-field totals **1 013 → 833 ms** on Broken Country and **317 → 266** on the Long March, both at under 5% spread. Legs, states, places and pressed-through counts all identical, so again nothing about the routes moved. `BodyScan` is back on top at ~50%. **The attribution that settled it.** `Stands` memoises on (place, front-bin) and counts only its misses, while `FrontsFor` calls `CanStandHere` straight past the memo — so the question was whether the volume was misses or a leak. Measured: **29 164 invocations against 29 164 misses on the Crucible, 26 906 against 26 906 on Broken Country, and 114 of 6 168 past the memo on the Long March.** No leak. There is a structural reason and it should have been read off the table without running anything: every `FrontsFor` call sits inside `SmoothRoute`, whose *inclusive* time is 0,9 ms of 1 126, which caps the bypass at 0,9 ms however many calls it makes. Routing those two calls through `Stands` is still worth doing so it cannot become a leak later; it is not one now. **The rule this leaves.** A merge that resolves a heavily-diverged file to one side has to diff the other side's version of each method it means to keep, not reason from the commit message. The commit message named five fixes in `RouteSearch`; this was a sixth it did not mention.

<a id="m53"></a>
**M53** — The bench warmed each planner and then timed it *once*, on all three fields — so [M49](#moving)'s and [M51](#moving)'s twelve rows were twelve lone samples from a distribution since measured at 13–40% wide, and "Broken Country agreed to 0,2%" was two lucky draws being read as corroboration. The 42% by which the Crucible's two numbers disagreed needed no explanation at all: one was a median of three, the other a single draw. **The protocol now.** Nine passes on the headline test and three to nine on the per-planner one under a time budget, with `n=` printed so a thin number looks thin; `GC.Collect` and `WaitForPendingFinalizers` between passes, outside the clock; and the passes reported **in the order they ran** rather than sorted, which is what tells drift from scatter and which the old report destroyed before printing. **The headline is the least, not the median** — the work is deterministic and CPU-bound, so noise is one-sided and every pass above the floor is the floor plus something that is not the code. **What the temporal order actually showed:** not the accumulating litter that was suspected, but a high *first* pass settling over the next two — tiered compilation still promoting after the single warm pass — with scattered spikes on top. The collection is kept regardless; it is free. **And the hybrid's 3–15% was the instrument.** At n=3 it is the steadiest row in the table (0,9–7,8% apart), and against the pre-merge single draws it comes out 1,3% faster, 2,7% dearer and 2,9% dearer on the three fields — mixed signs, all inside the spread. There is no effect, which is the right answer for a planner that shares no code with anything merged. **The finding that survives all of it:** with `StandCheck` fixed in [M52](#moving), `BodyScan` is the largest step for **all four** planners of the family on **all three** fields — 43–52% for the three searches, 58–69% for the ladder — and `GroundClear` never passes 6,4%. **A tail number exists now**, because "is this faster" and "does this hold a frame" are different questions and the totals only answer the first: the dearest single order is **45,2 ms on the Crucible, 74,3 on Broken Country, 25,6 on the Long March** — one order overrunning a 16,7 ms frame by up to four and a half times, which is what [M46](#moving)'s budget exists to defer and what nothing had measured. **Still one draw:** the instrumented pass. Its overhead reads +0,2%, +3,4% and +23,2% on the three fields, which cannot all be the same probes, so no overhead claim should be made until it is repeated too.

<a id="w1"></a>
**W1** — *"Do not take everything i say as absolute law — you need to adapt it and make sense."*

<a id="w2"></a>
**W2** — *"When you think you are misunderstanding something just ask me to confirm."*

<a id="w4"></a>
**W4** — Discuss the rule before writing the code.

<a id="w5"></a>
**W5** — Every arrival in a recorded game read "4,8 m/s" — the pace the regiment *could* have made on the ground it finished on, asked of the same code under suspicion. What it actually made was 2.6 to 3.6. A line written to diagnose slow marches reported the one number that could never show one. Same shape as reporting a bearing by asking the mover what its bearing is: measure the outcome, not the intent.

<a id="w6"></a>
**W6** — *"Each collision is documented and every decision including the calculated path and how to go around."* — who, where, facing where, how deeply overlapping, what each was doing, who gives ground and why, and how long it lasted. It was completely silent before: the overlap cost, the shuffle apart and the yield rule all ran without a word, so every play-test report of "it goes through them" had to be reproduced from scratch against a recording that contained no evidence it had happened. **And a routing decision names the line, not just the rung.** Which rung of M18 answered is a one-word summary of a decision whose substance is *where it decided to walk* — so every rung now prints its waypoints, and a detour prints which side it passes and how far off the straight line it swings. Both are said **once per event, not once per tick**: a rung reports on the tick its answer changes, and a collision on the tick it opens and the tick it clears. Volume is a build gate, not a preference — `NoSingleRuleDrownsOutTheRest` failed this pass at 218 of 297 lines and was right to.

<a id="m100"></a>
**M100** — The designer, on the search stages below the grid: *"so keep grid stage and cap any search on a total of 10ms lets try it like this"*.

`Marching.SearchBudgetMs` opens a deadline per plan, and `Marching.StopNow()` folds it together with [M80](#m80)'s `GiveUpNow` so one call answers both *"has this order been superseded"* and *"has this order run out of time"*. Polled at the round loop of `RouteSearch`, every 64 expansions of its ledger, and every 64 cells of the grid A*.

**The first version made things worse and the reason is worth keeping.** Timing out the coarse grid does not end the order; it drops through to the fine tier, the tangent graph and the pose lattice, each dearer than the thing that just gave up. The Crucible went **81 → 98 ms** and its worst order 6 → 23. The fix is not a bigger budget but a short-circuit: when time is out **and the ladder already has an answer**, take the ladder's answer and stop, rather than falling onward. Same shape as [M61](#m61) and [M105](#m105) — a bound on a cheap stage is a promotion for a dear one.

<a id="m101"></a>
**M101** — A gap wide enough to march through is not the same thing as a gap. `Marching.ThreadAGap` kept every pair of bodies whose separation cleared the mover's width, which on an open field is most pairs on the board: two regiments **eight hundred metres apart** name a "gap", and the three clearance tests were then run on it in full.

`GapWidthCeiling = 2f` — a passage is a passage while it is under twice what the regiment needs, and above that it is simply open ground the straight line already had. Measured on the Crucible: passages put to the clearance tests **25 582 → 1 062**. `found`, `failed` and `pressed` identical at every ceiling tried; march seconds unchanged on four fields and **−0,49% on the Long March**.

<a id="m102"></a>
**M102** — The designer, on where a crab should look: *"a crab is good and cheap if it only tries the bodies near the main body"*, and then precisely: *"if 8 is between 7 and 9 then verify 1-8 and 8-9 then 6-7 9-10 so on ... and after a number you just stop"*.

**`ThreadAGap`'s own remarks had always described this and the code had never done it** — *"the bodies are projected onto the axis across the march, sorted, and the spaces between them read off"*. Sorted and read off is a sweep: a formed line of ten bodies has **nine** spaces in it. What was written instead paired every body with every other, which on the same ten is **forty-five**, and most of those name no space at all because three other bodies stand between the two being asked about. Second instance this pass of behaviour documented but absent, after `GiveUpNow`.

Now: bodies sorted by **signed** offset across the march — signed, or the sort interleaves the two flanks and makes neighbours of bodies on opposite sides of the line — the walk started at the body that actually blocks the straight line, and worked outward both ways, `GapSpacesEitherSide = 4`.

**It answers identically.** March seconds identical on all five bench fields; `found` 80/80/80/40/40 and `failed` 0 throughout; the Long March keeps **all 24** of its threaded gaps at every setting down to one space either side. Pairs examined fell from 587 a call to about 8, and passages tried from 1 062 to 10 on the Crucible, 760 to 8 on Broken Country, 306 to 24 on the Great Field, 281 to 23 on the Sideways Smile, 152 to 24 on the Long March. Shipped at four rather than one because cost is flat from four down and a bench is not every arrangement.

<a id="m103"></a>
**M103** — The grid A* kept a `Dictionary<Coord, Coord>`, a `Dictionary<Coord, float>` and a `HashSet<Coord>`, and built all three **afresh on every order**. `CellTable` is one open-addressed probe table per thread carrying all three columns, with a generation counter so a search begins by incrementing an integer rather than by emptying anything. Not an array indexed by cell, because the cell range is not bounded — `HexLayout.ToCoord` will name a cell off the map, and the fine tier is sixteen times denser than the coarse one, so any fixed extent is wrong at one tier or enormous at the other.

**On the clock it buys nothing measurable, and that is the honest reading** — see [W12](#w12) for how it first appeared to buy 26%. Paired against `a6d7f5b` on the same machine, order alternated, least of four passes: **+0,7% to +3,3%** across five fields with fields rebuilt every order, which is inside this machine's spread.

**What it buys is litter, and that measurement holds to a tenth of a per cent**, because allocation is counted rather than timed. Bytes an order, `GC.GetAllocatedBytesForCurrentThread` over a whole field, least of three passes, spread 0,0–0,4%:

| field | before | after | |
|---|---|---|---|
| Crucible | 340,8 kB | **95,0 kB** | −72% |
| Broken Country | 362,6 | **101,5** | −72% |
| Long March | 70,2 | **27,3** | −61% |
| Great Field | 168,4 | **59,8** | −64% |
| Sideways Smile | 152,5 | **55,9** | −63% |

With fields kept it is −60% to −79%. This is the right thing to have bought: a managed allocation is nearly free to make and is paid for later, all at once, by whichever frame the collector lands on — which is what a hitch **is**, and it is charged to a frame that did no planning at all. The editor runs Mono, where it is dearer than on the bench. Two thirds of it is this table and the rest is the two scratch collections `SharedField.Mark` made per body — a `HashSet<Coord>` per call and a list of emptied cells per unmarking, both now kept per thread.

<a id="m104"></a>
**M104** — A field was thrown away whenever anything moved. That is not what the stamp says: it is a hash over the whole army, so it says only that **something** moved, and a tick moves the regiments that are marching and leaves the rest standing. Eighty bodies were re-marked because a dozen had gone anywhere.

**Exact, not approximate.** Coverage is counted rather than flagged ([`SharedField`](../packages/com.battlechess.core/Runtime/Rules/Battle/GridPlanning/SharedField.cs)), so marking is reversible: a body taken off with the rectangle it was put on with touches the same cells and the same samples and leaves the counts where a field that had never seen it would have them. So the field remembers each rectangle — a body that has moved can no longer say where it used to stand, and unmarking it at its new place would corrupt the field in silence. Cells an unmarking empties are dropped, or a patched field keeps every cell any body has ever stood in and `CountBlocked` grows for the rest of the battle.

**The gate is cell by cell, not a timing.** `PatchingAFieldTests` shoves 1, 12 or 40 regiments the way a tick does and compares `FillAt`, `IsBlocked` and `NodeAt` over every cell of the field against one raised from nothing: **0 of 2 377 / 3 069 / 6 398 / 100 cells differ** on four fields, blocked counts equal, and it asserts that something was actually restamped so that comparing two identical fields cannot pass for a check. A second gate destroys nine regiments, because the walk over the army can say which bodies moved and cannot say which stopped existing — they are no longer in it to be asked.

**Measured within one process**, which is the only comparison this machine can carry (see [W12](#w12)): the same arrangements, the same orders, twelve regiments moved between each, patched against raised again, three independent runs —

| field | clock, three runs | bytes an order |
|---|---|---|
| Crucible | **−22% / −19% / −22%** | 39,6 kB against 99,0 — **−60%** |
| Broken Country | **−22% / −19% / −21%** | 53,7 against 168,7 — **−68%** |
| Long March | −8% / −11% / −9% | 17,4 against 27,3 — −36% |
| Great Field | −1% / −5% / +2% | 55,0 against 67,7 — −19% |
| Sideways Smile | −1% / −5% / −0% | 56,7 against 67,6 — −16% |

**And it opened a hole, which is the part of this entry worth reading.** The field cache is keyed by spacing, samples, movement type, reach and facing — by everything about the **mover** and nothing about the **battle**. That was safe only because a field was thrown away the moment anything moved, so it could not outlive the arrangement it was raised over and certainly could not outlive the map. Patching removed that guarantee, and restamping the bodies says nothing whatever about the **going**, which is cached per cell and sampled from whichever terrain the field was built with. So a second battle on a second map found the first map's field, restamped the regiments onto it, and searched it with the first map's ground.

Caught by `WingOrderTests`, and only as *"2 of 80 routes changed when the wing was planned at once"* — [M62](#m62)'s symptom exactly, three steps downstream of the cause, and visible only in parallel because which thread carries which map's leftovers is a matter of which thread the pool hands out. Bisected to the lever in one run. The guard drops every kept field when the ground under it is not the ground being asked about, and the gate for it is direct rather than downstream: raise a field on the Crucible, then ask for one on the Long March, and compare it cell by cell against a Long March field raised from nothing. **Red first at 3 800 of 6 398 cells** — with the blocked counts *equal*, because the bodies were right and only the ground was wrong, which is what made it invisible to every check that counts routes.

**Why it is not the ten-fold the arithmetic suggests**, and this is the part to read before raising it further: a patch costs a walk over the whole army to find out who moved, plus an unmark and a mark for each who did, and an unmark is a mark plus the scan that drops emptied cells. Twelve of eighty moved is 24 body-marks against 80, and it measures at about half rather than at 0,3 of a rebuild. The two forty-regiment fields gain almost nothing because twelve of forty moving every order is most of the army — there, a patch **is** a rebuild, which is the honest bound on this and not a defect in it.

<a id="m105"></a>
**M105** — Bounding the grid A* to a corridor round the drawn line, on the reasoning that the fine tier of [M87](#m87) is sixteen times the cells and spends them settling ground hundreds of metres either side of a march that was only going to bend round one regiment. Refused on the measurement, and the premise was simply wrong: **A\* with an admissible heuristic does not settle a disc** — the heuristic pulls it down the line — so the corridor refuses almost nothing until it starts refusing the route.

| corridor | Crucible ms/order | Sideways Smile pressed | Sideways Smile unwalkable | Sideways Smile march s |
|---|---|---|---|---|
| unbounded | **2,14** | **1** | **2** | **39 638** |
| x2 of the march | 2,22 | 1 | 2 | 39 638 |
| x1 | 2,24 | 2 | 3 | 38 184 |
| x0,5 | **13,08** | 11 | 12 | 28 953 |
| x0,25 | 14,18 | 11 | 12 | 28 953 |

At x2 and x1 it costs 4% and saves nothing. Below that the cascade falls through to the stages the grid exists to avoid and an order goes from 2,1 ms to 13,1 — **six times dearer for being given less to search**. Which is [W10](#w10) twice over: the cheaper number was the worse route, and then it was not even cheaper. Third instance of the same shape after [M61](#m61) and [M100](#m100). Kept as a lever at nought with its numbers, so the idea is not had again.

<a id="m106"></a>
**M106** — A clearance query scanned the **bounding rectangle** of the leg, widened by `reach + widest reach + half a diagonal`. A march is nearly always diagonal, and a rectangle drawn round a diagonal line is mostly not near the line. Clipping the segment to each bucket row's own band first and taking the columns of *that* leaves the arithmetic per bucket exactly as it was and asks it of far fewer, and is still provably a superset: a bucket whose centre is within span of the segment has its nearest point on the segment within span in **y** as well, so that point lies inside the band, so its **x** is inside the range taken.

Like [M103](#m103), **inside the noise on the clock** and kept for the operation count rather than for a number the machine could not hold: 23,5 buckets examined per query to keep 11,1, where the rectangle scan it replaces examined about forty.

<a id="w12"></a>
**W12** — **Two builds compared across two processes cannot be trusted on this machine, and the first reading said the opposite of the truth.**

The pass above was first measured by running the record before the change and again after, one run each. It reported the Crucible **535 → 397 ms, −26%**, with `GridExpand` down 29% and `FieldMark` down 24% — and `FieldMark` had not been touched, which should have been the tell and was instead written up as a plausible knock-on from allocation pressure.

Run properly — the old tree in a git worktree beside the new one, both built once, the two **alternated** so neither is always the one that runs second, least of four passes each — the same comparison is **+0,7% to +3,3%**. The −26% was the machine. The baseline's own `GridExpand` read **56,6 ms in one run and 91,3 in another** from an identical binary, a 61% spread, which is larger than every effect being claimed.

**And the first paired attempt was still wrong, in my own favour.** It showed the new tree 7 to 31% *slower* — because the new tree's record calls `RegimentGrid.Forget()` at the start of each pass and the old one did not, so the new one paid for three field builds the old one skipped. **A paired measurement must pair the harness too**, not only the code: the two trees have to run the same test, the same number of modes, the same amount of work, or the difference measured is the difference between the tests.

So: [M53](#m53)'s protocol is not enough on its own. Least-of-N inside one process handles noise within a run and does nothing about drift between runs. **Where a comparison can be made inside one process by moving a lever, make it there** — [M104](#m104)'s patched-against-rebuilt is worth believing for exactly that reason and [M103](#m103)'s is not — and where it cannot, alternate the order and say the spread out loud. Fifth instance of the family that [W11](#w11) opened: a measurement that did not measure what it said it did.

<a id="w13"></a>
**W13** — The designer, cutting short a line of work: *"some increased RAM usage is fine as long as CPU time is lower so you dont have to mind that stat too much"*.

Said after [M103](#m103) and [M106](#m106) were kept on an **allocation** measurement, having come out inside the noise on the clock. So the standing order is: **the clock is the score, and memory is currency to spend on it.** Allocation still matters where it is charged to a frame that did no planning — a collector pause is CPU time in the place it hurts most — but it is not a result on its own, and a change that only moves bytes has not moved anything the designer asked for.

**Proportionate, not unconditional.** Asked how far it goes: *"even if you use 4-8gb of ram is fine it gives a decent return (i dont want to trade 2gb ram for 2% speed but if an algorythm can be optimised for speed rather than memory then its a good idea)"*. So the budget is real and large, and it is still a budget: gigabytes are available to an algorithm that becomes fundamentally cheaper, and are not available to buy a couple of per cent. [M107](#m107) is the shape that fails this twice over — it spent a third of a megabyte per field and bought nought point seven per cent.

**It changes answers already written down.** [M103](#m103) refused a flat array over the map on the grounds that the fine tier is sixteen times denser and any fixed extent is *"either wrong at one tier or enormous at the other"*. Enormous is now allowed, so that reasoning no longer holds — and [M107](#m107) is what happened when it was tried.

<a id="m107"></a>
**M107** — Built and reverted. Under [W13](#w13) the obvious move was to make the field itself flat: `SharedField` held a `Dictionary<Coord, byte[]>` of coverage and a `Dictionary<Coord, float>` of going, and between them `FieldPatch` and `GridExpand` were **48% of planning**, nearly all of it hashing an eight-byte struct. The A* asks two of those per neighbour, six neighbours to a cell; the marking asks one per sample point of every cell every body touches. A flat array indexed by cell makes each an add and an index, and at a quarter of the coarse spacing a two-kilometre map is fifty thousand cells — a third of a megabyte, which W13 says is affordable.

**It measures at nothing.** Paired against `8a3793d`, both trees running the identical record, order alternated, least of three: **−0,7% / −0,4% / −0,4% / −1,1% / −0,7%** on the five fields in the mode that models a battle, and −7,5% to +4,8% with mixed signs in the static modes. Below the ±3% this machine can hold. Reverted rather than kept, because a whole storage scheme is not worth carrying for a number that is not there — and it is a lot to carry: it needed a bounding box over axial coordinates, a fallback for cells outside it, and a second flag array to tell *has coverage* from *is already in the touched list*, which the gate caught as a patched field holding **300 cells against a rebuilt one's 292**.

**Why the obvious win is not one, and this is the part worth keeping.** The sparse structure was faster than it looks for the same reason the dense one is slower than it looks: **a battlefield is mostly empty**, so the dictionary held only the few hundred cells anything touched, packed together and resident in cache — while the flat array scatters the same few hundred reads across a third of a megabyte. The change trades a hash for a cache miss, and on this data those cost about the same. On top of that every field raised must now allocate and zero the whole array, which in the rebuild-every-order mode is forty-three memsets of 350 kB.

So W13's licence is real and this particular way of spending it is not. Sparse-because-empty is a property of the problem, not an accident of the first implementation.

<a id="m108"></a>
**M108** — Where the remaining cost sits, after M100–M106, on the Crucible with twelve regiments moving between orders and the field patched — 412 ms of self time over eighty orders:

| step | calls | self | share |
|---|---:|---:|---:|
| `FieldPatch` | 41 | 114,7 ms | **27,8%** |
| `GridExpand` | 44 | 84,9 | **20,6%** |
| `NearQuery` | 19 972 | 59,2 | **14,4%** |
| `BodyScan` | 19 972 | 59,0 | **14,3%** |
| `FieldMark` | 3 | 28,1 | 6,8% |
| `ClearLine` | 19 972 | 14,8 | 3,6% |
| `ThreadGap` | 45 | 1,9 | 0,5% |
| `Crab` | 50 | 0,1 | 0,0% |

The crab is finished — 12,5% before [M101](#m101) and [M102](#m102), half a per cent after. What is left is three things and none of them has a cheap answer yet: patching still costs about half a rebuild because the unmark-and-mark plus the walk over the army eat the arithmetic of *twelve of eighty*; `GridExpand` is now real work rather than allocation, and [M107](#m107) says the flat-array answer to it is not one; and `NearQuery` with `BodyScan` is 29% spread over twenty thousand calls, where every attempt so far to hoist the corridor scan per plan has cost more than it saved, because a plan-wide corridor hands back three times the bodies a per-leg one does.

<a id="m109"></a>
**M109** — [M95](#m95) asked the wrong question, and this pass is what happens when the right one is asked. Its complaint was arithmetically exact: a clearance query widens by `reach + widest reach`, takes any bucket whose centre lies within half a diagonal of that, and a body may sit half a diagonal outside the bucket it was filed in — **181 m of slack around a reach of about ninety**. M95 then swept the *bucket width* over a twenty-four-fold range, found a flat basin, and concluded the slack was the price of the arrangement. It was the price of **one line** of the arrangement: a regiment was filed under the single bucket its **centre** sat in, so a query had to carry somebody else's radius in case a body filed one bucket over reached in.

**File a body under every bucket its own circle reaches and the widening is not needed at all.** A body whose circle comes near the line covers some point near the line; that point lies in some bucket; the body is filed there — so nothing can be missed and the corridor is the caller's own reach. What it costs is the de-duplicating pass the disjoint filing existed to avoid, which is one stamp per body per query against an array indexed by the body's place in the order of battle. It costs almost no memory either — a regiment's circle spans one bucket, sometimes four, at 128 m — so this is [W13](#w13)'s *"if an algorythm can be optimised for speed rather than memory then its a good idea"* rather than a purchase.

**Paired against `71cdaa2`, both trees on the identical record, order alternated, least of three. Fifteen cells, one sign:**

| field | a bench | fields rebuilt | a battle, patched |
|---|---|---|---|
| Crucible | −6,1% | −2,8% | −2,7% |
| Broken Country | −8,2% | −3,4% | −2,9% |
| Long March | −4,7% | −6,8% | −4,1% |
| Great Field | −11,0% | −9,0% | −5,5% |
| Sideways Smile | −10,6% | −8,1% | −3,7% |

Buckets opened per query fall 31% and buckets examined 26%; `NearQuery` −6% and `BodyScan` −9% on the Crucible in the battle mode. Routes unmoved — the suite is 9 failed / 650 passed throughout, and a body wrongly dropped here is a collision nobody saw, which is what the whole suite is watching for.

**The bucket width is re-swept and stays at 128 m**, now for a different reason. With the widening gone the basin moved and flattened: summed over four fields, 256 m is 1,2% better than 128 and 512 is 7% worse, but the two candidates disagree in sign per field — the Crucible prefers 256 by 5,9% and Broken Country prefers 128 by 6,7%. Below 32 m it collapses, and hard: at 8 m a query opens 345 buckets on the Crucible and 1 627 on the Sideways Smile, and an order costs three to ten times what it does at 128. No reason to move it.

<a id="m110"></a>
**M110** — The designer asked for a moved-set on the battle, so that patching a field walks the dozen regiments that moved rather than asking all eighty where they are. **Measured before building, and it is not the lever.** Splitting `FieldPatch` into the walk and the restamping says the walk is **0,5 ms of 344,8 — a seventh of one per cent.** The whole of the patch is the marking: 696 restampings at 145 µs each, 29,3% of planning. A moved-set would have been machinery for nothing, and the arithmetic was already on the table — a patch of twelve costs 2 797 µs and twenty-four body-marks at 117 µs is 2 808.

**So the lever is what marking one body costs**, and it was doing two things per cell it did not need to.

The scan walks a square around the body at 0,45 of the cell spacing, and for every cell asked <see cref="OrientedRect.ContainsPoint"/> of all seven of its samples. But the square is a square and a regiment is a rectangle inside it, so most cells cannot hold a single sample - and each of those was paying seven point-in-rectangle tests to find out, each of which reads `Forward` and `Right` off the rectangle afresh and each of which calls `SampleAt`, which works out the cell's world centre **again**.

Now the cell's centre is worked out once and projected onto the body's own axes once, which gives the true distance from the cell to the rectangle on each axis; a cell further off than the outermost sample ring can be refused outright, on one test rather than seven. The samples that survive are projected in the same frame, so `ContainsPoint`'s trigonometry is hoisted out of the loop entirely. **Nothing about what is marked changes** - the refusal is exact, not a tolerance: a sample sits at most `_ringReach` from its cell's centre, so a cell further than that outside the body cannot contain one.

**Marking a whole field of eighty: 5 453 µs → 2 649, a shade over half.** Paired against `454c063`, order alternated, least of three:

| field | a bench | fields rebuilt | a battle, patched |
|---|---|---|---|
| Crucible | −12,1% | **−24,6%** | **−18,0%** |
| Broken Country | −8,3% | **−22,2%** | −13,6% |
| Long March | −1,3% | −11,5% | −5,6% |
| Great Field | −9,7% | −20,6% | −6,2% |
| Sideways Smile | −8,3% | −18,4% | −5,8% |

Fifteen cells, one sign again. Routes unmoved: 9 failed / 651 passed, the same nine.

<a id="m111"></a>
**M111** — Built and reverted, and this time with numbers rather than a paper estimate. A plan asks *"who is near this line"* about two hundred times, and the lines are legs of one march mostly lying on top of each other — the straight cast, the arc round a body, the halves of a bend, the smoothing casts. So a query was widened by a slack and kept, and a later leg answered from it whenever the leg lay wholly inside the corridor already asked for. The reuse is **provable, not likely**: a kept answer holds every body within `cachedReach` of the old line, so it serves a new leg whenever every point of that leg is within `cachedReach - reach` of the old line, and distance to a segment is convex along a segment, so testing the two ends tests the whole leg. It is also confined to the inside of a plan, because the test is geometric and says nothing about time — a corridor kept across a tick would answer with an army that has marched off it.

It works, and it costs more than it saves, at every setting:

| slack | Crucible ms/order | queries | reused | bodies scanned | `NearQuery` | `BodyScan` | the two together |
|---|---|---|---|---|---|---|---|
| **off** | **2,281** | 17 728 | 0 | 226 940 | 41,2 ms | 40,1 ms | **81,3 ms** |
| 10 m | 2,401 | 15 138 | 2 590 | 245 429 | 37,4 | 47,1 | 84,5 |
| 25 m | 2,351 | 12 382 | 5 346 | 278 605 | 33,3 | 55,1 | 88,4 |
| 50 m | 2,390 | 8 777 | 8 951 | 328 080 | 27,9 | 58,4 | 86,3 |
| 100 m | 2,423 | 4 370 | 13 358 | 450 987 | 19,3 | 71,6 | 90,9 |

At a hundred metres of slack it answers **three queries in four from the corridor** and the query time falls by more than half — and the bodies scanned **double**, and the scan costs more than the query saved. The exchange rate is the whole finding and it is not close to favourable: one avoided query saves about 2,3 µs and brings roughly seventeen extra bodies with it, at about 0,14 µs each. It is a loss at 10 m and a bigger loss at 100, on every field, monotonically. `routed`, `pressed` and the seconds of marching are identical at every setting, so nothing about the answers is in question — only the price.

Third time this idea has been costed and the first with numbers on it, so it is now closed rather than merely doubted. The reason it looked promising after [M109](#m109) — a corridor that no longer carries the widest radius on the field is cheap enough to widen — is true and beside the point: the cost was never the width of the query, it was the length of the list it hands back.

<a id="m112"></a>
**M112** — *Corrected in part by [M113](#m113): this entry's worst-order column is a single sample of a tail and does not order the caps, and its guess at why a tighter cap costs more is wrong.*

**M112** — The designer: *"experiment how many press throughs if we cap everything at 10ms (so like full algorithm ladder + whatever else to go at a total of 10ms). also verify for 5ms"*. [M100](#m100)'s budget is opened from the stamp the order is charged against, so it already caps the cascade end to end rather than the searches alone, and the question can be put to it directly. Five bench fields, three quality passes a row because **a wall-clock cap makes planning non-deterministic** — the same order on the same arrangement finishes under the wire on one pass and is cut off on the next, so what a cap costs is a distribution.

| cap | pressed, all five fields | orders over budget | worst order | ms an order | marching |
|---|---|---|---|---|---|
| **off** | **1** | 0 | 29,0 ms | 2,31 | — |
| 20 ms | 1 | 10–14 of 320 | 32,4 | 2,34 | unchanged |
| **10 ms** | **2** | 30–34 | **43,6** | 3,14 | −0,3% |
| **5 ms** | **5** | 117–125 | 38,7 | 2,94 | −1,4% |
| 2 ms | **64–67** | 518–524 | 3,3 | 1,07 | **−15%** |

**The answer to the question asked: almost none.** At ten milliseconds the whole bench gains **one** shoulder-through, on the Great Field, and every order still routes — 80/80/80/40/40. At five it gains four, and the marching is 1,4% shorter, which is press-throughs cutting corners and not routes improving. Only at two does it collapse: sixty-odd of 320 orders shouldering through their own, and the 15% drop in marching is that and nothing else.

**Two things the experiment turned up that were not asked for, and both matter more than the answer.**

**The cap does not cap.** At a ten-millisecond budget the worst single order is **43,6 ms**, and at five it is 38,7. `Marching.StopNow` is polled at the round loop of `RouteSearch`, every 64 expansions of its ledger and every 64 cells of the grid A\* — and nowhere else. Raising a field, restamping it, smoothing a route, the arch and the crab and every `IsClearLine` inside them run to completion whatever the clock says. So the budget bounds the *searches* and the entry says "the cascade", and on the tail — which is the only place a budget is for — it is out by four times.

**And below twenty milliseconds the cap makes orders dearer, not cheaper.** 2,31 ms an order uncapped, 3,14 at ten and 2,94 at five. Same shape as [M61](#m61), [M100](#m100) and [M105](#m105) for the fourth time: a stage cut off does not end the order, it hands it down to the stage below, and everything below the grid is dearer than the grid. M100's short-circuit only fires where the ladder already has an answer; where it has none the cascade goes on paying.

**This machine is not the game.** The bench is CoreCLR and the editor is Mono, two to four times slower on this arithmetic, so ten milliseconds here buys two to four times the work ten milliseconds buys in play. **The 5 ms row is the better guide to what a 10 ms cap will feel like in the editor** — four extra press-throughs across 320 orders, and a tail still eight times the cap.

### M113 — the cap's escape is in the wrong place, and the worst order is noise

**The designer, reading M112:** *"how is it that the cap at 10 ms has worst order
43 but cap at 20 ms has worst order at 32 it makes no sense if the cap is 10 ms
then worst is 10ms isnt it?"*

Two separate answers, and the first is a correction to M112.

**The worst-order column is a single sample and it swings.** Uncapped Broken
Country read **30,3 ms** in one run of `WhyATighterCapCostsMore` and **47,2 ms**
in the next — same binary, same setting, nothing changed between them. So M112's
"43,6 at ten against 32,4 at twenty" is not a real ordering, and reporting it as
one was over-reading a max. **A maximum over 80 orders has no central tendency to
average out; it is the tail itself, and one sample of a tail is not a
measurement.** Read the mean, and read a max only as an order of magnitude.

**But the cap does not bound an order, and that part stands.** Two counters split
out of the escape gate say why, and both are **zero at every budget**:

| cap | worst ms | ms/order | spent | escaped | coarse won | fine asked | fine won | pose asked |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| off | 29,5 / 47,2 | 2,60 / 2,98 | 0 | 0 | 128 | 44 / 40 | **44 / 40** | **0** |
| 20 ms | 32,6 / 45,2 | 2,41 / 2,84 | 0 | 0 | 128 | 44 / 40 | 40 / 36 | 4 / 4 |
| 10 ms | 44,5 / 40,4 | 3,20 / 3,22 | 0 | 0 | 128 | 44 / 40 | 28 / 24 | 16 / 16 |
| 5 ms | 41,0 / 38,0 | 3,03 / 3,39 | 0 | 0 | 128 | 47 / 48 | **21 / 12** | **19 / 24** |

*(crucible / brokencountry, least of three passes, counters over four passes.)*

`escaped` is the door at `StagedRoutePlanner.Choose` firing; `spent` is the clock
being out at the moment an order reaches it, whether or not it had an answer to
leave with. **`spent` is zero**, so the door is not barred by its
`ladder.Path.Found` condition — **it is never even reached in time**. It sits
after the coarse grid, which is cheap; the budget is spent in the *fine* tier and
the pose search, both of which are past it. A cap whose only exit is checked
before the expensive work cannot bound the expensive work.

**And the mechanism for orders getting dearer is not what M112 guessed.** It is
not demotion into the fine tier: `fine asked` is flat (44, 44, 44, 47). It is that
the cap makes the fine grid **fail** rather than skipping it. `RegimentGrid`
polls `StopNow` every 64 cells and breaks with no route, so a search that would
have answered now answers nothing — `fine won` **44 → 21** — and the order falls
through to the tangent graph and the pose search, `pose asked` **0 → 19**. The
lattice is tens of milliseconds by design. **The cap converts a finished cheap
search into a failed cheap search plus a whole dear one.**

This is the fifth instance of the shape behind M61, M100, M105 and M112: cutting a
stage off does not end an order, it hands the order downwards, and everything
below the grid is dearer than the grid.

**Not fixed here — measured only.** The fix has two halves and neither is taken
yet: move the escape so it is asked *between* stages rather than once, and make a
search that gives up on the clock say so, so the cascade can stop rather than
step down. `OutOfTimeAtTheGrid` also never reset with the other counters, so any
earlier table that read it was reading every pass since the process began; fixed.

### M114 — the cap asked at every stage boundary, and never inside one

**The designer, after M113:** *"when i said the cap i meant for the overall search
i think you understood it wrong."*

**The scope was already right and the enforcement was not**, which is worth
separating because it changes what needed building. `Marching.PlanTo` opens the
budget from the same timestamp the order is charged against and closes it in the
`finally`, so it already spans the whole cascade — ladder, crab, coarse grid, fine
grids, tangents, lattice, smoothing. What it lacked was any power to stop one.
[M113] measured the single door it had and found the count of orders arriving at
it out of time was **zero at every budget**: it sat after the cheap coarse grid
while the milliseconds go in the dear stages past it.

**So the clock is now asked at five stage boundaries** — before the coarse grid,
before the fine tier, before *each* finer spacing, before the visibility graphs,
and before the pose search — each handing back the answer already in hand and
already proved: the ladder's route, or the press it declared ([M98]).

**Asked between stages and never inside one, and that is a correctness
requirement rather than a preference.** A field is raised once and thereafter
patched, so a raise abandoned half way would be stamped current and read wrong by
every later order — exactly the fault [M104] shipped. A stage either runs or does
not start.

| cap | worst ms | ms/order | over | routed | pressed | before M114: worst / ms-order |
|---|---:|---:|---:|---:|---:|---:|
| off | 36,5 / 32,7 | 2,27 / 2,60 | 0 | 80 | 0 | — |
| 20 ms | **20,1 / 20,1** | 2,19 / 2,43 | 4 / 6 | 80 | 1 / 1 | 32,6 / 2,41 |
| 10 ms | **18,1 / 17,8** | **1,98 / 2,12** | 16 / 20 | 80 | 4 / 4 | **44,5 / 3,20** |
| 5 ms | 14,2 / 14,7 | 1,76 / 1,78 | 30 / 26 | 80 | 4 / 6 | 41,0 / 3,03 |
| 2 ms | 3,7 / 3,2 | 1,11 / 1,02 | 130 / 132 | 80 | 22 / 28 | — |

*(crucible / brokencountry; worst is the **worst** of the repeats, not the least,
because a claim that something is bounded must be tested against the worst seen.)*

**The inversion is gone.** A 10 ms cap used to make an order dearer than no cap at
all — 3,20 ms against 2,27. It now makes it **cheaper**, 1,98, which is what a cap
was always supposed to do. Every order still routes at every budget.

**It bounds an order to about twice the cap, not to the cap.** 18,1 ms under 10,
20,1 under 20. That is the gate's design showing through honestly: it stops a
stage *starting*, so the overshoot is whatever single stage was already running,
and the coarse grid's raise-and-search is itself ten-odd milliseconds on a bad
order. Cutting it further means making a field raise resumable, which is a much
larger change than this one and is not taken.

**What it costs is press-throughs, and the editor will pay more than this table.**
Four of eighty at 10 ms against none uncapped. The bench is CoreCLR and the editor
is Mono at two to four times slower, so **10 ms in play buys what 2 to 5 ms buys
here** — the 2 ms row presses 22 to 28 of 80. `BattlefieldController` ships
`SearchBudgetMs = 10f`, so this is live in the next play-test and the symptom it
would produce is *"cavalry goes through units"*, which is the report this whole
line of work began from. **Raise the setting before reading a play-test as a
routing fault.**

`OutOfTimeWithNothing` counts orders that ran out holding neither a route nor a
press, which have nothing to leave with and so carry on past every gate. **It is
zero on both fields at every budget**, so the hole is real but unreached. Refusing
those orders outright would leave a regiment standing still because planning was
slow — a rule about what the game does rather than what it costs, and the
designer's to settle.

### M114a — the floor under the cap, measured by driving the cap to nothing

**The designer:** *"so if the full search is capped to 2ms how do you have worst
ms at 3.7 then"*. The same question as before and it deserves a number rather
than a mechanism, so the cap was swept to where it cannot matter.

| cap | worst ms | ms/order | pressed | 2nd cast |
|---|---:|---:|---:|---:|
| off | 57,0 / 37,3 | 2,35 / 2,70 | 0 | 0 |
| 10 ms | 18,2 / 18,5 | 2,01 / 2,17 | 4 | 0 |
| 2 ms | 3,4 / 3,2 | 1,06 / 1,02 | 21 / 30 | 0 |
| 1 ms | 2,0 / 3,0 | 0,74 / 0,71 | 32 | 0 |
| 0,5 ms | 2,3 / 2,3 | 0,74 / 0,71 | 32 | 0 |
| 0,1 ms | 2,7 / 2,6 | 0,73 / 0,71 | 32 | 0 |
| **0,01 ms** | **2,0 / 2,4** | **0,74 / 0,71** | 32 | 0 |

**The floor is about 2 ms on the worst order and 0,71 ms on the average, and no
cap can go below it.** From 1 ms downwards every column is identical: the same
32 press-throughs, the same 0,71 ms an order, the same worst. A budget of a
hundredth of a millisecond buys exactly what a budget of one buys, which is the
definition of a floor.

**What is in it is the ladder, and the ladder cannot be gated by this design.**
Every gate hands back *what the ladder proved*, so the ladder has to have run
before the first gate can be asked. Smoothing and the fronts pass sit the other
side of `Choose` in `Cast` and are outside the gates for the same reason — they
run on the answer, not on the way to it. So an order costs one whole ladder, one
smoothing pass and one fronts pass whatever the clock says, and a 2 ms cap
produces 3,2 ms because that is the floor plus whatever stage had already begun.

**A correction to [M114]'s reasoning, though not to its numbers.** That entry
reasoned about `Cast` running twice under the M94a arrival licence. The counter
says `ArrivalAsked` is **0 at every budget on both fields** — the first cast
answers, so the second never happens here. The floor is one cast, not two.

**Below 2 ms the setting stops being a budget and becomes a switch.** At 1 ms and
under, 32 orders press through and 96 bodies end up unwalkable — the cascade is
being skipped wholesale rather than bounded. **2 ms is the lowest setting that
still plans anything**, and 10 ms remains the sensible operating point.

Lowering the floor further means interrupting the ladder itself, which means
deciding what a regiment does when even the straight-line check has not finished
— a rule about the game rather than about its cost, and the designer's to settle.

### M114b — the floor taken apart: it is the arch, asking who is near a line

**The designer:** *"so the ladder itself is 2ms?"*. No, and [M114a] should not
have implied it. Profiled at a 0,01 ms budget, where every gate fires at the first
opportunity and what is left on the clock is by construction only the work no gate
can prevent:

| | crucible | brokencountry |
|---|---:|---:|
| an ordinary order, uncapped | 2,364 ms | 2,612 ms |
| **the floor, an order** | **0,740 ms** | **0,719 ms** |
| the floor, worst single order | 2,0 ms | 2,4 ms |
| the floor as a share of an order | 31% | 28% |

**So the ladder is about three quarters of a millisecond, not two.** Two
milliseconds is its *worst single order* over eighty, and [M113] is the standing
warning against reading a maximum as a typical cost.

Where the floor's 63,7 ms over eighty orders goes, inclusive:

| step | incl ms | share of the floor |
|---|---:|---:|
| `Plan` | 63,7 | the floor itself |
| `Ladder` | 60,7 | **95%** |
| `WayRound` — the arch | 55,5 | **87%** |
| `Crab` | 2,7 | 4% |
| `Rung1` — the straight line | 0,8 | 1% |
| `SmoothRoute` | 0,3 | **0,5%** |

**Two corrections to [M114a], both in the same direction.** Smoothing and the
fronts pass are not in the floor in any meaningful sense — 0,3 ms across eighty
orders, half a percent — so naming them alongside the ladder was wrong. And the
ladder is not a flat cost either: **the arch is 87% of it and the straight line is
1%**.

**And the floor is not really an algorithm, it is a query count.** `BodyScan` self
22,4 ms and `NearQuery` self 19,0 ms are together **65% of the floor**, over
**8 986 calls in eighty orders — 112 clearance queries an order**, nearly all of
them from the arch. What no gate can prevent is not "planning"; it is asking *who
is near this line* a hundred and twelve times.

That is the target for anyone lowering the floor, and it is a target with a
history: [M111] tried to make those queries cheaper by keeping a corridor between
legs and lost monotonically, because *"the cost was never the width of the query,
it was the length of the list"*. Fewer queries, then, rather than cheaper ones —
which means the arch, and which is a change to how a way round is found rather
than to what it costs. Not taken here.

### M114c — the arch is not the dearest step; it is the dearest *survivor*

**The designer:** *"so wayaround is the most expensive?"*. Not in an ordinary
order, and the difference is self time against inclusive time — which is exactly
the distinction [M114b]'s table was too quick with.

An uncapped pass, eighty orders, by **self** time — the work a step does itself
rather than the work it calls:

| step | crucible self | brokencountry self | share |
|---|---:|---:|---:|
| `GridExpand` | 58,7 ms | 75,3 ms | **30% / 33%** |
| `NearQuery` | 40,6 | 46,8 | 21% / 21% |
| `BodyScan` | 39,8 | 43,2 | 20% / 19% |
| `FieldMark` | 14,3 | 14,4 | 7% / 6% |
| `ClearLine` | 10,7 | 12,4 | 5% / 5% |
| **`WayRound`** | **7,8** | **7,6** | **4% / 3%** |
| `Ladder` | 1,7 | 1,8 | 1% / 1% |
| total | 197,3 | 227,8 | |

**The arch's own work is three to four per cent of an order.** Its inclusive 55,6
ms is large, but inclusive time is what a step *and everything under it* cost, and
what is under the arch is clearance queries — which the grid stages ask too, in
larger numbers. Reading 87% off an inclusive column and calling the arch expensive
was reading a delegation as a cost.

**The dearest single step is `GridExpand` at 30–33%**, and the dearest *thing* is
not a step at all: `NearQuery` + `BodyScan` together are **39–41% of every order**,
17 728 and 20 288 calls, asked by every stage that exists.

**So the arch is the dearest survivor rather than the dearest step.** [M114b]'s
number was taken with the budget at a hundredth of a millisecond, where the grid,
the fine tier and the lattice have all been gated away and only the ladder is
left. Under a cap the arch is 87% of what remains; uncapped it is 4% of what
runs. Both true, and only one of them is an answer to *what costs the most*.

**Which does not change where the lever is, only its name.** Fewer clearance
queries is the same conclusion [M114b] reached and [M111] failed at from the other
side, and it is worth more than the arch alone suggested: at 39–41% of an order it
is the largest single thing in the profile after the grid's own expansion.

### M115 — the cascade in the order it runs, for the average order and the worst

**The designer:** *"if you run from start to finish, just all the orders, can you
show me in order every single step of search, both for average and for worst order,
the cost in ms?"*

Two things the ordinary profile could not answer. It sorts by cost, which says
*what is dear* and hides *what runs when*; and it is cumulative over a pass, so
there is no such thing in it as an order, let alone a worst one. `PlanningProfile`
gained `SelfTicks` and `Calls` snapshots so a run can be cut into orders, and the
steps are listed in cascade order by hand — an order that exists nowhere else in
the code, because the cascade's sequence lives in `Choose`'s control flow.

**The worst column is one real order, not a maximum per step.** Each step's own
worst, taken separately, sums to far more than any order ever cost and describes
an order that never happened. This is the single dearest order broken down, so the
column sums to its own total.

Uncapped, and the budget is set to nought inside the record rather than inherited,
because it is a static shared with an order-sensitive suite.

| field | orders | average | worst | worst was |
|---|---:|---:|---:|---|
| crucible | 80 | 2,699 ms | 19,959 ms | Swordsmen |
| broken country | 80 | 2,757 | 21,339 | Swordsmen |
| long march | 80 | 1,034 | 5,444 | Archers |
| great field | 40 | 1,911 | 10,426 | Swordsmen |
| sideways mile | 40 | 1,872 | 13,727 | Swordsmen |

**The finding is that the average order and the worst order have different
shapes, and so want different fixes.**

| | average | worst single order |
|---|---:|---:|
| `GridExpand` | 11–36% | **20–56%** |
| `BodyScan` + `NearQuery` | **41–60%** | 22–51% |
| the ladder, all rungs | 5–9% | 1–4% |
| `SmoothRoute` | 1–2% | 1–3% |

**The average order is clearance queries** — 137 to 254 of them per order, 41 to
60% of the clock, spread across every stage. **The worst order is the grid's own
expansion**: on the Crucible's dearest order `GridExpand` alone is 11,182 ms of
19,959, **56%**, against 36% of the average. That order asked 930 clearance
queries against an average of 222.

So the tail is not the average order being unlucky; it is a *different
distribution of work*. Cheapening the queries would move the average and barely
touch the stutter; bounding the grid moves the stutter and barely touches the
average. [M114]'s gates do the second, which is the right lever for the thing a
player feels.

**Two stages essentially never run.** `TangentGraph` is absent from all five
fields — zero calls — and `PoseSearch` appears once in 320 orders, on the sideways
mile. The cascade below the fine grids is, on these arrangements, dead weight kept
for the arrangements that need it. Worth knowing before anybody optimises it.

**`Rung1` is 0,000 ms on every field.** The straight line, which answers a good
share of all orders, costs nothing measurable. What costs is proving it clear,
which is charged to `ClearLine` and its children rather than to the rung.

### M116 — the corridor refused a second time, and this time we know why

**The designer:** *"limiting search radius (every) to 4x straight line length on
all sides"*. [M105] refused this, but only tight — a quarter and one times the span
— and it lost for a reason [M114] has since fixed, so it was worth asking again and
worth asking wide. The corridor is a half-width, so ×1 already admits a square of
side twice the march.

| corridor | crucible ms/order | worst | broken country | great field | cells outside | pressed |
|---|---:|---:|---:|---:|---:|---:|
| unbounded | 2,316 | 35,7 | 2,605 | 2,090 | 0 | 0 |
| ×4 | 2,383 | 34,6 | 2,688 | 2,111 | **0** | 0 |
| ×2 | 2,381 | 29,5 | 2,685 | 2,124 | **0** | 0 |
| ×1 | 2,375 | 29,4 | 2,689 | 2,103 | 2 352 | 0 / 2 |
| ×0,5 | **13,072** | **98,6** | 3,965 | 4,292 | 23 008 | 0 / 12 |

**At ×4 and ×2 the corridor refuses not one cell on any field.** That is the whole
answer, and it is a fact about the search rather than about the width: the grid A\*
is goal-directed, so it does not wander off the straight line and there is nothing
out there to prune. The 3% the bounded rows lose is the corridor test itself,
charged on every cell for nothing.

**Tighten it until it does bite and it bites the route, not the cost.** At ×1 it
refuses 2 352 cells and the clock does not move, but the great field loses two
routes to press-throughs. At ×0,5 the crucible goes to 13,072 ms an order — 5,6
times worse — with a worst order of 98,6 ms, reproducing [M105] exactly.

**So the grid's cost is not breadth, it is depth.** `GridExpand` is dear because it
settles many cells *near* the line through congested ground, not because it
searches far from it. No bound on where it may look can help; only a bound on how
long it may look, which is what [M114]'s gates already are. **Do not have this idea
a third time.**

### M117 — where a capped order's time actually goes, and what would make 5 ms bind

**The designer:** *"can't we somehow start the order and if it exceeds a threshold
(5ms) it presses through? or something that caps worst order so it cannot go over
5ms"*.

The per-order profile from [M115], run again at a 5 ms budget. The worst order on
the crucible costs **7,794 ms** and is made of:

| step | worst ms | share | calls in that order |
|---|---:|---:|---:|
| `NearQuery` | 2,357 | **30,2%** | 943 |
| `GridExpand` | 1,959 | 25,1% | 2 |
| `BodyScan` | 1,884 | 24,2% | 943 |
| `ClearLine` | 0,576 | 7,4% | 943 |
| `SmoothRoute` | 0,207 | 2,7% | 3 |
| `WayRound` | 0,176 | 2,3% | 1 |
| `GridFieldFine` | 0,142 | 1,8% | 1 |

**The overshoot is not one dear un-interruptible stage. It is 943 clearance
checks, 62% of the order.** `IsClearLine` polls nothing, so a stage part-way
through testing nine hundred legs runs every one of them whatever the clock says,
and the gates between stages never get asked.

**Which makes a hard 5 ms cap much cheaper to build than it looked.** The earlier
worry was that a field raise cannot be abandoned half way without leaving a field
stamped current and wrong ([M104]). That is still true and still forbids polling
there — but `GridFieldFine` is 0,142 ms of the worst order, 1,8%, so it is not
what needs stopping. **The clearance loops are, and they are safe to abandon**:
`WalksCleanly`, `RouteSmoothing`, `RouteFronts` and the candidate loops in
`WaysRound` all hold no cached state, so an abandoned one returns *not clear* or
*unsmoothed* and nothing downstream is corrupted.

Not built — it changes what a regiment does when the clock beats it, which is the
designer's rule to set, not a cost question.

### M118 — a query hands back thirteen, not three hundred, and tightening it loses

**The designer:** *"but bodyscan doesnt have to be used against 300 bodies, only
the ones in the radius right?"* Right, and it never was — but the numbers behind
that are worth having, because they say where the remaining slack is.

Counted per clearance query on the crucible:

| | per query | in the worst order |
|---|---:|---:|
| buckets touched | 16,75 | 16,16 |
| buckets visited | 7,27 | 6,71 |
| **bodies handed back** | **12,80** | **13,50** |
| **sweep tests actually done** | **1,48** | **1,28** |

So the radius filter works, and **seven bodies in eight are handed back and then
thrown away by the caller**. The gap is the bucket: at 128 m a query keeps a
bucket if its *square* reaches the corridor, so a body in the far corner of a kept
bucket comes back although it is nowhere near the line.

**Built, measured, refused — and the first measurement of it was wrong in the
usual direction.** `UnitIndex.SiftAtTheIndex` tests each body against the segment
before handing it back. The first run reported +9% to +16%, and that number
included a mistake of mine: `Marching`'s body scan **already applies exactly this
test** — `span = reach + BoundingRadius`, the same projection onto the same
segment — so the sift was *added* rather than *moved*, and every body paid the
arithmetic twice. Skipping the caller's copy when the sift is on is the honest
comparison:

| field | ms/order off | on | change | yield/query off → on | routes |
|---|---:|---:|---:|---:|---|
| crucible | 2,231 | 2,538 | **+13,8%** | 12,80 → 3,35 | identical |
| broken country | 2,548 | 2,795 | +9,7% | 10,62 → 3,07 | identical |
| long march | 1,056 | 1,169 | +10,7% | 9,82 → 2,58 | identical |
| great field | 2,065 | 2,186 | +5,9% | 7,14 → 3,60 | identical |
| sideways mile | 2,011 | 2,120 | +5,4% | 7,06 → 3,54 | identical |

**Still a loss, and now for a reason worth keeping.** The test did not get dearer,
it got **asked more often and colder**. The caller applies it after `IsOnField`
and `IsInTheWayOf` have already thrown bodies out, against a list it has just
built and is walking in order; the index applies it to every body in every visited
bucket, each one a dereference into the order of battle. Moving a rejection
earlier only helps if the thing it rejects was going to cost something, and here
the caller's version of the same rejection was already the cheap one.

**Fourth time this lesson.** [M111] cached the query and lost; [M116] bounded the
search and lost; this narrowed the answer and lost; and [M95] swept the bucket
width and found a flat basin. **The clearance query's cost is not the size of the
list it returns.** At 2,3 µs a call over 12,8 bodies it is already about 180 ns a
body, and there is no fat left in *which* bodies it looks at.

Kept behind the switch, off, as a measurement rather than deleted.

### M119 — the unified map already exists, twice, and the two halves do not talk

**The designer:** *"maybe we have some kind of map with the state of all the game
(all the terrain, obstacles, and units) and we could somehow extract out of it just
the units in a radius"*.

Both halves of that are built, which is worth writing down because the idea will
keep recurring:

- **`UnitIndex`** is the extract-by-radius half: a bucket grid over every unit on
  the field, asked by point or by segment, 128 m buckets, refiled when the
  arrangement changes. It is what [M118] just measured.
- **`SharedField`** is the whole-state half: a hex field over the whole map
  carrying terrain going cost *and* body coverage together, cached per footprint
  and spacing, and since [M104] patched incrementally as regiments move rather
  than raised again.

**The gap the designer's framing exposes is real, though, and it is not the one
the question asks about.** These two are separate representations of the same
information, and the cascade uses them separately: the grid stages reason over
`SharedField`, while every clearance check goes to `UnitIndex` and then sweeps
rectangles. **The field already knows where the bodies are, and the clearance
check re-derives it.**

Untried, and worth naming precisely so it can be tried or refused on purpose. A
field cell is regiment-sized and marked for a particular footprint and facing, so
it cannot *replace* the swept-rectangle test — it is coarse where the sweep is
exact, and the whole reason a grid route still has to pass `WalksCleanly`. What it
could do is answer the easy cases without a query at all: if every cell along a
leg is wholly unmarked, no body is near it and there is nothing to scan.

**The prior is poor and should be said out loud.** That would be the fifth attempt
in this family. What would make it different from the four that lost is that it
removes the query rather than narrowing it — but the ceiling on it is exactly the
share of clearance checks that touch no marked cell. **So that was measured first,
before anything was built.**

### M119a — the ceiling is five per cent, and three quarters of every check is a refusal

Every clearance check on every bench field, sorted by what it found:

| field | checks | nobody near | share | somebody near, clear | **blocked** | query+scan | **ceiling** |
|---|---:|---:|---:|---:|---:|---:|---:|
| crucible | 17 728 | 2 249 | 12,7% | 2 033 | **13 446 (75,8%)** | 37,8% | **4,8%** |
| broken country | 20 288 | 2 860 | 14,1% | 1 864 | **15 564 (76,7%)** | 36,3% | **5,1%** |
| long march | 11 323 | 1 478 | 13,1% | 1 192 | **8 653 (76,4%)** | 49,6% | **6,5%** |
| great field | 6 156 | 821 | 13,3% | 710 | **4 625 (75,1%)** | 33,7% | **4,5%** |
| sideways mile | 5 461 | 747 | 13,7% | 664 | **4 050 (74,2%)** | 30,9% | **4,2%** |

**The answer to the question asked: the ceiling is 4,2% to 6,5% of an order**, and
the cell walk that would replace the query has to come out of that, so the real
figure is lower and possibly nothing. Only one clearance check in eight has nobody
near it. **Not worth building**, and that is now a measurement rather than a
guess — which is the whole point of asking before building, after four in a row
that were built first.

**But the table answers a question nobody asked, and it is the larger one.
Three quarters of every clearance check in the game finds a blocker** — 74% to
77%, on every field, at every size. The planner is not mostly checking legs that
work; it is mostly discovering that the legs it invented do not.

That is not a micro-optimisation to be found in `UnitIndex` or `Sweep`. It says
**the candidate generators propose four legs for every one that survives**, and it
puts the five losses in this family in their proper light: [M95], [M111], [M116],
[M118] and now [M119] were all attempts to make a wasted question cheaper to ask.
The question itself is the waste.

Where that leads — fewer and better candidates from the arch and the tangent
graph, rather than a faster answer to each — is a change to how a way round is
found, so it is the designer's to direct and is not taken here. But it is where
the next real saving is, and the size of it is 76% of 31–50% of every order.

### M120 — who asks the checks that fail, and the smoother's forty-three casts a shortcut

**The designer:** *"so how do we generate fewer candidates?"*. [M119a] said three
quarters of every clearance check finds a blocker but not whose, which is the
difference between *the arch invents bad arcs* and *the grid proposes routes that
will not walk*. So each check is charged to the innermost open stage rather than
to the check.

| asked by | share of checks | refusal rate | share of all refusals |
|---|---:|---:|---:|
| `WayRound` — the arch | 41–49% | **81,5–85,5%** | 45–52% |
| `SmoothRoute` | 36–42% | **95,8–97,7%** | 46–54% |
| `Plan` (the ladder's own casts) | 9% | 2–4% | 0,3% |
| `GridFine` | 5–8% | 0–2% | 0,1% |
| `Crab`, `Rung1`, `ThreadGap` | ~1% | 63–100% | 1–4% |

**Two stages are 85–90% of every clearance check in the game and 93–99% of every
refusal.** The grid, which [M115] showed to be the dearest *step*, barely refuses
anything: its checks pass. Nothing else is worth looking at.

**The smoother is the surprise, and its loop explains it exactly.**

| field | casts tried | shortcuts taken | casts per shortcut | stretches with nothing clear |
|---|---:|---:|---:|---:|
| crucible | 6 344 | **148** | **43** | 218 |
| broken country | 8 608 | **140** | **62** | 229 |
| great field | 2 352 | **83** | **28** | 114 |

`Smooth` scans **furthest first** — from the last waypoint backwards, taking the
first clear cast — on the stated reasoning that *"stopping at the first one that
happens to be clear would keep most of the wobble it exists to remove"*. That
reasoning is sound against a *nearest-first, take-first-success* scan. It is not
sound against **extending outwards and stopping at the first failure**, which
returns the furthest point that is clear *contiguously* — the same answer wherever
clearance is monotone along the route, and it is monotone whenever the route bends
round a body, which is why the route bent.

The cost of the difference is the whole finding. Furthest-first pays one failed
clearance check for every waypoint it walks past, so a route whose only available
shortcut is short pays for the entire route. **Forty-three casts to make one
shortcut on the crucible, sixty-two on Broken Country.** And on the stretches
where nothing is clear at all — 218 of 366 on the crucible — it walks the whole
route and buys nothing.

**Measured, the outward scan reaching the same points costs 33,8% to 40,5% of the
casts.** That is a **60–66% cut in smoothing casts**, which is 36–42% of all
clearance checks, which are 31–50% of an order: **roughly 7–12% of every order,
for a change of loop direction.**

**Not taken here** — and then taken, measured and left off in [M121], which also
corrects the 7–12% below: it is a share of *checks*, and this pass's checks are
the cheap kind. It is not free: the two scans differ wherever clearance is
non-monotone along a route, and where they differ the outward one keeps a
waypoint the furthest-first one would have dropped. That is a route-quality
question — [W10] says a cheaper number is not a better route — and it wants the
gate [M119a] implies: the same routes, or a stated count of routes that changed
and why. The designer's call.

**And the arch is the other half**, at 81,5–85,5% refused over 8 646 casts. Same
shape, different cause: the smoother tries too many *ends* for one start, and the
arch proposes too many *arcs*. Cheaper pruning before the swept test — a bounding
test on whether the arc's corridor can clear the blocker at all — is the obvious
first thing to measure there, and it has not been measured.

### M121 — the outward scan, built, measured, and left off

**The designer:** *"do it behind a lever and see what routes change"*. [M120]
predicted an outward scan at a third of the casts and put it at 7–12% of an
order. The lever is `RouteSmoothing.ExtendOutwards`. Both halves were measured:
what it changes, and what it saves.

**What it changes.** Both arms plan the same orders against the same
arrangement, budget off, routes compared point by point.

| field | orders | same | changed | of those, dearer | extra points | walking | worst one route |
|---|---:|---:|---:|---:|---:|---:|---:|
| crucible | 80 | 54 | **26** | 21 of 26 | +68 | **+1,2%** | **+17,7%** |
| broken country | 80 | 59 | **21** | 14 of 21 | +79 | +0,3% | +12,7% |
| great field | 40 | 29 | **11** | 7 of 11 | +9 | +0,7% | +14,2% |
| long march | 80 | 74 | 6 | 0 of 6 | +10 | −0,1% | 0,0% |
| sideways mile | 40 | 31 | **9** | 5 of 9 | +16 | **+3,2%** | **+160,1%** |

**A third of every route on the crowded fields changes, and four in five of the
changed ones are dearer to walk.** One route on Sideways Mile costs 2,6 times
what it did. That is what the furthest-first scan was buying and nobody had
priced: the wobble it removes is real ground.

**What it saves: nothing measurable.** The casts fall to 34–40% exactly as
predicted, and the clock does not move.

| field | furthest first | outward | casts |
|---|---:|---:|---:|
| crucible | 0,425 ms/order | 0,419 | 40,1% |
| broken country | 0,501 | 0,491 | 35,4% |
| great field | 0,346 | 0,306 | 34,0% |

**[M120]'s 7–12% is wrong and is corrected here.** It was derived from a share of
clearance *checks*, and this pass's checks are the cheap kind: long casts down
open ground where the near query hands back nothing to sweep. **A count of checks
is not a cost.** That is the same mistake as [M114c]'s inclusive column read as a
self time, in a different disguise, and it is the third time this session that a
proxy has been mistaken for the clock.

**An unwarmed first row nearly hid it.** The first pass reported furthest-first
on the crucible at 1,800 ms/order against 0,441 — a fourfold win that was the
JIT, not the loop. Warming both arms before the table removed it entirely. [W12]
again, and the table now warms four times, alternating arms.

**Left off.** A third of the routes, +1,2% of every regiment's march and one
route at 2,6×, for a saving inside the noise. Kept as a lever because the
measurement is worth more than the code, and because an arrangement that made
these casts dear would change the answer.

### M122 — the cap at five, which already bound

**The designer:** *"implement the 5ms gate we discussed"*. Two things were done:
the budget is now polled from *inside* the two searches that can overrun
(`Marching.StopSearchingWhenOutOfTime`), and the host's setting drops from ten to
**five**. The measurement says the first was unnecessary.

| cap | worst ms | ms/order | over budget | pressed | won't walk | corner walk gave up | smoothing gave up |
|---|---:|---:|---:|---:|---:|---:|---:|
| off | 6–18 | 0,50–0,61 | 0 | 0 | 0 | — | — |
| 10 ms | 5,7–6,3 | 0,42–0,48 | 0 | 0 | 0 | 0 | 0 |
| **5 ms** | **5,0** | 0,41–0,46 | 4 | **1** | **1** | **0** | **0** |
| 2 ms | 3,4–3,5 | 0,36–0,38 | 16 | 4 | 4 | 0 | 0–90 |
| 1 ms | 2,7–3,1 | 0,31–0,32 | 27–34 | 4–6 | 4–6 | 0 | 201–1 863 |
| 0,5 ms | 1,1–1,2 | 0,24–0,25 | 90–124 | 9–11 | 8–9 | 0–1 | 2 472–6 954 |

**At five the cap binds on its own.** Worst order 5,0 ms on both crowded fields,
with the inner polls off and with them on — identical, and both give-up counters
at zero. [M114]'s stage gates are enough at this setting; the "about twice the
cap" they were measured to leave is a property of a slower run, and the uncapped
tail here swings between 6 and 18 ms across repeats, which is the same [W12]
factor of two by itself.

**The polls are live, not dead** — at a tenth of a millisecond the corner walk
gives up 137 times a field and straightening thousands — so the zeros above are
real zeros and not an unreachable gate ([W9]). They are kept on, because Mono is
where the cap actually bites: a played session measured its dearest order at
287 ms, and Mono at five behaves like this bench at one or two, where they fire.

**What five costs is one route in eighty**, and the price is not linear: one
press-through and one route the executor refuses at 5 ms, four and four at 2 ms,
nine and nine at half a millisecond. **The four-in-eighty row is the better guide
to what a player sees than the one-in-eighty row measured here**, and a route the
executor refuses is the "gets stuck" report ([M30]).

**Nothing that caches, stamps or half-raises a field is polled.** Both polled
places abandon nothing: the corner walk's predecessor chain holds only legs
`IsClearLeg` has already passed, and straightening keeps every point it has not
reached — which is a route that already passed the gate. A field left half raised
would be stamped current and wrong, which is the [M104] bug class.
