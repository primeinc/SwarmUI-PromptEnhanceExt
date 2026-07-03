# PromptEnhance — agent instructions

SwarmUI extension: C# backend (`WebAPI/`) + TypeScript frontend. `Frontend/*.ts` is
authoritative; the committed `Assets/*.js` SwarmUI serves is exact tsc output — never
hand-edit it, rebuild with `npm run build:frontend`.

## Gates (all must pass; run from repo root)

- `npm run check:frontend-parity` — Assets/*.js must be the exact tsc output of Frontend/*.ts
- `npm run test:frontend` — jsdom suite against the emitted Assets/*.js
- `dotnet test Tests/PromptEnhance.Tests.csproj -c Debug` — real SwarmUI host assemblies
- `just vendor-ci-test` — live host boot gate (SwarmUI `--ci_test`: any logged error = nonzero exit)

Shortcuts: `just check` (parity + both suites), `just dev` (fresh clone setup).

## Ground truth discipline

- Upstream SwarmUI is the anchor: `../refs/mcmonkeyprojects/SwarmUI`, pinned to the ref in
  `.github/workflows/gates.yml` (mirrored by `swarmui_pin` in the `justfile` — bump together).
- Before writing code that touches any SwarmUI API, type, or DOM surface, read the
  maintainer's source at that pin and cite `path:line` (JiT discipline — `/jit`). Never code
  SwarmUI behavior from memory or training data.
- Absence claims ("repo has no X", "upstream has no Y") require a same-tool/same-scope
  positive control first (`/search` discipline): `VALIDATED_EMPTY` or it is unknown.
- Search bounded paths only. `../refs` as a whole is ~1000 repos — never search its root,
  and never hand subagents a search surface wider than one repo.
- `.copilot-tracking/swarmui-divergence-journal.md` is the doc↔code alignment ledger; every
  row cites `path:line` at the pin.

## Pin bump procedure

1. Update the ref in `.github/workflows/gates.yml` AND `swarmui_pin` in `justfile` together.
2. Re-diff the vendored PropertyGroup in `PromptEnhance.csproj` against
   `src/SwarmUI.extension.props` at the new pin (it must stay a verbatim mirror).
3. `just vendor-sync`, then `just check`, then `just vendor-ci-test`.
4. Re-verify the divergence journal rows at the new pin.

## Environment notes (Windows)

- Concurrent bash spawns can crash Git Bash (cygwin `add_item` fatal error, exit 5):
  serialize shell calls, avoid `cd`-chained compounds, retry once on exit 5.
- `vendor/` is a project-local, gitignored SwarmUI checkout. Never point it at another
  project's tree. `just vendor-dev` seeds a no-backend dev install and re-syncs the
  extension copy inside the host (a copy, never a junction — the host deletes `bin`/`obj`
  in extension folders on boot).
