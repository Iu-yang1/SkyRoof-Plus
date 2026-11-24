import { useContext, useEffect, useState } from 'react';
import { CwReceivePanel } from './CwReceivePanel';
import { Ft4TransmitPanel } from './Ft4TransmitPanel';
import { DeviceHubContext, WorkspaceActiveContext } from './host';

type Slot = { utc: string; messages: string[]; sourceEpoch: number; rxDelayMs: number; timing: string };

export function DigitalPanel() {
  const hub = useContext(DeviceHubContext);
  const active = useContext(WorkspaceActiveContext);
  const [slots, setSlots] = useState<Slot[]>([]);
  const [status, setStatus] = useState('FT4 · NO STATION LINK');
  useEffect(() => {
    if (!hub || !active) return;
    let disposed = false;
    let pending = false;
    async function update() {
      if (disposed || pending) return;
      pending = true;
      try {
        const response = await fetch('/api/v1/dsp/ft4', { credentials: 'include' });
        if (!response.ok) throw new Error('FT4 DSP HTTP ' + response.status);
        const data = await response.json() as { results: Slot[] };
        if (disposed) return;
        setSlots(data.results);
        setStatus(data.results.length ? 'FT4 · RX' : 'FT4 · WAITING FOR SLOT');
      } catch (error) { if (!disposed) setStatus(String(error)); }
      finally { pending = false; }
    }
    void update();
    const timer = window.setInterval(() => { void update(); }, 1000);
    return () => { disposed = true; window.clearInterval(timer); };
  }, [hub, active]);
  return <section className="panel"><header>FT4 / CW CONSOLE</header>
    <p>{hub ? status : 'FT4 · NO STATION LINK'}</p>
    <div className="digital-messages">
      {slots.slice().reverse().map(slot => <div key={slot.utc + slot.sourceEpoch}>
        <time>{slot.utc.slice(11, 23)} UTC</time>
        {slot.messages.length ? slot.messages.map((message, i) => <div key={i}>{message}</div>) : <div>—</div>}
      </div>)}
    </div>
    <Ft4TransmitPanel available={Boolean(hub && active)} />
    <CwReceivePanel available={Boolean(hub && active)} />
    <small>FT4 RX · estimated UTC (RX delay {slots.at(-1)?.rxDelayMs ?? 0} ms).</small>
  </section>;
}
