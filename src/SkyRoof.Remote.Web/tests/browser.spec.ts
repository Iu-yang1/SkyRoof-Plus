import { expect, test } from '@playwright/test';

test('authenticated SkyRoof Remote Dockview workstation renders and saves layout', async ({ page }) => {
  await page.route('**/api/v1/media/rx-status', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: JSON.stringify({
      epoch: 147, source: 'icom-lan', streamActive: true, state: 'publishing',
      framesWritten: 22, samplesSkipped: 480, datagramsReceived: 26,
      datagramsOutOfOrder: 2, datagramsDropped: 1, datagramsMissing: 1
    })
  }));
  await page.goto('http://127.0.0.1:8076');
  await expect(page.getByRole('heading', { name: 'SkyRoof Remote' })).toBeVisible();
  await page.getByLabel('USER').fill('browser-admin');
  await page.getByLabel('PASSWORD').fill('BrowserSmokeTestP@ssw0rd!');
  await page.getByRole('button', { name: 'SIGN IN' }).click();

  const localSlot = page.locator('.workspace-slot').first();
  const remoteSlot = page.locator('.workspace-slot').nth(1);
  await expect(page.getByText('REMOTE V1 · DEV PROTOTYPE')).toBeVisible();
  // An omitted EL must not silently become 0° and steer a real rotator.
  const rotatorPanel = localSlot.locator('.rotator-panel');
  await rotatorPanel.getByLabel('Park waypoints').fill('90,');
  await rotatorPanel.getByRole('button', { name: 'PARK ROUTE' }).click();
  await expect(rotatorPanel.getByText(
    'PARK failed or interrupted: Error: Park route must contain AZ,EL pairs separated by semicolons.'
  )).toBeVisible();
  await expect(localSlot.getByText('RADIO · LIVE CAT READBACK')).toBeVisible();
  await expect(localSlot.getByText('48,000 samples/s · PCM16 · SAP/1')).toBeVisible();
  await expect(localSlot.getByText('MEDIA · RX ACTIVE · icom-lan · Epoch 147')).toBeVisible();
  await expect(localSlot.getByText(/Source frames 22.*Missing samples 480/)).toBeVisible();
  await expect(localSlot.getByText('FT4 / CW CONSOLE')).toBeVisible();
  await expect(page.getByText('SignalR/MessagePack connected')).toBeVisible();

  await page.getByRole('button', { name: 'SAVE LAYOUT' }).click();
  const stored = await page.evaluate(() => localStorage.getItem('skyroof.layout.local'));
  expect(stored).toBeTruthy();

  const originalLocal = await localSlot.elementHandle();
  expect(originalLocal).toBeTruthy();
  await expect(localSlot).toBeVisible();
  await expect(remoteSlot).toBeHidden();
  await page.getByRole('button', { name: 'Remote Workspace' }).click();
  await expect(localSlot).toBeHidden();
  await expect(remoteSlot).toBeVisible();
  await expect(remoteSlot.getByText('No Station Hub linked to this Workspace')).toBeVisible();
  await expect(remoteSlot.getByText('MEDIA · NO STATION LINK')).toBeVisible();
  await expect(remoteSlot.getByRole('button', { name: 'PLAY RX' })).toBeDisabled();
  expect(await originalLocal!.evaluate(node => node.isConnected)).toBe(true);
  await page.getByRole('button', { name: 'Local Station' }).click();
  await expect(localSlot).toBeVisible();
  await expect(remoteSlot).toBeHidden();
  expect(await originalLocal!.evaluate(node => node.isConnected)).toBe(true);
  await expect(localSlot.getByText('RADIO · LIVE CAT READBACK')).toBeVisible();
});

test('station control recovers when first SignalR negotiation fails', async ({ page }) => {
  let attempts = 0;
  await page.route('**/hub/control/negotiate*', async route => {
    attempts++;
    if (attempts === 1) await route.abort('failed');
    else await route.continue();
  });

  await page.goto('http://127.0.0.1:8076');
  await page.getByLabel('USER').fill('browser-admin');
  await page.getByLabel('PASSWORD').fill('BrowserSmokeTestP@ssw0rd!');
  await page.getByRole('button', { name: 'SIGN IN' }).click();

  await expect(page.getByText('SignalR/MessagePack connected')).toBeVisible({ timeout: 20000 });
  expect(attempts).toBeGreaterThanOrEqual(2);
});
