/**
 * Boundary contracts shared by settings.ts and promptenhance.ts: route names, numeric bounds,
 * defaults, and the wire adapters. Loaded first (PromptEnhanceExtension.OnPreInit).
 *
 * AUTHORITATIVE SOURCE: Frontend/contracts.ts. The committed Assets/contracts.js is tsc build
 * output — do not hand-edit it.
 */

/** The prompt-application policies, mirrored from contracts/pe-contract.json. */
let PE_REPLACE_MODES: readonly PEReplaceMode[] = ['preview', 'append', 'replace_with_restore'];

/** API route names, mirrored from contracts/pe-contract.json. */
let PE_ROUTES: PERoutes = {
    listModels: 'PromptEnhanceListModels',
    run: 'PromptEnhanceRun',
    getSettings: 'GetPromptEnhanceSettings',
    saveSettings: 'SavePromptEnhanceSettings',
    resetSettings: 'ResetPromptEnhanceSettings'
};

/** Numeric input bounds, mirrored from contracts/pe-contract.json. */
let PE_LIMITS: PELimits = {
    timeoutSeconds: { min: 1, max: 3600 },
    temperature: { min: 0, max: 2 },
    maxTokens: { min: 1 }
};

/** Settings defaults, mirrored from contracts/pe-contract.json. */
let PE_DEFAULT_SETTINGS: PESettings = {
    baseUrl: 'http://localhost:11434',
    model: '',
    timeoutSeconds: 60,
    systemPrompt: "You are a prompt enhancer for text-to-image generation. Rewrite the user's prompt into a single, richly detailed image-generation prompt. Reply with only the enhanced prompt, no preamble or explanation.",
    temperature: 0.7,
    maxTokens: 1024,
    sendSelectedImage: false,
    replaceMode: 'preview'
};

/** True for any non-null object. */
function peIsRecord(value: unknown): value is Record<string, unknown> {
    return typeof value == 'object' && value != null;
}

/** Normalizes a genericRequest error-callback value (string, Error, or arbitrary host object) into text. */
function peErrorText(err: unknown): string {
    if (err instanceof Error && err.message) {
        return err.message;
    }
    if (peIsRecord(err) && typeof err.message == 'string' && err.message) {
        return err.message;
    }
    if (typeof err == 'string' && err) {
        return err;
    }
    return 'request error';
}

/** Reads the API envelope's error text, if the response carries one. */
function peEnvelopeError(data: unknown, fallback: string): string {
    if (peIsRecord(data) && typeof data.error == 'string' && data.error) {
        return data.error;
    }
    return fallback;
}

/** Narrows an arbitrary value to a replace mode, or null. */
function peReplaceModeOf(value: unknown): PEReplaceMode | null {
    for (let mode of PE_REPLACE_MODES) {
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
function peNormalizeSettings(raw: PERawSettingsInput, current: PESettings): PESettings {
    let num = (text: string | undefined, fallback: number): number => {
        let value = Number.parseFloat(text ?? '');
        return Number.isFinite(value) ? value : fallback;
    };
    let clamp = (value: number, min: number, max: number): number => Math.min(max, Math.max(min, value));
    return {
        baseUrl: (raw.baseUrl ?? current.baseUrl).trim(),
        model: raw.model || current.model,
        timeoutSeconds: clamp(Math.round(num(raw.timeoutSeconds, current.timeoutSeconds)), PE_LIMITS.timeoutSeconds.min, PE_LIMITS.timeoutSeconds.max),
        systemPrompt: raw.systemPrompt ?? current.systemPrompt,
        temperature: clamp(num(raw.temperature, current.temperature), PE_LIMITS.temperature.min, PE_LIMITS.temperature.max),
        maxTokens: Math.max(PE_LIMITS.maxTokens.min, Math.round(num(raw.maxTokens, current.maxTokens))),
        sendSelectedImage: raw.sendSelectedImage ?? current.sendSelectedImage,
        replaceMode: peReplaceModeOf(raw.replaceMode) ?? current.replaceMode
    };
}

/** Adapter: Get/Save/ResetPromptEnhanceSettings response -> PESettingsResult. Accepts only `success: true` with an object `settings` payload; each key is copied only when it matches the schema type. */
function peAdaptSettingsResult(data: unknown): PESettingsResult {
    if (!peIsRecord(data) || data.success !== true || !peIsRecord(data.settings)) {
        return { ok: false, error: peEnvelopeError(data, 'Settings request failed.') };
    }
    let raw = data.settings;
    let settings: Partial<PESettings> = {};
    if (typeof raw.baseUrl == 'string') {
        settings.baseUrl = raw.baseUrl;
    }
    if (typeof raw.model == 'string') {
        settings.model = raw.model;
    }
    if (typeof raw.timeoutSeconds == 'number' && Number.isFinite(raw.timeoutSeconds)) {
        settings.timeoutSeconds = raw.timeoutSeconds;
    }
    if (typeof raw.systemPrompt == 'string') {
        settings.systemPrompt = raw.systemPrompt;
    }
    if (typeof raw.temperature == 'number' && Number.isFinite(raw.temperature)) {
        settings.temperature = raw.temperature;
    }
    if (typeof raw.maxTokens == 'number' && Number.isFinite(raw.maxTokens)) {
        settings.maxTokens = raw.maxTokens;
    }
    if (typeof raw.sendSelectedImage == 'boolean') {
        settings.sendSelectedImage = raw.sendSelectedImage;
    }
    let mode = peReplaceModeOf(raw.replaceMode);
    if (mode) {
        settings.replaceMode = mode;
    }
    return { ok: true, settings };
}

/** Adapter: PromptEnhanceListModels response -> PEModelsResult. An empty model list is classified as a failure. */
function peAdaptModelsResult(data: unknown): PEModelsResult {
    if (!peIsRecord(data) || data.success !== true || !Array.isArray(data.models)) {
        return { ok: false, error: peEnvelopeError(data, 'Could not fetch models.') };
    }
    let models: PEModelOption[] = [];
    for (let entry of data.models) {
        if (peIsRecord(entry) && typeof entry.id == 'string' && entry.id) {
            models.push({ id: entry.id, name: typeof entry.name == 'string' && entry.name ? entry.name : entry.id });
        }
    }
    if (models.length == 0) {
        return { ok: false, error: peEnvelopeError(data, 'Could not fetch models.') };
    }
    return { ok: true, models };
}

/** Adapter: PromptEnhanceRun response -> PEEnhanceResult. Success requires a non-empty string `response`. */
function peAdaptEnhanceResult(data: unknown): PEEnhanceResult {
    if (peIsRecord(data) && data.success === true && typeof data.response == 'string' && data.response.length > 0) {
        return { ok: true, response: data.response };
    }
    return { ok: false, error: peEnvelopeError(data, 'enhancement failed.') };
}
