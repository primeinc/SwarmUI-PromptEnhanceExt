import { expect, test } from '@playwright/test';
import * as path from 'node:path';
import { callRoute, shootPromptRegion, useFakeBackend } from './host';

const shotDir = path.join(__dirname, 'shots');

test.afterEach(async ({ page }) => {
    await callRoute(page, 'ResetPromptEnhanceSettings', {});
});

test('preview mode: Enhance shows the backend result, Apply replaces the prompt, Restore brings the original back', async ({ page }) => {
    await useFakeBackend(page, 'preview');
    const prompt = page.locator('#alt_prompt_textbox');
    await prompt.fill('a lighthouse at dusk');
    await shootPromptRegion(page, 'enhance-button');
    await page.locator('#pe_enhance_btn').click();
    await expect(page.locator('#pe_preview_text')).toHaveText('ENHANCED: a lighthouse at dusk');
    await expect(prompt, 'preview does not touch the prompt').toHaveValue('a lighthouse at dusk');
    await page.screenshot({ path: path.join(shotDir, 'enhance-preview.png') });
    await shootPromptRegion(page, 'enhance-preview');
    await page.locator('#pe_preview_apply').click();
    await expect(prompt).toHaveValue('ENHANCED: a lighthouse at dusk');
    await expect(page.locator('#pe_preview')).toBeHidden();
    await shootPromptRegion(page, 'enhance-restore');
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
