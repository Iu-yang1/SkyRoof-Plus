import { useEffect, useState } from 'react';
import type { HubConnection } from '@microsoft/signalr';

type MediaRxStatus = {
  epoch: number; source: string; streamActive: boolean; state: string;
  framesWritten: number; samplesSkipped: number;
  datagramsReceived: number; datagramsOutOfOrder: number;
  datagramsDropped: number; datagramsMissing: number;
};

export function useMediaRxStatus(hub: HubConnection | null) {
  const [media, setMedia] = useState<MediaRxStatus | null>(null);
  const [mediaMessage, setMediaMessage] = useState('MEDIA · CHECKING');

  useEffect(() => {
    if (!hub) {
      setMedia(null);
      setMediaMessage('MEDIA · NO STATION LINK');
      return;
    }
    let closed = false;
    let pending = false;
    async function refresh() {
      if (pending || closed) return;
      pending = true;
      try {
        const response = await fetch('/api/v1/media/rx-status', { credentials: 'include' });
        if (!response.ok) throw new Error('Media HTTP ' + response.status);
        const result = await response.json() as MediaRxStatus;
        if (!closed) {
          setMedia(result);
          setMediaMessage(result.streamActive ? 'MEDIA · RX ACTIVE' : 'MEDIA · IDLE');
        }
      } catch (error) {
        if (!closed) {
          setMedia(null);
          setMediaMessage(String(error));
        }
      } finally { pending = false; }
    }
    void refresh();
    const timer = window.setInterval(() => { void refresh(); }, 2000);
    return () => { closed = true; window.clearInterval(timer); };
  }, [hub]);

  return { media, mediaMessage };
}
