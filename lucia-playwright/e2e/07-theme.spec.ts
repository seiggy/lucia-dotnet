import { expect, test, type Page } from '@playwright/test';

async function mockSetup(page: Page): Promise<void> {
  await page.route('**/api/auth/status', async (route) => {
    await route.fulfill({ json: { authenticated: false, setupComplete: false, hasKeys: false } });
  });
  await page.route('**/api/setup/status', async (route) => {
    await route.fulfill({
      json: {
        hasDashboardKey: false,
        hasHaConnection: false,
        haUrl: null,
        hasChatProvider: false,
        pluginValidated: false,
        setupComplete: false,
      },
    });
  });
}

async function mockAuthenticatedDashboard(page: Page): Promise<void> {
  await page.route('**/api/auth/status', async (route) => {
    await route.fulfill({ json: { authenticated: true, setupComplete: true, hasKeys: true } });
  });
  await page.route('**/api/activity/summary', async (route) => {
    await route.fulfill({ status: 503, body: '' });
  });
  await page.route('**/api/activity/mesh', async (route) => {
    await route.fulfill({ status: 503, body: '' });
  });
  await page.route('**/api/activity/agent-stats', async (route) => {
    await route.fulfill({ status: 503, body: '' });
  });
  await page.route('**/api/activity/live', async (route) => {
    await route.fulfill({ status: 204, body: '' });
  });
}

test.describe('theme preference', () => {
  test('follows the system and persists explicit choices on setup', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await mockSetup(page);
    await page.goto('/');

    const root = page.locator('html');
    const themeControl = page.getByRole('group', { name: 'Theme' });

    await expect(root).toHaveAttribute('data-theme', 'light');
    await expect.poll(() => root.evaluate((element) => getComputedStyle(element).getPropertyValue('--color-void').trim())).toBe('#f1f2ef');
    await expect(themeControl).toBeVisible();
    await expect(page.getByRole('button', { name: 'Use system theme' })).toHaveAttribute('aria-pressed', 'true');

    await page.getByRole('button', { name: 'Use light theme' }).click();
    await expect(root).toHaveAttribute('data-theme', 'light');
    await expect.poll(() => page.evaluate(() => localStorage.getItem('lucia-theme'))).toBe('light');

    await page.reload();
    await expect(root).toHaveAttribute('data-theme', 'light');

    await page.getByRole('button', { name: 'Use dark theme' }).click();
    await expect(root).toHaveAttribute('data-theme', 'dark');
    await expect.poll(() => page.evaluate(() => localStorage.getItem('lucia-theme'))).toBe('dark');

    await page.reload();
    await expect(root).toHaveAttribute('data-theme', 'dark');

    await page.getByRole('button', { name: 'Use system theme' }).click();
    await expect(root).toHaveAttribute('data-theme', 'light');
    await expect.poll(() => page.evaluate(() => localStorage.getItem('lucia-theme'))).toBe('system');
    await page.emulateMedia({ colorScheme: 'dark' });
    await expect(root).toHaveAttribute('data-theme', 'dark');
  });

  test('keeps the selector available in the authenticated shell', async ({ page }) => {
    await mockAuthenticatedDashboard(page);
    await page.goto('/');

    await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible();
    await expect(page.getByRole('group', { name: 'Theme' })).toBeVisible();

    await page.setViewportSize({ width: 390, height: 844 });
    await page.getByRole('button', { name: 'Open sidebar menu' }).click();
    await page.getByRole('button', { name: 'Use dark theme' }).click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  });

  test('starts when browser storage is unavailable', async ({ page }) => {
    await page.addInitScript(() => {
      Object.defineProperty(window, 'localStorage', {
        configurable: true,
        get() {
          throw new DOMException('Storage is blocked', 'SecurityError');
        },
      });
    });
    await page.emulateMedia({ colorScheme: 'light' });
    await mockSetup(page);
    await page.goto('/');

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
    await expect(page.getByRole('group', { name: 'Theme' })).toBeVisible();
  });
});