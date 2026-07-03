/**
 * Generate-tab integration for the PromptEnhance extension: the Enhance
 * button bar, the image-context adapter, and the reversible prompt-mutation
 * policy (preview / append / replace-with-restore).
 *
 * AUTHORITATIVE SOURCE: Frontend/promptenhance.ts. The committed
 * Assets/promptenhance.js is tsc build output — do not hand-edit it.
 */

window.PromptEnhance = window.PromptEnhance || {};
PromptEnhance.initialized = false;
PromptEnhance.enhancing = false;
PromptEnhance.lastOriginal = null;
PromptEnhance.pending = null;

/** DOM adapter: the Generate-tab prompt textarea, or null when the tab isn't mounted. */
function pePromptBox(): HTMLTextAreaElement | null {
    return document.getElementById('alt_prompt_textbox') as HTMLTextAreaElement | null;
}

/**
 * Writes the prompt textarea and notifies SwarmUI through its own change
 * hook (`triggerChangeFor`, site.js) so Swarm-side listeners stay in sync.
 */
function peSetPrompt(text: string): void {
    const box = pePromptBox();
    if (!box) {
        return;
    }
    box.value = text;
    if (typeof window.triggerChangeFor === 'function') {
        window.triggerChangeFor(box);
    }
    box.focus();
}

function peSetLoading(on: boolean): void {
    const btn = document.getElementById('pe_enhance_btn') as HTMLButtonElement | null;
    const spinner = document.getElementById('pe_enhance_loading');
    if (btn) {
        btn.disabled = on;
        btn.classList.toggle('loading', on);
    }
    if (spinner) {
        spinner.style.display = on ? 'inline-block' : 'none';
    }
}

/**
 * Surfaces an error to the user. Prefers SwarmUI's own `showError` banner;
 * if the host helper is absent or itself throws, the failure is logged and
 * the message still reaches the user via alert. No path swallows the message.
 */
function peShowError(message: string): void {
    try {
        if (typeof window.showError === 'function') {
            window.showError(message);
            return;
        }
    } catch (err) {
        console.error('[PromptEnhance] host showError failed:', err);
    }
    console.error('[PromptEnhance]', message);
    alert(message);
}

/**
 * Image-context adapter: reads the currently selected Generate-tab image
 * (the `#current_image` element SwarmUI renders) into a base64 part.
 * Returns null when no image is selected — a legitimate text-only enhance.
 * Throws a classified, user-readable Error when an image exists but cannot
 * be read, so the caller can refuse to send a silently image-less request.
 */
async function peGetSelectedImage(): Promise<PEImagePart | null> {
    const img = document.querySelector<HTMLImageElement>('#current_image img.current-image-img')
        || document.querySelector<HTMLImageElement>('#current_image img');
    if (!img || !img.src) {
        return null;
    }
    try {
        const resp = await fetch(img.src);
        const blob = await resp.blob();
        return await new Promise<PEImagePart>((resolve, reject) => {
            const reader = new FileReader();
            reader.onloadend = () => {
                const base64 = String(reader.result).split(',')[1];
                if (!base64) {
                    reject(new Error('The selected image could not be read.'));
                    return;
                }
                resolve({
                    data: base64,
                    mediaType: blob.type || 'image/jpeg'
                });
            };
            reader.onerror = () => reject(new Error('The selected image could not be read.'));
            reader.readAsDataURL(blob);
        });
    } catch (err) {
        throw new Error('Could not attach the selected image: ' + peErrorText(err));
    }
}

/**
 * Transport adapter: one PromptEnhanceRun round-trip, normalized to a
 * PEEnhanceResult. Transport-level failures resolve (never reject) so the
 * caller has exactly one failure channel.
 */
function peEnhanceRequest(payload: PEEnhancePayload): Promise<PEEnhanceResult> {
    return new Promise((resolve) => {
        genericRequest(PE_ROUTES.run, payload,
            (data) => resolve(peAdaptEnhanceResult(data)),
            0,
            (err) => resolve({ ok: false, error: peErrorText(err) })
        );
    });
}

/**
 * Prompt-mutation policy. Every mode preserves a recovery path:
 * - preview: nothing changes until the user clicks Apply.
 * - append: the original stays inline above the enhancement.
 * - replace_with_restore: the box is replaced, and the TRUE original is
 *   stashed once (see PromptEnhanceNamespace.lastOriginal invariant) so
 *   Restore recovers it even after repeated enhances.
 */
function peApplyEnhancement(original: string, enhanced: string): void {
    const mode = PromptEnhance.settings?.replaceMode || 'preview';
    if (mode === 'append') {
        peSetPrompt(`${original}\n\n---\n\n${enhanced}`);
        peHideRestore();
        return;
    }
    if (mode === 'replace_with_restore') {
        if (PromptEnhance.lastOriginal === null) {
            PromptEnhance.lastOriginal = original;
        }
        peSetPrompt(enhanced);
        peShowRestore();
        return;
    }
    peShowPreview(original, enhanced);
}

function peShowPreview(original: string, enhanced: string): void {
    PromptEnhance.pending = { original, enhanced };
    const preview = document.getElementById('pe_preview');
    const text = document.getElementById('pe_preview_text');
    if (preview && text) {
        text.textContent = enhanced;
        preview.style.display = 'block';
    }
}

function peHidePreview(): void {
    PromptEnhance.pending = null;
    const preview = document.getElementById('pe_preview');
    if (preview) {
        preview.style.display = 'none';
    }
}

function peShowRestore(): void {
    const btn = document.getElementById('pe_restore_btn');
    if (btn) {
        btn.style.display = 'inline-block';
    }
}

function peHideRestore(): void {
    const btn = document.getElementById('pe_restore_btn');
    if (btn) {
        btn.style.display = 'none';
    }
}

/**
 * The Enhance click flow: validate input, optionally attach the selected
 * image, run the backend round-trip, and apply the result through the
 * mutation policy. Reentrancy is guarded, and the loading state clears on
 * every path — including image-collection failure and transport failure —
 * so the button can never wedge in spinner purgatory.
 */
async function peHandleEnhance(): Promise<void> {
    if (PromptEnhance.enhancing) {
        return;
    }
    const box = pePromptBox();
    if (!box) {
        return;
    }
    const original = box.value.trim();
    if (!original) {
        peShowError('Type a prompt to enhance first.');
        return;
    }
    PromptEnhance.enhancing = true;
    peSetLoading(true);
    peHidePreview();
    try {
        const payload: PEEnhancePayload = { prompt: original };
        if (PromptEnhance.settings?.sendSelectedImage) {
            const image = await peGetSelectedImage();
            if (image) {
                payload.media = [{ type: 'base64', data: image.data, mediaType: image.mediaType }];
            }
        }
        const result = await peEnhanceRequest(payload);
        if (result.ok) {
            peApplyEnhancement(original, result.response);
        } else {
            peShowError('PromptEnhance: ' + result.error);
        }
    } catch (err) {
        peShowError('PromptEnhance: ' + peErrorText(err));
    } finally {
        PromptEnhance.enhancing = false;
        peSetLoading(false);
    }
}

/**
 * Injects the button bar and preview panel into SwarmUI's Generate-tab
 * prompt region. Idempotent: a second call is a no-op while the bar exists.
 */
function peAddPromptButtons(): void {
    const region = document.querySelector('.alt_prompt_region');
    if (!region || document.getElementById('pe_button_bar')) {
        return;
    }
    const bar = document.createElement('div');
    bar.id = 'pe_button_bar';
    bar.className = 'promptenhance pe-button-bar';
    bar.innerHTML = `
        <button type="button" class="pe-enhance-btn" id="pe_enhance_btn">✨ Enhance Prompt</button>
        <button type="button" class="pe-settings-button" id="pe_settings_button" title="PromptEnhance Settings">⚙️</button>
        <span class="pe-loading" id="pe_enhance_loading" style="display:none"><span></span><span></span><span></span></span>
        <button type="button" class="pe-restore-btn" id="pe_restore_btn" style="display:none">↺ Restore Previous Prompt</button>
    `;
    const preview = document.createElement('div');
    preview.id = 'pe_preview';
    preview.className = 'promptenhance pe-preview';
    preview.style.display = 'none';
    preview.innerHTML = `
        <div class="pe-preview-label">Enhanced preview — nothing has changed yet:</div>
        <div class="pe-preview-text" id="pe_preview_text"></div>
        <div class="pe-preview-actions">
            <button type="button" class="pe-preview-apply" id="pe_preview_apply">Apply</button>
            <button type="button" class="pe-preview-cancel" id="pe_preview_cancel">Cancel</button>
        </div>
    `;

    region.insertBefore(preview, region.firstChild);
    region.insertBefore(bar, region.firstChild);

    bar.querySelector<HTMLButtonElement>('#pe_enhance_btn')!.addEventListener('click', peHandleEnhance);
    bar.querySelector<HTMLButtonElement>('#pe_settings_button')!.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        PromptEnhance.openSettingsPanel?.();
    });
    bar.querySelector<HTMLButtonElement>('#pe_restore_btn')!.addEventListener('click', () => {
        if (PromptEnhance.lastOriginal !== null && PromptEnhance.lastOriginal !== undefined) {
            peSetPrompt(PromptEnhance.lastOriginal);
            PromptEnhance.lastOriginal = null;
        }
        peHideRestore();
    });
    preview.querySelector<HTMLButtonElement>('#pe_preview_apply')!.addEventListener('click', () => {
        if (PromptEnhance.pending) {
            if (PromptEnhance.lastOriginal === null) {
                PromptEnhance.lastOriginal = PromptEnhance.pending.original;
            }
            peSetPrompt(PromptEnhance.pending.enhanced);
            peShowRestore();
        }
        peHidePreview();
    });
    preview.querySelector<HTMLButtonElement>('#pe_preview_cancel')!.addEventListener('click', peHidePreview);
}

/**
 * SwarmUI renders the Generate tab asynchronously after DOMContentLoaded, so
 * injection polls for `.alt_prompt_region` (every 250ms, up to ~10s) instead
 * of assuming the region exists at script load. Bounded so a non-Generate
 * page (eg the installer) doesn't poll forever.
 */
function peEnsureButtons(attempt: number = 0): void {
    if (document.querySelector('.alt_prompt_region')) {
        peAddPromptButtons();
        return;
    }
    if (attempt < 40) {
        setTimeout(() => peEnsureButtons(attempt + 1), 250);
    }
}

document.addEventListener('DOMContentLoaded', async () => {
    if (PromptEnhance.initialized) {
        return;
    }
    PromptEnhance.initialized = true;
    try {
        if (PromptEnhance.loadSettings) {
            await PromptEnhance.loadSettings();
        }
    } catch (err) {
        console.error('[PromptEnhance] settings load failed (continuing):', err);
    }
    peEnsureButtons();
    try {
        PromptEnhance.fetchModels?.();
    } catch (err) {
        console.error('[PromptEnhance] model fetch failed (continuing):', err);
    }
});
