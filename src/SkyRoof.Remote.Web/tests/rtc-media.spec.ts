import { test, expect } from '@playwright/test';

const base = process.env.SKYROOF_RTC_TEST_URL;

test('packaged native RTC receives real SAP/1 PCM in Chromium', async ({ page }) => {
  test.skip(!base, 'Requires Windows packaged RTC/SignalBus fixture');
  const url = base ?? '';
  await page.goto(url);
  const login = await page.request.post(url + '/api/v1/auth/login', {
    data: { userName: 'rtc-admin', password: 'RtcFixture-Password!123' }
  });
  expect(login.ok()).toBe(true);
  const result = await page.evaluate(async () => {
    const peer = new RTCPeerConnection();
    const channel = peer.createDataChannel('skyroof-rx-pcm', { ordered: false, maxRetransmits: 0 });
    channel.binaryType = 'arraybuffer';
    const signal = new WebSocket('ws://' + location.host + '/ws/rtc-rx');
    try {
      const packet = new Promise<ArrayBuffer>((resolve, reject) => {
        channel.onmessage = event => resolve(event.data as ArrayBuffer);
        channel.onerror = () => reject(new Error('RTC channel failed'));
      });
      await peer.setLocalDescription(await peer.createOffer());
      await new Promise<void>(resolve => {
        if (peer.iceGatheringState === 'complete') { resolve(); return; }
        const done = () => {
          if (peer.iceGatheringState === 'complete') {
            peer.removeEventListener('icegatheringstatechange', done);
            resolve();
          }
        };
        peer.addEventListener('icegatheringstatechange', done);
      });
      if (signal.readyState !== WebSocket.OPEN)
        await new Promise<void>((resolve, reject) => {
          signal.onopen = () => resolve();
          signal.onerror = () => reject(new Error('RTC signaling unavailable'));
        });
      const answer = new Promise<string>((resolve, reject) => {
        signal.onmessage = event => resolve(event.data as string);
        signal.onclose = () => reject(new Error('RTC signaling closed before answer'));
      });
      signal.send(peer.localDescription?.sdp ?? '');
      await peer.setRemoteDescription({ type: 'answer', sdp: await answer });
      const view = new DataView(await packet);
      return { magic: view.getUint32(0, true), epoch: view.getUint32(12, true),
        rate: view.getUint32(40, true), samples: view.getUint16(44, true),
        pcm: view.getInt16(52, true) };
    } finally {
      signal.close();
      peer.close();
    }
  });
  expect(result).toEqual({ magic: 0x31504153, epoch: 42, rate: 48000, samples: 960, pcm: 1234 });
});
