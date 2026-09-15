import { expect, test, type Page } from '@playwright/test';
import type { UserMemoryEntry } from '../../lucia-dashboard/src/api';

async function mockMemories(page: Page, failFirstSave = false) {
  const expiresAt = new Date(Date.now() + 3_600_000).toISOString();
  const createdAt = '2026-09-13T20:00:00Z';
  const entries: Record<string, UserMemoryEntry[]> = {
    'profile-alex': [{ key: 'drink', value: 'Coffee', createdAt, expiresAt: null }],
    'profile-sam': [
      { key: 'preferred_name', value: 'Sam', createdAt, expiresAt: null },
      { key: 'preferences', value: 'Keep replies short and use warm lighting in the evening.', createdAt, expiresAt },
    ],
  };
  const writes: Array<{ profile: string; key: string; body?: unknown; method: string }> = [];
  let shouldFail = failFirstSave;
  await page.route('**/api/auth/status', route => route.fulfill({
    json: { authenticated: true, setupComplete: true, hasKeys: true },
  }));
  await page.route('**/api/appliance/capabilities', route => route.fulfill({ status: 404 }));
  await page.route('**/api/speakers', route => route.fulfill({
    json: [
      { id: 'profile-alex', name: 'Alex', isProvisional: false, isAuthorized: true, interactionCount: 4, enrolledAt: createdAt, lastSeenAt: createdAt },
      { id: 'profile-sam', name: 'Sam', isProvisional: false, isAuthorized: true, interactionCount: 2, enrolledAt: createdAt, lastSeenAt: createdAt },
      { id: 'unknown-1', name: 'Unknown speaker', isProvisional: true, isAuthorized: false, interactionCount: 1, enrolledAt: createdAt, lastSeenAt: createdAt },
    ],
  }));
  await page.route('**/api/memory/**', async route => {
    const url = new URL(route.request().url());
    const parts = url.pathname.split('/');
    const profile = decodeURIComponent(parts[3]);
    const key = parts[4] ? decodeURIComponent(parts[4]) : '';
    const method = route.request().method();
    expect(profile in entries).toBe(true);
    if (method === 'GET') {
      expect(url.searchParams.get('personalOnly')).toBe('true');
      const query = (url.searchParams.get('query') ?? '').toLowerCase();
      await route.fulfill({ json: entries[profile].filter(entry => `${entry.key} ${entry.value}`.toLowerCase().includes(query)) });
      return;
    }
    if (method === 'PUT') {
      const body: { value: string; expiresAt: string | null } = route.request().postDataJSON();
      writes.push({ profile, key, body, method });
      if (shouldFail) {
        shouldFail = false;
        await route.fulfill({ status: 503, json: { detail: 'Memory storage is unavailable.' } });
        return;
      }
      const entry = entries[profile].find(candidate => candidate.key === key);
      expect(entry).toBeDefined();
      if (!entry) throw new Error('Missing fixture entry');
      Object.assign(entry, { value: body.value, expiresAt: body.expiresAt });
      await route.fulfill({ json: entry });
      return;
    }
    expect(method).toBe('DELETE');
    writes.push({ profile, key, method });
    entries[profile] = entries[profile].filter(entry => entry.key !== key);
    await route.fulfill({ json: { deleted: true } });
  });
  return { entries, writes, expiresAt };
}

test('edits and deletes only the selected enrolled users memory', async ({ page }) => {
  const fixture = await mockMemories(page);
  await page.goto('/user-memories?profile=profile-sam');
  await expect(page.getByRole('heading', { name: 'User memories', exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: /Unknown speaker/ })).toHaveCount(0);
  await expect(page.getByText('Keep replies short and use warm lighting in the evening.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Edit Preferences', exact: true }).click();
  await page.getByLabel('Edit Preferences', { exact: true }).fill('Keep replies concise.');
  await expect(page.getByLabel('Enrolled user')).toBeDisabled();
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Memory saved for Sam.');
  expect(fixture.writes[0]).toEqual({
    profile: 'profile-sam', key: 'preferences', method: 'PUT',
    body: { value: 'Keep replies concise.', expiresAt: fixture.expiresAt },
  });
  expect(fixture.entries['profile-alex'][0].value).toBe('Coffee');

  await page.getByLabel('Enrolled user').selectOption('profile-alex');
  await expect(page.getByText('Coffee', { exact: true })).toBeVisible();
  await expect(page.getByText('Keep replies concise.', { exact: true })).toHaveCount(0);
  page.once('dialog', async dialog => {
    expect(dialog.message()).toContain('for Alex');
    await dialog.accept();
  });
  await page.getByRole('button', { name: 'Delete drink', exact: true }).click();
  await expect(page.getByText(/No personal memories are saved for Alex/)).toBeVisible();
  expect(fixture.writes.at(-1)).toEqual({ profile: 'profile-alex', key: 'drink', method: 'DELETE' });
  expect(fixture.entries['profile-sam']).toHaveLength(2);
});

test('keeps an edited value available when saving fails', async ({ page }) => {
  await mockMemories(page, true);
  await page.goto('/user-memories?profile=profile-sam');
  await page.getByRole('button', { name: 'Edit Preferences', exact: true }).click();
  await page.getByLabel('Edit Preferences', { exact: true }).fill('Retry this value');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText('Memory storage is unavailable.');
  await expect(page.getByLabel('Edit Preferences', { exact: true })).toHaveValue('Retry this value');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Memory saved for Sam.');
});

test('labels birthday memories and keeps older preference memories editable', async ({ page }) => {
  const fixture = await mockMemories(page);
  fixture.entries['profile-sam'].push(
    { key: 'birthday', value: 'March 14, 1990', createdAt: '2026-09-13T20:00:00Z', expiresAt: null },
    { key: 'preferred_room', value: 'Office', createdAt: '2026-09-13T20:00:00Z', expiresAt: null },
  );
  await page.goto('/user-memories?profile=profile-sam');
  await expect(page.getByRole('heading', { name: 'Birthday', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Edit Preferred room', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Edit Preferences', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Edit Birthday', exact: true }).click();
  await page.getByLabel('Edit Birthday', { exact: true }).fill('March 15, 1990');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Memory saved for Sam.');
  expect(fixture.writes[0]).toEqual({
    profile: 'profile-sam', key: 'birthday', method: 'PUT',
    body: { value: 'March 15, 1990', expiresAt: null },
  });
});

test('returns keyboard focus to the edited memory after saving', async ({ page }) => {
  await mockMemories(page);
  await page.goto('/user-memories?profile=profile-sam');
  const editButton = page.getByRole('button', { name: 'Edit Preferences', exact: true });
  await editButton.focus();
  await page.keyboard.press('Enter');
  await page.getByLabel('Edit Preferences', { exact: true }).fill('Keep replies concise.');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Save changes', exact: true })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('status')).toHaveText('Memory saved for Sam.');
  await expect(editButton).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Delete Preferences', exact: true })).toBeFocused();
});

test('returns keyboard focus to the memory after cancelling an edit', async ({ page }) => {
  await mockMemories(page);
  await page.goto('/user-memories?profile=profile-sam');
  const editButton = page.getByRole('button', { name: 'Edit Preferences', exact: true });
  await editButton.focus();
  await page.keyboard.press('Enter');
  await page.getByRole('button', { name: 'Cancel', exact: true }).focus();
  await page.keyboard.press('Enter');
  await expect(editButton).toBeFocused();
});

test('focuses the user heading when a saved memory no longer matches the search', async ({ page }) => {
  await mockMemories(page);
  await page.goto('/user-memories?profile=profile-sam');
  await page.getByLabel('Search memories').fill('warm lighting');
  await page.getByRole('button', { name: 'Edit Preferences', exact: true }).click();
  await page.getByLabel('Edit Preferences', { exact: true }).fill('Keep replies concise.');
  await page.keyboard.press('Tab');
  await page.keyboard.press('Enter');
  await expect(page.getByText('No memories match this search.', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Sam', exact: true })).toBeFocused();
});

for (const scenario of [
  { name: 'next memory', profile: 'profile-sam', deleted: 'Preferred name', next: 'Preferences' },
  { name: 'previous memory', profile: 'profile-sam', deleted: 'Preferences', next: 'Preferred name' },
  { name: 'user heading when no memories remain', profile: 'profile-alex', deleted: 'drink', next: null },
]) {
  test(`moves keyboard focus to the ${scenario.name} after deleting`, async ({ page }) => {
    await mockMemories(page);
    await page.goto(`/user-memories?profile=${scenario.profile}`);
    const deleteButton = page.getByRole('button', { name: `Delete ${scenario.deleted}`, exact: true });
    await deleteButton.focus();
    page.once('dialog', dialog => dialog.accept());
    await page.keyboard.press('Enter');
    await expect(deleteButton).toHaveCount(0);
    if (scenario.next) {
      await expect(page.getByRole('button', { name: `Edit ${scenario.next}`, exact: true })).toBeFocused();
    } else {
      await expect(page.getByRole('heading', { name: 'Alex', exact: true })).toBeFocused();
    }
  });
}

test('does not substitute another user for an unknown profile link', async ({ page }) => {
  await mockMemories(page);
  await page.goto('/user-memories?profile=missing-profile');
  await expect(page.getByRole('heading', { name: 'That profile is no longer available' })).toBeVisible();
  await expect(page.getByText('Coffee', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Edit / })).toHaveCount(0);
});

test('keeps memory controls usable on desktop and narrow screens', async ({ page }, testInfo) => {
  await mockMemories(page);
  await page.goto('/user-memories?profile=profile-sam');
  await expect(page.getByRole('button', { name: 'Edit Preferences', exact: true })).toBeVisible();
  for (const viewport of [
    { name: 'desktop', width: 1280, height: 900 },
    { name: 'mobile', width: 390, height: 844 },
  ]) {
    await page.setViewportSize(viewport);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(viewport.width);
    const heights = await page.locator('main button:visible').evaluateAll(buttons =>
      buttons.map(button => button.getBoundingClientRect().height));
    expect(heights.every(height => height >= 44)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`user-memories-${viewport.name}.png`), fullPage: true });
  }
});
