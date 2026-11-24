import { useContext, useEffect, useRef, useState } from 'react';
import { DeviceHubContext, WorkspaceActiveContext } from './host';
import { useMediaRxStatus } from './useMediaRxStatus';
import { PcmTimeline } from './PcmTimeline';

type Playback = { context: AudioContext; socket: WebSocket; node: AudioWorkletNode };
export function AudioPanel() {
  const workspaceActive = useContext(WorkspaceActiveContext);
  const hub = useContext(DeviceHubContext);
  const playback = useRef<Playback | null>(null);
  const starting = useRef(false);
  const generation = useRef(0);
  const active = useRef(workspaceActive);
  active.current = workspaceActive;
  const timeline = useRef(new PcmTimeline());
  const [status, setStatus] = useState('RX AUDIO OFF');
  const [frames, setFrames] = useState(0);
  const [bufferedMs, setBufferedMs] = useState(0);
  const [underruns, setUnderruns] = useState(0);
  const { media, mediaMessage } = useMediaRxStatus(hub);

  function disposePlayback() {
    generation.current++;
    starting.current = false;
    timeline.current.reset();
    const current = playback.current;
    playback.current = null;
    if (current) {
      current.socket.close();
      current.node.disconnect();
      void current.context.close();
    }
  }

  function stop() {
    disposePlayback();
    setStatus('RX AUDIO OFF');
  }

  async function start() {
    if (playback.current || starting.current || !workspaceActive || !hub) return;
    starting.current = true;
    const attempt = ++generation.current;
    setStatus('CONNECTING...');
    let context: AudioContext | null = null;
    try {
      context = new AudioContext({ sampleRate: 48000, latencyHint: 'interactive' });
      await context.audioWorklet.addModule('/pcm-player.worklet.js');
      if (attempt !== generation.current) { await context.close(); return; }
      const node = new AudioWorkletNode(context, 'skyroof-pcm-player', {
        numberOfInputs: 0, numberOfOutputs: 1, outputChannelCount: [2]
      });
      node.connect(context.destination);
      const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
      const socket = new WebSocket(protocol + '//' + location.host + '/ws/audio');
      socket.binaryType = 'arraybuffer';
      playback.current = { context, node, socket };
      let count = 0;
      timeline.current.reset();
      node.port.onmessage = (event: MessageEvent<{ kind: string;
        underruns: number; bufferedMilliseconds: number }>) => {
        if (event.data.kind !== 'statistics') return;
        setUnderruns(event.data.underruns);
        setBufferedMs(event.data.bufferedMilliseconds);
      };
      socket.onopen = () => { if (attempt === generation.current) setStatus('WSS PCM16 · 48 kHz'); };
      socket.onclose = () => {
        if (attempt !== generation.current) return;
        stop();
        setStatus('WSS DISCONNECTED · PLAY RX to reconnect');
      };
      socket.onerror = () => { if (attempt === generation.current) setStatus('WSS ERROR'); };
      socket.onmessage = (event: MessageEvent<ArrayBuffer>) => {
        if (attempt !== generation.current || !active.current) return;
        const packet = timeline.current.accept(event.data);
        if (!packet) return;
        if (packet.discontinuity) node.port.postMessage({ kind: 'reset' });
        node.port.postMessage({ kind: 'samples', ...packet }, [packet.samples.buffer]);
        count++;
        if (count % 15 === 0) setFrames(count);
      };
      if (active.current) await context.resume();
      else await context.suspend();
    } catch (error) {
      if (attempt === generation.current) {
        const installed = playback.current?.context === context;
        stop();
        setStatus(String(error));
        if (!installed && context && context.state !== 'closed') await context.close();
      } else if (context && context.state !== 'closed') {
        await context.close().catch(() => undefined);
      }
    } finally {
      if (attempt === generation.current) starting.current = false;
    }
  }

  // Keep the source stream/identity alive while preventing non-active stations
  // from playing audio. Switching back resumes the same bounded Worklet.
  useEffect(() => {
    const current = playback.current;
    if (!current) return;
    timeline.current.reset();
    current.node.port.postMessage({ kind: 'reset' });
    if (workspaceActive) void current.context.resume();
    else void current.context.suspend();
  }, [workspaceActive]);

  useEffect(() => () => disposePlayback(), []);

  return <section className="panel audio-panel">
    <header>RX AUDIO · RAW PCM16 WSS</header>
    <p>{status}</p>
    <div className="readout">48,000 samples/s · PCM16 · SAP/1</div>
    <div className="readout">Received {frames} frames · Buffer {bufferedMs} ms · Underruns {underruns}</div>
    <div className="readout">{mediaMessage}{media ? ` · ${media.source || 'pcm16'} · Epoch ${media.epoch}` : ''}</div>
    {media && <div className="readout">Source frames {media.framesWritten} · Missing samples {media.samplesSkipped}
      · UDP RX {media.datagramsReceived} · Out-of-order {media.datagramsOutOfOrder}
      · Ignored {media.datagramsDropped} · Missing UDP {media.datagramsMissing}</div>}
    <div className="audio-buttons">
      <button disabled={!workspaceActive || !hub} onClick={() => { void start(); }}>PLAY RX</button>
      <button onClick={stop}>STOP RX</button>
    </div>
    <small>独立 WSS 接收路径。MEDIA 状态只说明站内接收进程，并非 IC-9700 实机认证或音频链路验收；Voice Track/RTC Data 自动切换仍待集成。</small>
  </section>;
}
