export type TurnState =
  | 'TRANSCRIBING'
  | 'INTERPRETING'
  | 'APPLYING'
  | 'COMPLETED'
  | 'NEEDS_CLARIFICATION'
  | 'FAILED'
  | 'EXPIRED';

export interface EventItem {
  id: string;
  kind: 'task' | 'reminder' | 'meeting';
  description: string | null;
  starts_on: string | null;
  starts_at: string | null;
  duration_minutes?: number | null;
}

export interface AnswerPayload {
  text: string;
  events: EventItem[];
}

export interface AudioPayload {
  url: string;
  duration_ms: number;
}

export interface TurnError {
  code: string;
  message: string;
}

export interface TurnSnapshot {
  turn_id: string;
  state: TurnState;
  text_input: string | null;
  answer: string | null;
  events: EventItem[];
  audio: AudioPayload | null;
  error: TurnError | null;
}

interface UploadRequest {
  content_type: string;
  size_bytes: number;
}

export interface UploadResponse {
  object_key: string;
  upload_url: string;
  expires_at: string;
}

export interface MessageResponse {
  turn_id: string;
  state: TurnState;
  events_url: string;
}

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || '/api/v1').replace(/\/$/, '');
const MAX_AUDIO_BYTES = 10 * 1024 * 1024;
const ALLOWED_AUDIO_TYPES = new Set([
  'audio/webm',
  'audio/ogg',
  'audio/wav',
  'audio/mpeg',
]);

function apiUrl(path: string): string {
  return `${API_BASE_URL}${path}`;
}

async function jsonRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(apiUrl(path), {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  });

  if (!response.ok) {
    let code = `HTTP_${response.status}`;
    let message = `Сервер вернул ошибку (${response.status}).`;

    try {
      const body: unknown = await response.json();
      if (isRecord(body) && isRecord(body.error)) {
        if (typeof body.error.code === 'string') code = body.error.code;
        if (typeof body.error.message === 'string') message = body.error.message;
      }
    } catch (error) {
      if (!(error instanceof SyntaxError)) throw error;
    }

    throw new ApiError(message, response.status, code);
  }

  return (await response.json()) as T;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

export async function requestUpload(
  contentType: string,
  sizeBytes: number,
): Promise<UploadResponse> {
  if (!ALLOWED_AUDIO_TYPES.has(contentType)) {
    throw new ApiError('Этот формат записи не поддерживается.', 400, 'UNSUPPORTED_AUDIO');
  }
  if (sizeBytes <= 0 || sizeBytes > MAX_AUDIO_BYTES) {
    throw new ApiError('Размер записи должен быть не больше 10 МБ.', 413, 'PAYLOAD_TOO_LARGE');
  }

  return jsonRequest<UploadResponse>('/uploads', {
    method: 'POST',
    body: JSON.stringify({ content_type: contentType, size_bytes: sizeBytes } satisfies UploadRequest),
  });
}

export async function uploadAudio(
  uploadUrl: string,
  audio: Blob,
  contentType: string,
): Promise<void> {
  const response = await fetch(uploadUrl, {
    method: 'PUT',
    headers: { 'Content-Type': contentType },
    body: audio,
  });
  if (!response.ok) {
    throw new ApiError('Не удалось загрузить запись. Попробуйте ещё раз.', response.status, 'UPLOAD_FAILED');
  }
}

export async function submitMessage(
  sessionId: string,
  input: { text: string } | { objectKey: string },
): Promise<MessageResponse> {
  const body = {
    session_id: sessionId,
    idempotency_key: crypto.randomUUID(),
    text: 'text' in input ? input.text : null,
    object_key: 'objectKey' in input ? input.objectKey : null,
  };

  return jsonRequest<MessageResponse>('/messages', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export async function getTurn(turnId: string): Promise<TurnSnapshot> {
  return jsonRequest<TurnSnapshot>(`/turns/${encodeURIComponent(turnId)}`);
}

export function absoluteEventsUrl(eventsUrl: string): string {
  const apiOrigin = new URL(API_BASE_URL, window.location.origin).origin;
  return new URL(eventsUrl, apiOrigin).toString();
}

export interface TurnCallbacks {
  onState: (state: TurnState) => void;
  onAnswer: (answer: AnswerPayload) => void;
  onAudio: (audio: AudioPayload) => void;
  onError: (error: TurnError) => void;
  onTranscript: (text: string) => void;
  onTransportError: () => void;
  onProtocolError: () => void;
  onClose?: () => void;
}

const TERMINAL_STATES = new Set<TurnState>([
  'COMPLETED',
  'NEEDS_CLARIFICATION',
  'FAILED',
  'EXPIRED',
]);

function readEventData<T>(event: Event, validate: (value: unknown) => value is T): T | null {
  try {
    const value: unknown = JSON.parse((event as MessageEvent<string>).data);
    return validate(value) ? value : null;
  } catch (error) {
    if (error instanceof SyntaxError) return null;
    throw error;
  }
}

function isTurnState(value: unknown): value is TurnState {
  return (
    value === 'TRANSCRIBING' ||
    value === 'INTERPRETING' ||
    value === 'APPLYING' ||
    value === 'COMPLETED' ||
    value === 'NEEDS_CLARIFICATION' ||
    value === 'FAILED' ||
    value === 'EXPIRED'
  );
}

function isAnswerPayload(value: unknown): value is AnswerPayload {
  return (
    isRecord(value) &&
    typeof value.text === 'string' &&
    Array.isArray(value.events)
  );
}

function isAudioPayload(value: unknown): value is AudioPayload {
  return (
    isRecord(value) &&
    typeof value.url === 'string' &&
    typeof value.duration_ms === 'number'
  );
}

function isTurnError(value: unknown): value is TurnError {
  return (
    isRecord(value) &&
    typeof value.code === 'string' &&
    typeof value.message === 'string'
  );
}

export function subscribeToTurn(
  turnId: string,
  eventsUrl: string,
  callbacks: TurnCallbacks,
): () => void {
  let disposed = false;
  let pollInFlight = false;
  let pollingTimer: number | undefined;
  let finishTimer: number | undefined;
  let failureTimer: number | undefined;
  let watchdogTimer: number | undefined;
  let source: EventSource | undefined;
  let hasAnswer = false;
  let hasAudio = false;
  let hasError = false;
  let transcriptChecked = false;

  const stop = (): void => {
    if (disposed) return;
    disposed = true;
    source?.close();
    if (pollingTimer !== undefined) window.clearInterval(pollingTimer);
    if (finishTimer !== undefined) window.clearTimeout(finishTimer);
    if (failureTimer !== undefined) window.clearTimeout(failureTimer);
    if (watchdogTimer !== undefined) window.clearTimeout(watchdogTimer);
    callbacks.onClose?.();
  };

  const scheduleCompletion = (state: TurnState): void => {
    if (!TERMINAL_STATES.has(state)) return;

    if (state === 'FAILED' || state === 'EXPIRED') {
      if (failureTimer === undefined) {
        failureTimer = window.setTimeout(() => {
          void pollSnapshot();
          window.setTimeout(stop, 1500);
        }, 1000);
      }
      return;
    }

    if (finishTimer === undefined) {
      finishTimer = window.setTimeout(stop, 30_000);
    }
  };

  const emitAnswer = (answer: AnswerPayload): void => {
    if (hasAnswer) return;
    hasAnswer = true;
    callbacks.onAnswer(answer);
  };

  const emitAudio = (audio: AudioPayload): void => {
    if (hasAudio) return;
    hasAudio = true;
    callbacks.onAudio(audio);
    stop();
  };

  const emitError = (error: TurnError): void => {
    if (hasError) return;
    hasError = true;
    callbacks.onError(error);
  };

  watchdogTimer = window.setTimeout(() => {
    emitError({ code: 'DEADLINE_EXCEEDED', message: '' });
    stop();
  }, 95_000);

  async function pollSnapshot(): Promise<void> {
    if (disposed || pollInFlight) return;
    pollInFlight = true;
    try {
      const snapshot = await getTurn(turnId);
      if (disposed) return;
      callbacks.onState(snapshot.state);
      scheduleCompletion(snapshot.state);
      if (snapshot.text_input) callbacks.onTranscript(snapshot.text_input);
      if (snapshot.answer) {
        emitAnswer({ text: snapshot.answer, events: snapshot.events ?? [] });
      }
      if (snapshot.audio) emitAudio(snapshot.audio);
      if (snapshot.error) emitError(snapshot.error);
    } catch {
      callbacks.onTransportError();
    } finally {
      pollInFlight = false;
    }
  }

  const startPolling = (): void => {
    if (pollingTimer !== undefined || disposed) return;
    void pollSnapshot();
    pollingTimer = window.setInterval(() => void pollSnapshot(), 3000);
  };

  if (typeof EventSource === 'undefined') {
    startPolling();
    return stop;
  }

  source = new EventSource(absoluteEventsUrl(eventsUrl));
  source.onopen = () => {
    if (pollingTimer !== undefined) {
      window.clearInterval(pollingTimer);
      pollingTimer = undefined;
    }
  };
  source.addEventListener('state', (event) => {
    const value = readEventData<{ state: TurnState }>(
      event,
      (candidate): candidate is { state: TurnState } =>
        isRecord(candidate) && isTurnState(candidate.state),
    );
    if (!value) {
      callbacks.onProtocolError();
      return;
    }
    callbacks.onState(value.state);
    scheduleCompletion(value.state);
    if (value.state === 'INTERPRETING' && !transcriptChecked) {
      transcriptChecked = true;
      void pollSnapshot();
    }
  });
  source.addEventListener('answer', (event) => {
    const value = readEventData<AnswerPayload>(event, isAnswerPayload);
    if (!value) {
      callbacks.onProtocolError();
      return;
    }
    emitAnswer(value);
  });
  source.addEventListener('audio', (event) => {
    const value = readEventData<AudioPayload>(event, isAudioPayload);
    if (!value) {
      callbacks.onProtocolError();
      return;
    }
    emitAudio(value);
  });
  source.addEventListener('error', (event) => {
    const value = readEventData<TurnError>(event, isTurnError);
    if (value) {
      emitError(value);
    } else if ('data' in event) {
      callbacks.onProtocolError();
    }
  });
  source.onerror = () => {
    if (!disposed) startPolling();
  };

  return stop;
}

export function userFacingError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'SESSION_BUSY') return 'Подождите, пока завершится предыдущий запрос.';
    if (error.code === 'UNSUPPORTED_AUDIO') return 'Браузер записал звук в неподдерживаемом формате.';
    if (error.code === 'PAYLOAD_TOO_LARGE') return 'Запись слишком большая. Попробуйте записать короче.';
    if (error.code === 'BAD_REQUEST') return 'Проверьте текст запроса и попробуйте ещё раз.';
    if (error.status >= 500) return 'Сервис временно недоступен. Попробуйте повторить запрос.';
    return error.message;
  }
  if (error instanceof TypeError) {
    return 'Не удалось связаться с сервером. Проверьте подключение и попробуйте ещё раз.';
  }
  return 'Не удалось выполнить запрос. Попробуйте ещё раз.';
}

export function turnErrorMessage(error: TurnError): string {
  switch (error.code) {
    case 'EMPTY_TRANSCRIPT':
      return 'Не расслышал. Нажмите микрофон и повторите или напишите запрос.';
    case 'STT_FAILED':
      return 'Не получилось распознать запись. Попробуйте ещё раз или напишите запрос.';
    case 'NLU_FAILED':
      return 'Не получилось разобрать запрос. Попробуйте переформулировать.';
    case 'DEADLINE_EXCEEDED':
      return 'Запрос занял слишком много времени. Попробуйте повторить.';
    default:
      return 'Не получилось обработать запрос. Попробуйте ещё раз.';
  }
}
