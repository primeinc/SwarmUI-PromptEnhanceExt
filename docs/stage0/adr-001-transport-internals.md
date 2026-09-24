# ADR-001 — Transport internals: hand-rolled HTTP pipeline vs openai-dotnet protocol layer

Status: Superseded. SDK-first voids the Decision section; ADR-002 re-runs it with the full option space —
per-provider maintained clients (openai-dotnet; OllamaSharp; Anthropic's client), vendor resilience
libraries, and the loader-deployment question settled by an empirical spike. Facts below remain verified at
the pins. Scope: the *internals* of the foundation's LLM transport, hidden behind the owned public SPI
defined in Stage 4; no public seam may depend on any option's types (PLATFORM-PLAN "Standing rules").

Evidence pins: openai-dotnet at `refs/openai/openai-dotnet` HEAD `6450c84ae936944613b1844d725466c9b450fa77`
(2026-07-01); System.ClientModel at `refs/Azure/azure-sdk-for-net` HEAD `9173609c7bc2e09c7a5402f424d666b569e5b25d`;
SwarmUI at `vendor/SwarmUI` HEAD `9c81c1cbcb5f256508e186fd3b4faa873c139b7d` (== `swarmui_pin`, `justfile:15`);
MagicPrompt at `refs/SwarmUI-MagicPromptExtension` HEAD `e2e50061055d4b16184efbc0b85425fc74fa5951`.

Note on inputs: PLATFORM-PLAN 0.3 names "the two memos from the 2026-07-04 session" as inputs. Those memos
were not available to this pass; the decision below is grounded solely in the capability checklist
(docs/stage0/magicprompt-inventory.md) and the cited sources. If the memos contain contrary evidence,
this ADR must be re-opened. (Blocked input, named explicitly per read-the-room discipline.)

## Read ledger (decision inputs)

| File | Read |
|---|---|
| `openai-dotnet/OpenAI/src/Generated/ChatClient.cs` | PARTIAL (lines 1-120; generated file, claims scoped to lines read) |
| `openai-dotnet/OpenAI/src/Generated/ChatClient.RestClient.cs` | PARTIAL (lines 56-76) |
| `openai-dotnet/OpenAI/src/Custom/Chat/ChatClient.cs` (410) | FULL |
| `openai-dotnet/OpenAI/src/Custom/Chat/ChatCompletionOptions.cs` (261) | FULL |
| `openai-dotnet/OpenAI/src/Custom/Chat/ChatCompletion.cs` (95) | FULL |
| `openai-dotnet/OpenAI/src/Custom/OpenAIClientOptions.cs` (110) | FULL |
| `openai-dotnet/OpenAI/src/OpenAI.csproj` (42) | FULL |
| `openai-dotnet/OpenAI/src/Generated/OpenAIModelClient.RestClient.cs` | PARTIAL (lines 17-35) |
| `azure-sdk-for-net/sdk/core/System.ClientModel/src/Pipeline/ClientRetryPolicy.cs` | PARTIAL (lines 1-80, `:293-307`) |
| `azure-sdk-for-net/sdk/core/System.ClientModel/src/Pipeline/ClientPipeline.cs` | PARTIAL (line 140 context) |
| `WebAPI/BackendClient.cs` (273, this repo) | FULL |
| `BackendSchema.cs` (54, this repo) | FULL |
| `vendor/SwarmUI/src/SwarmUI.deps.props` (13) | FULL |
| `vendor/SwarmUI/src/SwarmUI.extension.props` (20) | FULL |

## Context

The foundation must provide LLM transport behind a versioned SPI. The capability checklist derived from
the MagicPrompt inventory (docs/stage0/magicprompt-inventory.md, "Capability rollup") requires:

- C1. Non-streaming chat completion with system/user/image parts; callable from a synchronous host pipeline.
- C2. Keyed auth (optional Bearer today; per-provider header dialects for full parity), sourced from SwarmUI's key store.
- C3. Dialect control: local OpenAI-compatible servers speak **`max_tokens`** (MagicPrompt sends `max_tokens`
  everywhere — `BackendSchema.cs:181,192,205,217` in the MagicPrompt clone; PromptEnhance likewise —
  `BackendSchema.cs:49` in this repo); full parity later adds non-OpenAI request *paths*
  (Ollama `/api/chat`+`/api/tags`, Anthropic `/v1/messages` — inventory B10).
- C4. Model listing, including non-OpenAI dialects for parity (inventory B2, B10, B17).
- C5. Classified error taxonomy built from raw HTTP status + body (inventory B19), plus Stage 3's
  `finish_reason`/`refusal` surfacing.
- C6. Per-request timeout control and a local-backend reachability story (inventory B21-B22).
- C7. Streaming is required by **zero** current rows (every MagicPrompt request sets `stream:false`) —
  the SPI signature is streaming-shaped for the future, but v1 implements non-streaming (PLATFORM-PLAN Stage 4.1).

Current state (this repo): the hand-rolled client is one static `HttpClient` with per-request
`CancellationTokenSource` timeouts (`WebAPI/BackendClient.cs:18-25,136,212`), owned JSON bodies
(`BackendSchema.cs:19-52`), classified errors from status+body (`WebAPI/BackendClient.cs:140-163,219-245`),
and **no retries** — VALIDATED_EMPTY: `rg -i "retry|retries"` over `WebAPI/` + `BackendSchema.cs` → 0 hits
(exit 1), positive control `"Reachability"` → hits at `WebAPI/BackendClient.cs:27-28`. It compensates with a
reachability probe: an extra `GET /v1/models` per call window, TTL-cached 10s/30s
(`WebAPI/BackendClient.cs:57-84`).

Facts about the openai-dotnet option, verified at the pin:

- Protocol-layer methods exist: `CompleteChat(BinaryContent, RequestOptions)` /
  `CompleteChatAsync(BinaryContent, RequestOptions)` — `OpenAI/src/Generated/ChatClient.cs:78,86`. The caller
  owns the request JSON, so C3's *field* dialect (`max_tokens`, `seed`, `top_p`) is fully expressible there.
- The protocol layer pins the URL path: `{endpoint}/chat/completions`, built in an `internal virtual`
  method consumers cannot override — `OpenAI/src/Generated/ChatClient.RestClient.cs:58-70` (path at `:62`).
  Model listing is likewise pinned to `/models` — `OpenAI/src/Generated/OpenAIModelClient.RestClient.cs:21`.
  Ollama-native and Anthropic paths (C3/C4 parity) are unreachable through it.
- The convenience layer hides the deprecated `max_tokens` as internal `_deprecatedMaxTokens` and exposes only
  `MaxOutputTokenCount` → `max_completion_tokens` — `OpenAI/src/Custom/Chat/ChatCompletionOptions.cs:119-128`.
  A `max_tokens`-only local server cannot be addressed through the *stable* typed options surface; an
  `[Experimental("SCME0001")]` `JsonPatch` hatch on the same partial type can inject arbitrary wire fields
  (`OpenAI/src/Generated/Models/Chat/ChatCompletionOptions.cs:60-63`) but is evaluation-only API and leaves
  the pinned-path objection untouched.
- Responses carry typed `FinishReason` (flattened enum, `OpenAI/src/Custom/Chat/ChatCompletion.cs:50`) and
  `Refusal` (`:80`); the SDK's own convenience methods obtain the typed `ChatCompletion` by casting the
  protocol `ClientResult` (`OpenAI/src/Custom/Chat/ChatClient.cs:169,198`), so typed parsing is reachable
  from the protocol layer too.
- Streaming is SSE over `System.Net.ServerSentEvents` with `BufferResponse=false` enforcement —
  `OpenAI/src/Custom/Chat/ChatClient.cs:237-259`.
- Package dependencies: `System.ClientModel` and `System.Net.ServerSentEvents` always;
  `System.Diagnostics.DiagnosticSource` only for targets *not* net8.0-compatible —
  `OpenAI/src/OpenAI.csproj:20-27`. SwarmUI extensions target net8.0 (`vendor/SwarmUI/src/SwarmUI.extension.props:3`),
  so the direct additions are two packages (transitive closure unverified — open question below).
- The host's nine packages (`vendor/SwarmUI/src/SwarmUI.deps.props:3-11`: FreneticUtilities, Hardware.Info,
  LiteDB, LLamaSharp ×3, Newtonsoft.Json, ImageSharp ×2) have zero overlap with those — no version-conflict
  risk, but every SDK DLL is a *new private dep* the extension loader must serve: the extension props set
  `CopyLocalLockFileAssemblies=false` (`SwarmUI.extension.props:7`), and the per-extension ALC probes only the
  extension's build-output folder (`vendor/SwarmUI/src/Core/ExtensionsManager.cs:44-49,78-81`), so shipping the
  SDK requires overriding that default and verifying the DLLs land next to the built extension.
- Client construction requires a credential: every non-Experimental `ChatClient` constructor takes an
  `ApiKeyCredential` (`OpenAI/src/Custom/Chat/ChatClient.cs:35-65`); keyless local backends (the PromptEnhance
  default; MagicPrompt's ollama/openaiapi keys are optional — its `LLMAPICalls.cs:349-362`) would need a
  placeholder credential. Custom endpoints are supported via `OpenAIClientOptions.Endpoint`
  (`OpenAI/src/Custom/OpenAIClientOptions.cs:21-29`).
- Retries come free: `ClientPipeline` installs `options.RetryPolicy ?? GetClientRetryPolicy()`
  (`azure-sdk-for-net sdk/core/System.ClientModel/src/Pipeline/ClientPipeline.cs:140`), defaulting to 3 retries
  with 0.8s initial delay (`.../ClientRetryPolicy.cs:25,27,33`). openai-dotnet itself configures no retry
  override — VALIDATED_EMPTY: `rg "ClientRetryPolicy|RetryPolicy"` over `OpenAI/src/` + `README.md` → 0 hits
  (exit 1), positive control `ClientPipeline` same scope → hits.

## Options

**A. Hand-rolled HTTP pipeline** (status quo, hardened): `HttpClient` + owned request bodies + owned error
classification; add whatever reliability behavior the SPI conformance suite demands (retry/backoff is a
policy decision the suite can force later).

**B. openai-dotnet used via protocol-layer methods**: `ChatClient.CompleteChat(BinaryContent, RequestOptions)`
with caller-owned JSON, SDK-owned pipeline (auth header, retries, SSE machinery), typed response casting.

The convenience layer alone is eliminated by C3 directly: `max_tokens` is not expressible through it
(`ChatCompletionOptions.cs:119-128`). Option B is therefore the protocol layer, as the plan specified.

## Steelman — the losing option's strongest argument (Option B)

Stated in full, as the strongest case for adopting openai-dotnet's protocol layer:

> The foundation is about to re-implement, alone and forever, infrastructure that OpenAI's own .NET team
> maintains under conformance pressure from millions of callers. The protocol layer is *exactly* the
> escape hatch built for this situation: `CompleteChat(BinaryContent, RequestOptions)`
> (`Generated/ChatClient.cs:78,86`) hands you byte-level control of the request body — `max_tokens`,
> `seed`, any local-server quirk — while the pipeline underneath supplies what the hand-rolled client
> provably lacks: automatic retries (3 attempts, exponential from 0.8s — `ClientRetryPolicy.cs:27,33`),
> which would let you *delete* the reachability probe and its extra GET per call
> (`WebAPI/BackendClient.cs:57-84`) instead of maintaining a cache-TTL heuristic that guesses at
> availability. Stage 3's entire work item — `finish_reason` enum handling and `refusal` surfacing — is
> already shipped as tested public API (`ChatCompletion.cs:50,80`), reachable from protocol results by a
> one-line cast the SDK itself uses (`Custom/Chat/ChatClient.cs:169,198`). When streaming lands, SSE
> parsing arrives with it (`Custom/Chat/ChatClient.cs:237-259`) rather than being hand-written against a
> wire format with real edge cases. The dependency objection is weak: two packages, zero overlap with the
> host's nine (`SwarmUI.deps.props:3-11`), and the loader has a designed private-dep path with an explicit
> host-wins conflict rule (`ExtensionsManager.cs:30-50`). Choosing to hand-roll is choosing to own wire
> conformance, retry policy, SSE parsing, and every future OpenAI API change yourself — permanently — to
> avoid a one-time `CopyLocalLockFileAssemblies=true` line in a csproj.

## Decision

**Option A — keep the hand-rolled HTTP pipeline as the foundation's transport internals for SPI v1.**

Decided against the capability checklist, not preference:

1. **C3/C4 (dialect + parity paths) break Option B's coverage.** The protocol layer pins request paths
   (`/chat/completions` — `ChatClient.RestClient.cs:62`; `/models` — `OpenAIModelClient.RestClient.cs:21`)
   in `internal virtual` methods consumers cannot override. MagicPrompt parity (Stage 5 exit criterion)
   requires Ollama `/api/chat` + `/api/tags` and Anthropic `/v1/messages` (inventory B10). Under Option B
   those providers need a hand-rolled path *anyway*, so the SDK does not replace the hand-rolled pipeline —
   it adds a second pipeline beside it, and the SPI internals would have to unify two transport stacks.
2. **What Option B uniquely adds is not on the checklist.** Retries and SSE are its genuinely superior
   pieces; C7 records that no current capability row requires streaming, and retry policy is expressible
   in ~a page over `HttpClient` when the Stage 4 conformance suite demands it — whereas the probe removal
   argued in the steelman is available to either option (the probe is a product behavior choice, not an
   SDK artifact).
3. **Every checklist row is covered by Option A as built or as already-planned stage work** and exercised by socket-level tests
   (`Tests/BackendTransportTests.cs`, Stage 3 extends them): owned bodies give C3 by construction
   (`BackendSchema.cs:44-51` sends `max_tokens` verbatim), C5's taxonomy consumes raw status+body
   (`WebAPI/BackendClient.cs:140-163,219-245`), C2 is an `Authorization` header (Stage 4.2), and C6 exists
   (`:86-95`). Stage 3's `finish_reason`/`refusal` work is JSON-field handling on bodies we already parse,
   with the enum semantics specified by `openapi.yaml` at the refs mirror (per PLATFORM-PLAN Stage 3), not
   by the SDK.
4. **Option B's costs are structural, not one-line.** A keyless-backend placeholder credential
   (`ChatClient.cs:35-65`), a `CopyLocalLockFileAssemblies` override plus verification that every SDK DLL
   (direct and transitive) survives the extension build → output-folder → ALC-probe path
   (`SwarmUI.extension.props:7`, `ExtensionsManager.cs:44-49`), and a live host-boot gate
   (`just vendor-ci-test`) that must now certify third-party assembly loading on every SwarmUI pin bump.
   That is a standing integration surface purchased for capabilities the checklist does not yet demand.

Because the SPI hides transport internals (SPI design rule), this decision is reversible: Option B can be
adopted later as an alternative SPI implementation without any consumer-visible change — and the Stage 4
conformance suite is precisely the harness that would validate such a swap.

**Revisit triggers** (any one re-opens this ADR): the SPI commits to *implemented* streaming; a conformance
requirement lands that effectively means rebuilding `ClientPipeline` (retry + telemetry + auth policies);
or the provider set narrows to OpenAI-dialect-only, eliminating objection 1.

## Consequences

- The foundation owns wire conformance for the OpenAI-compatible dialect permanently: Stage 3's
  `finish_reason`, `refusal`, and MIME handling are implemented and tested in-repo against `openapi.yaml`
  citations, with no upstream SDK to lean on. This is accepted, priced work.
- Retry/backoff remains absent until the SPI conformance suite specifies it; the reachability probe
  (`WebAPI/BackendClient.cs:57-84`) remains the availability mechanism for v1 and its extra GET per
  call window remains a known cost, re-evaluated when retry policy is specified.
- Streaming, when implemented, will be built on `System.Net.ServerSentEvents` (BCL package, one dependency,
  no client framework) or re-open this ADR per the revisit trigger — recorded now to prevent an ad-hoc
  SSE parser appearing without a decision.
- No new package dependencies enter the extension in Stage 4; the loader-deployment question
  (`CopyLocalLockFileAssemblies`) stays closed until an ADR revisit opens it.
- The Stage 4 SPI must still be shaped so that an openai-dotnet-backed implementation could pass the
  conformance suite (no SPI type may leak `HttpClient`, `HttpResponseMessage`, or other Option-A internals) —
  this is the SPI design rule applied to this ADR's outcome.
- Dialect stance for Stage 3 item 4, recorded: send `max_tokens` (matches every local server in scope and
  MagicPrompt's own behavior); a capability flag for `max_completion_tokens`-preferring endpoints is deferred
  until a keyed-cloud requirement makes it real.

## Open questions carried forward

- The two 2026-07-04 session memos named as ADR inputs were unavailable to this pass (see Context note).
- Transitive dependency closure of `System.ClientModel` (e.g. `Microsoft.Extensions.Logging.Abstractions`
  usage visible at `ClientRetryPolicy.cs:10-11`) was not enumerated; must be sized before any future
  Option B adoption.
- Whether `System.ClientModel`'s `ClientErrorBehaviors`/no-throw mode cleanly supports C5's
  raw-body error taxonomy from protocol results was not verified (not needed for this decision; needed
  for any revisit).
- Option C — `System.ClientModel`'s public `ClientPipeline` used directly (vendor retry/pipeline machinery
  without openai-dotnet's pinned paths) — was not evaluated. It shares Option B's loader-deployment cost
  but must be named and priced in ADR-002.
