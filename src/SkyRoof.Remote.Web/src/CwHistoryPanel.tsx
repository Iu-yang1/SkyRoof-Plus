import { useEffect, useState } from 'react';

type Entry = { sessionId: string; timelineGeneration: string | number; usesAssociationHint: boolean;
  identityId: number; mode: string; committedText: string; lastWindowEndUtc: string };

export function CwHistoryPanel({ available }: { available: boolean }) {
  const [entries, setEntries] = useState<Entry[]>([]);
  const [error, setError] = useState('');
  const [open, setOpen] = useState(false);
  useEffect(() => { if (!available) { setEntries([]); setOpen(false); } }, [available]);
  async function load() {
    if (!available) return;
    setOpen(true);
    try {
      const response = await fetch('/api/v1/dsp/cw/history', { credentials: 'include' });
      if (!response.ok) throw new Error(await response.text());
      const result = await response.json() as { entries: Entry[]; error: string };
      setEntries(result.entries); setError(result.error);
    } catch (e) { setError(String(e)); }
  }
  return <section>
    <button disabled={!available} onClick={() => void load()}>CW HISTORY / REFRESH</button>
    {open && <><button onClick={() => setOpen(false)}>CLOSE HISTORY</button>
      {error && <p role="status">{error}</p>}
      {!entries.length && !error && <p>No committed archive entries.</p>}
      {entries.map(entry => <div key={[entry.sessionId, entry.timelineGeneration, entry.mode, entry.usesAssociationHint, entry.identityId].join(':')}>
        <small>{entry.lastWindowEndUtc} · {entry.mode} · Lane {entry.identityId}</small>
        <pre style={{whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>{entry.committedText}</pre>
      </div>)}
      <small>Persisted committed text · latest 100 lanes from the two most recent archive files.</small>
    </>}
  </section>;
}
