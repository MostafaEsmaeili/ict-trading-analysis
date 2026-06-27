// Per-symbol price precision (finding [21]): the wire carries no precision field, so the helper
// derives decimals from the symbol string — JPY → 3, metals → 2, indices → 1, FX majors → 5.
import { describe, expect, it } from 'vitest';
import { formatPrice, priceDecimals } from './format';

describe('priceDecimals', () => {
  it('uses 5 decimals for FX majors', () => {
    expect(priceDecimals('EURUSD')).toBe(5);
    expect(priceDecimals('GBPUSD')).toBe(5);
  });

  it('uses 3 decimals for JPY pairs', () => {
    expect(priceDecimals('USDJPY')).toBe(3);
    expect(priceDecimals('EURJPY')).toBe(3);
    expect(priceDecimals('gbpjpy')).toBe(3);
  });

  it('uses 2 decimals for metals', () => {
    expect(priceDecimals('XAUUSD')).toBe(2);
    expect(priceDecimals('XAGUSD')).toBe(2);
  });

  it('uses 1 decimal for indices', () => {
    expect(priceDecimals('US30')).toBe(1);
    expect(priceDecimals('NAS100')).toBe(1);
    expect(priceDecimals('SPX500')).toBe(1);
  });
});

describe('formatPrice', () => {
  it('formats per-symbol precision', () => {
    expect(formatPrice(1.0724, 'EURUSD')).toBe('1.07240');
    expect(formatPrice(156.123, 'USDJPY')).toBe('156.123');
    expect(formatPrice(2345.6, 'XAUUSD')).toBe('2345.60');
    expect(formatPrice(39123.4, 'US30')).toBe('39123.4');
  });
});
