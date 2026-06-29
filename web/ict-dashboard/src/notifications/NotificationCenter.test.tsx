// NotificationCenter — the NavBar bell + unread badge + history popover. Verifies the unread badge, the
// per-row dismiss, "Clear all", and that the popover lists the durable history (newest-first).
import { afterEach, describe, expect, it } from 'vitest';
import { act, fireEvent, screen, within } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import { NotificationCenter } from './NotificationCenter';
import { __resetNotificationsForTest, notify } from './notificationStore';

afterEach(() => {
  __resetNotificationsForTest();
});

function openPopover(): void {
  fireEvent.click(screen.getByRole('button', { name: /notifications/i }));
}

describe('NotificationCenter', () => {
  it('shows the unread count on the bell badge', () => {
    renderWithProviders(<NotificationCenter />);
    act(() => {
      notify({ kind: 'info', title: 'a' });
      notify({ kind: 'tradeOpened', title: 'b' });
    });
    expect(screen.getByRole('button', { name: /notifications \(2 unread\)/i })).toBeInTheDocument();
  });

  it('lists the history newest-first in the popover', () => {
    renderWithProviders(<NotificationCenter />);
    act(() => {
      notify({ kind: 'info', title: 'older' });
      notify({ kind: 'info', title: 'newer' });
    });
    openPopover();
    const dialog = screen.getByRole('dialog', { name: /notification history/i });
    const titles = within(dialog)
      .getAllByText(/older|newer/)
      .map((el) => el.textContent);
    expect(titles).toEqual(['newer', 'older']);
  });

  it('dismisses a single row from the popover (per-row ✕)', () => {
    renderWithProviders(<NotificationCenter />);
    act(() => {
      notify({ kind: 'tradeClosed', title: 'keep me' });
      notify({ kind: 'error', title: 'remove me' });
    });
    openPopover();
    const dialog = screen.getByRole('dialog', { name: /notification history/i });
    // Two rows, each with a dismiss button. Dismiss the first (newest = "remove me").
    const closes = within(dialog).getAllByRole('button', { name: /dismiss notification/i });
    expect(closes).toHaveLength(2);
    fireEvent.click(closes[0]);
    // The dismissed row drops out of the unread count (2 → 1) but both rows stay in history.
    expect(screen.getByRole('button', { name: /notifications \(1 unread\)/i })).toBeInTheDocument();
    expect(within(dialog).getByText('keep me')).toBeInTheDocument();
    expect(within(dialog).getByText('remove me')).toBeInTheDocument();
  });

  it('Clear all empties the history', () => {
    renderWithProviders(<NotificationCenter />);
    act(() => {
      notify({ kind: 'info', title: 'gone1' });
      notify({ kind: 'info', title: 'gone2' });
    });
    openPopover();
    fireEvent.click(screen.getByRole('button', { name: /clear all/i }));
    expect(screen.getByText('No notifications.')).toBeInTheDocument();
    expect(screen.queryByText('gone1')).toBeNull();
  });

  it('Mark all read clears the unread badge but keeps history', () => {
    renderWithProviders(<NotificationCenter />);
    act(() => {
      notify({ kind: 'info', title: 'stays' });
    });
    openPopover();
    fireEvent.click(screen.getByRole('button', { name: /mark all read/i }));
    const dialog = screen.getByRole('dialog', { name: /notification history/i });
    expect(within(dialog).getByText('stays')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^notifications$/i })).toBeInTheDocument();
  });
});
