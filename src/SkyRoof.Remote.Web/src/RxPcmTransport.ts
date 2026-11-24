// One active audio transport. Closing the RTC signaling socket also revokes
// its server-side peer; stale RTC callbacks cannot feed the WSS timeline.
export class RxPcmTransport {
  private rtc: RTCPeerConnection | null = null;
  private signaling: WebSocket | null = null;
  private wss: WebSocket | null = null;
  private timer: number | null = null;
  private stopped = false;
  private fallbackStarted = false;

  constructor(private status: (message: string) => void,
    private disconnected: () => void, private packet: (buffer: ArrayBuffer) => void) {}

  start() {
    this.status('RTC DATA · CONNECTING');
    void this.connectRtc().catch(() => this.fallback());
  }

  private url(path: string) {
    return (location.protocol === 'https:' ? 'wss:' : 'ws:') + '//' + location.host + path;
  }

  private async connectRtc() {
    const peer = new RTCPeerConnection();
    this.rtc = peer;
    const channel = peer.createDataChannel('skyroof-rx-pcm', { ordered: false, maxRetransmits: 0 });
    channel.binaryType = 'arraybuffer';
    channel.onmessage = event => {
      if (this.rtc !== peer || this.stopped || !(event.data instanceof ArrayBuffer)) return;
      this.status('RTC DATA · PCM16 · 48 kHz');
      this.arm(3000);
      this.packet(event.data);
    };
    channel.onclose = () => { if (this.rtc === peer) this.fallback(); };
    peer.onconnectionstatechange = () => {
      if (this.rtc === peer && ['failed', 'closed', 'disconnected'].includes(peer.connectionState))
        this.fallback();
    };
    this.arm(12000);
    await peer.setLocalDescription(await peer.createOffer());
    await this.waitForGathering(peer);
    if (this.rtc !== peer || this.stopped) return;
    const offer = peer.localDescription?.sdp;
    if (!offer) throw new Error('RTC offer missing');
    const signal = new WebSocket(this.url('/ws/rtc-rx'));
    this.signaling = signal;
    signal.onopen = () => signal.send(offer);
    signal.onmessage = event => {
      if (this.rtc !== peer || typeof event.data !== 'string') return;
      void peer.setRemoteDescription({ type: 'answer', sdp: event.data }).catch(() => this.fallback());
    };
    signal.onclose = signal.onerror = () => { if (this.rtc === peer) this.fallback(); };
  }

  private waitForGathering(peer: RTCPeerConnection) {
    if (peer.iceGatheringState === 'complete') return Promise.resolve();
    return new Promise<void>(resolve => {
      const finish = () => {
        window.clearTimeout(timeout);
        peer.removeEventListener('icegatheringstatechange', changed);
        resolve();
      };
      const changed = () => { if (peer.iceGatheringState === 'complete') finish(); };
      const timeout = window.setTimeout(finish, 2500);
      peer.addEventListener('icegatheringstatechange', changed);
    });
  }

  private arm(ms: number) {
    if (this.timer !== null) window.clearTimeout(this.timer);
    this.timer = window.setTimeout(() => this.fallback(), ms);
  }

  private closeRtc() {
    if (this.timer !== null) window.clearTimeout(this.timer);
    this.timer = null;
    const peer = this.rtc;
    this.rtc = null;
    peer?.close();
    this.signaling?.close();
    this.signaling = null;
  }

  private fallback() {
    if (this.stopped || this.fallbackStarted) return;
    this.fallbackStarted = true;
    this.closeRtc();
    this.status('RTC unavailable · WSS fallback');
    const socket = new WebSocket(this.url('/ws/audio'));
    this.wss = socket;
    socket.binaryType = 'arraybuffer';
    socket.onopen = () => { if (!this.stopped) this.status('WSS PCM16 · 48 kHz'); };
    socket.onmessage = event => { if (!this.stopped && event.data instanceof ArrayBuffer) this.packet(event.data); };
    socket.onclose = () => { if (!this.stopped) this.disconnected(); };
    socket.onerror = () => { if (!this.stopped) this.status('WSS ERROR'); };
  }

  close() {
    this.stopped = true;
    this.closeRtc();
    this.wss?.close();
    this.wss = null;
  }
}
