#!/usr/bin/env node
// Captures Playwright screenshots of the Lucia dashboard for README documentation.
// Usage: node scripts/capture-screenshots.mjs [dashboard-url] [api-key]

import { chromium } from 'playwright';
import { fileURLToPath } from 'url';
import { createRequire } from 'module';
import path from 'path';

const DASHBOARD_URL = process.argv[2] || 'http://localhost:35183';
const API_BASE = process.argv[3] || 'http://localhost:5151';
const API_KEY = process.argv[4] || process.env.LUCIA_API_KEY;
const OUTPUT_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../docs/images');

const VIEWPORT = { width: 1280, height: 800 };

async function main() {
  if (!API_KEY) {
    console.error('Usage: node capture-screenshots.mjs [dashboard-url] [api-base] <api-key>');
    process.exit(1);
  }

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: VIEWPORT,
    colorScheme: 'dark',
    ignoreHTTPSErrors: true,
  });
  const page = await context.newPage();

  // Authenticate via API then transfer session cookie to browser context
  console.log('Authenticating...');
  const loginResp = await page.request.post(`${API_BASE}/api/auth/login`, {
    data: { apiKey: API_KEY },
  });
  if (!loginResp.ok()) {
    console.error('Login failed:', await loginResp.text());
    process.exit(1);
  }
  console.log('Authenticated successfully');

  // Screenshot helper
  async function screenshot(name, url, opts = {}) {
    const { waitFor, action, delay } = opts;
    console.log(`Capturing: ${name}...`);
    await page.goto(`${DASHBOARD_URL}${url}`, { waitUntil: 'networkidle', timeout: 15000 }).catch(() => {});
    if (waitFor) await page.waitForSelector(waitFor, { timeout: 10000 }).catch(() => {});
    if (delay) await page.waitForTimeout(delay);
    if (action) await action(page);
    await page.screenshot({
      path: `${OUTPUT_DIR}/${name}.png`,
      fullPage: false,
    });
    console.log(`  ✓ Saved ${name}.png`);
  }

  // --- Login page (need to logout first) ---
  console.log('\n--- Login Page ---');
  await page.request.post(`${API_BASE}/api/auth/logout`).catch(() => {});
  await screenshot('login', '/login', {
    waitFor: 'input',
    delay: 500,
  });

  // Re-authenticate for dashboard pages
  await page.request.post(`${API_BASE}/api/auth/login`, {
    data: { apiKey: API_KEY },
  });

  // --- Dashboard pages ---
  console.log('\n--- Dashboard Pages ---');

  await screenshot('traces', '/', {
    waitFor: '[class*="trace"], table, [class*="empty"]',
    delay: 1000,
  });

  await screenshot('agents', '/agent-dashboard', {
    waitFor: '[class*="agent"], table, [class*="card"]',
    delay: 1000,
  });

  await screenshot('configuration', '/configuration', {
    waitFor: 'form, [class*="config"], [class*="setting"]',
    delay: 1000,
  });

  await screenshot('exports', '/exports', {
    waitFor: '[class*="export"], form, button',
    delay: 1000,
  });

  await screenshot('prompt-cache', '/prompt-cache', {
    waitFor: '[class*="cache"], [class*="stat"], table',
    delay: 1000,
  });

  await screenshot('tasks', '/tasks', {
    waitFor: '[class*="task"], table, [class*="empty"]',
    delay: 1000,
  });

  // --- Setup wizard (reset setup, capture all steps, then restore) ---
  console.log('\n--- Setup Wizard ---');
  // We need to intercept the auth/status API to fake setupComplete=false
  await page.request.post(`${API_BASE}/api/auth/logout`).catch(() => {});

  // Use route interception to fake setup-not-complete
  await page.route('**/api/auth/status', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ authenticated: false, setupComplete: false, hasKeys: false }),
    });
  });
  await page.route('**/api/setup/status', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        hasDashboardKey: false,
        hasHaConnection: false,
        haUrl: '',
        pluginValidated: false,
        setupComplete: false,
      }),
    });
  });

  // Step 1: Welcome
  await screenshot('setup-welcome', '/setup', {
    waitFor: 'button',
    delay: 800,
  });

  // Step 2: Configure - intercept to show the form
  await page.unroute('**/api/setup/status');
  await page.route('**/api/setup/status', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        hasDashboardKey: false,
        hasHaConnection: false,
        haUrl: '',
        pluginValidated: false,
        setupComplete: false,
      }),
    });
  });

  // Click "Get Started" or "Next" to move to step 2
  const nextBtn = await page.$('button:has-text("Get Started"), button:has-text("Next"), button:has-text("Begin")');
  if (nextBtn) {
    await nextBtn.click();
    await page.waitForTimeout(800);
  }
  await page.screenshot({
    path: `${OUTPUT_DIR}/setup-configure.png`,
    fullPage: false,
  });
  console.log('  ✓ Saved setup-configure.png');

  // Clean up route interceptions
  await page.unroute('**/api/auth/status');
  await page.unroute('**/api/setup/status');

  await browser.close();
  console.log(`\nDone! Screenshots saved to ${OUTPUT_DIR}/`);
}

main().catch((err) => {
  console.error('Error:', err);
  process.exit(1);
});
