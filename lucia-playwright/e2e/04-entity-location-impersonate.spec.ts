import { test, expect, type APIRequestContext } from '@playwright/test';
import dotenv from 'dotenv';
import path from 'path';

/**
 * Entity Locations — Agent Impersonation E2E Tests
 *
 * Validates that selecting an agent in the "Impersonate Agent" dropdown
 * correctly filters the entity list and search results to that agent's
 * visible domains and entity visibility settings.
 *
 * Prerequisites:
 *   - Aspire AppHost is running
 *   - Entity location cache is loaded
 *   - LUCIA_DASHBOARD_API_KEY is set in the repo root .env file
 *
 * Run with:
 *   SKIP_DOCKER=1 npx playwright test 04-entity-location-impersonate
 */

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

async function login(request: APIRequestContext) {
  const res = await request.post(`${BASE_URL}/api/auth/login`, {
    data: { apiKey: getDashboardApiKey() },
  });
  expect(res.ok(), `Login failed: ${res.status()} ${res.statusText()}`).toBeTruthy();
}

test.describe.serial('Entity Locations — Agent Impersonation', () => {

  test('available-agents API returns agents with domains', async ({ request }) => {
    await login(request);

    const res = await request.get(`${BASE_URL}/api/entity-location/visibility/available-agents`);
    expect(res.ok()).toBeTruthy();

    const agents: { name: string; domains: string[] }[] = await res.json();

    const lightAgent = agents.find(a => a.name === 'light-agent');
    expect(lightAgent, 'Expected light-agent in available agents').toBeTruthy();
    expect(lightAgent!.domains).toContain('light');
    expect(lightAgent!.domains).toContain('switch');
  });

  test('entities API filters by domain and agent visibility', async ({ request }) => {
    await login(request);

    // Domain-only: all light+switch entities
    const r1 = await request.get(
      `${BASE_URL}/api/entity-location/entities?domain=light,switch`
    );
    expect(r1.ok()).toBeTruthy();
    const allDomainEntities: { entityId: string; domain: string }[] = await r1.json();
    for (const e of allDomainEntities) {
      expect(['light', 'switch']).toContain(e.domain);
    }

    // Domain + agent: only entities visible to light-agent
    const r2 = await request.get(
      `${BASE_URL}/api/entity-location/entities?domain=light,switch&agent=light-agent`
    );
    expect(r2.ok()).toBeTruthy();
    const agentEntities: { entityId: string; domain: string }[] = await r2.json();
    expect(
      agentEntities.length,
      `Expected 22 entities visible to light-agent in light+switch domains, got ${agentEntities.length}`
    ).toBe(22);
  });

  test('impersonate light-agent on Entities tab shows 22 entities', async ({ page, request }) => {
    await login(request);
    const cookies = await request.storageState();
    await page.context().addCookies(cookies.cookies);

    await page.goto(`${BASE_URL}/entity-location`);
    await page.waitForLoadState('domcontentloaded');

    // Click the Entities tab (exact match to avoid "All Entities" button)
    const entitiesTab = page.getByRole('button', { name: 'Entities', exact: true });
    await entitiesTab.click();

    // Wait for entities table to appear
    await page.locator('tbody tr').first().waitFor({ timeout: 15_000 });

    // Select light-agent from the impersonate dropdown
    const agentSelect = page.locator('select').filter({ has: page.locator('option', { hasText: 'light-agent' }) });
    await agentSelect.selectOption('light-agent');

    // Wait for the impersonation badge to appear (indicates data reloaded)
    await expect(page.locator('text=light-agent view')).toBeVisible({ timeout: 10_000 });

    // Count visible entity rows
    const entityRows = page.locator('tbody tr').filter({ has: page.locator('td') });
    const rowCount = await entityRows.count();

    expect(
      rowCount,
      `Expected 22 entities visible to light-agent, got ${rowCount}`
    ).toBe(22);
  });

  test('search as light-agent for "zack\'s light" returns 1 result', async ({ page, request }) => {
    await login(request);
    const cookies = await request.storageState();
    await page.context().addCookies(cookies.cookies);

    await page.goto(`${BASE_URL}/entity-location`);
    await page.waitForLoadState('domcontentloaded');

    // Click the Search tab
    await page.locator('button', { hasText: 'Search' }).click();
    await page.waitForTimeout(500);

    // Select light-agent from the impersonate dropdown
    const agentSelect = page.locator('select').filter({ has: page.locator('option', { hasText: 'light-agent' }) });
    await agentSelect.selectOption('light-agent');

    // Type search term
    const searchInput = page.getByPlaceholder(/search by location/i);
    await searchInput.fill("zack's light");

    // Click Search action button (second one — the tab is first)
    await page.getByRole('button', { name: 'Search' }).nth(1).click();

    // Wait for results
    const matchText = page.locator('text=/\\d+ entit/');
    await expect(matchText).toBeVisible({ timeout: 15_000 });

    const text = await matchText.textContent();
    expect(
      text,
      `Expected "1 entity matched" but got "${text}"`
    ).toContain('1 entit');
  });
});
