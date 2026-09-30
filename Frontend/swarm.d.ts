/**
 * Ambient declarations for the SwarmUI host boundary and the PromptEnhance frontend contract types.
 *
 * The Frontend/*.ts files are authoritative; the committed Assets/*.js files SwarmUI serves are
 * deterministic tsc build output of them (see Frontend/tsconfig.json and
 * `npm run check:frontend-parity`). Never hand-edit the emitted Assets/*.js.
 *
 * These are classic global scripts (no import/export), loaded after SwarmUI's own genpage scripts
 * in registration order: contracts.js, settings.js, swarminput.js, promptenhance.js
 * (PromptEnhanceExtension.OnPreInit).
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
    sendPromptImages: boolean;
    pastGenerations: number;
    sendActiveModelContext: boolean;
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
    sendPromptImages?: boolean;
    pastGenerations?: string;
    sendActiveModelContext?: boolean;
    replaceMode?: string;
}

/** One selectable backend model, normalized from the `/v1/models` discovery route. */
interface PEModelOption {
    id: string;
    name: string;
}

/** SwarmUI generation input, keyed by the Generate tab's parameter ids, as `getInputVal(elem, true)` reads it (util.js). */
type PESwarmInput = Record<string, string | string[] | boolean>;

/** Request payload for the PromptEnhanceRun API route. `swarmInput` carries the enabled context channels' SwarmUI input; Past Generations come from the server-side history. */
interface PEEnhancePayload {
    prompt: string;
    swarmInput?: PESwarmInput;
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
    readonly pastGenerations: { readonly min: number; readonly max: number };
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

/** One registered generation parameter, as the Generate tab lists them (params.js); only the fields PromptEnhance reads. */
interface SwarmParamType {
    readonly id: string;
}

/** Every registered generation parameter, loaded with the Generate tab (params.js). */
declare var gen_param_types: SwarmParamType[];

/** Whether a parameter and all of its containing groups are enabled (params.js), the rule getGenInput uses to decide what it sends. */
declare function isParamEnabled(param: SwarmParamType): boolean;

/** The current value of an input element (util.js): a checkbox's boolean, a file input's data, a multi-select's selected values as an array when `rawLists`, else the element's value. */
declare function getInputVal(input: HTMLElement, rawLists?: boolean): string | string[] | boolean | null;

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
