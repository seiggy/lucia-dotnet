import { defineConfig, devices } from '@playwright/test';
import dotenv from 'dotenv';
import path from 'path';

dotenv.config({ path: path.resolve(import.meta.dirname, '.env') });

const BASE_URL = process.env.BASE_URL || 'http://localhost:7233';

/**
 * Integration test config for the lucia agent host.
 *
 * Lifecycle (when SKIP_DOCKER is not set):
 *   globalSetup  → docker compose up -d --build + wait for /health
 *   tests        → setup-wizard first (onboarding), then prompt-cache
 *   globalTeardown → docker compose down -v
 *
 * Set SKIP_DOCKER=1 to skip the docker lifecycle (e.g. when the stack
 * is already running via Aspire or a manual docker-compose up).
 */
export default defineConfig({
  testDir: './e2e',

  /* Docker lifecycle — skip with SKIP_DOCKER=1 */
  globalSetup: process.env.SKIP_DOCKER ? undefined : './global-setup.ts',
  globalTeardown: process.env.SKIP_DOCKER ? undefined : './global-teardown.ts',

  /* Tests run serially — setup-wizard must complete before prompt-cache */
  fullyParallel: false,
  workers: 1,

  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: 'html',
  timeout: 120_000,

  use: {
    baseURL: BASE_URL,
    ignoreHTTPSErrors: true,
    screenshot: 'on',
    trace: 'on-first-retry',
  },

  /* Single browser — integration tests don't need cross-browser coverage */
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
