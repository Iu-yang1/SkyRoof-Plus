import type { Config } from './Q65ReceivePanel';

export function Q65AdvancedConfig({ config, onChange }: { config: Config; onChange: (config: Config) => void }) {
  return <details>
    <summary>Q65 AP / EME OPTIONS</summary>
    <label>MY CALL <input maxLength={15} value={config.myCall} onChange={e => onChange({ ...config, myCall: e.target.value.toUpperCase() })} /></label>
    <label>DX CALL <input maxLength={15} value={config.hisCall} onChange={e => onChange({ ...config, hisCall: e.target.value.toUpperCase() })} /></label>
    <label>DX GRID <input maxLength={7} value={config.hisGrid} onChange={e => onChange({ ...config, hisGrid: e.target.value.toUpperCase() })} /></label>
    <label>AP <select value={config.apMode} onChange={e => onChange({ ...config, apMode: Number(e.target.value) })}>
      <option value={0}>OFF</option><option value={1}>CQ ONLY</option><option value={2}>FULL</option></select></label>
    <label>QSO STAGE <select value={config.qsoProgress} onChange={e => onChange({ ...config, qsoProgress: Number(e.target.value) })}>
      {['CALLING', 'REPLYING', 'REPORT', 'ROGER REPORT', 'ROGERS', 'SIGNOFF'].map((stage, i) => <option key={stage} value={i}>{stage}</option>)}</select></label>
    <label>MAX DRIFT (bins) <input type="number" min={0} max={50} value={config.maxDrift} onChange={e => onChange({ ...config, maxDrift: Number(e.target.value) })} /></label>
    <label><input type="checkbox" checked={config.pileup} onChange={e => onChange({ ...config, pileup: e.target.checked })} /> PILEUP RX</label>
    <label>FADING B90×Ts (0=OFF) <input type="number" min={0} step={0.1} value={config.fadingB90Ts} onChange={e => onChange({ ...config, fadingB90Ts: Number(e.target.value) })} /></label>
    <label>FADING MODEL <select value={config.lorentzian ? 'lorentzian' : 'gaussian'} onChange={e => onChange({ ...config, lorentzian: e.target.value === 'lorentzian' })}>
      <option value="gaussian">GAUSSIAN</option><option value="lorentzian">LORENTZIAN</option></select></label>
  </details>;
}
