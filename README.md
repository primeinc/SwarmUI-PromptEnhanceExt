# PromptEnhance

A [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) extension that adds an **Enhance Prompt** button to the Generate tab. Clicking it sends the current prompt text (and optionally the currently selected image) to a user-configured OpenAI-compatible chat endpoint, which rewrites the prompt into a more detailed one. The result is applied according to the `replaceMode` setting: shown as a preview to apply manually, appended below the original, or swapped in with a Restore button that recovers the original.

## Install

Clone this repository into your SwarmUI checkout's extensions directory, then restart SwarmUI:

```sh
cd <SwarmUI>/src/Extensions
git clone https://github.com/primeinc/SwarmUI-PromptEnhanceExt.git PromptEnhance
```

SwarmUI compiles extensions as part of its own build, so a restart (which rebuilds) is all that is needed. If the extension is listed in Swarm's extension manager (`Server` → `Extensions`), it can also be installed from there.

## Permissions

The extension registers two permission nodes, both defaulting to the `POWERUSERS` role with the `POWERFUL` safety level:

| Node | Gates |
| --- | --- |
| `promptenhance_use_backend` | Outbound calls to the configured OpenAI-compatible backend: the `PromptEnhanceListModels` and `PromptEnhanceRun` API routes. |
| `promptenhance_config` | Reading, saving, and resetting PromptEnhance settings: the `GetPromptEnhanceSettings`, `SavePromptEnhanceSettings`, and `ResetPromptEnhanceSettings` API routes. |

Users without `promptenhance_use_backend` cannot cause the server to make any network connection through this extension. Users without `promptenhance_config` cannot change where those connections go.

## Connections

This extension makes outbound web connections **only to the base URL configured in its settings** (default `http://localhost:11434`), and only when a permitted user triggers an action. Exactly three requests exist, touching only two paths under the base URL:

| Request | When | Why |
| --- | --- | --- |
| `GET {baseUrl}/v1/models` | Before every model-list or enhance call | A reachability probe so a dead backend fails fast. Only transport failures (connection refused, DNS) count as unreachable; if no response arrives within 3 seconds the probe gives up and the real call proceeds under `timeoutSeconds`. Results are cached (10s reachable, 30s unreachable). |
| `GET {baseUrl}/v1/models` | When the settings panel loads or refreshes the model list | Model discovery for the model dropdown. |
| `POST {baseUrl}/v1/chat/completions` | When the user clicks Enhance | The enhance call. Sends the configured system prompt, the user's prompt text, and — only if `sendSelectedImage` is enabled — the currently selected Generate-tab image as base64. |

No other hosts are ever contacted, and no path other than `/v1/models` and `/v1/chat/completions` is ever requested. There is no telemetry, no update check, and no analytics of any kind.

## Settings

Settings are stored per-user through SwarmUI's user-data store. The eight keys and their defaults:

| Key | Default | Meaning |
| --- | --- | --- |
| `baseUrl` | `http://localhost:11434` | Root URL of the OpenAI-compatible server. A trailing `/v1` is accepted and stripped; must be an absolute http(s) URL. |
| `model` | `""` | Model id sent in the chat request. Must be selected before enhancing. |
| `timeoutSeconds` | `60` | Per-request timeout for the model-list and enhance calls, 1 to 3600. |
| `systemPrompt` | `You are a prompt enhancer for text-to-image generation. Rewrite the user's prompt into a single, richly detailed image-generation prompt. Reply with only the enhanced prompt, no preamble or explanation.` | System message sent with every enhance call. |
| `temperature` | `0.7` | Sampling temperature, 0 to 2. |
| `maxTokens` | `1024` | `max_tokens` for the chat completion. |
| `sendSelectedImage` | `false` | When enabled, attaches the currently selected Generate-tab image to the enhance request. Requires a vision-capable model. |
| `replaceMode` | `preview` | How the enhanced prompt is applied: `preview`, `append`, or `replace_with_restore`. |

## Authentication limitation

No API key or `Authorization` header is sent with any request. Use a server that does not require authentication — for example Ollama, LM Studio, or llama.cpp's server — or place an authenticating proxy in front of a keyed service. Pointing `baseUrl` directly at a hosted API that requires a key will fail with the backend's own error message.

## Development and testing

Two layouts build and test identically; the C# project picks one automatically (`UseVendoredSwarmUI` in `PromptEnhance.csproj`):

1. **Host layout** — the checkout lives at `<SwarmUI>/src/Extensions/PromptEnhance` and imports SwarmUI's canonical `SwarmUI.extension.props`. `scripts/run-tests.sh` (requires `SWARMUI_ROOT`) reproduces every committed gate in this layout — the working tree is the build tree.
2. **Standalone workspace** — the checkout lives anywhere, with the SwarmUI host vendored at `./vendor/SwarmUI`, pinned to the same commit CI builds (`swarmui_pin` in the `justfile`, mirrored by the ref in `.github/workflows/gates.yml`). Set up with `just vendor-sync`; the vendored property group in `PromptEnhance.csproj` mirrors `SwarmUI.extension.props` verbatim and must be re-checked when the pin is bumped.

The individual gates, runnable from the extension directory in either layout:

```sh
npm ci
npm run check:frontend-parity   # Frontend/*.ts is authoritative; committed Assets/*.js must be its exact tsc output
npm run test:frontend           # compiled TypeScript tests against the emitted Assets/*.js, real jsdom
dotnet test Tests/PromptEnhance.Tests.csproj -c Debug   # C# suite against the real SwarmUI host
```

Or via [`just`](https://github.com/casey/just): `just vendor-sync` once, then `just check`.

## License

MIT — see [LICENSE](LICENSE).
