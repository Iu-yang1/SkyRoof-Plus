import { useState } from 'react';

export function CameraPtzControls({ enabled, profile }: { enabled: boolean; profile: string }) {
  const [step, setStep] = useState(0.1);
  const [pending, setPending] = useState(false);
  const [status, setStatus] = useState('');

  async function command(action: 'relative' | 'stop', pan = 0, tilt = 0, zoom = 0) {
    if (!enabled || (pending && action !== 'stop')) return;
    setPending(true);
    try {
      const response = await fetch('/api/v1/camera/ptz/' + action, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(action === 'stop' ? { profile } : { profile, pan, tilt, zoom })
      });
      if (!response.ok) throw new Error('PTZ ' + action + ' HTTP ' + response.status);
      setStatus(action === 'stop' ? 'Stop accepted' : 'Relative move accepted');
    } catch (error) { setStatus(String(error)); }
    finally { setPending(false); }
  }

  const blocked = !enabled || pending;
  return <section aria-label="Camera PTZ controls">
    <label>PTZ STEP <input type="number" min="0.01" max="1" step="0.01" value={step}
      onChange={event => { const value = Number(event.target.value); if (value >= 0.01 && value <= 1) setStep(value); }} /></label>
    <div className="audio-buttons">
      <button disabled={blocked} onClick={() => void command('relative', -step)}>LEFT</button>
      <button disabled={blocked} onClick={() => void command('relative', step)}>RIGHT</button>
      <button disabled={blocked} onClick={() => void command('relative', 0, step)}>UP</button>
      <button disabled={blocked} onClick={() => void command('relative', 0, -step)}>DOWN</button>
      <button disabled={blocked} onClick={() => void command('relative', 0, 0, step)}>ZOOM +</button>
      <button disabled={blocked} onClick={() => void command('relative', 0, 0, -step)}>ZOOM -</button>
      <button disabled={!enabled} onClick={() => void command('stop')}>STOP PTZ</button>
    </div>
    {status && <small role="status">{status}</small>}
    <small>Relative translation in ONVIF generic space; camera must support RelativeMove. Acceptance is not position readback.</small>
  </section>;
}
