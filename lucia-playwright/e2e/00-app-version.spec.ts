import { expect, test } from '@playwright/test';

test('shows the running Docker version at the bottom of desktop and mobile navigation', async ({ page }) => {
  await page.route('**/api/auth/status', route => route.fulfill({
    json: { authenticated: true, setupComplete: true, hasKeys: true },
  }));
  await page.route('**/api/appliance/capabilities', route => route.fulfill({ status: 404 }));
  await page.route('**/api/system/version', route => route.fulfill({ json: { version: '1.5.1-voice' } }));
  await page.route('**/api/speakers', route => route.fulfill({ json: [] }));
  await page.goto('/user-memories');
  const version = page.getByLabel('Application version');
  await expect(version).toHaveText('Lucia 1.5.1-voice');
  const footer = version.locator('..');
  await expect(footer.getByRole('button', { name: 'Sign Out' })).toBeVisible();
  for (const viewport of [{ width: 1280, height: 900 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(viewport);
    if (viewport.width < 768) {
      await page.getByRole('button', { name: 'Open sidebar menu' }).click();
    }
    await expect(version).toBeVisible();
    const box = await version.boundingBox();
    expect(box).not.toBeNull();
    expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(viewport.width);
  }
});

test('reports unavailable version metadata instead of showing a made-up release', async ({ page }) => {
  await page.route('**/api/auth/status', route => route.fulfill({
    json: { authenticated: true, setupComplete: true, hasKeys: true },
  }));
  await page.route('**/api/appliance/capabilities', route => route.fulfill({ status: 404 }));
  await page.route('**/api/system/version', route => route.fulfill({ status: 503 }));
  await page.route('**/api/speakers', route => route.fulfill({ json: [] }));
  await page.goto('/user-memories');
  await expect(page.getByLabel('Application version')).toHaveText('Version unavailable');
  await expect(page.getByRole('button', { name: 'Sign Out' })).toBeVisible();
});
