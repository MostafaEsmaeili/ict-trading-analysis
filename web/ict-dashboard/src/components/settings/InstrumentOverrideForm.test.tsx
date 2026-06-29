// InstrumentOverrideForm — the editable per-instrument override (k-of-n / required subset / per-pair
// costs / HTF bias). A preset FILLS the form (the operator reviews then Saves); a required subset must
// include DisplacementMss; saving PUTs the existing InstrumentSettingsDto. Read-only/advisory — no order.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import type { UseMutationResult } from '@tanstack/react-query';
import { InstrumentOverrideForm } from './InstrumentOverrideForm';
import type { GlobalConceptSettingsDto, InstrumentSettingsDto } from '../../types/api';

type UpdateMutation = UseMutationResult<
  void,
  Error,
  { symbol: string; body: InstrumentSettingsDto | null }
>;

const AVAILABLE = [
  'BiasAligned',
  'KillzoneEntry',
  'LiquiditySweep',
  'DisplacementMss',
  'FvgPresent',
  'PremiumDiscountHalf',
  'DrawTargetRrMet',
  'CalendarClear',
];

const GLOBAL: Partial<GlobalConceptSettingsDto> = {
  minRequiredConditions: null,
  minStopDistancePips: 10,
  spreadBasePips: 0.7,
  commissionPerLotRoundTripUsd: 6,
};

function mockUpdate(overrides: Partial<UpdateMutation> = {}): UpdateMutation {
  return {
    mutate: vi.fn(),
    isPending: false,
    isError: false,
    error: null,
    ...overrides,
  } as unknown as UpdateMutation;
}

function renderForm(update = mockUpdate()): UpdateMutation {
  render(
    <InstrumentOverrideForm
      symbol="EURUSD"
      initial={undefined}
      available={AVAILABLE}
      global={GLOBAL as GlobalConceptSettingsDto}
      hasOverride={false}
      update={update}
      onMutated={() => {}}
    />,
  );
  return update;
}

describe('InstrumentOverrideForm', () => {
  it('applies a preset into the form (Balanced sets k-of-n to 6)', () => {
    renderForm();
    const kField = screen.getByLabelText('Min required (k of n)') as HTMLInputElement;
    expect(kField.value).toBe('');

    fireEvent.click(screen.getByRole('button', { name: /balanced/i }));

    expect((screen.getByLabelText('Min required (k of n)') as HTMLInputElement).value).toBe('6');
  });

  it('surfaces the inherited global default beside the cost fields', () => {
    renderForm();
    expect(screen.getByLabelText('Min stop (pips)')).toHaveAttribute(
      'placeholder',
      'inherit (global: 10 pips)',
    );
  });

  it('saves a valid override via the mutation', () => {
    const update = renderForm();
    fireEvent.change(screen.getByLabelText('Min required (k of n)'), { target: { value: '6' } });
    fireEvent.submit(screen.getByRole('form', { name: /instrument override form/i }));

    expect(update.mutate).toHaveBeenCalledWith(
      expect.objectContaining({ symbol: 'EURUSD', body: expect.objectContaining({ minRequiredConditions: 6 }) }),
      expect.any(Object),
    );
  });

  it('blocks a required subset missing the direction lock', () => {
    const update = renderForm();
    fireEvent.click(screen.getByRole('button', { name: 'BiasAligned' }));
    fireEvent.submit(screen.getByRole('form', { name: /instrument override form/i }));

    expect(screen.getByRole('alert')).toHaveTextContent(/must include DisplacementMss/i);
    expect(update.mutate).not.toHaveBeenCalled();
  });
});
