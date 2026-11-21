import { expect, test } from '@playwright/test';

test('authenticated SkyRoof Remote Dockview workstation renders and saves layout', async ({ page }) => {
  await page.goto('http://127.0.0.1:8076');
  await expect(page.getByRole('heading', { name: 'SkyRoof Remote' })).toBeVisible();
  await page.getByLabel('USER').fill('browser-admin');
  await page.getByLabel('PASSWORD').fill('BrowserSmokeTestP@ssw0rd!');
  await page.getByRole('button', { name: 'SIGN IN' }).click();

  await expect(page.getByText('REMOTE V1 · DEV PROTOTYPE')).toBeVisible();
  await expect(page.getByText('RADIO · LIVE CAT READBACK')).toBeVisible();
  await expect(page.getByText('48,000 samples/s · PCM16 · SAP/1')).toBeVisible();
  await expect(page.getByText('FT4 / CW CONSOLE')).toBeVisible();
  await expect(page.getByText('SignalR/MessagePack connected')).toBeVisible();

  await page.getByRole('button', { name: 'SAVE LAYOUT' }).click();
  const stored = await page.evaluate(() => localStorage.getItem('skyroof.layout.local'));
  expect(stored).toBeTruthy();

  await page.getByRole('button', { name: 'Remote Workspace' }).click();
  await expect(page.getByText('No Station Hub linked to this Workspace')).toBeVisible();
  await page.getByRole('button', { name: 'Local Station' }).click();
  await expect(page.getByText('RADIO · LIVE CAT READBACK')).toBeVisible();
});
