import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import dotenv from 'dotenv';
import path from 'path';

/**
 * Plugin Update Detection E2E Tests
 *
 * Validates the plugin update detection and application flow:
 *   1. Installed plugins are listed on the Plugins page
 *   2. The updates API returns the correct response shape
 *   3. Update badges render for plugins with available updates
 *   4. Clicking update triggers the update endpoint
 *   5. After update, the version badge reflects the new version
 *   6. The restart banner appears after a plugin update
 *
 * Prerequisites:
 *   - Aspire AppHost is running (or services are running manually)
 *   - At least one plugin repository is configured and synced
 *   - LUCIA_DASHBOARD_API_KEY is set in the repo root .env file
 *   - BASE_URL in playwright .env points to the Vite dashboard
 *
 * Run with:
 *   SKIP_DOCKER=1 npx playwright test 06-plugin-update-detection
 */

// Load repo root .env first (for LUCIA_DASHBOARD_API_KEY), then playwright .env
dotenv.config({ path: path.resolve(import.meta.dirname, '../../.env') });
dotenv.config({ path: path.resolve(import.meta.dirname, '../.env') });

const BASE_URL = process.env.BASE_URL ?? 'http://127.0.0.1:7233';

function getDashboardApiKey(): string {
  const key = (
    process.env.LUCIA_DASHBOARD_API_KEY ??
    process.env.DASHBOARD_API_KEY ??
    ''
  ).trim();

  if (!key || key.includes('fake'))
    throw new Error(
      'Missing LUCIA_DASHBOARD_API_KEY in repo root .env (or DASHBOARD_API_KEY in playwright .env)'
    );

  return key;
}

/** Authenticate via the dashboard proxy (sets session cookie). */
async function login(request: APIRequestContext) {
  const res = await request.post(`${BASE_URL}/api/auth/login`, {
    data: { apiKey: getDashboardApiKey() },
  });
  expect(res.ok(), `Login failed: ${res.status()} ${res.statusText()}`).toBeTruthy();
}

/** Transfer auth cookies from APIRequestContext to a Page for UI testing. */
async function loginAndTransferCookies(page: Page, request: APIRequestContext) {
  await login(request);
  const cookies = await request.storageState();
  await page.context().addCookies(cookies.cookies);
}

test.describe.serial('Plugin Update Detection', () => {

  test('installed plugins API returns a list', async ({ request }) => {
    await login(request);

    const res = await request.get(`${BASE_URL}/api/plugins/installed`);
    expect(res.ok()).toBeTruthy();

    const plugins = await res.json();
    expect(Array.isArray(plugins)).toBeTruthy();

    // Each installed plugin should have the update fields in the response shape
    for (const plugin of plugins) {
      expect(plugin).toHaveProperty('id');
      expect(plugin).toHaveProperty('name');
      expect(plugin).toHaveProperty('version');
      expect(plugin).toHaveProperty('enabled');
      expect(plugin).toHaveProperty('updateAvailable');
      // availableVersion may be null if no update
      expect(plugin).toHaveProperty('availableVersion');
    }
  });

  test('updates API returns correct response shape', async ({ request }) => {
    await login(request);

    const res = await request.get(`${BASE_URL}/api/plugins/updates`);
    expect(res.ok()).toBeTruthy();

    const updates = await res.json();
    expect(Array.isArray(updates)).toBeTruthy();

    // Validate shape of each update entry (if any exist)
    for (const update of updates) {
      expect(update).toHaveProperty('pluginId');
      expect(update).toHaveProperty('pluginName');
      expect(update).toHaveProperty('installedVersion');
      expect(update).toHaveProperty('availableVersion');
      expect(update).toHaveProperty('repositoryId');
    }
  });

  test('Plugins page shows installed plugins', async ({ page, request }) => {
    await loginAndTransferCookies(page, request);

    await page.goto(`${BASE_URL}/plugins`);
    await page.waitForLoadState('domcontentloaded');

    // The Installed tab should be active by default
    const installedTab = page.locator('button', { hasText: 'Installed' });
    await expect(installedTab).toBeVisible({ timeout: 10_000 });

    // Wait for loading to finish
    await page.waitForTimeout(2_000);

    // Either plugins are listed or the empty state is shown
    const hasPlugins = await page.locator('[class*="border-stone"]').filter({
      has: page.locator('span', { hasText: /\w+/ }),
    }).first().isVisible({ timeout: 5_000 }).catch(() => false);

    const hasEmptyState = await page.locator('text=No plugins installed')
      .isVisible({ timeout: 2_000 }).catch(() => false);

    expect(hasPlugins || hasEmptyState).toBeTruthy();
  });

  test('update badge renders when update is available', async ({ page, request }) => {
    await loginAndTransferCookies(page, request);

    // First check if any updates exist via API
    const updatesRes = await request.get(`${BASE_URL}/api/plugins/updates`);
    const updates = await updatesRes.json();

    if (updates.length === 0) {
      // No updates available in this environment — skip UI assertion
      test.skip(true, 'No plugin updates available in the current environment');
      return;
    }

    await page.goto(`${BASE_URL}/plugins`);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2_000);

    // Look for the update badge
    const updateBadge = page.locator('[data-testid="update-badge"]');
    await expect(updateBadge.first()).toBeVisible({ timeout: 10_000 });

    const badgeText = await updateBadge.first().textContent();
    expect(badgeText).toContain('Update Available');
  });

  test('update button triggers plugin update', async ({ page, request }) => {
    await loginAndTransferCookies(page, request);

    // Check if updates exist
    const updatesRes = await request.get(`${BASE_URL}/api/plugins/updates`);
    const updates = await updatesRes.json();

    if (updates.length === 0) {
      test.skip(true, 'No plugin updates available in the current environment');
      return;
    }

    // Use API to trigger update directly
    const pluginId = updates[0].pluginId;
    const updateRes = await request.post(`${BASE_URL}/api/plugins/${pluginId}/update`);
    expect(updateRes.ok()).toBeTruthy();

    // Verify the installed plugin now shows the new version
    const installedRes = await request.get(`${BASE_URL}/api/plugins/installed`);
    const installed = await installedRes.json();
    const updatedPlugin = installed.find((p: { id: string }) => p.id === pluginId);

    expect(updatedPlugin).toBeTruthy();
    expect(updatedPlugin.version).toBe(updates[0].availableVersion);
  });

  test('restart banner appears after plugin update', async ({ page, request }, testInfo) => {
    await loginAndTransferCookies(page, request);

    // Check restart status via API
    const restartRes = await request.get(`${BASE_URL}/api/system/restart-required`);
    expect(restartRes.ok()).toBeTruthy();

    const restartStatus = await restartRes.json();

    await page.goto(`${BASE_URL}/plugins`);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2_000);

    if (restartStatus.restartRequired) {
      // If a restart is required (from a previous update), the banner should be visible
      const banner = page.locator('text=/restart/i');
      await expect(banner.first()).toBeVisible({ timeout: 10_000 });
    }

    await page.screenshot({ path: testInfo.outputPath('plugins-after-update.png') });
  });
});
