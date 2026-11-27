import { useContext, useEffect, useRef, useState } from 'react';
import { DeviceHubContext, WorkspaceActiveContext } from './host';
import { useMediaRxStatus } from './useMediaRxStatus';
import { PcmTimeline } from './PcmTimeline';
import { RxPcmTransport } from './RxPcmTransport';

type Playback = { context: AudioContext; socket: RxPcmTransport; node: AudioWorkletNode; voice?: MediaStreamAudioSourceNode };
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
      current.voice?.disconnect();
      void current.context.close();
    }
  }

  function stop() {
    disposePlayback();
    setStatus('RX AUDIO OFF');
  }

  async function start(voice = false) {
    if (!workspaceActive || !hub) return;
    if (playback.current || starting.current) disposePlayback();
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
      let count = 0;
      timeline.current.reset();
      node.port.onmessage = (event: MessageEvent<{ kind: string;
        underruns: number; bufferedMilliseconds: number }>) => {
        if (event.data.kind !== 'statistics') return;
        setUnderruns(event.data.underruns);
        setBufferedMs(event.data.bufferedMilliseconds);
      };
      const socket = new RxPcmTransport(message => {
        if (attempt === generation.current) setStatus(message);
      }, () => {
        if (attempt !== generation.current) return;
        stop();
        setStatus('RX DISCONNECTED · PLAY RX to reconnect');
      }, buffer => {
        if (attempt !== generation.current || !active.current) return;
        const packet = timeline.current.accept(buffer);
        if (!packet) return;
        if (packet.discontinuity) node.port.postMessage({ kind: 'reset' });
        node.port.postMessage({ kind: 'samples', ...packet }, [packet.samples.buffer]);
        count++;
        if (count % 15 === 0) setFrames(count);
      }, stream => {
        const current = playback.current;
        if (!current || attempt !== generation.current) return;
        current.voice?.disconnect();
        current.voice = undefined;
        if (stream) {
          current.voice = current.context.createMediaStreamSource(stream);
          current.voice.connect(current.context.destination);
        }
      });
      playback.current = { context, node, socket };
      socket.start(voice);
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
    <header>RX AUDIO · PCM / OPUS</header>
    <p>{status}</p>
    <div className="readout">48,000 samples/s · PCM16 · SAP/1</div>
    <div className="readout">Received {frames} frames · Buffer {bufferedMs} ms · Underruns {underruns}</div>
    <div className="readout">{mediaMessage}{media ? ` · ${media.source || 'pcm16'} · Epoch ${media.epoch}` : ''}</div>
    {media && <div className="readout">Source frames {media.framesWritten} · Missing samples {media.samplesSkipped}
      · UDP RX {media.datagramsReceived} · Out-of-order {media.datagramsOutOfOrder}
      · Ignored {media.datagramsDropped} · Missing UDP {media.datagramsMissing}</div>}
    <div className="audio-buttons">
      <button disabled={!workspaceActive || !hub} onClick={() => { void start(); }}>PLAY RX</button>
      <button disabled={!workspaceActive || !hub} onClick={() => { void start(true); }}>PLAY VOICE</button>
      <button onClick={stop}>STOP RX</button>
    </div>
    <small>PLAY RX 使用无损 PCM；PLAY VOICE 使用 Opus，失败时回退 PCM WSS。公网 ICE/FRP 仍需验收。</small>
  </section>;
}
