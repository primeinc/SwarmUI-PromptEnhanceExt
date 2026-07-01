# PromptEnhance — Governance & Validation Ledger

This document is the audit trail for turning `SwarmUI-MagicPromptExtension` (a fork of the general-purpose
MagicPrompt LLM client) into **PromptEnhance**, a minimal canonical SwarmUI Generate-tab prompt-enhancement
extension. It records what was complained about, the on-disk evidence, what changed, and — critically — how each
change was *validated*. A claim is not "fixed" here until a named validation item passes, or the item is honestly
marked as inspection-only or still open.

Last validated state: extension `Build succeeded — 0 Warning(s), 0 Error(s)`; C# test suite
`Passed! Failed: 0, Passed: 60, Skipped: 0` (29 cases added for these findings on top of the prior 31-case suite;
all test files are staged as new — HEAD has no committed tests), run via
`dotnet test Tests/PromptEnhance.Tests.csproj -c Debug` against a real SwarmUI host build; **plus 7 frontend
behavior tests** (`node Tests/frontend/promptenhance.test.js` → `PASS — 7/7`) covering the browser apply policy,
loading-always-clears, and F3 image surfacing.

---

## 1. Complaint-to-evidence ledger

Findings came from an adversarial-oracle pass, then each fix was independently ground against canonical SwarmUI
refs (`../refs/mcmonkeyprojects/SwarmUI`) and the current on-disk code, and adversarially verified against disk
before being applied. Status legend: **FIXED+TEST** (code change proven by an automated test), **FIXED+GROUNDED**
(doc/CSS change with no meaningful runtime assertion; verified by grounded inspection), **OPEN** (not resolved).

| ID | Complaint | Evidence (pre-fix) | Resolution | Validation item | Status |
|----|-----------|--------------------|------------|-----------------|--------|
| F3 | Selected image silently downgraded to text-only on collection/parse failure — contradicts README "never silently dropped" | `promptenhance.js` `reader.onerror → resolve(null)` / `catch → null`; `BackendClient.ParseMedia` `continue` on blank data | Browser path rejects/throws (surfaced + request aborted); `ParseMedia` is `public` and throws `ArgumentException`, caught → `UnsupportedImage` payload | `ParseMediaTests` (3, server) + `Tests/frontend` F3 tests (browser) | FIXED+TEST |
| F4 | `SavePromptEnhanceSettings` persisted any payload with zero validation → `timeoutSeconds:0` cancels every request, negative throws in CTS, non-numeric `temperature` throws on read | `SessionSettings.cs` merged known keys straight to store | Added pure `ValidateSettings(JObject)` guard, called before any storage I/O | `SessionSettingsTests` (10) | FIXED+TEST |
| F5 | Auth error told the user to "Set the API key…" but the extension sends no key and has no such setting | `ErrorHandler.cs` Authentication remediation | Reworded to "sends no API key; point at an unauthenticated/local server" | `ErrorHandlerTests.Format_Authentication_DoesNotInstructSettingAnApiKey` | FIXED+TEST |
| F6 | `RegisterAPICall` bool documented as "requires auth"; 3 of 5 values wrong for the real `isUserUpdate` convention | `PromptEnhanceAPI.Register()` | Doc corrected (bool = idle-timeout bookkeeping; auth = `PermInfo`); flipped ListModels→false, Save/Reset→true | `ApiRegistrationTests.Register_SetsIsUserUpdatePerConvention` | FIXED+TEST |
| F8 | Client default `systemPrompt` was `''` while server default is a real instruction; a backend-down init + save persists empty over the real default | `settings.js` vs `SessionSettings.Defaults` | Client default set equal to the server default verbatim | `SettingsDefaultsParityTests.ClientSystemPromptDefault_MatchesServerDefaultVerbatim` | FIXED+TEST |
| F9 | Any 400 with an image attached was labeled `UnsupportedImage`, even for context-length / bad-parameter errors | `BackendClient.cs` gated only on `media>0 && 400` | Added `ErrorHandler.LooksLikeImageRejection(body)`; a bare 400 now falls back to `HttpError` | `ErrorHandlerTests.LooksLikeImageRejection_*` (2 theories, 9 cases) | FIXED+TEST |
| F10 | Settings stored under `Program.Sessions.GenericSharedUser` — **shared across all users**, not per-user | `SessionSettings.cs` `GenericSharedUser.SaveGenericData/GetGenericData` | Threaded `Session` into all three settings routes; persistence now uses `session.User.GetGenericData/SaveGenericData` (keyed by `UserID`, User.cs:113,154) so each user's config is isolated | `ApiRegistrationTests` (routes register with the new `Session` signatures) + grounding (User.cs:113,154) | FIXED |
| F11 | Both `PermInfo` records omitted the 6th `PermSafetyLevel` arg → silent `UNTESTED`, against SwarmUI convention | `PromptEnhanceAPI.cs` 5-arg `PermInfo` ctors | Passed `PermSafetyLevel.POWERFUL` (outbound network / stores config) explicitly | `PermissionsTests` (2) | FIXED+TEST |
| F12 | Error status used `var(--error-line, #d55)` — `--error-line` does not exist in SwarmUI CSS, so it never themed | `settings.css:127` | Swapped to `var(--red, #d55)` (canonical `:root` theme var) | none (jsdom cannot compute the theme cascade; `--error-line` absent upstream, `--red` present — validated by `rg`) | FIXED+GROUNDED |
| F13 | `BackendClient`/`SessionSettings` inherited `: PromptEnhanceAPI` purely to reach static helpers (empty base, no polymorphism) | class decls | Dropped both bases; qualified every helper call `PromptEnhanceAPI.X(...)` | `InheritanceRefactorTests` (2) + the whole assembly compiling | FIXED+TEST |
| F14 | `ModelMissing` enum doc asserted the model was validated against the backend; no such validation exists | `ErrorHandler.cs:15` | Reworded to describe the two real triggers (empty model / 404) | none (enum XML doc) | FIXED+GROUNDED |
| F15 | `ReachabilityCache` static dict never evicted, undocumented | `BackendClient.cs:34` | Added `///` doc explaining bounded cardinality (INFO-tier; eviction would be over-engineering). Spec's only defect was a citation line number (`AGENTS.md:156`→`:155`) in prose, not in any shipped file | none (field doc) | FIXED+GROUNDED |

**Merge note.** Two fix specs collided because each was ground independently: **F9 ∩ F13** (same error block in
`PromptEnhanceRun`) and **F4 ∩ F13** (validation insert vs. base-drop qualification in `SessionSettings`). Both were
hand-merged; the resulting compile + `ParseMediaTests`/`SessionSettingsTests`/`ErrorHandlerTests` passing is the proof
the merge is correct.

---

## 2. File classification matrix (deletion-biased)

The canonical contract: read the Generate-tab prompt → optionally attach the current image (opt-in) → call the owned
`/v1/models` and `/v1/chat/completions` seams → return structured success/failure → apply via a reversible policy.
Everything not serving that contract, reducing blast radius, or teaching the SwarmUI extension pattern was removed.

### KEEP — every file in the current deliverable serves the contract

| File | Responsibility |
|------|----------------|
| `PromptEnhanceExtension.cs` | Extension entrypoint: registers assets and API routes (the SwarmUI extension seam) |
| `PromptEnhance.csproj` | `Microsoft.NET.Sdk.Web` + `SwarmUI.extension.props` import (canonical extension build) |
| `WebAPI/PromptEnhanceAPI.cs` | API registration, permissions, structured response helpers, wire deserialization |
| `WebAPI/BackendClient.cs` | The two owned transport seams; reachability cache; media parse; structured failure |
| `WebAPI/SessionSettings.cs` | Single source of truth for the 8 config knobs; load/save/reset; `ValidateSettings` |
| `WebAPI/ErrorHandler.cs` | Structured error taxonomy the UI switches on; HTTP→category; image-rejection heuristic |
| `WebAPI/Models/Models.cs` | Minimal wire DTOs for `/v1/models` and `/v1/chat/completions` |
| `BackendSchema.cs` | OpenAI chat-request shaper (text vs multimodal content parts) |
| `Assets/promptenhance.js` | Generate-tab surface: Enhance button, reversible apply policy, image collection |
| `Assets/settings.js` | Client config mirror + settings panel; load/save/reset; model fetch |
| `Assets/promptenhance.css`, `Assets/settings.css` | Theme-variable-driven styling for the above |
| `Tests/*` | 9 xUnit files (60 cases, C# surface) + `Tests/frontend/promptenhance.test.js` (7 Node `vm` tests, browser behavior, no npm dep) |
| `README.md`, `LICENSE`, `docs/GOVERNANCE.md` | Truthful docs, license + acknowledgment, this ledger |
| `.gitignore` | Build-artifact ignore rules (keeps `bin/`, `obj/`, `vendor/` out of the tree) |

### DELETE — removed as "general LLM client" sprawl (done; staged in the coherent commit)

Standalone chat surface (`Assets/chat.*`, `WebAPI/MagicPromptAPI.cs`), vision surface (`Assets/vision.*`),
branding assets (`Images/**`, `Assets/magicprompt.*`), provider matrix / cloud-key models
(`WebAPI/Models/{Anthropic,Ollama,OpenAI,OpenAIAPI,OpenRouter}Models.cs` — 5 files, `WebAPI/LLMAPICalls.cs`), instruction/prompt
machinery (`InstructionResolver.cs`, `ModelListProvider.cs`, `PromptCache.cs`, `PromptHandler.cs`), the separate
MagicPrompt tab (`Tabs/Text2Image/MagicPrompt.html`), and the old entrypoint (`MagicPromptExtension.cs`).

### UNKNOWN — none. Every current file has a stated responsibility above.

---

## 3. Validation report (contract → evidence)

| Contract clause | Evidence | Type |
|-----------------|----------|------|
| Owned base-URL normalization (`root` or `/v1` both resolve the seams) | `BackendClientTests.NormalizeBaseUrl_*` | test |
| Request wire shape (text-only string vs multimodal content array; system omitted when blank) | `BackendSchemaTests` (5) | test |
| Settings are real, typed, and range-guarded before persist | `SessionSettingsTests` (10) | test |
| Client/server `systemPrompt` default parity (the other 7 defaults match by inspection, not asserted) | `SettingsDefaultsParityTests` | test |
| Structured error taxonomy the UI reacts to | `ErrorHandlerTests` (status→category, code, format, excerpt, auth, image-rejection) | test |
| Image attach opt-in never silently downgraded (server side) | `ParseMediaTests` (3) | test |
| API routes register with the correct idle-timeout flag and explicit permission safety level | `ApiRegistrationTests`, `PermissionsTests` | test |
| No false is-a inheritance | `InheritanceRefactorTests` + full-assembly compile | test |
| Whole extension compiles against real SwarmUI | `dotnet build` → 0/0 | build |

### Honest coverage gaps (not covered by automated tests)

1. **Frontend behavior — now covered.** `Tests/frontend/promptenhance.test.js` (7 tests via Node `vm` + stubs, no
   npm dependency) enforces the reversible apply policy (preview does not mutate; append preserves the original;
   replace stashes it for restore), the loading state always clearing on success and on failure, and F3's
   browser-side image-collection surfacing (a failed image read surfaces an error and aborts — the request is
   never sent text-only). What remains untested here is the full DOM lifecycle (button injection, panel wiring),
   which needs a real jsdom/browser harness.
2. **Server-side wiring is inspection-only.** Only the *pure* helpers are unit-tested — `ValidateSettings`,
   `LooksLikeImageRejection`, `ParseMedia`, `NormalizeBaseUrl`, `BuildChatRequest`. The persistence entrypoints
   (`SavePromptEnhanceSettings` / `GetPromptEnhanceSettings` / `ResetPromptEnhanceSettings`) and both HTTP route
   bodies (`PromptEnhanceListModels` / `PromptEnhanceRun`) have **no** tests — they require a live SwarmUI `Session`
   / `GenericSharedUser` / real HTTP. So the *wiring* — that F4's guard runs before storage (`SessionSettings.cs:77-81`)
   and that F9's image-gate sits in the 400 path (`BackendClient.cs:220-222`) — is verified by inspection, not
   enforced by a test. A Save→Get round-trip through a stub session would upgrade these from inspection to enforced.
3. **F10 resolved (per-user).** Settings now persist under `session.User` (per-user, keyed by `UserID`). The route
   registration with the new `Session` signatures is test-covered (`ApiRegistrationTests`); the actual per-user DB
   isolation is a property of the upstream `User` store, verified by grounding (User.cs:113,154), not a
   host-booting test.
4. **F12 / F14 / F15** are CSS/doc changes validated by grounded inspection, not runtime assertions (a var()
   theme cascade and XML-doc text are not meaningfully unit-testable here).

### README truthfulness

The two previously load-bearing-but-unbacked claims are now true by code: README:21 ("no API key is sent") matches
F5 and the keyless transport; README:55 ("never silently dropped") is made true by F3. The backends table
(Ollama/LM Studio/llama.cpp/…) matches the F5 remediation text. No README edit was required.
