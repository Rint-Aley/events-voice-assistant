import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { submitMessage, subscribeToTurn } from './api';
import App from './App';

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>();
  return {
    ...actual,
    submitMessage: vi.fn(),
    subscribeToTurn: vi.fn(() => () => {}),
  };
});

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  cleanup();
  localStorage.clear();
});

describe('assistant chat', () => {
  it('shows the entry point and example requests', () => {
    render(<App />);

    expect(screen.getByRole('heading', { name: 'Скажите, что записать или найти' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Что у меня сегодня?' })).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Скажите или напишите запрос' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Начать запись' })).toBeEnabled();
  });

  it('lets the user draft a message during processing but prevents a second submission', async () => {
    vi.mocked(submitMessage).mockResolvedValue({
      turn_id: 'turn-1',
      state: 'INTERPRETING',
      events_url: '/api/v1/turns/turn-1/events',
    });

    render(<App />);
    const composer = screen.getByRole('textbox', { name: 'Скажите или напишите запрос' });
    fireEvent.change(composer, { target: { value: 'Что у меня сегодня?' } });
    fireEvent.click(screen.getByRole('button', { name: 'Отправить сообщение' }));

    await waitFor(() => expect(submitMessage).toHaveBeenCalledTimes(1));
    fireEvent.change(composer, { target: { value: 'Добавь встречу завтра' } });

    expect(composer).toBeEnabled();
    expect(composer).toHaveValue('Добавь встречу завтра');
    expect(screen.getByRole('button', { name: 'Идёт обработка запроса' })).toBeDisabled();
    expect(subscribeToTurn).toHaveBeenCalledTimes(1);
  });
});
