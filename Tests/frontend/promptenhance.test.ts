/**
 * Behavior tests for the PromptEnhance frontend, run against the EMITTED
 * Assets/*.js build output (the exact files SwarmUI serves), loaded into a
 * real jsdom page modeled on SwarmUI's Generate-tab DOM.
 *
 * AUTHORITATIVE SOURCE: this .ts file; it is compiled by Tests/frontend/tsconfig.json
 * into the gitignored Tests/frontend/out/ directory and run with node.
 */
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as assert from 'node:assert';
import { JSDOM, DOMWindow } from 'jsdom';

const ASSETS = path.join(__dirname, '..', '..', '..', 'Assets');
const CONTRACTS_SRC = fs.readFileSync(path.join(ASSETS, 'contracts.js'), 'utf8');
const SETTINGS_SRC = fs.readFileSync(path.join(ASSETS, 'settings.js'), 'utf8');
const PROMPT_SRC = fs.readFileSync(path.join(ASSETS, 'promptenhance.js'), 'utf8');

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

const BARE_HTML = '<!DOCTYPE html><html><body></body></html>';

interface RecordedCall {
    route: string;
    payload: PEEnhancePayload & { settings?: Partial<PESettings> };
}

interface BootCalls {
    genericRequest: RecordedCall[];
    showError: string[];
    alerts: string[];
    consoleErrors: string[];
}

interface BootOpts {
    prompt?: string;
    settings?: Partial<PESettings>;
    image?: { src: string };
    backendResponse?: unknown;
    routeResponses?: Record<string, unknown>;
    routeErrors?: Record<string, unknown>;
    fetch?: (url: string) => Promise<{ blob(): Promise<Blob> }>;
    pageHtml?: string;
    throwingShowError?: boolean;
}

type PETestWindow = DOMWindow & {
    PromptEnhance: PromptEnhanceNamespace;
    peAddPromptButtons: () => void;
    peHandleEnhance: () => Promise<void>;
    peApplyEnhancement: (original: string, enhanced: string) => void;
    peEnsureButtons: (attempt?: number) => void;
    peSaveSettings: () => Promise<boolean>;
    peResetSettings: () => Promise<boolean>;
    peFetchModels: () => Promise<void>;
    peShowError: (message: string) => void;
};

interface BootResult {
    dom: JSDOM;
    win: PETestWindow;
    doc: Document;
    calls: BootCalls;
}

async function boot(opts: BootOpts): Promise<BootResult> {
    const calls: BootCalls = { genericRequest: [], showError: [], alerts: [], consoleErrors: [] };
    const dom = new JSDOM(opts.pageHtml ?? PAGE_HTML, { runScripts: 'dangerously', url: 'http://localhost/' });
    const win = dom.window as PETestWindow;
    const doc = win.document;

    win.genericRequest = ((route: string, payload: object, onSuccess: (data: unknown) => void, _depth?: number, onError?: (err: unknown) => void) => {
        calls.genericRequest.push({ route, payload: payload as RecordedCall['payload'] });
        if (opts.routeErrors && route in opts.routeErrors) {
            (onError ?? (() => { }))(opts.routeErrors[route]);
            return;
        }
        const resp = (opts.routeResponses && route in opts.routeResponses)
            ? opts.routeResponses[route]
            : (opts.backendResponse ?? { success: true, response: 'ENHANCED PROMPT' });
        onSuccess(resp);
    }) as unknown as typeof win.genericRequest;
    if (opts.throwingShowError) {
        win.showError = () => { throw new Error('host banner is broken'); };
    } else {
        win.showError = (m: string) => { calls.showError.push(m); };
    }
    win.alert = ((m: string) => { calls.alerts.push(m); }) as typeof win.alert;
    const realConsoleError = win.console.error.bind(win.console);
    win.console.error = ((...args: unknown[]) => {
        calls.consoleErrors.push(args.map(String).join(' '));
        realConsoleError(...args);
    }) as typeof win.console.error;
    win.triggerChangeFor = () => { };
    if (opts.fetch) {
        win.fetch = opts.fetch as unknown as typeof win.fetch;
    }

    if (opts.prompt !== undefined) {
        (doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value = opts.prompt;
    }
    if (opts.image) {
        const img = doc.createElement('img');
        img.src = opts.image.src;
        doc.getElementById('current_image')!.appendChild(img);
    }

    for (const src of [CONTRACTS_SRC, SETTINGS_SRC, PROMPT_SRC]) {
        const s = doc.createElement('script');
        s.textContent = src;
        doc.body.appendChild(s);
    }
    assert.strictEqual(typeof win.peAddPromptButtons, 'function', 'peAddPromptButtons must load as a real window function');
    assert.strictEqual(typeof win.peHandleEnhance, 'function', 'peHandleEnhance must load as a real window function');

    await new Promise((resolve) => setTimeout(resolve, 0));
    await new Promise((resolve) => setTimeout(resolve, 0));

    if (opts.settings) {
        win.PromptEnhance.settings = opts.settings;
    }
    return { dom, win, doc, calls };
}

const tests: { name: string; fn: () => void | Promise<void> }[] = [];
function test(name: string, fn: () => void | Promise<void>): void {
    tests.push({ name, fn });
}

test('Injects a REAL button bar into the REAL .alt_prompt_region and parses its markup', async () => {
    const { win, doc } = await boot({});
    win.peAddPromptButtons();

    const bar = doc.getElementById('pe_button_bar');
    assert.ok(bar, '#pe_button_bar must exist as a real element after injection');
    const region = doc.getElementById('alt_prompt_region')!;
    assert.ok(region.contains(bar), '#pe_button_bar must be a real child of .alt_prompt_region');

    const enhance = doc.getElementById('pe_enhance_btn');
    assert.ok(enhance, '#pe_enhance_btn must be a real parsed element (proves innerHTML was really parsed)');
    assert.strictEqual(enhance.tagName, 'BUTTON', '#pe_enhance_btn must be a real <button>');
    assert.ok(doc.getElementById('pe_settings_button'), '#pe_settings_button must be a real parsed element');
    assert.strictEqual(bar.querySelector('#pe_enhance_btn'), enhance, 'querySelector inside the bar resolves the same real button node');
});

test('Injection is idempotent: a second peAddPromptButtons() does not duplicate the bar', async () => {
    const { win, doc } = await boot({});
    win.peAddPromptButtons();
    win.peAddPromptButtons();
    const bars = doc.querySelectorAll('#pe_button_bar');
    assert.strictEqual(bars.length, 1, 'exactly one #pe_button_bar after two calls (real guard exercised)');
});

test('A REAL click on the injected Enhance button fires the handler (empty prompt surfaces an error)', async () => {
    const { win, doc, calls } = await boot({ prompt: '' });
    win.peAddPromptButtons();
    const btn = doc.getElementById('pe_enhance_btn')!;
    btn.dispatchEvent(new win.MouseEvent('click', { bubbles: true, cancelable: true }));
    assert.strictEqual(calls.showError.length, 1, 'a real click on a real button drives peHandleEnhance');
    assert.ok(/prompt to enhance/i.test(calls.showError[0]!), 'empty-prompt error text is surfaced');
    assert.strictEqual(calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun').length, 0, 'no enhance request for an empty prompt');
});

test('F3: image-collection failure surfaces an error, does NOT send the request, and clears loading', async () => {
    const { win, doc, calls } = await boot({
        settings: { sendSelectedImage: true, replaceMode: 'preview' },
        image: { src: 'blob:current' },
        fetch: async () => { throw new Error('network down'); }
    });
    win.peAddPromptButtons();
    (doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value = 'a cat';
    await win.peHandleEnhance();
    const runs = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun');
    assert.strictEqual(runs.length, 0, 'must NOT send the enhance request when an attached image cannot be read');
    assert.strictEqual(calls.showError.length, 1, 'the failure must be surfaced');
    assert.strictEqual((doc.getElementById('pe_enhance_btn') as HTMLButtonElement).disabled, false, 'loading must clear even on image-collection failure');
});

test('F3: a valid selected image is read via the REAL FileReader and attached as base64 media', async () => {
    const { win, doc, calls } = await boot({
        settings: { sendSelectedImage: true, replaceMode: 'preview' },
        image: { src: 'blob:current' },
        fetch: async () => ({ blob: async () => new win.Blob([new Uint8Array([65, 66, 67])], { type: 'image/png' }) })
    });
    win.peAddPromptButtons();
    (doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value = 'a cat';
    await win.peHandleEnhance();
    const runs = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun');
    assert.strictEqual(runs.length, 1, 'one enhance request should be sent');
    const media = runs[0]!.payload.media;
    assert.ok(Array.isArray(media) && media.length === 1, 'media should carry one image part');
    assert.strictEqual(media[0]!.data, 'QUJD', 'base64 from the real FileReader should be forwarded');
    assert.strictEqual(media[0]!.mediaType, 'image/png', 'media type from the real Blob should be forwarded');
});

test('No image selected is a legitimate text-only request (not a hard drop)', async () => {
    const { win, doc, calls } = await boot({ settings: { sendSelectedImage: true, replaceMode: 'preview' } });
    win.peAddPromptButtons();
    (doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value = 'a cat';
    await win.peHandleEnhance();
    const runs = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun');
    assert.strictEqual(runs.length, 1, 'one enhance request should be sent text-only');
    assert.strictEqual(runs[0]!.payload.media, undefined, 'no media attached when no image is selected');
});

test('Loading clears on backend failure and the error is surfaced', async () => {
    const { win, doc, calls } = await boot({ prompt: 'a cat', backendResponse: { success: false, error: 'boom' } });
    win.peAddPromptButtons();
    await win.peHandleEnhance();
    const runs = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun');
    assert.strictEqual(runs.length, 1, 'one enhance request was sent');
    assert.strictEqual(calls.showError.length, 1, 'backend failure must be surfaced');
    assert.ok(calls.showError[0]!.includes('boom'), 'the classified server error text is surfaced');
    assert.strictEqual((doc.getElementById('pe_enhance_btn') as HTMLButtonElement).disabled, false, 'loading must clear on failure');
    assert.ok(!(doc.getElementById('pe_enhance_btn') as HTMLButtonElement).classList.contains('loading'), 'loading class must be removed');
});

test('Preview mode does not mutate the real prompt textarea', async () => {
    const { win, doc } = await boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'preview' } });
    win.peAddPromptButtons();
    win.peApplyEnhancement('ORIGINAL', 'ENHANCED');
    assert.strictEqual((doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value, 'ORIGINAL', 'preview must not touch the prompt until Apply');
    const preview = doc.getElementById('pe_preview')!;
    assert.strictEqual(preview.style.display, 'block', 'preview panel is shown');
    assert.strictEqual(doc.getElementById('pe_preview_text')!.textContent, 'ENHANCED', 'preview shows the enhanced text');
});

test('Append mode keeps the original inline in the real textarea', async () => {
    const { win, doc } = await boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'append' } });
    win.peAddPromptButtons();
    win.peApplyEnhancement('ORIGINAL', 'ENHANCED');
    const val = (doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value;
    assert.ok(val.indexOf('ORIGINAL') !== -1, 'original must be preserved');
    assert.ok(val.indexOf('ENHANCED') !== -1, 'enhanced must be appended');
});

test('Replace mode replaces the real textarea, stashes the original, and shows the real Restore button', async () => {
    const { win, doc } = await boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'replace_with_restore' } });
    win.peAddPromptButtons();
    win.peApplyEnhancement('ORIGINAL', 'ENHANCED');
    assert.strictEqual((doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value, 'ENHANCED', 'prompt is replaced');
    assert.strictEqual(win.PromptEnhance.lastOriginal, 'ORIGINAL', 'original must be recoverable');
    assert.strictEqual(doc.getElementById('pe_restore_btn')!.style.display, 'inline-block', 'Restore button is shown');
    doc.getElementById('pe_restore_btn')!.dispatchEvent(new win.MouseEvent('click', { bubbles: true }));
    assert.strictEqual((doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value, 'ORIGINAL', 'Restore returns the original prompt');
});

test('Replace mode preserves the TRUE original across a second enhance (Restore is not one-level)', async () => {
    const { win, doc } = await boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'replace_with_restore' } });
    win.peAddPromptButtons();
    win.peApplyEnhancement('ORIGINAL', 'ENHANCED1');
    win.peApplyEnhancement('ENHANCED1', 'ENHANCED2');
    assert.strictEqual((doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value, 'ENHANCED2', 'second enhance replaces the box');
    assert.strictEqual(win.PromptEnhance.lastOriginal, 'ORIGINAL', 'the earliest original must be preserved, not the intermediate');
    doc.getElementById('pe_restore_btn')!.dispatchEvent(new win.MouseEvent('click', { bubbles: true }));
    assert.strictEqual((doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value, 'ORIGINAL', 'Restore returns the TRUE original after multiple enhances');
});

test('Opening settings mounts a REAL panel into document.body and shows it', async () => {
    const { win, doc } = await boot({});
    assert.strictEqual(typeof win.PromptEnhance.openSettingsPanel, 'function', 'openSettingsPanel exported');
    assert.strictEqual(typeof win.PromptEnhance.loadSettings, 'function', 'loadSettings exported');
    assert.strictEqual(typeof win.PromptEnhance.fetchModels, 'function', 'fetchModels exported');
    win.PromptEnhance.openSettingsPanel!();
    const panel = doc.getElementById('pe_settings_panel');
    assert.ok(panel, '#pe_settings_panel is a real element in the document');
    assert.strictEqual(panel.parentNode, doc.body, 'panel is mounted on document.body');
    assert.strictEqual(panel.style.display, 'block', 'panel is shown');
    assert.strictEqual(doc.getElementById('pe_save_btn')!.tagName, 'BUTTON', 'a real Save button exists in the panel');
});

test('No bare generic globals leak onto window (must be pe-prefixed / namespaced)', async () => {
    const { win } = await boot({});
    for (const generic of ['loadSettings', 'saveSettings', 'resetSettings', 'fetchModels', 'openSettingsPanel', 'closeSettingsPanel']) {
        assert.strictEqual((win as unknown as Record<string, unknown>)[generic], undefined, `no bare global '${generic}'`);
    }
    assert.strictEqual(typeof win.PromptEnhance.openSettingsPanel, 'function', 'namespaced API present');
});

test('loadSettings merges server settings over defaults and rejects wrongly typed values', async () => {
    const { win } = await boot({
        routeResponses: {
            GetPromptEnhanceSettings: { success: true, settings: { model: 'mock-enhancer', timeoutSeconds: 'ninety', replaceMode: 'append' } }
        }
    });
    await win.PromptEnhance.loadSettings!();
    assert.strictEqual(win.PromptEnhance.settings!.model, 'mock-enhancer', 'server value overrides the default');
    assert.strictEqual(win.PromptEnhance.settings!.timeoutSeconds, 60, 'a wrongly typed server value is rejected by the adapter, keeping the default');
    assert.strictEqual(win.PromptEnhance.settings!.replaceMode, 'append', 'valid enum value is accepted');
    assert.strictEqual(win.PromptEnhance.settings!.baseUrl, 'http://localhost:11434', 'unsent keys keep their defaults');
});

test('Save settings round-trips the panel values and surfaces Saved status', async () => {
    const { win, doc, calls } = await boot({
        routeResponses: {
            SavePromptEnhanceSettings: { success: true, settings: { model: 'saved-model' } },
            PromptEnhanceListModels: { success: true, models: [{ id: 'saved-model' }] }
        }
    });
    win.PromptEnhance.openSettingsPanel!();
    (doc.getElementById('pe_base_url') as HTMLInputElement).value = '  http://box:8080/v1  ';
    const ok = await win.peSaveSettings();
    assert.strictEqual(ok, true, 'save resolves true on success');
    const saves = calls.genericRequest.filter((c) => c.route === 'SavePromptEnhanceSettings');
    assert.strictEqual(saves.length, 1, 'one save request sent');
    assert.strictEqual(saves[0]!.payload.settings!.baseUrl, 'http://box:8080/v1', 'panel value is trimmed and sent');
    assert.strictEqual(win.PromptEnhance.settings!.model, 'saved-model', 'server-confirmed settings are merged back');
    assert.strictEqual(doc.getElementById('pe_settings_status')!.textContent, 'Saved.', 'status line reports the save');
});

test('Save failure surfaces the classified error in the status line and resolves false', async () => {
    const { win, doc } = await boot({
        routeResponses: {
            SavePromptEnhanceSettings: { success: false, error: 'Timeout (seconds) must be a whole number' },
            PromptEnhanceListModels: { success: true, models: [{ id: 'm' }] }
        }
    });
    win.PromptEnhance.openSettingsPanel!();
    const ok = await win.peSaveSettings();
    assert.strictEqual(ok, false, 'save resolves false on rejection');
    const status = doc.getElementById('pe_settings_status')!;
    assert.ok(status.textContent!.startsWith('Save failed: '), 'failure status prefix present');
    assert.ok(status.textContent!.includes('Timeout (seconds)'), 'server validation text is surfaced verbatim');
    assert.ok(status.className.includes('error'), 'status line carries the error class');
});

test('Reset restores defaults, repopulates the panel, and refreshes the model list', async () => {
    const { win, doc, calls } = await boot({
        settings: { baseUrl: 'http://elsewhere:1', model: 'weird' },
        routeResponses: {
            ResetPromptEnhanceSettings: { success: true, settings: { baseUrl: 'http://localhost:11434', model: '' } },
            PromptEnhanceListModels: { success: true, models: [{ id: 'mock-enhancer' }] }
        }
    });
    win.PromptEnhance.openSettingsPanel!();
    const ok = await win.peResetSettings();
    assert.strictEqual(ok, true, 'reset resolves true on success');
    assert.strictEqual((doc.getElementById('pe_base_url') as HTMLInputElement).value, 'http://localhost:11434', 'panel repopulated with defaults');
    assert.strictEqual(win.PromptEnhance.settings!.model, '', 'server-confirmed defaults are merged back');
    const refreshes = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceListModels');
    assert.ok(refreshes.length >= 1, 'reset triggers a model-list refresh');
    const status = doc.getElementById('pe_settings_status')!;
    assert.ok(!status.className.includes('error'), 'the reset flow ends without an error state (the chained model refresh cleared the transient status)');
});

test('fetchModels populates REAL options and preselects the configured model', async () => {
    const { win, doc } = await boot({
        routeResponses: {
            PromptEnhanceListModels: { success: true, models: [{ id: 'a' }, { id: 'mock-enhancer', name: 'Mock Enhancer' }, { id: '' }, 'garbage'] }
        }
    });
    win.PromptEnhance.openSettingsPanel!();
    win.PromptEnhance.settings!.model = 'mock-enhancer';
    await win.peFetchModels();
    const select = doc.getElementById('pe_model_select') as HTMLSelectElement;
    const values = [...select.options].map((o) => o.value);
    assert.deepStrictEqual(values, ['', 'a', 'mock-enhancer'], 'placeholder plus each valid id; empty-id and non-object entries dropped by the adapter');
    assert.strictEqual(select.options[2]!.text, 'Mock Enhancer', 'display name comes from the wire name when present');
    assert.strictEqual(select.value, 'mock-enhancer', 'configured model is preselected');
});

test('fetchModels failure shows a disabled explanatory option and an error status', async () => {
    const { win, doc } = await boot({
        routeResponses: {
            PromptEnhanceListModels: { success: false, error: 'Cannot reach the LLM backend.' }
        }
    });
    win.PromptEnhance.openSettingsPanel!();
    await win.peFetchModels();
    const select = doc.getElementById('pe_model_select') as HTMLSelectElement;
    assert.strictEqual(select.options.length, 1, 'exactly one explanatory option');
    assert.strictEqual(select.options[0]!.disabled, true, 'the explanatory option is not selectable');
    assert.ok(select.options[0]!.text.includes('No models'), 'the option explains the empty list');
    const status = doc.getElementById('pe_settings_status')!;
    assert.strictEqual(status.textContent, 'Cannot reach the LLM backend.', 'classified backend error is surfaced verbatim');
});

test('fetchModels transport error (onError path) is classified, not swallowed', async () => {
    const { win, doc } = await boot({
        routeErrors: { PromptEnhanceListModels: new Error('socket hang up') }
    });
    win.PromptEnhance.openSettingsPanel!();
    await win.peFetchModels();
    const select = doc.getElementById('pe_model_select') as HTMLSelectElement;
    assert.strictEqual(select.options[0]!.disabled, true, 'error placeholder option is disabled');
    assert.ok(select.options[0]!.text.includes('Error loading models'), 'transport failure is explained in the dropdown');
    assert.strictEqual(doc.getElementById('pe_settings_status')!.textContent, 'socket hang up', 'transport error text reaches the status line');
});

test('peEnsureButtons retries until the Generate-tab region appears (async SwarmUI render)', async () => {
    const { win, doc } = await boot({ pageHtml: BARE_HTML });
    win.peEnsureButtons();
    assert.strictEqual(doc.getElementById('pe_button_bar'), null, 'no bar while the region is absent');
    const region = doc.createElement('div');
    region.className = 'alt_prompt_region';
    region.innerHTML = '<textarea id="alt_prompt_textbox"></textarea>';
    doc.body.appendChild(region);
    await new Promise((resolve) => setTimeout(resolve, 600));
    assert.ok(doc.getElementById('pe_button_bar'), 'the retry loop injects the bar once the region exists');
});

test('peShowError still reaches the user when the host showError itself throws', async () => {
    const { win, calls } = await boot({ throwingShowError: true });
    win.peShowError('the backend is on fire');
    assert.strictEqual(calls.alerts.length, 1, 'the alert fallback fires');
    assert.strictEqual(calls.alerts[0], 'the backend is on fire', 'the original message survives the fallback');
    assert.ok(calls.consoleErrors.some((line) => line.includes('host showError failed')), 'the host failure is logged, not swallowed');
});

(async () => {
    let failed = 0;
    for (const t of tests) {
        try {
            await t.fn();
            console.log('  ok   ' + t.name);
        } catch (err) {
            failed++;
            console.log('  FAIL ' + t.name + '\n       ' + (err instanceof Error && err.stack ? err.stack : String(err)));
        }
    }
    console.log('\n' + (failed === 0 ? 'PASS' : 'FAIL') + ' — ' + (tests.length - failed) + '/' + tests.length + ' frontend tests passed');
    process.exit(failed === 0 ? 0 : 1);
})();
