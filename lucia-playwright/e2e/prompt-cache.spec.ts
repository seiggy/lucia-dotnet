import { test, expect, type Page } from '@playwright/test';

/**
 * Prompt Cache E2E Tests
 *
 * Validates that the routing + agent-level prompt cache works correctly:
 *  1. Identical prompts produce cache hits (hitCount > 0)
 *  2. Semantically different prompts ("turn on" vs "turn off") do NOT collide
 *  3. Cache eviction works from the dashboard UI
 *
 * Requires a running Lucia instance with at least one agent registered
 * and a valid DASHBOARD_API_KEY in .env.
 */

const DASHBOARD_API_KEY = process.env.DASHBOARD_API_KEY!;

test.beforeAll(() => {
  if (!process.env.DASHBOARD_API_KEY) {
    throw new Error(
      'Missing DASHBOARD_API_KEY env var. Copy .env.template to .env and set your dashboard key.'
    );
  }
});

/** Log into the dashboard via the /login page. */
async function login(page: Page) {
  await page.goto('/login', { waitUntil: 'networkidle' });
  await page.getByLabel(/api key/i).fill(DASHBOARD_API_KEY);
  await page.getByRole('button', { name: /sign in/i }).click();
  // Wait for redirect away from /login
  await page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 10_000 });
}

/** Navigate to the Prompt Cache page and return { entries, stats }. */
async function getPromptCacheState(page: Page) {
  await page.goto('/prompt-cache', { waitUntil: 'networkidle' });
  // Wait for the cache table to render
  await page.waitForSelector('table', { timeout: 10_000 });
  // Small settle time for stats to populate
  await page.waitForTimeout(500);

  const stats = {
    totalEntries: await extractStatNumber(page, 'Total Entries'),
    hitRate: await extractStatText(page, 'Hit Rate'),
    totalHits: await extractStatNumber(page, 'Total Hits'),
    totalMisses: await extractStatNumber(page, 'Total Misses'),
  };

  // Collect entry rows
  const rows = page.locator('tbody tr');
  const rowCount = await rows.count();
  const entries: { prompt: string; agent: string; hitCount: number }[] = [];

  for (let i = 0; i < rowCount; i++) {
    const cells = rows.nth(i).locator('td');
    const cellCount = await cells.count();
    if (cellCount < 4) continue; // skip the "No cached entries" placeholder row

    entries.push({
      prompt: (await cells.nth(0).textContent()) ?? '',
      agent: (await cells.nth(1).textContent()) ?? '',
      hitCount: parseInt((await cells.nth(3).textContent()) ?? '0', 10),
    });
  }

  return { stats, entries };
}

/** Extract a numeric stat value from the cache stats panel. */
async function extractStatNumber(page: Page, label: string): Promise<number> {
  const panel = page.locator('.glass-panel', { hasText: label }).first();
  const value = await panel.locator('p.font-display').textContent();
  return parseInt(value?.replace(/[^0-9]/g, '') ?? '0', 10);
}

/** Extract a text stat value from the cache stats panel. */
async function extractStatText(page: Page, label: string): Promise<string> {
  const panel = page.locator('.glass-panel', { hasText: label }).first();
  return (await panel.locator('p.font-display').textContent()) ?? '';
}

/** Select the first agent and send a message via the Agent Dashboard chat. */
async function sendAgentMessage(page: Page, message: string) {
  await page.goto('/agent-dashboard', { waitUntil: 'networkidle' });

  // Wait for agent cards to load
  const agentCards = page.locator('[class*="cursor-pointer"]').filter({ hasText: /v\d/ });
  await expect(agentCards.first()).toBeVisible({ timeout: 15_000 });

  // Click the first agent to select it
  await agentCards.first().click();

  // Disable HA Simulation context so we send a clean prompt (no volatile fields)
  const haToggle = page.locator('label', { hasText: /HA Simulation/i }).locator('input[type="checkbox"]');
  if (await haToggle.isVisible()) {
    // Uncheck if it's checked
    if (await haToggle.isChecked()) {
      await haToggle.uncheck();
    }
  }

  // Wait for the message input to appear
  const input = page.getByPlaceholder(/send a test message/i);
  await expect(input).toBeVisible({ timeout: 10_000 });

  // Type the message and send
  await input.fill(message);
  await page.getByRole('button', { name: /send/i }).click();

  // Wait for the agent response (the bouncing dots disappear and a response bubble appears)
  await page.waitForFunction(
    () => {
      // Wait until there's no bouncing animation and at least one assistant message
      const sending = document.querySelector('.animate-bounce');
      const messages = document.querySelectorAll('[class*="bg-basalt"][class*="rounded-xl"]');
      return !sending && messages.length > 0;
    },
    { timeout: 60_000 }
  );

  // Give a moment for cache write to complete
  await page.waitForTimeout(1_000);
}

/** Clear all prompt cache entries via the dashboard UI. */
async function clearCache(page: Page) {
  await page.goto('/prompt-cache', { waitUntil: 'networkidle' });
  await page.waitForSelector('table', { timeout: 10_000 });

  const clearButton = page.getByRole('button', { name: /clear all/i });
  if (await clearButton.isEnabled()) {
    // Accept the confirm dialog
    page.on('dialog', (dialog) => dialog.accept());
    await clearButton.click();
    await page.waitForTimeout(1_000);
  }
}

// Run steps sequentially — each depends on cache state from the previous
test.describe.serial('Prompt Cache Validation', () => {
  test('login and clear existing cache', async ({ page }) => {
    await login(page);
    await clearCache(page);

    const state = await getPromptCacheState(page);
    expect(state.stats.totalEntries).toBe(0);
  });

  test('first prompt creates a cache entry (miss)', async ({ page }) => {
    await login(page);
    await sendAgentMessage(page, 'turn on the lights in the office');

    const state = await getPromptCacheState(page);
    expect(state.stats.totalEntries).toBeGreaterThanOrEqual(1);
    expect(state.stats.totalMisses).toBeGreaterThanOrEqual(1);

    // Find the entry matching our prompt
    const matchingEntry = state.entries.find((e) =>
      e.prompt.toLowerCase().includes('turn on the lights in the office')
    );
    expect(matchingEntry).toBeDefined();
    expect(matchingEntry!.hitCount).toBe(0); // First time = miss, no hits yet
  });

  test('identical prompt produces a cache hit', async ({ page }) => {
    await login(page);

    // Record stats before the second send
    const before = await getPromptCacheState(page);
    const hitsBefore = before.stats.totalHits;

    await sendAgentMessage(page, 'turn on the lights in the office');

    const after = await getPromptCacheState(page);

    // Total hits should have increased
    expect(after.stats.totalHits).toBeGreaterThan(hitsBefore);

    // The entry for our prompt should now have hits > 0
    const matchingEntry = after.entries.find((e) =>
      e.prompt.toLowerCase().includes('turn on the lights in the office')
    );
    expect(matchingEntry).toBeDefined();
    expect(matchingEntry!.hitCount).toBeGreaterThanOrEqual(1);
  });

  test('different prompt does NOT hit existing cache', async ({ page }) => {
    await login(page);

    const before = await getPromptCacheState(page);
    const entriesBefore = before.stats.totalEntries;
    const missesBefore = before.stats.totalMisses;

    // Send a semantically different prompt — "turn off" vs "turn on"
    await sendAgentMessage(page, 'turn off the lights in the office');

    const after = await getPromptCacheState(page);

    // Should create a NEW cache entry (not reuse the "turn on" one)
    expect(after.stats.totalEntries).toBeGreaterThan(entriesBefore);
    expect(after.stats.totalMisses).toBeGreaterThan(missesBefore);

    // The "turn on" entry should still have the same hit count as before
    const turnOnEntry = after.entries.find((e) =>
      e.prompt.toLowerCase().includes('turn on the lights in the office')
    );
    const turnOnBefore = before.entries.find((e) =>
      e.prompt.toLowerCase().includes('turn on the lights in the office')
    );
    expect(turnOnEntry).toBeDefined();
    expect(turnOnEntry!.hitCount).toBe(turnOnBefore!.hitCount);

    // The "turn off" entry should exist with hitCount = 0
    const turnOffEntry = after.entries.find((e) =>
      e.prompt.toLowerCase().includes('turn off the lights in the office')
    );
    expect(turnOffEntry).toBeDefined();
    expect(turnOffEntry!.hitCount).toBe(0);
  });

  test('cache eviction removes entries', async ({ page }) => {
    await login(page);

    // Verify we have entries before clearing
    const before = await getPromptCacheState(page);
    expect(before.stats.totalEntries).toBeGreaterThanOrEqual(2);

    await clearCache(page);

    const after = await getPromptCacheState(page);
    expect(after.stats.totalEntries).toBe(0);
  });
});
