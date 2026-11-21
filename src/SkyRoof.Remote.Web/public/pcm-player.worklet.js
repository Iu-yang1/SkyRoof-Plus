class SkyRoofPcmPlayer extends globalThis.AudioWorkletProcessor {
  constructor() {
    super();
    this.queue = [];
    this.current = null;
    this.offset = 0;
    this.queuedFrames = 0;
    this.started = false;
    this.epoch = -1;
    this.underruns = 0;
    this.callbacks = 0;
    this.port.onmessage = event => {
      const packet = event.data;
      if (packet.kind !== 'samples') return;
      if (this.epoch !== packet.epoch) {
        this.queue.length = 0;
        this.current = null;
        this.offset = 0;
        this.queuedFrames = 0;
        this.started = false;
        this.epoch = packet.epoch;
      }
      const frame = { samples: packet.samples, channels: packet.channels };
      const count = frame.samples.length / frame.channels;
      this.queue.push(frame);
      this.queuedFrames += count;
      while (this.queuedFrames > 9600 && this.queue.length > 1) {
        const oldest = this.queue.shift();
        this.queuedFrames -= oldest.samples.length / oldest.channels;
      }
      if (!this.started && this.queuedFrames >= 2880) this.started = true;
    };
  }

  process(_inputs, outputs) {
    const output = outputs[0];
    if (!output || output.length === 0) return true;
    const left = output[0];
    const right = output[1] ?? left;
    left.fill(0);
    if (right !== left) right.fill(0);
    for (let i = 0; i < left.length; ++i) {
      if (!this.started) break;
      if (!this.current) {
        this.current = this.queue.shift() ?? null;
        this.offset = 0;
        if (!this.current) {
          this.underruns++;
          this.started = false;
          break;
        }
      }
      const samples = this.current.samples;
      const channels = this.current.channels;
      const pos = this.offset * channels;
      left[i] = samples[pos];
      if (right !== left) right[i] = samples[pos + (channels === 2 ? 1 : 0)];
      this.offset++;
      this.queuedFrames--;
      if (this.offset >= samples.length / channels) {
        this.current = null;
        this.offset = 0;
      }
    }
    this.callbacks++;
    if (this.callbacks % 400 === 0)
      this.port.postMessage({ kind: 'statistics', underruns: this.underruns,
        bufferedMilliseconds: Math.round(this.queuedFrames / 48) });
    return true;
  }
}
globalThis.registerProcessor('skyroof-pcm-player', SkyRoofPcmPlayer);
