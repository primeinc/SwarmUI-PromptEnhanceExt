/**
 * Freshness gate for the committed README screenshots in ./screenshots.
 *
 *   clean  empties Tests/ui/shots/readme before a browser run, so only shots from that run survive
 *   write  copies Tests/ui/shots/readme into ./screenshots and records screenshots/manifest.json
 *   check  fails unless the manifest matches both the committed PNGs and the current inputs
 *
 * `just readme-shots` runs clean, the full browser gate, then write: a shot reaches ./screenshots
 * only from a green run. Hashes are git blob ids (`git hash-object`), which apply the repo's
 * line-ending normalization, so a CRLF checkout and an LF checkout agree. File mtimes are not
 * used: git does not preserve them.
 */
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';

const repo = path.resolve(import.meta.dirname, '..', '..');
const runDir = path.join(repo, 'Tests', 'ui', 'shots', 'readme');
const committedDir = path.join(repo, 'screenshots');
const manifestPath = path.join(committedDir, 'manifest.json');

/** Everything that changes what the screenshots show: extension UI source and styles, the specs that stage the shots, the contract defaults, and the SwarmUI pin. */
const inputRoots = ['Frontend', 'Assets', 'Tests/ui', 'contracts'];
const inputExcludes = ['Tests/ui/shots/', 'Tests/ui/test-results/', 'Tests/ui/readme-shots.mts'];

interface Manifest {
    inputs: string;
    swarmuiPin: string;
    shots: Record<string, string>;
}

function git(args: string[], input?: string): string {
    return execFileSync('git', args, { cwd: repo, encoding: 'utf8', input, maxBuffer: 64 * 1024 * 1024 });
}

/** Blob ids for repo-relative paths, in the given order. */
function blobIds(paths: string[]): string[] {
    const ids = git(['hash-object', '--stdin-paths'], `${paths.join('\n')}\n`).split('\n').filter((line) => line !== '');
    if (ids.length !== paths.length) {
        throw new Error(`git hash-object returned ${ids.length} ids for ${paths.length} paths`);
    }
    return ids;
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
    const listed = git(['ls-files', '-z', '--cached', '--others', '--exclude-standard', '--', ...inputRoots]).split('\0');
    const files = [...new Set(listed)]
        .filter((file) => file !== '' && !inputExcludes.some((prefix) => file.startsWith(prefix)) && fs.existsSync(path.join(repo, file)))
        .sort();
    if (files.length === 0) {
        throw new Error(`no input files found under ${inputRoots.join(', ')}`);
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

function clean(): void {
    fs.rmSync(runDir, { recursive: true, force: true });
}

function write(): void {
    const shots = pngsIn(runDir);
    if (shots.length === 0) {
        throw new Error(`no screenshots in ${runDir}; run \`just readme-shots\``);
    }
    for (const stale of pngsIn(committedDir)) {
        fs.rmSync(path.join(committedDir, stale));
    }
    fs.mkdirSync(committedDir, { recursive: true });
    for (const shot of shots) {
        fs.copyFileSync(path.join(runDir, shot), path.join(committedDir, shot));
    }
    const ids = blobIds(shots.map((shot) => `screenshots/${shot}`));
    const manifest: Manifest = { inputs: inputsDigest(), swarmuiPin: swarmuiPin(), shots: Object.fromEntries(shots.map((shot, index) => [shot, ids[index]!])) };
    fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
    console.log(`[readme-shots] wrote ${shots.length} screenshots and ${path.relative(repo, manifestPath)}`);
}

function check(): void {
    const fix = 'Run `just readme-shots` and commit ./screenshots.';
    if (!fs.existsSync(manifestPath)) {
        throw new Error(`${path.relative(repo, manifestPath)} is missing. ${fix}`);
    }
    const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8')) as Manifest;
    const problems: string[] = [];
    const recorded = Object.keys(manifest.shots).sort();
    const present = pngsIn(committedDir);
    for (const shot of present.filter((name) => !recorded.includes(name))) {
        problems.push(`screenshots/${shot} is not in the manifest`);
    }
    for (const shot of recorded.filter((name) => !present.includes(name))) {
        problems.push(`screenshots/${shot} is in the manifest but missing`);
    }
    const common = recorded.filter((name) => present.includes(name));
    const ids = blobIds(common.map((shot) => `screenshots/${shot}`));
    common.forEach((shot, index) => {
        if (manifest.shots[shot] !== ids[index]) {
            problems.push(`screenshots/${shot} differs from the image the last green run produced`);
        }
    });
    if (manifest.swarmuiPin !== swarmuiPin()) {
        problems.push(`screenshots were taken on SwarmUI ${manifest.swarmuiPin}, the pin is ${swarmuiPin()}`);
    }
    else if (manifest.inputs !== inputsDigest()) {
        problems.push('the extension UI, its styles, the UI specs, or the contract changed since the screenshots were taken');
    }
    const readme = fs.readFileSync(path.join(repo, 'README.md'), 'utf8');
    for (const shot of recorded.filter((name) => !readme.includes(`screenshots/${name}`))) {
        problems.push(`README.md does not show screenshots/${shot}`);
    }
    if (problems.length > 0) {
        throw new Error(`README screenshots are stale:\n  - ${problems.join('\n  - ')}\n${fix}`);
    }
    console.log(`[readme-shots] ${recorded.length} screenshots are current (SwarmUI ${manifest.swarmuiPin})`);
}

const modes: Record<string, () => void> = { clean, write, check };
const mode = process.argv[2] ?? '';
const run = modes[mode];
if (!run) {
    throw new Error(`usage: node Tests/ui/readme-shots.mts <${Object.keys(modes).join('|')}>`);
}
run();
