import { useEffect, useState } from 'react';
import { Q65AdvancedConfig } from './Q65AdvancedConfig';
import { Q65TransmitPanel } from './Q65TransmitPanel';

const modes = ['15A', '30A', '60A', '60B', '60C', '60D', '60E', '120D', '120E', '300A'];
export type Config = { enabled: boolean; submode: string; rxHz: number; toleranceHz: number; averaging: boolean; emeDelay: boolean; myCall: string; hisCall: string; hisGrid: string; apMode: number; qsoProgress: number; maxDrift: number; pileup: boolean; fadingB90Ts: number; lorentzian: boolean };
type Decode = { text: string; frequencyHz: number; dtSeconds: number; snrDb: number; flags: number; apType: number };
type Slot = { state: string; config: Config; utc?: string; messages?: Decode[]; error?: string };
const defaults: Config = { enabled: false, submode: '60B', rxHz: 1500, toleranceHz: 100, averaging: true, emeDelay: true, myCall: '', hisCall: '', hisGrid: '', apMode: 0, qsoProgress: 0, maxDrift: 0, pileup: false, fadingB90Ts: 0, lorentzian: false };

export function Q65ReceivePanel({ available }: { available: boolean }) {
  const [draft, setDraft] = useState<Config>(defaults);
  const [slots, setSlots] = useState<Slot[]>([]);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const latest = slots.at(-1);
  useEffect(() => {
    if (!available) { setSlots([]); return; }
    const abort = new AbortController();
    let pending = false;
    async function poll() {
      if (pending || abort.signal.aborted) return;
      pending = true;
      try {
        const response = await fetch('/api/v1/dsp/q65', { credentials: 'include', signal: abort.signal });
        if (!response.ok) throw new Error('Q65 HTTP ' + response.status);
        const data = await response.json() as { results: Slot[] };
        if (!abort.signal.aborted) { setSlots(data.results); setError(''); }
      } catch (e) { if (!abort.signal.aborted) setError(String(e)); }
      finally { pending = false; }
    }
    void poll();
    const timer = window.setInterval(() => void poll(), 1000);
    return () => { abort.abort(); window.clearInterval(timer); };
  }, [available]);

  async function configure(enabled: boolean) {
    setBusy(true);
    try {
      const response = await fetch('/api/v1/dsp/q65', { method: 'PUT', credentials: 'include',
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...draft, enabled }) });
      if (!response.ok) throw new Error(await response.text());
      const applied = await response.json() as Config;
      setDraft(applied); setSlots([{ state: enabled ? 'waiting-for-slot' : 'disabled', config: applied }]);
    } catch (e) { setError(String(e)); }
    finally { setBusy(false); }
  }

  return <section className="q65-receive-panel">
    <header>Q65 · EME RECEIVE</header>
    <label>SUBMODE <select value={draft.submode} onChange={e => setDraft({ ...draft, submode: e.target.value })}>
      {modes.map(mode => <option key={mode}>{mode}</option>)}</select></label>
    <label>RX AF Hz <input type="number" value={draft.rxHz} onChange={e => setDraft({ ...draft, rxHz: Number(e.target.value) })} /></label>
    <label>TOL Hz <input type="number" value={draft.toleranceHz} onChange={e => setDraft({ ...draft, toleranceHz: Number(e.target.value) })} /></label>
    <label><input type="checkbox" checked={draft.averaging} onChange={e => setDraft({ ...draft, averaging: e.target.checked })} /> AVG</label>
    <label><input type="checkbox" checked={draft.emeDelay} onChange={e => setDraft({ ...draft, emeDelay: e.target.checked })} /> EME DELAY</label>
    <Q65AdvancedConfig config={draft} onChange={setDraft} />
    <button disabled={!available || busy} onClick={() => void configure(true)}>START / APPLY</button>
    <button disabled={!available || busy} onClick={() => void configure(false)}>STOP / CLEAR AVG</button>
    <p role="status">{available ? 'Q65 · ' + (latest?.config.submode ?? draft.submode) + ' · ' + (latest?.state ?? 'waiting') : 'NO STATION LINK'}</p>
    {(error || latest?.error) && <p role="alert">{error || latest?.error}</p>}
    <table><thead><tr><th>UTC</th><th>dB</th><th>DT</th><th>Hz</th><th>AP</th><th>MESSAGE</th></tr></thead>
      <tbody>{slots.slice().reverse().flatMap(slot => (slot.messages ?? []).map((row, i) =>
        <tr key={(slot.utc ?? '') + ':' + i}><td>{slot.utc?.slice(11, 19)}</td><td>{row.snrDb.toFixed(0)}</td>
          <td>{row.dtSeconds.toFixed(2)}</td><td>{row.frequencyHz.toFixed(1)}</td><td>{row.apType || '—'}</td><td>{row.text}</td></tr>))}</tbody></table>
    <Q65TransmitPanel available={available} submode={draft.submode} rxHz={draft.rxHz} />
    <small>Station UTC slots · native decoder · estimated input timing. Requires mfsk.dll ABI v3.</small>
  </section>;
}
