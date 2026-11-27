class SkyRoofPcmCapture extends globalThis.AudioWorkletProcessor {
  constructor() {
    super();
    this.buffer = new Int16Array(960);
    this.offset = 0;
    this.active = false;
    this.port.onmessage = event => {
      this.active = event.data.active === true;
      this.generation = event.data.generation;
      this.offset = 0;
    };
  }

  process(inputs, outputs) {
    for (const channel of outputs[0] ?? []) channel.fill(0);
    if (!this.active) return true;
    const samples = inputs[0]?.[0];
    if (!samples) return true;
    for (const sample of samples) {
      this.buffer[this.offset++] = Math.max(-32768, Math.min(32767, Math.round(sample * 32768)));
      if (this.offset === 960) {
        const bytes = new ArrayBuffer(1920);
        const view = new DataView(bytes);
        for (let i = 0; i < 960; i++) view.setInt16(i * 2, this.buffer[i], true);
        this.port.postMessage({ generation: this.generation, pcm: bytes }, [bytes]);
        this.offset = 0;
      }
    }
    return true;
  }
}
globalThis.registerProcessor('skyroof-pcm-capture', SkyRoofPcmCapture);
