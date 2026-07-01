'use strict';
/*
 * Frontend behavior tests for Assets/promptenhance.js — the Generate-tab surface.
 *
 * These run the real extension script in a Node `vm` sandbox with stubbed DOM / fetch / FileReader /
 * genericRequest, so the browser-side guarantees the C# tests cannot reach are actually enforced:
 *   - the reversible apply policy never destroys the prompt without a recovery path (preview/append/restore),
 *   - the loading state always clears (success, backend failure, or image-collection failure),
 *   - F3: with "send selected image" on, a failed image collection surfaces an error and ABORTS —
 *     the enhance request is never silently sent text-only.
 *
 * No external dependency (no jsdom / npm): only Node built-ins. Run: `node Tests/frontend/promptenhance.test.js`.
 */
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert');

const SCRIPT_PATH = path.join(__dirname, '..', '..', 'Assets', 'promptenhance.js');
const SCRIPT_SRC = fs.readFileSync(SCRIPT_PATH, 'utf8');

/** Builds a fresh sandbox with the script loaded and stubbed globals; returns handles the tests assert on. */
function freshEnv(opts) {
    opts = opts || {};
    const calls = { genericRequest: [], showError: [], loading: [] };

    const promptBox = { value: opts.prompt !== undefined ? opts.prompt : 'a cat', focus() {} };
    const enhanceBtn = { disabled: false, classList: { toggle(_cls, on) { calls.loading.push(!!on); } } };
    const spinner = { style: {} };
    const previewEl = { style: {} };
    const previewText = { textContent: '' };
    const restoreBtn = { style: {} };

    const byId = {
        alt_prompt_textbox: promptBox,
        pe_enhance_btn: enhanceBtn,
        pe_enhance_loading: spinner,
        pe_preview: previewEl,
        pe_preview_text: previewText,
        pe_restore_btn: restoreBtn
    };

    const image = opts.image || null; // a fake <img> ({ src }) or null (no image selected)

    const document = {
        getElementById(id) { return byId[id] || null; },
        querySelector(sel) {
            if (sel.indexOf('current_image') !== -1) { return image; }
            return null; // .alt_prompt_region etc. — init never runs in these tests
        },
        createElement() { return { style: {}, classList: { add() {} }, querySelector() { return { addEventListener() {} }; }, addEventListener() {}, insertBefore() {} }; },
        addEventListener() { /* record DOMContentLoaded but never fire it */ },
        body: { appendChild() {} }
    };

    class FakeFileReader {
        readAsDataURL(blob) {
            if (opts.readerError) { if (this.onerror) { this.onerror(); } return; }
            this.result = opts.readerResult !== undefined ? opts.readerResult
                : 'data:' + ((blob && blob.type) || 'image/png') + ';base64,QUJD';
            if (this.onloadend) { this.onloadend(); }
        }
    }

    const fetchImpl = opts.fetch || (async () => ({ blob: async () => ({ type: 'image/png' }) }));

    const genericRequest = (route, payload, onSuccess, _depth, onError) => {
        calls.genericRequest.push({ route, payload });
        const resp = opts.backendResponse || { success: true, response: 'ENHANCED PROMPT' };
        if (resp.__error) { onError(resp.__error); } else { onSuccess(resp); }
    };

    const sandbox = {};
    sandbox.window = sandbox;
    sandbox.document = document;
    sandbox.FileReader = FakeFileReader;
    sandbox.fetch = fetchImpl;
    sandbox.genericRequest = genericRequest;
    sandbox.showError = (m) => calls.showError.push(m);
    sandbox.alert = () => {};
    sandbox.triggerChangeFor = () => {};
    sandbox.setTimeout = () => {};
    sandbox.console = { error() {}, log() {}, warn() {} };

    vm.createContext(sandbox);
    vm.runInContext(SCRIPT_SRC, sandbox, { filename: 'promptenhance.js' });
    sandbox.PromptEnhance.settings = opts.settings || { sendSelectedImage: false, replaceMode: 'preview' };

    return { sandbox, calls, promptBox, enhanceBtn };
}

const tests = [];
function test(name, fn) { tests.push({ name, fn }); }

// ---- F3: image collection failure must surface + abort, never silently downgrade to text-only ----
test('F3: image-collection failure surfaces an error and does NOT send the request', async () => {
    const env = freshEnv({
        settings: { sendSelectedImage: true, replaceMode: 'preview' },
        image: { src: 'blob:current' },
        fetch: async () => { throw new Error('network down'); }
    });
    await env.sandbox.handleEnhance();
    assert.strictEqual(env.calls.genericRequest.length, 0, 'enhance request must NOT be sent when the attached image cannot be read');
    assert.strictEqual(env.calls.showError.length, 1, 'the failure must be surfaced to the user');
    assert.strictEqual(env.enhanceBtn.disabled, false, 'loading must clear even on image-collection failure');
});

test('F3: a valid selected image is attached as media', async () => {
    const env = freshEnv({
        settings: { sendSelectedImage: true, replaceMode: 'preview' },
        image: { src: 'blob:current' }
    });
    await env.sandbox.handleEnhance();
    assert.strictEqual(env.calls.genericRequest.length, 1, 'request should be sent');
    const media = env.calls.genericRequest[0].payload.media;
    assert.ok(Array.isArray(media) && media.length === 1, 'media should carry one image part');
    assert.strictEqual(media[0].data, 'QUJD', 'base64 payload should be forwarded');
});

test('No image selected is a legitimate text-only request (not a drop)', async () => {
    const env = freshEnv({ settings: { sendSelectedImage: true, replaceMode: 'preview' }, image: null });
    await env.sandbox.handleEnhance();
    assert.strictEqual(env.calls.genericRequest.length, 1, 'request should be sent text-only');
    assert.strictEqual(env.calls.genericRequest[0].payload.media, undefined, 'no media attached when there is no image');
});

// ---- Loading always clears ----
test('Loading clears on a backend failure and the error is surfaced', async () => {
    const env = freshEnv({ backendResponse: { success: false, error: 'boom' } });
    await env.sandbox.handleEnhance();
    assert.strictEqual(env.calls.genericRequest.length, 1);
    assert.strictEqual(env.calls.showError.length, 1, 'backend failure must be surfaced');
    assert.strictEqual(env.enhanceBtn.disabled, false, 'loading must clear on failure');
    assert.strictEqual(env.calls.loading[env.calls.loading.length - 1], false, 'last loading toggle must be off');
});

// ---- Reversible apply policy: never destroy the prompt without a recovery path ----
test('Preview mode does not mutate the prompt', () => {
    const env = freshEnv({ prompt: 'ORIGINAL', settings: { replaceMode: 'preview' } });
    env.sandbox.peApplyEnhancement('ORIGINAL', 'ENHANCED');
    assert.strictEqual(env.promptBox.value, 'ORIGINAL', 'preview must not touch the prompt until Apply');
});

test('Append mode keeps the original inline', () => {
    const env = freshEnv({ prompt: 'ORIGINAL', settings: { replaceMode: 'append' } });
    env.sandbox.peApplyEnhancement('ORIGINAL', 'ENHANCED');
    assert.ok(env.promptBox.value.indexOf('ORIGINAL') !== -1, 'original must be preserved');
    assert.ok(env.promptBox.value.indexOf('ENHANCED') !== -1, 'enhanced must be appended');
});

test('Replace mode stashes the original for restore', () => {
    const env = freshEnv({ prompt: 'ORIGINAL', settings: { replaceMode: 'replace_with_restore' } });
    env.sandbox.peApplyEnhancement('ORIGINAL', 'ENHANCED');
    assert.strictEqual(env.promptBox.value, 'ENHANCED', 'prompt is replaced');
    assert.strictEqual(env.sandbox.PromptEnhance.lastOriginal, 'ORIGINAL', 'original must be recoverable via Restore');
});

(async () => {
    let failed = 0;
    for (const t of tests) {
        try {
            await t.fn();
            console.log('  ok   ' + t.name);
        } catch (err) {
            failed++;
            console.log('  FAIL ' + t.name + '\n       ' + (err && err.message));
        }
    }
    console.log('\n' + (failed === 0 ? 'PASS' : 'FAIL') + ' — ' + (tests.length - failed) + '/' + tests.length + ' frontend tests passed');
    process.exit(failed === 0 ? 0 : 1);
})();
