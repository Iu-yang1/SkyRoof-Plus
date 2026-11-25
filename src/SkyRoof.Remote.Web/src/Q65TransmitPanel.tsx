import { useEffect, useRef, useState } from 'react';

export function Q65TransmitPanel({ available, submode, rxHz }: { available: boolean; submode: string; rxHz: number }) {
  const [message, setMessage] = useState('');
  const [mhz, setMhz] = useState('');
  const [gain, setGain] = useState(50);
  const [odd, setOdd] = useState(false);
  const [running, setRunning] = useState(false);
  const [status, setStatus] = useState('Q65 TX · IDLE');
  const active = useRef<AbortController | null>(null);
  useEffect(() => () => active.current?.abort(), []);
  useEffect(() => { if (!available) active.current?.abort(); }, [available]);

  async function transmit() {
    if (!available || active.current) return;
    const frequencyHz = Math.round(Number(mhz) * 1e6);
    if (!(frequencyHz > 0) || !message.trim()) { setStatus('Enter TX frequency and Q65 message.'); return; }
    const abort = new AbortController();
    active.current = abort;
    setRunning(true); setStatus('Encoding · sealing · waiting for station UTC slot');
    try {
      const response = await fetch('/api/v1/dsp/q65/transmit', {
        method: 'POST', credentials: 'include', signal: abort.signal,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ submode, message: message.trim().toUpperCase(), frequencyHz, audioHz: rxHz, gain: gain / 100, odd })
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.detail ?? result.error ?? response.statusText);
      setStatus('Q65 TX ' + result.state + ' · UDP ' + result.actualFirstPacketUtc);
    } catch (e) { setStatus(abort.signal.aborted ? 'CANCEL REQUESTED · check CAT RX' : String(e)); }
    finally { active.current = null; setRunning(false); }
  }

  return <form onSubmit={event => { event.preventDefault(); void transmit(); }}>
    <label>Q65 {submode} MESSAGE <input value={message} maxLength={37} onChange={e => setMessage(e.target.value)} /></label>
    <label>TX MHz <input inputMode="decimal" value={mhz} onChange={e => setMhz(e.target.value)} /></label>
    <label>PCM LEVEL % <input type="number" min="0" max="100" value={gain} onChange={e => setGain(Number(e.target.value))} /></label>
    <label>TX SLOT <select value={odd ? 'odd' : 'even'} onChange={e => setOdd(e.target.value === 'odd')}>
      <option value="even">EVEN</option><option value="odd">ODD</option></select></label>
    <button disabled={!available || running} type="submit">SEND ONE Q65 SLOT</button>
    <button disabled={!running} type="button" onClick={() => active.current?.abort()}>CANCEL</button>
    <p role="status">{status}</p>
    <small>Uses station time and native LAN PCM grant; AF from RX frequency setting.</small>
  </form>;
}
