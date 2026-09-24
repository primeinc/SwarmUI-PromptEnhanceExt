import { defineConfig, devices } from '@playwright/test';

/** Port the vendored host listens on for browser runs; distinct from `vendor-ci-test` (7899). */
const port = Number(process.env.PE_UI_PORT ?? 7898);

/** Port of the fake OpenAI-compatible backend (fake-backend.mts) the extension is pointed at. */
const fakeBackendPort = Number(process.env.PE_FAKE_BACKEND_PORT ?? 7897);

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
    reporter: [['list']],
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
            env: { PE_FAKE_BACKEND_PORT: String(fakeBackendPort) },
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
