import { defineConfig, devices } from '@playwright/test';

/** Port the vendored host listens on for browser runs; distinct from `vendor-ci-test` (7899). */
const port = Number(process.env.PE_UI_PORT ?? 7898);

/** Port of the fake OpenAI-compatible backend (fake-backend.mts) the extension is pointed at. */
const fakeBackendPort = Number(process.env.PE_FAKE_BACKEND_PORT ?? 7897);

/** Port of the fake backend that requires `Authorization: Bearer <PE_FAKE_BACKEND_KEY>`. */
const fakeKeyedBackendPort = Number(process.env.PE_FAKE_KEYED_BACKEND_PORT ?? 7896);

/** True when every port is the one the README screenshots were taken with. */
const usesDefaultPorts = port === 7898 && fakeBackendPort === 7897 && fakeKeyedBackendPort === 7896;

/**
 * Browser gates against the real vendored SwarmUI host with this extension copied in.
 * `just ui-test` builds the frontend, syncs the extension copy, and builds the host first;
 * running this config directly serves whatever copy and build are already on disk.
 */
export default defineConfig({
    testDir: '.',
    outputDir: './test-results',
    fullyParallel: false,
    workers: 1,
    reporter: [['list'], ['./green-reporter.ts']],
    /**
     * The README screenshots are the toHaveScreenshot baselines. No pixel may differ beyond the
     * per-pixel color threshold, so a single changed character fails. They are taken on the default
     * ports, whose values the settings modal shows, so other ports skip the comparison.
     */
    snapshotPathTemplate: '../../screenshots/{arg}{ext}',
    ignoreSnapshots: !usesDefaultPorts,
    expect: {
        toHaveScreenshot: { maxDiffPixels: 0, animations: 'disabled' },
    },
    use: {
        baseURL: `http://localhost:${port}`,
        trace: 'retain-on-failure',
    },
    projects: [
        { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1366, height: 768 } } },
    ],
    webServer: [
        {
            name: 'fake-backend',
            command: 'node fake-backend.mts',
            cwd: '.',
            env: { PE_FAKE_BACKEND_PORT: String(fakeBackendPort), PE_FAKE_KEYED_BACKEND_PORT: String(fakeKeyedBackendPort) },
            url: `http://127.0.0.1:${fakeBackendPort}/v1/models`,
            reuseExistingServer: false,
            timeout: 30_000,
        },
        {
            name: 'swarmui',
            command: `dotnet src/bin/live_release/SwarmUI.dll --environment dev --launch_mode none --port ${port}`,
            cwd: '../../vendor/SwarmUI',
            url: `http://localhost:${port}/Text2Image`,
            reuseExistingServer: false,
            timeout: 120_000,
        },
    ],
});
