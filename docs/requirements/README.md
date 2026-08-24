# Requirements

What Battle Chess must do, as settled by the designer. One file per requirement
type. **If the code and these files disagree, the code is wrong.**

| Type | File | Covers |
|---|---|---|
| `S` | [shape.md](shape.md) | What a regiment is, geometrically |
| `M` | [movement.md](movement.md) | Getting from here to there |
| `C` | [combat.md](combat.md) | Arranging and resolving a fight |
| `O` | [orders.md](orders.md) | What an order means once given |
| `GUI` | [interface.md](interface.md) | Mouse, selection, deployment |
| `B` | [balance.md](balance.md) | Numbers still to settle |

**Priority.** *Mandatory* — must hold. *Preferred* — the rule is right but the
number, threshold or exact form is the designer's to move. `Priority N` — the
order in which alternatives are tried.

**Status.** ✅ in the code · ⚠️ partly · ❌ not yet · 🔜 next · ❓ to settle.

## Where the reasoning lives

These files state *what*, not *why*. The evidence — what was measured, what was
tried and rejected, what an earlier form of a rule got wrong — stays in
[../DECISIONS.md](../DECISIONS.md), which is the permanent record and is never
pruned. Working rules (`W`) and technique notes (`T`) also stay there; they
govern how the work is done rather than what the game does.
