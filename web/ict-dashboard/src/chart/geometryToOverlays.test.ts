import { describe, expect, it } from 'vitest';
import type { GeometryOverlayDto } from '../types/api';
import { geometryToOverlay, geometryToOverlays } from './geometryToOverlays';

function dto(overrides: Partial<GeometryOverlayDto> & { kind: string }): GeometryOverlayDto {
  return { direction: 'Bullish', atUtc: '2026-06-19T06:40:00Z', ...overrides };
}

describe('geometryToOverlay', () => {
  it('maps an FVG box + greys a mitigated one', () => {
    const open = geometryToOverlay(dto({ kind: 'fvg', top: 1.073, bottom: 1.072, state: 'Open' }));
    expect(open).toMatchObject({ kind: 'fvg', top: 1.073, bottom: 1.072, mitigated: false });

    const mitigated = geometryToOverlay(dto({ kind: 'fvg', top: 1.073, bottom: 1.072, state: 'Mitigated' }));
    expect(mitigated).toMatchObject({ kind: 'fvg', mitigated: true });
  });

  it('maps an order block with its mean threshold + flags a breaker (inverted)', () => {
    const ob = geometryToOverlay(dto({ kind: 'orderBlock', top: 1.071, bottom: 1.069, mid: 1.07, state: 'Open' }));
    expect(ob).toMatchObject({ kind: 'orderBlock', top: 1.071, bottom: 1.069, meanThreshold: 1.07, isBreaker: false });

    const breaker = geometryToOverlay(dto({ kind: 'orderBlock', top: 1.071, bottom: 1.069, mid: 1.07, state: 'Inverted' }));
    expect(breaker).toMatchObject({ kind: 'orderBlock', isBreaker: true });
  });

  it('maps a liquidity pool with a side + swept flag + strength label', () => {
    const pool = geometryToOverlay(
      dto({ kind: 'liquidity', direction: 'Bearish', price: 1.079, side: 'BuySide', swept: true, strength: 3 }),
    );
    expect(pool).toMatchObject({ kind: 'liquidity', side: 'buy', price: 1.079, swept: true, label: 'Buy-side ×3' });
  });

  it('maps sweep + MSS point markers to their level', () => {
    expect(geometryToOverlay(dto({ kind: 'sweep', price: 1.069 }))).toMatchObject({ kind: 'sweep', price: 1.069 });
    expect(geometryToOverlay(dto({ kind: 'mss', price: 1.081 }))).toMatchObject({
      kind: 'mss',
      brokenSwingPrice: 1.081,
    });
  });

  it('maps the OTE band to its 62/79/70.5 lines', () => {
    const ote = geometryToOverlay(dto({ kind: 'ote', top: 1.0838, bottom: 1.0821, mid: 1.08295 }));
    expect(ote).toMatchObject({ kind: 'ote', band62: 1.0838, band79: 1.0821, sweetSpot705: 1.08295 });
  });

  it('drops a box missing its bounds and an unknown kind', () => {
    expect(geometryToOverlay(dto({ kind: 'fvg', top: 1.073 }))).toBeNull(); // bottom missing
    expect(geometryToOverlay(dto({ kind: 'orderBlock', top: 1.071, bottom: 1.069 }))).toBeNull(); // mid missing
    expect(geometryToOverlay(dto({ kind: 'swing', price: 1.07 }))).toBeNull(); // unrendered kind
  });

  it('geometryToOverlays filters out the unrenderable entries', () => {
    const overlays = geometryToOverlays([
      dto({ kind: 'sweep', price: 1.069 }),
      dto({ kind: 'fvg', top: 1.073 }), // dropped (no bottom)
      dto({ kind: 'liquidity', price: 1.079, side: 'SellSide' }),
    ]);
    expect(overlays.map((o) => o.kind)).toEqual(['sweep', 'liquidity']);
  });
});
