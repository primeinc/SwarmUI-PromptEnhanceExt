"use strict";
/**
 * Settings for the PromptEnhance extension: the Get/Save/Reset settings round-trips, the
 * `/v1/models`-backed model list, and the settings modal.
 *
 * AUTHORITATIVE SOURCE: Frontend/settings.ts. The committed Assets/settings.js is tsc build
 * output — do not hand-edit it.
 */
/** Display labels for the replace modes, in PE_REPLACE_MODES order. */
let PE_MODE_LABELS = {
    preview: 'Preview (Apply / Cancel)',
    append: 'Append (keep original)',
    replace_with_restore: 'Replace (with Restore button)'
};
/** Server-backed extension settings and the modal that edits them. */
class PromptEnhanceSettings {
    /** Settings as last loaded or saved from the server; `effective()` layers them over PE_DEFAULT_SETTINGS. */
    loaded = {};
    /** The settings modal, built on first open. */
    modal = null;
    /** The full settings view: server-loaded values over defaults. */
    effective() {
        return Object.assign({}, PE_DEFAULT_SETTINGS, this.loaded);
    }
    /** Merges a server settings payload into `loaded`. */
    apply(settings) {
        this.loaded = Object.assign({}, this.loaded, settings);
    }
    /** Loads settings from the server. On failure the defaults stay in effect and the failure is logged. */
    load() {
        return new Promise((resolve) => {
            genericRequest(PE_ROUTES.getSettings, {}, (data) => {
                let result = peAdaptSettingsResult(data);
                if (result.ok) {
                    this.apply(result.settings);
                    if (peIsRecord(data) && data.recovered === true) {
                        console.warn('[PromptEnhance] Stored settings were corrupt; defaults were applied and the corrupt data was backed up server-side (generic-data subkey config_corrupt_backup).');
                    }
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
    /** Writes the modal status line; `kind` picks SwarmUI's success or error styling. */
    setStatus(message, kind) {
        let status = document.getElementById('pe_settings_status');
        if (!status) {
            return;
        }
        status.textContent = message;
        status.classList.toggle('modal_success_bottom', kind == 'ok');
        status.classList.toggle('modal_error_bottom', kind == 'error');
    }
    /** Reads the modal fields into a complete, in-bounds settings value. */
    readForm() {
        let value = (id) => getRequiredElementById(id).value;
        return peNormalizeSettings({
            baseUrl: value('pe_base_url'),
            model: value('pe_model_select'),
            timeoutSeconds: value('pe_timeout'),
            systemPrompt: value('pe_system_prompt'),
            temperature: value('pe_temperature'),
            maxTokens: value('pe_max_tokens'),
            sendSelectedImage: getRequiredElementById('pe_send_image').checked,
            replaceMode: value('pe_replace_mode')
        }, this.effective());
    }
    /** Writes the effective settings into the modal fields. */
    populateForm() {
        let current = this.effective();
        let set = (id, value) => {
            getRequiredElementById(id).value = `${value}`;
        };
        set('pe_base_url', current.baseUrl);
        set('pe_timeout', current.timeoutSeconds);
        set('pe_system_prompt', current.systemPrompt);
        set('pe_temperature', current.temperature);
        set('pe_max_tokens', current.maxTokens);
        set('pe_replace_mode', current.replaceMode);
        getRequiredElementById('pe_send_image').checked = current.sendSelectedImage;
        let model = getRequiredElementById('pe_model_select');
        if (current.model && [...model.options].some((option) => option.value == current.model)) {
            model.value = current.model;
        }
    }
    /** Persists the modal values through SavePromptEnhanceSettings. Resolves whether the save was accepted. */
    save() {
        let values = this.readForm();
        this.setStatus('Saving…', '');
        return new Promise((resolve) => {
            genericRequest(PE_ROUTES.saveSettings, { settings: values }, (data) => {
                let result = peAdaptSettingsResult(data);
                if (result.ok) {
                    this.apply(result.settings);
                    this.setStatus('Saved.', 'ok');
                    resolve(true);
                }
                else {
                    this.setStatus(`Save failed: ${result.error}`, 'error');
                    resolve(false);
                }
            }, 0, (err) => {
                this.setStatus(`Save failed: ${peErrorText(err)}`, 'error');
                resolve(false);
            });
        });
    }
    /** Resets server-side settings to defaults, then repopulates the modal and refreshes the model list. */
    reset() {
        this.setStatus('Resetting…', '');
        return new Promise((resolve) => {
            genericRequest(PE_ROUTES.resetSettings, {}, (data) => {
                let result = peAdaptSettingsResult(data);
                if (result.ok) {
                    this.loaded = result.settings;
                    this.populateForm();
                    this.setStatus('Reset to defaults.', 'ok');
                    this.fetchModels();
                    resolve(true);
                }
                else {
                    this.setStatus(`Reset failed: ${result.error}`, 'error');
                    resolve(false);
                }
            }, 0, (err) => {
                this.setStatus(`Reset failed: ${peErrorText(err)}`, 'error');
                resolve(false);
            });
        });
    }
    /** Replaces the model dropdown's options with one disabled explanatory entry. */
    showModelPlaceholder(select, text) {
        select.innerHTML = '';
        let option = new Option(text, '');
        option.disabled = true;
        select.add(option);
    }
    /** Fills the model dropdown from the backend's `/v1/models` route. Every failure lands as a disabled explanatory option plus a status message. */
    fetchModels() {
        let select = document.getElementById('pe_model_select');
        if (!select) {
            return Promise.resolve();
        }
        this.showModelPlaceholder(select, 'Loading models…');
        this.setStatus('Fetching models…', '');
        return new Promise((resolve) => {
            genericRequest(PE_ROUTES.listModels, {}, (data) => {
                let result = peAdaptModelsResult(data);
                if (result.ok) {
                    select.innerHTML = '';
                    select.add(new Option('-- Select a model --', ''));
                    for (let model of result.models) {
                        select.add(new Option(model.name, model.id));
                    }
                    let configured = this.effective().model;
                    if (configured) {
                        select.value = configured;
                    }
                    this.setStatus('', '');
                }
                else {
                    this.showModelPlaceholder(select, 'No models — check Base URL');
                    this.setStatus(result.error, 'error');
                }
                resolve();
            }, 0, (err) => {
                this.showModelPlaceholder(select, 'Error loading models');
                this.setStatus(peErrorText(err), 'error');
                resolve();
            });
        });
    }
    /** Builds the settings modal from SwarmUI's modal and input helpers (site.js) and appends it to the page. */
    buildModal() {
        let defaults = PE_DEFAULT_SETTINGS;
        let field = (id, name, type, description, input) => makeGenericPopover(id, name, type, description, '') + input;
        let body = field('pe_base_url', 'Base URL', 'text', 'OpenAI-compatible server. A root URL or one ending in /v1 both work. If the server needs an API key, set it under User → API Keys.', makeTextInput(null, 'pe_base_url', '', 'Base URL', '', defaults.baseUrl, 'normal', defaults.baseUrl, false, false, true))
            + '<div class="pe-api-key-row">API Key: <span id="pe_api_key_status"></span> <a href="#" id="pe_api_key_link">Set in User → API Keys</a></div>'
            + field('pe_model_select', 'Model', 'dropdown', 'The model the backend runs. The list comes from the backend at Base URL.', makeDropdownInput(null, 'pe_model_select', '', 'Model', '', [], '', false, true))
            + '<button type="button" class="basic-button" id="pe_refresh_models">Refresh Models</button>'
            + field('pe_system_prompt', 'System Prompt', 'text', 'Instruction sent ahead of the prompt to enhance.', makeTextInput(null, 'pe_system_prompt', '', 'System Prompt', '', defaults.systemPrompt, 'big', '', false, false, true))
            + field('pe_temperature', 'Temperature', 'number', `Sampling temperature, ${PE_LIMITS.temperature.min} to ${PE_LIMITS.temperature.max}.`, makeNumberInput(null, 'pe_temperature', '', 'Temperature', '', defaults.temperature, PE_LIMITS.temperature.min, PE_LIMITS.temperature.max, 0.05))
            + field('pe_max_tokens', 'Max Tokens', 'number', 'Upper bound on the length of the enhanced prompt.', makeNumberInput(null, 'pe_max_tokens', '', 'Max Tokens', '', defaults.maxTokens, PE_LIMITS.maxTokens.min, 1000000, 1))
            + field('pe_timeout', 'Timeout (s)', 'number', `Seconds to wait for the backend, ${PE_LIMITS.timeoutSeconds.min} to ${PE_LIMITS.timeoutSeconds.max}.`, makeNumberInput(null, 'pe_timeout', '', 'Timeout (s)', '', defaults.timeoutSeconds, PE_LIMITS.timeoutSeconds.min, PE_LIMITS.timeoutSeconds.max, 1))
            + field('pe_replace_mode', 'Apply Mode', 'dropdown', 'What Enhance does with the result: show it for Apply/Cancel, append it below the prompt, or replace the prompt with a Restore button.', makeDropdownInput(null, 'pe_replace_mode', '', 'Apply Mode', '', [...PE_REPLACE_MODES], defaults.replaceMode, false, true, PE_REPLACE_MODES.map((mode) => PE_MODE_LABELS[mode])))
            + field('pe_send_image', 'Send Selected Image', 'checkbox', 'Attach the currently selected image to the request. Needs a vision model.', makeCheckboxInput(null, 'pe_send_image', '', 'Send Selected Image', '', defaults.sendSelectedImage, false, false, true));
        document.body.insertAdjacentHTML('beforeend', modalHeader('pe_settings_modal', 'PromptEnhance Settings')
            + `<div class="modal-body">${body}</div>`
            + '<div class="modal-footer">'
            + '<span id="pe_settings_status"></span>'
            + '<button type="button" class="btn btn-secondary basic-button" id="pe_reset_btn">Reset</button>'
            + '<button type="button" class="btn btn-secondary basic-button" id="pe_close_btn">Close</button>'
            + '<button type="button" class="btn btn-primary basic-button" id="pe_save_btn">Save</button>'
            + '</div>'
            + modalFooter());
        let modal = getRequiredElementById('pe_settings_modal');
        getRequiredElementById('pe_refresh_models').addEventListener('click', () => this.fetchModels());
        getRequiredElementById('pe_api_key_link').addEventListener('click', (e) => {
            e.preventDefault();
            this.openApiKeys();
        });
        getRequiredElementById('pe_reset_btn').addEventListener('click', () => this.reset());
        getRequiredElementById('pe_close_btn').addEventListener('click', () => this.close());
        getRequiredElementById('pe_save_btn').addEventListener('click', () => this.save());
        return modal;
    }
    /** Shows whether a backend API key is saved, from SwarmUI's GetAPIKeyStatus route. The key itself never reaches the browser. */
    fetchApiKeyStatus() {
        let status = document.getElementById('pe_api_key_status');
        if (!status) {
            return;
        }
        status.textContent = '…';
        genericRequest('GetAPIKeyStatus', { keyType: PE_API_KEY_TYPE }, (data) => {
            status.textContent = peIsRecord(data) && typeof data.status == 'string' ? data.status : 'unknown';
        }, 0, (err) => {
            status.textContent = `unknown (${peErrorText(err)})`;
        });
    }
    /** Closes the modal and shows this extension's row in SwarmUI's User → API Keys table. */
    openApiKeys() {
        this.close();
        getRequiredElementById('usersettingstabbutton').click();
        getRequiredElementById('userinfotabbutton').click();
        let input = document.getElementById('promptenhance_api_key');
        if (input) {
            input.scrollIntoView({ block: 'center' });
            input.focus();
        }
    }
    /** Opens the settings modal with the current settings, a fresh model list, and the API key status. */
    open() {
        if (!this.modal) {
            this.modal = this.buildModal();
        }
        this.populateForm();
        this.setStatus('', '');
        this.fetchModels();
        this.fetchApiKeyStatus();
        $(this.modal).modal('show');
    }
    /** Closes the settings modal. */
    close() {
        if (this.modal) {
            $(this.modal).modal('hide');
        }
    }
}
/** Shared extension settings. */
let promptEnhanceSettings = new PromptEnhanceSettings();
