# Making the lattice search less — every lever, and what each costs

Reference for `HybridAStarPlanner`.

**Three of these now ship** — `StrayMultiple = 1.5`, `HeadingBins = 12`,
`PoseExpansionBudget = 20000`, chosen in [M71](DECISIONS.md#m71). The rest are
measurement levers, off by default.

**Sections 1 to 7 were all measured against the search as it was before that**:
unbounded, sixteen headings. They are kept as the record of how the choice was
made, not as advice about the current search — a lever measured against one
baseline says nothing about another, and section 8 is what happened when the
most promising of them was re-run against what now ships. Re-measure before
trusting any row here.

To try a lever, set the static named in its section and run
`core/BattleChess.Core.Tests/Battle/FewerExpansionsTests.cs` — `EveryWayToSearchLess`
for sections 1 to 7, `TheGroundBinOnEveryMap` for section 8. Both are skipped
by default because they drive global statics; unskip to re-measure.

Catalogued in [M70](DECISIONS.md#m70), chosen in [M71](DECISIONS.md#m71).

## The diagnosis these came from

The lattice bins ground at **20 m** and heading into **16**, so the Great Field
— 1800 × 2400 m — holds **172 800 states**. Instrumenting where the search
actually went:

| order | asked for | strayed sideways | strayed past its own ends | expansions |
|---|---:|---:|---:|---:|
| U19 | **282 m** | **841 m** | **666 m** | 31 640 |
| U13 | 404 m | 566 m | 412 m | 12 500 |
| U16 (cheap) | 356 m | 45 m | 4 m | 84 |

U19 searched a box roughly **1 700 m across to answer a 282 m order**. Nothing
bounded the search to the ground the order was about — and the route it returned
was the five-fold detour the [M65](DECISIONS.md#m65) ceiling then threw away.
The wandering and the silly-looking routes are one fault seen twice.

## How to read the numbers

**The metrics are deliberately not the earlier sweeps'.** Those scored a
press-through as damage, which contradicts M65 — a press is the right answer
when the way round is too dear — and counting it as damage made every
cheapening lever look worse than it was.

- **GF ms** — worst single order among the seven the recording caught costing
  22 to 889 ms. **Speed only**: every one of them presses whatever the setting,
  so a lever can look free here purely by making the search fail sooner.
- **CR / BC** — eighty one-click orders on the Crucible and Broken Country,
  where the pose search wins some, so what a cheaper search *costs* has
  somewhere to show up. **A lever is only free if it is free here.**
- **unwalk** — the only genuine failure: a route the executor refuses, which is
  a regiment that stands still.
- **pressed** — reported, not judged.
- **detour** — worst route as a multiple of walking straight there. The
  silly-route metric.

Every row below routed **80/80**.

---

## The two reference rows

| setting | GF ms | CR worst | CR total | unwalk | pressed | detour | route s | BC worst | unwalk | detour |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| nothing at all | 135,7 | 322,7 | 3047,0 | 17 | 16 | 2,65× | 33294 | 321,5 | 11 | 2,97× |
| **as it ships (cap 20k)** | 66,4 | 67,5 | 1114,2 | 17 | 16 | 2,65× | 33294 | 79,3 | 11 | 2,97× |

The shipped cap alone is **4,8× on the worst order with no quality change at
all**. Everything below is measured against the second row.

---

## 1. `StrayMultiple` — hold the search near the order

Prunes any state further from the straight line, or past either end of it, than
`multiple × the straight-line distance` (floored at `StrayFloorMetres = 60`).
`0` is unbounded, which is what it was.

| setting | GF ms | CR worst | CR total | unwalk | detour | route s | BC unwalk | BC detour |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| ×3,00 | 66,5 | 68,1 | 1097,9 | 17 | 2,65× | 33294 | 11 | 2,97× |
| ×2,00 | 68,6 | 77,8 | 1068,2 | 17 | 2,65× | 33294 | 11 | 2,97× |
| **×1,50** | **52,3** | 69,6 | **1029,8** | **17** | 2,65× | 33294 | **11** | 2,97× |
| ×1,25 | 60,2 | 73,8 | 1017,6 | 17 | 2,65× | 33294 | 11 | 2,97× |
| ×1,00 | 43,9 | 66,0 | 1017,8 | 20 | 2,34× | 32119 | 13 | 2,97× |
| ×0,75 | 27,2 | 66,2 | 961,4 | 22 | 2,02× | 31421 | 14 | 2,97× |
| ×0,50 | 17,6 | 71,7 | 910,9 | 29 | 2,02× | 27934 | 19 | 1,56× |
| ×0,35 | 9,3 | 70,3 | 753,2 | 31 | 2,02× | 27208 | 27 | 1,45× |

**Free down to ×1,25.** At ×1,00 quality starts going (17 → 20 unwalkable), and
below that it goes fast. Note it barely moves **CR worst** on its own — the
worst order runs to exhaustion, and a bound on *where* does not bound *how long*.
What it does is cut the total and make the cap cheap.

## 2. `PositionBin` — coarser ground bins

Overrides the 20 m binning. Halving the resolution quarters the state space.

| setting | GF ms | CR worst | CR total | unwalk | detour | route s | BC worst | BC unwalk |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 25 m | 58,3 | 98,7 | 1164,1 | **16** | 2,63× | 33339 | 70,4 | 11 |
| **30 m** | 40,9 | 67,0 | **855,7** | **16** | 2,71× | 33926 | 72,3 | **11** |
| 35 m | 21,4 | 71,4 | 876,6 | 24 | 2,65× | 31106 | 88,2 | 22 |
| 40 m | 15,3 | 101,5 | 857,1 | 26 | 2,31× | 30950 | 66,8 | 25 |
| 50 m | 6,6 | 68,2 | 345,2 | 34 | 2,02× | 27005 | 65,6 | 30 |
| 60 m | 4,1 | 14,4 | 209,2 | 37 | 2,02× | 26123 | 14,4 | 33 |
| 80 m | 4,2 | 12,1 | 222,0 | 37 | 2,02× | 26123 | 6,8 | 33 |

**The cliff is between 30 and 35 m** — unwalkable jumps 16 → 24 on the Crucible
and 11 → 22 on Broken Country. 30 m is the value to use; 25 m is *slower* than
20 m on the Crucible total, which is not monotonic and worth knowing before
trusting a fine-grained sweep here. 60 m and 80 m are the same answer, so the
bin has stopped meaning anything by then.

## 3. `Headings` — fewer heading bins

Overrides the 16 bins. **The most effective single lever on the real orders.**

| setting | GF ms | CR worst | CR total | unwalk | detour | route s | BC worst | BC unwalk |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 14 | **4,1** | 75,5 | 1139,4 | 17 | 2,72× | 33294 | 72,7 | 13 |
| **12** | **4,2** | 74,6 | 977,0 | **17** | 2,63× | 32937 | 73,4 | **10** |
| 10 | 5,0 | 64,7 | 829,8 | 17 | 2,73× | 33597 | 68,5 | 20 |
| 8 | 4,1 | 62,5 | 923,1 | 18 | 2,75× | 33946 | 99,2 | 12 |
| 6 | 4,1 | 66,6 | 624,8 | 26 | 2,34× | 29733 | 64,7 | 16 |

**16 → 14 takes the recorded orders from 66 ms to 4,1** — a sixteen-fold cut for
one bin. That is a large effect from a small change and deserves suspicion; the
likely cause is that 16 bins alias badly against the primitive set, so nearly
every state lands in its own bin and dominance pruning never fires.

**Caution: this column is not monotonic.** Broken Country's unwalkable goes
13 → 10 → **20** → 12 → 16 as bins fall. 12 is the best value measured, but the
spike at 10 means the mechanism is not understood, and a value should not be
picked from this table by interpolation.

## 4. `PoseExpansionBudget` — simply allow fewer expansions

The lever already shipped, at 20 000.

| setting | GF ms | CR worst | CR total | unwalk | detour | route s | BC worst | BC unwalk |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 40 000 | 84,8 | 162,4 | 1626,0 | 17 | 2,65× | 33294 | 135,6 | 11 |
| **10 000** | 40,2 | **38,2** | 839,8 | **17** | **2,65×** | **33294** | 44,0 | **11** |
| 5 000 | 23,8 | 43,5 | 704,8 | 26 | 2,47× | 29266 | 27,5 | 13 |
| 2 000 | 13,3 | 26,7 | 477,2 | 30 | 2,47× | 27940 | 15,5 | 16 |
| 1 000 | 10,6 | 14,5 | 321,8 | 31 | 2,02× | 27618 | 11,0 | 18 |
| 500 | 5,9 | 10,1 | 256,2 | 31 | 2,02× | 27618 | 8,9 | 20 |

**10 000 is byte-identical in quality to 20 000** — same unwalkable, same
detour, same 33294 route-seconds — at nearly half the worst order. The cap can
be halved for nothing. Below 5 000 it becomes the crudest lever in this
document: cap 1 000 buys 14,5 ms for **31 unwalkable against 17**.

This is the only lever that bounds **worst-case** time, because it is the only
one that stops a search that would otherwise run to exhaustion.

## 5. `Weight` — a greedier heuristic

| setting | GF ms | CR worst | CR total | unwalk | BC unwalk |
|---|---:|---:|---:|---:|---:|
| 2,5 | 71,4 | 74,0 | 1280,7 | 17 | 10 |
| 3,0 | 66,3 | 84,3 | 1251,3 | 18 | 9 |
| 4,0 | 67,5 | 82,3 | 1236,2 | 17 | 9 |
| 6,0 | 70,2 | 95,4 | 1295,5 | 18 | 10 |

**No use.** Every value is slower than shipping on the Crucible worst order.
Confirms the earlier finding at [M55](DECISIONS.md#moving) from a different
direction, now that the estimate knows what turning costs.

## 6. `SweepSpacing` — cheaper expansions rather than fewer

Widens the gap between sampled poses along a primitive. Does not reduce
expansions; reduces what each one costs.

| setting | GF ms | CR worst | CR total | unwalk | route s | BC unwalk |
|---|---:|---:|---:|---:|---:|---:|
| 3 m | 59,3 | 62,7 | 1047,7 | 17 | 33287 | 11 |
| 4 m | 54,5 | 82,2 | 1019,1 | 17 | 33257 | 11 |
| 6 m | 57,3 | 80,6 | 950,1 | 17 | 33247 | 10 |

Quality-neutral throughout but worth only 5–15%. What it spends is the margin
between the poses checked and the ones the body occupies — see the remarks on
`MaxSweepSpacingMetres`.

---

## 7. Combinations

| setting | GF ms | CR worst | CR total | unwalk | detour | route s | BC worst | BC unwalk |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| stray1,5 + h12 | 4,1 | 70,7 | 810,4 | 17 | 2,63× | 32937 | 77,9 | 10 |
| stray1,5 + h12 + bin25 | 4,1 | 74,9 | 755,7 | **16** | 2,84× | 33376 | 72,3 | **9** |
| stray1,5 + h12 + bin30 | 4,1 | 62,6 | 663,6 | 17 | 2,76× | 33446 | 69,5 | 13 |
| stray1 + h12 + bin30 | 5,0 | 62,4 | 541,0 | 19 | 2,61× | 32289 | 89,7 | 14 |
| **stray1,5 + h12 + cap10k** | **4,0** | **37,4** | **629,6** | **17** | **2,63×** | **32937** | 45,0 | **10** |
| stray1,5 + h12 + sweep4 | 4,3 | 73,7 | 766,9 | 17 | 2,63× | 32959 | 62,1 | 10 |

### Chosen, and now shipping — [M71](DECISIONS.md#m71)

`StrayMultiple = 1.5`, `HeadingBins = 12`, `PoseExpansionBudget = 20000`.

**The rows above were all measured against the unbounded 16-heading search.**
That is no longer the baseline, so a promising row there has to be re-measured
against what ships before it means anything — see section 8, where doing exactly
that reversed the ground-bin result.

Against **nothing at all**: the recorded orders go **135,7 → 4,0 ms** (34×), the
Crucible's worst order **322,7 → 37,4** (8,6×), its total **3047 → 630** (4,8×)
— while unwalkable stays **17**, the worst detour improves slightly (2,65 →
2,63×), route cost improves slightly (33294 → 32937 s), and Broken Country's
unwalkable improves (11 → **10**).

It is **equal or better on every quality metric measured**, which is the only
reason to prefer it over the cheaper-looking rows.

---

## 8. The ground bin, re-measured on every map against what now ships

Section 2 was taken against the old default. Re-run on all four maps on top of
`stray 1,5 + 12 headings + cap 20k`:

| map | bin | worst ms | total ms | unwalk | detour | route s |
|---|---|---:|---:|---:|---:|---:|
| Crucible | **20 (ships)** | 77,9 | 994,4 | **17** | 2,63× | 32937 |
| Crucible | 30 m | 65,3 | 641,2 | **17** | 2,76× | 33446 |
| Crucible | 40 m | 62,7 | 429,3 | 31 | 2,41× | 29073 |
| Crucible | 50 m | 61,0 | 387,2 | 32 | 2,61× | 28730 |
| Crucible | 60 m | 41,1 | 252,8 | 35 | 2,02× | 27097 |
| Broken Country | **20 (ships)** | 70,5 | 515,1 | **10** | 2,95× | 43250 |
| Broken Country | 30 m | 73,8 | 538,9 | 13 | 2,67× | 44069 |
| Broken Country | 40 m | 66,4 | 279,1 | 29 | 2,21× | 37095 |
| Broken Country | 50 m | 68,2 | 291,4 | 25 | 2,07× | 39298 |
| Broken Country | 60 m | 74,7 | 329,9 | 30 | 1,80× | 36343 |
| Long March | **20 (ships)** | 50,1 | 193,5 | **1** | 2,79× | 62112 |
| Long March | 30 m | 25,1 | 69,1 | 4 | 2,89× | 61065 |
| Long March | 40 m | 11,1 | 54,3 | 3 | 2,60× | 61555 |
| Long March | 50 m | 6,9 | 43,4 | 5 | 2,60× | 60621 |
| Long March | 60 m | 11,7 | 45,5 | 6 | 2,60× | 60374 |

And the map the game is actually played on:

| map | bin | worst ms | total ms |
|---|---|---:|---:|
| Great Field | 20 (ships) | 4,1 | 16,9 |
| Great Field | 30 m | 4,1 | 16,6 |
| Great Field | 40 m | 4,4 | 17,2 |
| Great Field | 50 m | 4,0 | 16,7 |
| Great Field | 60 m | 4,0 | 17,3 |

**The bin stays at 20 m.** Three reasons, in order of weight:

1. **On the Great Field it now changes nothing** — 4,0 to 4,4 ms at every value.
   The other three levers already took that map to four milliseconds and there
   is nothing left for the bin to buy on the map you play.
2. **30 m is not free after all.** Section 2 said 16 unwalkable against 17;
   against the new baseline the Crucible holds at 17 but Broken Country goes
   **10 → 13** and the Long March **1 → 4**.
3. **40 m and coarser is bad everywhere** — 31, 29 and 3 unwalkable, and it does
   not even buy worst-case time: Broken Country's worst order is 70,5 ms at 20 m
   and 74,7 at 60 m.

The one thing it does buy is **total** time on the Long March (193,5 → 43,4 ms
at 50 m), which is the field where almost nothing reaches the lattice anyway.

## What none of these fix

**The worst detour never falls below about 2,6× on the Crucible or 2,9× on
Broken Country** at any setting that keeps quality. That number is the M65
ceiling doing its job, not the search improving — so the L-shaped and
right-angled routes reported in play are **not addressed by any lever in this
document**. They need their own pass.
