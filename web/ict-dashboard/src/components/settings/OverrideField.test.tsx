// OverrideField — a per-instrument number input that makes inheritance explicit: an empty field shows the
// inherited global default as ghost text ("inherit (global: 10 pips)"), and a filled field shows a "vs
// default" delta chip so the operator sees they changed it.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { OverrideField } from './OverrideField';

describe('OverrideField', () => {
  it('shows the inherited global default as placeholder ghost text when empty', () => {
    render(
      <OverrideField label="Min stop (pips)" value="" onChange={() => {}} globalDefault={10} unit="pips" />,
    );
    const input = screen.getByLabelText('Min stop (pips)');
    expect(input).toHaveAttribute('placeholder', 'inherit (global: 10 pips)');
    // No "vs default" delta chip while inheriting.
    expect(screen.queryByText(/^vs /)).toBeNull();
  });

  it('shows a "vs default" delta chip once a value overrides the inherited default', () => {
    render(
      <OverrideField label="Spread base (pips)" value="0.3" onChange={() => {}} globalDefault={0.7} unit="pips" />,
    );
    expect(screen.getByText(/vs 0\.7 pips/)).toBeInTheDocument();
  });

  it('falls back to a plain "inherit" placeholder when the default is unknown', () => {
    render(<OverrideField label="Commission" value="" onChange={() => {}} globalDefault={null} />);
    expect(screen.getByLabelText('Commission')).toHaveAttribute('placeholder', 'inherit');
  });

  it('reports edits through onChange', () => {
    const onChange = vi.fn();
    render(<OverrideField label="Min stop (pips)" value="" onChange={onChange} globalDefault={10} />);
    fireEvent.change(screen.getByLabelText('Min stop (pips)'), { target: { value: '15' } });
    expect(onChange).toHaveBeenCalledWith('15');
  });
});
