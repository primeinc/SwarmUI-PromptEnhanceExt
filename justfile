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
