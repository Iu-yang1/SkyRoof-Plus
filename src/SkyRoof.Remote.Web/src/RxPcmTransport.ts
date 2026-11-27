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
    private disconnected: () => void, private packet: (buffer: ArrayBuffer) => void,
    private voiceStream?: (stream: MediaStream | null) => void) {}

  start(voice = false) {
    this.status(voice ? 'RTC VOICE · CONNECTING' : 'RTC DATA · CONNECTING');
    void this.connectRtc(voice).catch(() => this.fallback());
  }

  private url(path: string) {
    return (location.protocol === 'https:' ? 'wss:' : 'ws:') + '//' + location.host + path;
  }

  private async connectRtc(voice: boolean) {
    const peer = new RTCPeerConnection();
    this.rtc = peer;
    if (voice) this.configureVoice(peer);
    else this.configureData(peer);
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
    const signal = new WebSocket(this.url(voice ? '/ws/rtc-voice' : '/ws/rtc-rx'));
    this.signaling = signal;
    signal.onopen = () => signal.send(offer);
    signal.onmessage = event => {
      if (this.rtc !== peer || typeof event.data !== 'string') return;
      void peer.setRemoteDescription({ type: 'answer', sdp: event.data }).catch(() => this.fallback());
    };
    signal.onclose = signal.onerror = () => { if (this.rtc === peer) this.fallback(); };
  }

  private configureVoice(peer: RTCPeerConnection) {
    peer.addTransceiver('audio', { direction: 'recvonly' });
    peer.ontrack = event => {
      if (this.rtc !== peer || this.stopped) return;
      const stream = event.streams[0] ?? new MediaStream([event.track]);
      event.track.onunmute = () => {
        if (this.rtc !== peer || this.stopped) return;
        this.status('RTC VOICE · Opus 48 kHz');
        if (this.timer !== null) window.clearTimeout(this.timer);
        this.timer = null;
      };
      event.track.onended = () => { if (this.rtc === peer) this.fallback(); };
      event.track.onmute = () => { if (this.rtc === peer) this.arm(3000); };
      this.voiceStream?.(stream);
      if (!event.track.muted) {
        this.status('RTC VOICE · Opus 48 kHz');
        if (this.timer !== null) window.clearTimeout(this.timer);
        this.timer = null;
      }
    };
  }

  private configureData(peer: RTCPeerConnection) {
    const channel = peer.createDataChannel('skyroof-rx-pcm', { ordered: false, maxRetransmits: 0 });
    channel.binaryType = 'arraybuffer';
    channel.onmessage = event => {
      if (this.rtc !== peer || this.stopped || !(event.data instanceof ArrayBuffer)) return;
      this.status('RTC DATA · PCM16 · 48 kHz');
      this.arm(3000);
      this.packet(event.data);
    };
    channel.onclose = () => { if (this.rtc === peer) this.fallback(); };
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
    this.voiceStream?.(null);
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
