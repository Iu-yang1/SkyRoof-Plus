import { useState } from 'react';
import type { HubConnection } from '@microsoft/signalr';

const fields = [
  ['TransponderOffsetHz', 'POSITION'], ['RitHz', 'RIT'], ['XitHz', 'XIT'],
  ['DownlinkManualHz', 'RX MANUAL'], ['UplinkManualHz', 'TX MANUAL'],
  ['DownlinkBaseOffsetHz', 'RX BASE Δ'], ['UplinkBaseOffsetHz', 'TX BASE Δ']
] as const;

export type ActiveOffsets = Partial<Record<(typeof fields)[number][0], number>>;

export function LiveRadioLinkTune({ hub, jobId, available, active }: { hub: HubConnection | null; jobId: string; available: boolean; active: ActiveOffsets }) {
  const [values, setValues] = useState<Record<string, string>>({});
  const [status, setStatus] = useState('');
  const [busy, setBusy] = useState(false);
  async function apply() {
    if (!hub || busy || !available) return;
    setBusy(true);
    try {
      const patch: Record<string, string | number> = { JobId: jobId };
      for (const [key] of fields) {
        const value = values[key]?.trim();
        if (!value) continue;
        const number = Number(value);
        if (!Number.isFinite(number)) throw new Error(key + ' must be numeric.');
        patch[key] = number;
      }
      const result = await hub.invoke<{ JobId: string; Phase: string }>('TuneTrackingJob', patch);
      setStatus(result.JobId + ' · ' + result.Phase);
      setValues({});
    } catch (error) { setStatus(String(error)); }
    finally { setBusy(false); }
  }
  return <details><summary>LIVE RADIO LINK TUNING</summary>
    <small>Hz; leave a field blank to keep its existing offset. Changes apply to the active satellite job.</small>
    <div className="tracking-grid">{fields.map(([key, label]) => <label key={key}>{label} · {active[key] ?? 0} Hz
      <input type="number" step={key.includes('BaseOffset') ? '1' : 'any'} placeholder={String(active[key] ?? 0)} value={values[key] ?? ''}
        onChange={e => setValues(old => ({ ...old, [key]: e.target.value }))} />
    </label>)}</div>
    <button type="button" disabled={!available || busy} onClick={() => void apply()}>UPDATE ACTIVE LINK</button>
    <small role="status">{status}</small>
  </details>;
}
