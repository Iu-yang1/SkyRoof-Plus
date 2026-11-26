import { useContext, useEffect, useState } from 'react';
import { DeviceHubContext, WorkspaceActiveContext } from './host';
import { CameraVideoPlayer } from './CameraVideoPlayer';
import { CameraPtzControls } from './CameraPtzControls';

type Profile = { token: string; name: string; media2: boolean };
type Device = { endpoint: string; scope: string };

export function CameraPanel() {
  const hub = useContext(DeviceHubContext);
  const active = useContext(WorkspaceActiveContext);
  const [profiles, setProfiles] = useState<Profile[]>([]);
  const [selected, setSelected] = useState(0);
  const [devices, setDevices] = useState<Device[]>([]);
  const [image, setImage] = useState('');
  const [error, setError] = useState('');
  const [running, setRunning] = useState(false);
  const [video, setVideo] = useState(false);
  async function load() {
    setError('');
    try {
      const response = await fetch('/api/v1/camera/profiles');
      if (!response.ok) throw new Error('ONVIF profiles HTTP ' + response.status);
      setProfiles(await response.json() as Profile[]); setSelected(0);
    } catch (err) { setError(String(err)); }
  }
  async function discover() {
    setError('');
    try {
      const response = await fetch('/api/v1/camera/discover', { method: 'POST' });
      if (!response.ok) throw new Error('ONVIF discovery HTTP ' + response.status);
      const data = await response.json() as { devices: Device[] };
      setDevices(data.devices);
    } catch (err) { setError(String(err)); }
  }

  const profile = profiles[selected];
  useEffect(() => {
    if (!running || video || !hub || !active || !profile) return;
    const controller = new AbortController();
    let pending = false;
    async function next() {
      if (pending || controller.signal.aborted) return;
      pending = true;
      try {
        const response = await fetch('/api/v1/camera/snapshot?profile=' +
          encodeURIComponent(profile.token) + '&media2=' + profile.media2, { signal: controller.signal });
        if (!response.ok) throw new Error('Snapshot HTTP ' + response.status);
        const blob = await response.blob();
        if (!controller.signal.aborted) { setImage(URL.createObjectURL(blob)); setError(''); }
      } catch (err) { if (!controller.signal.aborted) setError(String(err)); }
      finally { pending = false; }
    }
    void next();
    const timer = window.setInterval(() => void next(), 1500);
    return () => { controller.abort(); window.clearInterval(timer); };
  }, [running, video, hub, active, profile]);
  useEffect(() => () => { if (image) URL.revokeObjectURL(image); }, [image]);

  return <section className="panel"><header>STATION CAMERA · ONVIF</header>
    <div className="audio-buttons">
      <button disabled={!hub || !active} onClick={() => void discover()}>DISCOVER</button>
      <button disabled={!hub || !active} onClick={() => void load()}>LOAD PROFILES</button>
      <button disabled={!hub || !active || !profile} onClick={() => { setVideo(false); setRunning(v => !v); }}>{running ? 'STOP SNAPSHOTS' : 'START SNAPSHOTS'}</button>
      <button disabled={!hub || !active || !profile} onClick={() => { setRunning(false); setVideo(v => !v); }}>{video ? 'STOP VIDEO' : 'START VIDEO'}</button>
    </div>
    <label>PROFILE <select value={selected} onChange={e => setSelected(Number(e.target.value))}>
      {profiles.map((p, i) => <option key={p.token + p.media2} value={i}>{p.name} ({p.media2 ? 'Media2' : 'Media1'})</option>)}
    </select></label>
    {video && hub && active && profile && <CameraVideoPlayer profile={profile.token} media2={profile.media2} />}
    {!video && image && <img src={image} alt="Station ONVIF camera snapshot" style={{ width: '100%', objectFit: 'contain' }} />}
    {profile && <CameraPtzControls enabled={!!hub && active} profile={profile.token} />}
    {error && <p role="alert">{error}</p>}
    {devices.map(d => <p key={d.endpoint}>{d.endpoint} · {d.scope}</p>)}
    <small>Station-configured camera; discovery never sends credentials. Snapshots or H.264 RTSP remux (station FFmpeg required).</small>
  </section>;
}
