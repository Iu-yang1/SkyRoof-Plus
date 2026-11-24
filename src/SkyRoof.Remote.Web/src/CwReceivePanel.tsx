import { useEffect, useState } from 'react';

type Track = { id: number; associationHintId: number; frequencyHz: number; snrDb: number; driftHzPerSecond: number;
  keying: string; active: boolean; confirmed: boolean };
type Transcript = { trackId: number; associationHintId: number; committedText: string;
  provisionalText: string; lastWindowEndUtc: string };
type CwState = { enabled?: boolean; mode?: string; state: string; sourceEpoch?: number;
  tracks?: Track[]; transcripts?: Transcript[]; inferenceBusy?: boolean;
  completedInferenceWindows?: number; skippedInferenceWindows?: number; lastError?: string };

export function CwReceivePanel({ available }: { available: boolean }) {
  const [state, setState] = useState<CwState>({ state: 'disabled' });
  const [mode, setMode] = useState('DeepCW');
  const [error, setError] = useState('');
  const [changing, setChanging] = useState(false);
  useEffect(() => {
    if (!available) return;
    const abort = new AbortController();
    let busy = false;
    async function poll() {
      if (busy || abort.signal.aborted) return;
      busy = true;
      try {
        const response = await fetch('/api/v1/dsp/cw', { credentials: 'include', signal: abort.signal });
        if (!response.ok) throw new Error('CW HTTP ' + response.status);
        const data = await response.json() as CwState;
        if (!abort.signal.aborted) { setState(data); setError(''); }
      } catch (e) { if (!abort.signal.aborted) setError(String(e)); }
      finally { busy = false; }
    }
    void poll();
    const timer = window.setInterval(() => { void poll(); }, 500);
    return () => { abort.abort(); window.clearInterval(timer); };
  }, [available]);

  async function configure(enabled: boolean) {
    setChanging(true);
    try {
      const response = await fetch('/api/v1/dsp/cw', { method: 'PUT', credentials: 'include',
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ enabled, mode }) });
      if (!response.ok) throw new Error('CW configuration HTTP ' + response.status);
      setState({ state: enabled ? 'waiting-for-audio' : 'disabled', enabled, mode });
      setError('');
    } catch (e) { setError(String(e)); }
    finally { setChanging(false); }
  }

  return <section className="cw-receive-panel">
    <header>CW · MULTI-LANE RECEIVE</header>
    <div className="audio-buttons">
      <select aria-label="CW decoder" value={mode} onChange={e => setMode(e.target.value)}>
        <option value="DeepCW">DeepCW / ONNX</option><option value="NewCW">New CW / HSMM</option>
      </select>
      <button disabled={!available || changing} onClick={() => void configure(true)}>START / APPLY</button>
      <button disabled={!available || changing} onClick={() => void configure(false)}>STOP CW</button>
    </div>
    <p>{available ? `CW · ${state.mode ?? mode} · ${state.state}` : 'CW · NO STATION LINK'}
      {state.inferenceBusy ? ' · DECODING' : ''}</p>
    {(error || state.lastError) && <p role="status">{error || state.lastError}</p>}
    {state.state === 'NotInstalled' && <small>DeepCW model is not installed on the DSP host. New CW works without a model.</small>}
    <div className="readout">Decoded windows {state.completedInferenceWindows ?? 0} · skipped {state.skippedInferenceWindows ?? 0}</div>
    <table><thead><tr><th>AF Hz</th><th>Peak dB</th><th>State</th><th>Transcript</th></tr></thead>
      <tbody>{(state.tracks ?? []).map(track => {
        const text = state.transcripts?.find(t => track.associationHintId ? t.associationHintId === track.associationHintId : t.trackId === track.id);
        return <tr key={track.id}>
          <td>{track.frequencyHz.toFixed(1)}</td><td>{track.snrDb.toFixed(1)}</td>
          <td>{track.keying}{track.active ? ' · RX' : ''}</td>
          <td>{text?.committedText}<i>{text?.provisionalText}</i></td>
        </tr>;
      })}</tbody></table>
    <small>Original CW tracker · eight lanes · provisional text in italics.</small>
  </section>;
}
