# PromptEnhance developer task runner
# Run `just` to see available recipes.

[windows]
set shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-Command"]

alias i := install
alias b := build
alias t := test
alias c := check

backend_project := "PromptEnhance.csproj"
backend_test_project := "Tests/PromptEnhance.Tests.csproj"

# xunit.v3 >=3.2.0 bundles MTP telemetry; documented opt-out keeps runs deterministic.
export TESTINGPLATFORM_TELEMETRY_OPTOUT := "1"

# Must match the SwarmUI ref pinned in .github/workflows/gates.yml — PinParityTests
# (Tests/ContractParityTests.cs) fails on drift. Bump via `just vendor-bump <sha>`, never by hand.
swarmui_pin := "96a4c3d14a31776bab5e3300301e078c15875f22"
swarmui_url := "https://github.com/mcmonkeyprojects/SwarmUI"

# Install Node dev dependencies
install:
    npm ci

# Build authoritative frontend TS sources into Assets/*.js
frontend-build:
    npm run build:frontend

# Verify Frontend/*.ts -> Assets/*.js parity
frontend-parity:
    npm run check:frontend-parity

# Lint TypeScript sources and tests (Biome)
lint:
    npm run lint

# Run frontend tests (jsdom)
frontend-test:
    npm run test:frontend

# Pin ./vendor/SwarmUI to the exact commit CI builds against (standalone workspace layout). Refuses symlinked vendor paths.
[windows]
vendor-sync:
    if ((Get-Item 'vendor' -Force -ErrorAction SilentlyContinue).LinkType -or (Get-Item 'vendor/SwarmUI' -Force -ErrorAction SilentlyContinue).LinkType) { Write-Error 'vendor or vendor/SwarmUI is a symlink/junction - refusing to touch it. Delete the link and re-run.'; exit 1 }
    if (-not (Test-Path 'vendor/SwarmUI/.git')) { git init -q vendor/SwarmUI; git -C vendor/SwarmUI remote add origin {{ swarmui_url }} }
    git -C vendor/SwarmUI fetch --depth 1 origin {{ swarmui_pin }}
    git -C vendor/SwarmUI checkout -q --detach {{ swarmui_pin }}
    git -C vendor/SwarmUI rev-parse HEAD

# Pin ./vendor/SwarmUI to the exact commit CI builds against (see the [windows] variant)
[unix]
vendor-sync:
    if [ -L vendor ] || [ -L vendor/SwarmUI ]; then echo 'vendor or vendor/SwarmUI is a symlink - refusing to touch it. Delete the link and re-run.' >&2; exit 1; fi
    if [ ! -e vendor/SwarmUI/.git ]; then git init -q vendor/SwarmUI && git -C vendor/SwarmUI remote add origin {{ swarmui_url }}; fi
    git -C vendor/SwarmUI fetch --depth 1 origin {{ swarmui_pin }}
    git -C vendor/SwarmUI checkout -q --detach {{ swarmui_pin }}
    git -C vendor/SwarmUI rev-parse HEAD

# Make the vendored host a runnable dev install: seed Data/Settings.fds and copy this extension into the host's src/Extensions (a copy, never a junction).
[windows]
vendor-dev: vendor-sync
    if (-not (Test-Path 'vendor/SwarmUI/Data')) { New-Item -ItemType Directory -Force 'vendor/SwarmUI/Data' | Out-Null }
    if (-not (Test-Path 'vendor/SwarmUI/Data/Settings.fds')) { Copy-Item 'scripts/vendor-dev-settings.fds' 'vendor/SwarmUI/Data/Settings.fds' }
    robocopy . 'vendor/SwarmUI/src/Extensions/PromptEnhance' /MIR /XD .git vendor node_modules bin obj out .vs .idea .playwright-mcp .git-recovery .copilot-tracking shots test-results .claude /NFL /NDL /NJH /NJS ; if ($LASTEXITCODE -ge 8) { exit 1 } else { exit 0 }

# Make the vendored host a runnable dev install (see the [windows] variant)
[unix]
vendor-dev: vendor-sync
    mkdir -p vendor/SwarmUI/Data
    if [ ! -e vendor/SwarmUI/Data/Settings.fds ]; then cp scripts/vendor-dev-settings.fds vendor/SwarmUI/Data/Settings.fds; fi
    rsync -a --delete --exclude .git --exclude vendor --exclude node_modules --exclude bin --exclude obj --exclude out --exclude .vs --exclude .idea --exclude .playwright-mcp --exclude .git-recovery --exclude .copilot-tracking --exclude shots --exclude test-results --exclude .claude ./ vendor/SwarmUI/src/Extensions/PromptEnhance/

# Bump the SwarmUI pin in every mirror in one recipe (justfile + gates.yml), resync vendor, rerun
# the gates including the live host boot. Not transactional: a partial failure leaves the mirrors
# desynced, and PinParityTests catches that on the next run. Only checkout blocks belonging to
# repository mcmonkeyprojects/SwarmUI are rewritten; the sha is validated by just itself ([arg]
# pattern) before any shell sees it. The justfile needs just >= 1.56 (conditional `set shell`).
[arg('sha', pattern='[0-9a-f]{40}')]
[windows]
vendor-bump sha:
    [IO.File]::WriteAllText('justfile', ([IO.File]::ReadAllText('justfile') -replace 'swarmui_pin := "[0-9a-f]{40}"', 'swarmui_pin := "{{ sha }}"'), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText('.github/workflows/gates.yml', ([IO.File]::ReadAllText('.github/workflows/gates.yml') -replace '(repository: mcmonkeyprojects/SwarmUI\s*\r?\n\s*ref: )[0-9a-f]{40}', '${1}{{ sha }}'), [Text.UTF8Encoding]::new($false))
    just vendor-sync
    just backend-test
    just vendor-ci-test

# Bump the SwarmUI pin in every mirror in one recipe (see the [windows] variant)
[arg('sha', pattern='[0-9a-f]{40}')]
[unix]
vendor-bump sha:
    perl -0pi -e 's{swarmui_pin := "[0-9a-f]\{40\}"}{swarmui_pin := "{{ sha }}"}' justfile
    perl -0pi -e 's{(repository: mcmonkeyprojects/SwarmUI\s*\n\s*ref: )[0-9a-f]\{40\}}{${1}{{ sha }}}g' .github/workflows/gates.yml
    just vendor-sync
    just backend-test
    just vendor-ci-test

# Build the vendored host (with the freshly copied extension) into src/bin/live_release
vendor-host-build: vendor-dev
    dotnet build vendor/SwarmUI/src/SwarmUI.csproj --configuration Debug -o vendor/SwarmUI/src/bin/live_release

# Live host boot gate: SwarmUI's --ci_test boots the real host with this extension and exits nonzero on any logged error.
[arg('port', pattern='[0-9]{1,5}')]
[windows]
vendor-ci-test $port='7899': vendor-host-build
    Push-Location vendor/SwarmUI; dotnet src/bin/live_release/SwarmUI.dll --environment dev --ci_test true --launch_mode none --port $env:port; $code = $LASTEXITCODE; Pop-Location; exit $code

# Live host boot gate (see the [windows] variant for details)
[arg('port', pattern='[0-9]{1,5}')]
[unix]
vendor-ci-test $port='7899': vendor-host-build
    cd vendor/SwarmUI && dotnet src/bin/live_release/SwarmUI.dll --environment dev --ci_test true --launch_mode none --port "$port"

# Install the Playwright browser the UI gates drive
ui-install:
    npm run ui:install

# Browser gates, skipped when nothing they depend on changed since the last green run (screenshots/manifest.json)
[windows]
ui-test:
    node Tests/ui/readme-shots.mts current; if ($LASTEXITCODE -ne 0) { just ui-test-force; exit $LASTEXITCODE }

# Browser gates, skipped when nothing they depend on changed since the last green run (screenshots/manifest.json)
[unix]
ui-test:
    node Tests/ui/readme-shots.mts current || just ui-test-force

# Browser gates against the real vendored host, always run: rebuilds the frontend and host first so the copy served is current. Screenshots land in Tests/ui/shots; a fully green run also rewrites the README screenshots and their manifest in ./screenshots.
ui-test-force: frontend-build vendor-host-build readme-shots-clean
    npm run typecheck:ui
    npm run test:ui:update
    npm run shots:write

# Empty the per-run README screenshot folder so only the next run's shots can be committed
readme-shots-clean:
    npm run shots:clean

# Fail if ./screenshots is not what a green run of the current UI and SwarmUI pin produced
readme-shots-check:
    npm run shots:check

# Build extension C# project
backend-build:
    dotnet build {{ backend_project }} -v minimal --nologo

# Run backend C# tests (path A: VSTest-mode dotnet test).
# Platform disposition: BOTH platforms live, on xunit.v3 3.2.2 + runner 3.1.5.
#   A) VSTest via plain `dotnet test` (default while VSTest packages are referenced);
#      zero-test runs fail via Tests/.runsettings TreatNoTestsAsError.
#   B) MTP via `dotnet test -p:TestingPlatformDotnetTestSupport=true` — the property is the
#      documented .NET 8/9 SDK mechanism; applying it per-invocation (instead of hard-set in
#      the csproj as upstream's template does) is our pattern, not upstream's, kept so both
#      dotnet test modes stay live. MTP fails zero-test runs by default (exit 8).
#   C) MTP via the stand-alone test executable (`dotnet run`), enabled by
#      UseMicrosoftTestingPlatformRunner in the Tests csproj.
# Endgame, in order: when the SwarmUI host flips to net10/SDK 10 (it already warns .NET 10
# "will be required in a future version" — src/Utils/Utilities.cs CheckDotNet), switch B to
# global.json {"test":{"runner":"Microsoft.Testing.Platform"}} and retire the property; move
# to the xunit.v3 4.x/MTP-v2 line when it leaves pre-release; drop the VSTest packages only
# when every consuming runner speaks MTP (upstream guidance).
backend-test:
    dotnet test {{ backend_test_project }} -c Debug -v minimal --nologo

# Run backend C# tests (path B: MTP through dotnet test, .NET 8/9 SDK mechanism)
backend-test-mtp:
    dotnet test {{ backend_test_project }} -c Debug -v minimal --nologo -p:TestingPlatformDotnetTestSupport=true

# Run backend C# tests (path C: the stand-alone MTP executable)
backend-test-exe:
    dotnet run --project {{ backend_test_project }} -c Debug

# Build everything
build: frontend-build backend-build

# Run all tests
test: frontend-test backend-test backend-test-mtp backend-test-exe

# Validation gate used before commit
check: frontend-parity lint readme-shots-check test

# Full setup + validation for a fresh clone
dev: install check

# Clean .NET and frontend test output
clean:
    dotnet clean {{ backend_project }} -v minimal --nologo
    dotnet clean {{ backend_test_project }} -v minimal --nologo
    node -e "const fs=require('node:fs'); fs.rmSync('Tests/frontend/out', {recursive:true, force:true});"

# Show tool versions
versions:
    node --version
    npm --version
    dotnet --version
