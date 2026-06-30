// InfoTip — the accessible "?" help popover for the Settings jargon. It must be a real focusable button
// (keyboard + screen-reader reachable, NOT a bare title=) wired with aria-describedby to its tooltip text.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { InfoTip } from './InfoTip';

describe('InfoTip', () => {
  it('renders a focusable button described by the tooltip (aria-describedby → the glossary body)', () => {
    render(<InfoTip term="kOfN" />);

    const btn = screen.getByRole('button', { name: /help: required conditions/i });
    // Keyboard-reachable: a real <button> (not a div / bare title attribute).
    expect(btn.tagName).toBe('BUTTON');

    // aria-describedby points at the tooltip element, which carries the plain-language body text.
    const describedById = btn.getAttribute('aria-describedby');
    expect(describedById).toBeTruthy();
    const tip = document.getElementById(describedById as string);
    expect(tip).not.toBeNull();
    expect(tip).toHaveAttribute('role', 'tooltip');
    expect(tip).toHaveTextContent(/relaxes that so a signal can fire/i);
  });

  it('opens on keyboard focus and closes on Escape (aria-expanded reflects state)', () => {
    render(<InfoTip term="confluence" />);
    const btn = screen.getByRole('button', { name: /help: confluence/i });

    expect(btn).toHaveAttribute('aria-expanded', 'false');
    fireEvent.focus(btn);
    expect(btn).toHaveAttribute('aria-expanded', 'true');
    fireEvent.keyDown(btn, { key: 'Escape' });
    expect(btn).toHaveAttribute('aria-expanded', 'false');
  });

  it('supports explicit title/children (used for non-glossary help)', () => {
    render(
      <InfoTip title="Presets" label="Help: presets">
        One click fills the form.
      </InfoTip>,
    );
    const btn = screen.getByRole('button', { name: /help: presets/i });
    const tip = document.getElementById(btn.getAttribute('aria-describedby') as string);
    expect(tip).toHaveTextContent('Presets');
    expect(tip).toHaveTextContent('One click fills the form.');
  });
});
