/**
 * Ambient declarations for the SwarmUI host boundary and the PromptEnhance
 * frontend contract types.
 *
 * The three Frontend/*.ts files are authoritative; the committed Assets/*.js
 * files SwarmUI serves are deterministic tsc build output of them (see
 * Frontend/tsconfig.json and `npm run check:frontend-parity`). Never hand-edit
 * the emitted Assets/*.js.
 *
 * These are global scripts (no import/export): SwarmUI loads extension
 * frontend files as plain <script> tags via Extension.ScriptFiles, so all
 * three files share one global scope, in registration order:
 * contracts.js, promptenhance.js, settings.js (PromptEnhanceExtension.OnPreInit).
 */

/** Prompt-application policy selector. `preview` is the only non-mutating mode. */
type PEReplaceMode = 'preview' | 'append' | 'replace_with_restore';

/**
 * The single settings schema, mirrored verbatim by the server-side
 * `SessionSettings.Defaults` (WebAPI/SessionSettings.cs). The server is the
 * source of truth; this type is the client-side view of the same eight keys.
 */
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

/**
 * Discriminated results produced by the wire adapters in contracts.ts.
 * Every backend response crosses the boundary through one of these; loosely
 * shaped JSON never leaks past the adapter that classified it.
 */
type PESettingsResult = { ok: true; settings: Partial<PESettings> } | { ok: false; error: string };
type PEModelsResult = { ok: true; models: PEModelOption[] } | { ok: false; error: string };
type PEEnhanceResult = { ok: true; response: string } | { ok: false; error: string };

/**
 * The extension's single frontend namespace. All cross-file state lives here;
 * nothing else is added to `window` except pe-prefixed top-level functions.
 * Fields are optional because each script bootstraps the namespace
 * defensively and script order is a host concern, not a code assumption.
 */
/** API route names, mirrored from contracts/pe-contract.json (see contracts.ts). */
interface PERoutes {
    readonly listModels: string;
    readonly run: string;
    readonly getSettings: string;
    readonly saveSettings: string;
    readonly resetSettings: string;
}

/** Numeric input bounds, mirrored from contracts/pe-contract.json (see contracts.ts). */
interface PELimits {
    readonly timeoutSeconds: { readonly min: number; readonly max: number };
    readonly temperature: { readonly min: number; readonly max: number };
    readonly maxTokens: { readonly min: number };
}

interface PromptEnhanceNamespace {
    initialized?: boolean;
    enhancing?: boolean;
    /**
     * The earliest pre-enhancement prompt, stashed once per replace cycle.
     * Invariant: never overwritten while non-null, so Restore always returns
     * the TRUE original even after repeated enhances. Cleared only by Restore.
     */
    lastOriginal?: string | null;
    pending?: PEPending | null;
    settings?: Partial<PESettings>;
    REPLACE_MODES?: readonly PEReplaceMode[];
    ROUTES?: PERoutes;
    LIMITS?: PELimits;
    loadSettings?: () => Promise<void>;
    fetchModels?: () => Promise<void>;
    openSettingsPanel?: () => void;
}

interface Window {
    PromptEnhance?: PromptEnhanceNamespace;
    /** SwarmUI host helper (site.js): notifies Swarm's UI that an input changed. Optional: absent in test harnesses. */
    triggerChangeFor?: (elem: HTMLElement) => void;
    /** SwarmUI host helper (site.js): surfaces an error banner to the user. Optional: absent in test harnesses. */
    showError?: (message: string) => void;
}

declare var PromptEnhance: PromptEnhanceNamespace;

/**
 * SwarmUI host API transport (site.js). Sends a session-authenticated POST to
 * `/API/<route>`. The response JSON is untyped at this boundary by design —
 * every caller must normalize `data` through a contracts.ts adapter before use.
 * `onError` receives whatever the host passes (string or Error-like).
 */
declare function genericRequest(
    route: string,
    payload: object,
    onSuccess: (data: unknown) => void,
    depth?: number,
    onError?: (err: unknown) => void
): void;
