import { expect, test } from '@playwright/test';

test('guides the first browser claim through installation', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 568 });

  let installRequest: unknown;
  let installStarted = false;
  await page.route('**/api/installer/capabilities', async (route) => {
    await route.fulfill({
      json: {
        mode: 'installer',
        requiresSetupCode: false,
        isClaimed: false,
      },
    });
  });
  await page.route('**/api/installer/claim', async (route) => {
    await route.fulfill({ json: { claimed: true } });
  });

  await page.route('**/api/installer/status', async (route) => {
    await route.fulfill({
      json: installStarted
        ? {
            phase: 'installing',
            stage: 'writing',
            bytesWritten: 30_601_641_984,
            totalBytes: 61_203_283_968,
          }
        : { phase: 'waiting-for-configuration' },
    });
  });

  await page.route('**/api/installer/disks', async (route) => {
    await route.fulfill({
      json: [
        {
          id: '/dev/disk/by-id/nvme-Lab_SSD_LAB123',
          model: 'Lab SSD',
          serial: 'LAB123',
          confirmationPhrase: 'ERASE LAB123',
          sizeBytes: 2_000_000_000_000,
          classification: 'occupied',
          action: 'confirmation-required',
        },
      ],
    });
  });
  await page.route('**/api/installer/networks', async (route) => {
    await route.fulfill({
      json: [{ ssid: 'Lab WiFi', signal: 82, security: 'WPA2' }],
    });
  });
  await page.route('**/api/installer/install', async (route) => {
    installRequest = route.request().postDataJSON();
    installStarted = true;
    await route.fulfill({
      status: 202,
      json: {
        phase: 'authorized',
        dashboardKey: 'lk_dashboard-owner-bootstrap-key',
      },
    });
  });

  await page.goto('/install', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('img', { name: 'Lucia is starting' })).toBeVisible();
  await expect(page.locator('.installer-boot')).toHaveClass(/is-active/);
  const logoAnimation = await page.locator('.installer-boot-logo').evaluate(
    (logo) => getComputedStyle(logo).animationName,
  );
  expect(logoAnimation).toBe('installer-logo-wake');
  await expect(page.getByRole('heading', { name: 'Bring Lucia home' })).toBeVisible();
  await expect(page.getByRole('img', { name: 'Lucia is starting' })).toHaveCount(0);
  await expect(page.getByText('Step 1 of 5')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(320);
  const firstActionBottom = await page
    .getByRole('button', { name: 'Begin setup' })
    .evaluate((button) => button.getBoundingClientRect().bottom);
  expect(firstActionBottom).toBeLessThanOrEqual(568);
  const shortControls = await page.locator('button:visible').evaluateAll(
    (controls) => controls
      .map((control) => control.getBoundingClientRect().height)
      .filter((height) => height < 44),
  );
  expect(shortControls).toEqual([]);

  await expect(page.getByLabel('Setup code')).toHaveCount(0);
  await page.getByRole('button', { name: 'Begin setup' }).click();

  await expect(page.getByText('Lab SSD')).toBeVisible();
  await page.getByRole('button', { name: /Use Lab SSD/ }).click();
  await page.getByRole('button', { name: 'Continue to network' }).click();

  await page.getByLabel('Home Wi-Fi').selectOption('Lab WiFi');
  await page.getByLabel('Wi-Fi password').fill('lab-wifi-password');
  await page.getByLabel('Lucia name').fill('lucia-lab');
  await page.getByLabel('Recovery password', { exact: true }).fill('correct horse battery staple');
  await page.getByLabel('Confirm recovery password').fill('correct horse battery staple');
  await page.getByRole('button', { name: 'Review installation' }).click();

  await expect(page.getByText('Everything on Lab SSD will be erased')).toBeVisible();
  await page
    .getByLabel('Type the erase phrase to confirm')
    .fill('ERASE LAB123');
  await page.getByRole('button', { name: 'Erase drive and install Lucia' }).click();

  await expect(page.getByRole('heading', { name: 'Lucia is moving in' })).toBeVisible();
  await expect(page.getByRole('progressbar', { name: 'NVMe image write progress' }))
    .toHaveAttribute('aria-valuenow', '50');
  await expect(page.getByText('50.0%')).toBeVisible();
  expect(installRequest).toEqual({
    deviceId: '/dev/disk/by-id/nvme-Lab_SSD_LAB123',
    eraseConfirmation: 'ERASE LAB123',
    hostname: 'lucia-lab',
    recoveryPassword: 'correct horse battery staple',
    wifi: {
      ssid: 'Lab WiFi',
      passphrase: 'lab-wifi-password',
    },
  });
  await expect(page.getByText('lk_dashboard-owner-bootstrap-key')).toBeVisible();
});

test('does not expose installer setup on a non-appliance host', async ({ page }) => {
  await page.route('**/api/installer/capabilities', async (route) => {
    await route.fulfill({ status: 404 });
  });
  await page.route('**/api/auth/status', async (route) => {
    await route.fulfill({
      json: {
        authenticated: false,
        setupComplete: false,
        hasKeys: false,
      },
    });
  });

  await page.goto('/install');
  await expect(page).toHaveURL(/\/setup$/);
  await expect(page.getByRole('heading', { name: 'Bring Lucia home' })).toHaveCount(0);
});

test('restores persisted installation progress after reconnect', async ({ page }) => {
  let dashboardKeyAcknowledged = false;
  await page.addInitScript(() => {
    sessionStorage.setItem('lucia-installer-boot-seen', 'true');
  });
  await page.route('**/api/installer/capabilities', async (route) => {
    await route.fulfill({
      json: { mode: 'installer', requiresSetupCode: false, isClaimed: true },
    });
  });
  await page.route('**/api/installer/claim', async (route) => {
    await route.fulfill({ json: { claimed: true } });
  });
  await page.route('**/api/installer/status', async (route) => {
    await route.fulfill({
      json: {
        phase: 'installing',
        stage: 'writing',
        bytesWritten: 30_601_641_984,
        totalBytes: 61_203_283_968,
        dashboardKey: 'lk_recovered-dashboard-owner-key',
      },
    });
  });
  await page.route('**/api/installer/dashboard-key/acknowledge', async (route) => {
    dashboardKeyAcknowledged = true;
    await route.fulfill({ status: 204 });
  });

  await page.goto('/install');

  await expect(page.getByRole('heading', { name: 'Lucia is moving in' })).toBeVisible();
  await expect(page.getByText('50.0%')).toBeVisible();
  await expect(page.getByText('lk_recovered-dashboard-owner-key')).toBeVisible();
  expect(dashboardKeyAcknowledged).toBe(false);
  await page.getByRole('button', { name: 'I saved this key' }).click();
  expect(dashboardKeyAcknowledged).toBe(true);
  await expect.poll(
    () => page.evaluate(() => sessionStorage.getItem(
      'lucia-dashboard-bootstrap-key',
    )),
  ).toBeNull();
});

test('offers Wi-Fi retry immediately after provisioning fails', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 568 });
  await page.addInitScript(() => {
    sessionStorage.setItem('lucia-installer-boot-seen', 'true');
  });
  await page.route('**/api/installer/capabilities', async (route) => {
    await route.fulfill({
      json: { mode: 'installer', requiresSetupCode: false, isClaimed: true },
    });
  });
  await page.route('**/api/installer/status', async (route) => {
    await route.fulfill({
      json: {
        phase: 'failed',
        stage: 'failed',
        canRetryNetwork: true,
        message: 'Home Wi-Fi could not connect. Check the network name and password, then retry.',
      },
    });
  });
  await page.route('**/api/installer/networks', async (route) => {
    await route.fulfill({
      json: [{ ssid: 'Lab WiFi', signal: 82, security: 'WPA2' }],
    });
  });
  await page.route('**/api/installer/retry-network', async (route) => {
    await route.fulfill({ status: 202, json: { phase: 'authorized' } });
  });

  await page.goto('/install');

  await expect(page.getByRole('heading', { name: 'Installation needs attention' })).toBeVisible();
  await expect(page.getByLabel('Home Wi-Fi')).toBeVisible();
  await page.getByLabel('Home Wi-Fi').selectOption('Lab WiFi');
  await page.getByLabel('Wi-Fi password').fill('corrected-password');
  await page.getByRole('button', { name: 'Retry Wi-Fi' }).click();
  await expect(page.getByRole('heading', { name: 'Lucia is moving in' })).toBeVisible();
});

test('shows the server error when another browser owns setup', async ({ page }) => {
  await page.addInitScript(() => {
    sessionStorage.setItem('lucia-installer-boot-seen', 'true');
  });
  await page.route('**/api/installer/capabilities', async (route) => {
    await route.fulfill({
      json: { mode: 'installer', requiresSetupCode: false, isClaimed: true },
    });
  });
  await page.route('**/api/installer/claim', async (route) => {
    await route.fulfill({
      status: 409,
      json: { error: 'This Lucia is already being set up in another browser.' },
    });
  });

  await page.goto('/install');
  await page.getByRole('button', { name: 'Begin setup' }).click();

  await expect(page.getByRole('alert')).toHaveText(
    'This Lucia is already being set up in another browser.',
  );
});
