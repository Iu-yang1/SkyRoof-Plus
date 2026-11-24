import type { HubConnection } from '@microsoft/signalr';

type Grant = { id: string; epoch: number; sequence: number; generation: number; pending: number };
type Capture = { context: AudioContext; stream: MediaStream; node: AudioWorkletNode };

export class TxMicrophone {
  private capture: Capture | null = null;
  private current: Grant | null = null;
  private generation = 0;
  private opening = false;

  constructor(private hub: HubConnection, private status: (message: string) => void) {}

  async enable() {
    if (this.capture || this.opening) return;
    this.opening = true;
    const generation = ++this.generation;
    let stream: MediaStream | null = null;
    let context: AudioContext | null = null;
    try {
      stream = await navigator.mediaDevices.getUserMedia({
        audio: { channelCount: 1, echoCancellation: false, noiseSuppression: false, autoGainControl: false }
      });
      context = new AudioContext({ sampleRate: 48000, latencyHint: 'interactive' });
      if (context.sampleRate !== 48000) throw new Error('48 kHz microphone context unavailable');
      await context.audioWorklet.addModule('/pcm-capture.worklet.js');
      const node = new AudioWorkletNode(context, 'skyroof-pcm-capture', {
        numberOfInputs: 1, numberOfOutputs: 1, channelCount: 1, outputChannelCount: [1]
      });
      context.createMediaStreamSource(stream).connect(node);
      node.connect(context.destination); // worklet always outputs silence
      node.port.onmessage = (event: MessageEvent<{ generation: number; pcm: ArrayBuffer }>) =>
        this.send(event.data.generation, event.data.pcm);
      await context.resume();
      if (generation !== this.generation) throw new Error('Microphone activation cancelled');
      this.capture = { context, stream, node };
      this.status('MIC READY');
    } catch (error) {
      stream?.getTracks().forEach(track => track.stop());
      if (context && context.state !== 'closed') await context.close();
      if (generation === this.generation) this.status(String(error));
    } finally { this.opening = false; }
  }

  get enabled() { return this.capture !== null; }

  grant(id: string, epoch: number) {
    if (!this.capture) return;
    this.current = { id, epoch, sequence: 0, generation: ++this.generation, pending: 0 };
    this.capture.node.port.postMessage({ active: true, generation: this.generation });
    this.status('TX MIC · PCM16');
  }

  revoke() {
    this.current = null;
    this.capture?.node.port.postMessage({ active: false, generation: ++this.generation });
    if (this.capture) this.status('MIC READY');
  }

  disable() {
    this.generation++;
    this.revoke();
    const capture = this.capture;
    this.capture = null;
    if (capture) {
      capture.node.port.onmessage = null;
      capture.node.disconnect();
      capture.stream.getTracks().forEach(track => track.stop());
      void capture.context.close();
    }
    this.status('MIC OFF');
  }

  private send(generation: number, pcm: ArrayBuffer) {
    const grant = this.current;
    if (!grant || grant.generation !== generation) return;
    const sequence = grant.sequence++;
    // Real-time audio must not queue behind a slow network; keep capacity for
    // PTT release and lease heartbeats on the same SignalR connection.
    if (grant.pending >= 5) return;
    grant.pending++;
    void this.hub.invoke<boolean>('PushTxPcm', grant.id, grant.epoch, sequence, new Uint8Array(pcm))
      .then(accepted => {
        if (!accepted && this.current === grant) {
          this.revoke();
          this.status('TX MIC · no active native grant');
        }
      })
      .catch(error => {
        if (this.current === grant) {
          this.revoke();
          this.status(String(error));
        }
      })
      .finally(() => { grant.pending--; });
  }
}
