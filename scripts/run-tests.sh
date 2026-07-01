#!/usr/bin/env bash
# Reproduce the committed PromptEnhance test suite with NO hand-synced copy.
#
# SwarmUI's extension build model requires an extension's files to live INSIDE a SwarmUI host at
# src/Extensions/<ext>/ — src/SwarmUI.extension.props references ../../GlobalSuppressions.cs, ../../GlobalUsings.cs,
# and ../../bin/live_release/SwarmUI.dll relative to the extension project. There is therefore no in-place standalone
# build; the extension must live inside a host. The canonical, copy-free reproduction is a single git checkout of THIS
# repo living at <host>/src/Extensions/PromptEnhance — the working tree IS the build tree (no separate copy to keep in
# sync). (A symlink of an out-of-host working tree does NOT work: if the host is reachable from the repo it creates an
# MSBuild glob cycle.)
#
# Usage:
#   SWARMUI_ROOT=/path/to/SwarmUI ./scripts/run-tests.sh
# Get a host with:  git clone https://github.com/mcmonkeyprojects/SwarmUI
#
# Runs both gates: the C# suite (dotnet test against the real host) and the frontend jsdom suite (node).
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
