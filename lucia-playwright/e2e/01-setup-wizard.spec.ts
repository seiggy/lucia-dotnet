import { test, expect, type Page } from '@playwright/test';
import fs from 'fs';
import path from 'path';

/**
 * Shared state file written by this test suite so that downstream specs
 * (e.g. prompt-cache) can read the dashboard key generated during onboarding.
 */
const WIZARD_STATE_PATH = path.resolve(import.meta.dirname, '../.test-state/wizard-state.json');

function persistWizardState(key: string) {
  const dir = path.dirname(WIZARD_STATE_PATH);
  if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(WIZARD_STATE_PATH, JSON.stringify({ dashboardKey: key }));
}

function loadPersistedKey(): string {
  try {
    const data = JSON.parse(fs.readFileSync(WIZARD_STATE_PATH, 'utf-8'));
    return data.dashboardKey || '';
  } catch {
    return '';
  }
}

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
    persistWizardState(dashboardKey);

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

    // 2e: Advance to AI Providers step
    const nextBtn = page.locator('button', { hasText: /^Next$/i });
    await expect(nextBtn).toBeVisible();
    await nextBtn.click();
    await page.waitForTimeout(2000);

    // Should be on AI Provider step
    await expect(page.locator('text=Configure AI Provider')).toBeVisible();
  });

  test('Step 3: Configure AI providers (Chat + Embedding)', async ({ page }) => {
    await navigateToAiProviders(page);

    // --- Create Chat Provider ---
    await addProviderInWizard(page, {
      purpose: 'Chat',
      providerType: 'AzureOpenAI',
      modelName: AZURE_CHAT_DEPLOYMENT,
      endpoint: AZURE_ENDPOINT,
      apiKey: AZURE_API_KEY,
    });

    // Provider should appear in the list
    await expect(page.locator('text=AzureOpenAI')).toBeVisible();

    // Test the chat provider
    const testBtn = page.locator('button[title="Test"]').first();
    if (await testBtn.isVisible({ timeout: 3000 })) {
      await testBtn.click();
      await page.waitForTimeout(5000);
      await expect(page.locator('text=✓ OK').first()).toBeVisible({ timeout: 10_000 });
    }

    // --- Create Embedding Provider ---
    await addProviderInWizard(page, {
      purpose: 'Embedding',
      providerType: 'AzureOpenAI',
      modelName: AZURE_EMBEDDING_DEPLOYMENT,
      endpoint: AZURE_ENDPOINT,
      apiKey: AZURE_API_KEY,
    });

    // Both providers should be listed
    const providerCards = page.locator('text=AzureOpenAI');
    await expect(providerCards).toHaveCount(2, { timeout: 5000 });

    // Next button should now be enabled (we have a Chat provider)
    const nextBtn = page.locator('button', { hasText: /^Next$/i });
    await expect(nextBtn).toBeEnabled();
    await nextBtn.click();
    await page.waitForTimeout(2000);

    // Should be on Agent Status step
    await expect(page.locator('text=Starting Agents')).toBeVisible();
  });

  test('Step 4: Wait for agents and generate HA Plugin key', async ({ page }) => {
    await navigateToAgentStatus(page);

    // Wait for agents to initialize (poll up to 90s)
    const agentsReady = page.locator('text=/agent.*initialized and ready/i');
    const skipLink = page.locator('text=Skip');

    // Either agents come online, or we skip after timeout
    const isReady = await agentsReady.isVisible({ timeout: 90_000 }).catch(() => false);
    if (isReady) {
      // Advance to HA Plugin step
      const nextBtn = page.locator('button', { hasText: /^Next$/i });
      await expect(nextBtn).toBeEnabled();
      await nextBtn.click();
    } else {
      // Skip — agents didn't start (HA may not be reachable in CI)
      await skipLink.click();
    }
    await page.waitForTimeout(2000);

    // Should be on HA Plugin step
    await expect(page.locator('button', { hasText: /Generate HA Key/i })).toBeVisible();

    // Generate HA Plugin Key
    const genHaKeyBtn = page.locator('button', { hasText: /Generate HA Key/i });
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

    // Should either show "Done" screen or auto-redirect to dashboard
    const goDashBtn = page.locator('a, button', { hasText: /Go to Dashboard/i });
    const isDone = await goDashBtn.isVisible({ timeout: 3000 }).catch(() => false);
    if (!isDone) {
      expect(page.url()).not.toContain('/setup');
    }

    persistWizardState(dashboardKey);
  });

  test('Step 5: Authenticate and land on the dashboard', async ({ page }) => {
    // Setup was completed in the previous test. Authenticate via API
    // (each test gets a fresh browser context with no cookies)
    expect(dashboardKey).toBeTruthy();
    await page.request.post('/api/auth/login', {
      data: { apiKey: dashboardKey },
    });

    // Navigate to root — should land on dashboard, not /login or /setup
    await page.goto('/');
    await page.waitForTimeout(5000);

    const url = page.url();
    expect(url).not.toContain('/login');
    expect(url).not.toContain('/setup');
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

  /** Walk through Welcome → Configure HA → arrive at AI Providers step */
  async function navigateToAiProviders(page: Page): Promise<void> {
    if (dashboardKey) {
      await page.request.post('/api/auth/login', {
        data: { apiKey: dashboardKey },
      });
    }

    await page.goto('/setup', { waitUntil: 'networkidle' });

    // Step 1: Welcome — may be skipped if already past it
    const getStartedBtn = page.getByRole('button', { name: /Get Started/i });
    if (await getStartedBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await getStartedBtn.click();
      await page.waitForTimeout(1000);
    }

    // Step 2: Configure page — key may already exist from a prior test
    const genKeyBtn = page.locator('button', { hasText: /Generate Dashboard Key/i });
    const keyExistsMsg = page.locator('text=Dashboard key was already generated');

    if (await genKeyBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await genKeyBtn.click();
      await page.waitForTimeout(2000);

      const codeEl = page.locator('code').first();
      if (await codeEl.isVisible({ timeout: 3000 }).catch(() => false)) {
        const key = (await codeEl.textContent())?.trim() || '';
        if (key.startsWith('lk_')) {
          dashboardKey = key;
          persistWizardState(dashboardKey);
        }
      }
    } else if (await keyExistsMsg.isVisible({ timeout: 3000 }).catch(() => false)) {
      // Key already generated — login via the resume form
      const key = dashboardKey || loadPersistedKey();
      if (!key) throw new Error('Dashboard key already exists but no key saved — cannot resume');
      const resumeInput = page.locator('input[type="password"][placeholder*="lk_"]');
      await resumeInput.fill(key);
      await page.locator('button', { hasText: /^Login$/i }).click();
      await page.waitForTimeout(2000);
    }

    // Fill HA details (idempotent — overwriting is fine)
    const urlInput = page.locator('input[type="url"], input[placeholder*="homeassistant"]').first();
    if (await urlInput.isVisible({ timeout: 3000 }).catch(() => false)) {
      await urlInput.clear();
      await urlInput.fill(HA_URL);

      const tokenInput = page.locator('input[type="password"], input[placeholder*="token"]').first();
      await tokenInput.clear();
      await tokenInput.fill(HA_TOKEN);

      const saveBtn = page.locator('button', { hasText: /^Save$/i });
      if (await saveBtn.isEnabled({ timeout: 3000 }).catch(() => false)) {
        await saveBtn.click();
        await page.waitForTimeout(2000);
      }
    }

    // Advance to AI Providers
    const nextBtn = page.locator('button', { hasText: /^Next$/i });
    await expect(nextBtn).toBeVisible({ timeout: 5000 });
    await nextBtn.click();
    await page.waitForTimeout(2000);
  }

  /** Navigate through to the Agent Status step */
  async function navigateToAgentStatus(page: Page): Promise<void> {
    await navigateToAiProviders(page);

    // If we're on AI Provider step, check if a provider already exists
    const addProviderBtn = page.locator('button', { hasText: /Add AI Provider/i });
    const existingProvider = page.locator('text=AzureOpenAI');
    const skipLink = page.locator('text=Skip for now');

    if (await existingProvider.isVisible({ timeout: 3000 }).catch(() => false)) {
      // Provider already created — click Next
      const nextBtn = page.locator('button', { hasText: /^Next$/i });
      await nextBtn.click();
    } else if (await skipLink.isVisible({ timeout: 2000 }).catch(() => false)) {
      // No provider — skip for now
      await skipLink.click();
    }
    await page.waitForTimeout(2000);
  }

  /** Authenticate and navigate to the dashboard (setup is already complete) */
  async function completeSetupAndLogin(page: Page): Promise<void> {
    expect(dashboardKey).toBeTruthy();
    await page.request.post('/api/auth/login', {
      data: { apiKey: dashboardKey },
    });
    await page.goto('/');
    await page.waitForTimeout(5000);
  }

  interface WizardProviderConfig {
    purpose: string;
    providerType: string;
    modelName: string;
    endpoint: string;
    apiKey: string;
  }

  /** Fill and submit the Add Provider form inside the wizard's AI Provider step */
  async function addProviderInWizard(page: Page, cfg: WizardProviderConfig): Promise<void> {
    const addBtn = page.locator('button', { hasText: /Add AI Provider/i });
    await expect(addBtn).toBeVisible();
    await addBtn.click();
    await page.waitForTimeout(1000);

    // The wizard form has selects in order: Purpose, Provider Type
    const selects = page.locator('select');

    // Purpose
    await selects.nth(0).selectOption(cfg.purpose);
    await page.waitForTimeout(300);

    // Provider Type
    await selects.nth(1).selectOption(cfg.providerType);
    await page.waitForTimeout(300);

    // Deployment/Model Name — the text input after the selects
    const textInputs = page.locator(`input[type="text"]`);
    const modelInput = textInputs.first();
    await modelInput.fill(cfg.modelName);

    // Endpoint — second text input
    const endpointInput = textInputs.nth(1);
    await endpointInput.fill(cfg.endpoint);

    // API Key
    const apiKeyInput = page.locator('input[type="password"]').first();
    await apiKeyInput.fill(cfg.apiKey);

    // Save
    const saveBtn = page.locator('button', { hasText: /Save Provider/i });
    await expect(saveBtn).toBeEnabled();
    await saveBtn.click();
    await page.waitForTimeout(3000);
  }

  interface ProviderPageConfig {
    id: string;
    name: string;
    purpose: string;
    providerType: string;
    endpoint: string;
    deployment: string;
    apiKey: string;
  }

  /** Fill and submit the Add Provider form on the /model-providers page */
  async function createProviderOnPage(page: Page, cfg: ProviderPageConfig): Promise<void> {
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
