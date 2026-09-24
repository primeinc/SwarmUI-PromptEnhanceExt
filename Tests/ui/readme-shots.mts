/**
 * Freshness record for the browser gates and the committed README screenshots in ./screenshots.
 *
 *   clean    starts a run record: the input digest the coming browser run starts from
 *   write    records screenshots/manifest.json for the run that just finished
 *   check    fails unless the manifest matches the committed PNGs, the README, and the current inputs
 *   current  exits 0 when a green browser run already covers the current inputs, else 1 with the reason
 *
 * The README screenshots are Playwright toHaveScreenshot baselines (playwright.config.ts): a run
 * compares them pixel-wise with a small tolerance, and `just ui-test-force` runs with
 * --update-snapshots=changed, so Playwright rewrites a baseline only when the picture really
 * changed. `just ui-test` runs `current` and stops there when nothing changed; otherwise it runs
 * clean, the full browser gate, then write. `write` refuses unless that same run passed in full
 * (the pass record from green-reporter.ts), the inputs did not change during it, the default ports
 * were used, and the vendored host is clean at the pin. Hashes are sha256 over file content, with
 * CRLF read as LF in text files, so they do not depend on checkout settings. Committed-file mtimes
 * are not used: git does not preserve them.
 */
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';

const repo = path.resolve(import.meta.dirname, '..', '..');
const runDir = path.join(repo, 'Tests', 'ui', 'shots', 'run');
const committedDir = path.join(repo, 'screenshots');
const manifestPath = path.join(committedDir, 'manifest.json');

/**
 * Everything a browser run depends on besides the SwarmUI pin: the served frontend and styles, the
 * server-side extension (routes, validation, error text), the contract, the specs and fake backends,
 * the dev host seed, and the npm lockfile that fixes the Playwright and Chromium versions.
 */
const inputPaths = [
    'Frontend', 'Assets', 'WebAPI', 'contracts', 'Tests/ui',
    'BackendSchema.cs', 'PromptEnhanceExtension.cs', 'PromptEnhance.csproj',
    'scripts/vendor-dev-settings.fds', 'package.json', 'package-lock.json'
];
const inputExcludes = ['Tests/ui/shots/', 'Tests/ui/test-results/', 'Tests/ui/readme-shots.mts'];

/** Written by `clean` when a run starts. */
const runRecordPath = path.join(runDir, 'run.json');

/** Written by green-reporter.ts when that run passed in full. */
const passedRecordPath = path.join(runDir, 'passed.json');

/** The ports the screenshots are taken with; the settings-modal shot shows the fake backend's. */
const defaultPorts: Record<string, string> = { ui: '7898', fakeBackend: '7897', fakeKeyedBackend: '7896' };

interface Manifest {
    inputs: string;
    swarmuiPin: string;
    shots: Record<string, string>;
}

interface RunRecord {
    inputs: string;
    startedAt: number;
}

interface PassedRecord {
    status: string;
    ports: Record<string, string | null>;
}

function git(args: string[], cwd = repo): string {
    return execFileSync('git', args, { cwd, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

/** sha256 of a file's content. Text (no NUL byte) is read with CRLF as LF, so checkouts with and without CRLF conversion agree. */
function contentHash(file: string): string {
    const bytes = fs.readFileSync(path.join(repo, file));
    const hash = createHash('sha256');
    if (bytes.includes(0)) {
        hash.update(bytes);
    }
    else {
        hash.update(bytes.toString('utf8').replaceAll('\r\n', '\n'));
    }
    return hash.digest('hex');
}

/** Content hashes for repo-relative paths, in the given order. */
function blobIds(paths: string[]): string[] {
    return paths.map(contentHash);
}

/** The `swarmui_pin` string literal from the justfile. */
function swarmuiPin(): string {
    const prefix = 'swarmui_pin := ';
    const line = fs.readFileSync(path.join(repo, 'justfile'), 'utf8').split('\n').find((text) => text.startsWith(prefix));
    if (!line) {
        throw new Error('justfile defines no swarmui_pin');
    }
    const pin: unknown = JSON.parse(line.slice(prefix.length).trim());
    if (typeof pin !== 'string' || pin.length !== 40) {
        throw new Error(`justfile swarmui_pin is not a 40-character SHA: ${line}`);
    }
    return pin;
}

/** One digest over every input file (tracked and untracked, gitignored excluded) plus the pin. */
function inputsDigest(): string {
    const listed = git(['ls-files', '-z', '--cached', '--others', '--exclude-standard', '--', ...inputPaths]).split('\0');
    const files = [...new Set(listed)]
        .filter((file) => file !== '' && !inputExcludes.some((prefix) => file.startsWith(prefix)) && fs.existsSync(path.join(repo, file)))
        .sort();
    if (files.length === 0) {
        throw new Error(`no input files found under ${inputPaths.join(', ')}`);
    }
    const ids = blobIds(files);
    const hash = createHash('sha256');
    hash.update(`pin ${swarmuiPin()}\n`);
    for (const [index, file] of files.entries()) {
        hash.update(`${ids[index]} ${file}\n`);
    }
    return hash.digest('hex');
}

function pngsIn(dir: string): string[] {
    return fs.existsSync(dir) ? fs.readdirSync(dir).filter((name) => name.endsWith('.png')).sort() : [];
}

/** Every reason the committed screenshots and manifest do not describe a green run of the current inputs; empty when they do. */
function problems(): string[] {
    if (!fs.existsSync(manifestPath)) {
        return [`${path.relative(repo, manifestPath)} is missing`];
    }
    const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8')) as Manifest;
    const found: string[] = [];
    const recorded = Object.keys(manifest.shots).sort();
    const present = pngsIn(committedDir);
    for (const shot of present.filter((name) => !recorded.includes(name))) {
        found.push(`screenshots/${shot} is not in the manifest`);
    }
    for (const shot of recorded.filter((name) => !present.includes(name))) {
        found.push(`screenshots/${shot} is in the manifest but missing`);
    }
    const common = recorded.filter((name) => present.includes(name));
    const ids = blobIds(common.map((shot) => `screenshots/${shot}`));
    for (const [index, shot] of common.entries()) {
        if (manifest.shots[shot] !== ids[index]) {
            found.push(`screenshots/${shot} differs from the image the last green run produced`);
        }
    }
    if (manifest.swarmuiPin !== swarmuiPin()) {
        found.push(`the last green run was on SwarmUI ${manifest.swarmuiPin}, the pin is ${swarmuiPin()}`);
    }
    else if (manifest.inputs !== inputsDigest()) {
        found.push(`files the browser gates depend on changed since the last green run (${inputPaths.join(', ')})`);
    }
    const readme = fs.readFileSync(path.join(repo, 'README.md'), 'utf8');
    for (const shot of recorded.filter((name) => !readme.includes(`screenshots/${name}`))) {
        found.push(`README.md does not show screenshots/${shot}`);
    }
    return found;
}

function clean(): void {
    fs.rmSync(runDir, { recursive: true, force: true });
    fs.mkdirSync(runDir, { recursive: true });
    const record: RunRecord = { inputs: inputsDigest(), startedAt: Date.now() };
    fs.writeFileSync(runRecordPath, `${JSON.stringify(record, null, 2)}\n`);
}

/** Every reason the run that just finished may not certify ./screenshots; empty when it may. */
function writeRefusals(shots: string[]): string[] {
    const refusals: string[] = [];
    if (!fs.existsSync(runRecordPath)) {
        return ['no run record: the shots did not come from `just ui-test`'];
    }
    const run = JSON.parse(fs.readFileSync(runRecordPath, 'utf8')) as RunRecord;
    if (!fs.existsSync(passedRecordPath)) {
        refusals.push('no pass record: the browser run failed, was filtered, or never ran');
    }
    else {
        const passed = JSON.parse(fs.readFileSync(passedRecordPath, 'utf8')) as PassedRecord;
        for (const [name, port] of Object.entries(passed.ports)) {
            if (port !== null && port !== defaultPorts[name]) {
                refusals.push(`the run used ${name} port ${port}; screenshots are taken on the default ports`);
            }
        }
    }
    if (run.inputs !== inputsDigest()) {
        refusals.push('inputs changed while the browser gates ran');
    }
    if (shots.length === 0) {
        refusals.push('./screenshots holds no screenshots');
    }
    const vendor = path.join(repo, 'vendor', 'SwarmUI');
    if (!fs.existsSync(path.join(vendor, '.git'))) {
        refusals.push('vendor/SwarmUI is missing');
    }
    else {
        if (git(['rev-parse', 'HEAD'], vendor).trim() !== swarmuiPin()) {
            refusals.push(`vendor/SwarmUI is not at the pin ${swarmuiPin()}`);
        }
        if (git(['status', '--porcelain'], vendor).trim() !== '') {
            refusals.push('vendor/SwarmUI has local changes');
        }
    }
    return refusals;
}

function write(): void {
    const shots = pngsIn(committedDir);
    const refusals = writeRefusals(shots);
    if (refusals.length > 0) {
        throw new Error(`refusing to update ./screenshots:\n  - ${refusals.join('\n  - ')}\nRun \`just ui-test-force\`.`);
    }
    const ids = blobIds(shots.map((shot) => `screenshots/${shot}`));
    const manifest: Manifest = { inputs: inputsDigest(), swarmuiPin: swarmuiPin(), shots: Object.fromEntries(shots.map((shot, index) => [shot, ids[index]!])) };
    fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
    console.log(`[readme-shots] certified ${shots.length} screenshots in ${path.relative(repo, manifestPath)}`);
}

function check(): void {
    const found = problems();
    if (found.length > 0) {
        throw new Error(`README screenshots are stale:\n  - ${found.join('\n  - ')}\nRun \`just ui-test\` and commit ./screenshots.`);
    }
    console.log(`[readme-shots] screenshots are current (SwarmUI ${swarmuiPin()})`);
}

function current(): void {
    const found = problems();
    if (found.length > 0) {
        console.log(`[readme-shots] running the browser gates:\n  - ${found.join('\n  - ')}`);
        process.exitCode = 1;
        return;
    }
    console.log('[readme-shots] nothing the browser gates depend on changed since the last green run; skipping. `just ui-test-force` runs them anyway.');
}

const modes: Record<string, () => void> = { clean, write, check, current };
const mode = process.argv[2] ?? '';
const run = modes[mode];
if (!run) {
    throw new Error(`usage: node Tests/ui/readme-shots.mts <${Object.keys(modes).join('|')}>`);
}
run();
