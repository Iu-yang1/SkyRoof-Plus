import { useContext, useEffect, useRef, useState, type FormEvent, type RefObject, type PointerEvent as ReactPointerEvent } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import { DeviceHubContext, WorkspaceActiveContext, type DeviceReadback, DeviceOperation } from './host';

function toHz(mhz: string): number {
  const frequency = Number(mhz);
  if (!Number.isFinite(frequency) || frequency <= 0)
    throw new Error('Frequency must be a positive MHz value.');
  return Math.round(frequency * 1_000_000);
}

function fromHz(hz: number): string {
  return (hz / 1_000_000).toFixed(6);
}

type PttInputRefs = {
  active: RefObject<boolean>;
  transaction: RefObject<string | null>;
  pointer: RefObject<number | null>;
  press: RefObject<() => void>;
  release: RefObject<() => void>;
};

function usePttInputListeners(hub: HubConnection | null, refs: PttInputRefs) {
  const { active, transaction, pointer, press, release } = refs;
  useEffect(() => {
    const down = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (!active.current || event.code !== 'Space' || event.repeat ||
          target?.closest('input, textarea, select, button, [contenteditable]')) return;
      event.preventDefault();
      press.current();
    };
    const up = (event: KeyboardEvent) => {
      if (event.code !== 'Space' || !transaction.current) return;
      event.preventDefault();
      release.current();
    };
    const blur = () => release.current();
    // A hidden but mounted Workspace must retain TX until its original pointer is released.
    const pointerEnd = (event: PointerEvent) => {
      if (pointer.current === event.pointerId) release.current();
    };
    window.addEventListener('keydown', down);
    window.addEventListener('keyup', up);
    window.addEventListener('pointerup', pointerEnd);
    window.addEventListener('pointercancel', pointerEnd);
    window.addEventListener('blur', blur);
    return () => {
      window.removeEventListener('keydown', down);
      window.removeEventListener('keyup', up);
      window.removeEventListener('pointerup', pointerEnd);
      window.removeEventListener('pointercancel', pointerEnd);
      window.removeEventListener('blur', blur);
      release.current();
    };
  }, [hub]);
}

export function RadioPanel() {
  const hub = useContext(DeviceHubContext);
  const workspaceActive = useContext(WorkspaceActiveContext);
  const activeRef = useRef(workspaceActive);
  activeRef.current = workspaceActive;
  const [radio, setRadio] = useState<DeviceReadback | null>(null);
  const [rx, setRx] = useState('');
  const [tx, setTx] = useState('');
  const [mode, setMode] = useState('USB');
  const [error, setError] = useState('');
  const [transmitting, setTransmitting] = useState(false);
  const pending = useRef<Promise<void>>(Promise.resolve());
  const heldTx = useRef<string | null>(null);
  const heldPointer = useRef<number | null>(null);
  const heldRef = useRef<{ rx: string; tx: string; mode: string }>({ rx, tx, mode });
  heldRef.current = { rx, tx, mode };

  useEffect(() => {
    if (!hub) { setRadio(null); return; }
    let closed = false;
    let polling = false;
    async function read() {
      if (closed || polling) return;
      polling = true;
      try {
        const reply = await hub!.invoke<DeviceReadback>('GetCatReadback');
        if (!closed) {
          setRadio(reply);
          setRx(existing => existing || fromHz(reply.RxFrequencyHz));
          setTx(existing => existing || fromHz(reply.TxFrequencyHz));
          setError('');
        }
      } catch (err) { if (!closed) setError(String(err)); }
      finally { polling = false; }
    }
    void read();
    const interval = window.setInterval(() => { void read(); }, 1200);
    return () => {
      closed = true;
      window.clearInterval(interval);
    };
  }, [hub]);

  function queue(action: () => Promise<void>) {
    pending.current = pending.current.catch(() => undefined).then(action)
      .catch(err => setError(String(err)));
  }

  useEffect(() => {
    if (!hub) return;
    const timer = window.setInterval(() => {
      const txId = heldTx.current;
      if (txId === null) return;
      queue(async () => {
        const state = await hub.invoke<DeviceReadback>('ExecuteCatCommand', {
          TransactionId: txId, Operation: DeviceOperation.ManualHeartbeat,
          FrequencyHz: 0, Mode: '', AudioSource: ''
        });
        setRadio(state);
      });
    }, 3000);
    return () => window.clearInterval(timer);
  }, [hub]);

  function pttDown() {
    if (!hub || !activeRef.current || heldTx.current) return;
    const id = crypto.randomUUID();
    heldTx.current = id;
    const { tx: frequency, mode: txMode } = heldRef.current;
    queue(async () => {
      const next = await hub.invoke<DeviceReadback>('ExecuteCatCommand', {
        TransactionId: id, Operation: DeviceOperation.ManualBegin,
        FrequencyHz: toHz(frequency), Mode: txMode, AudioSource: 'LAN'
      });
      setRadio(next);
      setTransmitting(next.ActualPtt);
    });
  }

  function pttUp() {
    const txId = heldTx.current;
    if (!hub || !txId) return;
    heldTx.current = null;
    heldPointer.current = null;
    queue(async () => {
      const next = await hub.invoke<DeviceReadback>('ExecuteCatCommand', {
        TransactionId: txId, Operation: DeviceOperation.ManualEnd,
        FrequencyHz: 0, Mode: '', AudioSource: ''
      });
      setRadio(next);
      setTransmitting(false);
    });
  }

  const pressRef = useRef(pttDown);
  const releaseRef = useRef(pttUp);
  pressRef.current = pttDown;
  releaseRef.current = pttUp;

  usePttInputListeners(hub, { active: activeRef, transaction: heldTx,
    pointer: heldPointer, press: pressRef, release: releaseRef });

  function tune(event: FormEvent<HTMLFormElement>, target: 'rx' | 'tx') {
    event.preventDefault();
    if (!hub) return;
    const desired = target === 'rx' ? rx : tx;
    queue(async () => {
      const reply = await hub.invoke<DeviceReadback>('ExecuteCatCommand', {
        TransactionId: crypto.randomUUID(),
        Operation: target === 'rx' ? DeviceOperation.RxFrequency : DeviceOperation.TxFrequency,
        FrequencyHz: toHz(desired), Mode: '', AudioSource: ''
      });
      setRadio(reply);
      if (target === 'rx') setRx(fromHz(reply.RxFrequencyHz));
      else setTx(fromHz(reply.TxFrequencyHz));
    });
  }

  function pointerDown(event: ReactPointerEvent<HTMLButtonElement>) {
    if (event.button !== 0 || !hub || !radio || !activeRef.current || heldTx.current) return;
    heldPointer.current = event.pointerId;
    event.currentTarget.setPointerCapture(event.pointerId);
    pttDown();
  }

  function pointerUp(event: ReactPointerEvent<HTMLButtonElement>) {
    if (heldPointer.current === event.pointerId) pttUp();
  }

  return <section className="panel radio-panel">
    <header>RADIO · LIVE CAT READBACK</header>
    {hub ? <div className="cat-status">{radio ? 'CAT connected' : 'Connecting CAT…'}</div>
      : <div className="cat-status">No Station Hub linked to this Workspace</div>}
    <div className="frequency">{radio ? fromHz(radio.RxFrequencyHz) : '—'} <small>MHz RX</small></div>
    <div className="frequency tx-frequency">{radio ? fromHz(radio.TxFrequencyHz) : '—'} <small>MHz TX</small></div>
    <div className="readout">{radio?.ActualPtt ? 'PTT ON' : 'RX'} · {radio?.TxMode || 'mode pending'}
      · Epoch {radio?.Epoch ?? '—'}</div>
    <div className="tune-grid">
      <form onSubmit={event => tune(event, 'rx')}><label>RX MHz<input value={rx} onChange={event => setRx(event.target.value)} inputMode="decimal" /></label>
        <button disabled={!hub}>SET RX</button></form>
      <form onSubmit={event => tune(event, 'tx')}><label>TX MHz<input value={tx} onChange={event => setTx(event.target.value)} inputMode="decimal" /></label>
        <button disabled={!hub}>SET TX</button></form>
    </div>
    <label className="mode-select">TX MODE <select value={mode} onChange={event => setMode(event.target.value)}>
      <option>USB</option><option>LSB</option><option>FM</option><option>AM</option><option>CW</option><option>PKTUSB</option>
    </select></label>
    <button className={'ptt-button ' + (transmitting || radio?.ActualPtt ? 'active' : '')}
      disabled={!hub || !radio}
      onPointerDown={pointerDown} onPointerUp={pointerUp}
      onPointerCancel={pointerUp}
      type="button">PTT · HOLD / SPACE</button>
    {error && <p role="alert" className="error">{error}</p>}
    <small>此界面直接调用 Station Core 的 CAT 事务；Device 必须连接实际 SkyCAT/rigctld。没有音频 TX 时仅有 PTT/CAT，不代表可传送话音。</small>
  </section>;
}
