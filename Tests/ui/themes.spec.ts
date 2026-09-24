import { expect, type Page, test } from '@playwright/test';
import * as path from 'node:path';

const shotDir = path.join(__dirname, 'shots', 'themes');

/** Elements promptenhance.css draws a 1px border on, opened into view before measuring. Buttons and the settings modal carry SwarmUI's own styling. */
const borderedSelectors = ['#pe_preview', '#pe_preview_text'];

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
        if (!selector) {
            throw new Error('theme selector missing');
        }
        selector.value = theme;
        triggerChangeFor(selector);
        const links = [...document.querySelectorAll<HTMLLinkElement>('link.theme_sheet_header')];
        await Promise.all(links.map((link) => link.sheet ? null : new Promise((resolve, reject) => {
            link.addEventListener('load', resolve);
            link.addEventListener('error', () => reject(new Error(`failed to load ${link.href}`)));
        })));
        return links.map((link) => new URL(link.href).pathname.slice(1));
    }, theme);
}

/** Opens the preview and Restore so every extension surface is on screen. */
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

test('the settings modal renders in every registered theme', async ({ page }) => {
    await page.goto('/Text2Image');
    await expect(page.locator('#pe_settings_button')).toBeVisible();
    const themes = await registeredThemes(page);
    await page.locator('#pe_settings_button').click();
    const modal = page.locator('#pe_settings_modal .modal-content');
    await expect(modal).toBeVisible();
    const broken: string[] = [];
    for (const [theme, cssPaths] of Object.entries(themes)) {
        await expect.poll(() => applyTheme(page, theme), { message: `theme ${theme} stylesheets load` }).toEqual(cssPaths);
        const box = await modal.evaluate((elem) => {
            const rect = elem.getBoundingClientRect();
            return { width: rect.width, height: rect.height, background: getComputedStyle(elem).backgroundColor };
        });
        if (box.width < 200 || box.height < 200 || box.background === 'rgba(0, 0, 0, 0)' || box.background === 'transparent') {
            broken.push(`${theme}: ${box.width}x${box.height} background ${box.background}`);
        }
        await modal.screenshot({ path: path.join(shotDir, `modal-${theme}.png`) });
    }
    expect(broken, 'the modal has a real size and an opaque background in every theme').toEqual([]);
});
