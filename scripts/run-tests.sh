#!/usr/bin/env bash
# Canonical reproduction of every committed validation gate.
#
# SwarmUI's extension.props hard-requires the extension to live inside a host
# checkout at src/Extensions/PromptEnhance (it references ../../GlobalUsings.cs
# and ../../bin/live_release/SwarmUI.dll), so the working tree IS the build
# tree: one git checkout placed in the host, no copy step.
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

echo "[run-tests] Frontend build parity (Frontend/*.ts is authoritative; committed Assets/*.js must be its exact tsc output)…"
( cd "$DEST" && npm ci && npm run check:frontend-parity )

echo "[run-tests] Frontend suite (compiled TypeScript tests against the emitted Assets/*.js, real jsdom)…"
( cd "$DEST" && npm run test:frontend )

echo "[run-tests] C# suite (dotnet test against the real SwarmUI host)…"
dotnet test "$DEST/Tests/PromptEnhance.Tests.csproj" -c Debug

echo "[run-tests] OK — all gates passed."
