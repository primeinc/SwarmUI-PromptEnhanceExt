# SwarmUI Docs ↔ Codebase Divergence Journal

Evidence base: a SwarmUI checkout pinned to `9c81c1cbcb5f256508e186fd3b4faa873c139b7d` —
the exact ref `.github/workflows/gates.yml` builds and `justfile` (`swarmui_pin`) vendors;
reproduce it anywhere with `just vendor-sync` (lands at `vendor/SwarmUI`). All SwarmUI
citations below are `path:line` relative to that repo's root; this-repo citations are
relative to this repo's root. Re-verify all rows when the pin is bumped.

Legend — **Status**: RESOLVED (evidence-bound, no action) · CONTRADICTED (old claim was
wrong or is now false) · NOTE (true, monitor on pin bumps).

Prior journal (2026-07-01) reviewed 2026-07-02: D1 and D4 were contradicted by the
tree as it stands; D2, D3, D5 are resolved below with citations.

---

## D1 — "Stale duplicate extension inside the host" — CONTRADICTED, then superseded
- **Old claim:** `vendor/SwarmUI/src/Extensions/PromptEnhance` is a stale independent copy.
- **Reality check (2026-07-02):** that path did not exist; `vendor` was a symlink into a
  sibling project (`../SwarmUI-GridSweep/vendor`), since replaced by a project-local clone
  pinned to the CI ref (`justfile` `vendor-sync`).
- **Superseding design:** the copy is now *managed*, not stale — `just vendor-dev` robocopy/rsyncs
  the working tree into `vendor/SwarmUI/src/Extensions/PromptEnhance` (fix option (b) from the
  old journal). A junction was rejected deliberately: the host **deletes `bin`/`obj` inside the
  extension folder on boot** (`src/Core/ExtensionsManager.cs:213-220`) and dotnet-builds the
  folder (`:228`), so a junction would let a live host mutate the real working tree.
- **Status:** RESOLVED

## D2 — csproj deviates from canonical minimal form — RESOLVED (compliant-by-mirror)
- **Doc basis:** canonical csproj is 3 lines + `<Import Project="../../SwarmUI.extension.props" />`
  (`docs/Making Extensions.md:26-37`), with the warning about messing with framework targets.
- **Reality:** in host layout this repo imports the canonical props (`PromptEnhance.csproj:11`).
  The vendored-only PropertyGroup (`PromptEnhance.csproj:19-23`) is a **verbatim mirror** of
  `src/SwarmUI.extension.props:2-8` (`TargetFramework net8.0, OutputType library,
  InvariantGlobalization true, RollForward Major, CopyLocalLockFileAssemblies false`) —
  the props file cannot be imported standalone because its `HintPath`/`Compile` entries
  (`SwarmUI.extension.props:10-17`) are host-layout-relative.
- **Status:** RESOLVED — NOTE: re-diff the mirror against `SwarmUI.extension.props` on every pin bump.

## D3 — UI injected into `.alt_prompt_region` — RESOLVED (hook is load-bearing in host)
- **Host definition:** `<div class="alt_prompt_region drag_image_target" id="alt_prompt_region">`
  (`src/Pages/_Generate/GenerateTab.cshtml:91`).
- **Host consumers (stability signal):** `wwwroot/js/genpage/main.js:598`,
  `gentab/layout.js:127`, `gentab/params.js:580`, `gentab/currentimagehandler.js:1045`, plus CSS
  (`genpage.css:1226`, `themes/modern.css:342`). `#alt_prompt_textbox` referenced across 11 host
  files. These are core Generate-tab surfaces, not incidental markup.
- **Other extension-relied host APIs, verified:** `showError` (`wwwroot/js/site.js:71`),
  `genericRequest(url, in_data, callback, depth=0, errorHandle=null)` (`site.js:148` — injects
  `session_id`, retries invalid sessions), `triggerChangeFor` (`site.js:278`),
  `#current_image` + `.current-image-img` (`gentab/currentimagehandler.js:711,862`).
- **Transport nuance:** `genericRequest` intercepts `data.error` **before** `onSuccess`
  (`site.js:188-193`) — server envelopes with `error` reach the frontend via `errorHandle` as
  text. The extension handles both channels (adapters + `peErrorText`), and the jsdom suite
  covers both (`routeResponses` with `success:false`, and `routeErrors`).
- **Runtime proof:** `just vendor-ci-test` boots the real host with the extension and exits 0
  (extension built + loaded through the real lifecycle; SwarmUI `--ci_test` turns any
  `Logs.Error` into a nonzero exit — `src/Utils/Logs.cs:143-146`, `src/Core/Program.cs:425-432`).
- **Status:** RESOLVED — injection is allowed (docs cover Tabs/ as the *documented* UI path but
  do not forbid DOM injection), the hook is host-core, and the retry loop + live gate guard it.

## D4 — "Lifecycle test coverage regressed" — CONTRADICTED
- **Old claim:** the active `OnPreInit` lifecycle test was replaced by a permanently skipped placeholder.
- **Reality:** `Tests/ExtensionLifecycleTests.cs` contains BOTH the active
  `OnPreInit_RegistersAssetsAndLicense_ThroughRealExtensionBase` test AND the explicit
  BLOCKED marker for browser E2E. The live-host boot gap the marker described is now
  partially closed by the committed `just vendor-ci-test` gate (browser-click E2E remains
  uncommitted and explicitly marked).
- **Status:** RESOLVED

## D5 — Settings persistence via GetGenericData/SaveGenericData — RESOLVED (contract verified)
- **Contract (`src/Accounts/User.cs`):** key is `{UserID}///${dataname}///{name.ToLowerFast()}`
  (`:113`, `:154`) — user-scoped; raw string upserted into LiteDB (`:155`) and read back verbatim
  (`:113`), so the store round-trips exactly. No size limit or sanitization at this layer.
- **Silent no-op gates, quoted:** `if (Program.NoPersist) { return; }` (`:144-147`) and
  `if (!MayCreateSessions) { return; }` (`:150-153`). These are precisely the two failure modes
  `SessionSettings.PersistVerified` (read-back verification) exists for, and
  `Tests/SessionSettingsTests.cs` exercises both against the real `User` code paths.
- **Old concern about the semantic-compare fallback:** the store round-trips verbatim, so the
  ordinal compare is the path that fires; the `JToken.DeepEquals` fallback is inert insurance.
- **Status:** RESOLVED

---

## Compliance spot-checks (unchanged from prior journal, re-verified at the pin)
- Namespace contains no "SwarmUI" (`docs/Making Extensions.md:125` rule).
- Root class name matches file name, extends `Extension`.
- `ScriptFiles`/`StyleSheetFiles` populated in `OnPreInit`.
- `API.RegisterAPICall(method, isUserUpdate, permission)` canonical signature.
- Permissions registered via `Permissions.Register` + `PermInfoGroup`/`PermInfo`.
- README "Connections" section satisfies Extension Standard #5 (`docs/Making Extensions.md:100-101`).
- MIT license in code + LICENSE file.

## De-handroll decisions (2026-07-02) — replace-or-justify, each cited
- **Reachability TTL cache — REPLACED.** The hand-rolled `Dictionary` + lock + timestamp
  prune in `WebAPI/BackendClient.cs` is now `Microsoft.Extensions.Caching.Memory.MemoryCache`
  with per-entry absolute expiration (MS Learn "Caching in .NET" / "Cache in-memory in
  ASP.NET Core": `IMemoryCache` recommended, `AbsoluteExpirationRelativeToNow`, "use
  expirations to limit cache growth"). Ships in the ASP.NET Core shared framework the
  `Sdk.Web` csproj already references — no new dependency. Probe behavior re-verified
  through the public-route socket tests and the live host boot gate.
- **MockHttpServer (raw TCP, Tests/BackendTransportTests.cs) — KEPT, justified.** MS Learn's
  recommended in-proc path (`Microsoft.AspNetCore.TestHost` `TestServer` /
  `WebApplicationFactory`) is "an in-memory test server" that handles requests "without
  network overhead" — i.e. no sockets. These tests exist to classify socket-level failures
  (connection refused, header-then-stall timeout, exact malformed bytes), which the
  vendor-recommended tool cannot exercise by design. A dumb deterministic TCP responder is
  the fit-for-purpose hostile-server simulator; it is contained test infrastructure covered
  by 14 green tests.
- **peEnsureButtons 250ms×40 poll (Frontend/promptenhance.ts) — KEPT, justified.** SwarmUI's
  first-party JS contains zero `MutationObserver` usage (sole hit in the repo is the bundled
  third-party `wwwroot/js/lib/select2.min.js`; positive control: same-scope greps hit
  constantly). The bounded poll matches host idiom, and a `subtree` observer on `body` would
  fire on every DOM change of the heavy Generate-tab render for a one-shot lookup.

## Known follow-ups
- Upstream offers `--ci_test_extensions` + a `ci-test` flag in `launchtools/extension_list.fds`
  (`src/Core/ExtensionsManager.cs:193-196`) — when this extension is PR'd to the extension list,
  opting into upstream CI is free coverage.
- The "did not come from git" warning during `vendor-ci-test`
  (`ExtensionsManager` metadata populate) is expected: the dev copy excludes `.git`. Warning-level
  only; does not fail the gate.
