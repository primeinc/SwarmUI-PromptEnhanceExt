"use strict";
/**
 * Generate-tab integration for the PromptEnhance extension: the Enhance bar, the preview panel,
 * the image-context adapter, and the prompt-mutation policy (preview / append / replace-with-restore).
 *
 * AUTHORITATIVE SOURCE: Frontend/promptenhance.ts. The committed Assets/promptenhance.js is tsc
 * build output — do not hand-edit it.
 */
/** The Enhance controls on the Generate tab and the prompt edits they make. */
class PromptEnhanceGenTab {
    /** True while an enhancement round-trip is in flight; guards against re-entry. */
    enhancing = false;
    /** The earliest pre-enhancement prompt, stashed once per replace cycle. Never overwritten while non-null; cleared only by Restore. */
    lastOriginal = null;
    /** A preview-mode result awaiting Apply or Cancel. */
    pending = null;
    /** The Enhance button bar, once mounted. */
    bar = null;
    /** The preview panel, once mounted. */
    preview = null;
    /** Settles when session-ready startup (mount, settings load) has finished. */
    ready = null;
    /** The Generate-tab prompt textarea. */
    promptBox() {
        return getRequiredElementById('alt_prompt_textbox');
    }
    /** Writes the prompt textarea and notifies SwarmUI of the change. */
    setPrompt(text) {
        let box = this.promptBox();
        box.value = text;
        triggerChangeFor(box);
        box.focus();
    }
    /** Shows or clears the in-flight state on the Enhance button. */
    setLoading(on) {
        let button = getRequiredElementById('pe_enhance_btn');
        button.disabled = on;
        getRequiredElementById('pe_enhance_loading').style.display = on ? 'inline-flex' : 'none';
    }
    /** Surfaces an error through SwarmUI's error banner, falling back to console and alert if the banner itself fails. */
    showError(message) {
        try {
            showError(message);
        }
        catch (err) {
            console.error('[PromptEnhance] host showError failed:', err);
            console.error('[PromptEnhance]', message);
            alert(message);
        }
    }
    /** Re-offsets SwarmUI's prompt region after the extension changes the height of `#alt_prompt_extra_area`. */
    relayout() {
        genTabLayout.altPromptSizeHandle();
    }
    /** Reads the currently selected Generate-tab image into a base64 part. Returns null when no image is selected; throws when an image exists but cannot be read. */
    async getSelectedImage() {
        let img = document.querySelector('#current_image img.current-image-img')
            || document.querySelector('#current_image img');
        if (!img?.src) {
            return null;
        }
        try {
            let resp = await fetch(img.src);
            let blob = await resp.blob();
            return await new Promise((resolve, reject) => {
                let reader = new FileReader();
                reader.onloadend = () => {
                    let base64 = `${reader.result}`.split(',')[1];
                    if (!base64) {
                        reject(new Error('The selected image could not be read.'));
                        return;
                    }
                    resolve({ data: base64, mediaType: blob.type || 'image/jpeg' });
                };
                reader.onerror = () => reject(new Error('The selected image could not be read.'));
                reader.readAsDataURL(blob);
            });
        }
        catch (err) {
            throw new Error(`Could not attach the selected image: ${peErrorText(err)}`);
        }
    }
    /** One PromptEnhanceRun round-trip, normalized to a PEEnhanceResult. Transport failures resolve, never reject. */
    enhanceRequest(payload) {
        return new Promise((resolve) => {
            genericRequest(PE_ROUTES.run, payload, (data) => resolve(peAdaptEnhanceResult(data)), 0, (err) => resolve({ ok: false, error: peErrorText(err) }));
        });
    }
    /** Prompt-mutation policy: preview shows an Apply/Cancel panel; append keeps the original inline; replace_with_restore swaps the prompt and stashes the original for Restore. */
    applyEnhancement(original, enhanced) {
        let mode = promptEnhanceSettings.effective().replaceMode;
        if (mode == 'append') {
            this.setPrompt(`${original}\n\n---\n\n${enhanced}`);
            this.hideRestore();
            return;
        }
        if (mode == 'replace_with_restore') {
            if (this.lastOriginal == null) {
                this.lastOriginal = original;
            }
            this.setPrompt(enhanced);
            this.showRestore();
            return;
        }
        this.showPreview(original, enhanced);
    }
    /** Shows a preview-mode result for Apply or Cancel. */
    showPreview(original, enhanced) {
        this.pending = { original, enhanced };
        getRequiredElementById('pe_preview_text').textContent = enhanced;
        getRequiredElementById('pe_preview').style.display = 'block';
        this.relayout();
    }
    /** Hides the preview panel and drops its pending result. */
    hidePreview() {
        this.pending = null;
        getRequiredElementById('pe_preview').style.display = 'none';
        this.relayout();
    }
    /** Shows the Restore button. */
    showRestore() {
        getRequiredElementById('pe_restore_btn').style.display = 'inline-block';
        this.relayout();
    }
    /** Hides the Restore button. */
    hideRestore() {
        getRequiredElementById('pe_restore_btn').style.display = 'none';
        this.relayout();
    }
    /** Applies the pending preview result, stashing the original for Restore. */
    applyPreview() {
        if (this.pending) {
            if (this.lastOriginal == null) {
                this.lastOriginal = this.pending.original;
            }
            this.setPrompt(this.pending.enhanced);
            this.showRestore();
        }
        this.hidePreview();
    }
    /** Puts the stashed original prompt back. */
    restore() {
        if (this.lastOriginal != null) {
            this.setPrompt(this.lastOriginal);
            this.lastOriginal = null;
        }
        this.hideRestore();
    }
    /** The Enhance click flow: validate input, optionally attach the selected image, run the backend round-trip, apply the result. Re-entry is guarded; the loading state clears on every path. */
    async handleEnhance() {
        if (this.enhancing) {
            return;
        }
        let original = this.promptBox().value.trim();
        if (!original) {
            this.showError('Type a prompt to enhance first.');
            return;
        }
        this.enhancing = true;
        this.setLoading(true);
        this.hidePreview();
        try {
            let payload = { prompt: original };
            if (promptEnhanceSettings.effective().sendSelectedImage) {
                let image = await this.getSelectedImage();
                if (image) {
                    payload.media = [{ type: 'base64', data: image.data, mediaType: image.mediaType }];
                }
            }
            let result = await this.enhanceRequest(payload);
            if (result.ok) {
                this.applyEnhancement(original, result.response);
            }
            else {
                this.showError(`PromptEnhance: ${result.error}`);
            }
        }
        catch (err) {
            this.showError(`PromptEnhance: ${peErrorText(err)}`);
        }
        finally {
            this.enhancing = false;
            this.setLoading(false);
        }
    }
    /**
     * Mounts the Enhance bar and preview panel at the top of `#alt_prompt_extra_area`, the one part of
     * SwarmUI's prompt region whose height the region offset accounts for. Idempotent.
     */
    mount() {
        if (this.bar) {
            return;
        }
        let area = getRequiredElementById('alt_prompt_extra_area');
        this.bar = createDiv('pe_button_bar', 'pe-button-bar', `
            <button type="button" class="basic-button pe-enhance-btn" id="pe_enhance_btn">Enhance Prompt</button>
            <button type="button" class="basic-button" id="pe_settings_button" title="PromptEnhance Settings">&#x2699;&#xFE0F;</button>
            <span class="pe-loading" id="pe_enhance_loading" style="display: none;"><span></span><span></span><span></span></span>
            <button type="button" class="basic-button" id="pe_restore_btn" style="display: none;">Restore Previous Prompt</button>`);
        this.preview = createDiv('pe_preview', 'pe-preview', `
            <div class="pe-preview-label">Enhanced preview — nothing has changed yet:</div>
            <div class="pe-preview-text" id="pe_preview_text"></div>
            <div class="pe-preview-actions">
                <button type="button" class="basic-button" id="pe_preview_apply">Apply</button>
                <button type="button" class="basic-button" id="pe_preview_cancel">Cancel</button>
            </div>`);
        this.preview.style.display = 'none';
        area.insertBefore(this.preview, area.firstChild);
        area.insertBefore(this.bar, area.firstChild);
        getRequiredElementById('pe_enhance_btn').addEventListener('click', () => this.handleEnhance());
        getRequiredElementById('pe_settings_button').addEventListener('click', () => promptEnhanceSettings.open());
        getRequiredElementById('pe_restore_btn').addEventListener('click', () => this.restore());
        getRequiredElementById('pe_preview_apply').addEventListener('click', () => this.applyPreview());
        getRequiredElementById('pe_preview_cancel').addEventListener('click', () => this.hidePreview());
        this.relayout();
    }
    /** Session-ready startup: mount the controls and load settings. */
    async start() {
        this.mount();
        await promptEnhanceSettings.load();
    }
}
/** Shared Generate-tab integration. */
let promptEnhanceGenTab = new PromptEnhanceGenTab();
sessionReadyCallbacks.push(() => {
    if (!promptEnhanceGenTab.ready) {
        promptEnhanceGenTab.ready = promptEnhanceGenTab.start();
    }
});
