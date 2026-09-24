import { expect, type Page, test } from '@playwright/test';
import * as path from 'node:path';

const shotDir = path.join(__dirname, 'shots', 'themes');

/** Elements the extension draws a 1px border on, opened into view before measuring. */
const borderedSelectors = ['#pe_enhance_btn', '#pe_restore_btn', '#pe_preview', '#pe_preview_text', '#pe_settings_panel'];

/** Every registered theme id mapped to the stylesheet paths it loads (GetUserSettings). */
async function registeredThemes(page: Page): Promise<Record<string, string[]>> {
    const themes = await page.evaluate(() => new Promise<Record<string, string[]>>((resolve, reject) => {
        genericRequest('GetUserSettings', {}, (data) => {
            const themeMap = (data as { themes?: Record<string, { css_paths: string[] }> }).themes;
            if (!themeMap) {
                reject(new Error('GetUserSettings returned no themes'));
                return;
            }
            const paths: Record<string, string[]> = {};
            for (const [id, theme] of Object.entries(themeMap)) {
                paths[id] = theme.css_paths;
            }
            resolve(paths);
        }, 0, (err) => reject(new Error(String(err))));
    }));
    expect(Object.keys(themes).length, 'the host reports its registered themes').toBeGreaterThan(1);
    return themes;
}

/**
 * Switches the page to `theme` the way the User tab's theme selector does (change event, no save),
 * then reports the stylesheet paths now loaded, once every theme sheet has finished loading.
 */
async function applyTheme(page: Page, theme: string): Promise<string[]> {
    return page.evaluate(async (theme) => {
        const selector = document.getElementById('usersettings_theme') as HTMLSelectElement | null;
        if (!selector || !window.triggerChangeFor) {
            throw new Error('theme selector or triggerChangeFor missing');
        }
        selector.value = theme;
        window.triggerChangeFor(selector);
        const links = [...document.querySelectorAll<HTMLLinkElement>('link.theme_sheet_header')];
        await Promise.all(links.map((link) => link.sheet ? null : new Promise((resolve, reject) => {
            link.addEventListener('load', resolve);
            link.addEventListener('error', () => reject(new Error(`failed to load ${link.href}`)));
        })));
        return links.map((link) => new URL(link.href).pathname.slice(1));
    }, theme);
}

/** Opens every extension surface that carries a border: preview, Restore, settings panel. */
async function openBorderedSurfaces(page: Page): Promise<void> {
    await page.evaluate(() => {
        const preview = document.getElementById('pe_preview');
        const text = document.getElementById('pe_preview_text');
        const restore = document.getElementById('pe_restore_btn');
        if (!preview || !text || !restore) {
            throw new Error('extension UI missing');
        }
        text.textContent = 'a weathered lighthouse at dusk';
        preview.style.display = 'block';
        restore.style.display = 'inline-block';
        PromptEnhance.openSettingsPanel?.();
    });
}

test('every extension border renders in every registered theme', async ({ page }) => {
    await page.goto('/Text2Image');
    await expect(page.locator('#pe_enhance_btn')).toBeVisible();
    const themes = await registeredThemes(page);
    await openBorderedSurfaces(page);
    const missing: string[] = [];
    for (const [theme, cssPaths] of Object.entries(themes)) {
        await expect.poll(() => applyTheme(page, theme), { message: `theme ${theme} stylesheets load` }).toEqual(cssPaths);
        const borders = await page.evaluate((selectors) => selectors.map((selector) => {
            const elem = document.querySelector(selector);
            if (!elem) {
                return { selector, width: -1, style: 'missing' };
            }
            const style = getComputedStyle(elem);
            return { selector, width: Number.parseFloat(style.borderTopWidth), style: style.borderTopStyle };
        }), borderedSelectors);
        for (const entry of borders) {
            if (entry.width <= 0 || entry.style === 'none') {
                missing.push(`${theme} ${entry.selector}: ${entry.style} ${entry.width}px`);
            }
        }
        await page.screenshot({ path: path.join(shotDir, `${theme}.png`) });
    }
    expect(missing).toEqual([]);
});
