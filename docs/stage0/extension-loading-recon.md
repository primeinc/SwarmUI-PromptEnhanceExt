# Stage 0.1 — Extension-loading recon (SwarmUI at pin)

Pin evidence: `git -C vendor/SwarmUI rev-parse HEAD` → `9c81c1cbcb5f256508e186fd3b4faa873c139b7d`, equal to
`swarmui_pin` (`justfile:15`). All `path:line` citations below are relative to the SwarmUI repo root at that ref.

Scope: recon only — how extension folders are discovered, compiled, loaded, initialized, and what
cross-extension visibility exists. No design, no code changes.

## Read ledger

| File | Read |
|---|---|
| `src/Core/ExtensionsManager.cs` (383 lines) | FULL |
| `src/Core/Extension.cs` (127) | FULL |
| `src/Core/Program.cs` (918) | FULL |
| `src/SwarmUI.csproj` (26) | FULL |
| `src/SwarmUI.extension.props` (20) | FULL |
| `src/SwarmUI.deps.props` (13) | FULL |
| `src/GlobalUsings.cs` (7) | FULL |
| `docs/Making Extensions.md` (180) | FULL |
| `launchtools/linux-build-logic.sh` (52) | FULL |
| `src/Utils/Utilities.cs` | PARTIAL (`:1227-1292`, RunGitProcess) |
| `src/BuiltinExtensions/DynamicThresholding/DynamicThresholdingExtension.cs` | PARTIAL (namespace line 8 only, as a namespace-prefix control) |

Claim labels: constraints below are **Direct evidence** unless marked otherwise.

## Constraints — discovery

1. Built-in extensions are discovered by scanning already-loaded assemblies for non-abstract `Extension` subclasses at boot — `src/Core/ExtensionsManager.cs:119-124`.
2. Built-ins compile into `SwarmUI.dll` itself: the host csproj excludes only `Extensions/**` from compile (`src/SwarmUI.csproj:20`), and `src/BuiltinExtensions/` contains no csproj — VALIDATED_EMPTY (`rg --files -g "*.csproj"` → 0 hits, exit 1; positive control `-g "*.cs"` same scope → 19 files).
3. Core-vs-external is decided by namespace prefix `SwarmUI.` — `src/Core/ExtensionsManager.cs:121`; external extensions are forbidden from using it (`docs/Making Extensions.md:125`, control: `src/BuiltinExtensions/DynamicThresholding/DynamicThresholdingExtension.cs:8` uses `SwarmUI.Builtin_DynamicThresholding`).
4. External extensions are subdirectories of `src/Extensions/` — `src/Core/ExtensionsManager.cs:103`; loose `.cs` files directly in `src/Extensions/` are invalid — `src/Core/ExtensionsManager.cs:280-283`.
5. Folder-name suffixes are control signals: `.delete` folders are deleted at boot, `.disable` folders are skipped — `src/Core/ExtensionsManager.cs:104-118`.
6. `ServerSettings.DisabledExtensions` filters extension folders by name before build — `src/Core/ExtensionsManager.cs:106-107`, `:334-337`.
7. An extension's root class must live in `{ClassName}.cs` inside its folder; path resolution searches candidate folders for exactly that file — `src/Core/ExtensionsManager.cs:257-268`, `:294`.
8. Two extensions with the same class name → `CriticalLoadError` ("Something will break") — `src/Core/ExtensionsManager.cs:261-264`.

## Constraints — compilation model

9. Each external extension is compiled at host boot by a `dotnet build` subprocess (`Debug` when `IsDevMode`, else `Release`) into `./src/bin/extensions/{SwarmExtension{Folder}}/` — `src/Core/ExtensionsManager.cs:205-239` (build invocation `:227-228`).
10. Project selection: first `*.csproj` in the folder root, names containing "extension" preferred — `src/Core/ExtensionsManager.cs:131`; documented rule `docs/Making Extensions.md:39`.
11. Missing csproj → an auto-generated `SwarmAutoGenExtensionProjectFile.csproj` (the `ReferenceCsproj` template importing `../../SwarmUI.extension.props`) is written, built, then deleted — `src/Core/ExtensionsManager.cs:83-91`, `:132-137`, `:169-176`.
12. The output assembly name embeds an 8-char git hash of the extension folder: `SwarmExtension{Folder}-{hash}.dll` — assembly identity changes on every extension commit — `src/Core/ExtensionsManager.cs:208-211`, `:227`. `"unknown"` fires only when the combined git output is shorter than 8 chars; a non-git folder actually yields the first 8 chars of git's error text (e.g. `fatal: n`, containing a Windows-invalid `:` for filenames) because `RunGitProcess` returns stdout+stderr on failure — `src/Core/ExtensionsManager.cs:209-210`, `src/Utils/Utilities.cs:1249-1256`. Candidate upstream bug.
13. Rebuild is skipped when the hash-named target exists and the host is not in dev mode — `src/Core/ExtensionsManager.cs:221-225`; the whole `src/bin/extensions` cache is wiped when the host itself rebuilds — `launchtools/linux-build-logic.sh:31`.
14. `bin`/`obj` inside the extension source folder are force-deleted before build — `src/Core/ExtensionsManager.cs:212-220`.
15. Extension builds run sequentially (`await` inside the enumeration loop, with a TODO about file-lock breakage) — `src/Core/ExtensionsManager.cs:138-143`.
16. Extensions compile against the pre-built host binary `../../bin/live_release/SwarmUI.dll` with `Private=false` (not copied) — `src/SwarmUI.extension.props:10-13`; `live_release` is produced by `dotnet build src/SwarmUI.csproj -c Release` — `launchtools/linux-build-logic.sh:36-37`.
17. The extension build graph is exactly: SwarmUI.dll reference + the nine shared packages (`src/SwarmUI.deps.props:3-11`, imported at `src/SwarmUI.extension.props:19`) + `GlobalSuppressions.cs`/`GlobalUsings.cs` (`src/SwarmUI.extension.props:16-17`). Nothing in the props references another extension's output (full read of the 20-line file; absence within a fully-read file).
18. `CopyLocalLockFileAssemblies=false` — NuGet dependency DLLs are **not** copied into the build output — `src/SwarmUI.extension.props:7`; props values are explicitly overridable per extension — `docs/Making Extensions.md:36`.

## Constraints — assembly loading

19. Each external extension loads into its own named, non-collectible `AssemblyLoadContext` (`SwarmExtensionLoadContext`) — `src/Core/ExtensionsManager.cs:27`, `:78-81`.
20. Dependency resolution: host Default ALC wins; only on `FileNotFoundException` does the ALC probe the extension's *own build-output directory* for `{Name}.dll` — `src/Core/ExtensionsManager.cs:30-50` (probe dir is `Path.GetDirectoryName(targetPath)`, `:80`).
21. An extension shipping a DLL the host can resolve gets the host copy; the warning fires only when the file exists in the extension's output AND its name is outside the `CoreDependencyNames` snapshot — in the common case the substitution is silent — `src/Core/ExtensionsManager.cs:37-40`; `CoreDependencyNames` = Default-ALC assembly names + `AppContext.BaseDirectory` DLLs — `:96-100`.
22. The ALC never probes other extensions' folders or delegates to sibling ALCs — the fully-read `Load` override has exactly two resolution paths: Default ALC, then own `extensionDir` — `src/Core/ExtensionsManager.cs:30-50`.

## Constraints — initialization order

23. Boot order in `Main`: `LoadSettingsFile` (`src/Core/Program.cs:144`) → `PrepExtensions` (`:203`; internally runs `OnFirstInit` then `PopulateMetadata` — `src/Core/ExtensionsManager.cs:177-178`) → `OnPreInit` (`Program.cs:308`) → param/backend/web prep (`:311-322`) → `OnInit` (`:323`) → model lists, backends load, API + web prep (`:330-348`) → `OnPreLaunch` (`:350`) → `Web.Launch()` (`:354`); `OnShutdown` runs during shutdown (`:581`) and the list is then cleared (`:582`).
24. Doc/code divergence note: `src/Core/Extension.cs:53` documents `OnFirstInit` as "before settings or anything else is loaded", but the settings file is loaded at `Program.cs:144`, before `PrepExtensions` at `:203`. (Recorded here only; this recon changes nothing.)
25. Within every lifecycle event, extensions run in `Extensions`-list insertion order: all built-ins first (scanned from loaded assemblies, `src/Core/ExtensionsManager.cs:119-124`), then external extensions in `Directory.EnumerateDirectories("./src/Extensions/")` order — `:103`, `:127`, `:150-168`, insertion at `:248`, iteration at `:305-319`.
26. `Extension` declares no dependency or ordering surface — full read of `src/Core/Extension.cs` (127 lines): fields are metadata, asset lists (`ScriptFiles`/`StyleSheetFiles`/`OtherAssets`, `:43-51`), and the lifecycle virtuals; nothing else.
27. A lifecycle handler that throws is caught and logged; the event continues for remaining extensions — `src/Core/ExtensionsManager.cs:305-319`.
28. `OnInit` is the documented registration point ("ideal place for registering backends, features, etc.") — `src/Core/Extension.cs:63`.

## Constraints — cross-extension visibility today

29. `GetExtension<T>()` returns another loaded extension's instance, but requires compile-time access to `T` — `src/Core/ExtensionsManager.cs:321-325`.
30. Extension A cannot use extension B's types today: (a) the build graph provides no reference path to a sibling's output (constraint 17); (b) at runtime A's ALC resolves only via host-then-own-folder (constraints 20, 22), so `SwarmExtensionB-{hash}` is unresolvable from A; (c) hand-copying B's DLL into A's output folder loads a *second copy* inside A's ALC via the private-dep path (`src/Core/ExtensionsManager.cs:44-49`). Legs (a)-(c) are Direct evidence; that the second copy has distinct type identity from B's own loaded copy is **Weak inference** (standard .NET AssemblyLoadContext semantics; not demonstrated in-repo).
31. Load/registration order between two external extensions is filesystem enumeration order with no declaration mechanism (constraints 25, 26); a "provider" extension has no guarantee of initializing before a "consumer" within the same event.
32. Extension web assets are served from the extension's source folder via `ExtensionFile/{ExtName}/...` — `src/Core/Extension.cs:49-51`; script/style registration must happen by `OnInit` — `:43-47`.
33. Extension standard 4 prohibits hacking core to work around limitations; the sanctioned path is an upstream PR — `docs/Making Extensions.md:96-99`.
34. CI can clone and boot-test listed extensions: `--ci_test_extensions` (`src/Core/Program.cs:796-797`) clones `extension_list.fds` entries flagged `ci-test` — `src/Core/ExtensionsManager.cs:193-196`.

## Implications for an extension→extension dependency seam

Facts-derived constraints only; no design.

- Assembly identity of an extension is unstable across commits (hash-suffixed name, constraint 12) — any seam that binds to an extension's assembly name breaks on the provider's next commit.
- There is no compile-time path from one extension to another (17), and no runtime resolution path either (20, 22): a compile-time reference wired by hand would fail to resolve at runtime or, via a copied DLL, split type identity (30).
- The only assembly today whose types are shared across all extensions with a single identity is `SwarmUI.dll` in the Default ALC (20, 21) — a cross-extension contract type can only unify if it lives where the Default ALC resolves it.
- A shared-contract assembly shipped as a *private dep* by multiple extensions does not unify: each consumer's ALC loads its own copy (20, 30).
- Initialization order between external extensions is undeclared filesystem order (25, 31): a seam requiring "provider registers before consumer's `OnInit`" (28) cannot be guaranteed by the current loader.
- Lifecycle errors are isolated and swallowed per-extension (27): a consumer cannot rely on the host to fail-fast when its provider failed; discovery of a missing provider is only by probing (e.g., `GetExtension<T>()` null — which itself needs `T`, 29).
- Extra NuGet dependencies require the extension to override `CopyLocalLockFileAssemblies` (or otherwise place DLLs in its build output) because the ALC probes only that folder (18, 20).
- Any host-level mechanism (Stage 6) necessarily lands in SwarmUI core — the Default ALC and `ExtensionsManager` are the only shared planes (20, 25) — and per extension standard 4 must be an upstream PR, not a local hack (33).
- The host boot-tests extensions in CI via `--ci_test_extensions` (34), so a two-extension composition is exercisable in the existing gate without new infrastructure.
