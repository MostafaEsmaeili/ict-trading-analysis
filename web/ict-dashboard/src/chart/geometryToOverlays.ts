// ---------------------------------------------------------------------------------------------------
// Live "engine view" geometry → chart overlays (plan §9.1). Maps each GeometryOverlayDto the host projects
// off the scanner's live MarketContext (open FVGs / OBs / liquidity pools, the latest sweep / MSS, the OTE
// band) into the chart's ChartOverlay union, so the concept toggles have data even between confirmed setups.
// A read-only projection — it routes nowhere near an order path (§6.3).
// ---------------------------------------------------------------------------------------------------

import type { Direction, GeometryOverlayDto } from '../types/api';
import type { ChartOverlay } from '../types/overlays';

function asDirection(raw: string): Direction {
  return raw === 'Bearish' ? 'Bearish' : 'Bullish';
}

/** A short buy-/sell-side label (+ the equal-touch cluster count when >1). The chart appends " (swept)". */
function liquidityLabel(side: 'buy' | 'sell', strength: number | null | undefined): string {
  const base = side === 'buy' ? 'Buy-side' : 'Sell-side';
  return strength != null && strength > 1 ? `${base} ×${strength}` : base;
}

/**
 * Maps ONE live geometry DTO to a chart overlay, or null when the kind is unknown or the required prices are
 * absent (defensive — a box needs top+bottom, a level needs price). Unknown kinds (e.g. a future concept the
 * chart doesn't render yet) are skipped rather than throwing.
 */
export function geometryToOverlay(g: GeometryOverlayDto): ChartOverlay | null {
  const direction = asDirection(g.direction);

  switch (g.kind) {
    case 'fvg':
      if (g.top == null || g.bottom == null) return null;
      return {
        kind: 'fvg',
        direction,
        fromUtc: g.atUtc,
        toUtc: g.atUtc,
        top: g.top,
        bottom: g.bottom,
        // Anything past "Open" (Mitigated / VoidedTwoTouch) greys the gap on the chart.
        mitigated: (g.state ?? 'Open') !== 'Open',
      };

    case 'orderBlock':
      if (g.top == null || g.bottom == null || g.mid == null) return null;
      return {
        kind: 'orderBlock',
        direction,
        // An inverted order block is a breaker (§2.5.8).
        isBreaker: g.state === 'Inverted',
        fromUtc: g.atUtc,
        toUtc: g.atUtc,
        top: g.top,
        bottom: g.bottom,
        meanThreshold: g.mid,
      };

    case 'liquidity': {
      if (g.price == null) return null;
      const side = g.side === 'BuySide' ? 'buy' : 'sell';
      return {
        kind: 'liquidity',
        side,
        price: g.price,
        fromUtc: g.atUtc,
        label: liquidityLabel(side, g.strength),
        swept: g.swept ?? false,
      };
    }

    case 'sweep':
      if (g.price == null) return null;
      return { kind: 'sweep', direction, atUtc: g.atUtc, price: g.price };

    case 'mss':
      if (g.price == null) return null;
      return { kind: 'mss', direction, atUtc: g.atUtc, brokenSwingPrice: g.price };

    case 'ote':
      // top = the 62% retrace price, bottom = the 79%, mid = the 70.5% sweet spot (raw prices — the chart
      // labels them, so their relative order does not matter across a bullish vs bearish leg).
      if (g.top == null || g.bottom == null || g.mid == null) return null;
      return {
        kind: 'ote',
        direction,
        fromUtc: g.atUtc,
        toUtc: g.atUtc,
        band62: g.top,
        band79: g.bottom,
        sweetSpot705: g.mid,
      };

    default:
      return null;
  }
}

/** Maps a live geometry snapshot into chart overlays, dropping any that can't be rendered. */
export function geometryToOverlays(geometry: readonly GeometryOverlayDto[]): ChartOverlay[] {
  return geometry.flatMap((g) => {
    const overlay = geometryToOverlay(g);
    return overlay ? [overlay] : [];
  });
}
