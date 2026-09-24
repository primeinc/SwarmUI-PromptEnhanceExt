/**
 * Ambient declarations for the SwarmUI host boundary and the PromptEnhance frontend contract types.
 *
 * The Frontend/*.ts files are authoritative; the committed Assets/*.js files SwarmUI serves are
 * deterministic tsc build output of them (see Frontend/tsconfig.json and
 * `npm run check:frontend-parity`). Never hand-edit the emitted Assets/*.js.
 *
 * These are classic global scripts (no import/export), loaded after SwarmUI's own genpage scripts
 * in registration order: contracts.js, settings.js, promptenhance.js (PromptEnhanceExtension.OnPreInit).
 */

/** Prompt-application policy selector. */
type PEReplaceMode = 'preview' | 'append' | 'replace_with_restore';

/** The settings schema, mirrored by the server-side `SessionSettings.Defaults` (WebAPI/SessionSettings.cs). */
interface PESettings {
    baseUrl: string;
    model: string;
    timeoutSeconds: number;
    systemPrompt: string;
    temperature: number;
    maxTokens: number;
    sendSelectedImage: boolean;
    replaceMode: PEReplaceMode;
}

/** Raw settings form input: text as typed, before parsing and clamping by `peNormalizeSettings`. */
interface PERawSettingsInput {
    baseUrl?: string;
    model?: string;
    timeoutSeconds?: string;
    systemPrompt?: string;
    temperature?: string;
    maxTokens?: string;
    sendSelectedImage?: boolean;
    replaceMode?: string;
}

/** One selectable backend model, normalized from the `/v1/models` discovery route. */
interface PEModelOption {
    id: string;
    name: string;
}

/** Base64 image part collected from the Generate-tab selected image. */
interface PEImagePart {
    data: string;
    mediaType: string;
}

/** Wire shape of one media entry in a PromptEnhanceRun request. */
interface PEMediaEntry {
    type: 'base64';
    data: string;
    mediaType: string;
}

/** Request payload for the PromptEnhanceRun API route. */
interface PEEnhancePayload {
    prompt: string;
    media?: PEMediaEntry[];
}

/** A preview-mode enhancement awaiting explicit Apply/Cancel. */
interface PEPending {
    original: string;
    enhanced: string;
}

/** Discriminated results produced by the wire adapters in contracts.ts. */
type PESettingsResult = { ok: true; settings: Partial<PESettings> } | { ok: false; error: string };
type PEModelsResult = { ok: true; models: PEModelOption[] } | { ok: false; error: string };
type PEEnhanceResult = { ok: true; response: string } | { ok: false; error: string };

/** API route names, mirrored from contracts/pe-contract.json (see contracts.ts). */
interface PERoutes {
    readonly listModels: 'PromptEnhanceListModels';
    readonly run: 'PromptEnhanceRun';
    readonly getSettings: 'GetPromptEnhanceSettings';
    readonly saveSettings: 'SavePromptEnhanceSettings';
    readonly resetSettings: 'ResetPromptEnhanceSettings';
}

/** Numeric input bounds, mirrored from contracts/pe-contract.json (see contracts.ts). */
interface PELimits {
    readonly timeoutSeconds: { readonly min: number; readonly max: number };
    readonly temperature: { readonly min: number; readonly max: number };
    readonly maxTokens: { readonly min: number; readonly max: number };
}

/** SwarmUI Generate-tab layout singleton (js/genpage/gentab/layout.js). */
interface SwarmGenTabLayout {
    /** Re-offsets `#alt_prompt_region` from the heights of both prompt textareas and `#alt_prompt_extra_area`, then reflows the tab. */
    altPromptSizeHandle(): void;
}

declare var genTabLayout: SwarmGenTabLayout;

/** SwarmUI startup hooks (site.js), fired by genpage main.js once the session exists and the Generate tab is initialized. */
declare var sessionReadyCallbacks: (() => void)[];

/** SwarmUI API transport (site.js). Sends a session-authenticated POST to `/API/<route>`. `onError` receives whatever the host passes (string or Error-like). */
declare function genericRequest(route: string, payload: object, onSuccess: (data: unknown) => void, depth?: number, onError?: (err: unknown) => void): void;

/** SwarmUI error banner (site.js). */
declare function showError(message: string): void;

/** Fires `input` and `change` for a programmatically edited control (site.js). */
declare function triggerChangeFor(elem: HTMLElement): void;

/** Reads an image src (including SwarmUI `inputs/` paths and Civitai URLs) into a data URL; the callback receives null on failure (util.js). */
declare function imageToData(src: string, callback: (dataUrl: string | null) => void, resize256?: boolean): void;

/** Returns the element with the id, throwing when it is absent (util.js). */
declare function getRequiredElementById(id: string): HTMLElement;

/** Creates a div with the id, classes, and inner HTML (util.js). `html` is assigned as innerHTML: trusted markup only. */
declare function createDiv(id: string | null, classes: string | null, html?: string | null): HTMLDivElement;

/** Opening markup of a Bootstrap modal with a header title (site.js). Close it with `modalFooter()`. */
declare function modalHeader(id: string, title: string): string;

/** Closing markup for `modalHeader` (site.js). */
declare function modalFooter(): string;

/** Hidden info popover for the input with the id (site.js); pairs with a `make*Input` built with `popover_button`. */
declare function makeGenericPopover(id: string, name: string, type: string, description: string, example: string): string;

/** SwarmUI parameter-style text input (site.js). `format` is 'normal', 'big', 'prompt', or 'secret'. */
declare function makeTextInput(featureid: string | null, id: string, paramid: string, name: string, description: string, value: string, format: string, placeholder: string, toggles?: boolean, genPopover?: boolean, popover_button?: boolean): string;

/** SwarmUI parameter-style number input (site.js). */
declare function makeNumberInput(featureid: string | null, id: string, paramid: string, name: string, description: string, value: number, min: number, max: number, step?: number, format?: string, toggles?: boolean, popover_button?: boolean): string;

/** SwarmUI parameter-style dropdown (site.js). `alt_names` are display labels paired with `values`; they show only while `reparse_alt_names` is true. */
declare function makeDropdownInput(featureid: string | null, id: string, paramid: string, name: string, description: string, values: string[], defaultVal: string, toggles?: boolean, popover_button?: boolean, alt_names?: string[] | null, reparse_alt_names?: boolean): string;

/** SwarmUI parameter-style checkbox (site.js). */
declare function makeCheckboxInput(featureid: string | null, id: string, paramid: string, name: string, description: string, value: boolean, toggles?: boolean, genPopover?: boolean, popover_button?: boolean): string;

/** The jQuery Bootstrap modal plugin SwarmUI loads on every page (_Layout.cshtml). */
declare function $(elem: HTMLElement): { modal(action: 'show' | 'hide'): void };
