import { expect, test } from '@playwright/test';

test('manages an installed appliance from mobile', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  let statusRequestCount = 0;
  let installRequestBody: string | null = null;
  let rollbackRequestCount = 0;
  let finishRollbackResponse: (() => void) | null = null;
  await page.route('**/api/appliance/capabilities', async (route) => {
    await route.fulfill({ json: { enabled: true } });
  });
  await page.route('**/api/auth/status', async (route) => {
    await route.fulfill({
      json: { authenticated: true, setupComplete: true, hasKeys: true },
    });
  });
  await page.route('**/api/appliance/status', async (route) => {
    statusRequestCount++;
    await route.fulfill({
      json: {
        hostname: 'lucia',
        architecture: 'arm64',
        board: 'jetson-orin-nano-super-p3767-0005',
        luciaVersion: '0.2.0',
        storageBytes: 2_000_000_000_000,
        rebootRequired: false,
        network: { ssid: 'Home WiFi', signal: 87 },
        os: {
          name: 'Ubuntu',
          versionId: '22.04',
          imageVersion: '0.2.0',
          jetsonLinuxVersion: '36.5.2',
        },
        services: [
          { id: 'agenthost', activeState: 'active', unitFileState: 'enabled' },
          { id: 'redis', activeState: 'active', unitFileState: 'enabled' },
          { id: 'collector', activeState: 'inactive', unitFileState: 'disabled' },
          { id: 'redis-exporter', activeState: 'inactive', unitFileState: 'disabled' },
        ],
      },
    });
  });
  await page.route('**/api/appliance/telemetry', async (route) => {
    await route.fulfill({
      json: {
        configured: false,
        enabled: false,
        endpoint: '',
        insecureSkipVerify: false,
        hasAuthorization: false,
      },
    });
  });
  await page.route('**/api/appliance/updates/operation', async (route) => {
    const hasInstallStarted = installRequestBody !== null;
    await route.fulfill({
      json: {
        operationId: hasInstallStarted
          ? '11111111-1111-1111-1111-111111111111'
          : null,
        action: hasInstallStarted ? 'apply' : 'none',
        channel: hasInstallStarted ? 'lucia' : 'none',
        status: hasInstallStarted ? 'succeeded' : 'idle',
        tag: hasInstallStarted ? 'v0.3.0' : null,
        message: null,
        luciaRollbackAvailable: hasInstallStarted,
        osRollbackAvailable: false,
      },
    });
  });
  await page.route('**/api/appliance/updates', async (route) => {
    await route.fulfill({
      json: {
        currentLuciaVersion: '0.2.0',
        currentOsVersion: '0.2.0',
        latestLuciaVersion: '0.3.0',
        latestOsVersion: '0.4.0',
        manifestAvailable: true,
        compatible: true,
        luciaCompatible: true,
        osCompatible: true,
        luciaNewerDiscovered: true,
        osNewerDiscovered: true,
        luciaUpdateAvailable: true,
        osUpdateAvailable: true,
        releaseTag: 'v0.3.0',
        releaseUrl: 'https://github.com/seiggy/lucia-dotnet/releases/tag/v0.3.0',
        message: 'A signed update is ready to verify and install.',
      },
    });
  });
  await page.route('**/api/appliance/updates/lucia/install', async (route) => {
    installRequestBody = route.request().postData();
    await route.fulfill({
      status: 202,
      json: {
        operationId: '11111111-1111-1111-1111-111111111111',
        action: 'stage',
        channel: 'lucia',
        status: 'queued',
        tag: 'v0.3.0',
        message: null,
        luciaRollbackAvailable: true,
        osRollbackAvailable: false,
      },
    });
  });
  await page.route('**/api/appliance/updates/operations/*', async (route) => {
    const isRollback = rollbackRequestCount > 0;
    await route.fulfill({
      json: {
        operationId: isRollback
          ? '22222222-2222-2222-2222-222222222222'
          : '11111111-1111-1111-1111-111111111111',
        action: isRollback ? 'rollback' : 'apply',
        channel: 'lucia',
        status: 'succeeded',
        tag: 'v0.3.0',
        message: null,
        luciaRollbackAvailable: false,
        osRollbackAvailable: false,
      },
    });
  });
  await page.route('**/api/appliance/updates/lucia/rollback', async (route) => {
    rollbackRequestCount++;
    await new Promise<void>((resolve) => {
      finishRollbackResponse = resolve;
    });
    await route.fulfill({
      status: 202,
      json: {
        operationId: '22222222-2222-2222-2222-222222222222',
        action: 'rollback',
        channel: 'lucia',
        status: 'queued',
        tag: 'v0.3.0',
        message: null,
        luciaRollbackAvailable: true,
        osRollbackAvailable: false,
      },
    });
  });
  await page.route('**/api/system/restart', async (route) => {
    await route.fulfill({ status: 202 });
  });

  const capabilitiesLoaded = page.waitForResponse('**/api/appliance/capabilities');
  const authenticationLoaded = page.waitForResponse('**/api/auth/status');
  await page.goto('/');
  await Promise.all([capabilitiesLoaded, authenticationLoaded]);
  await page.getByRole('button', { name: 'Open sidebar menu' }).click();
  await page.getByRole('link', { name: 'Appliance' }).click();

  await expect(page.getByRole('heading', { name: 'lucia', exact: true })).toBeVisible();
  await expect(page.getByText('2/4 active')).toBeVisible();
  await page.getByRole('button', { name: 'Check for updates' }).click();
  await expect(page.getByRole('button', { name: /^Install / })).toHaveCount(2);
  await page.getByRole('button', { name: 'Install Lucia' }).click();
  await page.getByRole('button', { name: 'Verify and install' }).click();
  await expect.poll(() => installRequestBody).toBe('{"tag":"v0.3.0"}');
  await expect(
    page.getByRole('button', { name: 'Restart', exact: true }).first(),
  ).toBeDisabled();
  await expect(page.getByRole('button', { name: /^Install / })).toHaveCount(0);
  await expect(page.getByText(
    'Lucia update installed. Services are restarting.',
  )).toBeVisible();
  await page.getByRole('button', { name: 'Roll back Lucia' }).click();
  await page.getByRole('button', { name: 'Roll back', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Roll back Lucia' })).toBeDisabled();
  expect(rollbackRequestCount).toBe(1);
  expect(finishRollbackResponse).not.toBeNull();
  const rollbackReloaded = page.waitForResponse('**/api/appliance/status');
  finishRollbackResponse?.();
  await rollbackReloaded;
  await expect(page.getByText(
    'Lucia rollback completed. Services are restarting.',
  )).toBeVisible();
  await expect(page.getByRole('heading', { name: 'OpenTelemetry', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Restart', exact: true })).toHaveCount(2);
  const agentHostRow = page
    .getByRole('heading', { name: 'Lucia AgentHost', exact: true })
    .locator('..')
    .locator('..')
    .locator('..');
  const statusRequestsBeforeRestart = statusRequestCount;
  await agentHostRow.getByRole('button', { name: 'Restart' }).click();
  await expect(page.getByText('Lucia AgentHost restart requested.')).toBeVisible();
  expect(statusRequestCount).toBe(statusRequestsBeforeRestart);

  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(390);
  const shortControls = await page.locator(
    'button:visible, input:visible:not([type="checkbox"]), select:visible',
  ).evaluateAll(
    (controls) => controls
      .map((control) => control.getBoundingClientRect().height)
      .filter((height) => height < 44),
  );
  expect(shortControls).toEqual([]);

  await page.getByRole('button', { name: 'Reboot Jetson' }).click();
  await expect(page.getByRole('dialog', { name: 'Reboot the Jetson?' })).toBeVisible();
});

test('keeps appliance navigation when the manager is temporarily unavailable', async ({ page }) => {
  await page.route('**/api/appliance/capabilities', async (route) => {
    await route.fulfill({ json: { enabled: true } });
  });
  await page.route('**/api/auth/status', async (route) => {
    await route.fulfill({
      json: { authenticated: true, setupComplete: true, hasKeys: true },
    });
  });
  await page.route('**/api/appliance/status', async (route) => {
    await route.fulfill({
      status: 502,
      json: { detail: 'The appliance manager is restarting.' },
    });
  });
  await page.route('**/api/appliance/telemetry', async (route) => {
    await route.fulfill({
      status: 502,
      json: { detail: 'The appliance manager is restarting.' },
    });
  });
  await page.route('**/api/appliance/updates/operation', async (route) => {
    await route.fulfill({
      status: 502,
      json: { detail: 'The appliance manager is restarting.' },
    });
  });

  const capabilitiesLoaded = page.waitForResponse('**/api/appliance/capabilities');
  const authenticationLoaded = page.waitForResponse('**/api/auth/status');
  await page.goto('/');
  await Promise.all([capabilitiesLoaded, authenticationLoaded]);
  await page.getByRole('link', { name: 'Appliance' }).click();

  await expect(page).toHaveURL(/\/appliance$/);
  await expect(page.getByRole('alert')).toContainText('appliance manager is restarting');
  await expect(page.getByRole('link', { name: 'Appliance' })).toBeVisible();
});
