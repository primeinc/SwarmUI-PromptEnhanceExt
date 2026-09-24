import { expect, type Page, test } from '@playwright/test';
import * as path from 'node:path';

const shotDir = path.join(__dirname, 'shots');

/** Geometry of the prompt boxes against the splitter that tops the bottom panel. */
interface PromptGeometry {
    promptBottom: number;
    negativeBottom: number;
    splitterTop: number;
    /** `offsetHeight` of `#alt_prompt_extra_area`: the figure SwarmUI's region offset uses. */
    extraAreaOffsetHeight: number;
    /** Distance from the top of the region's content box to the prompt main line: the space the offset has to cover. */
    extraAreaRenderedHeight: number;
}

/** Measures where both prompt boxes end relative to the bottom panel's splitter bar. */
async function measure(page: Page): Promise<PromptGeometry> {
    return page.evaluate(() => {
        const rect = (id: string) => {
            const elem = document.getElementById(id);
            if (!elem) {
                throw new Error(`missing #${id}`);
            }
            return elem.getBoundingClientRect();
        };
        const region = document.getElementById('alt_prompt_region');
        const area = document.getElementById('alt_prompt_extra_area');
        const mainLine = document.querySelector('#alt_prompt_region .alt_prompt_main_line');
        if (!region || !area || !mainLine) {
            throw new Error('missing prompt region structure');
        }
        const regionStyle = getComputedStyle(region);
        const contentTop = region.getBoundingClientRect().top + Number.parseFloat(regionStyle.borderTopWidth) + Number.parseFloat(regionStyle.paddingTop);
        return {
            promptBottom: rect('alt_prompt_textbox').bottom,
            negativeBottom: rect('alt_negativeprompt_textbox').bottom,
            splitterTop: rect('t2i-mid-split-bar').top,
            extraAreaOffsetHeight: area.offsetHeight,
            extraAreaRenderedHeight: mainLine.getBoundingClientRect().top - contentTop,
        };
    });
}

/** Both prompt boxes end above the bottom panel, i.e. neither is covered by it. */
function expectPromptBoxesClear(geometry: PromptGeometry): void {
    expect(geometry.promptBottom, JSON.stringify(geometry)).toBeLessThanOrEqual(geometry.splitterTop);
    expect(geometry.negativeBottom, JSON.stringify(geometry)).toBeLessThanOrEqual(geometry.splitterTop);
    expect(Math.abs(geometry.extraAreaRenderedHeight - geometry.extraAreaOffsetHeight), `no margin escapes #alt_prompt_extra_area: ${JSON.stringify(geometry)}`).toBeLessThan(1);
}

/** Runs SwarmUI's prompt-region layout pass and waits two frames for it to apply. */
async function relayout(page: Page): Promise<void> {
    await page.evaluate(() => genTabLayout.altPromptSizeHandle());
    await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))));
}

test.beforeEach(async ({ page }) => {
    await page.goto('/Text2Image');
    await expect(page.locator('#alt_negativeprompt_textbox')).toBeVisible();
    await expect(page.locator('#pe_enhance_btn')).toBeVisible();
    await relayout(page);
});

test('control: without the extension UI the prompt boxes clear the bottom panel', async ({ page }) => {
    await page.evaluate(() => {
        document.getElementById('pe_button_bar')?.remove();
        document.getElementById('pe_preview')?.remove();
    });
    await relayout(page);
    expectPromptBoxesClear(await measure(page));
});

test('the Enhance bar does not push the prompt boxes under the bottom panel', async ({ page }) => {
    await page.screenshot({ path: path.join(shotDir, 'prompt-region-bar.png') });
    expectPromptBoxesClear(await measure(page));
});

test('an open enhancement preview does not push the prompt boxes under the bottom panel', async ({ page }) => {
    await page.locator('#alt_prompt_textbox').fill('a lighthouse at dusk');
    await page.evaluate(() => {
        const preview = document.getElementById('pe_preview');
        const text = document.getElementById('pe_preview_text');
        if (!preview || !text) {
            throw new Error('preview panel missing');
        }
        text.textContent = 'a weathered lighthouse at dusk, long exposure, crashing waves, warm window light';
        preview.style.display = 'block';
    });
    await relayout(page);
    await page.screenshot({ path: path.join(shotDir, 'prompt-region-preview.png') });
    expectPromptBoxesClear(await measure(page));
});
