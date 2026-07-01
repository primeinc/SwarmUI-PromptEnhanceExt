"use strict";
/**
 * Boundary contracts shared by settings.ts and promptenhance.ts.
 *
 * This file owns every adapter that normalizes loosely shaped input crossing
 * an ownership boundary: SwarmUI API responses, transport errors, and the
 * settings schema. It is registered FIRST in Extension.ScriptFiles so its
 * declarations exist before either feature script runs a handler.
 *
 * AUTHORITATIVE SOURCE: Frontend/contracts.ts. The committed Assets/contracts.js
 * is tsc build output — do not hand-edit it.
 */
window.PromptEnhance = window.PromptEnhance || {};
PromptEnhance.REPLACE_MODES = ['preview', 'append', 'replace_with_restore'];
/**
 * Client-side mirror of SessionSettings.Defaults (WebAPI/SessionSettings.cs).
 * SettingsDefaultsParityTests pins the systemPrompt text verbatim against the
 * server default; if you change one side, the committed gate fails.
 */
const PE_DEFAULT_SETTINGS = {
    baseUrl: 'http://localhost:11434',
    model: '',
    timeoutSeconds: 60,
    systemPrompt: "You are a prompt enhancer for text-to-image generation. Rewrite the user's prompt into a single, richly detailed image-generation prompt. Reply with only the enhanced prompt, no preamble or explanation.",
    temperature: 0.7,
    maxTokens: 1024,
    sendSelectedImage: false,
    replaceMode: 'preview'
};
PromptEnhance.settings = Object.assign({}, PE_DEFAULT_SETTINGS, PromptEnhance.settings);
/** The full settings view: server-loaded values over defaults, never partial. */
function peEffectiveSettings() {
    return Object.assign({}, PE_DEFAULT_SETTINGS, PromptEnhance.settings);
}
function peIsRecord(value) {
    return typeof value === 'object' && value !== null;
}
/**
 * Normalizes a genericRequest error-callback value (string, Error, or
 * arbitrary host object) into user-presentable text. Nothing is dropped:
 * unrecognized shapes fall back to a stable generic message.
 */
function peErrorText(err) {
    if (err instanceof Error && err.message) {
        return err.message;
    }
    if (peIsRecord(err) && typeof err.message === 'string' && err.message) {
        return err.message;
    }
    if (typeof err === 'string' && err) {
        return err;
    }
    return 'request error';
}
/** Reads the API envelope's error text, if the response carries one. */
function peEnvelopeError(data, fallback) {
    if (peIsRecord(data) && typeof data.error === 'string' && data.error) {
        return data.error;
    }
    return fallback;
}
/**
 * Adapter: Get/Save/ResetPromptEnhanceSettings response -> PESettingsResult.
 * Accepts only `success: true` with an object `settings` payload; every key is
 * copied only when it matches the schema type, so a corrupted store can never
 * leak a wrongly typed value into the client settings state.
 */
function peAdaptSettingsResult(data) {
    if (!peIsRecord(data) || data.success !== true || !peIsRecord(data.settings)) {
        return { ok: false, error: peEnvelopeError(data, 'Settings request failed.') };
    }
    const raw = data.settings;
    const settings = {};
    if (typeof raw.baseUrl === 'string') {
        settings.baseUrl = raw.baseUrl;
    }
    if (typeof raw.model === 'string') {
        settings.model = raw.model;
    }
    if (typeof raw.timeoutSeconds === 'number' && Number.isFinite(raw.timeoutSeconds)) {
        settings.timeoutSeconds = raw.timeoutSeconds;
    }
    if (typeof raw.systemPrompt === 'string') {
        settings.systemPrompt = raw.systemPrompt;
    }
    if (typeof raw.temperature === 'number' && Number.isFinite(raw.temperature)) {
        settings.temperature = raw.temperature;
    }
    if (typeof raw.maxTokens === 'number' && Number.isFinite(raw.maxTokens)) {
        settings.maxTokens = raw.maxTokens;
    }
    if (typeof raw.sendSelectedImage === 'boolean') {
        settings.sendSelectedImage = raw.sendSelectedImage;
    }
    if (raw.replaceMode === 'preview' || raw.replaceMode === 'append' || raw.replaceMode === 'replace_with_restore') {
        settings.replaceMode = raw.replaceMode;
    }
    return { ok: true, settings };
}
/**
 * Adapter: PromptEnhanceListModels response -> PEModelsResult.
 * An empty model list is classified as a failure — the UI treats "no models"
 * as a configuration problem to surface, never as a silently empty dropdown.
 */
function peAdaptModelsResult(data) {
    if (!peIsRecord(data) || data.success !== true || !Array.isArray(data.models)) {
        return { ok: false, error: peEnvelopeError(data, 'Could not fetch models.') };
    }
    const models = [];
    for (const entry of data.models) {
        if (peIsRecord(entry) && typeof entry.id === 'string' && entry.id) {
            models.push({ id: entry.id, name: typeof entry.name === 'string' && entry.name ? entry.name : entry.id });
        }
    }
    if (models.length === 0) {
        return { ok: false, error: peEnvelopeError(data, 'Could not fetch models.') };
    }
    return { ok: true, models };
}
/**
 * Adapter: PromptEnhanceRun response -> PEEnhanceResult.
 * Success requires a non-empty string `response`; anything else is a
 * classified failure carrying the server's error text when present.
 */
function peAdaptEnhanceResult(data) {
    if (peIsRecord(data) && data.success === true && typeof data.response === 'string' && data.response.length > 0) {
        return { ok: true, response: data.response };
    }
    return { ok: false, error: peEnvelopeError(data, 'enhancement failed.') };
}
