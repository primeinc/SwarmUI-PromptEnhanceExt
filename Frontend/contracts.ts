/**
 * Boundary contracts shared by settings.ts and promptenhance.ts: route names, numeric bounds,
 * defaults, and the wire adapters. Loaded first (PromptEnhanceExtension.OnPreInit).
 *
 * AUTHORITATIVE SOURCE: Frontend/contracts.ts. The committed Assets/contracts.js is tsc build
 * output — do not hand-edit it.
 */

/** The prompt-application policies, mirrored from contracts/pe-contract.json. */
const PE_REPLACE_MODES: readonly PEReplaceMode[] = ['preview', 'append', 'replace_with_restore'];

/** API route names, mirrored from contracts/pe-contract.json. */
globalThis.PE_ROUTES = {
    listModels: 'PromptEnhanceListModels',
    run: 'PromptEnhanceRun',
    getSettings: 'GetPromptEnhanceSettings',
    saveSettings: 'SavePromptEnhanceSettings',
    resetSettings: 'ResetPromptEnhanceSettings'
};

/** Key type of the backend API key in SwarmUI's User → API Keys table, mirrored from contracts/pe-contract.json. */
globalThis.PE_API_KEY_TYPE = 'promptenhance_api';

/** Numeric input bounds, mirrored from contracts/pe-contract.json. */
const PE_LIMITS: PELimits = {
    timeoutSeconds: { min: 1, max: 3600 },
    temperature: { min: 0, max: 2 },
    maxTokens: { min: 1, max: 2_147_483_647 },
    pastGenerations: { min: 0, max: 10 }
};

/** Settings defaults, mirrored from contracts/pe-contract.json. */
globalThis.PE_DEFAULT_SETTINGS = {
    baseUrl: 'http://localhost:11434',
    model: '',
    timeoutSeconds: 60,
    systemPrompt: "You are a prompt enhancer for text-to-image generation. Rewrite the user's prompt into a single, richly detailed image-generation prompt. Reply with only the enhanced prompt, no preamble or explanation.",
    temperature: 0.7,
    maxTokens: 1024,
    sendPromptImages: false,
    pastGenerations: 0,
    sendActiveModelContext: false,
    replaceMode: 'preview'
};

/** True for any non-null object. */
function peIsRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null;
}

/** A finite number, or undefined. */
function peFiniteNumber(value: unknown): number | undefined {
    return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

/** Normalizes a genericRequest error-callback value (string, Error, or arbitrary host object) into text. */
globalThis.peErrorText = (err: unknown): string => {
    if (err instanceof Error && err.message) {
        return err.message;
    }
    if (peIsRecord(err)) {
        const { message } = err;
        if (typeof message === 'string' && message) {
            return message;
        }
    }
    if (typeof err === 'string' && err) {
        return err;
    }
    return 'request error';
};

/** Narrows an arbitrary value to a replace mode, or null. */
function peReplaceModeOf(value: unknown): PEReplaceMode | null {
    for (const mode of PE_REPLACE_MODES) {
        if (value === mode) {
            return mode;
        }
    }
    return null;
}

/**
 * Builds a complete, in-bounds settings value from raw form input over `current`: numeric fields
 * parse and clamp to PE_LIMITS, unparseable numbers and unknown modes keep the current value, and
 * an empty model keeps the current model.
 */
globalThis.peNormalizeSettings = (raw: PERawSettingsInput, current: PESettings): PESettings => {
    const num = (text: string | undefined, fallback: number): number => {
        const value = Number.parseFloat(text ?? '');
        return Number.isFinite(value) ? value : fallback;
    };
    const clamp = (value: number, min: number, max: number): number => Math.min(max, Math.max(min, value));
    return {
        baseUrl: (raw.baseUrl ?? current.baseUrl).trim(),
        model: raw.model || current.model,
        timeoutSeconds: clamp(Math.round(num(raw.timeoutSeconds, current.timeoutSeconds)), PE_LIMITS.timeoutSeconds.min, PE_LIMITS.timeoutSeconds.max),
        systemPrompt: raw.systemPrompt ?? current.systemPrompt,
        temperature: clamp(num(raw.temperature, current.temperature), PE_LIMITS.temperature.min, PE_LIMITS.temperature.max),
        maxTokens: clamp(Math.round(num(raw.maxTokens, current.maxTokens)), PE_LIMITS.maxTokens.min, PE_LIMITS.maxTokens.max),
        sendPromptImages: raw.sendPromptImages ?? current.sendPromptImages,
        pastGenerations: clamp(Math.round(num(raw.pastGenerations, current.pastGenerations)), PE_LIMITS.pastGenerations.min, PE_LIMITS.pastGenerations.max),
        sendActiveModelContext: raw.sendActiveModelContext ?? current.sendActiveModelContext,
        replaceMode: peReplaceModeOf(raw.replaceMode) ?? current.replaceMode
    };
};

/**
 * The adapters below see only responses SwarmUI's genericRequest passed to the success callback;
 * any response carrying `error` goes to the error callback instead (site.js). A failed result here
 * therefore means a success response of the wrong shape.
 */

/** Adapter: Get/Save/ResetPromptEnhanceSettings response -> PESettingsResult. Accepts only `success: true` with an object `settings` payload; each key is copied only when it matches the schema type. */
globalThis.peAdaptSettingsResult = (data: unknown): PESettingsResult => {
    if (!peIsRecord(data)) {
        return { ok: false, error: 'The server returned settings in an unexpected shape.' };
    }
    const { success, settings: raw } = data;
    if (success !== true || !peIsRecord(raw)) {
        return { ok: false, error: 'The server returned settings in an unexpected shape.' };
    }
    const { baseUrl, model, timeoutSeconds, systemPrompt, temperature, maxTokens, sendPromptImages, pastGenerations, sendActiveModelContext, replaceMode } = raw;
    const settings: Partial<PESettings> = {};
    if (typeof baseUrl === 'string') {
        settings.baseUrl = baseUrl;
    }
    if (typeof model === 'string') {
        settings.model = model;
    }
    const timeout = peFiniteNumber(timeoutSeconds);
    if (timeout !== undefined) {
        settings.timeoutSeconds = timeout;
    }
    if (typeof systemPrompt === 'string') {
        settings.systemPrompt = systemPrompt;
    }
    const temp = peFiniteNumber(temperature);
    if (temp !== undefined) {
        settings.temperature = temp;
    }
    const tokens = peFiniteNumber(maxTokens);
    if (tokens !== undefined) {
        settings.maxTokens = tokens;
    }
    if (typeof sendPromptImages === 'boolean') {
        settings.sendPromptImages = sendPromptImages;
    }
    if (Number.isInteger(pastGenerations) && typeof pastGenerations === 'number' && pastGenerations >= PE_LIMITS.pastGenerations.min && pastGenerations <= PE_LIMITS.pastGenerations.max) {
        settings.pastGenerations = pastGenerations;
    }
    if (typeof sendActiveModelContext === 'boolean') {
        settings.sendActiveModelContext = sendActiveModelContext;
    }
    const mode = peReplaceModeOf(replaceMode);
    if (mode) {
        settings.replaceMode = mode;
    }
    return { ok: true, settings };
};

/** Adapter: PromptEnhanceListModels response -> PEModelsResult. An empty model list is classified as a failure. */
globalThis.peAdaptModelsResult = (data: unknown): PEModelsResult => {
    if (!peIsRecord(data)) {
        return { ok: false, error: 'The server returned the model list in an unexpected shape.' };
    }
    const { success, models: entries } = data;
    if (success !== true || !Array.isArray(entries)) {
        return { ok: false, error: 'The server returned the model list in an unexpected shape.' };
    }
    const models: PEModelOption[] = [];
    for (const entry of entries) {
        if (peIsRecord(entry)) {
            const { id, name } = entry;
            if (typeof id === 'string' && id) {
                models.push({ id, name: typeof name === 'string' && name ? name : id });
            }
        }
    }
    if (models.length === 0) {
        return { ok: false, error: 'The backend lists no models.' };
    }
    return { ok: true, models };
};

/** Adapter: PromptEnhanceRun response -> PEEnhanceResult. Success requires a non-empty string `response`. */
globalThis.peAdaptEnhanceResult = (data: unknown): PEEnhanceResult => {
    if (peIsRecord(data)) {
        const { success, response } = data;
        if (success === true && typeof response === 'string' && response.length > 0) {
            return { ok: true, response };
        }
    }
    return { ok: false, error: 'The server returned the enhancement in an unexpected shape.' };
};
