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
    enhancing: boolean = false;

    /** The earliest pre-enhancement prompt, stashed once per replace cycle. Never overwritten while non-null; cleared only by Restore. */
    lastOriginal: string | null = null;

    /** A preview-mode result awaiting Apply or Cancel. */
    pending: PEPending | null = null;

    /** The Enhance button bar, once mounted. */
    bar: HTMLElement | null = null;

    /** The preview panel, once mounted. */
    preview: HTMLElement | null = null;

    /** Settles when session-ready startup (mount, settings load) has finished. */
    ready: Promise<void> | null = null;

    /** The Generate-tab prompt textarea. */
    promptBox(): HTMLTextAreaElement {
        return getRequiredElementById('alt_prompt_textbox') as HTMLTextAreaElement;
    }

    /** Writes the prompt textarea and notifies SwarmUI of the change. */
    setPrompt(text: string): void {
        let box = this.promptBox();
        box.value = text;
        triggerChangeFor(box);
        box.focus();
    }

    /** Shows or clears the in-flight state on the Enhance button. */
    setLoading(on: boolean): void {
        let button = getRequiredElementById('pe_enhance_btn') as HTMLButtonElement;
        button.disabled = on;
        getRequiredElementById('pe_enhance_loading').style.display = on ? 'inline-flex' : 'none';
    }

    /** Surfaces an error through SwarmUI's error banner, falling back to console and alert if the banner itself fails. */
    showError(message: string): void {
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
    relayout(): void {
        genTabLayout.altPromptSizeHandle();
    }

    /** Reads the currently selected Generate-tab image into a base64 part through SwarmUI's imageToData. Returns null when no image is selected; throws when an image exists but does not read as an image. */
    getSelectedImage(): Promise<PEImagePart | null> {
        let img = document.querySelector<HTMLImageElement>('#current_image img.current-image-img')
            || document.querySelector<HTMLImageElement>('#current_image img');
        let src = img?.getAttribute('src');
        if (!src) {
            return Promise.resolve(null);
        }
        return new Promise((resolve, reject) => {
            imageToData(src, (dataUrl) => {
                let text = dataUrl ?? '';
                let comma = text.indexOf(',');
                let header = comma > 0 ? text.substring(0, comma) : '';
                let data = comma > 0 ? text.substring(comma + 1) : '';
                if (!header.startsWith('data:image/') || !header.endsWith(';base64') || !data) {
                    reject(new Error('Could not attach the selected image: it did not load as an image.'));
                    return;
                }
                resolve({ data: data, mediaType: header.substring('data:'.length, header.length - ';base64'.length) });
            });
        });
    }

    /** One PromptEnhanceRun round-trip, normalized to a PEEnhanceResult. Transport failures resolve, never reject. */
    enhanceRequest(payload: PEEnhancePayload): Promise<PEEnhanceResult> {
        return new Promise((resolve) => {
            genericRequest(PE_ROUTES.run, payload,
                (data) => resolve(peAdaptEnhanceResult(data)),
                0,
                (err) => resolve({ ok: false, error: peErrorText(err) }));
        });
    }

    /** Prompt-mutation policy: preview shows an Apply/Cancel panel; append keeps the original inline; replace_with_restore swaps the prompt and stashes the original for Restore. */
    applyEnhancement(original: string, enhanced: string): void {
        let mode = promptEnhanceSettings.effective().replaceMode;
        if (mode == 'append') {
            this.setPrompt(`${original.trimEnd()}\n\n---\n\n${enhanced}`);
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
    showPreview(original: string, enhanced: string): void {
        this.pending = { original, enhanced };
        getRequiredElementById('pe_preview_text').textContent = enhanced;
        getRequiredElementById('pe_preview').style.display = 'block';
        this.relayout();
    }

    /** Hides the preview panel and drops its pending result. */
    hidePreview(): void {
        this.pending = null;
        getRequiredElementById('pe_preview').style.display = 'none';
        this.relayout();
    }

    /** Shows the Restore button. */
    showRestore(): void {
        getRequiredElementById('pe_restore_btn').style.display = 'inline-block';
        this.relayout();
    }

    /** Hides the Restore button. */
    hideRestore(): void {
        getRequiredElementById('pe_restore_btn').style.display = 'none';
        this.relayout();
    }

    /** Applies the pending preview result, stashing the original for Restore. */
    applyPreview(): void {
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
    restore(): void {
        if (this.lastOriginal != null) {
            this.setPrompt(this.lastOriginal);
            this.lastOriginal = null;
        }
        this.hideRestore();
    }

    /** The Enhance click flow: validate input, optionally attach the selected image, run the backend round-trip, apply the result. Re-entry is guarded; the loading state clears on every path. */
    async handleEnhance(): Promise<void> {
        if (this.enhancing) {
            return;
        }
        let original = this.promptBox().value;
        if (!original.trim()) {
            this.showError('Type a prompt to enhance first.');
            return;
        }
        this.enhancing = true;
        this.setLoading(true);
        this.hidePreview();
        try {
            let payload: PEEnhancePayload = { prompt: original.trim() };
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
    mount(): void {
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
    async start(): Promise<void> {
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
