# Opening the Unity harness

## First time

1. Open **Unity Hub** → *Add* → *Add project from disk* → `E:\UnityGame\unity\BattleChess`
2. Open it with **6000.5.6f1**. The first import takes a few minutes.
3. Menu: **Battle Chess → Open Battlefield Scene** (or `Ctrl+Shift+B`)
4. Press **Play**.

The scene is generated rather than checked in — Unity scene files merge badly and this one holds only a camera and one component, so it is cheaper to rebuild than to version. **Battle Chess → Rebuild Battlefield Scene** recreates it at any time.

## Controls

| | |
|---|---|
| **Left click** a regiment | Select it |
| **Left click** the ground | Plan a route for the selected regiment |
| **Right / middle drag** | Pan |
| **Scroll** | Zoom toward the cursor |
| **WASD** | Pan |

## What you are looking at

Units are drawn as **the ground they actually occupy**, not as tokens — a regiment is 80–110 m of frontage against 25 m terrain cells. The white bar marks the front edge, which is what flanking will be judged against. Colour fades as a unit takes casualties, and its frontage genuinely narrows, because footprint is computed from current strength rather than stored.

Routes respect the selected unit's **own half-frontage as clearance**, so a scout squadron and a full battle line get different answers about whether a gap is passable.

## Changing what you see

Everything is plain text under `content/`, and edits show up on the next Play — no reimport, no rebuild.

- `content/battles/ford.battle.txt` — who is on the field and where
- `content/maps/valley.map.txt` — the battlefield itself, one character per 25 m
- `content/units.cfg` — the roster; every combat value is **per man**
- `content/terrain.cfg` — terrain speeds and effects

To load a different battle, select the **Battlefield** object in the scene and change *Battle Name*.

The same files drive the command-line harness, so the two can never disagree:

```bash
dotnet run --project core/BattleChess.Cli -- battle ford
```

## Checking the client without opening the editor

```bash
./tools/unity-check.sh
```

Run it from Git Bash (`bash tools/unity-check.sh` from PowerShell). Compiles everything under `Assets/` against the rules the way the editor would, in
seconds. Its job is the boundary: Unity is deliberately outside the rules' internals
(`InternalsVisibleTo` names the tests and nothing else), so a script that reaches for
an internal member has to fail here rather than three minutes into an import.

Everything it uses is discovered, never named — the editor from `ProjectVersion.txt`,
the compiler from that editor's own `DotNetSdk`, the reference assemblies **built by
the check** rather than found, and the sources globbed from `Assets/`. That is W9, and
it is not decoration: this check has twice passed for the wrong reason, once on a
compiler too old to parse the code and once on a rules assembly that had not been
rebuilt. It also self-tests on every run, compiling a probe that must be rejected for
reaching an internal, and reports *inconclusive* rather than passing if the main
compile already failed.

Warnings count as failures. They are at zero, and the reason the bar is worth
keeping is that the response file this check grew from muted four warning codes,
of which exactly one ever fired — so three real warnings sat in the editor
console with the only thing that could have surfaced them holding its tongue.

What it cannot see is Unity's own analyzers — the `UAC*` diagnostics, such as a
public field on a component whose type the editor cannot serialize. They live in
`Unity.Analyzers.Common.dll`, load fine under `-analyzer:`, and never fire
outside the editor's compilation pipeline. **Read the editor console after any
change that touches components or serialized fields**; nothing here covers it.

It is still an approximation. The editor compiles `Assets/Scripts` and `Assets/Editor`
as two assemblies and the rules from source via the asmdef; this compiles all of them
as one library against a netstandard build. The honest check is
`Unity.exe -batchmode -quit -projectPath unity/BattleChess`, which costs minutes and a
licence — worth running before committing anything that crosses the Unity/rules
boundary, and overkill for everything else.


## Placeholder art

Terrain is a generated texture, one pixel per cell; units are flat rectangles. This is deliberately the cheapest thing that shows the simulation honestly. Replacing it means changing `TerrainView` and `UnitView` and nothing else — no other code knows how anything is drawn.

## Notes

- **Content is found by walking up from `Assets/` to `content/`.** That works in the editor and in Play mode. A standalone build has no repository around it, so shipping will need the content copied into `StreamingAssets` — a build step for when there is something worth building.
- **The harness references `BattleChess.Rules`**, which the eventual networked client must not. That is correct for now: offline play makes this machine the host, and the host runs the rules. The split into a view that only ever sees fogged data arrives with `IMatchAuthority` in M5.
- **The core package is referenced in place** (`file:../../../packages/com.battlechess.core`), not copied. Editing a file under `packages/` recompiles in Unity and in `dotnet build` alike — one source, two toolchains.
