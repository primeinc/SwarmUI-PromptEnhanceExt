# Stage 0.2 — MagicPrompt feature inventory (SPI sufficiency checklist)

Source: local clone `C:\Users\will\dev\refs\SwarmUI-MagicPromptExtension` at HEAD
`e2e50061055d4b16184efbc0b85425fc74fa5951` (2026-05-22, "Merge pull request #64 from rlerdorf/error-handler-fix";
`git status --porcelain` clean). All citations are repo-relative paths at that commit. The clone sits at a
flat refs path rather than org-scoped `refs/HartsyAI/...`; citations resolve as written.

This table is the checklist Stage 4's SPI must be measured against (every row → an SPI call or a named,
deliberate gap). Err-toward-completeness rule applied; uncertain rows are marked UNCERTAIN, not omitted.

## Read ledger

| File | Read |
|---|---|
| `README.md` (286 lines) | FULL |
| `MagicPromptExtension.cs` (123) | FULL |
| `PromptHandler.cs` (195) | FULL |
| `PromptCache.cs` (229) | FULL |
| `InstructionResolver.cs` (154) | FULL |
| `ModelListProvider.cs` (140) | FULL |
| `BackendSchema.cs` (272) | FULL |
| `WebAPI/MagicPromptAPI.cs` (402) | FULL |
| `WebAPI/LLMAPICalls.cs` (627) | FULL |
| `WebAPI/ErrorHandler.cs` (960) | FULL |
| `WebAPI/SessionSettings.cs` (316) | FULL |
| `WebAPI/Models/*.cs` (6 files, 555 total) | FULL |
| `Assets/magicprompt.js` (712) | FULL |
| `Assets/chat.js` (428) | FULL |
| `Assets/vision.js` (226) | FULL |
| `Assets/settings.js` (2523) | FULL |
| `Tabs/Text2Image/MagicPrompt.html` (530) | FULL |
| `Assets/*.css`, `Images/*` | NOT READ (presentation-only; no rows sourced from them) |

Capability vocabulary used in the last column: `chat` (non-streaming chat completion), `vision` (image
parts in the chat body), `models` (model listing), `keyed-auth` (per-provider API key headers),
`dialect` (provider-specific request/response shape), `errors` (classified error taxonomy from raw
status+body), `timeout` (per-request timeout control), `probe` (reachability fast-fail), `none`
(no LLM transport involved). **Streaming appears nowhere below**: every request body sent sets
`stream = false` or omits it (`BackendSchema.cs:125,135,183,194,207,219`; Anthropic body has no
stream field, `BackendSchema.cs:256-271`) — VALIDATED by full read of the only request-builder file.

## User-facing features

| # | Feature | Source citation | Transport capability required |
|---|---|---|---|
| U1 | "Enhance Prompt" button in Generate tab rewrites the current prompt | `Assets/magicprompt.js:313-360` (handler), `:437-440` (button) | chat, dialect, errors |
| U2 | Empty prompt → random-prompt generation first, then enhance | `Assets/magicprompt.js:324-337`; server-side instruction fallback `WebAPI/LLMAPICalls.cs:484,530` | chat |
| U3 | "Magic Vision" button captions the currently selected image into the prompt box | `Assets/magicprompt.js:368-415` | vision, chat, dialect |
| U4 | Gear mini-panel in Generate tab remaps which instruction each button uses | `Assets/magicprompt.js:447-577` | none |
| U5 | MagicPrompt tab, Chat mode: single-turn chat with the LLM (explicitly no memory) | `Assets/chat.js:184-231`; no-memory statement `README.md:213` (no history is sent: payload carries only the current input, `chat.js:206-210`) | chat |
| U6 | MagicPrompt tab, Prompt mode: enhance via chat box | `Assets/chat.js:201-210` (`prompt-mode` action) | chat |
| U7 | MagicPrompt tab, Vision mode: questions about the uploaded image; auto-injects image into chat payloads | mode radio `Tabs/Text2Image/MagicPrompt.html:96-102`; injection `Assets/magicprompt.js:90-105` | vision, chat |
| U8 | Image upload via button, drag-drop, and global paste | `Assets/vision.js:37-108` | none |
| U9 | Auto-caption on upload (toggle) | `Assets/vision.js:122-126`; caption call `:128-155` | vision, chat |
| U10 | Vision actions: Caption / Use as Init / Send To Prompt / Edit Image / Clear | `Assets/vision.js:128-219`; buttons `Tabs/Text2Image/MagicPrompt.html:45-51` | Caption: vision+chat; others: none (SwarmUI UI glue) |
| U11 | Chat message actions: clear, use-as-prompt, regenerate | `Assets/chat.js:284-351`, template `MagicPrompt.html:128-138` | regenerate: chat; others: none |
| U12 | Settings modal: 6 selectable backends (Ollama, OpenRouter, OpenAIAPI-local, OpenAI, Anthropic, Grok) for chat and vision independently | `Tabs/Text2Image/MagicPrompt.html:168-230`; save path `Assets/settings.js:149-246` | models, dialect |
| U13 | Base URL configuration for local backends only; cloud URLs pinned server-side | `Assets/settings.js:10,25-36` (`FIXED_URL_BACKENDS`); server pinning `WebAPI/SessionSettings.cs:262-266` | dialect |
| U14 | Per-backend timeout (seconds) editable in settings | `Assets/settings.js:189-214,2102-2157,2236-2285`; consumed `WebAPI/LLMAPICalls.cs:397-419` | timeout |
| U15 | Link/unlink chat and vision settings (one model for both, or split) | `Assets/settings.js:1799-1805,2363-2434`; `MagicPrompt.html:146-149` | none |
| U16 | Model dropdowns populated from the live backend | `Assets/settings.js:291-428` | models, keyed-auth |
| U17 | Instruction system: built-in instruction types chat/vision/caption/prompt (+ randomprompt, instructiongen server-side defaults) | `Assets/settings.js:1428-1470`; defaults `WebAPI/SessionSettings.cs:85-93` | none (settings persistence) |
| U18 | Custom instructions: create, edit, soft-delete (tombstone), list, per-category assignment | `Assets/settings.js:669-757` (delete tombstone `:736-739`), UI `:764-1056`; merge/delete server-side `WebAPI/SessionSettings.cs:191-226` | none |
| U19 | Feature→instruction mapping (which instruction each feature uses), with defaults | `Assets/settings.js:13-22` (`DEFAULT_FEATURE_MAPPINGS`), `:513-537`, `:607-662` | none |
| U20 | AI-assisted instruction generation ("AI Assisted Creation" in the custom-instruction modal) | `Assets/settings.js:1112-1200`; action `generate-instruction` → `WebAPI/LLMAPICalls.cs:529` | chat |
| U21 | Import/export custom instructions as JSON (single and all) | `Assets/settings.js:1284-1409` | none |
| U22 | Reset settings to defaults | `Assets/settings.js:252-285`; `WebAPI/SessionSettings.cs:300-315` | none |
| U23 | T2I sidebar param group "Magic Prompt Auto Enable": MP Use Cache, MP Generate Wildcard Seed, MP Model ID, MP Instructions | `MagicPromptExtension.cs:42-96` | models (param `GetValues`), chat (at generation) |
| U24 | `<mpprompt:...>` / `<mpprompt[Instruction]:...>` prompt-tag processing during generation (tag replaced by LLM response) | registration `MagicPromptExtension.cs:91-95`; processing `PromptHandler.cs:35-80` (regex `:13`) | chat (synchronous/blocking inside the T2I pipeline, `PromptHandler.cs:128-130`), timeout |
| U25 | `<mporiginal>` re-inserts the first original mpprompt content | `PromptHandler.cs:191-194`; README `:58` | none |
| U26 | `<mpresponse:N>` chains a previous tag's LLM response into a later tag | `PromptHandler.cs:14,146-169`; autocomplete help `Assets/magicprompt.js:702-712` | none (composition over chat results) |
| U27 | `<var:name>` substitution inside instructions from prompt-set variables (`mp_variables` via ExtraMeta) | `InstructionResolver.cs:38-70`; capture `MagicPromptExtension.cs:100-103` | none |
| U28 | LRU response cache with cross-thread request deduplication (MP Use Cache) | `PromptCache.cs:26-93` (dedup via TaskCompletionSource), LRU `:142-169` | none (sits above transport) |
| U29 | Wildcard-seed regeneration per Generate click (cache-friendly batching) | `Assets/magicprompt.js:587-608` | none |
| U30 | Seed passthrough to the LLM request for reproducibility | `PromptHandler.cs:125` → `WebAPI/LLMAPICalls.cs:435` → `BackendSchema.cs:108-110,175-185,199-209` | dialect (`seed` field; Ollama nests under `options`) |
| U31 | Tab-completion prefixes for `mpprompt`, `mporiginal`, `mpresponse`, and per-instruction `mpprompt[Name]` | `Assets/magicprompt.js:665-712` | none |
| U32 | Ollama "Unload Models After Response" toggle (keep_alive=0) | `Assets/chat.js:120-128`; `Assets/magicprompt.js:169`; wire field `BackendSchema.cs:126,136` | dialect (Ollama `keep_alive`) |
| U33 | Resizable vision/chat split with persisted width | `Assets/magicprompt.js:236-304,643-655` | none |
| U34 | UNCERTAIN — vision auto-inject in chat mode reads `window.visionHandler.getCurrentImage()` / `.currentMediaType`, but no file in the repo assigns `window.visionHandler` (VALIDATED_EMPTY: `rg -F "visionHandler"` → only the 4 read sites `Assets/magicprompt.js:97,167,637-638`; positive control `window.visionTab` hits `Assets/vision.js:226`). The optional-chained reads make U7's injection path and the per-image `mediaType` silently fall back (`"image/jpeg"`, `magicprompt.js:167`) | vision (if intended path were live) |

## Backend surfaces

| # | Surface | Source citation | Transport capability required |
|---|---|---|---|
| B1 | API route `MagicPromptPhoneHome` (chat + vision + prompt actions, one endpoint) | registration `WebAPI/MagicPromptAPI.cs:32`; implementation `WebAPI/LLMAPICalls.cs:431-626` | chat, vision, dialect, keyed-auth, timeout, errors |
| B2 | API route `GetMagicPromptModels` (chat + vision model lists in one response) | `WebAPI/MagicPromptAPI.cs:36`; `WebAPI/LLMAPICalls.cs:90-141` | models, keyed-auth, probe, timeout |
| B3 | API routes `GetMagicPromptSettings` / `SaveMagicPromptSettings` / `ResetMagicPromptSettings` | `WebAPI/MagicPromptAPI.cs:33-35`; `WebAPI/SessionSettings.cs:97-315` | none (persistence) |
| B4 | Settings persistence via `Program.Sessions.GenericSharedUser.SaveGenericData("magicprompt","config",…)` — one shared config for all users | `WebAPI/SessionSettings.cs:9-10,101,281,306` | none (note: instance-global, not per-user) |
| B5 | Deep-merge save semantics preserving unknown keys, backend sub-objects, custom-instruction tombstones; strips legacy `apikey` fields | `WebAPI/SessionSettings.cs:146-297` (apikey strip `:268-280`) | none |
| B6 | Permission group "MagicPrompt" with 5 permissions (phone home, save/read/reset config, get models), default POWERUSERS | `WebAPI/MagicPromptAPI.cs:16-24` | none (host auth surface) |
| B7 | API-key registration in SwarmUI's Users tab for 5 key types: `openai_api`, `anthropic_api`, `openrouter_api`, `openaiapi_local`, `grok_api` (+ accepted-type registration) | `WebAPI/MagicPromptAPI.cs:38-53`, idempotent register `:57-75` | keyed-auth (host-native key storage) |
| B8 | Per-request key retrieval: session user's key, falling back to `GenericSharedUser` key | `WebAPI/LLMAPICalls.cs:311-361` | keyed-auth |
| B9 | Auth header dialects: `Authorization: Bearer` (OpenAI/Grok/OpenRouter/OpenAIAPI-local/Ollama-optional); Anthropic `x-api-key` + `anthropic-version: 2023-06-01`; OpenRouter extra `HTTP-Referer`/`X-Title` | `WebAPI/LLMAPICalls.cs:308-362` | keyed-auth, dialect |
| B10 | Endpoint routing per backend and per purpose (chat/vision/models), from stored per-backend endpoint maps; Anthropic models path hardcoded `/v1/models`; Ollama models = `/api/tags` | `WebAPI/LLMAPICalls.cs:220-274`; defaults `WebAPI/SessionSettings.cs:13-77` | dialect (non-OpenAI URL paths: Ollama `/api/chat`+`/api/tags`, Anthropic `/v1/messages`) |
| B11 | Request-body dialect, Ollama: `messages`, `stream:false`, `keep_alive`, `options{temperature,top_p[,seed]}`, vision via `images` array of raw base64 | `BackendSchema.cs:100-139` | dialect, vision |
| B12 | Request-body dialect, OpenAI-compatible (OpenAI/OpenAIAPI/OpenRouter/Grok): `messages`, `max_tokens:1000` (hardcoded), `temperature`, `top_p` (text-only default path), `stream:false`, optional `seed`; vision via `image_url` data-URI parts (WebP, or PNG for Grok) | `BackendSchema.cs:142-221` (Grok PNG preference `:51`) | dialect (**`max_tokens`, not `max_completion_tokens`**), vision |
| B13 | Request-body dialect, Anthropic: top-level `system`, `max_tokens:1024` (hardcoded), PNG-only images as `source{type:base64,media_type,data}` | `BackendSchema.cs:224-272` | dialect, vision |
| B14 | Image preprocessing before transport: downscale to max 256px, transcode to WEBP/PNG/JPG per provider, quality 40-60, via SwarmUI ImageSharp utilities | `BackendSchema.cs:58-97` | none (pre-transport transform) |
| B15 | Response parsing per dialect: OpenAI/Grok (`choices[0].message.content`), Anthropic (`content[0].text`), Ollama (`message.content`), OpenAIAPI, OpenRouter (string-or-object content with custom JsonConverter) | `WebAPI/MagicPromptAPI.cs:79-214`; converter `WebAPI/Models/OpenRouterModels.cs:121-153` | dialect |
| B16 | `finish_reason == "length"` detection surfaces a TokenLimit error (OpenRouter path only; typed `FinishReason`/`NativeFinishReason` fields exist only on the OpenRouter model) | `WebAPI/MagicPromptAPI.cs:156-166`; `WebAPI/Models/OpenRouterModels.cs:96-102` (OpenAI model has the field but it is unchecked, `OpenAIModels.cs:51-52`) | dialect, errors (finish_reason visibility) |
| B17 | Model-list normalization to `id///displayName`, Anthropic friendly names + version extraction | `ModelListProvider.cs:49`; `WebAPI/MagicPromptAPI.cs:216-377` | models |
| B18 | Model/instruction lists for T2I params served from a 10s TTL cache with fire-and-forget background refresh (never blocks UI thread) | `ModelListProvider.cs:9-139` | models |
| B19 | Error taxonomy: 13 `ErrorType` values; per-provider error parsers (OpenAI, OpenRouter incl. moderation metadata, Anthropic, Ollama); HTTP-status→type maps with provider overrides (e.g. Ollama 401→Connectivity, OpenRouter 403→ContentModeration, Ollama 404→ModelNotFound); templated user-facing messages with causes/solutions | `WebAPI/ErrorHandler.cs:14-42` (enum), `:183-604` (templates+maps), `:647-843` (parsers) | errors |
| B20 | `MaxTokensParameter` error class exists specifically for models rejecting `max_tokens` ("The system will attempt to automatically adjust parameters for you" — template text only; no adjustment code exists in the repo: VALIDATED_EMPTY, `rg -i "max_completion_tokens"` → only `OpenRouterModels.cs:89` response field; positive control `max_tokens` → 14 hits) | `WebAPI/ErrorHandler.cs:227-238` | errors, dialect |
| B21 | Reachability probe for local backends (ollama, openaiapi): GET base URL, 3s timeout, cache 10s success / 30s failure; cloud backends skip | `WebAPI/LLMAPICalls.cs:19-86` | probe |
| B22 | Per-backend request timeouts with defaults (local 120s, cloud 60s / models 20s) driven by settings | `WebAPI/LLMAPICalls.cs:172-175,372-419,562-565` | timeout |
| B23 | Shared `HttpClient` from SwarmUI's `NetworkBackendUtils.MakeHttpClient()` with infinite global timeout, per-request CTS | `WebAPI/LLMAPICalls.cs:17-34` | none (transport internals) |
| B24 | Blocking bridge: T2I pipeline calls the async route synchronously (`GetAwaiter().GetResult()`) inside `LateSpecialParameterHandlers` | `PromptHandler.cs:128-130`; hook `MagicPromptExtension.cs:115-122` | chat callable from sync context |
| B25 | Extension lifecycle: assets registered `OnPreInit`, API+params registered `OnInit` | `MagicPromptExtension.cs:18-28` | none (host integration) |
| B26 | Errors from LLM calls during generation become `SwarmReadableErrorException` (user-visible, generation-aborting) | `PromptHandler.cs:105-109` | errors |

## Capability rollup (what the SPI must offer to absorb every transport-touching row)

- Non-streaming chat completion with optional system instruction, user text, and optional image parts (U1-U3, U5-U7, U9, U20, U24, B1, B11-B13).
- Callable from a synchronous host pipeline context without deadlock (B24).
- Model listing (U16, U23, B2, B10, B17-B18) — including non-OpenAI paths (Ollama `/api/tags`) for full MagicPrompt parity (B10).
- Keyed auth sourced from SwarmUI's per-user key store with shared-user fallback, with per-provider header dialects (B7-B9).
- Provider dialect control over: request path (B10), body field names and hardcoded token caps — `max_tokens` today, never `max_completion_tokens` (B12-B13, B20) — `seed` placement (U30), `keep_alive` (U32), image encoding rules per provider (B11-B14).
- Classified error taxonomy derived from HTTP status + raw body, with provider-specific parsers and `finish_reason` visibility (B16, B19-B20, B26).
- Per-request timeout control (U14, B22) and local-backend reachability probing (B21).
- **Not required by any row: streaming** (every request is `stream:false`); the SPI's streaming-shaped signature (PLATFORM-PLAN Stage 4) is forward-looking, not MagicPrompt-parity-driven.
