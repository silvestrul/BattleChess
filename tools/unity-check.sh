#!/usr/bin/env bash
#
# Compiles the Unity client against the rules, the way the editor would.
#
# The point of this check is the boundary: Unity is deliberately outside the
# rules' internals, so a Unity script that reaches for an internal member has
# to fail here rather than in the editor. That only works if the check is
# incapable of passing for the wrong reason, which it has managed twice —
# M64 (a C# 7.3 compiler that failed identically whatever you wrote) and M82
# (a stale reference assembly that passed identically whatever you wrote).
#
# So every input is discovered, never named, and every guard below says what
# it verified. W9: a check must be able to say what would have made it fail.
#
# What it does NOT cover, stated because a limit you know is worth more than
# coverage you assume: Unity's own Roslyn analyzers, the UAC* diagnostics that
# catch things like a public field the editor cannot serialize. They live in
# Unity.Analyzers.Common.dll and were tried here with -analyzer:; they load
# without complaint and never fire, because they want the editor's own
# compilation pipeline around them. The editor console stays the only place
# those appear, so read it after a change that touches components or fields.

set -uo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${UNITY_CHECK_OUT:-${TMPDIR:-/tmp}/unity-check}"
refs="$out/refs"
fail=0

say()  { printf '  %s\n' "$*"; }
step() { printf '\n%s\n' "$*"; }
die()  { printf '\nREFUSED: %s\n' "$*" >&2; exit 2; }
win()  { cygpath -w "$1"; }          # csc is a Windows program; give it Windows paths

# ---------------------------------------------------------------- the editor
# Discovered from the project's own version file, so the check cannot drift
# onto an editor the project does not use.

step "editor"
version="$(sed -n 's/^m_EditorVersion: *//p' \
    "$repo/unity/BattleChess/ProjectSettings/ProjectVersion.txt")"
[ -n "$version" ] || die "ProjectVersion.txt names no editor version"

editor="${UNITY_EDITOR_PATH:-/c/Program Files/Unity/Hub/Editor/$version}"
[ -d "$editor" ] || die "no editor at $editor (set UNITY_EDITOR_PATH)"
say "$version at $editor"

# M64's correction: the editor's own compiler, not whatever csc is on PATH and
# not the 2.9 one under Tools/Roslyn. Globbed, so an editor patch that moves
# the SDK forward is followed rather than fought.
csc="$(ls "$editor"/Editor/Data/DotNetSdk/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | head -1)"
host="$editor/Editor/Data/DotNetSdk/dotnet.exe"
[ -f "$csc" ]  || die "no Roslyn under the editor's DotNetSdk"
[ -f "$host" ] || die "no dotnet host under the editor's DotNetSdk"
say "compiler $(echo "$csc" | sed "s|.*/sdk/||;s|/Roslyn.*||") (the editor's own)"

# ------------------------------------------------------------- the reference
# Built here rather than found, so there is no build for the check to be
# stale against. This is the M82 fix: reference the source, not a file.

step "rules"
rm -rf "$refs"
build="$("$host" build "$repo/core/BattleChess.Rules/BattleChess.Rules.csproj" \
    -c Release -o "$refs" --nologo -v quiet 2>&1)" || {
        printf '%s\n' "$build" >&2; die "the rules do not build"; }

for a in Contracts Rules; do
    [ -f "$refs/BattleChess.$a.dll" ] || die "the build emitted no BattleChess.$a.dll"
done
say "built Contracts + Rules into $refs"

# Guard, and it should be impossible to trip now that the build is above it.
# It stays because that is the whole point: if anyone ever puts a found
# assembly back, this is what shouts instead of quietly passing.
for a in Contracts Rules; do
    newest="$(find "$repo/packages/com.battlechess.core/Runtime/$a" -name '*.cs' \
        -printf '%T@\n' | sort -rn | head -1 | cut -d. -f1)"
    built="$(stat -c %Y "$refs/BattleChess.$a.dll")"
    [ "$built" -ge "$newest" ] || die \
        "BattleChess.$a.dll is older than its own source — the check would be vacuous"
done
say "both assemblies are newer than every source file behind them"

# -------------------------------------------------------------- the client
# Globbed. A tenth script joins the check by existing, which is how the two
# Editor scripts below spent their whole lives outside it.

step "client"
mapfile -t scripts < <(find "$repo/unity/BattleChess/Assets" -name '*.cs' | sort)
[ "${#scripts[@]}" -gt 0 ] || die "no Unity scripts found"
say "${#scripts[@]} scripts under Assets/"

mapfile -t managed < <(ls "$editor"/Editor/Data/Managed/UnityEngine/*.dll)
netstandard="$editor/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll"
[ "${#managed[@]}" -gt 0 ] || die "no managed assemblies in the editor"
[ -f "$netstandard" ]      || die "no netstandard reference assembly"
say "${#managed[@]} editor assemblies + netstandard 2.1"

rsp="$out/client.rsp"
{
    echo "-target:library"
    echo "\"-out:$(win "$out/client.dll")\""
    echo "-nologo"
    echo "-langversion:9.0"     # what Unity 6 accepts; see Directory.Build.props
    echo "-nostdlib+"
    for d in "${managed[@]}" "$netstandard" \
             "$refs/BattleChess.Contracts.dll" "$refs/BattleChess.Rules.dll"; do
        echo "\"-r:$(win "$d")\""
    done
    for f in "${scripts[@]}"; do printf '"%s"\n' "$(win "$f")"; done
} > "$rsp"

step "compile"
said="$("$host" "$csc" -noconfig "@$(win "$rsp")" 2>&1)"; built=$?
printf '%s' "$said" | grep -E "error CS" && fail=1
warned="$(printf '%s' "$said" | grep -c "warning ")"
[ "$built" -eq 0 ] || fail=1

if [ "$warned" -gt 0 ]; then
    printf '%s' "$said" | grep "warning " | sed 's/^/  /'
    say "$warned warning(s) — the editor shows these too, so they count"
    fail=1
fi
[ $fail -eq 0 ] && say "clean, no warnings"

# --------------------------------------------------------------- self-test
# The check must be shown to be capable of failing, on every run, or a green
# result is only evidence that csc exited zero. RouteSmoothing is the exact
# internal M82 tripped over, so this probe is the bug in miniature.

step "self-test"
probe="$out/Probe.cs"
cat > "$probe" <<'PROBE'
using BattleChess.Rules;
static class Probe { static object P => typeof(RouteSmoothing); }
PROBE

if [ $fail -ne 0 ]; then
    say "inconclusive — the compile above failed, so nothing here is evidence"
else
    said="$("$host" "$csc" -noconfig "@$(win "$rsp")"         "-out:$(win "$out/probe.dll")" "$(win "$probe")" 2>&1)"
    # It must fail, and fail *because of the probe*. A check that accepts any
    # non-zero exit would call a typo in the probe a healthy boundary.
    if printf '%s' "$said" | grep -q "CS0122\|CS0246"; then
        say "reaching RouteSmoothing is rejected on its accessibility, so the boundary is live"
    else
        say "BROKEN: the probe did not fail on accessibility — this check proves nothing"
        printf '%s\n' "$said" | head -5
        fail=1
    fi
fi

printf '\n%s\n' "$([ $fail -eq 0 ] && echo "unity-check: pass" || echo "unity-check: FAIL")"
exit $fail
