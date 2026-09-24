import { expect, type Page, test } from '@playwright/test';
import * as path from 'node:path';

const shotDir = path.join(__dirname, 'shots');
const fakeBackendUrl = `http://127.0.0.1:${process.env.PE_FAKE_BACKEND_PORT ?? 7897}`;

/** Calls one SwarmUI API route from inside the page, through the host's own session-aware transport. */
async function callRoute(page: Page, route: string, payload: object): Promise<unknown> {
    return page.evaluate(([route, payload]) => new Promise((resolve, reject) => {
        genericRequest(route as string, payload as object, resolve, 0, (err) => reject(new Error(String(err))));
    }), [route, payload] as const);
}

/** Opens the Generate tab and waits until the extension finished its session-ready startup. */
async function openGenerateTab(page: Page): Promise<void> {
    const settingsLoaded = page.waitForResponse((resp) => resp.url().endsWith('/API/GetPromptEnhanceSettings') && resp.status() === 200);
    await page.goto('/Text2Image');
    await expect(page.locator('#pe_enhance_btn')).toBeVisible();
    await settingsLoaded;
}

/** Points the extension at the fake backend with the given apply mode, then reloads so the client picks it up. */
async function useFakeBackend(page: Page, replaceMode: PEReplaceMode): Promise<void> {
    await openGenerateTab(page);
    const saved = await callRoute(page, 'SavePromptEnhanceSettings', { settings: { baseUrl: fakeBackendUrl, model: 'fake-enhancer', replaceMode } });
    expect(saved, 'settings saved').toMatchObject({ success: true, settings: { baseUrl: fakeBackendUrl, replaceMode } });
    await openGenerateTab(page);
}

test.afterEach(async ({ page }) => {
    await callRoute(page, 'ResetPromptEnhanceSettings', {});
});

test('preview mode: Enhance shows the backend result, Apply replaces the prompt, Restore brings the original back', async ({ page }) => {
    await useFakeBackend(page, 'preview');
    const prompt = page.locator('#alt_prompt_textbox');
    await prompt.fill('a lighthouse at dusk');
    await page.locator('#pe_enhance_btn').click();
    await expect(page.locator('#pe_preview_text')).toHaveText('ENHANCED: a lighthouse at dusk');
    await expect(prompt, 'preview does not touch the prompt').toHaveValue('a lighthouse at dusk');
    await page.screenshot({ path: path.join(shotDir, 'enhance-preview.png') });
    await page.locator('#pe_preview_apply').click();
    await expect(prompt).toHaveValue('ENHANCED: a lighthouse at dusk');
    await expect(page.locator('#pe_preview')).toBeHidden();
    await page.locator('#pe_restore_btn').click();
    await expect(prompt).toHaveValue('a lighthouse at dusk');
    await expect(page.locator('#pe_restore_btn')).toBeHidden();
});

test('append mode keeps the original above the enhancement', async ({ page }) => {
    await useFakeBackend(page, 'append');
    const prompt = page.locator('#alt_prompt_textbox');
    await prompt.fill('a red fox');
    await page.locator('#pe_enhance_btn').click();
    await expect(prompt).toHaveValue('a red fox\n\n---\n\nENHANCED: a red fox');
});

test('the settings panel lists the backend models and saves a changed value', async ({ page }) => {
    await useFakeBackend(page, 'preview');
    await page.locator('#pe_settings_button').click();
    await expect(page.locator('#pe_model_select option[value="fake-enhancer"]')).toHaveCount(1);
    await expect(page.locator('#pe_model_select')).toHaveValue('fake-enhancer');
    await page.locator('#pe_temperature').fill('1.25');
    await page.screenshot({ path: path.join(shotDir, 'settings-panel.png') });
    await page.locator('#pe_save_btn').click();
    await expect(page.locator('#pe_settings_status')).toHaveText('Saved.');
    const stored = await callRoute(page, 'GetPromptEnhanceSettings', {});
    expect(stored).toMatchObject({ success: true, settings: { temperature: 1.25, model: 'fake-enhancer' } });
});
