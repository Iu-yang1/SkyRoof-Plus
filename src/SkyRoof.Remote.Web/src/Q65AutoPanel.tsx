import { useEffect, useRef, useState } from 'react';
import { readFt4Events, type Ft4AutoEvent } from './Ft4AutoEvents';
import type { Config } from './Q65ReceivePanel';

type Event = Ft4AutoEvent & { slotSeconds?: number };

export function Q65AutoPanel({ available, config, onRunning }: { available: boolean; config: Config; onRunning: (running: boolean) => void }) {
  const [call, setCall] = useState('');
  const [grid, setGrid] = useState('');
  const [target, setTarget] = useState('');
  const [mhz, setMhz] = useState('');
  const [gain, setGain] = useState(50);
  const [odd, setOdd] = useState(false);
  const [attempts, setAttempts] = useState(4);
  const [event, setEvent] = useState<Event>({ state: 'idle' });
  const [running, setRunning] = useState(false);
  const active = useRef<AbortController | null>(null);
  useEffect(() => () => active.current?.abort(), []);
  useEffect(() => { if (!available) active.current?.abort(); }, [available]);

  async function start() {
    if (!available || active.current) return;
    const frequencyHz = Math.round(Number(mhz) * 1e6);
    if (!call.trim() || !grid.trim() || !(frequencyHz > 0) || !Number.isInteger(attempts) || attempts < 1) {
      setEvent({ state: 'error', error: 'Enter MY CALL, grid, TX MHz and positive attempts.' });
      return;
    }
    const controller = new AbortController();
    active.current = controller; setRunning(true); onRunning(true);
    setEvent({ state: 'connecting' });
    try {
      const response = await fetch('/api/v1/dsp/q65/auto', {
        method: 'POST', credentials: 'include', signal: controller.signal,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ call, grid, target, frequencyHz, gain: gain / 100, odd, attempts, rx: config })
      });
      if (!response.ok || !response.body) throw new Error(await response.text() || 'Q65 HTTP ' + response.status);
      await readFt4Events(response.body, setEvent);
    } catch (err) {
      setEvent(controller.signal.aborted ? { state: 'cancel-requested', error: 'Check CAT RX.' } : { state: 'error', error: String(err) });
    } finally {
      active.current = null; setRunning(false); onRunning(false);
    }
  }

  return <form onSubmit={e => { e.preventDefault(); void start(); }}>
    <header>Q65 AUTO QSO</header>
    <fieldset disabled={running}>
      <label>MY CALL <input value={call} onChange={e => setCall(e.target.value.toUpperCase())} /></label>
      <label>MY GRID <input value={grid} onChange={e => setGrid(e.target.value.toUpperCase())} /></label>
      <label>DX CALL (blank = CQ) <input value={target} onChange={e => setTarget(e.target.value.toUpperCase())} /></label>
      <label>TX MHz <input inputMode="decimal" value={mhz} onChange={e => setMhz(e.target.value)} /></label>
      <label>PCM LEVEL % <input type="number" min="0" max="100" value={gain} onChange={e => setGain(Number(e.target.value))} /></label>
      <label>TX SLOT <select value={odd ? 'odd' : 'even'} onChange={e => setOdd(e.target.value === 'odd')}><option value="even">EVEN</option><option value="odd">ODD</option></select></label>
      <label>ATTEMPTS <input type="number" min="1" value={attempts} onChange={e => setAttempts(Number(e.target.value))} /></label>
    </fieldset>
    <button disabled={!available || running} type="submit">START AUTO QSO</button>
    <button disabled={!running} type="button" onClick={() => active.current?.abort()}>STOP AUTO QSO</button>
    <p role="status">Q65 AUTO · {event.state} · {event.stage ?? ''} {event.peer ?? ''}</p>
    {event.slotIndex != null && Boolean(event.slotSeconds) && <p>SLOT {new Date(event.slotIndex * (event.slotSeconds ?? 0) * 1000).toISOString().slice(11, 19)} UTC</p>}
    {event.message && <p>{event.message}</p>}
    {event.error && <p role="alert">{event.error}</p>}
    <small>Complete native RX slots; if decoding misses TX preparation time the next same-parity slot is selected. Standard calls only.</small>
  </form>;
}
