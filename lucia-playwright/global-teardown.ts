import { execSync } from 'child_process';
import path from 'path';

const COMPOSE_DIR = path.resolve(import.meta.dirname, '../infra/docker');

/**
 * Playwright global teardown: stops the docker-compose stack and
 * removes volumes so the next run starts completely clean.
 */
export default async function globalTeardown() {
  console.log('\n🧹 Tearing down docker-compose stack and removing volumes...');
  try {
    execSync('docker compose down -v --remove-orphans', {
      cwd: COMPOSE_DIR,
      stdio: 'inherit',
      timeout: 60_000,
    });
    console.log('✅ Docker-compose stack torn down and volumes removed.\n');
  } catch (err) {
    console.warn('⚠️  Failed to tear down docker-compose stack:', err);
  }
}
