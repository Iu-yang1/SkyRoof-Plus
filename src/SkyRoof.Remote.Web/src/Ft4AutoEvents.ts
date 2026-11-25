export type Ft4AutoEvent = {
  state: string; slotIndex?: number; stage?: string; peer?: string; message?: string;
  grid?: string; reportSent?: number; reportReceived?: number; error?: string;
};

export async function readFt4Events(stream: ReadableStream<Uint8Array>, publish: (event: Ft4AutoEvent) => void) {
  const reader = stream.getReader();
  const decoder = new TextDecoder();
  let pending = '';
  let terminal = false;
  const consume = (line: string) => {
    if (!line.trim()) return;
    const event = JSON.parse(line) as Ft4AutoEvent;
    publish(event);
    terminal = ['completed', 'attempts-exhausted', 'error'].includes(event.state);
  };
  try {
    while (true) {
      const result = await reader.read();
      if (result.done) break;
      pending += decoder.decode(result.value, { stream: true });
      const lines = pending.split('\n');
      pending = lines.pop() ?? '';
      for (const line of lines) consume(line);
    }
    pending += decoder.decode();
    if (pending.trim()) consume(pending);
    if (!terminal) throw new Error('Automatic digital session ended without a terminal receipt; check CAT RX.');
  } finally { reader.releaseLock(); }
}
