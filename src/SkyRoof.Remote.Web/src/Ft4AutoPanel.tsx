import { useEffect, useRef, useState } from 'react';
import { readFt4Events, type Ft4AutoEvent } from './Ft4AutoEvents';

type Radio = { frequencyHz: number; audioHz: number; gain: number; odd: boolean };
type Props = { available: boolean; radio: Radio; onRunning: (running: boolean) => void };

export function Ft4AutoPanel({ available, radio, onRunning }: Props) {
  const [call, setCall] = useState('');
  const [grid, setGrid] = useState('');
  const [target, setTarget] = useState('');
  const [attempts, setAttempts] = useState('4');
  const [event, setEvent] = useState<Ft4AutoEvent>({ state: 'idle' });
  const [running, setRunning] = useState(false);
  const current = useRef<AbortController | null>(null);
  useEffect(() => () => current.current?.abort(), []);
  useEffect(() => { if (!available) current.current?.abort(); }, [available]);

  async function start() {
    if (!available || current.current) return;
    if (!call.trim() || !grid.trim() || !Number.isFinite(radio.frequencyHz) || radio.frequencyHz <= 0 ||
        !Number.isInteger(Number(attempts)) || Number(attempts) < 1) {
      setEvent({ state: 'error', error: 'Enter callsign, grid, TX MHz and positive retry count.' });
      return;
    }
    const controller = new AbortController();
    current.current = controller;
    setRunning(true);
    onRunning(true);
    setEvent({ state: 'connecting' });
    try {
      const response = await fetch('/api/v1/dsp/ft4/auto', {
        method: 'POST', credentials: 'include', signal: controller.signal,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...radio, call, grid, target, attempts: Number(attempts) })
      });
      if (!response.ok) throw new Error(await response.text() || 'FT4 HTTP ' + response.status);
      if (!response.body) throw new Error('Missing FT4 session stream.');
      await readFt4Events(response.body, setEvent);
    } catch (error) {
      setEvent(controller.signal.aborted
        ? { state: 'cancel-requested', error: 'Check CAT RX.' }
        : { state: 'error', error: String(error) });
    } finally {
      current.current = null;
      setRunning(false);
      onRunning(false);
    }
  }

  return <form onSubmit={e => { e.preventDefault(); void start(); }}>
    <header>FT4 AUTO QSO</header>
    <label>MY CALL <input value={call} onChange={e => setCall(e.target.value.toUpperCase())} disabled={running} /></label>
    <label>MY GRID <input value={grid} onChange={e => setGrid(e.target.value.toUpperCase())} disabled={running} /></label>
    <label>DX CALL (blank = CQ) <input value={target} onChange={e => setTarget(e.target.value.toUpperCase())} disabled={running} /></label>
    <label>ATTEMPTS PER STAGE <input type="number" min="1" value={attempts} onChange={e => setAttempts(e.target.value)} disabled={running} /></label>
    <button type="submit" disabled={!available || running}>START AUTO QSO</button>
    <button type="button" disabled={!running} onClick={() => current.current?.abort()}>STOP AUTO QSO</button>
    <p role="status">AUTO QSO · {event.state}{event.stage ? ' · ' + event.stage : ''}{event.peer ? ' · ' + event.peer : ''}</p>
    {event.slotIndex != null && <p>SLOT {new Date(event.slotIndex * 7500).toISOString().slice(11, 23)} UTC</p>}
    {event.message && <p>{event.message}</p>}
    {(event.reportSent != null || event.reportReceived != null) && <p>REPORT TX {event.reportSent ?? '—'} / RX {event.reportReceived ?? '—'}</p>}
    {event.error && <p>{event.error}</p>}
    <small>Uses the TX MHz, AF, PCM level and slot above. Standard calls only; manual messages remain available. No automatic log submission.</small>
  </form>;
}
