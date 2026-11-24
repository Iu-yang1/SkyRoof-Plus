import { useState } from 'react';

export type CatalogLink = {
  downlinkLowHz: number; downlinkHighHz: number; uplinkLowHz: number; uplinkHighHz: number;
  inverting: boolean;
};
type CatalogTransmitter = { id: string; description: string; alive: boolean; status: string;
  mode: string; uplinkMode: string; link: CatalogLink };
type CatalogSatellite = { satelliteId: string; norad: number; name: string; status: string;
  tleLine1: string; tleLine2: string; orbitSource: string; orbitState: string; transmitters: CatalogTransmitter[] };
export type CatalogSelection = { satellite: CatalogSatellite; transmitter: CatalogTransmitter };

export function SatelliteCatalogPicker({ available, onSelect }: {
  available: boolean; onSelect: (value: CatalogSelection) => void
}) {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<CatalogSatellite[]>([]);
  const [satId, setSatId] = useState('');
  const [txId, setTxId] = useState('');
  const [status, setStatus] = useState('Search station satellite cache.');
  const [busy, setBusy] = useState(false);
  const satellite = results.find(item => item.satelliteId === satId);
  const transmitter = satellite?.transmitters.find(item => item.id === txId);

  async function search() {
    if (!available || busy) return;
    setBusy(true);
    try {
      const response = await fetch('/api/v1/catalog/satellites?q=' + encodeURIComponent(query),
        { credentials: 'include' });
      if (!response.ok) throw new Error('Catalog HTTP ' + response.status);
      const entries = await response.json() as CatalogSatellite[];
      setResults(entries);
      setSatId(entries[0]?.satelliteId ?? '');
      setTxId(entries[0]?.transmitters[0]?.id ?? '');
      setStatus(entries.length + ' records');
    } catch (e) {
      setResults([]); setSatId(''); setTxId('');
      setStatus(String(e));
    } finally { setBusy(false); }
  }

  return <section className="catalog-picker">
    <div className="audio-buttons">
      <input aria-label="Satellite or NORAD" placeholder="Satellite / NORAD"
        value={query} onChange={e => setQuery(e.target.value)}
        onKeyDown={e => { if (e.key === 'Enter') void search(); }} />
      <button type="button" disabled={!available || busy} onClick={() => void search()}>SEARCH</button>
    </div>
    <small>{status}</small>
    {results.length > 0 && <>
      <label>SATELLITE<select value={satId} onChange={e => {
        setSatId(e.target.value);
        setTxId(results.find(x => x.satelliteId === e.target.value)?.transmitters[0]?.id ?? '');
      }}>{results.map(item => <option key={item.satelliteId} value={item.satelliteId}>
        {item.name} · {item.norad || '—'} · {item.orbitState}
      </option>)}</select></label>
      <label>TRANSMITTER<select value={txId} onChange={e => setTxId(e.target.value)}>
        {(satellite?.transmitters ?? []).map(item => <option key={item.id} value={item.id}>
          {item.description} · {item.mode} · {item.alive ? 'ALIVE' : item.status || 'INACTIVE'}
        </option>)}</select></label>
      <button type="button" disabled={!available || !satellite || !transmitter || !['tle', 'omm-only'].includes(satellite.orbitState)}
        onClick={() => { if (satellite && transmitter) onSelect({ satellite, transmitter }); }}>
        LOAD RADIO LINK
      </button>
      <small>Source: {satellite?.orbitSource || 'unavailable'}. Orbit is resolved by Station on every tracking tick.</small>
    </>}
  </section>;
}
