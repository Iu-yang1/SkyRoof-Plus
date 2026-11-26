function avcCodec(bytes: Uint8Array): string | undefined {
  for (let i = 4; i + 7 < bytes.length; i++) {
    if (bytes[i] === 97 && bytes[i + 1] === 118 && bytes[i + 2] === 99 && bytes[i + 3] === 67 && bytes[i + 4] === 1)
      return 'avc1.' + Array.from(bytes.slice(i + 5, i + 8), x => x.toString(16).padStart(2, '0')).join('').toUpperCase();
  }
  return undefined;
}

function update(buffer: SourceBuffer, signal: AbortSignal, action: () => void): Promise<void> {
  return new Promise((resolve, reject) => {
    function cleanup() {
      buffer.removeEventListener('updateend', done);
      buffer.removeEventListener('error', failed);
      signal.removeEventListener('abort', cancelled);
    }
    function done() { cleanup(); resolve(); }
    function failed() { cleanup(); reject(new Error('Camera video SourceBuffer error.')); }
    function cancelled() { cleanup(); reject(new DOMException('Cancelled', 'AbortError')); }
    if (signal.aborted) { cancelled(); return; }
    buffer.addEventListener('updateend', done, { once: true });
    buffer.addEventListener('error', failed, { once: true });
    signal.addEventListener('abort', cancelled, { once: true });
    try { action(); } catch (error) { cleanup(); reject(error); }
  });
}

function createBuffer(source: MediaSource, codec: string): SourceBuffer {
  const mime = 'video/mp4; codecs="' + codec + '"';
  if (!MediaSource.isTypeSupported(mime)) throw new Error('Browser does not support camera codec ' + codec);
  return source.addSourceBuffer(mime);
}

async function initializeBuffer(source: MediaSource, reader: ReadableStreamDefaultReader<Uint8Array>, signal: AbortSignal) {
  let initial = new Uint8Array(0);
  while (!signal.aborted) {
    const { done, value } = await reader.read();
    if (done) break;
    const joined = new Uint8Array(initial.length + value.length);
    joined.set(initial);
    joined.set(value, initial.length);
    initial = joined;
    const codec = avcCodec(initial);
    if (codec) {
      const buffer = createBuffer(source, codec);
      const first = initial;
      await update(buffer, signal, () => buffer.appendBuffer(first));
      return buffer;
    }
    if (initial.length > 1024 * 1024) throw new Error('Select an H.264 camera profile.');
  }
  if (signal.aborted) return null;
  throw new Error('Camera returned no supported H.264 initialization segment.');
}

async function trimLiveWindow(buffer: SourceBuffer, video: HTMLVideoElement, signal: AbortSignal) {
  if (!buffer.buffered.length) return;
  const end = buffer.buffered.end(buffer.buffered.length - 1);
  const start = buffer.buffered.start(0);
  if (video.currentTime < end - 5 && end > start + 2) video.currentTime = end - 2;
  if (end - start > 30) await update(buffer, signal, () => buffer.remove(start, end - 20));
}

export async function streamCamera(source: MediaSource, video: HTMLVideoElement, profile: string, media2: boolean, signal: AbortSignal) {
  const response = await fetch('/api/v1/camera/video?profile=' + encodeURIComponent(profile) + '&media2=' + media2, { signal });
  if (!response.ok || !response.body) throw new Error('Camera video HTTP ' + response.status);
  const reader = response.body.getReader();
  try {
    const buffer = await initializeBuffer(source, reader, signal);
    if (!buffer) return;
    await trimLiveWindow(buffer, video, signal);
    while (!signal.aborted) {
      const { done, value } = await reader.read();
      if (done) break;
      const chunk = new Uint8Array(value);
      await update(buffer, signal, () => buffer.appendBuffer(chunk));
      await trimLiveWindow(buffer, video, signal);
    }
    if (!signal.aborted && source.readyState === 'open') source.endOfStream();
  } finally {
    try { await reader.cancel(); } finally { reader.releaseLock(); }
  }
}
