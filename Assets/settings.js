"use strict";
/**
 * Settings persistence and model-discovery UI for the PromptEnhance extension.
 *
 * Owns the settings panel, the Get/Save/Reset settings round-trips, and the
 * `/v1/models`-backed model dropdown. All wire data is normalized through the
 * contracts.ts adapters; no raw response object escapes this file's callbacks.
 *
 * AUTHORITATIVE SOURCE: Frontend/settings.ts. The committed Assets/settings.js
 * is tsc build output — do not hand-edit it.
 */
window.PromptEnhance = window.PromptEnhance || {};
/** Writes the panel status line. `kind` is '' | 'ok' | 'error' (CSS class). */
function peSetStatus(message, kind) {
    const el = document.getElementById('pe_settings_status');
    if (!el) {
        return;
    }
    el.textContent = message || '';
    el.className = 'pe-settings-status' + (kind ? ' ' + kind : '');
}
/**
 * Loads settings from the server into PromptEnhance.settings.
 * Failure is surfaced to the console and the client keeps its defaults —
 * a broken settings store degrades to defaults visibly, never to a crash
 * that would take the Enhance button with it.
 */
function peLoadSettings() {
    return new Promise((resolve) => {
        genericRequest(PE_ROUTES.getSettings, {}, (data) => {
            const result = peAdaptSettingsResult(data);
            if (result.ok) {
                PromptEnhance.settings = Object.assign({}, PromptEnhance.settings, result.settings);
            }
            else {
                console.error('[PromptEnhance] Failed to load settings:', result.error);
            }
            resolve();
        }, 0, (err) => {
            console.error('[PromptEnhance] Failed to load settings:', peErrorText(err));
            resolve();
        });
    });
}
/**
 * DOM adapter: reads the panel fields into a full PESettings value.
 * Missing or non-numeric fields fall back to the current effective settings.
 * Numeric clamps come from PE_LIMITS (contracts.ts, mirroring
 * contracts/pe-contract.json — the same bounds ValidateSettings enforces
 * server-side; maxTokens' int.MaxValue ceiling is not mirrored). An empty
 * model selection means "keep the current model": the dropdown has no
 * affordance for clearing a model, so an unpopulated or placeholder
 * selection must never erase one.
 */
function peReadPanelValues() {
    const current = peEffectiveSettings();
    const num = (id, fallback) => {
        const el = document.getElementById(id);
        const v = parseFloat(el?.value ?? '');
        return Number.isFinite(v) ? v : fallback;
    };
    const text = (id, fallback) => {
        const el = document.getElementById(id);
        return el ? el.value : fallback;
    };
    const rawMode = text('pe_replace_mode', current.replaceMode);
    const replaceMode = rawMode === 'append' || rawMode === 'replace_with_restore' ? rawMode : 'preview';
    const sendImage = document.getElementById('pe_send_image');
    return {
        baseUrl: text('pe_base_url', current.baseUrl).trim(),
        model: text('pe_model_select', current.model) || current.model,
        timeoutSeconds: Math.min(PE_LIMITS.timeoutSeconds.max, Math.max(PE_LIMITS.timeoutSeconds.min, Math.round(num('pe_timeout', current.timeoutSeconds)))),
        systemPrompt: text('pe_system_prompt', current.systemPrompt),
        temperature: Math.min(PE_LIMITS.temperature.max, Math.max(PE_LIMITS.temperature.min, num('pe_temperature', current.temperature))),
        maxTokens: Math.max(PE_LIMITS.maxTokens.min, Math.round(num('pe_max_tokens', current.maxTokens))),
        sendSelectedImage: sendImage ? sendImage.checked : current.sendSelectedImage,
        replaceMode: replaceMode
    };
}
/** Persists the panel values through SavePromptEnhanceSettings. Resolves whether the save was accepted. */
function peSaveSettings() {
    const values = peReadPanelValues();
    peSetStatus('Saving…', '');
    return new Promise((resolve) => {
        genericRequest(PE_ROUTES.saveSettings, { settings: values }, (data) => {
            const result = peAdaptSettingsResult(data);
            if (result.ok) {
                PromptEnhance.settings = Object.assign({}, PromptEnhance.settings, result.settings);
                peSetStatus('Saved.', 'ok');
                resolve(true);
            }
            else {
                peSetStatus('Save failed: ' + result.error, 'error');
                resolve(false);
            }
        }, 0, (err) => {
            peSetStatus('Save failed: ' + peErrorText(err), 'error');
            resolve(false);
        });
    });
}
/** Resets server-side settings to defaults, then repopulates the panel and refreshes the model list. */
function peResetSettings() {
    peSetStatus('Resetting…', '');
    return new Promise((resolve) => {
        genericRequest(PE_ROUTES.resetSettings, {}, (data) => {
            const result = peAdaptSettingsResult(data);
            if (result.ok) {
                PromptEnhance.settings = Object.assign({}, PromptEnhance.settings, result.settings);
                pePopulatePanel();
                peSetStatus('Reset to defaults.', 'ok');
                peFetchModels();
                resolve(true);
            }
            else {
                peSetStatus('Reset failed: ' + result.error, 'error');
                resolve(false);
            }
        }, 0, (err) => {
            peSetStatus('Reset failed: ' + peErrorText(err), 'error');
            resolve(false);
        });
    });
}
/**
 * Populates the model dropdown from the backend's `/v1/models` discovery
 * route. Every failure mode (unreachable, HTTP error, empty list, transport
 * error) lands as a visible disabled option plus a status-line message —
 * never an empty dropdown with no explanation.
 */
function peFetchModels() {
    const select = document.getElementById('pe_model_select');
    if (!select) {
        return Promise.resolve();
    }
    select.innerHTML = '';
    select.add(new Option('Loading models…', ''));
    peSetStatus('Fetching models…', '');
    return new Promise((resolve) => {
        genericRequest(PE_ROUTES.listModels, {}, (data) => {
            const result = peAdaptModelsResult(data);
            select.innerHTML = '';
            if (result.ok) {
                select.add(new Option('-- Select a model --', ''));
                for (const model of result.models) {
                    select.add(new Option(model.name, model.id));
                }
                const configured = PromptEnhance.settings?.model;
                if (configured) {
                    select.value = configured;
                }
                peSetStatus('', '');
            }
            else {
                const opt = new Option('No models — check Base URL', '');
                opt.disabled = true;
                select.add(opt);
                peSetStatus(result.error, 'error');
            }
            resolve();
        }, 0, (err) => {
            select.innerHTML = '';
            const opt = new Option('Error loading models', '');
            opt.disabled = true;
            select.add(opt);
            peSetStatus(peErrorText(err), 'error');
            resolve();
        });
    });
}
/** DOM adapter: writes the current effective settings into the panel fields. */
function pePopulatePanel() {
    const current = peEffectiveSettings();
    const set = (id, value) => {
        const el = document.getElementById(id);
        if (el) {
            el.value = String(value);
        }
    };
    set('pe_base_url', current.baseUrl);
    set('pe_timeout', current.timeoutSeconds);
    set('pe_system_prompt', current.systemPrompt);
    set('pe_temperature', current.temperature);
    set('pe_max_tokens', current.maxTokens);
    set('pe_replace_mode', current.replaceMode);
    const sendImage = document.getElementById('pe_send_image');
    if (sendImage) {
        sendImage.checked = !!current.sendSelectedImage;
    }
    const model = document.getElementById('pe_model_select');
    if (model && current.model && [...model.options].some(o => o.value === current.model)) {
        model.value = current.model;
    }
}
/** Builds (once) and returns the settings panel, mounted on document.body. */
function peBuildSettingsPanel() {
    let panel = document.getElementById('pe_settings_panel');
    if (panel) {
        return panel;
    }
    panel = document.createElement('div');
    panel.id = 'pe_settings_panel';
    panel.className = 'pe-settings-panel';
    panel.style.display = 'none';
    panel.innerHTML = `
        <div class="pe-settings-header">
            <span>PromptEnhance Settings</span>
            <button type="button" class="pe-settings-close" id="pe_settings_close" title="Close">×</button>
        </div>
        <div class="pe-settings-body">
            <label for="pe_base_url">Base URL</label>
            <input type="text" id="pe_base_url" placeholder="http://localhost:11434">
            <div class="pe-field-hint">OpenAI-compatible server. A root URL or one ending in /v1 both work. No API key is sent, so the server must not require authentication.</div>

            <label for="pe_model_select">Model
                <button type="button" class="pe-inline-btn" id="pe_refresh_models" title="Refresh models">⟳</button>
            </label>
            <select id="pe_model_select"><option value="">Loading models…</option></select>

            <label for="pe_system_prompt">System Prompt</label>
            <textarea id="pe_system_prompt" rows="4"></textarea>

            <div class="pe-field-row">
                <div class="pe-field-col">
                    <label for="pe_temperature">Temperature</label>
                    <input type="number" id="pe_temperature" min="${PE_LIMITS.temperature.min}" max="${PE_LIMITS.temperature.max}" step="0.05">
                </div>
                <div class="pe-field-col">
                    <label for="pe_max_tokens">Max Tokens</label>
                    <input type="number" id="pe_max_tokens" min="${PE_LIMITS.maxTokens.min}" step="1">
                </div>
                <div class="pe-field-col">
                    <label for="pe_timeout">Timeout (s)</label>
                    <input type="number" id="pe_timeout" min="${PE_LIMITS.timeoutSeconds.min}" max="${PE_LIMITS.timeoutSeconds.max}" step="1">
                </div>
            </div>

            <label for="pe_replace_mode">Apply Mode</label>
            <select id="pe_replace_mode">
                <option value="preview">Preview (Apply / Cancel)</option>
                <option value="append">Append (keep original)</option>
                <option value="replace_with_restore">Replace (with Restore button)</option>
            </select>

            <label class="pe-checkbox-label">
                <input type="checkbox" id="pe_send_image"> Send selected image with enhance (needs a vision model)
            </label>

            <div class="pe-settings-status" id="pe_settings_status"></div>
        </div>
        <div class="pe-settings-footer">
            <button type="button" class="pe-reset-btn" id="pe_reset_btn">Reset</button>
            <button type="button" class="pe-save-btn" id="pe_save_btn">Save</button>
        </div>
    `;
    document.body.appendChild(panel);
    panel.querySelector('#pe_settings_close').addEventListener('click', peCloseSettingsPanel);
    panel.querySelector('#pe_save_btn').addEventListener('click', () => { peSaveSettings(); });
    panel.querySelector('#pe_reset_btn').addEventListener('click', () => { peResetSettings(); });
    panel.querySelector('#pe_refresh_models').addEventListener('click', (e) => { e.preventDefault(); peFetchModels(); });
    document.addEventListener('click', (e) => {
        if (panel.style.display !== 'block') {
            return;
        }
        const trigger = document.getElementById('pe_settings_button');
        if (!panel.contains(e.target) && trigger && !trigger.contains(e.target)) {
            peCloseSettingsPanel();
        }
    });
    return panel;
}
/**
 * Opens the panel and refreshes the model list. The fetch must run on every
 * open: the dropdown otherwise holds only the static placeholder, and a Save
 * would read an empty selection instead of the configured model.
 */
function peOpenSettingsPanel() {
    const panel = peBuildSettingsPanel();
    pePopulatePanel();
    peSetStatus('', '');
    peFetchModels();
    panel.style.display = 'block';
}
function peCloseSettingsPanel() {
    const panel = document.getElementById('pe_settings_panel');
    if (panel) {
        panel.style.display = 'none';
    }
}
window.PromptEnhance.loadSettings = peLoadSettings;
window.PromptEnhance.fetchModels = peFetchModels;
window.PromptEnhance.openSettingsPanel = peOpenSettingsPanel;
