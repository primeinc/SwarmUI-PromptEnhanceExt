/**
 * Behavior tests for the PromptEnhance frontend, run against the EMITTED Assets/*.js build output
 * (the exact files SwarmUI serves) together with SwarmUI's real util.js, loaded into a jsdom page
 * modeled on SwarmUI's Generate-tab DOM. The settings modal needs SwarmUI's site.js and Bootstrap
 * and is covered by the browser gates (Tests/ui).
 *
 * AUTHORITATIVE SOURCE: this .ts file; it is compiled by Tests/frontend/tsconfig.json into the
 * gitignored Tests/frontend/out/ directory and run with node.
 */
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as assert from 'node:assert';
import { JSDOM, type DOMWindow } from 'jsdom';

const REPO = path.join(__dirname, '..', '..', '..');
const ASSETS = path.join(REPO, 'Assets');
const CONTRACTS_SRC = fs.readFileSync(path.join(ASSETS, 'contracts.js'), 'utf8');
const SETTINGS_SRC = fs.readFileSync(path.join(ASSETS, 'settings.js'), 'utf8');
const PROMPT_SRC = fs.readFileSync(path.join(ASSETS, 'promptenhance.js'), 'utf8');

/** SwarmUI's util.js: vendored standalone workspace first, then the host layout `<SwarmUI>/src/Extensions/PromptEnhance`. */
function readHostUtilJs(): string {
    const candidates = [
        path.join(REPO, 'vendor', 'SwarmUI', 'src', 'wwwroot', 'js', 'util.js'),
        path.join(REPO, '..', '..', 'wwwroot', 'js', 'util.js'),
    ];
    for (const candidate of candidates) {
        if (fs.existsSync(candidate)) {
            return fs.readFileSync(candidate, 'utf8');
        }
    }
    throw new Error(`SwarmUI util.js not found; looked in: ${candidates.join(', ')}. Run \`just vendor-sync\` or place the extension in a SwarmUI checkout.`);
}
const UTIL_SRC = readHostUtilJs();

interface PEContractFile {
    routes: Record<string, string>;
    apiKeyType: string;
    errorCategories: Record<string, string>;
    store: { dataname: string; name: string };
    settings: Record<string, { type: string; default: unknown; min?: number; max?: number; enum?: string[] }>;
}
const CONTRACT: PEContractFile = JSON.parse(fs.readFileSync(path.join(REPO, 'contracts', 'pe-contract.json'), 'utf8'));

/** SwarmUI's prompt region markup (src/Pages/_Generate/GenerateTab.cshtml). */
const PAGE_HTML = `<!DOCTYPE html><html><body>
  <div class="current_image drag_image_target" id="current_image"></div>
  <div class="alt_prompt_region drag_image_target drag_audio_target" id="alt_prompt_region">
    <div id="alt_prompt_extra_area" class="alt_prompt_extra_area">
      <button id="alt_prompt_image_clear_button" style="display: none;">Clear Attachments</button>
      <div class="added-image-area alt-prompt-added-image-area" id="alt_prompt_image_area"></div>
    </div>
    <div class="alt_prompt_main_line">
      <div class="alt_prompt_textboxes">
        <textarea id="alt_prompt_textbox" rows="1"></textarea>
        <textarea id="alt_negativeprompt_textbox" rows="1"></textarea>
      </div>
    </div>
  </div>
</body></html>`;

/** Host functions the harness stubs in place of SwarmUI's site.js and genpage scripts. */
const HOST_STUBS = ['genericRequest', 'showError', 'triggerChangeFor', 'genTabLayout', 'sessionReadyCallbacks'];

interface RecordedCall {
    route: string;
    payload: PEEnhancePayload & { settings?: Partial<PESettings> };
}

interface BootCalls {
    genericRequest: RecordedCall[];
    showError: string[];
    alerts: string[];
    consoleErrors: string[];
    consoleWarns: string[];
}

interface BootOpts {
    prompt?: string;
    settings?: Partial<PESettings>;
    image?: { src: string };
    backendResponse?: unknown;
    routeResponses?: Record<string, unknown>;
    routeErrors?: Record<string, unknown>;
    fetch?: (url: string) => Promise<{ blob(): Promise<Blob> }>;
    throwingShowError?: boolean;
    genTabLayout?: SwarmGenTabLayout;
    /** When true, boot leaves `sessionReadyCallbacks` unfired; the test fires them via `fireSessionReady`. */
    holdSessionReady?: boolean;
}

/** The members of `promptEnhanceGenTab` (Frontend/promptenhance.ts) these tests drive. */
interface PEGenTabSurface {
    ready: Promise<void> | null;
    lastOriginal: string | null;
    mount(): void;
    handleEnhance(): Promise<void>;
    applyEnhancement(original: string, enhanced: string): void;
    showError(message: string): void;
}

/** The members of `promptEnhanceSettings` (Frontend/settings.ts) these tests drive. */
interface PESettingsSurface {
    effective(): PESettings;
    apply(settings: Partial<PESettings>): void;
}

/** The extension's global surface as the page sees it; `let` and `class` globals are read through the page realm. */
interface PEGlobals {
    genTab: PEGenTabSurface;
    settings: PESettingsSurface;
    PE_ROUTES: PERoutes;
    PE_API_KEY_TYPE: string;
    PE_LIMITS: PELimits;
    PE_REPLACE_MODES: readonly PEReplaceMode[];
    PE_DEFAULT_SETTINGS: PESettings;
    peAdaptSettingsResult: (data: unknown) => PESettingsResult;
    peNormalizeSettings: (raw: PERawSettingsInput, current: PESettings) => PESettings;
}

type PETestWindow = DOMWindow & {
    sessionReadyCallbacks: (() => void)[];
};

interface BootResult {
    dom: JSDOM;
    win: PETestWindow;
    doc: Document;
    calls: BootCalls;
    pe: PEGlobals;
    /** Fires SwarmUI's session-ready hooks the way genpage main.js does, then awaits the startup they begin. */
    fireSessionReady: () => Promise<void>;
}

async function boot(opts: BootOpts): Promise<BootResult> {
    const calls: BootCalls = { genericRequest: [], showError: [], alerts: [], consoleErrors: [], consoleWarns: [] };
    const dom = new JSDOM(PAGE_HTML, { runScripts: 'dangerously', url: 'http://localhost/' });
    const win = dom.window as PETestWindow;
    const doc = win.document;
    const host = win as unknown as Record<string, unknown>;

    host.genericRequest = (route: string, payload: object, onSuccess: (data: unknown) => void, _depth?: number, onError?: (err: unknown) => void) => {
        calls.genericRequest.push({ route, payload: payload as RecordedCall['payload'] });
        if (opts.routeErrors && route in opts.routeErrors) {
            (onError ?? (() => { }))(opts.routeErrors[route]);
            return;
        }
        const resp = (opts.routeResponses && route in opts.routeResponses)
            ? opts.routeResponses[route]
            : route === 'GetPromptEnhanceSettings'
                ? { success: true, settings: {} }
                : (opts.backendResponse ?? { success: true, response: 'ENHANCED PROMPT' });
        const hostError = (resp as { error?: unknown }).error;
        if (hostError) {
            // site.js genericRequest hands any response carrying `error` to the error handler, as the bare string.
            (onError ?? (() => { }))(hostError);
            return;
        }
        onSuccess(resp);
    };
    host.showError = opts.throwingShowError
        ? () => { throw new Error('host banner is broken'); }
        : (message: string) => { calls.showError.push(message); };
    win.alert = ((message: string) => { calls.alerts.push(message); }) as typeof win.alert;
    const realConsoleError = win.console.error.bind(win.console);
    win.console.error = ((...args: unknown[]) => {
        calls.consoleErrors.push(args.map(String).join(' '));
        realConsoleError(...args);
    }) as typeof win.console.error;
    const realConsoleWarn = win.console.warn.bind(win.console);
    win.console.warn = ((...args: unknown[]) => {
        calls.consoleWarns.push(args.map(String).join(' '));
        realConsoleWarn(...args);
    }) as typeof win.console.warn;
    host.triggerChangeFor = () => { };
    host.genTabLayout = opts.genTabLayout ?? { altPromptSizeHandle: () => { } };
    win.sessionReadyCallbacks = [];
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

    for (const src of [UTIL_SRC, CONTRACTS_SRC, SETTINGS_SRC, PROMPT_SRC]) {
        const script = doc.createElement('script');
        script.textContent = src;
        doc.body.appendChild(script);
    }
    const pe = win.eval(`({
        genTab: promptEnhanceGenTab,
        settings: promptEnhanceSettings,
        PE_ROUTES, PE_API_KEY_TYPE, PE_LIMITS, PE_REPLACE_MODES, PE_DEFAULT_SETTINGS,
        peAdaptSettingsResult, peNormalizeSettings
    })`) as PEGlobals;
    const fireSessionReady = async () => {
        for (const callback of win.sessionReadyCallbacks) {
            callback();
        }
        await pe.genTab.ready;
    };
    if (!opts.holdSessionReady) {
        await fireSessionReady();
    }
    if (opts.settings) {
        pe.settings.apply(opts.settings);
    }
    return { dom, win, doc, calls, pe, fireSessionReady };
}

function promptValue(doc: Document): string {
    return (doc.getElementById('alt_prompt_textbox') as HTMLTextAreaElement).value;
}

const tests: { name: string; fn: () => void | Promise<void> }[] = [];
function test(name: string, fn: () => void | Promise<void>): void {
    tests.push({ name, fn });
}

test('Mounts the bar at the top of #alt_prompt_extra_area with the preview right after it', async () => {
    const { doc } = await boot({});
    const bar = doc.getElementById('pe_button_bar');
    assert.ok(bar, '#pe_button_bar exists after session-ready startup');
    const area = doc.getElementById('alt_prompt_extra_area')!;
    assert.strictEqual(area.firstElementChild, bar, '#pe_button_bar is the first child of #alt_prompt_extra_area');
    assert.strictEqual(bar.nextElementSibling, doc.getElementById('pe_preview'), '#pe_preview follows the bar');
    assert.strictEqual(doc.getElementById('pe_enhance_btn')!.tagName, 'BUTTON', 'Enhance is a real button');
    assert.ok(doc.getElementById('pe_enhance_btn')!.classList.contains('basic-button'), 'Enhance uses SwarmUI button styling');
    assert.ok(doc.getElementById('pe_settings_button'), 'the settings button is mounted');
});

test('Mounting is idempotent', async () => {
    const { doc, pe } = await boot({});
    pe.genTab.mount();
    assert.strictEqual(doc.querySelectorAll('#pe_button_bar').length, 1, 'exactly one bar after a second mount');
});

test('A real click on Enhance with an empty prompt surfaces an error and sends nothing', async () => {
    const { win, doc, calls } = await boot({ prompt: '' });
    doc.getElementById('pe_enhance_btn')!.dispatchEvent(new win.MouseEvent('click', { bubbles: true, cancelable: true }));
    assert.strictEqual(calls.showError.length, 1, 'the click drives handleEnhance');
    assert.ok(calls.showError[0]!.includes('prompt to enhance'), 'empty-prompt error text is surfaced');
    assert.strictEqual(calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun').length, 0, 'no enhance request for an empty prompt');
});

test('Image-collection failure surfaces an error, sends nothing, and clears loading', async () => {
    const { doc, calls, pe } = await boot({
        prompt: 'a cat',
        settings: { sendSelectedImage: true, replaceMode: 'preview' },
        image: { src: 'blob:current' },
        fetch: async () => { throw new Error('network down'); }
    });
    await pe.genTab.handleEnhance();
    assert.strictEqual(calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun').length, 0, 'no request when the attached image cannot be read');
    assert.strictEqual(calls.showError.length, 1, 'the failure is surfaced');
    assert.strictEqual((doc.getElementById('pe_enhance_btn') as HTMLButtonElement).disabled, false, 'loading clears');
});

test('A selected image is read via the real FileReader and attached as base64 media', async () => {
    const { win, calls, pe } = await boot({
        prompt: 'a cat',
        settings: { sendSelectedImage: true, replaceMode: 'preview' },
        image: { src: 'blob:current' },
        fetch: async () => ({ blob: async () => new win.Blob([new Uint8Array([65, 66, 67])], { type: 'image/png' }) })
    });
    await pe.genTab.handleEnhance();
    const runs = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun');
    assert.strictEqual(runs.length, 1, 'one enhance request');
    const media = runs[0]!.payload.media;
    assert.ok(Array.isArray(media) && media.length === 1, 'one image part');
    assert.strictEqual(media[0]!.data, 'QUJD', 'base64 from the real FileReader');
    assert.strictEqual(media[0]!.mediaType, 'image/png', 'media type from the real Blob');
});

test('No selected image is a text-only request', async () => {
    const { calls, pe } = await boot({ prompt: 'a cat', settings: { sendSelectedImage: true, replaceMode: 'preview' } });
    await pe.genTab.handleEnhance();
    const runs = calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun');
    assert.strictEqual(runs.length, 1, 'one text-only request');
    assert.strictEqual(runs[0]!.payload.media, undefined, 'no media attached');
});

test('Loading clears on backend failure and the server error is surfaced', async () => {
    const { doc, calls, pe } = await boot({ prompt: 'a cat', backendResponse: { success: false, error: 'boom' } });
    await pe.genTab.handleEnhance();
    assert.strictEqual(calls.showError.length, 1, 'backend failure surfaced');
    assert.ok(calls.showError[0]!.includes('boom'), 'the classified server error text is surfaced');
    assert.strictEqual((doc.getElementById('pe_enhance_btn') as HTMLButtonElement).disabled, false, 'loading clears');
    assert.strictEqual(doc.getElementById('pe_enhance_loading')!.style.display, 'none', 'loading indicator hidden');
});

test('Preview mode does not touch the prompt until Apply', async () => {
    const { doc, pe } = await boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'preview' } });
    pe.genTab.applyEnhancement('ORIGINAL', 'ENHANCED');
    assert.strictEqual(promptValue(doc), 'ORIGINAL', 'prompt untouched');
    assert.strictEqual(doc.getElementById('pe_preview')!.style.display, 'block', 'preview shown');
    assert.strictEqual(doc.getElementById('pe_preview_text')!.textContent, 'ENHANCED', 'preview shows the enhanced text');
    doc.getElementById('pe_preview_apply')!.click();
    assert.strictEqual(promptValue(doc), 'ENHANCED', 'Apply replaces the prompt');
    assert.strictEqual(pe.genTab.lastOriginal, 'ORIGINAL', 'Apply stashes the original for Restore');
});

test('Append mode keeps the original above the enhancement', async () => {
    const { doc, pe } = await boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'append' } });
    pe.genTab.applyEnhancement('ORIGINAL', 'ENHANCED');
    assert.strictEqual(promptValue(doc), 'ORIGINAL\n\n---\n\nENHANCED');
});

test('Replace mode replaces the prompt and Restore brings the original back', async () => {
    const { doc, pe } = await boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'replace_with_restore' } });
    pe.genTab.applyEnhancement('ORIGINAL', 'ENHANCED');
    assert.strictEqual(promptValue(doc), 'ENHANCED', 'prompt replaced');
    assert.strictEqual(doc.getElementById('pe_restore_btn')!.style.display, 'inline-block', 'Restore shown');
    doc.getElementById('pe_restore_btn')!.click();
    assert.strictEqual(promptValue(doc), 'ORIGINAL', 'Restore returns the original');
    assert.strictEqual(doc.getElementById('pe_restore_btn')!.style.display, 'none', 'Restore hidden again');
});

test('Replace mode keeps the TRUE original across a second enhance', async () => {
    const { doc, pe } = await boot({ prompt: 'ORIGINAL', settings: { replaceMode: 'replace_with_restore' } });
    pe.genTab.applyEnhancement('ORIGINAL', 'ENHANCED1');
    pe.genTab.applyEnhancement('ENHANCED1', 'ENHANCED2');
    assert.strictEqual(promptValue(doc), 'ENHANCED2', 'second enhance replaces the prompt');
    assert.strictEqual(pe.genTab.lastOriginal, 'ORIGINAL', 'the earliest original is kept');
    doc.getElementById('pe_restore_btn')!.click();
    assert.strictEqual(promptValue(doc), 'ORIGINAL', 'Restore returns the true original');
});

test('Every window global the extension adds is pe-prefixed', async () => {
    const hostOnly = new JSDOM(`<!DOCTYPE html><body><script>${UTIL_SRC}</script></body>`, { runScripts: 'dangerously' }).window;
    const baseline = new Set(Object.keys(hostOnly));
    const { win } = await boot({});
    const added = Object.keys(win).filter((key) => !baseline.has(key) && !HOST_STUBS.includes(key));
    assert.ok(added.includes('peAdaptSettingsResult'), 'control: extension function declarations are visible on window');
    assert.deepStrictEqual(added.filter((key) => !key.startsWith('pe')), [], 'no unprefixed extension globals on window');
});

test('Contract: defaults, routes, API key type, replace modes, and bounds match contracts/pe-contract.json exactly', async () => {
    const { pe } = await boot({});
    const expectedDefaults: Record<string, unknown> = {};
    for (const [key, spec] of Object.entries(CONTRACT.settings)) {
        expectedDefaults[key] = spec.default;
    }
    assert.deepStrictEqual({ ...pe.PE_DEFAULT_SETTINGS }, expectedDefaults, 'defaults equal the contract');
    assert.deepStrictEqual({ ...pe.settings.effective() }, expectedDefaults, 'boot-time effective settings equal the contract defaults');
    assert.deepStrictEqual({ ...pe.PE_ROUTES }, CONTRACT.routes, 'routes equal the contract');
    assert.strictEqual(pe.PE_API_KEY_TYPE, CONTRACT.apiKeyType, 'API key type equals the contract');
    assert.deepStrictEqual([...pe.PE_REPLACE_MODES], CONTRACT.settings.replaceMode!.enum, 'replace modes equal the contract enum');
    assert.deepStrictEqual(JSON.parse(JSON.stringify(pe.PE_LIMITS)), {
        timeoutSeconds: { min: CONTRACT.settings.timeoutSeconds!.min, max: CONTRACT.settings.timeoutSeconds!.max },
        temperature: { min: CONTRACT.settings.temperature!.min, max: CONTRACT.settings.temperature!.max },
        maxTokens: { min: CONTRACT.settings.maxTokens!.min }
    }, 'bounds equal the contract');
});

test('Contract: peNormalizeSettings clamps numbers to the contract bounds and keeps current values for bad input', async () => {
    const { pe } = await boot({});
    const current = pe.settings.effective();
    const clamped = pe.peNormalizeSettings({ timeoutSeconds: '999999', temperature: '9.5', maxTokens: '-5', baseUrl: '  http://box:8080/v1  ', model: '' }, { ...current, model: 'stored-model' });
    assert.strictEqual(clamped.timeoutSeconds, CONTRACT.settings.timeoutSeconds!.max, 'timeout clamps to the max');
    assert.strictEqual(clamped.temperature, CONTRACT.settings.temperature!.max, 'temperature clamps to the max');
    assert.strictEqual(clamped.maxTokens, CONTRACT.settings.maxTokens!.min, 'maxTokens clamps to the floor');
    assert.strictEqual(clamped.baseUrl, 'http://box:8080/v1', 'base URL is trimmed');
    assert.strictEqual(clamped.model, 'stored-model', 'an empty model selection keeps the stored model');
    const kept = pe.peNormalizeSettings({ timeoutSeconds: 'ninety', replaceMode: 'bogus' }, current);
    assert.strictEqual(kept.timeoutSeconds, current.timeoutSeconds, 'an unparseable number keeps the current value');
    assert.strictEqual(kept.replaceMode, current.replaceMode, 'an unknown mode keeps the current mode');
});

test('Contract: the settings adapter accepts every contract replace mode', async () => {
    const { pe } = await boot({});
    for (const mode of CONTRACT.settings.replaceMode!.enum!) {
        const result = pe.peAdaptSettingsResult({ success: true, settings: { replaceMode: mode } });
        assert.ok(result.ok, `adapter accepts '${mode}'`);
        assert.strictEqual((result as { ok: true; settings: Partial<PESettings> }).settings.replaceMode, mode, `adapter passes '${mode}' through`);
    }
});

test('A recovered settings store is surfaced via console.warn', async () => {
    const { calls } = await boot({ routeResponses: { GetPromptEnhanceSettings: { success: true, settings: {}, recovered: true } } });
    assert.ok(calls.consoleWarns.some((line) => line.includes('corrupt')), 'the recovered flag produces a warning');
    assert.deepStrictEqual(calls.consoleErrors, [], 'recovery is a warning, not an error');
});

test('Boot is clean: one settings load, zero console errors', async () => {
    const { calls } = await boot({});
    assert.deepStrictEqual(calls.consoleErrors, [], 'no console.error output');
    assert.strictEqual(calls.genericRequest.filter((c) => c.route === 'GetPromptEnhanceSettings').length, 1, 'exactly one settings load');
});

test('Settings load failure keeps the defaults, is logged, and Enhance still works', async () => {
    const { calls, pe } = await boot({ prompt: 'a cat', routeErrors: { GetPromptEnhanceSettings: new Error('store on fire') } });
    assert.ok(calls.consoleErrors.some((line) => line.includes('Failed to load settings')), 'the failure is logged');
    assert.strictEqual(pe.settings.effective().baseUrl, 'http://localhost:11434', 'defaults stay in effect');
    await pe.genTab.handleEnhance();
    assert.strictEqual(calls.genericRequest.filter((c) => c.route === 'PromptEnhanceRun').length, 1, 'Enhance still sends');
    assert.strictEqual(calls.showError.length, 0, 'no error banner');
});

test('Loading settings merges server values over defaults and rejects wrongly typed values', async () => {
    const { pe } = await boot({
        routeResponses: { GetPromptEnhanceSettings: { success: true, settings: { model: 'mock-enhancer', timeoutSeconds: 'ninety', replaceMode: 'append' } } }
    });
    const effective = pe.settings.effective();
    assert.strictEqual(effective.model, 'mock-enhancer', 'server value overrides the default');
    assert.strictEqual(effective.timeoutSeconds, 60, 'a wrongly typed value keeps the default');
    assert.strictEqual(effective.replaceMode, 'append', 'a valid enum value is accepted');
    assert.strictEqual(effective.baseUrl, 'http://localhost:11434', 'unsent keys keep their defaults');
});

test('Nothing mounts and no API call is made until SwarmUI signals the session is ready', async () => {
    const { win, doc, calls, fireSessionReady } = await boot({ holdSessionReady: true });
    assert.strictEqual(win.sessionReadyCallbacks.length, 1, 'startup registers exactly one session-ready hook');
    assert.strictEqual(doc.getElementById('pe_button_bar'), null, 'no bar before the session is ready');
    assert.strictEqual(calls.genericRequest.length, 0, 'no API call before the session is ready');
    await fireSessionReady();
    assert.ok(doc.getElementById('pe_button_bar'), 'the session-ready hook mounts the bar');
    assert.deepStrictEqual(calls.genericRequest.map((c) => c.route), ['GetPromptEnhanceSettings'], 'the session-ready hook loads settings');
    await fireSessionReady();
    assert.strictEqual(doc.querySelectorAll('#pe_button_bar').length, 1, 'a repeated signal does not remount');
    assert.strictEqual(calls.genericRequest.length, 1, 'a repeated signal does not reload settings');
});

test('Every change to the extension UI height asks SwarmUI to re-offset the prompt region', async () => {
    let relayouts = 0;
    const { doc, pe } = await boot({ prompt: 'a cat', settings: { replaceMode: 'preview' }, genTabLayout: { altPromptSizeHandle: () => { relayouts++; } } });
    assert.ok(relayouts > 0, 'mounting calls genTabLayout.altPromptSizeHandle()');
    const expectRelayout = async (action: string, run: () => void | Promise<void>) => {
        const before = relayouts;
        await run();
        assert.ok(relayouts > before, `${action} calls genTabLayout.altPromptSizeHandle()`);
    };
    await expectRelayout('opening the preview', () => pe.genTab.handleEnhance());
    assert.strictEqual(doc.getElementById('pe_preview')!.style.display, 'block', 'the preview is open');
    await expectRelayout('applying the preview', () => doc.getElementById('pe_preview_apply')!.click());
    await expectRelayout('restoring', () => doc.getElementById('pe_restore_btn')!.click());
});

test('An error still reaches the user when the host showError itself throws', async () => {
    const { calls, pe } = await boot({ throwingShowError: true });
    pe.genTab.showError('the backend is on fire');
    assert.strictEqual(calls.alerts.length, 1, 'the alert fallback fires');
    assert.strictEqual(calls.alerts[0], 'the backend is on fire', 'the message survives the fallback');
    assert.ok(calls.consoleErrors.some((line) => line.includes('host showError failed')), 'the host failure is logged');
});

(async () => {
    let failed = 0;
    for (const t of tests) {
        try {
            await t.fn();
            console.log(`  ok   ${t.name}`);
        } catch (err) {
            failed++;
            console.log(`  FAIL ${t.name}\n       ${err instanceof Error && err.stack ? err.stack : String(err)}`);
        }
    }
    console.log(`\n${failed === 0 ? 'PASS' : 'FAIL'} — ${tests.length - failed}/${tests.length} frontend tests passed`);
    process.exit(failed === 0 ? 0 : 1);
})();
