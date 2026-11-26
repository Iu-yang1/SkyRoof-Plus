import { useRef } from 'react';
import { useCameraVideo } from './CameraVideoAttachment';

export function CameraVideoPlayer({ profile, media2 }: { profile: string; media2: boolean }) {
  const video = useRef<HTMLVideoElement>(null);
  const error = useCameraVideo(video, profile, media2);
  return <><video ref={video} autoPlay muted playsInline controls style={{ width: '100%' }} />
    {error && <p role="alert">{error}</p>}</>;
}
