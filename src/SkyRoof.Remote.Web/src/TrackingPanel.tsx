import { useContext, useState, type FormEvent } from 'react';
import { DeviceHubContext } from './host';

type TrackingStatus = {
  NoradNumber: number; OrbitBackend: string; DataQuality: string;
  AzimuthDegrees: number; ElevationDegrees: number;
  RangeMetres: number; RangeRateMetresPerSecond: number;
  RxHz: number; TxHz: number; CatApplied: boolean; RotatorApplied: boolean;
  ActualRxHz: number; ActualTxHz: number;
  ActualAzimuthDegrees: number | null; ActualElevationDegrees: number | null;
  RotatorError: string;
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

  async function submit(event: FormEvent<HTMLFormElement>, apply: boolean) {
    event.preventDefault();
    if (!hub || pending) return;
    const form = new FormData(event.currentTarget);
    setPending(true);
    setError('');
    try {
      const result = await hub.invoke<TrackingStatus>(apply ? 'ApplyTracking' : 'PredictTracking', {
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
        ElevationMaskDegrees: numeric(form.get('mask'), 'Elevation mask')
      });
      setStatus(result);
    } catch (err) { setError(String(err)); }
    finally { setPending(false); }
  }

  return <section className="panel tracking-panel">
    <header>SATELLITE TRACKING · SGP.NET</header>
    <form onSubmit={event => {
      const button = (event.nativeEvent as SubmitEvent).submitter;
      void submit(event, button?.getAttribute('value') === 'apply');
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
      <div className="tracking-options">
        <label><input name="correctRx" type="checkbox" defaultChecked /> RX DOPPLER</label>
        <label><input name="correctTx" type="checkbox" defaultChecked /> TX DOPPLER</label>
        <label><input name="rotator" type="checkbox" /> POINT ROTATOR</label>
      </div>
      <div className="audio-buttons">
        <button disabled={!hub || pending} value="predict">PREDICT NOW</button>
        <button disabled={!hub || pending} value="apply">APPLY ONCE</button>
      </div>
    </form>
    {status && <div className="tracking-result">
      <div className="readout">NORAD {status.NoradNumber} · {status.OrbitBackend}</div>
      <div className="readout">AZ {status.AzimuthDegrees.toFixed(2)}° · EL {status.ElevationDegrees.toFixed(2)}°</div>
      <div className="readout">Range {(status.RangeMetres / 1000).toFixed(1)} km · Rate {status.RangeRateMetresPerSecond.toFixed(1)} m/s</div>
      <div className="readout">RX {(status.RxHz / 1e6).toFixed(6)} · TX {(status.TxHz / 1e6).toFixed(6)} MHz</div>
      {status.CatApplied && <p>CAT readback: {status.ActualRxHz} / {status.ActualTxHz} Hz</p>}
      {status.RotatorApplied && <p>Rotator readback: {status.ActualAzimuthDegrees} / {status.ActualElevationDegrees}°</p>}
      {status.RotatorError && <p className="error">{status.RotatorError}</p>}
      <small>{status.DataQuality}</small>
    </div>}
    {error && <p className="error" role="alert">{error}</p>}
    <small>Single observation/application at current UTC; does not key PTT or start continuous tracking.</small>
  </section>;
}
