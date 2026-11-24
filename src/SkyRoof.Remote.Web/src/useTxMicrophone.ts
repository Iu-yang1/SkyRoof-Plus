import { useEffect, useRef, useState } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import { TxMicrophone } from './TxMicrophone';

export function useTxMicrophone(hub: HubConnection | null) {
  const capture = useRef<TxMicrophone | null>(null);
  const [status, setStatus] = useState('MIC OFF');
  const [enabled, setEnabled] = useState(false);
  useEffect(() => {
    if (!hub) return;
    let disposed = false;
    const microphone = new TxMicrophone(hub, message => {
      if (!disposed) { setStatus(message); setEnabled(microphone.enabled); }
    });
    capture.current = microphone;
    return () => {
      disposed = true;
      microphone.disable();
      capture.current = null;
    };
  }, [hub]);

  return {
    status, enabled,
    toggle: () => {
      const mic = capture.current;
      if (!mic) return;
      if (mic.enabled) mic.disable();
      else void mic.enable();
    },
    grant: (id: string, epoch: number) => capture.current?.grant(id, epoch),
    revoke: () => capture.current?.revoke()
  };
}
