// ---------------------------------------------------------------------------------------------------
// Display formatting helpers. Prices are formatted per-symbol because the wire DTOs carry only raw
// `number` prices with NO pip/tick/precision field (see PaperTradeDto in types/api.ts — the backend
// SymbolSpec price geometry is not surfaced). Until a precision field is added to the contract, we
// derive the decimal count from the symbol string: JPY pairs quote to 3, metals (XAU/XAG) to 2,
// indices to 1, and FX majors to 5. This is display-only — the underlying numbers are unchanged.
// ---------------------------------------------------------------------------------------------------

/** Index symbols seen across the supported feeds (OANDA/Finnhub/TraderMade names). */
const INDEX_PATTERN = /US30|NAS|SPX|GER|UK100|DOW|NDX|US500|JPN225|HK50|AUS200/;

/**
 * Decimal places to render a price for `symbol`. JPY pairs → 3, gold/silver → 2, indices → 1,
 * FX majors (the default) → 5. Keyed off the symbol string because the wire carries no precision.
 */
export function priceDecimals(symbol: string): number {
  const upper = symbol.toUpperCase();
  if (upper.includes('JPY')) return 3;
  if (upper.includes('XAU') || upper.includes('XAG')) return 2;
  if (INDEX_PATTERN.test(upper)) return 1;
  return 5;
}

/** Format a price for display at the per-symbol precision (see {@link priceDecimals}). */
export function formatPrice(price: number, symbol: string): string {
  return price.toFixed(priceDecimals(symbol));
}

/**
 * Format an account-currency amount. Money is rendered with a thousands separator and 2 decimals; a
 * non-negative value carries a leading "+" so a colored P&L cell reads as a signed delta. `null` →
 * em-dash (an open trade has no realized money yet).
 */
export function formatMoney(amount: number | null | undefined, options: { signed?: boolean } = {}): string {
  if (amount == null) return '—';
  const abs = Math.abs(amount).toLocaleString('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  const sign = amount < 0 ? '-' : options.signed ? '+' : '';
  return `${sign}$${abs}`;
}

/** Format an R multiple — null (an open trade) renders as an em-dash. */
export function formatR(r: number | null | undefined): string {
  if (r == null) return '—';
  const sign = r > 0 ? '+' : '';
  return `${sign}${r.toFixed(2)}R`;
}

/** Format a 0..1 fraction as a percent (e.g. 0.625 → "62.5%"). */
export function formatPct(fraction: number, decimals = 1): string {
  return `${(fraction * 100).toFixed(decimals)}%`;
}

/** Format an already-0..100 percent value (e.g. a risk-utilization percent). */
export function formatPercentValue(value: number, decimals = 1): string {
  return `${value.toFixed(decimals)}%`;
}

/** A signed percent delta vs a base (e.g. equity vs starting), as a string like "+4.88%". */
export function deltaPercent(current: number, base: number): number {
  if (base === 0) return 0;
  return ((current - base) / base) * 100;
}
