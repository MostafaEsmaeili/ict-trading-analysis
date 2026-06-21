// ---------------------------------------------------------------------------------------------------
// Small presentational chips/badges shared by the panels (plan §9 "killzone badge + direction + style
// chip"). Pure, colour-driven from src/theme.ts so the semantic palette stays in one place.
// ---------------------------------------------------------------------------------------------------

import { directionTone, gradeColors, killzoneColors } from '../theme';
import type { Killzone } from '../types/api';

/** A killzone badge in its semantic colour (Asian indigo / London teal / NY orange / PM amber). */
export function KillzoneBadge({ killzone }: { killzone: string | null }): React.JSX.Element | null {
  if (!killzone || killzone === 'None') {
    return null;
  }
  const c = killzoneColors[killzone as Killzone] ?? killzoneColors.None;
  return (
    <span className="chip" style={{ color: c.fg, background: c.bg }} title={`Killzone: ${killzone}`}>
      {killzone}
    </span>
  );
}

/** A direction chip — green long/bullish, red short/bearish. */
export function DirectionChip({ direction }: { direction: string | null }): React.JSX.Element | null {
  if (!direction) {
    return null;
  }
  const tone = directionTone(direction);
  return (
    <span className={`chip ${tone}`} title={`Direction: ${direction}`}>
      {direction}
    </span>
  );
}

/** A trade-style chip (Scalp/Intraday/Swing/Position). */
export function StyleChip({ style }: { style: string | null }): React.JSX.Element | null {
  if (!style) {
    return null;
  }
  return (
    <span className="chip chip--style" title={`Style: ${style}`}>
      {style}
    </span>
  );
}

/** A setup-grade chip (A/B tradeable, C watchlist, Reject muted). */
export function GradeChip({ grade }: { grade: string }): React.JSX.Element {
  const c = gradeColors[grade] ?? gradeColors.Reject;
  return (
    <span className="chip" style={{ color: c.fg, background: c.bg }} title={`Grade: ${grade}`}>
      {grade}
    </span>
  );
}
