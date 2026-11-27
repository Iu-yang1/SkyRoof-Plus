import { useEffect, useRef } from 'react';
import { useCameraVideo } from './CameraVideoAttachment';

export function CameraVideoPlayer({ profile, media2 }: { profile: string; media2: boolean }) {
  const video = useRef<HTMLVideoElement>(null);
  const error = useCameraVideo(video, profile, media2);
  useEffect(() => {
    // Scroll only when the operator opens the video preview, not on every
    // fragment or playback update. PTZ and STOP remain in the same panel.
    video.current?.scrollIntoView({ block: 'start', inline: 'nearest' });
  }, []);
  return <><video ref={video} autoPlay muted playsInline controls style={{ width: '100%', maxHeight: '50vh', objectFit: 'contain', display: 'block' }} />
    {error && <p role="alert">{error}</p>}</>;
}
