/**
 * Ambient declarations for the SwarmUI host boundary and the PromptEnhance
 * frontend contract types.
 *
 * The three Frontend/*.ts files are authoritative; the committed Assets/*.js
 * files SwarmUI serves are deterministic tsc build output of them (see
 * Frontend/tsconfig.json and `npm run check:frontend-parity`). Never hand-edit
 * the emitted Assets/*.js.
 *
 * These are global scripts (no import/export), loaded as plain <script> tags
 * via Extension.ScriptFiles in registration order:
 * contracts.js, promptenhance.js, settings.js (PromptEnhanceExtension.OnPreInit).
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
    readonly maxTokens: { readonly min: number };
}

interface PromptEnhanceNamespace {
    initialized?: boolean;
    enhancing?: boolean;
    /** The earliest pre-enhancement prompt, stashed once per replace cycle. Never overwritten while non-null; cleared only by Restore. */
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
    /** SwarmUI host helper (site.js): notifies Swarm's UI that an input changed. Absent in test harnesses. */
    triggerChangeFor?: (elem: HTMLElement) => void;
    /** SwarmUI host helper (site.js): surfaces an error banner to the user. Absent in test harnesses. */
    showError?: (message: string) => void;
}

declare var PromptEnhance: PromptEnhanceNamespace;

/** SwarmUI host API transport (site.js). Sends a session-authenticated POST to `/API/<route>`. `onError` receives whatever the host passes (string or Error-like). */
declare function genericRequest(
    route: string,
    payload: object,
    onSuccess: (data: unknown) => void,
    depth?: number,
    onError?: (err: unknown) => void
): void;
