'use strict';

window.PromptEnhance = window.PromptEnhance || {};

PromptEnhance.REPLACE_MODES = ['preview', 'append', 'replace_with_restore'];

PromptEnhance.settings = {
    baseUrl: 'http://localhost:11434',
    model: '',
    timeoutSeconds: 60,
    systemPrompt: "You are a prompt enhancer for text-to-image generation. Rewrite the user's prompt into a single, richly detailed image-generation prompt. Reply with only the enhanced prompt, no preamble or explanation.",
    temperature: 0.7,
    maxTokens: 1024,
    sendSelectedImage: false,
    replaceMode: 'preview'
};

function peSetStatus(message, kind) {
    const el = document.getElementById('pe_settings_status');
    if (!el) {
        return;
    }
    el.textContent = message || '';
    el.className = 'pe-settings-status' + (kind ? ' ' + kind : '');
}

function peLoadSettings() {
    return new Promise((resolve) => {
        genericRequest('GetPromptEnhanceSettings', {}, (data) => {
            if (data.success && data.settings) {
                PromptEnhance.settings = Object.assign({}, PromptEnhance.settings, data.settings);
            } else {
                console.error('[PromptEnhance] Failed to load settings:', data.error);
            }
            resolve(PromptEnhance.settings);
        }, 0, (err) => {
            console.error('[PromptEnhance] Failed to load settings:', err);
            resolve(PromptEnhance.settings);
        });
    });
}

function peReadPanelValues() {
    const num = (id, fallback) => {
        const v = parseFloat(document.getElementById(id)?.value);
        return Number.isFinite(v) ? v : fallback;
    };
    return {
        baseUrl: document.getElementById('pe_base_url')?.value?.trim() ?? PromptEnhance.settings.baseUrl,
        model: document.getElementById('pe_model_select')?.value ?? PromptEnhance.settings.model,
        timeoutSeconds: Math.max(1, Math.round(num('pe_timeout', PromptEnhance.settings.timeoutSeconds))),
        systemPrompt: document.getElementById('pe_system_prompt')?.value ?? PromptEnhance.settings.systemPrompt,
        temperature: num('pe_temperature', PromptEnhance.settings.temperature),
        maxTokens: Math.max(1, Math.round(num('pe_max_tokens', PromptEnhance.settings.maxTokens))),
        sendSelectedImage: document.getElementById('pe_send_image')?.checked ?? PromptEnhance.settings.sendSelectedImage,
        replaceMode: document.getElementById('pe_replace_mode')?.value ?? PromptEnhance.settings.replaceMode
    };
}

function peSaveSettings() {
    const values = peReadPanelValues();
    peSetStatus('Saving…', '');
    return new Promise((resolve) => {
        genericRequest('SavePromptEnhanceSettings', { settings: values }, (data) => {
            if (data.success && data.settings) {
                PromptEnhance.settings = Object.assign({}, PromptEnhance.settings, data.settings);
                peSetStatus('Saved.', 'ok');
                resolve(true);
            } else {
                peSetStatus('Save failed: ' + (data.error || 'unknown error'), 'error');
                resolve(false);
            }
        }, 0, (err) => {
            peSetStatus('Save failed: ' + (err?.message || err || 'request error'), 'error');
            resolve(false);
        });
    });
}

function peResetSettings() {
    peSetStatus('Resetting…', '');
    genericRequest('ResetPromptEnhanceSettings', {}, (data) => {
        if (data.success && data.settings) {
            PromptEnhance.settings = Object.assign({}, PromptEnhance.settings, data.settings);
            pePopulatePanel();
            peSetStatus('Reset to defaults.', 'ok');
            peFetchModels();
        } else {
            peSetStatus('Reset failed: ' + (data.error || 'unknown error'), 'error');
        }
    }, 0, (err) => {
        peSetStatus('Reset failed: ' + (err?.message || err || 'request error'), 'error');
    });
}

function peFetchModels() {
    const select = document.getElementById('pe_model_select');
    if (!select) {
        return Promise.resolve();
    }
    select.innerHTML = '';
    select.add(new Option('Loading models…', ''));
    peSetStatus('Fetching models…', '');
    return new Promise((resolve) => {
        genericRequest('PromptEnhanceListModels', {}, (data) => {
            select.innerHTML = '';
            if (data.success && Array.isArray(data.models) && data.models.length > 0) {
                select.add(new Option('-- Select a model --', ''));
                for (const m of data.models) {
                    if (m && m.id) {
                        select.add(new Option(m.name || m.id, m.id));
                    }
                }
                if (PromptEnhance.settings.model) {
                    select.value = PromptEnhance.settings.model;
                }
                peSetStatus('', '');
            } else {
                const opt = new Option('No models — check Base URL', '');
                opt.disabled = true;
                select.add(opt);
                peSetStatus(data.error || 'Could not fetch models.', 'error');
            }
            resolve();
        }, 0, (err) => {
            select.innerHTML = '';
            const opt = new Option('Error loading models', '');
            opt.disabled = true;
            select.add(opt);
            peSetStatus(err?.message || 'Could not fetch models.', 'error');
            resolve();
        });
    });
}

function pePopulatePanel() {
    const set = (id, value) => { const el = document.getElementById(id); if (el) { el.value = value; } };
    set('pe_base_url', PromptEnhance.settings.baseUrl ?? '');
    set('pe_timeout', PromptEnhance.settings.timeoutSeconds ?? 60);
    set('pe_system_prompt', PromptEnhance.settings.systemPrompt ?? '');
    set('pe_temperature', PromptEnhance.settings.temperature ?? 0.7);
    set('pe_max_tokens', PromptEnhance.settings.maxTokens ?? 1024);
    set('pe_replace_mode', PromptEnhance.settings.replaceMode ?? 'preview');
    const sendImage = document.getElementById('pe_send_image');
    if (sendImage) {
        sendImage.checked = !!PromptEnhance.settings.sendSelectedImage;
    }
    const model = document.getElementById('pe_model_select');
    if (model && PromptEnhance.settings.model && [...model.options].some(o => o.value === PromptEnhance.settings.model)) {
        model.value = PromptEnhance.settings.model;
    }
}

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
            <div class="pe-field-hint">OpenAI-compatible server. A root URL or one ending in /v1 both work.</div>

            <label for="pe_model_select">Model
                <button type="button" class="pe-inline-btn" id="pe_refresh_models" title="Refresh models">⟳</button>
            </label>
            <select id="pe_model_select"><option value="">Loading models…</option></select>

            <label for="pe_system_prompt">System Prompt</label>
            <textarea id="pe_system_prompt" rows="4"></textarea>

            <div class="pe-field-row">
                <div class="pe-field-col">
                    <label for="pe_temperature">Temperature</label>
                    <input type="number" id="pe_temperature" min="0" max="2" step="0.05">
                </div>
                <div class="pe-field-col">
                    <label for="pe_max_tokens">Max Tokens</label>
                    <input type="number" id="pe_max_tokens" min="1" step="1">
                </div>
                <div class="pe-field-col">
                    <label for="pe_timeout">Timeout (s)</label>
                    <input type="number" id="pe_timeout" min="1" step="1">
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

function peOpenSettingsPanel() {
    const panel = peBuildSettingsPanel();
    pePopulatePanel();
    peSetStatus('', '');
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
