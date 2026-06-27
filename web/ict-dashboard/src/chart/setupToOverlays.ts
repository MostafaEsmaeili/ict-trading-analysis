// ---------------------------------------------------------------------------------------------------
// Setup → overlay geometry (plan §9.1). The host derives full overlay geometry from a Setup's Evidence
// JSONB; until WP7 emits that, this turns the priced fields a SetupDto already carries (entry/stop/
// targets/RR/direction) into the trade-level + draw overlays so a streamed `SetupDetected` still draws
// something meaningful on the chart. Richer zone geometry (FVG/OB/OTE bounds) arrives with Evidence.
// ---------------------------------------------------------------------------------------------------

import type { Direction, SetupDto } from '../types/api';
import type { ChartOverlay } from '../types/overlays';

function asDirection(raw: string): Direction {
  return raw === 'Bearish' ? 'Bearish' : 'Bullish';
}

export function setupToOverlays(setup: SetupDto): ChartOverlay[] {
  const direction = asDirection(setup.direction);
  const overlays: ChartOverlay[] = [
    {
      kind: 'tradeLevels',
      direction,
      entry: setup.entry,
      stop: setup.stop,
      targets: setup.targets,
      rewardRatio: setup.rewardRatio,
      // The entry MARKER pins to the confirming candle (detectedAtUtc) so the chart shows WHEN the
      // trade enters, not only the entry price line. The symbol drives the marker's price-label precision.
      entryUtc: setup.detectedAtUtc,
      symbol: setup.symbol,
      setupId: setup.id,
    },
  ];

  const draw = setup.targets.at(-1);
  if (draw !== undefined) {
    overlays.push({
      kind: 'drawOnLiquidity',
      direction,
      fromUtc: setup.detectedAtUtc,
      targetPrice: draw,
      setupId: setup.id,
    });
  }

  return overlays;
}
