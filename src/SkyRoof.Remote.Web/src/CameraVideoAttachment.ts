import { useEffect, useState, type RefObject } from 'react';
import { streamCamera } from './CameraVideoStream';

export function useCameraVideo(video: RefObject<HTMLVideoElement | null>, profile: string, media2: boolean) {
  const [error, setError] = useState('');
  useEffect(() => {
    const target = video.current;
    if (!target) return;
    if (!('MediaSource' in window)) {
      setError('MediaSource is not supported by this browser.');
      return;
    }
    return attachCamera(target, profile, media2, setError);
  }, [video, profile, media2]);
  return error;
}

function attachCamera(target: HTMLVideoElement, profile: string, media2: boolean, setError: (message: string) => void) {
  const abort = new AbortController();
  const source = new MediaSource();
  const object = URL.createObjectURL(source);
  target.src = object;
  setError('');
  const reportFailure = (error: unknown) => {
    if (!abort.signal.aborted) setError(String(error));
  };
  const open = () => {
    void streamCamera(source, target, profile, media2, abort.signal).catch(reportFailure);
  };
  source.addEventListener('sourceopen', open, { once: true });
  return () => {
    abort.abort();
    source.removeEventListener('sourceopen', open);
    target.pause();
    target.removeAttribute('src');
    target.load();
    URL.revokeObjectURL(object);
  };
}
