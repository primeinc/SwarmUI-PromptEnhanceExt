import { expect, type Page, test } from '@playwright/test';
import * as path from 'node:path';

/** Screenshots land outside Playwright's outputDir, which is wiped at the start of every run. */
const shotDir = path.join(__dirname, 'shots');

/** Browser errors collected per test; a clean Generate tab has none. */
function collectErrors(page: Page): string[] {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(`pageerror: ${err.message}`));
    page.on('console', (msg) => {
        if (msg.type() === 'error') {
            errors.push(`console: ${msg.text()}`);
        }
    });
    page.on('response', async (resp) => {
        if (resp.status() >= 400) {
            errors.push(`http ${resp.status()}: ${resp.request().method()} ${resp.url()} ${await resp.text()}`);
        }
    });
    return errors;
}

/** Opens the Generate tab and waits until the extension has mounted its controls. */
async function openGenerateTab(page: Page): Promise<void> {
    await page.goto('/Text2Image');
    await expect(page.locator('#alt_prompt_textbox')).toBeVisible();
    await expect(page.locator('#pe_enhance_btn')).toBeVisible();
}

test('Generate tab renders with the extension loaded', async ({ page }) => {
    const errors = collectErrors(page);
    const settingsLoaded = page.waitForResponse((resp) => resp.url().endsWith('/API/GetPromptEnhanceSettings') && resp.status() === 200);
    await openGenerateTab(page);
    await settingsLoaded;
    await page.screenshot({ path: path.join(shotDir, 'generate-tab.png') });
    await page.locator('#alt_prompt_region').screenshot({ path: path.join(shotDir, 'prompt-region.png') });
    expect(errors).toEqual([]);
});
