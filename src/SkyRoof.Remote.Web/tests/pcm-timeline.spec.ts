import { expect, test } from '@playwright/test';
import { PcmTimeline } from '../src/PcmTimeline';

function frame(epoch: number, sequence: bigint, first: bigint) {
  const bytes = new ArrayBuffer(56);
  const v = new DataView(bytes);
  v.setUint32(0, 0x31504153, true);
  v.setUint8(4, 1); v.setUint8(5, 2);
  v.setUint32(12, epoch, true);
  v.setBigUint64(16, sequence, true); v.setBigUint64(24, first, true);
  v.setUint32(40, 48000, true); v.setUint16(44, 2, true);
  v.setUint8(46, 1); v.setUint32(48, 4, true);
  v.setInt16(52, 16384, true); v.setInt16(54, -16384, true);
  return bytes;
}

test('SAP PCM timeline preserves small gaps and resets after a long gap or new epoch', () => {
  const t = new PcmTimeline();
  expect(t.accept(frame(1, 1n, 0n))?.discontinuity).toBe(true);
  expect(t.accept(frame(1, 1n, 0n))).toBeNull();
  const gap = t.accept(frame(1, 3n, 4n));
  expect(Array.from(gap?.samples ?? [])).toEqual([0, 0, 0.5, -0.5]);
  expect(t.accept(frame(1, 4n, 3n))).toBeNull();
  expect(t.accept(frame(1, 5n, 50000n))?.discontinuity).toBe(true);
  expect(t.accept(frame(2, 1n, 0n))?.discontinuity).toBe(true);
  t.reset();
  expect(t.accept(frame(2, 1n, 0n))?.discontinuity).toBe(true);
});
