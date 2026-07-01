# PromptEnhance — a SwarmUI extension

A minimal SwarmUI extension that enhances the current Generate-tab prompt through a configurable, OpenAI-compatible chat endpoint.

It does exactly one thing, and does it safely:

1. Reads the prompt in the Generate tab.
2. Optionally attaches the currently selected image as multimodal context (opt-in).
3. Sends it to a configurable OpenAI-compatible backend (`/v1/chat/completions`).
4. Applies the enhanced result through a **reversible** policy — your original prompt is never destroyed without a way back.

No chat window, no provider matrix, no cloud-key management, no prompt-tag processing. It is meant to be small enough to read end-to-end and copy as a pattern for a well-behaved SwarmUI extension.

## Network connection disclosure

This extension makes **one kind of outbound connection**: HTTP requests to the OpenAI-compatible backend you configure in its settings (`Base URL`). It calls two endpoints on that server:

- `GET {baseUrl}/v1/models` — to list available models for the settings dropdown.
- `POST {baseUrl}/v1/chat/completions` — to enhance a prompt.

It contacts nothing else — no telemetry, no analytics, no third-party services. If you never open the settings and never click Enhance, it makes no network calls at all. The intended target is a **local** OpenAI-compatible server (see the table below); no API key is sent, so point it at a server that does not require authentication.

## Requirements

- [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI).
- A running OpenAI-compatible server reachable from SwarmUI (local is the intended use). Examples below.

## Installation

**From the SwarmUI extensions list:** open `Server` → `Extensions`, find it, click Install, and restart when prompted.

**Manual:** clone this repo into `SwarmUI/src/Extensions/`, then run the SwarmUI `update` script (or launch with a `launch-dev` script) so it recompiles. Restart SwarmUI.

## Usage

In the Generate tab, above the prompt box, you get two controls:

- **✨ Enhance Prompt** — sends the current prompt to your backend and applies the result per your chosen Apply Mode.
- **⚙️ (Settings)** — opens the compact settings panel.

A loading indicator shows while a request is in flight and always clears — on success, failure, timeout, invalid response, or a refused connection. A backend that is down, slow, or misconfigured produces a clear, categorized error and never wedges the tab.

## Settings

All configuration lives in one place (the settings panel), backed by a single **per-user** stored config object — each user's Base URL, model, and prompt settings are private to their own account:

| Setting | Meaning |
|---|---|
| **Base URL** | Your OpenAI-compatible server. A server root (`http://localhost:11434`) or a `/v1` URL both work — it is normalized. |
| **Model** | The model used for enhancement. Populated from `/v1/models`; refreshable. |
| **System Prompt** | The instruction that shapes how the model rewrites your prompt. |
| **Temperature** | Sampling temperature. |
| **Max Tokens** | Maximum length of the enhanced result. |
| **Timeout (s)** | Per-request timeout. |
| **Send selected image** | When on, the currently selected image is attached as multimodal context (requires a vision-capable model). If the model can't accept it, you get a clear "unsupported image" error — it is never silently dropped. |
| **Apply Mode** | How the result is applied (see below). |

Settings save with a visible confirmation, and a failed save says so — it never fails silently. If the backend is unreachable, the model list shows an inline error but the rest of the panel stays usable.

### Apply Modes (prompt recovery)

The enhanced text is never written over your prompt without a recovery path:

- **Preview** — shows the enhanced text with **Apply / Cancel**. Nothing changes until you click Apply (and Apply still leaves a Restore button).
- **Append** — appends the enhanced text below your original with a separator, so the original stays intact inline.
- **Replace (with Restore)** — replaces the prompt and shows a **Restore Previous Prompt** button that puts your original back.

## Error categories

Failures are classified and surfaced clearly rather than as raw stack traces: server unavailable, timeout, invalid base URL, model missing, unsupported image input, invalid response shape, authentication (the backend demanded credentials this extension does not send — point it at an unauthenticated/local server), and HTTP errors (with a response-body excerpt).

## Known working OpenAI-compatible backends

URLs are common defaults — adjust to your setup. Any server that implements `/v1/models` and `/v1/chat/completions` should work.

| Name | Base URL | Notes |
|---|---|---|
| Ollama | `http://localhost:11434` | Exposes OpenAI-compatible `/v1` endpoints. |
| LM Studio | `http://localhost:1234` | Enable the local server. |
| llama.cpp (`llama-server`) | `http://localhost:8080` | Built-in OpenAI-compatible server. |
| KoboldCPP | `http://localhost:5001` | Start with `--api`. |
| Oobabooga (text-generation-webui) | `http://127.0.0.1:5000` | Enable the OpenAI API extension. |

## Troubleshooting

- **"Cannot reach the LLM backend"** — confirm the server is running and the Base URL is correct.
- **"No usable model"** — pick a model in settings and confirm the backend has one loaded.
- **"…not valid OpenAI-style chat JSON"** — the Base URL is probably not pointing at an OpenAI-compatible server.
- Check the SwarmUI logs (entries are prefixed `[PromptEnhance]`).

## License & acknowledgment

MIT — see [LICENSE](LICENSE). Originally forked from [HartsyAI/SwarmUI-MagicPromptExtension](https://github.com/HartsyAI/SwarmUI-MagicPromptExtension) (MIT, © Hartsy), then rebuilt from first principles into this minimal reference extension. Built for [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI) — thanks to [mcmonkey](https://github.com/mcmonkey4eva) for the platform and the extension APIs.
