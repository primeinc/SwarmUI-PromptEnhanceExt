import { expect, type Page, test } from '@playwright/test';
import { callRoute, fakeBackendKey, fakeKeyedBackendUrl, useSettings } from './host';

/** Key type of the extension's row in SwarmUI's User → API Keys table (contracts/pe-contract.json apiKeyType). */
const keyType = 'promptenhance_api';

/**
 * Switches to a top-level tab. The test host has no image backends, so SwarmUI's "No backends present"
 * status bar overlays the tab row; the click is dispatched on the tab link itself, running the same handler.
 */
async function openTab(page: Page, tabButtonId: string): Promise<void> {
    await page.locator(`#${tabButtonId}`).dispatchEvent('click');
}

/** Saves `key` through SwarmUI's own User → API Keys row, then returns to the Generate tab. */
async function saveKeyInUserTab(page: Page, key: string, shot?: string): Promise<void> {
    await openTab(page, 'usersettingstabbutton');
    await page.locator('#userinfotabbutton').click();
    const row = page.locator(`tr[data-key="${keyType}"]`);
    await expect(row).toContainText('PromptEnhance LLM Server');
    if (shot) {
        // Taken before saving: once saved, the status cell shows the save time, which would change the image on every run.
        await expect(page.locator('#promptenhance_key_status')).toHaveText('not set');
        await expect(row).toHaveScreenshot(`${shot}.png`);
    }
    await page.locator('#promptenhance_api_key').fill(key);
    await page.locator('#promptenhance_key_submit').click();
    await expect(page.locator('#promptenhance_key_status')).toContainText('last updated');
    await openTab(page, 'text2imagetabbutton');
}

/** Types `prompt` and clicks Enhance. */
async function enhance(page: Page, prompt: string): Promise<void> {
    await page.locator('#alt_prompt_textbox').fill(prompt);
    await page.locator('#pe_enhance_btn').click();
}

test.afterEach(async ({ page }) => {
    await callRoute(page, 'SetAPIKey', { keyType, key: 'none' });
    await callRoute(page, 'ResetPromptEnhanceSettings', {});
});

test('a server that requires a key refuses Enhance until the key is saved in User → API Keys, then answers; the key never reaches the browser', async ({ page }) => {
    const pendingBodies: Promise<string | null>[] = [];
    page.on('response', (response) => {
        // Redirects and aborted requests have no body.
        pendingBodies.push(response.text().catch(() => null));
    });
    await useSettings(page, { baseUrl: fakeKeyedBackendUrl, model: 'fake-enhancer', replaceMode: 'preview' });
    await enhance(page, 'a quiet harbor');
    await expect(page.locator('#error_toast_content')).toContainText('rejected the request as unauthorized');
    await expect(page.locator('#pe_preview')).toBeHidden();

    await saveKeyInUserTab(page, fakeBackendKey, 'api-key-row');
    await expect(page.locator('#promptenhance_api_key'), 'the host clears the key field after saving').toHaveValue('');
    await enhance(page, 'a quiet harbor');
    await expect(page.locator('#pe_preview_text')).toHaveText('ENHANCED: a quiet harbor');

    const settings = await callRoute(page, 'GetPromptEnhanceSettings', {});
    expect(JSON.stringify(settings)).not.toContain(fakeBackendKey);
    const bodies = (await Promise.all(pendingBodies)).filter((body): body is string => body !== null);
    expect(bodies.length, 'control: responses were captured').toBeGreaterThan(5);
    expect(bodies.filter((body) => body.includes(fakeBackendKey)), 'no response the browser received contains the key').toEqual([]);
});

test('a wrong key is refused by the server', async ({ page }) => {
    await useSettings(page, { baseUrl: fakeKeyedBackendUrl, model: 'fake-enhancer', replaceMode: 'preview' });
    await callRoute(page, 'SetAPIKey', { keyType, key: `${fakeBackendKey}-wrong` });
    await enhance(page, 'a red kite');
    await expect(page.locator('#error_toast_content')).toContainText('rejected the request as unauthorized');
    await expect(page.locator('#error_toast_content')).toContainText('Incorrect API key provided.');
});

test('the settings modal lists a keyed server\'s models, shows the key status, and links to the key row', async ({ page }) => {
    await useSettings(page, { baseUrl: fakeKeyedBackendUrl, model: 'fake-enhancer' });
    await page.locator('#pe_settings_button').click();
    await expect(page.locator('#pe_api_key_status')).toHaveText('not set');
    await expect(page.locator('#pe_model_select option').first()).toHaveText('Error loading models');
    await page.locator('#pe_close_btn').click();
    await expect(page.locator('#pe_settings_modal')).toBeHidden();

    await callRoute(page, 'SetAPIKey', { keyType, key: fakeBackendKey });
    await page.locator('#pe_settings_button').click();
    await expect(page.locator('#pe_api_key_status')).toContainText('last updated');
    await expect(page.locator('#pe_model_select option[value="fake-enhancer"]')).toHaveCount(1);
    await page.locator('#pe_api_key_link').click();
    await expect(page.locator('#pe_settings_modal')).toBeHidden();
    await expect(page.locator('#promptenhance_api_key')).toBeFocused();
});
