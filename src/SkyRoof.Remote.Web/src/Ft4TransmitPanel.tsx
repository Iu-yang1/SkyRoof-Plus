import { useEffect, useRef, useState } from 'react';
import { Ft4AutoPanel } from './Ft4AutoPanel';

export function Ft4TransmitPanel({ available }: { available: boolean }) {
  const [message, setMessage] = useState('');
  const [frequency, setFrequency] = useState('');
  const [audio, setAudio] = useState('1500');
  const [gain, setGain] = useState('50');
  const [odd, setOdd] = useState(false);
  const [status, setStatus] = useState('FT4 TX · IDLE');
  const [running, setRunning] = useState(false);
  const [autoRunning, setAutoRunning] = useState(false);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);
  useEffect(() => { if (!available) controller.current?.abort(); }, [available]);

  async function transmit() {
    if (!available || autoRunning || controller.current) return;
    const hz = Math.round(Number(frequency) * 1e6);
    if (!Number.isFinite(hz) || hz <= 0 || !message.trim()) {
      setStatus('Enter a TX frequency and FT4 message.');
      return;
    }
    const abort = new AbortController();
    controller.current = abort;
    setRunning(true);
    setStatus('Encoding · sealing · waiting for station UTC slot');
    try {
      const response = await fetch('/api/v1/dsp/ft4/transmit', {
        method: 'POST', credentials: 'include', signal: abort.signal,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: message.trim().toUpperCase(), frequencyHz: hz,
          audioHz: Number(audio), gain: Number(gain) / 100, odd })
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.detail ?? result.error ?? response.statusText);
      setStatus('TX ' + result.state + ' · UDP ' + result.actualFirstPacketUtc + ' UTC');
    } catch (error) {
      setStatus(abort.signal.aborted ? 'CANCEL REQUESTED · check CAT RX' : String(error));
    } finally { controller.current = null; setRunning(false); }
  }

  return <><form onSubmit={event => { event.preventDefault(); void transmit(); }}>
    <label>FT4 MESSAGE <input value={message} maxLength={37} onChange={e => setMessage(e.target.value)} /></label>
    <label>TX MHz <input value={frequency} inputMode="decimal" onChange={e => setFrequency(e.target.value)} /></label>
    <label>AF Hz <input type="number" value={audio} onChange={e => setAudio(e.target.value)} /></label>
    <label>PCM LEVEL % <input type="number" min="0" max="100" value={gain} onChange={e => setGain(e.target.value)} /></label>
    <label>TX SLOT <select value={odd ? 'odd' : 'even'} onChange={e => setOdd(e.target.value === 'odd')}>
      <option value="even">EVEN</option><option value="odd">ODD</option></select></label>
    <button disabled={!available || running || autoRunning} type="submit">SEND ONE FT4 SLOT</button>
    <button disabled={!running} type="button" onClick={() => controller.current?.abort()}>CANCEL</button>
    <p>{status}</p>
    <small>Station-local timing; PCM level is not measured RF power. Native LAN TX grant required.</small>
  </form>
    <Ft4AutoPanel available={available && !running} onRunning={setAutoRunning}
      radio={{ frequencyHz: Math.round(Number(frequency) * 1e6), audioHz: Number(audio), gain: Number(gain) / 100, odd }} />
  </>;
}
