import { test, expect, type APIRequestContext } from '@playwright/test';
import dotenv from 'dotenv';
import path from 'path';

/**
 * Domain Settings Persistence — Bug #71 Regression Tests
 *
 * Validates that:
 *   1. Skill config domain edits survive an agent definition update (frontend fix)
 *   2. System flags (IsBuiltIn, IsRemote, IsOrchestrator) are preserved after
 *      updating an agent definition (backend fix)
 *
 * Bug context:
 *   - SkillConfigEditor maintained local React state; "Update Agent" only saved
 *     the main definition — domain edits were silently lost.
 *   - UpdateDefinitionAsync replaced the full document without preserving
 *     IsBuiltIn, IsRemote, and IsOrchestrator flags.
 *
 * Prerequisites:
 *   - Aspire AppHost is running (or docker stack via global-setup)
 *   - LUCIA_DASHBOARD_API_KEY is set in the repo root .env file
 *
 * Run with:
 *   SKIP_DOCKER=1 npx playwright test 05-domain-settings-persist
 */

dotenv.config({ path: path.resolve(import.meta.dirname, '../../.env') });
dotenv.config({ path: path.resolve(import.meta.dirname, '../.env') });

const BASE_URL = process.env.BASE_URL ?? 'http://127.0.0.1:7233';

// Unique domain name used only by this test — avoids collisions with real config
const TEST_DOMAIN = '_e2e_test_domain_';

interface AgentDefinition {
  id: string;
  name: string;
  displayName?: string;
  isBuiltIn: boolean;
  isRemote: boolean;
  isOrchestrator: boolean;
  enabled: boolean;
}

interface SkillConfigSection {
  sectionName: string;
  displayName: string;
  schema: { name: string; type: string; defaultValue: unknown }[];
  values: Record<string, unknown>;
}

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

/**
 * Find a built-in agent that has skill config with a string[] domain field.
 * Returns the agent definition and its skill config sections.
 */
async function findAgentWithDomainConfig(request: APIRequestContext): Promise<{
  agent: AgentDefinition;
  sections: SkillConfigSection[];
  domainFieldSection: string;
  domainFieldName: string;
}> {
  const defsRes = await request.get(`${BASE_URL}/api/agent-definitions`);
  expect(defsRes.ok()).toBeTruthy();
  const definitions: AgentDefinition[] = await defsRes.json();

  // Look for a built-in agent with skill config sections containing a domain field
  for (const agent of definitions.filter(d => d.isBuiltIn)) {
    const configRes = await request.get(
      `${BASE_URL}/api/agent-definitions/${agent.name}/skill-config`
    );
    if (!configRes.ok()) continue;

    const sections: SkillConfigSection[] = await configRes.json();
    for (const section of sections) {
      const domainProp = section.schema.find(
        p => p.type === 'string[]' && /domain/i.test(p.name)
      );
      if (domainProp) {
        return {
          agent,
          sections,
          domainFieldSection: section.sectionName,
          domainFieldName: domainProp.name,
        };
      }
    }
  }

  throw new Error('No built-in agent with a domain skill config field found');
}

// ── Tests ────────────────────────────────────────────────────────────────

test.describe.serial('Domain Settings Persistence (Bug #71)', () => {
  // Shared state across serial steps
  let targetAgent: AgentDefinition;
  let domainSection: string;
  let domainField: string;
  let originalDomains: string[];

  // ── Step 1: Discover a built-in agent with domain config ────────────

  test('find built-in agent with domain skill config', async ({ request }) => {
    await login(request);

    const { agent, sections, domainFieldSection, domainFieldName } =
      await findAgentWithDomainConfig(request);

    targetAgent = agent;
    domainSection = domainFieldSection;
    domainField = domainFieldName;

    // Record original domains so we can restore them later
    const section = sections.find(s => s.sectionName === domainSection)!;
    const current = section.values[domainField];
    originalDomains = Array.isArray(current) ? (current as string[]) : [];

    expect(targetAgent.isBuiltIn).toBe(true);
    expect(domainSection).toBeTruthy();
    expect(domainField).toBeTruthy();
  });

  // ── Step 2: Record pre-update system flags via API ──────────────────

  test('record system flags before update', async ({ request }) => {
    await login(request);

    const res = await request.get(`${BASE_URL}/api/agent-definitions`);
    expect(res.ok()).toBeTruthy();
    const definitions: AgentDefinition[] = await res.json();

    const agent = definitions.find(d => d.id === targetAgent.id);
    expect(agent, `Agent ${targetAgent.id} not found`).toBeTruthy();
    expect(agent!.isBuiltIn).toBe(true);

    // Snapshot flags — these MUST survive the update
    targetAgent = agent!;
  });

  // ── Step 3: Add a test domain via API-level skill config update ─────

  test('add test domain via skill config API', async ({ request }) => {
    await login(request);

    // Read current domains
    const configRes = await request.get(
      `${BASE_URL}/api/agent-definitions/${targetAgent.name}/skill-config`
    );
    expect(configRes.ok()).toBeTruthy();
    const sections: SkillConfigSection[] = await configRes.json();
    const section = sections.find(s => s.sectionName === domainSection)!;
    const currentDomains = Array.isArray(section.values[domainField])
      ? (section.values[domainField] as string[])
      : [];

    // Add the test domain if not already present
    const newDomains = currentDomains.includes(TEST_DOMAIN)
      ? currentDomains
      : [...currentDomains, TEST_DOMAIN];

    const updateRes = await request.put(
      `${BASE_URL}/api/agent-definitions/${targetAgent.name}/skill-config/${domainSection}`,
      {
        data: { ...section.values, [domainField]: newDomains },
      }
    );
    expect(updateRes.ok(), `Skill config update failed: ${updateRes.status()}`).toBeTruthy();
  });

  // ── Step 4: Update the agent definition (triggers the backend bug) ──

  test('update agent definition preserves skill config and system flags', async ({ request }) => {
    await login(request);

    // Read the full existing definition so the PUT does not clobber unrelated fields.
    const getRes = await request.get(`${BASE_URL}/api/agent-definitions/${targetAgent.id}`);
    expect(getRes.ok(), `Agent fetch failed: ${getRes.status()}`).toBeTruthy();
    const existingDefinition: unknown = await getRes.json();

    // Perform a PUT on the agent definition, sending back the full definition
    const updateRes = await request.put(
      `${BASE_URL}/api/agent-definitions/${targetAgent.id}`,
      { data: existingDefinition }
    );
    expect(updateRes.ok(), `Agent update failed: ${updateRes.status()}`).toBeTruthy();

    const updated: AgentDefinition = await updateRes.json();

    // Backend fix: system flags must be preserved
    expect(updated.isBuiltIn, 'isBuiltIn was clobbered by update').toBe(targetAgent.isBuiltIn);
    expect(updated.isRemote, 'isRemote was clobbered by update').toBe(targetAgent.isRemote);
    expect(updated.isOrchestrator, 'isOrchestrator was clobbered by update').toBe(
      targetAgent.isOrchestrator
    );
  });

  // ── Step 5: Verify domain persisted after definition update ─────────

  test('domain persists after agent definition update', async ({ request }) => {
    await login(request);

    const configRes = await request.get(
      `${BASE_URL}/api/agent-definitions/${targetAgent.name}/skill-config`
    );
    expect(configRes.ok()).toBeTruthy();
    const sections: SkillConfigSection[] = await configRes.json();
    const section = sections.find(s => s.sectionName === domainSection)!;
    const domains = section.values[domainField] as string[];

    expect(
      domains,
      `Expected test domain "${TEST_DOMAIN}" to persist in skill config after agent update`
    ).toContain(TEST_DOMAIN);
  });

  // ── Step 6: UI round-trip — edit agent, navigate away, verify ───────

  test('UI: navigate to definitions, open agent, verify domain visible', async ({
    page,
    request,
  }, testInfo) => {
    await login(request);
    const cookies = await request.storageState();
    await page.context().addCookies(cookies.cookies);

    // Navigate to Agent Definitions page
    await page.goto(`${BASE_URL}/agent-definitions`);
    await page.waitForLoadState('domcontentloaded');

    // Wait for agent cards to render
    const agentCard = page.locator('div', {
      hasText: targetAgent.displayName || targetAgent.name,
    }).filter({ has: page.locator('button', { hasText: 'Edit' }) });
    await agentCard.first().waitFor({ timeout: 15_000 });

    await page.screenshot({ path: testInfo.outputPath('05-definitions-list.png') });

    // Verify system badge is shown for built-in agent
    if (targetAgent.isBuiltIn) {
      const badge = agentCard.first().locator('text=System');
      await expect(badge).toBeVisible({ timeout: 5_000 });
    }

    // Click Edit to open the agent form
    await agentCard.first().locator('button', { hasText: 'Edit' }).click();

    // Wait for the form to load, including skill config
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    await page.screenshot({ path: testInfo.outputPath('05-agent-edit-form.png') });

    // Verify the Skill Configuration section is visible
    const skillConfigHeading = page.locator('text=Skill Configuration');
    await expect(skillConfigHeading).toBeVisible({ timeout: 10_000 });

    // Verify the test domain tag is visible in the skill config editor
    const domainTag = page.locator('span', { hasText: TEST_DOMAIN }).first();
    await expect(
      domainTag,
      `Expected test domain "${TEST_DOMAIN}" tag to be visible in skill config editor`
    ).toBeVisible({ timeout: 5_000 });

    await page.screenshot({ path: testInfo.outputPath('05-domain-visible.png') });
  });

  // ── Step 7: UI: update agent via form, navigate away, come back ─────

  test('UI: update agent and verify domain survives navigation', async ({ page, request }, testInfo) => {
    await login(request);
    const cookies = await request.storageState();
    await page.context().addCookies(cookies.cookies);

    // Go to definitions and click Edit on the target agent
    await page.goto(`${BASE_URL}/agent-definitions`);
    await page.waitForLoadState('domcontentloaded');

    const agentCard = page.locator('div', {
      hasText: targetAgent.displayName || targetAgent.name,
    }).filter({ has: page.locator('button', { hasText: 'Edit' }) });
    await agentCard.first().waitFor({ timeout: 15_000 });
    await agentCard.first().locator('button', { hasText: 'Edit' }).click();

    // Wait for skill config to load
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Confirm domain is visible before clicking Update
    const domainTag = page.locator('span', { hasText: TEST_DOMAIN }).first();
    await expect(domainTag).toBeVisible({ timeout: 5_000 });

    // Click "Update Agent" button
    const updateButton = page.getByRole('button', { name: /Update Agent/i });
    await expect(updateButton).toBeVisible();
    await updateButton.click();

    // Wait for the save to complete and return to the list view
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    await page.screenshot({ path: testInfo.outputPath('05-after-update.png') });

    // Navigate away to a different page
    await page.goto(`${BASE_URL}/`);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(1000);

    // Navigate back to definitions
    await page.goto(`${BASE_URL}/agent-definitions`);
    await page.waitForLoadState('domcontentloaded');

    // Re-open the same agent
    const agentCardAfter = page.locator('div', {
      hasText: targetAgent.displayName || targetAgent.name,
    }).filter({ has: page.locator('button', { hasText: 'Edit' }) });
    await agentCardAfter.first().waitFor({ timeout: 15_000 });

    // Verify system badge still shows (flags preserved)
    if (targetAgent.isBuiltIn) {
      const badge = agentCardAfter.first().locator('text=System');
      await expect(badge, 'System badge should still be visible after update').toBeVisible({
        timeout: 5_000,
      });
    }

    await agentCardAfter.first().locator('button', { hasText: 'Edit' }).click();

    // Wait for skill config to load
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // The test domain should still be present after the full round-trip
    const domainAfter = page.locator('span', { hasText: TEST_DOMAIN }).first();
    await expect(
      domainAfter,
      `Domain "${TEST_DOMAIN}" should persist after Update Agent + navigation round-trip`
    ).toBeVisible({ timeout: 10_000 });

    await page.screenshot({ path: testInfo.outputPath('05-domain-persisted-after-roundtrip.png') });
  });

  // ── Step 8: Verify system flags preserved via API after UI update ───

  test('system flags preserved after full UI update round-trip', async ({ request }) => {
    await login(request);

    const res = await request.get(`${BASE_URL}/api/agent-definitions`);
    expect(res.ok()).toBeTruthy();
    const definitions: AgentDefinition[] = await res.json();

    const agent = definitions.find(d => d.id === targetAgent.id);
    expect(agent, `Agent ${targetAgent.id} should still exist`).toBeTruthy();

    expect(agent!.isBuiltIn, 'isBuiltIn flag lost after UI update').toBe(targetAgent.isBuiltIn);
    expect(agent!.isRemote, 'isRemote flag lost after UI update').toBe(targetAgent.isRemote);
    expect(agent!.isOrchestrator, 'isOrchestrator flag lost after UI update').toBe(
      targetAgent.isOrchestrator
    );
  });

  // ── Cleanup: remove the test domain ─────────────────────────────────

  test('cleanup: remove test domain from skill config', async ({ request }) => {
    await login(request);

    const configRes = await request.get(
      `${BASE_URL}/api/agent-definitions/${targetAgent.name}/skill-config`
    );
    if (!configRes.ok()) return; // Defensive — don't fail cleanup

    const sections: SkillConfigSection[] = await configRes.json();
    const section = sections.find(s => s.sectionName === domainSection);
    if (!section) return;

    const currentDomains = Array.isArray(section.values[domainField])
      ? (section.values[domainField] as string[])
      : [];
    const cleaned = currentDomains.filter(d => d !== TEST_DOMAIN);

    // Only update if we actually removed something
    if (cleaned.length < currentDomains.length) {
      const res = await request.put(
        `${BASE_URL}/api/agent-definitions/${targetAgent.name}/skill-config/${domainSection}`,
        {
          data: { ...section.values, [domainField]: cleaned },
        }
      );
      expect(res.ok(), 'Cleanup: skill config update failed').toBeTruthy();
    }

    // Verify cleanup succeeded
    const verifyRes = await request.get(
      `${BASE_URL}/api/agent-definitions/${targetAgent.name}/skill-config`
    );
    if (verifyRes.ok()) {
      const verifySections: SkillConfigSection[] = await verifyRes.json();
      const verifySection = verifySections.find(s => s.sectionName === domainSection);
      if (verifySection) {
        const domains = verifySection.values[domainField] as string[];
        expect(domains).not.toContain(TEST_DOMAIN);
      }
    }
  });
});
