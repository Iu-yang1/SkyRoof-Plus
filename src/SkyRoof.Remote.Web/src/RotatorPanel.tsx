import { useContext, useEffect, useState, type FormEvent } from 'react';
import { DeviceHubContext } from './host';

type Position = { AzimuthDegrees: number; ElevationDegrees: number;
  ReadbackFresh: boolean; StopTransmitted: boolean; HasPosition: boolean };

export function RotatorPanel() {
  const hub = useContext(DeviceHubContext);
  const [position, setPosition] = useState<Position | null>(null);
  const [azimuth, setAzimuth] = useState('90');
  const [elevation, setElevation] = useState('0');
  const [parkRoute, setParkRoute] = useState('90,0; 0,0');
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

  const park = async () => {
    if (!hub || busy) return;
    setBusy(true);
    try {
      const route = parkRoute.split(';').map(part => {
        const values = part.trim().split(',').map(Number);
        if (values.length !== 2 || values.some(value => !Number.isFinite(value)))
          throw new Error('Park route must contain AZ,EL pairs separated by semicolons.');
        return { AzimuthDegrees: values[0], ElevationDegrees: values[1] };
      });
      setStatus('PARK in progress · position readback is live');
      const result = await hub.invoke<Position>('ParkRotator', route);
      setPosition(result);
      setStatus('PARK completed at requested final AZ/EL');
    } catch (error) { setStatus('PARK failed or interrupted: ' + String(error)); }
    finally { setBusy(false); }
  };

  const stop = async () => {
    if (!hub) return;
    setBusy(true);
    try {
      const response = await hub.invoke<Position>('StopRotator');
      setPosition(response);
      setStatus(response.StopTransmitted
        ? 'STOP command acknowledged · position not freshly verified'
        : 'STOP outcome unknown');
    } catch (error) { setStatus('STOP outcome unconfirmed: ' + String(error)); }
    finally { setBusy(false); }
  };

  return <section className="panel rotator-panel">
    <header>ROTATOR · LIVE GS-232 / ROTCTLD</header>
    <div className="readout">AZ {position?.HasPosition ? position.AzimuthDegrees.toFixed(2) : '—'}° ·
      EL {position?.HasPosition ? position.ElevationDegrees.toFixed(2) : '—'}°</div>
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
    <label>PARK WAYPOINTS · AZ,EL; AZ,EL
      <input value={parkRoute} onChange={event => setParkRoute(event.target.value)}
        aria-label="Park waypoints" placeholder="90,0; 0,0" />
    </label>
    <div className="audio-buttons">
      <button type="button" disabled={!hub || busy} onClick={() => { void park(); }}>PARK ROUTE</button>
    </div>
    <small>实际位置取自 rotctld p 命令。PARK 按顺序到达各路径点，每段等待真实位置反馈；STOP 可抢占 PARK。STOP 回执不代表机械制动确认。</small>
  </section>;
}
