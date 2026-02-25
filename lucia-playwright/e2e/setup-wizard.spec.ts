import { test, expect, type Page } from '@playwright/test';

/**
 * Environment variables loaded by playwright.config.ts via dotenv.
 * Required: HA_TOKEN, AZURE_ENDPOINT, AZURE_API_KEY
 */
const HA_URL = process.env.HA_URL || 'http://homeassistant:8123';
const HA_TOKEN = process.env.HA_TOKEN!;
const AZURE_ENDPOINT = process.env.AZURE_ENDPOINT!;
const AZURE_API_KEY = process.env.AZURE_API_KEY!;
const AZURE_CHAT_DEPLOYMENT = process.env.AZURE_CHAT_DEPLOYMENT || 'chat';
const AZURE_EMBEDDING_DEPLOYMENT = process.env.AZURE_EMBEDDING_DEPLOYMENT || 'text-embedding-3-small';

test.beforeAll(() => {
  const missing: string[] = [];
  if (!process.env.HA_TOKEN) missing.push('HA_TOKEN');
  if (!process.env.AZURE_ENDPOINT) missing.push('AZURE_ENDPOINT');
  if (!process.env.AZURE_API_KEY) missing.push('AZURE_API_KEY');
  if (missing.length > 0) {
    throw new Error(
      `Missing required env vars: ${missing.join(', ')}. Copy .env.template to .env and fill in values.`
    );
  }
});

// Run steps sequentially — each depends on the previous
test.describe.serial('Setup Wizard', () => {
  let dashboardKey = '';

  test('redirects to /setup on first visit', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });
    await expect(page).toHaveURL(/\/setup/);
  });

  test('Step 1: Welcome — click Get Started', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });
    const getStartedBtn = page.getByRole('button', { name: /Get Started/i });
    await expect(getStartedBtn).toBeVisible();
    await getStartedBtn.click();

    // Should advance to configuration step
    await expect(page.locator('button', { hasText: /Generate Dashboard Key/i })).toBeVisible();
  });

  test('Step 2: Generate dashboard key and configure Home Assistant', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });
    // Advance past welcome
    await page.getByRole('button', { name: /Get Started/i }).click();

    // 2a: Generate Dashboard API Key
    const genKeyBtn = page.locator('button', { hasText: /Generate Dashboard Key/i });
    await expect(genKeyBtn).toBeVisible();
    await genKeyBtn.click();
    await page.waitForTimeout(2000);

    // Capture the generated key
    const codeEl = page.locator('code').first();
    await expect(codeEl).toBeVisible();
    dashboardKey = (await codeEl.textContent())?.trim() || '';
    expect(dashboardKey).toMatch(/^lk_/);

    // 2b: Fill Home Assistant connection details
    const urlInput = page.locator('input[type="url"], input[placeholder*="homeassistant"]').first();
    await urlInput.clear();
    await urlInput.fill(HA_URL);

    const tokenInput = page.locator('input[type="password"], input[placeholder*="token"]').first();
    await tokenInput.clear();
    await tokenInput.fill(HA_TOKEN);

    // 2c: Save HA configuration
    const saveBtn = page.locator('button', { hasText: /^Save$/i });
    if (await saveBtn.isVisible({ timeout: 3000 })) {
      await saveBtn.click();
      await page.waitForTimeout(2000);
    }

    // 2d: Test connection (may fail — HA not reachable from Docker)
    const testBtn = page.locator('button', { hasText: /Test Connection/i });
    if (await testBtn.isVisible({ timeout: 3000 })) {
      await testBtn.click();
      await page.waitForTimeout(5000);
    }

    // 2e: Advance to next step
    const nextBtn = page.locator('button', { hasText: /^Next$/i });
    await expect(nextBtn).toBeVisible();
    await nextBtn.click();
    await page.waitForTimeout(2000);

    // Should be on HA Plugin step
    await expect(page.locator('button', { hasText: /Generate HA Key/i })).toBeVisible();
  });

  test('Step 3: Generate HA Plugin key and complete setup', async ({ page }) => {
    await navigateToStep3(page);

    // Generate HA Plugin Key
    const genHaKeyBtn = page.locator('button', { hasText: /Generate HA Key/i });
    await expect(genHaKeyBtn).toBeVisible();
    await genHaKeyBtn.click();
    await page.waitForTimeout(2000);

    // Verify key appears
    const codeEls = page.locator('code');
    const count = await codeEls.count();
    let haKey = '';
    for (let i = 0; i < count; i++) {
      const text = await codeEls.nth(i).textContent();
      if (text?.startsWith('lk_')) {
        haKey = text.trim();
        break;
      }
    }
    expect(haKey).toMatch(/^lk_/);

    // Complete setup (skip plugin validation)
    const skipBtn = page.locator('button', { hasText: /Skip.*Complete|Complete Setup/i });
    await expect(skipBtn).toBeVisible();
    await skipBtn.click();
    await page.waitForTimeout(3000);

    // Should show the "Done" screen with a "Go to Dashboard" action
    await expect(page.locator('a, button', { hasText: /Go to Dashboard/i })).toBeVisible();
  });

  test('Step 4: Navigate to dashboard (auto-login after key generation)', async ({ page }) => {
    await navigateToStep3(page);

    // Complete setup quickly
    await page.locator('button', { hasText: /Generate HA Key/i }).click();
    await page.waitForTimeout(2000);
    await page.locator('button', { hasText: /Skip.*Complete|Complete Setup/i }).click();
    await page.waitForTimeout(3000);

    // Click "Go to Dashboard"
    const goDashBtn = page.locator('a, button', { hasText: /Go to Dashboard/i });
    await goDashBtn.click();
    await page.waitForTimeout(3000);

    // Should be on main dashboard — NOT on /login (auto-login from key generation)
    const url = page.url();
    expect(url).not.toContain('/login');
    expect(url).not.toContain('/setup');
  });

  test('Step 5: Configure AI providers', async ({ page }) => {
    await completeSetupAndLogin(page);

    // Navigate to model providers
    await page.goto('/model-providers', { waitUntil: 'networkidle' });

    // --- Create Chat Provider ---
    await createProvider(page, {
      id: 'azure-chat',
      name: 'Azure OpenAI Chat',
      purpose: 'Chat',
      providerType: 'AzureOpenAI',
      endpoint: AZURE_ENDPOINT,
      deployment: AZURE_CHAT_DEPLOYMENT,
      apiKey: AZURE_API_KEY,
    });

    // Navigate back if needed
    const backBtn = page.locator('button', { hasText: /Back/i });
    if (await backBtn.isVisible({ timeout: 2000 })) {
      await backBtn.click();
      await page.waitForTimeout(1500);
    }

    // --- Create Embedding Provider ---
    await createProvider(page, {
      id: 'azure-embedding',
      name: 'Azure OpenAI Embeddings',
      purpose: 'Embedding',
      providerType: 'AzureOpenAI',
      endpoint: AZURE_ENDPOINT,
      deployment: AZURE_EMBEDDING_DEPLOYMENT,
      apiKey: AZURE_API_KEY,
    });
  });

  test('Step 6: Setup endpoints are permanently disabled after completion', async ({ page }) => {
    await completeSetupAndLogin(page);

    // /api/setup/status should return 403 after setup completion
    const response = await page.evaluate(async () => {
      const res = await fetch('/api/setup/status');
      return { status: res.status, body: await res.json() };
    });
    expect(response.status).toBe(403);
    expect(response.body.error).toBe('setup_complete');

    // Providers should be accessible and contain our configured providers
    const providers = await page.evaluate(async () => {
      const res = await fetch('/api/model-providers');
      return res.json();
    });
    expect(Array.isArray(providers)).toBe(true);
    expect(providers.length).toBeGreaterThanOrEqual(2);
  });

  // ── Helpers ──────────────────────────────────────────────────────

  /** Walk through Welcome → Configure HA → arrive at Step 3 (HA Plugin) */
  async function navigateToStep3(page: Page): Promise<void> {
    await page.goto('/', { waitUntil: 'networkidle' });

    // Step 1: Welcome
    await page.getByRole('button', { name: /Get Started/i }).click();

    // Step 2: Generate key + fill HA details
    await page.locator('button', { hasText: /Generate Dashboard Key/i }).click();
    await page.waitForTimeout(2000);

    // Capture key for later use
    const codeEl = page.locator('code').first();
    const key = (await codeEl.textContent())?.trim() || '';
    if (key.startsWith('lk_')) dashboardKey = key;

    const urlInput = page.locator('input[type="url"], input[placeholder*="homeassistant"]').first();
    await urlInput.clear();
    await urlInput.fill(HA_URL);

    const tokenInput = page.locator('input[type="password"], input[placeholder*="token"]').first();
    await tokenInput.clear();
    await tokenInput.fill(HA_TOKEN);

    const saveBtn = page.locator('button', { hasText: /^Save$/i });
    if (await saveBtn.isVisible({ timeout: 3000 })) {
      await saveBtn.click();
      await page.waitForTimeout(2000);
    }

    await page.locator('button', { hasText: /^Next$/i }).click();
    await page.waitForTimeout(2000);
  }

  /** Run through the entire setup wizard and end up authenticated on the dashboard */
  async function completeSetupAndLogin(page: Page): Promise<void> {
    await navigateToStep3(page);

    await page.locator('button', { hasText: /Generate HA Key/i }).click();
    await page.waitForTimeout(2000);
    await page.locator('button', { hasText: /Skip.*Complete|Complete Setup/i }).click();
    await page.waitForTimeout(3000);

    // Go to Dashboard
    const goDashBtn = page.locator('a, button', { hasText: /Go to Dashboard/i });
    if (await goDashBtn.isVisible({ timeout: 5000 })) {
      await goDashBtn.click();
      await page.waitForTimeout(3000);
    }

    // If we ended up on /login (shouldn't happen with auto-login, but handle it)
    if (page.url().includes('/login')) {
      const apiKeyInput = page.locator('input[type="password"]').first();
      await apiKeyInput.fill(dashboardKey);
      await page.locator('button', { hasText: /Sign In/i }).click();
      await page.waitForTimeout(3000);
    }
  }

  interface ProviderConfig {
    id: string;
    name: string;
    purpose: string;
    providerType: string;
    endpoint: string;
    deployment: string;
    apiKey: string;
  }

  /** Fill and submit the Add Provider form */
  async function createProvider(page: Page, cfg: ProviderConfig): Promise<void> {
    const addBtn = page.locator('button', { hasText: /Add Provider/i });
    await expect(addBtn).toBeVisible();
    await addBtn.click();
    await page.waitForTimeout(1500);

    // Provider ID
    await page.locator('input[placeholder*="gpt4o"]').first().fill(cfg.id);
    // Display Name
    await page.locator('input[placeholder*="GPT-4o"]').first().fill(cfg.name);
    // Purpose
    await page.locator('select').nth(0).selectOption(cfg.purpose);
    await page.waitForTimeout(500);
    // Provider Type
    await page.locator('select').nth(1).selectOption(cfg.providerType);
    await page.waitForTimeout(500);
    // Endpoint
    await page.locator('input[placeholder*="api.openai.com"], input[placeholder*="https://"]').first().fill(cfg.endpoint);
    // Model / Deployment
    await page.locator('input[placeholder*="gpt-4o"], input[placeholder*="text-embedding"]').first().fill(cfg.deployment);
    // Auth Type
    const selects = page.locator('select');
    if (await selects.count() >= 3) {
      await selects.nth(2).selectOption('api-key');
      await page.waitForTimeout(500);
    }
    // API Key
    await page.locator('input[type="password"]').first().fill(cfg.apiKey);

    // Submit
    const createBtn = page.locator('button', { hasText: /Create Provider/i });
    await createBtn.click();
    await page.waitForTimeout(3000);
  }
});
