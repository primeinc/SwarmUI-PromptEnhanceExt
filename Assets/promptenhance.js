'use strict';

window.PromptEnhance = window.PromptEnhance || {};
PromptEnhance.initialized = false;
PromptEnhance.enhancing = false;
PromptEnhance.lastOriginal = null;
PromptEnhance.pending = null;

function pePromptBox() {
    return document.getElementById('alt_prompt_textbox');
}

function peSetPrompt(text) {
    const box = pePromptBox();
    if (!box) {
        return;
    }
    box.value = text;
    if (typeof triggerChangeFor === 'function') {
        triggerChangeFor(box);
    }
    box.focus();
}

function peSetLoading(on) {
    const btn = document.getElementById('pe_enhance_btn');
    const spinner = document.getElementById('pe_enhance_loading');
    if (btn) {
        btn.disabled = on;
        btn.classList.toggle('loading', on);
    }
    if (spinner) {
        spinner.style.display = on ? 'inline-block' : 'none';
    }
}

function peShowError(message) {
    try {
        if (typeof showError === 'function') {
            showError(message);
            return;
        }
    } catch (_) { }
    console.error('[PromptEnhance]', message);
    alert(message);
}

async function peGetSelectedImage() {
    const img = document.querySelector('#current_image img.current-image-img')
        || document.querySelector('#current_image img');
    if (!img || !img.src) {
        return null;
    }
    try {
        const resp = await fetch(img.src);
        const blob = await resp.blob();
        return await new Promise((resolve, reject) => {
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
        throw new Error('Could not attach the selected image: ' + ((err && err.message) || err));
    }
}

function peEnhanceRequest(payload) {
    return new Promise((resolve) => {
        genericRequest('PromptEnhanceRun', payload,
            (data) => resolve(data),
            0,
            (err) => resolve({ success: false, error: (err && err.message) || String(err) || 'request error' })
        );
    });
}

function peApplyEnhancement(original, enhanced) {
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

function peShowPreview(original, enhanced) {
    PromptEnhance.pending = { original, enhanced };
    const preview = document.getElementById('pe_preview');
    const text = document.getElementById('pe_preview_text');
    if (preview && text) {
        text.textContent = enhanced;
        preview.style.display = 'block';
    }
}

function peHidePreview() {
    PromptEnhance.pending = null;
    const preview = document.getElementById('pe_preview');
    if (preview) {
        preview.style.display = 'none';
    }
}

function peShowRestore() {
    const btn = document.getElementById('pe_restore_btn');
    if (btn) {
        btn.style.display = 'inline-block';
    }
}

function peHideRestore() {
    const btn = document.getElementById('pe_restore_btn');
    if (btn) {
        btn.style.display = 'none';
    }
}

async function peHandleEnhance() {
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
        const payload = { prompt: original };
        if (PromptEnhance.settings?.sendSelectedImage) {
            const image = await peGetSelectedImage();
            if (image && image.data) {
                payload.media = [{ type: 'base64', data: image.data, mediaType: image.mediaType }];
            }
        }
        const data = await peEnhanceRequest(payload);
        if (data && data.success && typeof data.response === 'string' && data.response.length > 0) {
            peApplyEnhancement(original, data.response);
        } else {
            peShowError('PromptEnhance: ' + ((data && data.error) || 'enhancement failed.'));
        }
    } catch (err) {
        peShowError('PromptEnhance: ' + ((err && err.message) || err));
    } finally {
        PromptEnhance.enhancing = false;
        peSetLoading(false);
    }
}

function peAddPromptButtons() {
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

    bar.querySelector('#pe_enhance_btn').addEventListener('click', peHandleEnhance);
    bar.querySelector('#pe_settings_button').addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        PromptEnhance.openSettingsPanel?.();
    });
    bar.querySelector('#pe_restore_btn').addEventListener('click', () => {
        if (PromptEnhance.lastOriginal !== null) {
            peSetPrompt(PromptEnhance.lastOriginal);
            PromptEnhance.lastOriginal = null;
        }
        peHideRestore();
    });
    preview.querySelector('#pe_preview_apply').addEventListener('click', () => {
        if (PromptEnhance.pending) {
            if (PromptEnhance.lastOriginal === null) {
                PromptEnhance.lastOriginal = PromptEnhance.pending.original;
            }
            peSetPrompt(PromptEnhance.pending.enhanced);
            peShowRestore();
        }
        peHidePreview();
    });
    preview.querySelector('#pe_preview_cancel').addEventListener('click', peHidePreview);
}

function peEnsureButtons(attempt = 0) {
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
