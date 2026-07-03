# PromptEnhance developer task runner
# Run `just` to see available recipes.

# Use PowerShell on Windows (no `sh` there); default to sh/bash elsewhere.
set windows-shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-Command"]

alias i := install
alias b := build
alias t := test
alias c := check

backend_project := "PromptEnhance.csproj"
backend_test_project := "Tests/PromptEnhance.Tests.csproj"

# Must match the SwarmUI ref pinned in .github/workflows/gates.yml — bump both together.
swarmui_pin := "9c81c1cbcb5f256508e186fd3b4faa873c139b7d"
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

# Run frontend tests (jsdom)
frontend-test:
  npm run test:frontend

# Pin ./vendor/SwarmUI to the exact commit CI builds against (standalone workspace layout).
# Refuses symlinked vendor paths: older layouts linked vendor into a sibling project, and
# checkout/robocopy through the link would mutate that other repo's working tree.
[windows]
vendor-sync:
  if ((Get-Item 'vendor' -Force -ErrorAction SilentlyContinue).LinkType -or (Get-Item 'vendor/SwarmUI' -Force -ErrorAction SilentlyContinue).LinkType) { Write-Error 'vendor or vendor/SwarmUI is a symlink/junction - refusing to touch it. Delete the link and re-run.'; exit 1 }
  if (-not (Test-Path 'vendor/SwarmUI/.git')) { git init -q vendor/SwarmUI; git -C vendor/SwarmUI remote add origin {{swarmui_url}} }
  git -C vendor/SwarmUI fetch --depth 1 origin {{swarmui_pin}}
  git -C vendor/SwarmUI checkout -q --detach {{swarmui_pin}}
  git -C vendor/SwarmUI rev-parse HEAD

# Pin ./vendor/SwarmUI to the exact commit CI builds against (see the [windows] variant)
[unix]
vendor-sync:
  if [ -L vendor ] || [ -L vendor/SwarmUI ]; then echo 'vendor or vendor/SwarmUI is a symlink - refusing to touch it. Delete the link and re-run.' >&2; exit 1; fi
  if [ ! -e vendor/SwarmUI/.git ]; then git init -q vendor/SwarmUI && git -C vendor/SwarmUI remote add origin {{swarmui_url}}; fi
  git -C vendor/SwarmUI fetch --depth 1 origin {{swarmui_pin}}
  git -C vendor/SwarmUI checkout -q --detach {{swarmui_pin}}
  git -C vendor/SwarmUI rev-parse HEAD

# Make the vendored host a runnable dev install: seed Data/Settings.fds (skips the
# installer; SwarmUI docs/Troubleshooting.md documents the IsInstalled key) and copy
# this extension into the host's src/Extensions (the host only loads extensions from
# there, and deletes bin/obj inside the folder on boot — a copy, never a junction).
[windows]
vendor-dev: vendor-sync
  if (-not (Test-Path 'vendor/SwarmUI/Data')) { New-Item -ItemType Directory -Force 'vendor/SwarmUI/Data' | Out-Null }
  if (-not (Test-Path 'vendor/SwarmUI/Data/Settings.fds')) { Copy-Item 'scripts/vendor-dev-settings.fds' 'vendor/SwarmUI/Data/Settings.fds' }
  robocopy . 'vendor/SwarmUI/src/Extensions/PromptEnhance' /MIR /XD .git vendor node_modules bin obj out .vs .idea .playwright-mcp .git-recovery .copilot-tracking /NFL /NDL /NJH /NJS ; if ($LASTEXITCODE -ge 8) { exit 1 } else { exit 0 }

# Make the vendored host a runnable dev install (see the [windows] variant for why a copy)
[unix]
vendor-dev: vendor-sync
  mkdir -p vendor/SwarmUI/Data
  if [ ! -e vendor/SwarmUI/Data/Settings.fds ]; then cp scripts/vendor-dev-settings.fds vendor/SwarmUI/Data/Settings.fds; fi
  rsync -a --delete --exclude .git --exclude vendor --exclude node_modules --exclude bin --exclude obj --exclude out --exclude .vs --exclude .idea --exclude .playwright-mcp --exclude .git-recovery --exclude .copilot-tracking ./ vendor/SwarmUI/src/Extensions/PromptEnhance/

# Live host boot gate: SwarmUI's own CI test mode (--ci_test) boots the real host,
# dotnet-builds and loads this extension through the real lifecycle, self-shuts after
# ~3s, and exits nonzero if anything logged an error. Port 7899 avoids a running Swarm.
[windows]
vendor-ci-test port='7899': vendor-dev
  dotnet build vendor/SwarmUI/src/SwarmUI.csproj --configuration Debug -o vendor/SwarmUI/src/bin/live_release
  Push-Location vendor/SwarmUI; dotnet src/bin/live_release/SwarmUI.dll --environment dev --ci_test true --launch_mode none --port {{port}}; $code = $LASTEXITCODE; Pop-Location; exit $code

# Live host boot gate (see the [windows] variant for details)
[unix]
vendor-ci-test port='7899': vendor-dev
  dotnet build vendor/SwarmUI/src/SwarmUI.csproj --configuration Debug -o vendor/SwarmUI/src/bin/live_release
  cd vendor/SwarmUI && dotnet src/bin/live_release/SwarmUI.dll --environment dev --ci_test true --launch_mode none --port {{port}}

# Build extension C# project
backend-build:
  dotnet build {{backend_project}} -v minimal --nologo

# Run backend C# tests
backend-test:
  dotnet test {{backend_test_project}} -c Debug -v minimal --nologo

# Build everything
build: frontend-build backend-build

# Run all tests
test: frontend-test backend-test

# Validation gate used before commit
check: frontend-parity test

# Full setup + validation for a fresh clone
dev: install check

# Clean .NET and frontend test output
clean:
  dotnet clean {{backend_project}} -v minimal --nologo
  dotnet clean {{backend_test_project}} -v minimal --nologo
  node -e "const fs=require('node:fs'); fs.rmSync('Tests/frontend/out', {recursive:true, force:true});"

# Show tool versions
versions:
  node --version
  npm --version
  dotnet --version
