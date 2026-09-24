import { expect, type Page, test } from '@playwright/test';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { callRoute, fakeBackendUrl, openGenerateTab, readmeShotDir, useFakeBackend, useSettings } from './host';

const shotDir = path.join(__dirname, 'shots');
const contract = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'contracts', 'pe-contract.json'), 'utf8')) as {
    settings: Record<string, { default: unknown; enum?: string[] }>;
};

/** A base URL with nothing listening. */
const deadBackendUrl = 'http://127.0.0.1:1';

async function openModal(page: Page): Promise<void> {
    await page.locator('#pe_settings_button').click();
    await expect(page.locator('#pe_settings_modal .modal-content')).toBeVisible();
}

test.afterEach(async ({ page }) => {
    await callRoute(page, 'ResetPromptEnhanceSettings', {});
});

test('lists the backend models and saves changed values', async ({ page }) => {
    await useFakeBackend(page, 'preview');
    await openModal(page);
    await expect(page.locator('#pe_model_select option[value="fake-enhancer"]')).toHaveCount(1);
    await expect(page.locator('#pe_model_select')).toHaveValue('fake-enhancer');
    await page.locator('#pe_temperature').fill('1.25');
    await page.screenshot({ path: path.join(shotDir, 'settings-modal.png') });
    await page.locator('#pe_settings_modal .modal-content').screenshot({ path: path.join(readmeShotDir, 'settings-modal.png'), animations: 'disabled' });
    await page.locator('#pe_base_url').fill(`  ${fakeBackendUrl}  `);
    await page.locator('#pe_save_btn').click();
    await expect(page.locator('#pe_settings_status')).toHaveText('Saved.');
    await expect(page.locator('#pe_settings_status')).toHaveClass(/modal_success_bottom/);
    const stored = await callRoute(page, 'GetPromptEnhanceSettings', {});
    expect(stored).toMatchObject({ success: true, settings: { temperature: 1.25, model: 'fake-enhancer', baseUrl: fakeBackendUrl } });
});

test('offers exactly the contract apply modes, each under a readable label', async ({ page }) => {
    await openGenerateTab(page);
    await openModal(page);
    const options = await page.locator('#pe_replace_mode option').evaluateAll((elems) => elems.map((elem) => ({ value: (elem as HTMLOptionElement).value, label: elem.textContent?.trim() ?? '' })));
    expect(options.map((option) => option.value)).toEqual(contract.settings.replaceMode!.enum);
    for (const option of options) {
        expect(option.label, `mode ${option.value} shows a label, not its wire value`).not.toEqual(option.value);
        expect(option.label).not.toEqual('');
    }
});

test('a rejected save shows the server reason as an error', async ({ page }) => {
    await openGenerateTab(page);
    await openModal(page);
    await page.locator('#pe_base_url').fill('not a url');
    await page.locator('#pe_save_btn').click();
    await expect(page.locator('#pe_settings_status')).toContainText('Base URL must be a valid http(s) URL');
    await expect(page.locator('#pe_settings_status')).toHaveClass(/modal_error_bottom/);
});

test('an unreachable backend leaves one disabled model entry and an error status, and keeps the stored model on save', async ({ page }) => {
    await useSettings(page, { baseUrl: deadBackendUrl, model: 'stored-model' });
    await openModal(page);
    const options = page.locator('#pe_model_select option');
    await expect(options).toHaveCount(1);
    await expect(options.first()).toHaveText('Error loading models');
    await expect(options.first()).toBeDisabled();
    await expect(page.locator('#pe_settings_status')).toHaveClass(/modal_error_bottom/);
    await expect(page.locator('#pe_settings_status')).not.toBeEmpty();
    await page.locator('#pe_temperature').fill('0.5');
    await page.locator('#pe_save_btn').click();
    await expect(page.locator('#pe_settings_status')).toHaveText('Saved.');
    const stored = await callRoute(page, 'GetPromptEnhanceSettings', {});
    expect(stored).toMatchObject({ success: true, settings: { model: 'stored-model', temperature: 0.5 } });
});

test('Reset restores the contract defaults in the form and on the server', async ({ page }) => {
    await useSettings(page, { baseUrl: deadBackendUrl, temperature: 1.5, replaceMode: 'append' });
    await openModal(page);
    await expect(page.locator('#pe_temperature')).toHaveValue('1.5');
    await page.locator('#pe_reset_btn').click();
    await expect(page.locator('#pe_base_url')).toHaveValue(contract.settings.baseUrl!.default as string);
    await expect(page.locator('#pe_temperature')).toHaveValue(String(contract.settings.temperature!.default));
    await expect(page.locator('#pe_replace_mode')).toHaveValue(contract.settings.replaceMode!.default as string);
    const stored = await callRoute(page, 'GetPromptEnhanceSettings', {});
    expect(stored).toMatchObject({ success: true, settings: { baseUrl: contract.settings.baseUrl!.default, temperature: contract.settings.temperature!.default } });
});

test('Close hides the modal', async ({ page }) => {
    await openGenerateTab(page);
    await openModal(page);
    await page.locator('#pe_close_btn').click();
    await expect(page.locator('#pe_settings_modal')).toBeHidden();
});
