import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import dotenv from 'dotenv';
import path from 'path';

/**
 * Skill Optimizer — Import from Traces E2E Test
 *
 * Validates that the Skill Optimizer can import search terms from
 * existing conversation traces for a skill's owning agent.
 *
 * Prerequisites:
 *   - Aspire AppHost is running (or services are running manually)
 *   - Traces exist for the Light Agent (at least one conversation)
 *   - LUCIA_DASHBOARD_API_KEY is set in the repo root .env file
 *   - BASE_URL in playwright .env points to the Vite dashboard
 *     (check Aspire dashboard for the lucia-dashboard endpoint URL)
 *
 * Run with:
 *   SKIP_DOCKER=1 npx playwright test 03-skill-optimizer-traces
 */

// Load repo root .env first (for LUCIA_DASHBOARD_API_KEY), then playwright .env
dotenv.config({ path: path.resolve(import.meta.dirname, '../../.env') });
dotenv.config({ path: path.resolve(import.meta.dirname, '../.env') });

// AgentHost has a fixed port from launchSettings.json — used for direct API tests
// Use 127.0.0.1 instead of localhost to avoid Node.js IPv6 resolution issues
const AGENTHOST_URL = process.env.AGENTHOST_URL ?? 'http://127.0.0.1:5151';
// Dashboard is pinned to port 7233 in AppHost.cs via WithHttpEndpoint
const DASHBOARD_URL = process.env.BASE_URL ?? 'http://127.0.0.1:7233';

function getDashboardApiKey(): string {
  const key =
    process.env.LUCIA_DASHBOARD_API_KEY ??
    process.env.DASHBOARD_API_KEY;

  if (!key || key.includes('fake'))
    throw new Error(
      'Missing LUCIA_DASHBOARD_API_KEY in repo root .env (or DASHBOARD_API_KEY in playwright .env)'
    );

  return key;
}

/** Authenticate against the agenthost API directly (bypasses Vite proxy). */
async function loginApi(request: APIRequestContext) {
  const res = await request.post(`${AGENTHOST_URL}/api/auth/login`, {
    data: { apiKey: getDashboardApiKey() },
  });
  expect(res.ok(), `Login failed: ${res.status()} ${res.statusText()}`).toBeTruthy();
}

/** Authenticate the browser page session (for UI tests via the dashboard). */
async function loginPage(page: Page) {
  await page.request.post('/api/auth/login', {
    data: { apiKey: getDashboardApiKey() },
  });
}

test.describe.serial('Skill Optimizer — Import from Traces', () => {

  test('traces API returns Light Agent traces', async ({ request }) => {
    await loginApi(request);

    const res = await request.get(
      `${AGENTHOST_URL}/api/traces?agentFilter=light-agent&pageSize=10&page=1`
    );
    expect(res.ok()).toBeTruthy();

    const body = await res.json();
    expect(
      body.items.length,
      'Expected at least one trace for light-agent. Send a light command first (e.g. "turn on kitchen lights").'
    ).toBeGreaterThan(0);

    // Verify at least one trace has an AgentExecution with light-agent
    const hasLightAgent = body.items.some((trace: any) =>
      trace.agentExecutions?.some((exec: any) => exec.agentId === 'light-agent')
    );
    expect(hasLightAgent).toBeTruthy();
  });

  test('skill traces API returns search terms for light-control', async ({ request }) => {
    await loginApi(request);

    const res = await request.get(
      `${AGENTHOST_URL}/api/skill-optimizer/skills/light-control/traces?limit=200`
    );
    expect(res.ok()).toBeTruthy();

    const traces: any[] = await res.json();

    // This is the core assertion — the API should return extracted search terms
    expect(
      traces.length,
      'Expected skill traces API to return search terms extracted from Light Agent traces. ' +
        'Got 0 results — the trace import is broken.'
    ).toBeGreaterThan(0);

    // Each result should have the expected shape
    for (const t of traces) {
      expect(t).toHaveProperty('searchTerm');
      expect(t).toHaveProperty('occurrenceCount');
      expect(t.searchTerm).toBeTruthy();
      expect(t.occurrenceCount).toBeGreaterThanOrEqual(1);
    }
  });

  test('Import from Traces button populates test cases in the UI', async ({ page }) => {
    await loginPage(page);
    await page.goto('/skill-optimizer');
    await page.waitForLoadState('networkidle');

    // Select the "Light Control" skill from the CustomSelect dropdown
    const skillSection = page.locator('label', { hasText: /^Skill$/i }).locator('..');
    const skillButton = skillSection.locator('button').first();
    await skillButton.click();
    await page.waitForTimeout(300);

    // Type to filter and click the Light Control option
    const filterInput = page.getByPlaceholder('Type to filter...');
    if (await filterInput.isVisible({ timeout: 2000 }).catch(() => false)) {
      await filterInput.fill('Light');
      await page.waitForTimeout(200);
    }
    const lightOption = page.locator('[data-option]', { hasText: /Light Control/i }).first();
    await lightOption.click();
    await page.waitForTimeout(500);

    // Verify skill is selected (params should be visible)
    await expect(page.locator('text=Threshold:')).toBeVisible({ timeout: 5000 });

    // Click "Import from Traces"
    const importButton = page.getByRole('button', { name: /Import from Traces/i });
    await expect(importButton).toBeEnabled();
    await importButton.click();

    // Wait for either a success toast or error toast
    const successToast = page.locator('text=Imported').first();
    const errorToast = page.locator('text=No trace data found').first();
    const failedToast = page.locator('text=Failed to import').first();

    const result = await Promise.race([
      successToast.waitFor({ timeout: 15_000 }).then(() => 'success' as const),
      errorToast.waitFor({ timeout: 15_000 }).then(() => 'no-data' as const),
      failedToast.waitFor({ timeout: 15_000 }).then(() => 'failed' as const),
    ]);

    // The import should succeed — if it doesn't, the trace extraction is broken
    expect(
      result,
      'Expected "Import from Traces" to succeed with imported search terms, ' +
        `but got "${result}". The skill optimizer is not correctly extracting ` +
        'search terms from Light Agent traces.'
    ).toBe('success');

    // Verify test cases were actually added to the table
    const testCaseRows = page.locator('input[placeholder="Search term..."]');
    const rowCount = await testCaseRows.count();
    expect(
      rowCount,
      'Expected at least one test case row after importing from traces'
    ).toBeGreaterThan(0);

    // Verify the imported cases are tagged as "trace" variant
    const traceBadges = page.locator('text=trace');
    const badgeCount = await traceBadges.count();
    expect(badgeCount).toBeGreaterThan(0);
  });
});
