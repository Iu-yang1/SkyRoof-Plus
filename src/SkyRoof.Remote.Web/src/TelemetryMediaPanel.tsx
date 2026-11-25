import { useState } from 'react';

export type TelemetryProduct = { id: string; format: 'jpg' | 'png' | 'wav' | 'txt'; label: string; received: number; expected: number; complete: boolean; utc?: string };

export function TelemetryMediaPanel({ products, available }: { products: TelemetryProduct[]; available: boolean }) {
  const [id, setId] = useState('');
  const product = products.find(item => item.id === id) ?? products[0];
  if (!available || !product) return null;
  const url = '/api/v1/dsp/telemetry/product/' + product.id + '/' + product.format;
  return <section aria-label="Decoded satellite products">
    <header>DECODED MEDIA</header>
    <label>PRODUCT <select value={product.id} onChange={e => setId(e.target.value)}>
      {products.map(item => <option key={item.id} value={item.id}>{item.utc?.slice(11, 19)} · {item.label}</option>)}
    </select></label>
    <p>{product.label} · {product.received}/{product.expected} · {product.complete ? 'RECEIVED SPAN COMPLETE' : 'PARTIAL'}</p>
    {(product.format === 'jpg' || product.format === 'png') && <img key={url} src={url} alt={product.label} style={{ width: '100%', objectFit: 'contain' }} />}
    {product.format === 'wav' && <audio key={url} src={url} controls preload="metadata" />}
    <a href={url} download={product.id + '.' + product.format}>DOWNLOAD {product.format.toUpperCase()}</a>
  </section>;
}
