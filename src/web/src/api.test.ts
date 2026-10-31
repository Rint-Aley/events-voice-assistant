import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, requestUpload, submitMessage, turnErrorMessage, userFacingError } from './api';

describe('requestUpload', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('rejects unsupported formats before making a request', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    await expect(requestUpload('audio/mp4', 1024)).rejects.toMatchObject({
      code: 'UNSUPPORTED_AUDIO',
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('sends the upload metadata using the HTTP contract', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          object_key: 'audio/DEMO/example.webm',
          upload_url: 'https://storage.example/upload',
          expires_at: '2026-10-26T12:00:00Z',
        }),
        { status: 201, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    await requestUpload('audio/webm', 2048);

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/uploads',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ content_type: 'audio/webm', size_bytes: 2048 }),
      }),
    );
  });
});

describe('submitMessage', () => {
  it('sends exactly one of text or object_key and an idempotency key', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue('00000000-0000-4000-8000-000000000001');
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          turn_id: '00000000-0000-4000-8000-000000000002',
          state: 'INTERPRETING',
          events_url: '/api/v1/turns/00000000-0000-4000-8000-000000000002/events',
        }),
        { status: 202, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    await submitMessage('00000000-0000-4000-8000-000000000003', {
      text: 'Что у меня сегодня?',
    });

    const [, init] = fetchMock.mock.calls[0];
    expect(JSON.parse(String(init?.body))).toEqual({
      session_id: '00000000-0000-4000-8000-000000000003',
      idempotency_key: '00000000-0000-4000-8000-000000000001',
      text: 'Что у меня сегодня?',
      object_key: null,
    });
  });
});

describe('user-facing errors', () => {
  it('maps a busy session to a retry hint', () => {
    expect(userFacingError(new ApiError('busy', 409, 'SESSION_BUSY'))).toBe(
      'Подождите, пока завершится предыдущий запрос.',
    );
  });

  it('maps domain errors without showing internal error codes', () => {
    expect(turnErrorMessage({ code: 'EMPTY_TRANSCRIPT', message: 'internal details' })).toContain(
      'Не расслышал',
    );
    expect(turnErrorMessage({ code: 'NLU_FAILED', message: 'internal details' })).toContain(
      'переформулировать',
    );
  });
});
