import { useContext, useEffect, useState, type FormEvent } from 'react';
import { DeviceHubContext } from './host';

type Position = { AzimuthDegrees: number; ElevationDegrees: number };

export function RotatorPanel() {
  const hub = useContext(DeviceHubContext);
  const [position, setPosition] = useState<Position | null>(null);
  const [azimuth, setAzimuth] = useState('90');
  const [elevation, setElevation] = useState('0');
  const [status, setStatus] = useState('No rotator status');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (!hub) { setPosition(null); setStatus('No Station connection'); return; }
    let closed = false;
    let inProgress = false;
    const poll = async () => {
      if (inProgress || closed) return;
      inProgress = true;
      try {
        const current = await hub.invoke<Position>('GetRotatorPosition');
        if (!closed) {
          setPosition(current);
          setStatus('Device feedback');
        }
      } catch (error) {
        if (!closed) setStatus(String(error));
      } finally { inProgress = false; }
    };
    void poll();
    const interval = window.setInterval(() => { void poll(); }, 1000);
    return () => { closed = true; window.clearInterval(interval); };
  }, [hub]);

  const point = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!hub || busy) return;
    setBusy(true);
    try {
      const response = await hub.invoke<Position>('PointRotator', Number(azimuth), Number(elevation));
      setPosition(response);
      setStatus('Position command readback confirmed');
    } catch (error) { setStatus(String(error)); }
    finally { setBusy(false); }
  };

  const stop = async () => {
    if (!hub) return;
    setBusy(true);
    try {
      const response = await hub.invoke<Position>('StopRotator');
      setPosition(response);
      setStatus('STOP sent; position readback received');
    } catch (error) { setStatus('STOP outcome unconfirmed: ' + String(error)); }
    finally { setBusy(false); }
  };

  return <section className="panel rotator-panel">
    <header>ROTATOR · LIVE GS-232 / ROTCTLD</header>
    <div className="readout">AZ {position ? position.AzimuthDegrees.toFixed(2) : '—'}° ·
      EL {position ? position.ElevationDegrees.toFixed(2) : '—'}°</div>
    <p>{status}</p>
    <form onSubmit={event => { void point(event); }}>
      <div className="tracking-grid">
        <label>AZ °<input type="number" step="any" required value={azimuth}
          onChange={event => setAzimuth(event.target.value)} /></label>
        <label>EL °<input type="number" step="any" required value={elevation}
          onChange={event => setElevation(event.target.value)} /></label>
      </div>
      <div className="audio-buttons">
        <button type="submit" disabled={!hub || busy}>POINT AZ/EL</button>
        <button type="button" onClick={() => { void stop(); }} disabled={!hub}>STOP</button>
      </div>
    </form>
    <small>实际位置取自 rotctld p 命令；POINT 受 Device 机械限位配置约束。STOP 发送后的读回不是机械制动确认。</small>
  </section>;
}
