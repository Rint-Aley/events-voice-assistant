import { describe, expect, it, vi } from 'vitest';
import { findRecordingFormat, formatDuration } from './voice';

describe('findRecordingFormat', () => {
  it('selects the first browser-supported format accepted by the API', () => {
    const format = findRecordingFormat((mimeType) => mimeType === 'audio/ogg;codecs=opus');
    expect(format).toEqual({
      mimeType: 'audio/ogg;codecs=opus',
      contentType: 'audio/ogg',
      extension: 'ogg',
    });
  });

  it('returns null when the browser does not support an accepted recording format', () => {
    expect(findRecordingFormat(vi.fn(() => false))).toBeNull();
  });
});

describe('formatDuration', () => {
  it('formats elapsed time as minutes and seconds', () => {
    expect(formatDuration(0)).toBe('0:00');
    expect(formatDuration(61_000)).toBe('1:01');
    expect(formatDuration(60_000)).toBe('1:00');
  });
});
