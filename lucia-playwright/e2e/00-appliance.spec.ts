import { expect, test } from '@playwright/test';

test('manages an installed appliance from mobile', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.route('**/api/auth/status', async (route) => {
    await route.fulfill({
      json: { authenticated: true, setupComplete: true, hasKeys: true },
    });
  });
  await page.route('**/api/appliance/status', async (route) => {
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
  await page.route('**/api/appliance/updates', async (route) => {
    await route.fulfill({
      json: {
        currentLuciaVersion: '0.2.0',
        currentOsVersion: '0.2.0',
        latestLuciaVersion: '0.3.0',
        latestOsVersion: '0.4.0',
        manifestAvailable: true,
        compatible: true,
        luciaUpdateAvailable: false,
        osUpdateAvailable: false,
        releaseUrl: 'https://github.com/seiggy/lucia-dotnet/releases/tag/v0.3.0',
        message: 'A compatible release was found, but installation remains locked until GitHub attestation verification is implemented.',
      },
    });
  });

  await page.goto('/appliance');

  await expect(page.getByRole('heading', { name: 'lucia', exact: true })).toBeVisible();
  await expect(page.getByText('2/4 active')).toBeVisible();
  await page.getByRole('button', { name: 'Check for updates' }).click();
  await expect(page.getByText('Verification required')).toHaveCount(2);
  await expect(page.getByRole('button', { name: 'Install' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'OpenTelemetry', exact: true })).toBeVisible();

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

  await page.goto('/appliance');

  await expect(page).toHaveURL(/\/appliance$/);
  await expect(page.getByRole('alert')).toContainText('appliance manager is restarting');
  await expect(page.getByRole('link', { name: 'Appliance' })).toBeVisible();
});
