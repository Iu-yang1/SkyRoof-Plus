import { useState } from 'react';
import type { CatalogLink } from './SatelliteCatalogPicker';

function number(form: FormData, name: string, scale = 1) {
  const text = String(form.get(name) ?? '0').trim();
  const value = Number(text || '0') * scale;
  if (!Number.isFinite(value)) throw new Error(name + ' must be numeric.');
  return value;
}

export function readRadioLink(form: FormData) {
  if (!form.has('radioLink')) return null;
  return {
    DownlinkLowHz: Math.round(number(form, 'rx', 1e6)),
    DownlinkHighHz: Math.round(number(form, 'rxHigh', 1e6)),
    UplinkLowHz: Math.round(number(form, 'tx', 1e6)),
    UplinkHighHz: Math.round(number(form, 'txHigh', 1e6)),
    Inverting: form.has('inverting'),
    DownlinkBaseOffsetHz: Math.round(number(form, 'rxBase')),
    UplinkBaseOffsetHz: Math.round(number(form, 'txBase')),
    TransponderOffsetHz: number(form, 'position'),
    RitHz: number(form, 'rit'),
    XitHz: number(form, 'xit'),
    DownlinkManualHz: number(form, 'rxManual'),
    UplinkManualHz: number(form, 'txManual')
  };
}

export function RadioLinkOptions({ seed }: { seed?: CatalogLink }) {
  const [enabled, setEnabled] = useState(Boolean(seed));
  return <details className="tracking-link">
    <summary>RADIO LINK / TRANSPONDER</summary>
    <label><input type="checkbox" name="radioLink" checked={enabled}
      onChange={e => setEnabled(e.target.checked)} /> USE PASSBAND MODEL</label>
    <fieldset disabled={!enabled}>
      <small>Enter low edges in DOWNLINK / UPLINK above. Use 0 for an absent link.</small>
      <div className="tracking-grid">
        <label>RX HIGH MHz<input name="rxHigh" type="number" step="any" defaultValue={(seed?.downlinkHighHz ?? 0) / 1e6} /></label>
        <label>TX HIGH MHz<input name="txHigh" type="number" step="any" defaultValue={(seed?.uplinkHighHz ?? 0) / 1e6} /></label>
        <label>RX BASE Δ Hz<input name="rxBase" type="number" step="1" defaultValue="0" /></label>
        <label>TX BASE Δ Hz<input name="txBase" type="number" step="1" defaultValue="0" /></label>
        <label>POSITION Hz<input name="position" type="number" step="any" defaultValue="0" /></label>
        <label>RIT Hz<input name="rit" type="number" step="any" defaultValue="0" /></label>
        <label>XIT Hz<input name="xit" type="number" step="any" defaultValue="0" /></label>
        <label>RX MANUAL Hz<input name="rxManual" type="number" step="any" defaultValue="0" /></label>
        <label>TX MANUAL Hz<input name="txManual" type="number" step="any" defaultValue="0" /></label>
      </div>
      <label><input type="checkbox" name="inverting" defaultChecked={seed?.inverting ?? false} /> INVERTING TRANSPONDER</label>
      <small>Position is not clamped to the published database edges.</small>
    </fieldset>
  </details>;
}
