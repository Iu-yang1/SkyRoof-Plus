import { expect, test } from '@playwright/test';

test.use({ channel: 'chrome' });
test('authenticated ONVIF RTSP video decodes real H264 frames', async ({ page }) => {
  test.skip(!process.env.SKYROOF_CAMERA_BROWSER_TEST, 'Requires the real loopback ONVIF and RTP fixtures');
  await page.goto('http://127.0.0.1:8076');
  await page.getByLabel('USER').fill('browser-admin');
  await page.getByLabel('PASSWORD').fill('BrowserSmokeTestP@ssw0rd!');
  await page.getByRole('button', { name: 'SIGN IN' }).click();
  await expect(page.getByText('SignalR/MessagePack connected')).toBeVisible();
  await page.getByRole('button', { name: 'CAMERA', exact: true }).click();
  await page.getByRole('button', { name: 'LOAD PROFILES' }).click();
  await expect(page.getByRole('button', { name: 'START VIDEO' })).toBeEnabled();
  await page.getByRole('button', { name: 'START VIDEO' }).click();
  const video = page.locator('video');
  await expect.poll(() => video.evaluate((v: HTMLVideoElement) => ({
    width: v.videoWidth, height: v.videoHeight,
    decoded: v.getVideoPlaybackQuality().totalVideoFrames > 2,
    playing: v.currentTime > 0.2 && !v.paused,
  })), { timeout: 20000 }).toEqual({ width: 160, height: 120, decoded: true, playing: true });
  const firstTime = await video.evaluate((v: HTMLVideoElement) => v.currentTime);
  await expect.poll(() => video.evaluate((v: HTMLVideoElement) => v.currentTime), { timeout: 5000 }).toBeGreaterThan(firstTime + 0.3);
  await page.getByRole('button', { name: 'STOP VIDEO' }).click();
  await expect(video).toHaveCount(0);
});
