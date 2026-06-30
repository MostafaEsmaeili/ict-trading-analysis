// PresetPicker — three one-click presets (Strict / Balanced / Discovery) that FILL the override form. It
// applies the existing InstrumentSettingsDto fields only (k-of-n), shows a "what this changes vs default"
// diff, and warns on the looser presets. Applying does not auto-save (the form's Save owns the PUT).
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { PresetPicker } from './PresetPicker';

describe('PresetPicker', () => {
  it('applies the Balanced preset (k=6 of 8) and shows the diff', () => {
    const onApply = vi.fn();
    render(<PresetPicker availableCount={8} onApply={onApply} />);

    fireEvent.click(screen.getByRole('button', { name: /balanced/i }));

    expect(onApply).toHaveBeenCalledWith({ minRequiredConditions: 6, requiredConditions: null });
    expect(screen.getByRole('status')).toHaveTextContent('requires 6 of 8 conditions');
  });

  it('applies Strict as the all-required default with no warning', () => {
    const onApply = vi.fn();
    render(<PresetPicker availableCount={8} onApply={onApply} />);

    fireEvent.click(screen.getByRole('button', { name: /strict/i }));

    expect(onApply).toHaveBeenCalledWith({ minRequiredConditions: null, requiredConditions: null });
    expect(screen.getByRole('status')).toHaveTextContent('requires all 8 conditions');
    expect(screen.queryByRole('note')).toBeNull();
  });

  it('warns on the Discovery preset (lower average grade)', () => {
    render(<PresetPicker availableCount={8} onApply={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: /discovery/i }));

    expect(screen.getByRole('note')).toHaveTextContent(/lower average grade/i);
  });
});
