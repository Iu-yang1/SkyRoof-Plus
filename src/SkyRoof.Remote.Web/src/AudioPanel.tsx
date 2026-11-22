import { useContext, useEffect, useRef, useState } from 'react';
import { WorkspaceActiveContext } from './host';

type Playback = { context: AudioContext; socket: WebSocket; node: AudioWorkletNode };

export function AudioPanel() {
  const workspaceActive = useContext(WorkspaceActiveContext);
  const playback = useRef<Playback | null>(null);
  const [status, setStatus] = useState('RX AUDIO OFF');
  const [frames, setFrames] = useState(0);
  const [bufferedMs, setBufferedMs] = useState(0);
  const [underruns, setUnderruns] = useState(0);

  function stop() {
    const current = playback.current;
    playback.current = null;
    if (current) {
      current.socket.close();
      current.node.disconnect();
      void current.context.close();
    }
    setStatus('RX AUDIO OFF');
  }

  async function start() {
    if (playback.current || !workspaceActive) return;
    setStatus('CONNECTING...');
    let context: AudioContext | null = null;
    try {
      context = new AudioContext({ sampleRate: 48000, latencyHint: 'interactive' });
      await context.audioWorklet.addModule('/pcm-player.worklet.js');
      const node = new AudioWorkletNode(context, 'skyroof-pcm-player', {
        numberOfInputs: 0, numberOfOutputs: 1, outputChannelCount: [2]
      });
      node.connect(context.destination);
      const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
      const socket = new WebSocket(protocol + '//' + location.host + '/ws/audio');
      socket.binaryType = 'arraybuffer';
      playback.current = { context, node, socket };
      let count = 0;
      let lastSequence = -1n;
      let lastEpoch = -1;
      node.port.onmessage = (event: MessageEvent<{ kind: string;
        underruns: number; bufferedMilliseconds: number }>) => {
        if (event.data.kind !== 'statistics') return;
        setUnderruns(event.data.underruns);
        setBufferedMs(event.data.bufferedMilliseconds);
      };
      socket.onopen = () => setStatus('WSS PCM16 · 48 kHz');
      socket.onclose = () => setStatus('WSS DISCONNECTED');
      socket.onerror = () => setStatus('WSS ERROR');
      socket.onmessage = (event: MessageEvent<ArrayBuffer>) => {
        const view = new DataView(event.data);
        if (view.byteLength < 52 || view.getUint32(0, true) !== 0x31504153 ||
            view.getUint8(4) !== 1 || view.getUint8(5) !== 2) return;
        const epoch = view.getUint32(12, true);
        const sequence = view.getBigUint64(16, true);
        const rate = view.getUint32(40, true);
        const sampleFrames = view.getUint16(44, true);
        const channels = view.getUint8(46);
        const length = view.getUint32(48, true);
        if (rate !== 48000 || (channels !== 1 && channels !== 2) ||
            length !== sampleFrames * channels * 2 ||
            view.byteLength !== 52 + length) return;
        if (lastEpoch === epoch && sequence <= lastSequence) return;
        if (lastEpoch !== epoch) lastSequence = -1n;
        lastEpoch = epoch;
        lastSequence = sequence;
        const pcm = new Float32Array(sampleFrames * channels);
        for (let i = 0; i < pcm.length; ++i)
          pcm[i] = view.getInt16(52 + 2 * i, true) / 32768;
        node.port.postMessage({ kind: 'samples', samples: pcm,
          channels, epoch }, [pcm.buffer]);
        count++;
        if (count % 15 === 0) setFrames(count);
      };
      if (workspaceActive) await context.resume();
    } catch (error) {
      if (context) await context.close();
      playback.current?.socket.close();
      playback.current = null;
      setStatus(String(error));
    }
  }

  // Keep the source stream/identity alive while preventing non-active stations
  // from playing audio. Switching back resumes the same bounded Worklet.
  useEffect(() => {
    const current = playback.current;
    if (!current) return;
    if (workspaceActive) void current.context.resume();
    else void current.context.suspend();
  }, [workspaceActive]);

  useEffect(() => () => {
    const current = playback.current;
    playback.current = null;
    if (current) {
      current.socket.close();
      current.node.disconnect();
      void current.context.close();
    }
  }, []);

  return <section className="panel audio-panel">
    <header>RX AUDIO · RAW PCM16 WSS</header>
    <p>{status}</p>
    <div className="readout">48,000 samples/s · PCM16 · SAP/1</div>
    <div className="readout">Received {frames} frames · Buffer {bufferedMs} ms · Underruns {underruns}</div>
    <div className="audio-buttons">
      <button disabled={!workspaceActive} onClick={() => { void start(); }}>PLAY RX</button>
      <button onClick={stop}>STOP RX</button>
    </div>
    <small>独立 WSS 接收路径。当前无实际 IC-9700 RX PCM 输入；Voice Track/RTC Data 自动切换仍待 Media Service 集成。</small>
  </section>;
}
