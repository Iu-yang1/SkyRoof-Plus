import { useContext, useEffect, useState, type FormEvent } from 'react';
import { DeviceHubContext } from './host';
import { RadioLinkOptions, readRadioLink } from './RadioLinkOptions';

type TrackingStatus = {
  NoradNumber: number; OrbitBackend: string; DataQuality: string;
  AzimuthDegrees: number; ElevationDegrees: number;
  RangeMetres: number; RangeRateMetresPerSecond: number;
  RxHz: number; TxHz: number; CatApplied: boolean; RotatorApplied: boolean;
  ActualRxHz: number; ActualTxHz: number;
  ActualAzimuthDegrees: number | null; ActualElevationDegrees: number | null;
  RotatorError: string;
};

type TrackingJob = {
  JobId: string; Running: boolean; Phase: string; CompletedTicks: number;
  Latest: TrackingStatus | null; Error: string; ResumeAfterRestart: boolean;
};

function numeric(value: FormDataEntryValue | null, field: string): number {
  if (typeof value !== 'string' || value.trim() === '' || !Number.isFinite(Number(value)))
    throw new Error(field + ' must be numeric.');
  return Number(value);
}

export function TrackingPanel() {
  const hub = useContext(DeviceHubContext);
  const [status, setStatus] = useState<TrackingStatus | null>(null);
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);

  const [job, setJob] = useState<TrackingJob | null>(null);
  const [jobError, setJobError] = useState('');

  useEffect(() => {
    setJob(null);
    if (!hub) return;
    let disposed = false;
    let busy = false;
    async function poll() {
      if (disposed || busy || !hub) return;
      busy = true;
      try {
        const next = await hub.invoke<TrackingJob>('GetTrackingJob');
        if (!disposed) { setJob(next); setJobError(''); }
      } catch (err) {
        if (!disposed) setJobError(String(err));
      } finally { busy = false; }
    }
    void poll();
    const timer = window.setInterval(() => { void poll(); }, 1000);
    return () => { disposed = true; window.clearInterval(timer); };
  }, [hub]);

  async function stopJob() {
    if (!hub || !job?.Running || pending) return;
    setPending(true);
    try { setJob(await hub.invoke<TrackingJob>('StopTrackingJob', job.JobId)); }
    catch (err) { setError(String(err)); }
    finally { setPending(false); }
  }

  async function submit(event: FormEvent<HTMLFormElement>, operation: string) {
    event.preventDefault();
    if (!hub || pending) return;
    const form = new FormData(event.currentTarget);
    setPending(true);
    setError('');
    try {
      const request = {
        TleLine1: String(form.get('tle1') ?? '').trim(),
        TleLine2: String(form.get('tle2') ?? '').trim(),
        LatitudeDegrees: numeric(form.get('latitude'), 'Latitude'),
        LongitudeDegrees: numeric(form.get('longitude'), 'Longitude'),
        HeightMetres: numeric(form.get('height'), 'Height'),
        UtcUnixMilliseconds: Date.now(),
        DownlinkHz: Math.round(numeric(form.get('rx'), 'Downlink') * 1e6),
        UplinkHz: Math.round(numeric(form.get('tx'), 'Uplink') * 1e6),
        CorrectDownlink: form.has('correctRx'),
        CorrectUplink: form.has('correctTx'),
        PointRotator: form.has('rotator'),
        ElevationMaskDegrees: numeric(form.get('mask'), 'Elevation mask'),
        RadioLink: readRadioLink(form)
      };
      if (operation === 'start') {
        const interval = numeric(form.get('interval'), 'Interval');
        setJob(await hub.invoke<TrackingJob>('StartTrackingJob',
          request, interval, form.has('resume')));
        setStatus(null);
      } else {
        const result = await hub.invoke<TrackingStatus>(
          operation === 'apply' ? 'ApplyTracking' : 'PredictTracking', request);
        setStatus(result);
      }
    } catch (err) { setError(String(err)); }
    finally { setPending(false); }
  }

  const displayed = job?.Running ? job.Latest : status;
  return <section className="panel tracking-panel">
    <header>SATELLITE TRACKING</header>
    <form onSubmit={event => {
      const button = (event.nativeEvent as SubmitEvent).submitter;
      void submit(event, button?.getAttribute('value') ?? 'predict');
    }}>
      <label>TLE LINE 1<input name="tle1" required placeholder="1 …" /></label>
      <label>TLE LINE 2<input name="tle2" required placeholder="2 …" /></label>
      <div className="tracking-grid">
        <label>LAT °<input name="latitude" type="number" step="any" required /></label>
        <label>LON °<input name="longitude" type="number" step="any" required /></label>
        <label>HEIGHT m<input name="height" type="number" step="any" defaultValue="0" required /></label>
        <label>MASK °<input name="mask" type="number" step="any" defaultValue="0" required /></label>
        <label>DOWNLINK MHz<input name="rx" type="number" step="any" required /></label>
        <label>UPLINK MHz (0 = RX only)<input name="tx" type="number" step="any" defaultValue="0" required /></label>
      </div>
      <RadioLinkOptions />
      <label>INTERVAL ms<input name="interval" type="number" min="100" step="100" defaultValue="1000" required /></label>
      <div className="tracking-options">
        <label><input name="correctRx" type="checkbox" defaultChecked /> RX DOPPLER</label>
        <label><input name="correctTx" type="checkbox" defaultChecked /> TX DOPPLER</label>
        <label><input name="rotator" type="checkbox" /> POINT ROTATOR</label>
        <label><input name="resume" type="checkbox" defaultChecked /> RESUME AFTER RESTART</label>
      </div>
      <div className="audio-buttons">
        <button disabled={!hub || pending} value="predict">PREDICT NOW</button>
        <button disabled={!hub || pending} value="apply">APPLY ONCE</button>
        <button disabled={!hub || pending} value="start">START TRACKING</button>
        <button type="button" disabled={!hub || pending || !job?.Running} onClick={() => { void stopJob(); }}>STOP TRACKING</button>
      </div>
    </form>
    {job && <div className="readout">STATION TASK · {job.Phase.toUpperCase()} · {job.CompletedTicks} updates</div>}
    {jobError && <small>{jobError}</small>}
    {job?.Error && <p className="error">{job.Error}</p>}
    {displayed && <div className="tracking-result">
      <div className="readout">NORAD {displayed.NoradNumber} · {displayed.OrbitBackend}</div>
      <div className="readout">AZ {displayed.AzimuthDegrees.toFixed(2)}° · EL {displayed.ElevationDegrees.toFixed(2)}°</div>
      <div className="readout">Range {(displayed.RangeMetres / 1000).toFixed(1)} km · Rate {displayed.RangeRateMetresPerSecond.toFixed(1)} m/s</div>
      <div className="readout">RX {(displayed.RxHz / 1e6).toFixed(6)} · TX {(displayed.TxHz / 1e6).toFixed(6)} MHz</div>
      {displayed.CatApplied && <p>CAT readback: {displayed.ActualRxHz} / {displayed.ActualTxHz} Hz</p>}
      {displayed.RotatorApplied && <p>Rotator readback: {displayed.ActualAzimuthDegrees} / {displayed.ActualElevationDegrees}°</p>}
      {displayed.RotatorError && <p className="error">{displayed.RotatorError}</p>}
      <small>{displayed.DataQuality}</small>
    </div>}
    {error && <p className="error" role="alert">{error}</p>}
    <small>Tracking runs in Station Core and survives client disconnect. No PTT is keyed by tracking.</small>
  </section>;
}
