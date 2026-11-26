import { useCallback, useEffect, useRef, useState } from 'react';
import { StationLauncher } from './StationLauncher';

const directoryKey = 'skyroof.workspace.origins';
const readyMessage = 'skyroof-workspace-ready';
const activeMessage = 'skyroof-workspace-active';

function parseOrigin(value: string): string | null {
  try {
    const url = new URL(value);
    return ['http:', 'https:'].includes(url.protocol) && !url.username &&
      !url.password && url.origin === value ? url.origin : null;
  } catch { return null; }
}

function loadOrigins(): string[] {
  try {
    const saved: unknown = JSON.parse(localStorage.getItem(directoryKey) ?? '[]');
    if (!Array.isArray(saved)) return [];
    return [...new Set(saved.filter((value): value is string =>
      typeof value === 'string' && parseOrigin(value) !== null && value !== location.origin))];
  } catch { return []; }
}

export function useStationWorkspaces() {
  const [origins, setOrigins] = useState(loadOrigins);
  const [active, setActive] = useState('local');
  useEffect(() => { localStorage.setItem(directoryKey, JSON.stringify(origins)); }, [origins]);

  function add(origin: string) {
    if (!parseOrigin(origin) || origin === location.origin)
      throw new Error('Use a distinct station Host origin.');
    setOrigins(previous => previous.includes(origin) ? previous : [...previous, origin]);
    setActive(origin);
  }
  function remove(origin: string) {
    setOrigins(previous => previous.filter(item => item !== origin));
    setActive(previous => previous === origin ? 'local' : previous);
  }
  return { origins, active, setActive, add, remove };
}

export function StationWorkspaceTabs({ workspaces }: { workspaces: ReturnType<typeof useStationWorkspaces> }) {
  return <nav className="station-tabs">
    <button className={workspaces.active === 'local' ? 'active' : ''} onClick={() => workspaces.setActive('local')}>Local Station</button>
    {workspaces.origins.map(origin => <div key={origin} className="station-tab-group">
      <button className={workspaces.active === origin ? 'active' : ''} onClick={() => workspaces.setActive(origin)}>{origin}</button>
      <button aria-label={'Close ' + origin} onClick={() => workspaces.remove(origin)}>×</button>
    </div>)}
    <StationLauncher onAdd={workspaces.add} />
  </nav>;
}

export function StationFrame({ origin, active }: { origin: string; active: boolean }) {
  const frame = useRef<HTMLIFrameElement>(null);
  const url = new URL('/', origin);
  url.searchParams.set('skyroof-workspace-parent', location.origin);
  const synchronize = useCallback(() => {
    frame.current?.contentWindow?.postMessage({ type: activeMessage, active }, origin);
  }, [origin, active]);
  useEffect(() => {
    function ready(event: MessageEvent) {
      if (event.origin === origin && event.source === frame.current?.contentWindow &&
          event.data?.type === readyMessage) synchronize();
    }
    window.addEventListener('message', ready);
    synchronize();
    return () => window.removeEventListener('message', ready);
  }, [origin, synchronize]);

  return <div className="workspace-slot" style={{ display: active ? 'flex' : 'none' }}>
    <iframe ref={frame} className="station-frame" title={'Station ' + origin}
      src={url.href} allow="microphone; autoplay" onLoad={synchronize} />
  </div>;
}

function getParentOrigin() {
  if (window.parent === window) return null;
  const parent = new URLSearchParams(location.search).get('skyroof-workspace-parent');
  return parent ? parseOrigin(parent) : null;
}

export function useEmbeddedWorkspace() {
  const parentOrigin = getParentOrigin();
  const [active, setActive] = useState(parentOrigin === null);
  useEffect(() => {
    if (!parentOrigin) return;
    function receive(event: MessageEvent) {
      if (event.origin === parentOrigin && event.source === window.parent &&
          event.data?.type === activeMessage && typeof event.data.active === 'boolean')
        setActive(event.data.active);
    }
    window.addEventListener('message', receive);
    window.parent.postMessage({ type: readyMessage }, parentOrigin);
    return () => window.removeEventListener('message', receive);
  }, [parentOrigin]);
  return { embedded: parentOrigin !== null, active };
}
