import { useEffect, useState } from 'react';

export type TelemetryProduct = { id: string; format: 'jpg' | 'png' | 'wav' | 'txt'; label: string; received: number; expected: number; complete: boolean; final?: boolean; revision?: number; utc?: string };
type ProductPage = { norad: number; day: string; items: TelemetryProduct[]; next: string | null };

function mergeProducts(history: TelemetryProduct[], live: TelemetryProduct[]) {
  const byId = new Map<string, TelemetryProduct>();
  for (const item of [...history, ...live]) {
    const key = item.id.replaceAll('-', '').toLowerCase();
    const previous = byId.get(key);
    if (!previous || (item.revision ?? 0) >= (previous.revision ?? 0)) byId.set(key, { ...item, id: key });
  }
  return [...byId.values()].sort((a, b) => (b.utc ?? '').localeCompare(a.utc ?? ''));
}

export function TelemetryMediaPanel({ products, available, norad }: { products: TelemetryProduct[]; available: boolean; norad: number }) {
  const [id, setId] = useState('');
  const [day, setDay] = useState(() => new Date().toISOString().slice(0, 10));
  const [history, setHistory] = useState<TelemetryProduct[]>([]);
  const [next, setNext] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => { setHistory([]); setNext(null); setError(''); }, [norad, day, available]);
  async function load(before?: string) {
    setBusy(true); setError('');
    try {
      const params = new URLSearchParams({ norad: String(norad), day: day.replaceAll('-', ''), limit: '30' });
      if (before) params.set('before', before);
      const response = await fetch('/api/v1/dsp/telemetry/products?' + params);
      if (!response.ok) throw new Error('Media catalog HTTP ' + response.status);
      const data = await response.json() as ProductPage;
      setHistory(old => before ? mergeProducts(old, data.items) : data.items);
      setNext(data.next);
    } catch (e) { setError(String(e)); }
    finally { setBusy(false); }
  }
  const all = mergeProducts(history, products);
  const product = all.find(item => item.id === id) ?? all[0];
  if (!available) return null;
  const url = product ? '/api/v1/dsp/telemetry/product/' + product.id + '/' + product.format + '?revision=' + (product.revision ?? 0) : '';
  return <section aria-label="Decoded satellite products">
    <header>DECODED MEDIA · HISTORY</header>
    <label>UTC DATE <input type="date" value={day} onChange={e => setDay(e.target.value)} /></label>
    <button disabled={busy || norad <= 0 || !day} onClick={() => void load()}>LOAD MEDIA</button>
    {next && <button disabled={busy} onClick={() => void load(next)}>OLDER MEDIA</button>}
    {error && <p role="alert">{error}</p>}
    {product && <>
      <label>PRODUCT <select value={product.id} onChange={e => setId(e.target.value)}>
        {all.map(item => <option key={item.id} value={item.id}>{item.utc?.slice(11, 19)} · {item.label}</option>)}
      </select></label>
      <p>{product.label} · {product.received}/{product.expected} · {product.final === false ? 'RECEIVING' : product.complete ? 'RECEIVED SPAN COMPLETE' : 'PARTIAL'}</p>
      {(product.format === 'jpg' || product.format === 'png') && <img key={url} src={url} alt={product.label} style={{ width: '100%', objectFit: 'contain' }} />}
      {product.format === 'wav' && <audio key={url} src={url} controls preload="metadata" />}
      <a href={url} download={product.id + '.' + product.format}>DOWNLOAD {product.format.toUpperCase()}</a>
    </>}
  </section>;
}
