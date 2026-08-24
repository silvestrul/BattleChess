# Movement — `M`

How a regiment gets from where it is to where it was told to go.

Priority: *Mandatory* · *Preferred* · `Priority N` — the order rungs are tried in.

| | Requirement | Priority | Status |
|---|---|---|---|
| M1 | Friendly regiments **should not** share ground. | see Mx2c | ✅ |
| M1a | A regiment only moves when told to, unless it is retreating or has to change routes. | see Mx2c, Mx2d | ✅ |
| **Mx2** | **Pathfinding.** A regiment that cannot reach its destination as a straight line must try the following, in order. | Priority 0 | ✅ |
| Mx2a | Try to find a way around the obstacle. If that fails, try another way around it. | Priority 1 | ✅ |
| Mx2b | If there is no way around the obstacle or the cost is too big, and a friendly regiment blocks the path, that regiment moves sideways to make space before the marcher arrives. | Priority 2 | ❌ not built — no regiment moves aside |
| Mx2c | If clean movement of the blocking regiment is not possible, a press-through is initiated. | Priority 3 | ✅ last resort since M55 |
| Mx2d | If an enemy regiment blocks the path on the first or second leg, try to find a way around. | Priority 4 | ❌ enemies are not planning obstacles at any stance |
| Mx2e | If an enemy regiment cannot be safely avoided during the current turn, the regiment cancels its order and stops in place. | Priority 5 | ❌ not built |
| **Mx6** | **Cost.** One click on a selected wing plans every regiment's route without a noticeable pause — a hundred routes on any of the three fields. | Mandatory | ✅ 109 / 176 / 121 ms for a hundred, twelve cores |
| Mx6a | A batch of orders is planned in parallel; nothing in a plan writes to the battle. | Mandatory | ✅ same routes as one at a time, compared order by order |
| Mx6b | No plan allocates working memory it will drop at the end of the search. | Preferred | ✅ scratchpads borrowed, one per thread |
| M2 | Contact of 5% or less is ignored, for convenience. | Mandatory | ✅ |
| **M3** | **Attack positioning.** A move faces its line of march, at **any** distance and any angle. | Mandatory | ✅ |
| M3a | If the regiment is moving sideways, the line of march may be kept. | Mandatory | |
| M3b | Ordered to attack an enemy from the front, its final position faces the enemy on the **same bearing the enemy has**. | Mandatory | |
| M3c | Attacking from the outer angle, a **flanking** is initiated instead, and the bearing is perpendicular — the long side attacks the flank. | Mandatory | |
| M3d | Attacking from the inner angle from behind, a **rear attack** is initiated instead, and the bearing faces the enemy rear (parallel). | Mandatory | |
| M3e | If the attack position is blocked by another regiment, the attack planner resolves in this order: **1)** the regiment in front makes space so both share half frontage against the opposing regiment; **2)** if the front is blocked or there is an order, a flanking is initiated; **3)** if both flanks are blocked, the rear allows up to two regiments, as in 1; **4)** otherwise the regiment waits before its last leg. | Mandatory | |
| M4 | A drawn bearing is the front to **arrive on**. *(Definition rather than requirement, as with legs.)* | Mandatory | ✅ |
| **M5** | **Turning.** Turning on the spot is never blocked by collision. | Mandatory | ✅ |
| **M7** | **Bound armies.** Box-selecting several regiments groups them temporarily, with no button pressed. | Mandatory | ✅ |
| M7a | Bound regiments move as one, at the pace of whichever is slowest — by base speed or by ground. | Mandatory | ✅ |
| M7b | Each regiment pathfinds individually, so a wing does not walk into itself. | Mandatory | ✅ |
| **M8** | **Grouped armies.** Grouped regiments that are "glued" pathfind as one larger unit with the shared frontage. | Mandatory | |
| **M11** | **Cadence.** Routes are rebuilt on a cadence. | Mandatory | ⚠️ `RepathIntervalTicks = 5` exists and is being bypassed — finding 7 |
| M12 | The rectangle is what travels, not a point. | Mandatory | ⚠️ the cast asks the whole body; the fallback search still uses 2 m |
| **M13** | **Crabbing.** A regiment that cannot fit its full length but can fit sideways may walk sideways, at a movement-speed penalty. *(Penalty value not yet stated.)* | Mandatory | ✅ |
| **M15** | **Obstacle definition.** An obstacle is whatever will not get out of your way — impassable terrain, enemy regiments. | Mandatory | ❌ new |
| M15a | Stance decides whether an enemy is a wall. | Mandatory | ⚠️ |
| M16a | Friends are predicted from their orders; enemies only from what has been seen of them. | Mandatory | ❌ new |
| **M16b** | **Giving way.** Only one of any two regiments gives way. | Mandatory | ❌ new |
| **M19** | **Pathfinding.** Cast the whole ray, then look at what is along it. | Mandatory | ❌ new |
| M19a | Room to spare when going around. | Mandatory | ❌ new |
| **M20** | **Pressing through.** Being inside your own men costs pace — as does going behind, or lateral walking. | Mandatory | ✅ |
| M21 | A detour is committed until the thing it went round is behind you. | Mandatory | ✅ |
| M22 | A route is costed in seconds, not metres. | Mandatory | ✅ |
| M23 | A march comes round onto every leg, not once onto the whole route. | Mandatory | ✅ |
| **M25** | **Obstacles.** A body you are already standing in, by a noticeable margin, is not in your way. | Mandatory | ✅ |
| M27 | A gap between two regiments is a corridor. | Mandatory | ✅ |
| M34 | What a march costs is legs priced, and legs priced is places squared. | Mandatory | 🔜 |
| M36 | Tangency prunes legs at corners. | Mandatory | ✅ |
| M45 | A regiment's size ceiling belongs to the battle, not only to the troop type. | Mandatory | ✅ |
