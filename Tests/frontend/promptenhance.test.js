'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert');
const { JSDOM } = require('jsdom');

const ASSETS = path.join(__dirname, '..', '..', 'Assets');
const PROMPT_SRC = fs.readFileSync(path.join(ASSETS, 'promptenhance.js'), 'utf8');
const SETTINGS_SRC = fs.readFileSync(path.join(ASSETS, 'settings.js'), 'utf8');

const PAGE_HTML = `<!DOCTYPE html><html><body>
  <div class="current_image drag_image_target" id="current_image"></div>
  <div class="alt_prompt_region drag_image_target" id="alt_prompt_region">
    <div class="alt_prompt_main_line">
      <div class="alt_prompt_textboxes">
        <textarea id="alt_prompt_textbox" rows="1"></textarea>
        <textarea id="alt_negativeprompt_textbox" rows="1"></textarea>
      </div>
    </div>
  </div>
</body></html>`;

function boot(opts) {
    opts = opts || {};
    const calls = { genericRequest: [], showError: [] };
    const dom = new JSDOM(PAGE_HTML, { runScripts: 'dangerously', url: 'http://localhost/' });
    const win = dom.window;
    const doc = win.document;

    win.genericRequest = (route, payload, onSuccess, _depth, onError) => {
        calls.genericRequest.push({ route, payload });
        const resp = opts.backendResponse || { success: true, response: 'ENHANCED PROMPT' };
        if (resp.__error) { (onError || (() => {}))(resp.__error); }
        else { onSuccess(resp); }
    };
    win.showError = (m) => { calls.showError.push(m); };
    win.triggerChangeFor = () => {};
    if (opts.fetch) { win.fetch = opts.fetch; }

    if (opts.prompt !== undefined) { doc.getElementById('alt_prompt_textbox').value = opts.prompt; }
    if (opts.image) {
        const img = doc.createElement('img');
        img.src = opts.image.src;
        doc.getElementById('current_image').appendChild(img);
    }

    for (const src of [SETTINGS_SRC, PROMPT_SRC]) {
        const s = doc.createElement('script');
        s.textContent = src;
        doc.body.appendChild(s);
    }
    assert.strictEqual(typeof win.peAddPromptButtons, 'function', 'peAddPromptButtons must load as a real window function');
    assert.strictEqual(typeof win.peHandleEnhance, 'function', 'peHandleEnhance must load as a real window function');

    if (opts.settings) { win.PromptEnhance.settings = opts.settings; }
    return { dom, win, doc, calls };
}

const tests = [];
function test(name, fn) { tests.push({ name, fn }); }

test('Injects a REAL button bar into the REAL .alt_prompt_region and parses its markup', () => {
    const { win, doc } = boot({});
    win.peAddPromptButtons();

    const bar = doc.getElementById('pe_button_bar');
    assert.ok(bar, '#pe_button_bar must exist as a real element after injection');
    const region = doc.getElementById('alt_prompt_region');
    assert.ok(region.contains(bar), '#pe_button_bar must be a real child of .alt_prompt_region');

    const enhance = doc.getElementById('pe_enhance_btn');
    assert.ok(enhance, '#pe_enhance_btn must be a real parsed element (proves innerHTML was really parsed)');
    assert.strictEqual(enhance.tagName, 'BUTTON', '#pe_enhance_btn must be a real <button>');
    assert.ok(doc.getElementById('pe_settings_button'), '#pe_settings_button must be a real parsed element');
    assert.strictEqual(bar.querySelector('#pe_enhance_btn'), enhance, 'querySelector inside the bar resolves the same real button node');
});

test('Injection is idempotent: a second peAddPromptButtons() does not duplicate the bar', () => {
    const { win, doc } = boot({});
    win.peAddPromptButtons();
    win.peAddPromptButtons();
    const bars = doc.querySelectorAll('#pe_button_bar');
    assert.strictEqual(bars.length, 1, 'exactly one #pe_button_bar after two calls (real guard exercised)');
});

test('A REAL click on the injected Enhance button fires the handler (empty prompt surfaces an error)', () => {
    const { win, doc, calls } = boot({ prompt: '' });
    win.peAddPromptButtons();
    const btn = doc.getElementById('pe_enhance_btn');
    btn.dispatchEvent(new win.MouseEvent('click', { bubbles: true, cancelable: true }));
    assert.strictEqual(calls.showError.length, 1, 'a real click on a real button drives peHandleEnhance');
    assert.ok(/prompt to enhance/i.test(calls.showError[0]), 'empty-prompt error text is surfaced');
    assert.strictEqual(calls.genericRequest.length, 0, 'no backend call for an empty prompt');
});

test('F3: image-collection failure surfaces an error, does NOT send the request, and clears loading', async () => {
    const { win, doc, calls } = boot({
        settings: { sendSelectedImage: true, replaceMode: 'preview' },
        image: { src: 'blob:current' },
        fetch: async () => { throw new Error('network down'); }
    });
    win.peAddPromptButtons();
    doc.getElementById('alt_prompt_textbox').value = 'a cat';
    await win.peHandleEnhance();
    const runs = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun');
    assert.strictEqual(runs.length, 0, 'must NOT send the enhance request when an attached image cannot be read');
    assert.strictEqual(calls.showError.length, 1, 'the failure must be surfaced');
    assert.strictEqual(doc.getElementById('pe_enhance_btn').disabled, false, 'loading must clear even on image-collection failure');
});

test('F3: a valid selected image is read via the REAL FileReader and attached as base64 media', async () => {
    const { win, doc, calls } = boot({
        settings: { sendSelectedImage: true, replaceMode: 'preview' },
        image: { src: 'blob:current' },
        fetch: async () => ({ blob: async () => new win.Blob([new Uint8Array([65, 66, 67])], { type: 'image/png' }) })
    });
    win.peAddPromptButtons();
    doc.getElementById('alt_prompt_textbox').value = 'a cat';
    await win.peHandleEnhance();
    const runs = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun');
    assert.strictEqual(runs.length, 1, 'one enhance request should be sent');
    const media = runs[0].payload.media;
    assert.ok(Array.isArray(media) && media.length === 1, 'media should carry one image part');
    assert.strictEqual(media[0].data, 'QUJD', 'base64 from the real FileReader should be forwarded');
    assert.strictEqual(media[0].mediaType, 'image/png', 'media type from the real Blob should be forwarded');
});

test('No image selected is a legitimate text-only request (not a hard drop)', async () => {
    const { win, doc, calls } = boot({ settings: { sendSelectedImage: true, replaceMode: 'preview' } });
    win.peAddPromptButtons();
    doc.getElementById('alt_prompt_textbox').value = 'a cat';
    await win.peHandleEnhance();
    const runs = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun');
    assert.strictEqual(runs.length, 1, 'one enhance request should be sent text-only');
    assert.strictEqual(runs[0].payload.media, undefined, 'no media attached when no image is selected');
});

test('Loading clears on backend failure and the error is surfaced', async () => {
    const { win, doc, calls } = boot({ prompt: 'a cat', backendResponse: { success: false, error: 'boom' } });
    win.peAddPromptButtons();
    await win.peHandleEnhance();
    const runs = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun');
    assert.strictEqual(runs.length, 1, 'one enhance request was sent');
    assert.strictEqual(calls.showError.length, 1, 'backend failure must be surfaced');
    assert.strictEqual(doc.getElementById('pe_enhance_btn').disabled, false, 'loading must clear on failure');
    assert.ok(!doc.getElementById('pe_enhance_btn').classList.contains('loading'), 'loading class must be removed');
});

test('Preview mode does not mutate the real prompt textarea', () => {
    const { win, doc } = boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'preview' } });
    win.peAddPromptButtons();
    win.peApplyEnhancement('ORIGINAL', 'ENHANCED');
    assert.strictEqual(doc.getElementById('alt_prompt_textbox').value, 'ORIGINAL', 'preview must not touch the prompt until Apply');
    const preview = doc.getElementById('pe_preview');
    assert.strictEqual(preview.style.display, 'block', 'preview panel is shown');
    assert.strictEqual(doc.getElementById('pe_preview_text').textContent, 'ENHANCED', 'preview shows the enhanced text');
});

test('Append mode keeps the original inline in the real textarea', () => {
    const { win, doc } = boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'append' } });
    win.peAddPromptButtons();
    win.peApplyEnhancement('ORIGINAL', 'ENHANCED');
    const val = doc.getElementById('alt_prompt_textbox').value;
    assert.ok(val.indexOf('ORIGINAL') !== -1, 'original must be preserved');
    assert.ok(val.indexOf('ENHANCED') !== -1, 'enhanced must be appended');
});

test('Replace mode replaces the real textarea, stashes the original, and shows the real Restore button', () => {
    const { win, doc } = boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'replace_with_restore' } });
    win.peAddPromptButtons();
    win.peApplyEnhancement('ORIGINAL', 'ENHANCED');
    assert.strictEqual(doc.getElementById('alt_prompt_textbox').value, 'ENHANCED', 'prompt is replaced');
    assert.strictEqual(win.PromptEnhance.lastOriginal, 'ORIGINAL', 'original must be recoverable');
    assert.strictEqual(doc.getElementById('pe_restore_btn').style.display, 'inline-block', 'Restore button is shown');
    doc.getElementById('pe_restore_btn').dispatchEvent(new win.MouseEvent('click', { bubbles: true }));
    assert.strictEqual(doc.getElementById('alt_prompt_textbox').value, 'ORIGINAL', 'Restore returns the original prompt');
});

test('Replace mode preserves the TRUE original across a second enhance (Restore is not one-level)', () => {
    const { win, doc } = boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'replace_with_restore' } });
    win.peAddPromptButtons();
    win.peApplyEnhancement('ORIGINAL', 'ENHANCED1');
    win.peApplyEnhancement('ENHANCED1', 'ENHANCED2');
    assert.strictEqual(doc.getElementById('alt_prompt_textbox').value, 'ENHANCED2', 'second enhance replaces the box');
    assert.strictEqual(win.PromptEnhance.lastOriginal, 'ORIGINAL', 'the earliest original must be preserved, not the intermediate');
    doc.getElementById('pe_restore_btn').dispatchEvent(new win.MouseEvent('click', { bubbles: true }));
    assert.strictEqual(doc.getElementById('alt_prompt_textbox').value, 'ORIGINAL', 'Restore returns the TRUE original after multiple enhances');
});

test('Opening settings mounts a REAL panel into document.body and shows it', () => {
    const { win, doc } = boot({});
    assert.strictEqual(typeof win.PromptEnhance.openSettingsPanel, 'function', 'openSettingsPanel exported');
    assert.strictEqual(typeof win.PromptEnhance.loadSettings, 'function', 'loadSettings exported');
    assert.strictEqual(typeof win.PromptEnhance.fetchModels, 'function', 'fetchModels exported');
    win.PromptEnhance.openSettingsPanel();
    const panel = doc.getElementById('pe_settings_panel');
    assert.ok(panel, '#pe_settings_panel is a real element in the document');
    assert.strictEqual(panel.parentNode, doc.body, 'panel is mounted on document.body');
    assert.strictEqual(panel.style.display, 'block', 'panel is shown');
    assert.strictEqual(doc.getElementById('pe_save_btn').tagName, 'BUTTON', 'a real Save button exists in the panel');
});

test('No bare generic globals leak onto window (must be pe-prefixed / namespaced)', () => {
    const { win } = boot({});
    for (const generic of ['loadSettings', 'saveSettings', 'resetSettings', 'fetchModels', 'openSettingsPanel', 'closeSettingsPanel']) {
        assert.strictEqual(win[generic], undefined, `no bare global '${generic}'`);
    }
    assert.strictEqual(typeof win.PromptEnhance.openSettingsPanel, 'function', 'namespaced API present');
});

(async () => {
    let failed = 0;
    for (const t of tests) {
        try {
            await t.fn();
            console.log('  ok   ' + t.name);
        } catch (err) {
            failed++;
            console.log('  FAIL ' + t.name + '\n       ' + (err && err.stack ? err.stack : err));
        }
    }
    console.log('\n' + (failed === 0 ? 'PASS' : 'FAIL') + ' — ' + (tests.length - failed) + '/' + tests.length + ' frontend tests passed');
    process.exit(failed === 0 ? 0 : 1);
})();
