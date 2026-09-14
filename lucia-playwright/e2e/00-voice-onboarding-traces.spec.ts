import { expect, test } from '@playwright/test';
import type { CommandTrace } from '../../lucia-dashboard/src/types';

test('shows voice onboarding and its returned conversation without an LLM trace', async ({ page }) => {
  const trace: CommandTrace = {
    id: 'onboarding-trace',
    timestamp: '2026-09-13T21:42:27Z',
    rawText: '[Voice onboarding reply]',
    cleanText: '[Voice onboarding reply]',
    normalizedText: '',
    speakerId: null,
    requestContext: {
      conversationId: 'new-client-conversation',
      deviceId: 'satellite-1',
      deviceArea: 'Office',
      deviceType: 'voice_assistant',
      userId: null,
      speakerId: null,
      location: 'Home',
    },
    match: {
      isMatch: false,
      confidence: 0,
      patternId: null,
      skillId: null,
      action: null,
      templateUsed: null,
      capturedValues: null,
      matchDurationMs: 0,
      tokenHighlights: [],
    },
    execution: null,
    llmFallback: null,
    templateRender: null,
    workflow: {
      name: 'voice-onboarding',
      stage: 'Name',
      conversationId: 'voice-onboarding:original-conversation',
      needsInput: true,
    },
    outcome: 'commandHandled',
    totalDurationMs: 12,
    responseText: 'Voice onboarding is waiting for a reply.',
    error: null,
  };

  await page.route('**/api/installer/capabilities', route => route.fulfill({ status: 404 }));
  await page.route('**/api/auth/status', route => route.fulfill({
    json: { authenticated: true, setupComplete: true, hasKeys: true },
  }));
  await page.route('**/api/command-traces/live', route => route.fulfill({
    contentType: 'text/event-stream',
    body: 'data: {"type":"connected"}\n\n',
  }));
  await page.route('**/api/command-traces/stats', route => route.fulfill({
    json: { totalCount: 1, commandHandledCount: 1, llmFallbackCount: 0, errorCount: 0, avgDurationMs: 12, bySkill: {} },
  }));
  await page.route('**/api/command-traces?*', route => route.fulfill({
    json: { items: [trace], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
  }));
  await page.route('**/api/command-traces/onboarding-trace', route => route.fulfill({ json: trace }));

  await page.goto('/command-traces');
  const row = page.getByRole('row').filter({ hasText: '[Voice onboarding reply]' });
  await expect(row.getByText('Voice onboarding', { exact: true })).toBeVisible();
  await row.click();

  await expect(page.getByRole('heading', { name: 'Voice onboarding', exact: true })).toBeVisible();
  await expect(page.getByText('Waiting for a reply', { exact: true })).toBeVisible();
  await expect(page.getByText('voice-onboarding:original-conversation', { exact: true })).toBeVisible();
  await expect(page.getByText('new-client-conversation', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('No pattern match', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'LLM Fallback', exact: true })).toHaveCount(0);
});
