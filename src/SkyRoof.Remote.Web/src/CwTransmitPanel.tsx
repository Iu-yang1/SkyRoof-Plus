import { useEffect, useRef, useState } from 'react';
import { useCwMacros } from './CwMacros';

type Status = { lease: string; mode: string; breakIn: number; transmitting: boolean; wpm: number; actualTxFrequencyHz: number };

export function CwTransmitPanel({ available }: { available: boolean }) {
  const [status, setStatus] = useState<Status | null>(null);
  const [wpm, setWpm] = useState('20');
  const [message, setMessage] = useState('');
  const [feedback, setFeedback] = useState('CW TX · IDLE');
  const [sending, setSending] = useState(false);
  const [editing, setEditing] = useState<number | null>(null);
  const { macros, edit } = useCwMacros();
  const current = useRef<AbortController | null>(null);

  useEffect(() => () => current.current?.abort(), []);
  useEffect(() => { if (!available) { current.current?.abort(); setStatus(null); } }, [available]);

  async function read(speed?: number) {
    if (!available) return;
    try {
      const response = await fetch('/api/v1/device/cw/' + (speed === undefined ? 'status' : 'speed'), {
        credentials: 'include', ...(speed === undefined ? {} : { method: 'PUT',
          headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ wpm: speed }) })
      });
      if (!response.ok) throw new Error(await response.text());
      const result = await response.json() as Status;
      setStatus(result); setWpm(result.wpm.toFixed(1)); setFeedback('CW STATUS · ' + result.lease);
    } catch (e) { setFeedback(String(e)); }
  }

  async function send(value: string) {
    if (!available || current.current) return;
    const text = value.trim().toUpperCase();
    if (!text) { setFeedback('Enter CW text.'); return; }
    const controller = new AbortController();
    current.current = controller;
    setSending(true); setFeedback('CW TX · ' + text);
    try {
      const response = await fetch('/api/v1/device/cw/send', {
        method: 'POST', credentials: 'include', signal: controller.signal,
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text })
      });
      if (!response.ok) throw new Error(await response.text());
      const result = await response.json() as { state: string };
      setFeedback('CW · ' + result.state);
    } catch (e) {
      setFeedback(controller.signal.aborted ? 'STOP REQUESTED · CHECK RX' : String(e));
    } finally { current.current = null; setSending(false); }
  }

  function macro(index: number, transmit: boolean) {
    setMessage(macros[index]);
    if (transmit) void send(macros[index]);
  }

  return <section className="cw-transmit-panel" tabIndex={0} onKeyDown={e => {
    if (!available || !/^F[1-8]$/.test(e.key)) return;
    e.preventDefault(); macro(Number(e.key.slice(1)) - 1, e.shiftKey);
  }}>
    <header>CW · TRANSMIT</header>
    <label>KEY SPEED WPM <input type="number" min="6" max="48" step="0.1" value={wpm}
      onChange={e => setWpm(e.target.value)} /></label>
    <button disabled={!available || sending} onClick={() => void read()}>READ CW STATUS</button>
    <button disabled={!available || sending} onClick={() => void read(Number(wpm))}>SET WPM</button>
    {status && <p>{status.mode} · BK-IN {['OFF','SEMI','FULL'][status.breakIn]} · {status.wpm.toFixed(1)} WPM · {status.actualTxFrequencyHz || '—'} Hz</p>}
    <label>CW TEXT <input maxLength={30} value={message} onChange={e => setMessage(e.target.value.toUpperCase())} /></label>
    <button disabled={!available || sending} onClick={() => void send(message)}>SEND CW</button>
    <button disabled={!sending} onClick={() => current.current?.abort()}>STOP CW TX</button>
    <div className="audio-buttons">{macros.map((m, i) =>
      <button key={i} title={m || 'Right click to edit'} disabled={!available}
        onContextMenu={e => { e.preventDefault(); setEditing(i); }}
        onClick={e => macro(i, e.shiftKey)}>F{i + 1} {m.slice(0, 10)}</button>
    )}</div>
    {editing !== null && <label>EDIT F{editing + 1} <input maxLength={30} value={macros[editing]}
      onChange={e => edit(editing, e.target.value.toUpperCase())} /><button onClick={() => setEditing(null)}>DONE</button></label>}
    <p role="status">{feedback}</p>
    <small>SkyCAT hardware keyer · F1–F8 load, Shift sends · presets saved in this browser.</small>
  </section>;
}
