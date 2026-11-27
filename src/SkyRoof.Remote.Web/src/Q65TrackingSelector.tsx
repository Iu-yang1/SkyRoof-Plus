import { useContext, useState } from 'react';
import { DeviceHubContext } from './host';

type MoonJob = { JobId: string; Running: boolean; Latest?: { CelestialTarget?: string; TxHz: number } | null };

export function Q65TrackingSelector({ jobId, onChange, disabled }: { jobId: string; onChange: (jobId: string) => void; disabled: boolean }) {
  const hub = useContext(DeviceHubContext);
  const [status, setStatus] = useState('');
  const [pending, setPending] = useState(false);

  async function select() {
    if (!hub) return;
    setPending(true);
    try {
      const job = await hub.invoke<MoonJob>('GetTrackingJob');
      if (!job.Running || job.Latest?.CelestialTarget !== 'MOON' || !(job.Latest.TxHz > 0))
        throw new Error('Start a Moon tracking job with a TX frequency first.');
      onChange(job.JobId);
      setStatus('Bound Moon task · latest TX ' + (job.Latest.TxHz / 1e6).toFixed(6) + ' MHz');
    } catch (err) { setStatus(String(err)); }
    finally { setPending(false); }
  }

  return <div className="audio-buttons">
    <button type="button" disabled={disabled || pending || !hub} onClick={() => void select()}>USE MOON TRACKING</button>
    <button type="button" disabled={disabled || !jobId} onClick={() => { onChange(''); setStatus('Manual RF'); }}>MANUAL RF</button>
    <small>{jobId ? 'TRACKING · ' + jobId.slice(0, 8) + ' · ' : ''}{status}</small>
  </div>;
}
