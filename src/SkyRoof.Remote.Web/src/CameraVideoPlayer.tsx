import { useEffect, useRef, useState } from 'react';

export function CameraVideoPlayer({ profile, media2 }: { profile: string; media2: boolean }) {
  const video = useRef<HTMLVideoElement>(null);
  const [error, setError] = useState('');
  useEffect(() => {
    const target = video.current;
    if (!target) return;
    if (!('MediaSource' in window)) { setError('MediaSource is not supported by this browser.'); return; }
    const abort = new AbortController();
    const source = new MediaSource();
    const object = URL.createObjectURL(source);
    target.src = object;
    setError('');
    const open = () => void streamCamera(source, target, profile, media2, abort.signal)
      .catch(e => { if (!abort.signal.aborted) setError(String(e)); });
    source.addEventListener('sourceopen', open, { once: true });
    return () => {
      abort.abort();
      source.removeEventListener('sourceopen', open);
      target.pause();
      target.removeAttribute('src');
      target.load();
      URL.revokeObjectURL(object);
    };
  }, [profile, media2]);
  return <><video ref={video} autoPlay muted playsInline controls style={{ width: '100%' }} />
    {error && <p role="alert">{error}</p>}</>;
}

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

async function streamCamera(source: MediaSource, video: HTMLVideoElement, profile: string, media2: boolean, signal: AbortSignal) {
  const response = await fetch('/api/v1/camera/video?profile=' + encodeURIComponent(profile) + '&media2=' + media2, { signal });
  if (!response.ok || !response.body) throw new Error('Camera video HTTP ' + response.status);
  const reader = response.body.getReader();
  let initial = new Uint8Array(0);
  let buffer: SourceBuffer | undefined;
  try {
    while (!signal.aborted) {
      const { done, value } = await reader.read();
      if (done) break;
      if (!buffer) {
        const joined = new Uint8Array(initial.length + value.length);
        joined.set(initial); joined.set(value, initial.length); initial = joined;
        const codec = avcCodec(initial);
        if (!codec) {
          if (initial.length > 1024 * 1024) throw new Error('Select an H.264 camera profile.');
          continue;
        }
        const mime = 'video/mp4; codecs="' + codec + '"';
        if (!MediaSource.isTypeSupported(mime)) throw new Error('Browser does not support camera codec ' + codec);
        buffer = source.addSourceBuffer(mime);
        const first = initial;
        await update(buffer, signal, () => buffer?.appendBuffer(first));
        initial = new Uint8Array(0);
      } else {
        const chunk = new Uint8Array(value);
        await update(buffer, signal, () => buffer?.appendBuffer(chunk));
      }
      if (buffer.buffered.length) {
        const end = buffer.buffered.end(buffer.buffered.length - 1);
        const start = buffer.buffered.start(0);
        if (video.currentTime < end - 5 && end > start + 2) video.currentTime = end - 2;
        if (end - start > 30) await update(buffer, signal, () => buffer?.remove(start, end - 20));
      }
    }
    if (!signal.aborted && !buffer) throw new Error('Camera returned no supported H.264 initialization segment.');
    if (!signal.aborted && source.readyState === 'open') source.endOfStream();
  } finally { await reader.cancel(); reader.releaseLock(); }
}
