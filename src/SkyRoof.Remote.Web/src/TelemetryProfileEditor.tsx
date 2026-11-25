import { telemetryProfiles, type TelemetryConfig } from './TelemetryProfiles';

export function TelemetryProfileEditor({ config, onChange }: { config: TelemetryConfig; onChange: (config: TelemetryConfig) => void }) {
  return <details open>
    <summary>RX PROFILE</summary>
    <label>PRESET <select value="" onChange={e => {
      const name = e.target.value as keyof typeof telemetryProfiles;
      if (telemetryProfiles[name]) onChange({ ...telemetryProfiles[name] });
    }}><option value="">CUSTOM</option>{Object.keys(telemetryProfiles).map(name => <option key={name}>{name}</option>)}</select></label>
    <label>SATELLITE <input value={config.satellite} onChange={e => onChange({ ...config, satellite: e.target.value })} /></label>
    <label>NORAD <input type="number" value={config.noradId} onChange={e => onChange({ ...config, noradId: Number(e.target.value) })} /></label>
    <label>INPUT <select value={config.input} onChange={e => onChange({ ...config, input: e.target.value })}>
      <option value="tone-audio">TONE AF</option><option value="fm-discriminator">FM DISCRIMINATOR</option><option value="real-if">REAL 12 kHz IF</option></select></label>
    <label>MOD <select value={config.modulation} onChange={e => onChange({ ...config, modulation: e.target.value })}>
      {['BPSK','QPSK','AFSK','FSK','GFSK','GMSK'].map(v => <option key={v}>{v}</option>)}</select></label>
    <label>FRAME <select value={config.framing} onChange={e => onChange({ ...config, framing: e.target.value })}>
      {['AO40FEC','AX25G3RUH','AX100ASM','AX100RS','CCSDS','USP','GEOSCAN','HADES'].map(v => <option key={v}>{v}</option>)}</select></label>
    <label>BAUD <input type="number" value={config.baud} onChange={e => onChange({ ...config, baud: Number(e.target.value) })} /></label>
    <label>CENTER Hz <input type="number" value={config.centerHz} onChange={e => onChange({ ...config, centerHz: Number(e.target.value) })} /></label>
    <label>DEVIATION Hz <input type="number" value={config.deviationHz} onChange={e => onChange({ ...config, deviationHz: Number(e.target.value) })} /></label>
    <label><input type="checkbox" checked={config.differential} onChange={e => onChange({ ...config, differential: e.target.checked })} /> DBPSK</label>
    <label><input type="checkbox" checked={config.manchester} onChange={e => onChange({ ...config, manchester: e.target.checked })} /> MANCHESTER</label>
    <small>Input describes existing station PCM. REAL IF requires the station source to actually supply 12 kHz IF.</small>
  </details>;
}
