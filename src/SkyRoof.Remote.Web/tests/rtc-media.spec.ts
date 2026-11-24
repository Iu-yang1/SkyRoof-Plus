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

test('packaged native RTC sends Opus RTP to a Chromium receive-only track', async ({ page }) => {
  test.skip(!base, 'Requires Windows packaged RTC/SignalBus fixture');
  const url = base ?? '';
  await page.goto(url);
  const login = await page.request.post(url + '/api/v1/auth/login', {
    data: { userName: 'rtc-admin', password: 'RtcFixture-Password!123' }
  });
  expect(login.ok()).toBe(true);
  const result = await page.evaluate(async () => {
    const peer = new RTCPeerConnection();
    peer.addTransceiver('audio', { direction: 'recvonly' });
    const signal = new WebSocket('ws://' + location.host + '/ws/rtc-voice');
    try {
      await peer.setLocalDescription(await peer.createOffer());
      await new Promise<void>(resolve => {
        if (peer.iceGatheringState === 'complete') { resolve(); return; }
        const changed = () => {
          if (peer.iceGatheringState !== 'complete') return;
          peer.removeEventListener('icegatheringstatechange', changed);
          resolve();
        };
        peer.addEventListener('icegatheringstatechange', changed);
      });
      if (signal.readyState !== WebSocket.OPEN)
        await new Promise<void>((resolve, reject) => {
          signal.onopen = () => resolve();
          signal.onerror = () => reject(new Error('Voice signaling unavailable'));
        });
      const answer = new Promise<string>((resolve, reject) => {
        signal.onmessage = event => resolve(event.data as string);
        signal.onclose = () => reject(new Error('Voice signaling closed before answer'));
      });
      signal.send(peer.localDescription?.sdp ?? '');
      await peer.setRemoteDescription({ type: 'answer', sdp: await answer });
      const end = performance.now() + 12000;
      while (performance.now() < end) {
        const stats = await peer.getStats();
        for (const item of stats.values()) {
          if (item.type !== 'inbound-rtp' || item.kind !== 'audio' || item.packetsReceived < 5)
            continue;
          const codec = stats.get(item.codecId);
          if (codec?.mimeType?.toLowerCase() === 'audio/opus' && item.bytesReceived > 0)
            return { codec: codec.mimeType.toLowerCase(), clock: codec.clockRate,
              decoded: item.totalSamplesReceived > 0 };
        }
        await new Promise(resolve => setTimeout(resolve, 100));
      }
      throw new Error('No decoded Opus RTP in Chromium');
    } finally { signal.close(); peer.close(); }
  });
  expect(result).toEqual({ codec: 'audio/opus', clock: 48000, decoded: true });
});
