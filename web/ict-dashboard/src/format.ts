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
