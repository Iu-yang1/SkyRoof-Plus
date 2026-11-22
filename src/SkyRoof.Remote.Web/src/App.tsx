import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { DockviewReact, type DockviewApi, type DockviewReadyEvent } from 'dockview-react';
import { connectControl, queryStation, signIn, DeviceHubContext, WorkspaceActiveContext, type StationInfo } from './host';
import { type HubConnection } from '@microsoft/signalr';
import { RadioPanel } from './RadioPanel';
import { AudioPanel } from './AudioPanel';
import { TrackingPanel } from './TrackingPanel';
import { RotatorPanel } from './RotatorPanel';
import { SpectrumPanel } from './SpectrumPanel';

function DigitalPanel() {
  return <section className="panel"><header>FT4 / CW CONSOLE</header>
    <p>DSP 解码及 TX 事务等待业务迁移后接入。</p>
  </section>;
}
function RfSpectrumPanel() { return <SpectrumPanel />; }
function AfSpectrumPanel() { return <SpectrumPanel audioFrequency />; }
const components = {
  radio: RadioPanel, audio: AudioPanel, scope: RfSpectrumPanel, afscope: AfSpectrumPanel,
  satellites: TrackingPanel, rotator: RotatorPanel, digital: DigitalPanel
};

function Workbench({ station }: { station: string }) {
  const api = useRef<DockviewApi | null>(null);
  const key = 'skyroof.layout.' + station;
  const onReady = useCallback((ready: DockviewReadyEvent) => {
    api.current = ready.api;
    const saved = localStorage.getItem(key);
    if (saved) {
      try { ready.api.fromJSON(JSON.parse(saved)); return; }
      catch { localStorage.removeItem(key); }
    }
    ready.api.addPanel({ id: 'radio', title: 'Radio', component: 'radio' });
    ready.api.addPanel({ id: 'scope', title: 'Spectrum', component: 'scope',
      position: { referencePanel: 'radio', direction: 'below' } });
    ready.api.addPanel({ id: 'af-scope', title: 'AF Spectrum', component: 'afscope',
      position: { referencePanel: 'scope', direction: 'below' } });
    ready.api.addPanel({ id: 'sat', title: 'Satellites', component: 'satellites',
      position: { referencePanel: 'radio', direction: 'right' } });
    ready.api.addPanel({ id: 'rot', title: 'Rotator', component: 'rotator',
      position: { referencePanel: 'sat', direction: 'below' } });
    ready.api.addPanel({ id: 'audio', title: 'RX Audio', component: 'audio',
      position: { referencePanel: 'rot', direction: 'below' } });
    ready.api.addPanel({ id: 'digital', title: 'Digital', component: 'digital',
      position: { referencePanel: 'scope', direction: 'right' } });
  }, [key]);

  return <div className="workspace">
    <div className="toolbar"><span>WORKSPACE · {station}</span>
      <button onClick={() => {
        if (api.current) localStorage.setItem(key, JSON.stringify(api.current.toJSON()));
      }}>SAVE LAYOUT</button>
    </div>
    <div className="dock">
      <DockviewReact className="dockview-theme-abyss" onReady={onReady} components={components} />
    </div>
  </div>;
}

export default function App() {
  const [station, setStation] = useState<StationInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [status, setStatus] = useState('Disconnected');
  const [error, setError] = useState('');
  const [workspace, setWorkspace] = useState('local');
  const [hub, setHub] = useState<HubConnection | null>(null);

  useEffect(() => {
    void queryStation().then(setStation).catch(err => setError(String(err)))
      .finally(() => setLoading(false));
  }, []);
  useEffect(() => {
    if (!station) return;
    let closed = false;
    let connection: HubConnection | null = null;
    let retryTimer: number | null = null;

    const reconnect = async () => {
      if (closed) return;
      try {
        const next = await connectControl();
        if (closed) { await next.stop(); return; }
        connection = next;
        next.onreconnecting(() => {
          if (!closed) setStatus('SignalR reconnecting');
        });
        next.onreconnected(() => {
          if (!closed) setStatus('SignalR/MessagePack connected');
        });
        next.onclose(() => {
          if (closed || connection !== next) return;
          connection = null;
          setHub(null);
          setStatus('SignalR disconnected · retrying');
          retryTimer = window.setTimeout(() => { void reconnect(); }, 3000);
        });
        setHub(next);
        setStatus('SignalR/MessagePack connected');
      } catch (err) {
        if (closed) return;
        setStatus('SignalR unavailable · retrying: ' + String(err));
        retryTimer = window.setTimeout(() => { void reconnect(); }, 3000);
      }
    };

    void reconnect();
    return () => {
      closed = true;
      if (retryTimer !== null) window.clearTimeout(retryTimer);
      if (connection) void connection.stop();
      setHub(null);
    };
  }, [station]);
  const submit = async (ev: FormEvent<HTMLFormElement>) => {
    ev.preventDefault();
    setError('');
    try {
      await signIn(username, password);
      setStation(await queryStation());
    } catch (err) { setError(String(err)); }
    setPassword('');
  };

  if (loading) return <main className="login">Connecting...</main>;
  if (!station) return <main className="login"><form onSubmit={ev => void submit(ev)}>
    <h1>SkyRoof Remote</h1><p>Station Login</p>
    <label>USER<input autoComplete="username" value={username} onChange={ev => setUsername(ev.target.value)} /></label>
    <label>PASSWORD<input autoComplete="current-password" type="password" value={password} onChange={ev => setPassword(ev.target.value)} /></label>
    {error && <p className="error" role="alert">{error}</p>}
    <button type="submit">SIGN IN</button>
  </form></main>;

  return <div className="app">
    <div className="titlebar"><strong>SkyRoof</strong><span>REMOTE V1 · DEV PROTOTYPE</span>
      <small>{station.user} · {status}</small></div>
    <nav className="station-tabs">
      <button className={workspace === 'local' ? 'active' : ''} onClick={() => setWorkspace('local')}>Local Station</button>
      <button className={workspace === 'remote' ? 'active' : ''} onClick={() => setWorkspace('remote')}>Remote Workspace</button>
      <small>第二工作区仅展示独立布局，不连接另一台真实台站</small>
    </nav>
    <div className="workspace-slot" style={{ display: workspace === 'local' ? 'flex' : 'none' }}>
      <DeviceHubContext.Provider value={hub}>
        <WorkspaceActiveContext.Provider value={workspace === 'local'}>
          <Workbench station="local" />
        </WorkspaceActiveContext.Provider>
      </DeviceHubContext.Provider>
    </div>
    <div className="workspace-slot" style={{ display: workspace === 'remote' ? 'flex' : 'none' }}>
      <DeviceHubContext.Provider value={null}>
        <WorkspaceActiveContext.Provider value={workspace === 'remote'}>
          <Workbench station="remote" />
        </WorkspaceActiveContext.Provider>
      </DeviceHubContext.Provider>
    </div>
    <footer className="status">{station.runtimeMode} · Device CAT via Station Core · AF DSP bridge available; RF/IF and TX integration pending</footer>
  </div>;
}
