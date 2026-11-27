// SAP/1 PCM16 timeline shared by WSS and future RTC Data transport.
export class PcmTimeline {
  private epoch = -1;
  private sequence = -1n;
  private nextSample = 0n;

  reset() { this.epoch = -1; this.sequence = -1n; this.nextSample = 0n; }

  accept(buffer: ArrayBuffer) {
    const view = new DataView(buffer);
    if (view.byteLength < 52 || view.getUint32(0, true) !== 0x31504153 ||
        view.getUint8(4) !== 1 || view.getUint8(5) !== 2) return null;
    const epoch = view.getUint32(12, true);
    const sequence = view.getBigUint64(16, true);
    const first = view.getBigUint64(24, true);
    const frames = view.getUint16(44, true);
    const channels = view.getUint8(46);
    const length = view.getUint32(48, true);
    if (view.getUint32(40, true) !== 48000 || !frames ||
        (channels !== 1 && channels !== 2) ||
        length !== frames * channels * 2 || view.byteLength !== 52 + length) return null;
    const changed = this.epoch !== epoch;
    if (!changed && (sequence <= this.sequence || first < this.nextSample)) return null;
    const gap = changed ? 0n : first - this.nextSample;
    // Short holes preserve the audio clock; long holes re-prime rather than queue stale silence.
    const discontinuity = changed || gap > 4800n;
    const silenceFrames = discontinuity ? 0 : Number(gap);
    const samples = new Float32Array((frames + silenceFrames) * channels);
    for (let i = 0; i < frames * channels; i++)
      samples[silenceFrames * channels + i] = view.getInt16(52 + i * 2, true) / 32768;
    this.epoch = epoch;
    this.sequence = sequence;
    this.nextSample = first + BigInt(frames);
    return { samples, channels, epoch, discontinuity };
  }
}
