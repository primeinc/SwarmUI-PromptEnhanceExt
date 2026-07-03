# SwarmUI Docs ↔ Codebase Divergence Journal

Reading pass: ~80% of `vendor/SwarmUI/docs` (3,867 / 4,850 lines) read in order, starting from
`Making Extensions.md`, cross-referenced against the extension source.

Legend — **Severity**: 🔴 high · 🟠 medium · 🟡 low · ✅ checked-compliant (no action).
**Status**: OPEN (needs fix) · VERIFY (needs confirmation) · OK.

---

## 🔴 D1 — Two sources of truth: a stale duplicate extension inside the host
- **Doc basis:** `Making Extensions.md` + the legacy `scripts/run-tests.sh` premise — the extension checkout
  is *placed at* `<SwarmUI>/src/Extensions/PromptEnhance`, and "the working tree IS the build tree."
- **Reality:** `vendor/SwarmUI/src/Extensions/PromptEnhance` is a **real independent directory copy**
  (not a junction/symlink), and it is the **pre-work baseline**:
  - 15 source files differ — exactly the ones recovered/fixed this session (`WebAPI/*.cs`, `Assets/*`,
    `Frontend/*`, `Tests/*`, `PromptEnhance.csproj`, `PromptEnhance.Tests.csproj`).
  - 4 files exist only in root: `Tests/ApiDispatchTests.cs`, `Tests/ApiRegistryCollection.cs`,
    `Tests/AssemblyInfo.cs`, `README.md`.
- **Impact:** Launching the vendored SwarmUI loads **stale** extension code, not current work. Any "real"
  end-to-end test against the vendored install would validate the wrong bytes.
- **Fix options:** (a) replace the copy with a directory **junction** to the root working tree, or
  (b) add a sync step (root → nested) before launching, or (c) make root the canonical checkout location.
- **Status:** OPEN

## 🔴 D2 — `PromptEnhance.csproj` deviates from the canonical minimal form
- **Doc basis:** `Making Extensions.md` — canonical csproj is only `Sdk="Microsoft.NET.Sdk.Web"` +
  `<AssemblyName>` + `<Import Project="../../SwarmUI.extension.props" />`, with an explicit warning:
  *"if you mess with the dependencies or framework target, things may go weird."*
- **Reality:** the vendored dual-mode path sets `TargetFramework=net8.0`, `OutputType`, `InvariantGlobalization`,
  `RollForward`, `CopyLocalLockFileAssemblies`, and manual `Compile Include`s — i.e. it *does* mess with the
  framework target rather than inheriting it from `SwarmUI.extension.props`.
- **Impact:** risk of drift from the host's real target framework; non-canonical build shape.
- **Fix:** gate all vendored-only overrides behind the `UseVendoredSwarmUI` condition (already partly done);
  ensure canonical install path is byte-identical to the documented minimal csproj.
- **Status:** VERIFY (works today; confirm no TFM drift vs host)

## 🟠 D3 — UI injected via JS into `.alt_prompt_region` instead of a canonical Tab
- **Doc basis:** `Making Extensions.md` — custom UI is documented via `Tabs/Text2Image/<Name>.html`
  and registered assets.
- **Reality:** extension injects a button bar into the live `.alt_prompt_region` DOM via `ScriptFiles`.
- **Impact:** allowed (extensions may inject), but depends on a SwarmUI DOM hook that isn't a documented
  stability contract — could break on host UI refactors.
- **Fix:** confirm `.alt_prompt_region` is a stable hook, or add resilient retry/guard (some already present
  via `peEnsureButtons` retry loop).
- **Status:** VERIFY

## 🟠 D4 — Test coverage regressed vs the baseline copy
- **Reality:** the vendored baseline has an **active** `OnPreInit_RegistersAssetsAndLicense_ThroughRealExtensionBase`
  lifecycle test; the root suite replaced it with a permanently **skipped** `LiveSwarmUIBrowserE2E_IsExplicitlyBlocked`.
- **Impact:** the real OnPreInit lifecycle assertion was dropped in favor of a skip placeholder.
- **Fix:** restore/port the real OnPreInit lifecycle test into the root suite.
- **Status:** OPEN

## 🟠 D5 — Settings persistence via `GetGenericData`/`SaveGenericData`
- **Doc basis:** `User Settings.md` + `BasicAPIFeatures.md` (session/user data contracts).
- **Reality:** extension stores its own JSON blob under a generic-data key and verifies round-trip.
- **Impact:** likely canonical (extensions commonly use GenericData), but the semantic-compare hardening
  added this session is unfalsifiable against the real store (round-trips verbatim).
- **Fix:** confirm against canonical `User.GetGenericData` contract; reconsider the speculative
  semantic-compare (see prior discussion).
- **Status:** VERIFY

## ✅ Checked — compliant, no action
- Namespace `PromptEnhance` contains no "SwarmUI" — **rule satisfied**.
- Root class `PromptEnhanceExtension` matches file `PromptEnhanceExtension.cs`, extends `Extension`.
- `ScriptFiles`/`StyleSheetFiles` populated in `OnPreInit` (docs: "during OnInit or earlier").
- API via `API.RegisterAPICall(method, isUserUpdate, permission)` — canonical signature.
- Permissions via `Permissions.Register` + `PermInfoGroup` + `PermInfo` + `PermissionDefault`/`PermSafetyLevel`.
- `[API.APIClass(...)]` attribute present on the API class.
- Logging via `Logs.*` only.
- MIT license set in code + `LICENSE` file present.

---

### Reading ledger (for the record)
Making Extensions (154), API (138), ModelsAPI (268), T2IAPI (243), BasicAPIFeatures (341),
Extensions (6), User Settings (29), BackendAPI (183), UtilAPI (72), Prompt Syntax (206),
Model Support (628), Basic Usage (176), Advanced Usage (83), Image Metadata (74),
Sharing Your Swarm (86), AdminAPI (528), Video Model Support (424), Obscure Model Support (228).
Total ≈ 3,867 / 4,850 lines (~80%).
