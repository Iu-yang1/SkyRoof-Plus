import { useContext, useEffect, useState } from 'react';
import { DeviceHubContext, WorkspaceActiveContext } from './host';
import { telemetryProfiles, type TelemetryConfig } from './TelemetryProfiles';
import { TelemetryProfileEditor } from './TelemetryProfileEditor';

type Frame = { state: string; config?: TelemetryConfig; utc?: string; generation?: number; length?: number;
  hex?: string; crcValid?: boolean | null; snrDb?: number | null; cfoHz?: number | null;
  outputs?: { archiveSaved: number; archiveErrors: number; kissPort: number; kissClients: number; kissError: string };
  fields?: { name: string; value: string }[]; framing?: string; note?: string; error?: string };

export function TelemetryPanel() {
  const hub = useContext(DeviceHubContext);
  const active = useContext(WorkspaceActiveContext);
  const available = Boolean(hub && active);
  const [config, setConfig] = useState<TelemetryConfig>(telemetryProfiles['AO-73']);
  const [results, setResults] = useState<Frame[]>([]);
  const [selected, setSelected] = useState<Frame>();
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (!available) return;
    const abort = new AbortController();
    let pending = false;
    async function poll() {
      if (pending || abort.signal.aborted) return;
      pending = true;
      try {
        const response = await fetch('/api/v1/dsp/telemetry', { credentials: 'include', signal: abort.signal });
        if (!response.ok) throw new Error('Telemetry HTTP ' + response.status);
        const data = await response.json() as { results: Frame[] };
        if (!abort.signal.aborted) setResults(data.results);
      } catch (e) { if (!abort.signal.aborted) setError(String(e)); }
      finally { pending = false; }
    }
    void poll();
    const timer = window.setInterval(() => void poll(), 1000);
    return () => { abort.abort(); window.clearInterval(timer); };
  }, [available]);

  async function apply(enabled: boolean) {
    setBusy(true); setError('');
    try {
      const response = await fetch('/api/v1/dsp/telemetry', { method: 'PUT', credentials: 'include',
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...config, enabled }) });
      if (!response.ok) throw new Error(await response.text());
      setConfig(await response.json() as TelemetryConfig);
    } catch (e) { setError(String(e)); }
    finally { setBusy(false); }
  }

  const latest = results.at(-1);
  const frames = results.filter(f => f.state === 'frame').reverse();
  const detail = selected ?? frames[0];
  return <section className="panel">
    <header>TELEMETRY</header>
    <TelemetryProfileEditor config={config} onChange={setConfig} />
    <button disabled={!available || busy} onClick={() => void apply(true)}>START / APPLY</button>
    <button disabled={!available || busy} onClick={() => void apply(false)}>STOP / FLUSH</button>
    <p role="status">{available ? latest?.state ?? 'WAITING' : 'NO STATION LINK'}</p>
    {latest?.outputs && <p>ARCHIVE {latest.outputs.archiveSaved} SAVED / {latest.outputs.archiveErrors} ERRORS · KISS {latest.outputs.kissPort || 'OFF'} · {latest.outputs.kissClients} CLIENTS {latest.outputs.kissError}</p>}
    {(error || latest?.error) && <p role="alert">{error || latest?.error}</p>}
    <div className="digital-messages">{frames.map((frame, i) =>
      <button key={frame.utc + ':' + i} onClick={() => setSelected(frame)}>
        {frame.utc?.slice(11, 19)} · {frame.config?.satellite} · {frame.length} B · {frame.crcValid === true ? 'CRC/FEC OK' : frame.crcValid === false ? 'FAIL' : 'UNVERIFIED'}
      </button>)}</div>
    {detail && <><p>{detail.config?.satellite} · {detail.framing} · {detail.snrDb?.toFixed(1)} dB · CFO {detail.cfoHz?.toFixed(1)} Hz</p>
      <p>{detail.note}</p><table><tbody>{detail.fields?.map((field, i) => <tr key={i}><th>{field.name}</th><td>{field.value}</td></tr>)}</tbody></table>
      <textarea readOnly value={detail.hex ?? ''} rows={5} aria-label="Decoded payload HEX" /></>}
  </section>;
}
