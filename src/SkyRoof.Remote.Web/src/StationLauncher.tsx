import { useState } from 'react';

export function StationLauncher() {
  const [address, setAddress] = useState('');
  const [error, setError] = useState('');
  function connect() {
    try {
      const url = new URL(address);
      if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) throw new Error('Use a station HTTP(S) URL without credentials.');
      window.open(url.href, '_blank', 'noopener,noreferrer');
      setError('');
    } catch (err) { setError(String(err)); }
  }
  return <div className="audio-buttons">
    <input aria-label="Other station URL" placeholder="https://station.example" value={address} onChange={e => setAddress(e.target.value)} />
    <button disabled={!address.trim()} onClick={connect}>OPEN STATION</button>
    {error && <small role="alert">{error}</small>}
  </div>;
}
