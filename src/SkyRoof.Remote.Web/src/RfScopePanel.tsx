import { useContext, useEffect, useRef, useState } from 'react';
import { SpectrumPanel } from './SpectrumPanel';
import { DeviceHubContext, WorkspaceActiveContext } from './host';

export function RfScopePanel() {
  const [simulate, setSimulate] = useState(false);
  return <section className="panel scope">
    <header>RF SPECTRUM <button onClick={() => setSimulate(v => !v)}>
      {simulate ? 'LIVE' : 'SIM'}
    </button></header>
    {simulate ? <SpectrumPanel /> : <NativeRfScope />}
  </section>;
}

function NativeRfScope() {
  const canvas = useRef<HTMLCanvasElement>(null);
  const active = useContext(WorkspaceActiveContext);
  const hub = useContext(DeviceHubContext);
  const [band, setBand] = useState('main');
  const [running, setRunning] = useState(false);
  const [status, setStatus] = useState('No source');
  const [axis, setAxis] = useState('');
  useEffect(() => {
    if (!running || !active || !hub) return;
    const scheme = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const socket = new WebSocket(scheme + '//' + location.host + '/ws/rf-scope/' + band);
    socket.binaryType = 'arraybuffer';
    setStatus('Connecting');
    socket.onopen = () => setStatus('Waiting for RF sweep');
    socket.onerror = () => setStatus('RF scope unavailable');
    socket.onclose = () => setStatus('Disconnected');
    socket.onmessage = (event: MessageEvent<ArrayBuffer>) => {
      const frame = new DataView(event.data);
      if (frame.byteLength !== 499 || frame.getUint32(0, true) !== 0x31465253 ||
          frame.getUint8(4) !== (band === 'sub' ? 1 : 0)) return;
      const lower = Number(frame.getBigInt64(8, true));
      const upper = Number(frame.getBigInt64(16, true));
      setAxis((lower / 1e6).toFixed(6) + ' – ' + (upper / 1e6).toFixed(6) + ' MHz');
      setStatus(frame.getUint8(6) ? 'Out of range' : 'LIVE RF · raw scope magnitude');
      if (canvas.current) drawSweep(canvas.current, frame);
    };
    return () => { socket.onclose = null; socket.onerror = null; socket.onmessage = null; socket.close(); };
  }, [band, running, active, hub]);
  return <>
    <canvas ref={canvas} aria-label="Native RF spectrum" />
    <footer><span>{status} · {axis}</span>
      <select aria-label="RF receiver" value={band} onChange={e => setBand(e.target.value)}>
        <option value="main">MAIN</option><option value="sub">SUB</option>
      </select>
      <button disabled={!active || !hub} onClick={() => setRunning(v => !v)}>{running ? 'STOP' : 'RUN RF'}</button>
    </footer>
    <small>IC-9700 27 00 · 475 bins · raw 0–160, not calibrated dBm.</small>
  </>;
}

function drawSweep(canvas: HTMLCanvasElement, frame: DataView) {
  const context = canvas.getContext('2d');
  if (!context) return;
  canvas.width = Math.max(1, Math.round(canvas.clientWidth * devicePixelRatio));
  canvas.height = Math.max(1, Math.round(canvas.clientHeight * devicePixelRatio));
  context.fillStyle = '#08121c';
  context.fillRect(0, 0, canvas.width, canvas.height);
  if (frame.getUint8(6)) return;
  context.strokeStyle = '#59c2fa';
  context.beginPath();
  for (let i = 0; i < 475; i++) {
    const x = i * canvas.width / 474;
    const y = canvas.height * (1 - frame.getUint8(24 + i) / 160);
    if (i === 0) context.moveTo(x, y); else context.lineTo(x, y);
  }
  context.stroke();
}
