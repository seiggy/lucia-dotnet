/**
 * Lucia Setup Wizard E2E Test
 * 
 * Walks through the full setup wizard and configures AI providers
 * using values from the .env file.
 * 
 * Usage: node lucia-playwright/setup-wizard-test.mjs
 */

import { chromium } from 'playwright';
import { writeFileSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';
import { config } from 'dotenv';

const __dirname = dirname(fileURLToPath(import.meta.url));
config({ path: join(__dirname, '.env') });

const BASE_URL = process.env.BASE_URL || 'http://localhost:7233';
const RESULTS_FILE = join(__dirname, 'wizard-results.json');

const HA_URL = process.env.HA_URL || 'http://homeassistant:8123';
const HA_TOKEN = process.env.HA_TOKEN;
const AZURE_ENDPOINT = process.env.AZURE_ENDPOINT;
const AZURE_API_KEY = process.env.AZURE_API_KEY;
const AZURE_CHAT_DEPLOYMENT = process.env.AZURE_CHAT_DEPLOYMENT || 'chat';
const AZURE_EMBEDDING_DEPLOYMENT = process.env.AZURE_EMBEDDING_DEPLOYMENT || 'text-embedding-3-small';

if (!HA_TOKEN || !AZURE_ENDPOINT || !AZURE_API_KEY) {
  console.error('Missing required env vars. Copy .env.template to .env and fill in values.');
  process.exit(1);
}

const results = {
  timestamp: new Date().toISOString(),
  steps: {},
  keys: {},
  providers: {},
  errors: [],
};

function log(msg) {
  console.log(`[${new Date().toISOString()}] ${msg}`);
}

async function takeScreenshot(page, name) {
  const path = join(__dirname, `screenshot-${name}.png`);
  await page.screenshot({ path, fullPage: true });
  log(`  Screenshot saved: ${path}`);
}

async function run() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1280, height: 900 },
    ignoreHTTPSErrors: true,
  });
  const page = await context.newPage();

  try {
    // ================================================================
    // STEP 0: Navigate to the app — expect redirect to /setup
    // ================================================================
    log('Step 0: Navigating to app...');
    await page.goto(BASE_URL, { waitUntil: 'networkidle', timeout: 30000 });
    await page.waitForTimeout(2000);
    
    const currentUrl = page.url();
    log(`  Current URL: ${currentUrl}`);
    results.steps.navigation = { url: currentUrl, success: currentUrl.includes('/setup') };
    await takeScreenshot(page, '00-initial');

    // ================================================================
    // STEP 1: Welcome — click "Get Started"
    // ================================================================
    log('Step 1: Welcome screen...');
    await page.waitForSelector('text=Get Started', { timeout: 15000 });
    await takeScreenshot(page, '01-welcome');
    
    await page.click('text=Get Started');
    await page.waitForTimeout(1500);
    results.steps.welcome = { success: true };
    log('  ✓ Clicked "Get Started"');

    // ================================================================
    // STEP 2: Configure Lucia & Home Assistant
    // ================================================================
    log('Step 2: Configure Lucia & Home Assistant...');
    await takeScreenshot(page, '02-configure-start');

    // 2a: Generate Dashboard API Key
    log('  Generating Dashboard API Key...');
    const genDashKeyBtn = page.locator('button', { hasText: /Generate Dashboard Key/i });
    await genDashKeyBtn.waitFor({ timeout: 10000 });
    await genDashKeyBtn.click();
    await page.waitForTimeout(2000);

    // Capture the generated dashboard key from the code element
    const dashKeyEl = page.locator('code').first();
    let dashboardKey = '';
    try {
      dashboardKey = await dashKeyEl.textContent({ timeout: 5000 });
      dashboardKey = dashboardKey?.trim() || '';
      log(`  ✓ Dashboard Key generated: ${dashboardKey.substring(0, 20)}...`);
    } catch {
      log('  ⚠ Could not read dashboard key from UI');
    }
    results.keys.dashboardKey = dashboardKey;
    await takeScreenshot(page, '02a-dashboard-key');

    // 2b: Fill Home Assistant URL and Token
    log('  Filling HA connection details...');
    const urlInput = page.locator('input[type="url"], input[placeholder*="homeassistant"]').first();
    await urlInput.waitFor({ timeout: 5000 });
    await urlInput.clear();
    await urlInput.fill(HA_URL);

    const tokenInput = page.locator('input[type="password"], input[placeholder*="token"]').first();
    await tokenInput.waitFor({ timeout: 5000 });
    await tokenInput.clear();
    await tokenInput.fill(HA_TOKEN);
    log(`  ✓ Filled HA URL: ${HA_URL}`);
    
    await takeScreenshot(page, '02b-ha-details');

    // 2c: Save the HA config
    const saveBtn = page.locator('button', { hasText: /^Save$/i });
    if (await saveBtn.isVisible({ timeout: 3000 })) {
      await saveBtn.click();
      await page.waitForTimeout(2000);
      log('  ✓ Saved HA configuration');
    }
    await takeScreenshot(page, '02c-ha-saved');

    // 2d: Try test connection (may fail since HA isn't reachable from Docker)
    log('  Testing HA connection...');
    const testBtn = page.locator('button', { hasText: /Test Connection/i });
    if (await testBtn.isVisible({ timeout: 3000 })) {
      await testBtn.click();
      await page.waitForTimeout(5000);
      log('  ℹ Connection test attempted (may fail - expected)');
    }
    await takeScreenshot(page, '02d-connection-test');
    results.steps.configure = { success: true, haUrl: HA_URL };

    // 2e: Click Next to advance
    const nextBtn = page.locator('button', { hasText: /^Next$/i });
    await nextBtn.waitFor({ timeout: 5000 });
    await nextBtn.click();
    await page.waitForTimeout(2000);
    log('  ✓ Advanced to step 3');

    // ================================================================
    // STEP 3: Connect HA Plugin
    // ================================================================
    log('Step 3: Connect HA Plugin...');
    await takeScreenshot(page, '03-ha-plugin-start');

    // Generate HA Plugin Key
    const genHaKeyBtn = page.locator('button', { hasText: /Generate HA Key/i });
    if (await genHaKeyBtn.isVisible({ timeout: 5000 })) {
      await genHaKeyBtn.click();
      await page.waitForTimeout(2000);

      // Try to capture the HA key
      const codeEls = page.locator('code');
      const codeCount = await codeEls.count();
      let haKey = '';
      for (let i = 0; i < codeCount; i++) {
        const text = await codeEls.nth(i).textContent();
        if (text && text.startsWith('lk_')) {
          haKey = text.trim();
          break;
        }
      }
      if (haKey) {
        log(`  ✓ HA Plugin Key generated: ${haKey.substring(0, 20)}...`);
      } else {
        log('  ⚠ Could not read HA key from UI');
      }
      results.keys.haPluginKey = haKey;
    }
    await takeScreenshot(page, '03a-ha-key-generated');

    // Skip the plugin validation - click "Skip & Complete Later" or "Complete Setup"
    log('  Completing setup (skipping plugin validation)...');
    const skipBtn = page.locator('button', { hasText: /Skip.*Complete|Complete Setup/i });
    if (await skipBtn.isVisible({ timeout: 5000 })) {
      await skipBtn.click();
      await page.waitForTimeout(3000);
      log('  ✓ Clicked complete/skip button');
    } else {
      // Fallback: try the complete API directly (requires auth cookie from auto-login)
      log('  ℹ Skip button not found, trying API fallback...');
      await page.evaluate(async () => {
        await fetch('/api/setup/complete', { method: 'POST' });
      });
      await page.waitForTimeout(2000);
    }
    await takeScreenshot(page, '03b-setup-completing');
    results.steps.haPlugin = { success: true };

    // ================================================================
    // STEP 4: Done — navigate to dashboard
    // ================================================================
    log('Step 4: Done screen...');
    await page.waitForTimeout(2000);
    await takeScreenshot(page, '04-done');

    const goDashBtn = page.locator('a, button', { hasText: /Go to Dashboard/i });
    if (await goDashBtn.isVisible({ timeout: 5000 })) {
      await goDashBtn.click();
      await page.waitForTimeout(3000);
      log('  ✓ Clicked "Go to Dashboard"');
    } else {
      await page.goto(`${BASE_URL}/`, { waitUntil: 'networkidle', timeout: 15000 });
      await page.waitForTimeout(2000);
    }
    await takeScreenshot(page, '04a-after-done');
    results.steps.done = { success: true, finalUrl: page.url() };
    log(`  Post-wizard URL: ${page.url()}`);

    // ================================================================
    // STEP 4b: Login with Dashboard API Key
    // (Security fix: setup wizard auto-logs in after key generation,
    //  so we may already be authenticated here)
    // ================================================================
    log('Step 4b: Logging in with Dashboard API Key...');
    
    // Check if we're already logged in (auto-login after key generation)
    const postWizardUrl = page.url();
    const isOnLogin = postWizardUrl.includes('/login');
    
    if (isOnLogin) {
      log('  On login page — entering credentials...');
      const apiKeyLoginInput = page.locator('input[type="password"]').first();
      await apiKeyLoginInput.waitFor({ timeout: 10000 });
      await apiKeyLoginInput.fill(results.keys.dashboardKey);
      await takeScreenshot(page, '04b-login-filled');
      
      const signInBtn = page.locator('button', { hasText: /Sign In/i });
      await signInBtn.click();
      await page.waitForTimeout(3000);
      await takeScreenshot(page, '04c-logged-in');
      log(`  Post-login URL: ${page.url()}`);
      results.steps.login = { success: !page.url().includes('/login'), url: page.url() };
    } else {
      log('  ✓ Already authenticated (auto-login after key generation)');
      await takeScreenshot(page, '04c-logged-in');
      results.steps.login = { success: true, url: postWizardUrl, autoLogin: true };
    }

    // ================================================================
    // STEP 5: Configure AI Providers
    // ================================================================
    log('Step 5: Configuring AI Providers...');

    // Navigate to model providers page
    await page.goto(`${BASE_URL}/model-providers`, { waitUntil: 'networkidle', timeout: 15000 });
    await page.waitForTimeout(2000);
    await takeScreenshot(page, '05-providers-page');

    // --- 5a: Create Chat Provider (Azure OpenAI) ---
    log('  Creating Chat provider (Azure OpenAI)...');
    const addBtn = page.locator('button', { hasText: /Add Provider/i });
    await addBtn.waitFor({ timeout: 10000 });
    await addBtn.click();
    await page.waitForTimeout(1500);
    await takeScreenshot(page, '05a-add-chat-form');

    // Fill Provider ID
    await page.locator('input[placeholder*="gpt4o"]').first().fill('azure-chat');

    // Fill Display Name
    await page.locator('input[placeholder*="GPT-4o"]').first().fill('Azure OpenAI Chat');

    // Purpose = Chat (already default, but be explicit)
    await page.locator('select').nth(0).selectOption('Chat');
    await page.waitForTimeout(500);

    // Provider Type = Azure OpenAI
    await page.locator('select').nth(1).selectOption('AzureOpenAI');
    await page.waitForTimeout(500);

    // Endpoint URL
    await page.locator('input[placeholder*="api.openai.com"], input[placeholder*="https://"]').first().fill(AZURE_ENDPOINT);

    // Model/Deployment Name
    await page.locator('input[placeholder*="gpt-4o"]').first().fill(AZURE_CHAT_DEPLOYMENT);

    // Auth Type = API Key (already default)
    await page.locator('select').nth(2).selectOption('api-key');
    await page.waitForTimeout(500);

    // API Key
    await page.locator('input[type="password"]').first().fill(AZURE_API_KEY);

    await takeScreenshot(page, '05b-chat-form-filled');

    // Submit
    const createBtn = page.locator('button', { hasText: /Create Provider/i });
    await createBtn.click();
    await page.waitForTimeout(3000);
    await takeScreenshot(page, '05c-chat-created');
    log('  ✓ Chat provider created');
    results.providers.chat = {
      id: 'azure-chat',
      name: 'Azure OpenAI Chat',
      provider: 'AzureOpenAI',
      purpose: 'Chat',
      endpoint: AZURE_ENDPOINT,
      deployment: AZURE_CHAT_DEPLOYMENT,
    };

    // --- 5b: Create Embedding Provider (Azure OpenAI) ---
    log('  Creating Embedding provider (Azure OpenAI)...');
    
    // Navigate back to providers list if needed
    const backBtn = page.locator('button', { hasText: /Back/i });
    if (await backBtn.isVisible({ timeout: 2000 })) {
      await backBtn.click();
      await page.waitForTimeout(1500);
    }

    // Click Add Provider again
    const addBtn2 = page.locator('button', { hasText: /Add Provider/i });
    await addBtn2.waitFor({ timeout: 10000 });
    await addBtn2.click();
    await page.waitForTimeout(1500);
    await takeScreenshot(page, '05d-add-embedding-form');

    // Fill Provider ID
    await page.locator('input[placeholder*="gpt4o"]').first().fill('azure-embedding');

    // Fill Display Name
    await page.locator('input[placeholder*="GPT-4o"]').first().fill('Azure OpenAI Embeddings');

    // Set Purpose to Embedding
    await page.locator('select').nth(0).selectOption('Embedding');
    await page.waitForTimeout(500);

    // Provider Type
    await page.locator('select').nth(1).selectOption('AzureOpenAI');
    await page.waitForTimeout(500);

    // Endpoint
    await page.locator('input[placeholder*="api.openai.com"], input[placeholder*="https://"]').first().fill(AZURE_ENDPOINT);

    // Model/Deployment
    await page.locator('input[placeholder*="gpt-4o"], input[placeholder*="text-embedding"]').first().fill(AZURE_EMBEDDING_DEPLOYMENT);

    // Auth Type
    const allSelects2 = page.locator('select');
    const allSelectCount2 = await allSelects2.count();
    if (allSelectCount2 >= 3) {
      await allSelects2.nth(2).selectOption('api-key');
      await page.waitForTimeout(500);
    }

    // API Key
    await page.locator('input[type="password"]').first().fill(AZURE_API_KEY);

    await takeScreenshot(page, '05e-embedding-form-filled');

    // Submit
    const createBtn2 = page.locator('button', { hasText: /Create Provider/i });
    await createBtn2.click();
    await page.waitForTimeout(3000);
    await takeScreenshot(page, '05f-embedding-created');
    log('  ✓ Embedding provider created');
    results.providers.embedding = {
      id: 'azure-embedding',
      name: 'Azure OpenAI Embeddings',
      provider: 'AzureOpenAI',
      purpose: 'Embedding',
      endpoint: AZURE_ENDPOINT,
      deployment: AZURE_EMBEDDING_DEPLOYMENT,
    };

    // Navigate back to list to verify
    const backBtn2 = page.locator('button', { hasText: /Back/i });
    if (await backBtn2.isVisible({ timeout: 2000 })) {
      await backBtn2.click();
      await page.waitForTimeout(1500);
    }
    await takeScreenshot(page, '05g-providers-list-final');

    // ================================================================
    // STEP 6: Verify setup via API
    // ================================================================
    log('Step 6: Verifying setup...');
    
    const setupStatus = await page.evaluate(async () => {
      const res = await fetch('/api/setup/status');
      return res.json();
    });
    log(`  Setup status: ${JSON.stringify(setupStatus)}`);
    results.steps.verification = { setupStatus };

    const providers = await page.evaluate(async () => {
      const res = await fetch('/api/model-providers');
      return res.json();
    });
    log(`  Providers count: ${Array.isArray(providers) ? providers.length : 'unknown'}`);
    results.steps.verification.providerCount = Array.isArray(providers) ? providers.length : 0;
    results.steps.verification.providers = providers;

    // ================================================================
    // FINAL: Write results
    // ================================================================
    results.success = true;
    log('\n✅ Setup wizard completed successfully!');

  } catch (error) {
    results.success = false;
    results.errors.push({
      message: error.message,
      stack: error.stack?.split('\n').slice(0, 5).join('\n'),
    });
    log(`\n❌ Error: ${error.message}`);
    await takeScreenshot(page, 'error-final').catch(() => {});
  } finally {
    // Write results to file
    writeFileSync(RESULTS_FILE, JSON.stringify(results, null, 2));
    log(`\nResults written to: ${RESULTS_FILE}`);
    
    await browser.close();
  }
}

run().catch(console.error);
