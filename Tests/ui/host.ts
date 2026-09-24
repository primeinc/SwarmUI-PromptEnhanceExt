import { expect, type Page } from '@playwright/test';

/** The OpenAI-compatible fake backend started by playwright.config.ts. */
export const fakeBackendUrl = `http://127.0.0.1:${process.env.PE_FAKE_BACKEND_PORT ?? 7897}`;

/** Calls one SwarmUI API route from inside the page, through the host's own session-aware transport. */
export async function callRoute(page: Page, route: string, payload: object): Promise<unknown> {
    return page.evaluate(([route, payload]) => new Promise((resolve, reject) => {
        genericRequest(route as string, payload as object, resolve, 0, (err) => reject(new Error(String(err))));
    }), [route, payload] as const);
}

/** Opens the Generate tab and waits until the extension finished its session-ready startup. */
export async function openGenerateTab(page: Page): Promise<void> {
    const settingsLoaded = page.waitForResponse((resp) => resp.url().endsWith('/API/GetPromptEnhanceSettings') && resp.status() === 200);
    await page.goto('/Text2Image');
    await expect(page.locator('#pe_enhance_btn')).toBeVisible();
    await settingsLoaded;
}

/** Stores `settings` server-side, then reloads so the client picks them up. */
export async function useSettings(page: Page, settings: Partial<PESettings>): Promise<void> {
    await openGenerateTab(page);
    const saved = await callRoute(page, 'SavePromptEnhanceSettings', { settings });
    expect(saved, 'settings saved').toMatchObject({ success: true, settings });
    await openGenerateTab(page);
}

/** Points the extension at the fake backend with the given apply mode. */
export async function useFakeBackend(page: Page, replaceMode: PEReplaceMode): Promise<void> {
    await useSettings(page, { baseUrl: fakeBackendUrl, model: 'fake-enhancer', replaceMode });
}
