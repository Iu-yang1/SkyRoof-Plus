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
      <option value="tone-audio">TONE AF</option><option value="fm-discriminator">FM DISCRIMINATOR</option><option value="real-if">REAL 12 kHz IF</option><option value="iq-cf32">SDR IQ · 48 kHz CF32</option></select></label>
    <label>MOD <select value={config.modulation} onChange={e => onChange({ ...config, modulation: e.target.value })}>
      {['BPSK','QPSK','AFSK','FSK','GFSK','GMSK'].map(v => <option key={v}>{v}</option>)}</select></label>
    <label>FRAME <select value={config.framing} onChange={e => onChange({ ...config, framing: e.target.value })}>
      {['AO40FEC','AX25G3RUH','AX100ASM','AX100RS','CCSDS','USP','GEOSCAN','HADES'].map(v => <option key={v}>{v}</option>)}</select></label>
    <label>BAUD <input type="number" value={config.baud} onChange={e => onChange({ ...config, baud: Number(e.target.value) })} /></label>
    <label>CENTER Hz <input type="number" value={config.centerHz} onChange={e => onChange({ ...config, centerHz: Number(e.target.value) })} /></label>
    <label>DEVIATION Hz <input type="number" value={config.deviationHz} onChange={e => onChange({ ...config, deviationHz: Number(e.target.value) })} /></label>
    <label>PRECODING <select value={config.differentialMode || (config.differential ? 'differential' : 'auto')}
      onChange={e => onChange({ ...config, differentialMode: e.target.value, differential: e.target.value === 'differential' })}>
      <option value="auto">AUTO</option><option value="coherent">NONE / COHERENT</option><option value="differential">DIFFERENTIAL / NRZI</option></select></label>
    <label>DECODER <select value={config.decoderProtocol} onChange={e => onChange({ ...config, decoderProtocol: e.target.value })}>
      <option value="">STANDARD</option><option value="unne1b">UNNE-1B</option></select></label>
    {config.framing === 'CCSDS' && <details><summary>CCSDS</summary>
      <label>FRAME SIZE <input type="number" min="0" value={config.frameSize} onChange={e => onChange({ ...config, frameSize: Number(e.target.value) })} /></label>
      <label>RS INTERLEAVING <input type="number" min="0" value={config.rsInterleaving} onChange={e => onChange({ ...config, rsInterleaving: Number(e.target.value) })} /></label>
      <label>RS BASIS <select value={config.rsBasis} onChange={e => onChange({ ...config, rsBasis: e.target.value })}><option value="">AUTO</option><option value="conventional">CONVENTIONAL</option><option value="dual">DUAL</option></select></label>
      <label>RS <select value={config.rsMode} onChange={e => onChange({ ...config, rsMode: e.target.value })}><option value="">AUTO</option><option value="on">ON</option><option value="off">OFF</option></select></label>
      <label>SCRAMBLER <select value={config.scramblerMode} onChange={e => onChange({ ...config, scramblerMode: e.target.value })}><option value="">AUTO</option><option value="on">ON</option><option value="off">OFF</option></select></label>
      <label>CONVOLUTIONAL <select value={config.convolutional} onChange={e => onChange({ ...config, convolutional: e.target.value })}><option value="">NONE</option>
        {['CCSDS','CCSDS uninverted','NASA-DSN','NASA-DSN uninverted'].map(v => <option key={v}>{v}</option>)}</select></label>
    </details>}
    <label><input type="checkbox" checked={config.manchester} onChange={e => onChange({ ...config, manchester: e.target.checked })} /> MANCHESTER</label>
    <label><input type="checkbox" checked={config.decodeMedia} onChange={e => onChange({ ...config, decodeMedia: e.target.checked })} /> DECODE SSDV / JPEG / CODEC2</label>
    <label><input type="checkbox" checked={config.decodeSstv} onChange={e => onChange({ ...config, decodeSstv: e.target.checked, sstvOnly: e.target.checked && config.sstvOnly })} /> DECODE SSTV (FM / IQ)</label>
    {config.decodeSstv && <label><input type="checkbox" checked={config.sstvOnly} onChange={e => onChange({ ...config, sstvOnly: e.target.checked })} /> SSTV ONLY</label>}
    <label><input type="checkbox" checked={config.saveFrames} onChange={e => onChange({ ...config, saveFrames: e.target.checked })} /> SAVE BIN + LOG ON STATION</label>
    <label>KISS TCP PORT (0=OFF) <input type="number" min={0} max={65535} value={config.kissPort} onChange={e => onChange({ ...config, kissPort: Number(e.target.value) })} /></label>
    <label><input type="checkbox" checked={config.uploadSatnogs} onChange={e => onChange({ ...config, uploadSatnogs: e.target.checked })} /> SUBMIT TO SATNOGS (station credentials)</label>
    <small>Input describes actual source. IQ mode requires a local DSP IPC CF32 producer; REAL IF requires real 12 kHz IF.</small>
  </details>;
}
