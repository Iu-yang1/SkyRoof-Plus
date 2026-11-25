import { useEffect, useState } from 'react';

type ArchivePage = { norad: number; day: string; items: { stem: string; bytes: number }[]; next: string | null };

export function TelemetryArchivePanel({ available, norad }: { available: boolean; norad: number }) {
  const [day, setDay] = useState(() => new Date().toISOString().slice(0, 10));
  const [page, setPage] = useState<ArchivePage>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => { setPage(undefined); setError(''); }, [norad, day, available]);

  async function load(before?: string) {
    setBusy(true); setError('');
    try {
      const params = new URLSearchParams({ norad: String(norad), day: day.replaceAll('-', ''), limit: '50' });
      if (before) params.set('before', before);
      const response = await fetch('/api/v1/dsp/telemetry/archive?' + params);
      if (!response.ok) throw new Error('Archive HTTP ' + response.status);
      setPage(await response.json() as ArchivePage);
    } catch (err) { setError(String(err)); }
    finally { setBusy(false); }
  }

  const visible = available && page?.norad === norad && page.day === day.replaceAll('-', '') ? page : undefined;
  return <section aria-label="Telemetry history">
    <header>TELEMETRY ARCHIVE · UTC</header>
    <label>DATE <input type="date" value={day} onChange={e => setDay(e.target.value)} /></label>
    <button disabled={!available || busy || !day || norad <= 0} onClick={() => void load()}>LOAD HISTORY</button>
    {visible && <div className="digital-messages">
      {visible.items.length === 0 && <p>No archived frames.</p>}
      {visible.items.map(item => {
        const base = '/api/v1/dsp/telemetry/archive/' + visible.norad + '/' + visible.day + '/' + item.stem;
        return <p key={item.stem}>{item.stem} · {item.bytes} B · <a href={base + '/bin'}>BIN</a> · <a href={base + '/log'}>LOG</a></p>;
      })}
      {visible.next && <button disabled={busy} onClick={() => void load(visible.next ?? undefined)}>OLDER FRAMES</button>}
    </div>}
    {error && <p role="alert">{error}</p>}
  </section>;
}
