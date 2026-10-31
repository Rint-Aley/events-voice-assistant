import { useEffect, useMemo, useRef, useState } from 'react';
import {
  ApiError,
  requestUpload,
  submitMessage,
  subscribeToTurn,
  turnErrorMessage,
  uploadAudio,
  userFacingError,
  type AnswerPayload,
  type EventItem,
  type MessageResponse,
  type TurnState,
} from './api';
import styles from './App.module.css';
import { findRecordingFormat, formatDuration, MAX_AUDIO_BYTES, MAX_RECORDING_MS } from './voice';

interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  text: string;
  createdAt: number;
  pending?: boolean;
  isError?: boolean;
  events?: EventItem[];
  turnId?: string;
  eventsUrl?: string;
  turnState?: TurnState;
  audioUrl?: string;
  userMessageId?: string;
}

interface SavedState {
  messages: ChatMessage[];
  sessionId: string;
  warning: string | null;
}

const MESSAGE_STORAGE_KEY = 'gorky-assistant-messages-v1';
const SESSION_STORAGE_KEY = 'gorky-assistant-session-v1';
const MAX_MESSAGES = 100;
const SUGGESTIONS = [
  'Что у меня сегодня?',
  'Напомни завтра в 10 позвонить Ивану',
  'Найди задачу про договор',
];
const TIME_OPTIONS = ['10:00', '12:00', '15:00', '18:00'];

function newId(): string {
  return crypto.randomUUID();
}

function restoreState(): SavedState {
  try {
    const savedMessages = localStorage.getItem(MESSAGE_STORAGE_KEY);
    const messages: ChatMessage[] = savedMessages
      ? parseMessages(savedMessages)
      : [];
    let sessionId = localStorage.getItem(SESSION_STORAGE_KEY);
    if (!sessionId) {
      sessionId = newId();
      localStorage.setItem(SESSION_STORAGE_KEY, sessionId);
    }
    return { messages, sessionId, warning: null };
  } catch (error) {
    const warning =
      error instanceof SyntaxError
        ? 'Историю не удалось прочитать. Начинаем новый диалог.'
        : 'Историю не удалось сохранить в этом браузере.';
    return { messages: [], sessionId: newId(), warning };
  }
}

function parseMessages(value: string): ChatMessage[] {
  const parsed: unknown = JSON.parse(value);
  if (!Array.isArray(parsed)) throw new SyntaxError('История диалога имеет неверный формат.');
  return parsed.filter(isChatMessage).slice(-MAX_MESSAGES);
}

function isChatMessage(value: unknown): value is ChatMessage {
  if (typeof value !== 'object' || value === null) return false;
  const message = value as Record<string, unknown>;
  return (
    typeof message.id === 'string' &&
    (message.role === 'user' || message.role === 'assistant') &&
    typeof message.text === 'string' &&
    typeof message.createdAt === 'number'
  );
}

function getStatusText(state: TurnState | undefined): string {
  switch (state) {
    case 'TRANSCRIBING':
      return 'Распознаю речь…';
    case 'INTERPRETING':
    case 'APPLYING':
      return 'Обрабатываю…';
    default:
      return 'Думаю…';
  }
}

function formatEventWhen(item: EventItem): string {
  if (!item.starts_on && !item.starts_at) return '';
  const date = item.starts_on
    ? new Date(`${item.starts_on}T00:00:00`).toLocaleDateString('ru-RU', {
        day: 'numeric',
        month: 'long',
      })
    : '';
  const time = item.starts_at ? item.starts_at.slice(0, 5) : '';
  return [date, time].filter(Boolean).join(', ');
}

export default function App() {
  const initial = useMemo(restoreState, []);
  const [messages, setMessages] = useState<ChatMessage[]>(initial.messages);
  const [sessionId] = useState(initial.sessionId);
  const [draft, setDraft] = useState('');
  const [voiceEnabled, setVoiceEnabled] = useState(true);
  const [activity, setActivity] = useState<
    'idle' | 'requesting' | 'recording' | 'uploading' | 'processing'
  >(
    initial.messages.some((message) => message.pending) ? 'processing' : 'idle',
  );
  const [recordingMs, setRecordingMs] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const [storageWarning, setStorageWarning] = useState(initial.warning);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<BlobPart[]>([]);
  const recordingFailedRef = useRef(false);
  const recordingStartedAtRef = useRef(0);
  const messagesEndRef = useRef<HTMLDivElement | null>(null);
  const subscriptionsRef = useRef(new Set<() => void>());
  const resumedTurnRef = useRef<ChatMessage | null>(
    initial.messages.find(
      (message) => message.role === 'assistant' && message.pending && message.turnId && message.eventsUrl,
    ) ?? null,
  );

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [messages, activity]);

  useEffect(() => {
    try {
      const persistentMessages = messages.slice(-MAX_MESSAGES).map(({ audioUrl: _audioUrl, ...message }) => message);
      localStorage.setItem(MESSAGE_STORAGE_KEY, JSON.stringify(persistentMessages));
      setStorageWarning(null);
    } catch {
      setStorageWarning('Историю не удалось сохранить. Она может исчезнуть после закрытия страницы.');
    }
  }, [messages]);

  useEffect(() => {
    const pendingMessage = resumedTurnRef.current;
    if (!pendingMessage?.turnId || !pendingMessage.eventsUrl) return;
    return attachToTurn(pendingMessage, pendingMessage.turnId, pendingMessage.eventsUrl);
    // Reconnect only the pending turn restored on initial load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(
    () => () => {
      subscriptionsRef.current.forEach((stop) => stop());
      streamRef.current?.getTracks().forEach((track) => track.stop());
      streamRef.current = null;
    },
    [],
  );

  useEffect(() => {
    if (activity !== 'recording') return;
    const timer = window.setInterval(() => {
      const elapsed = Date.now() - recordingStartedAtRef.current;
      setRecordingMs(elapsed);
      if (elapsed >= MAX_RECORDING_MS && recorderRef.current?.state === 'recording') {
        recorderRef.current.stop();
      }
    }, 250);
    return () => window.clearInterval(timer);
  }, [activity]);

  const hasMessages = messages.length > 0;
  const canSend = draft.trim().length > 0 && draft.length <= 1000 && activity === 'idle';

  function appendMessage(message: ChatMessage): void {
    setMessages((current) => [...current, message].slice(-MAX_MESSAGES));
  }

  function updateMessage(id: string, update: (message: ChatMessage) => ChatMessage): void {
    setMessages((current) => current.map((message) => (message.id === id ? update(message) : message)));
  }

  function appendTurnMessages(text: string, isVoice = false): { assistantId: string; userId: string } {
    const userId = newId();
    const assistantId = newId();
    appendMessage({
      id: userId,
      role: 'user',
      text: isVoice ? 'Голосовой запрос' : text,
      createdAt: Date.now(),
    });
    appendMessage({
      id: assistantId,
      role: 'assistant',
      text: isVoice ? 'Загружаю запись…' : 'Думаю…',
      createdAt: Date.now(),
      pending: true,
      userMessageId: userId,
    });
    return { assistantId, userId };
  }

  function attachToTurn(
    assistantMessage: ChatMessage,
    turnId: string,
    eventsUrl: string,
  ): () => void {
    let dispose = () => {};
    const stop = subscribeToTurn(turnId, eventsUrl, {
      onState: (state) => {
        updateMessage(assistantMessage.id, (message) => ({
          ...message,
          turnState: state,
          text: message.pending ? getStatusText(state) : message.text,
        }));
        if (state === 'COMPLETED' || state === 'NEEDS_CLARIFICATION' || state === 'FAILED' || state === 'EXPIRED') {
          setActivity('idle');
        } else {
          setActivity('processing');
        }
      },
      onAnswer: (answer) => applyAnswer(assistantMessage.id, answer),
      onAudio: (audio) => {
        updateMessage(assistantMessage.id, (message) => ({ ...message, audioUrl: audio.url }));
      },
      onError: (error) => {
        updateMessage(assistantMessage.id, (message) => ({
          ...message,
          text: turnErrorMessage(error),
          pending: false,
          isError: true,
        }));
        setActivity('idle');
      },
      onTranscript: (text) => {
        if (assistantMessage.userMessageId) {
          updateMessage(assistantMessage.userMessageId, (message) => ({ ...message, text }));
        }
      },
      onTransportError: () => {
        setNotice('Связь прервалась. Пытаюсь восстановить соединение…');
      },
      onProtocolError: () => {
        setNotice('Сервер прислал ответ в неожиданном формате. Проверяю состояние запроса…');
      },
      onClose: () => subscriptionsRef.current.delete(dispose),
    });
    dispose = () => {
      stop();
      subscriptionsRef.current.delete(dispose);
    };
    subscriptionsRef.current.add(dispose);

    updateMessage(assistantMessage.id, (message) => ({
      ...message,
      turnId,
      eventsUrl,
    }));
    return dispose;
  }

  function applyAnswer(messageId: string, answer: AnswerPayload): void {
    updateMessage(messageId, (message) => ({
      ...message,
      text: answer.text,
      events: answer.events,
      pending: false,
    }));
    setNotice(null);
  }

  function followTurn(
    assistantId: string,
    response: MessageResponse,
    userId: string,
  ): void {
    const assistantMessage: ChatMessage = {
      id: assistantId,
      role: 'assistant',
      text: getStatusText(response.state),
      createdAt: Date.now(),
      pending: true,
      userMessageId: userId,
    };
    updateMessage(assistantId, (message) => ({
      ...message,
      ...assistantMessage,
      turnId: response.turn_id,
      eventsUrl: response.events_url,
      turnState: response.state,
    }));
    setActivity('processing');
    attachToTurn(assistantMessage, response.turn_id, response.events_url);
  }

  async function sendText(value: string): Promise<void> {
    const text = value.trim();
    if (!text || activity !== 'idle') return;
    if (text.length > 1000) {
      setNotice('Запрос должен быть не длиннее 1000 символов.');
      return;
    }
    const { assistantId, userId } = appendTurnMessages(text);
    setDraft('');
    setNotice(null);
    setActivity('processing');
    try {
      const response = await submitMessage(sessionId, { text });
      followTurn(assistantId, response, userId);
    } catch (error) {
      updateMessage(assistantId, (message) => ({
        ...message,
        text: userFacingError(error),
        pending: false,
        isError: true,
      }));
      setActivity('idle');
    }
  }

  async function sendVoice(audio: Blob, contentType: string): Promise<void> {
    const { assistantId, userId } = appendTurnMessages('', true);
    setActivity('uploading');
    setNotice(null);
    try {
      if (audio.size === 0 || audio.size > MAX_AUDIO_BYTES) {
        throw new ApiError('Размер записи должен быть не больше 10 МБ.', 413, 'PAYLOAD_TOO_LARGE');
      }
      const upload = await requestUpload(contentType, audio.size);
      await uploadAudio(upload.upload_url, audio, contentType);
      updateMessage(assistantId, (message) => ({ ...message, text: 'Распознаю речь…' }));
      const response = await submitMessage(sessionId, { objectKey: upload.object_key });
      followTurn(assistantId, response, userId);
    } catch (error) {
      updateMessage(assistantId, (message) => ({
        ...message,
        text: userFacingError(error),
        pending: false,
        isError: true,
      }));
      setActivity('idle');
    }
  }

  async function startRecording(): Promise<void> {
    setNotice(null);
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
      setNotice('Этот браузер не поддерживает запись. Напишите запрос в поле ниже.');
      return;
    }
    const format = findRecordingFormat(MediaRecorder.isTypeSupported.bind(MediaRecorder));
    if (!format) {
      setNotice('Браузер не поддерживает аудиоформат для записи. Напишите запрос.');
      return;
    }

    try {
      setActivity('requesting');
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;
      const recorder = new MediaRecorder(stream, { mimeType: format.mimeType });
      chunksRef.current = [];
      recordingFailedRef.current = false;
      recorderRef.current = recorder;
      recordingStartedAtRef.current = Date.now();
      setRecordingMs(0);
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) chunksRef.current.push(event.data);
      };
      recorder.onerror = () => {
        recordingFailedRef.current = true;
        stopTracks();
        setActivity('idle');
        setNotice('Не удалось записать звук. Проверьте разрешение на микрофон и попробуйте снова.');
      };
      recorder.onstop = () => {
        const audio = new Blob(chunksRef.current, { type: format.mimeType });
        chunksRef.current = [];
        stopTracks();
        setActivity('idle');
        if (recordingFailedRef.current) return;
        if (audio.size === 0) {
          setNotice('Запись пустая. Попробуйте ещё раз или напишите запрос.');
          return;
        }
        void sendVoice(audio, format.contentType);
      };
      recorder.start(250);
      setActivity('recording');
    } catch (error) {
      stopTracks();
      setActivity('idle');
      const message =
        error instanceof DOMException && error.name === 'NotAllowedError'
          ? 'Разрешите доступ к микрофону в настройках браузера или напишите запрос.'
          : 'Не удалось включить микрофон. Проверьте подключение и попробуйте снова.';
      setNotice(message);
    }
  }

  function stopTracks(): void {
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    recorderRef.current = null;
  }

  function stopRecording(): void {
    if (recorderRef.current?.state === 'recording') recorderRef.current.stop();
  }

  function handleSubmit(event: React.FormEvent<HTMLFormElement>): void {
    event.preventDefault();
    if (!canSend) return;
    void sendText(draft);
  }

  function handleComposerKeyDown(event: React.KeyboardEvent<HTMLTextAreaElement>): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      if (canSend) void sendText(draft);
    }
  }

  return (
    <main className={styles.appShell}>
      <section className={styles.chat} aria-label="Голосовой помощник">
        <header className={styles.header}>
          <div className={styles.brand} aria-label="Помощник">
            Помощник
          </div>
          <button
            className={styles.voiceStatus}
            type="button"
            aria-pressed={voiceEnabled}
            onClick={() => setVoiceEnabled((enabled) => !enabled)}
          >
            <span className={styles.voiceDot} />
            Голос: {voiceEnabled ? 'вкл' : 'выкл'}
          </button>
        </header>

        <div className={styles.conversation} aria-live="polite" aria-relevant="additions text">
          {!hasMessages && (
            <div className={styles.welcome}>
              <h1>Скажите, что записать или найти</h1>
              <p>Нажмите на микрофон или напишите. Например:</p>
              <div className={styles.suggestions}>
                {SUGGESTIONS.map((suggestion) => (
                  <button
                    className={styles.suggestion}
                    type="button"
                    key={suggestion}
                    disabled={activity !== 'idle'}
                    onClick={() => void sendText(suggestion)}
                  >
                    {suggestion}
                  </button>
                ))}
              </div>
            </div>
          )}

          {messages.map((message) => (
            <MessageView
              key={message.id}
              message={message}
              previousUserText={
                message.role === 'assistant' && message.turnState === 'NEEDS_CLARIFICATION'
                  ? messages.find((item) => item.id === message.userMessageId)?.text
                  : undefined
              }
              onQuickReply={(text) => void sendText(text)}
            />
          ))}

          {activity === 'recording' && (
            <div className={styles.recordingTranscript} role="status">
              <span className={styles.recordingIndicator} />
              <div>
                <strong>Идёт запись</strong>
                <span>Нажмите на кнопку записи, чтобы закончить · {formatDuration(recordingMs)}</span>
              </div>
            </div>
          )}
          <div ref={messagesEndRef} />
        </div>

        {(notice || storageWarning) && (
          <div className={styles.notice} role="status">
            <span>{notice ?? storageWarning}</span>
            {notice && (
              <button type="button" onClick={() => setNotice(null)} aria-label="Закрыть сообщение">
                ×
              </button>
            )}
          </div>
        )}

        <form className={styles.composer} onSubmit={handleSubmit}>
          <textarea
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={handleComposerKeyDown}
            placeholder={
              activity === 'requesting'
                ? 'Подключаю микрофон…'
                : activity === 'recording'
                ? `Идёт запись · ${formatDuration(recordingMs)}`
                : activity === 'uploading'
                  ? 'Загружаю запись…'
                  : 'Скажите или напишите'
            }
            aria-label="Скажите или напишите запрос"
            maxLength={1000}
            rows={1}
            disabled={
              activity === 'requesting' || activity === 'recording' || activity === 'uploading'
            }
          />
          {draft.length > 800 && (
            <span className={styles.characterCount} aria-live="polite">
              {draft.length}/1000
            </span>
          )}
          {activity === 'recording' ? (
            <button
              className={`${styles.voiceButton} ${styles.recordingButton}`}
              type="button"
              onClick={stopRecording}
              aria-label="Остановить запись"
              title="Остановить запись"
            >
              <StopIcon />
            </button>
          ) : (
            <>
              <button
                className={`${styles.voiceButton} ${canSend ? styles.sendButton : ''}`}
                type={canSend ? 'submit' : 'button'}
                onClick={!canSend && activity === 'idle' ? () => void startRecording() : undefined}
                disabled={
                  activity === 'uploading' ||
                  activity === 'processing' ||
                  activity === 'requesting' ||
                  (activity === 'idle' && !canSend && !voiceEnabled)
                }
                aria-label={
                  canSend
                    ? 'Отправить сообщение'
                    : activity === 'processing' || activity === 'requesting'
                      ? 'Идёт обработка запроса'
                      : voiceEnabled
                        ? 'Начать запись'
                        : 'Голос отключён'
                }
                title={canSend ? 'Отправить сообщение' : 'Начать запись'}
              >
                {canSend ? (
                  <SendIcon />
                ) : activity === 'uploading' ||
                  activity === 'processing' ||
                  activity === 'requesting' ? (
                  <SpinnerIcon />
                ) : (
                  <MicrophoneIcon />
                )}
              </button>
              {activity === 'processing' && (
                <span className={styles.sendingHint} role="status">
                  Обрабатываю…
                </span>
              )}
            </>
          )}
        </form>
        <footer className={styles.footerHint}>Enter — отправить · Shift + Enter — новая строка</footer>
      </section>
    </main>
  );
}

function MessageView({
  message,
  previousUserText,
  onQuickReply,
}: {
  message: ChatMessage;
  previousUserText?: string;
  onQuickReply: (text: string) => void;
}) {
  const isUser = message.role === 'user';
  const asksForTime =
    message.turnState === 'NEEDS_CLARIFICATION' && /время|час|когда/i.test(message.text);

  return (
    <article className={`${styles.messageRow} ${isUser ? styles.userRow : styles.assistantRow}`}>
      <div
        className={`${styles.bubble} ${isUser ? styles.userBubble : styles.assistantBubble} ${
          message.isError ? styles.errorBubble : ''
        }`}
      >
        {message.pending ? (
          <span className={styles.pendingText}>
            <SpinnerIcon />
            {message.text}
          </span>
        ) : (
          <p>{message.text}</p>
        )}
        {message.role === 'assistant' && message.events && message.events.length > 0 && (
          <div className={styles.eventList} aria-label="Найденные или созданные записи">
            {message.events.map((item) => (
              <div className={styles.eventCard} key={item.id}>
                <span className={styles.eventKind}>{eventKindLabel(item.kind)}</span>
                <strong>{item.description || eventKindLabel(item.kind)}</strong>
                {formatEventWhen(item) && <span>{formatEventWhen(item)}</span>}
                {item.duration_minutes && <span>Длительность: {item.duration_minutes} мин.</span>}
              </div>
            ))}
          </div>
        )}
        {message.turnState === 'NEEDS_CLARIFICATION' && previousUserText && (
          <div className={styles.draftCard}>
            <span>Черновик</span>
            <strong>{previousUserText}</strong>
          </div>
        )}
        {asksForTime && (
          <div className={styles.quickReplies} aria-label="Варианты времени">
            {TIME_OPTIONS.map((time) => (
              <button key={time} type="button" onClick={() => onQuickReply(time)}>
                {time}
              </button>
            ))}
          </div>
        )}
        {message.audioUrl && (
          <audio className={styles.audioPlayer} controls preload="none" src={message.audioUrl}>
            Браузер не поддерживает воспроизведение аудио.
          </audio>
        )}
      </div>
    </article>
  );
}

function eventKindLabel(kind: EventItem['kind']): string {
  switch (kind) {
    case 'task':
      return 'Задача';
    case 'reminder':
      return 'Напоминание';
    case 'meeting':
      return 'Встреча';
  }
}

function MicrophoneIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M12 14a3 3 0 0 0 3-3V6a3 3 0 0 0-6 0v5a3 3 0 0 0 3 3Z" />
      <path d="M19 11a7 7 0 0 1-14 0M12 18v4m-4 0h8" />
    </svg>
  );
}

function StopIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <rect x="6" y="6" width="12" height="12" rx="2" />
    </svg>
  );
}

function SendIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M5 12h14m-6-6 6 6-6 6" />
    </svg>
  );
}

function SpinnerIcon() {
  return <span className={styles.spinner} aria-hidden="true" />;
}
