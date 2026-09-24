import type { FullResult, Reporter } from '@playwright/test/reporter';
import * as fs from 'node:fs';
import * as path from 'node:path';

/** Where readme-shots.mts `write` looks for proof that the run that produced the screenshots passed. */
const passedPath = path.join(__dirname, 'shots', 'run', 'passed.json');

/** True when the command line runs every spec: only a config flag and snapshot updating, no file filters, --grep, or other selection. */
function isFullRun(): boolean {
    const args = process.argv.slice(process.argv.indexOf('test') + 1);
    for (let i = 0; i < args.length; i++) {
        const arg = args[i]!;
        if (arg === '-c' || arg === '--config') {
            i++;
            continue;
        }
        if (arg.startsWith('--config=') || arg.startsWith('--update-snapshots')) {
            continue;
        }
        return false;
    }
    return true;
}

/** Records a pass for readme-shots.mts only when a full, unfiltered run finished with every test passing; any other run removes the record. */
export default class GreenRunReporter implements Reporter {
    onEnd(result: FullResult): void {
        fs.rmSync(passedPath, { force: true });
        if (result.status !== 'passed' || !isFullRun()) {
            return;
        }
        fs.mkdirSync(path.dirname(passedPath), { recursive: true });
        fs.writeFileSync(passedPath, `${JSON.stringify({
            status: result.status,
            ports: {
                ui: process.env.PE_UI_PORT ?? null,
                fakeBackend: process.env.PE_FAKE_BACKEND_PORT ?? null,
                fakeKeyedBackend: process.env.PE_FAKE_KEYED_BACKEND_PORT ?? null,
            },
        }, null, 2)}\n`);
    }
}
