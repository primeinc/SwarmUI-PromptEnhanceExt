#!/usr/bin/env bash
set -euo pipefail

HOST="${SWARMUI_ROOT:?Set SWARMUI_ROOT to a SwarmUI checkout (git clone https://github.com/mcmonkeyprojects/SwarmUI)}"
if [ ! -f "$HOST/src/SwarmUI.csproj" ]; then
    echo "[run-tests] SWARMUI_ROOT ($HOST) is not a SwarmUI checkout — no src/SwarmUI.csproj found." >&2
    exit 1
fi

DEST="$HOST/src/Extensions/PromptEnhance"
if [ ! -e "$DEST" ]; then
    echo "[run-tests] Placing the extension checkout inside the host at $DEST (the checkout is the build tree)…"
    git clone https://github.com/primeinc/SwarmUI-PromptEnhanceExt.git "$DEST"
fi
if [ ! -f "$DEST/Tests/PromptEnhance.Tests.csproj" ]; then
    echo "[run-tests] $DEST is not a PromptEnhance checkout." >&2
    exit 1
fi

echo "[run-tests] C# suite (dotnet test against the real SwarmUI host)…"
dotnet test "$DEST/Tests/PromptEnhance.Tests.csproj" -c Debug

echo "[run-tests] Frontend suite (jsdom)…"
( cd "$DEST" && node Tests/frontend/promptenhance.test.js )

echo "[run-tests] OK — both gates passed."
