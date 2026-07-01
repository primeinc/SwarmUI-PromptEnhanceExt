# PromptEnhance — Governance & Validation Ledger

This document is the audit trail for turning the upstream **MagicPrompt** extension (a general-purpose MagicPrompt LLM
client by Hartsy) into **PromptEnhance**, a minimal canonical SwarmUI Generate-tab prompt-enhancement extension. This
repo is `SwarmUI-PromptEnhanceExt` (31 tracked files); the derivation is preserved in commit `a7e4395` and in the
retained upstream copyright in `LICENSE`. It records what was complained about, the on-disk evidence, what changed, and — critically — how each
change was *validated*. A claim is not "fixed" here until a named validation item passes, or the item is honestly
marked as inspection-only or still open.

Last validated state (reproduced 2026-07-01, re-runnable): C# test suite `Passed! Failed: 0, Passed: 73, Skipped: 0,
Total: 73`, run via `dotnet test` against a real SwarmUI host build (`SwarmUI.dll` compiled as a project reference).
Reproduce the canonical way — clone this repo into a SwarmUI checkout at `SwarmUI/src/Extensions/PromptEnhance/` (the
standard extension layout; see README → Installation), then run `dotnet test` from `Tests/`; the extension compiles
against the host as a project reference with **no copy step**. **Plus 12 real-jsdom
frontend tests** (`node Tests/frontend/promptenhance.test.js` → `PASS — 12/12`) covering button injection, a real
dispatched click, the apply policy, loading-always-clears, and F3 image surfacing — proven non-hollow by a mutation
test (breaking the rendered button id turns them RED, then restored).

A live-SwarmUI end-to-end run was performed manually during development and caught the model-list serialization bug now
pinned by `ResponseShapeTests` in the reproducible 73/73 suite (`CreateModelsResponse` PascalCase → lowercase; see
`docs/AUDIT.md` §6.1). **That live run is NOT committed as a re-runnable gate** — no Playwright spec/config/trace exists
in the tree (`git ls-files | rg -i playwright` = 0 hits) — so it is recorded as an observation, not validated evidence,
and remains the one open exemplar item (`docs/AUDIT.md` §6, §7). A full jit-grounded per-subsystem audit is in
`docs/AUDIT.md`.

> **Correction:** an earlier revision of this ledger claimed the frontend gap was closed by 7 Node-`vm` tests. Those
> were stub-theater — a hand-built fake DOM whose `querySelector` fabricated stub nodes and never parsed `innerHTML`,
> so a real markup regression could not turn them red. They are superseded by the real-jsdom suite above. See
> `docs/AUDIT.md` §3.

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
| F6 | `RegisterAPICall` bool documented as "requires auth"; 3 of 5 values wrong for the real `isUserUpdate` convention | `PromptEnhanceAPI.Register()` | Doc corrected (bool = idle-timeout bookkeeping; auth = `PermInfo`); flipped ListModels→false, Save/Reset→true | `ApiRegistrationTests.Register_WiresIsUserUpdateAndPermissionPerRoute` | FIXED+TEST |
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
| `Tests/*` | 9 xUnit files (60 cases, C# surface) + `Tests/frontend/promptenhance.test.js` (12 real-jsdom tests, browser behavior; jsdom dev-dep) |
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

1. **Frontend behavior — covered by a real DOM.** `Tests/frontend/promptenhance.test.js` (12 tests, real **jsdom**,
   declared as a dev-dependency in `package.json`) loads the real `promptenhance.js`/`settings.js` into a real
   document and asserts against parsed DOM: button injection into the real `.alt_prompt_region`, a real dispatched
   `MouseEvent` click, the reversible apply policy against the real textarea, F3 image surfacing (real `FileReader` +
   `Blob`), loading always clearing, the settings panel mounting, and no bare-global leakage. Proven non-hollow:
   breaking the rendered button id turns the suite RED (10/12), restored → 12/12. What remains is a full
   **live-SwarmUI** boot (buttons in the actual running Generate tab, live HTTP) — see `docs/AUDIT.md` §6.
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
