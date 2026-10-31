export const MAX_RECORDING_MS = 60_000;
export const MAX_AUDIO_BYTES = 10 * 1024 * 1024;

const RECORDING_OPTIONS = [
  { mimeType: 'audio/webm;codecs=opus', contentType: 'audio/webm', extension: 'webm' },
  { mimeType: 'audio/ogg;codecs=opus', contentType: 'audio/ogg', extension: 'ogg' },
  { mimeType: 'audio/webm', contentType: 'audio/webm', extension: 'webm' },
  { mimeType: 'audio/ogg', contentType: 'audio/ogg', extension: 'ogg' },
  { mimeType: 'audio/wav', contentType: 'audio/wav', extension: 'wav' },
  { mimeType: 'audio/mpeg', contentType: 'audio/mpeg', extension: 'mp3' },
] as const;

export type RecordingFormat = (typeof RECORDING_OPTIONS)[number];

export function findRecordingFormat(
  isTypeSupported: (mimeType: string) => boolean,
): RecordingFormat | null {
  return RECORDING_OPTIONS.find(({ mimeType }) => isTypeSupported(mimeType)) ?? null;
}

export function formatDuration(milliseconds: number): string {
  const seconds = Math.floor(milliseconds / 1000);
  const minutes = Math.floor(seconds / 60);
  return `${minutes}:${String(seconds % 60).padStart(2, '0')}`;
}
