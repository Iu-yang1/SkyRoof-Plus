import { useState, useEffect } from 'react';

type Status = { configured: boolean; running: boolean; state: string; detail: string };

export function SdrControl({ available, channelHz }: { available: boolean; channelHz: number }) {
  const [center, setCenter] = useState(String(channelHz || 435000000));
  const [channel, setChannel] = useState(center);
  const [rate, setRate] = useState('1000000');
  const [gain, setGain] = useState('');
  const [status, setStatus] = useState<Status>();
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  async function command(method: string) {
    setBusy(true); setError('');
    try {
      const body = { centerHz: Number(center), channelHz: Number(channel), sampleRate: Number(rate),
        ...(gain.trim() ? { gainDb: Number(gain) } : {}) };
      const response = await fetch('/api/v1/sdr/', { method, credentials: 'include',
        ...(method === 'PUT' ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {}) });
      if (!response.ok) throw new Error(await response.text());
      setStatus(await response.json() as Status);
    } catch (e) { setError(String(e)); }
    finally { setBusy(false); }
  }
  useEffect(() => {
    if (!available) return;
    const abort = new AbortController();
    const poll = async () => {
      try {
        const response = await fetch('/api/v1/sdr/', { signal: abort.signal, credentials: 'include' });
        if (response.ok && !abort.signal.aborted) setStatus(await response.json() as Status);
      } catch { /* status remains at the last confirmed value */ }
    };
    void poll();
    const timer = window.setInterval(() => void poll(), 1500);
    return () => { abort.abort(); window.clearInterval(timer); };
  }, [available]);
  return <details><summary>STATION SDR · IQ SOURCE</summary>
    <label>CENTER Hz<input value={center} onChange={e => setCenter(e.target.value)} /></label>
    <label>CHANNEL Hz<input value={channel} onChange={e => setChannel(e.target.value)} /></label>
    <button onClick={() => setChannel(String(channelHz))}>USE DOWNLINK</button>
    <label>SAMPLE RATE<input value={rate} onChange={e => setRate(e.target.value)} /></label>
    <label>GAIN dB<input value={gain} placeholder="Device default" onChange={e => setGain(e.target.value)} /></label>
    <button disabled={!available || busy || !status?.configured} onClick={() => void command('PUT')}>START / RETUNE SDR</button>
    <button disabled={!available || busy} onClick={() => void command('DELETE')}>STOP SDR</button>
    <p>{status?.state ?? 'UNAVAILABLE'} · {status?.detail}</p>
    {error && <p role="alert">{error}</p>}
  </details>;
}
