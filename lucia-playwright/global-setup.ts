import { execSync } from 'child_process';
import path from 'path';

const COMPOSE_DIR = path.resolve(import.meta.dirname, '../infra/docker');
const HEALTH_URL = 'http://localhost:7233/health';
const MAX_WAIT_MS = 300_000; // 5 min for build + startup
const POLL_INTERVAL_MS = 3_000;

/**
 * Playwright global setup: builds and starts the docker-compose stack,
 * then waits for the lucia service health check to pass.
 */
export default async function globalSetup() {
  console.log('\n🐳 Building and starting docker-compose stack...');

  // Clean up any leftover state from previous runs
  try {
    execSync('docker compose down -v --remove-orphans', {
      cwd: COMPOSE_DIR,
      stdio: 'inherit',
      timeout: 60_000,
    });
  } catch {
    // Ignore — may not have been running
  }

  // Build and start fresh
  execSync('docker compose up -d --build', {
    cwd: COMPOSE_DIR,
    stdio: 'inherit',
    timeout: 600_000, // 10 min for Docker build
  });

  // Wait for health check — the service reports healthy even before the setup
  // wizard runs (it's in "waiting for config" state which is healthy).
  console.log('⏳ Waiting for lucia service to become healthy...');
  const deadline = Date.now() + MAX_WAIT_MS;

  while (Date.now() < deadline) {
    try {
      const res = await fetch(HEALTH_URL);
      if (res.ok) {
        console.log('✅ Lucia service is healthy!\n');
        return;
      }
      // 503 = infrastructure not ready yet (e.g. MongoDB connecting)
      console.log(`  Health check returned ${res.status}, retrying...`);
    } catch {
      // Server not ready yet (connection refused)
    }
    await new Promise((r) => setTimeout(r, POLL_INTERVAL_MS));
  }

  throw new Error(`Lucia service did not become healthy within ${MAX_WAIT_MS / 1000}s`);
}
