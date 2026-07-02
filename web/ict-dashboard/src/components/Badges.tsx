// ---------------------------------------------------------------------------------------------------
// Small presentational chips/badges shared by the panels (plan §9 "killzone badge + direction + style
// chip"). Pure, colour-driven from src/theme.ts so the semantic palette stays in one place.
// ---------------------------------------------------------------------------------------------------

import { directionTone, gradeColors, killzoneColors } from '../theme';
import { modelBadgeText, modelLabel } from '../models';
import type {
  Direction,
  Killzone,
  SetupGrade,
  TradeCloseReason,
  TradeDirection,
  TradeStatus,
  TradeStyle,
} from '../types/api';

// Frozen DTO unions at the contracts-v1 boundary (no bare `string` — enum drift fails typecheck).
// A direction chip serves BOTH structure/setup direction (Bullish/Bearish) and a trade's side
// (Long/Short), so it accepts either frozen union.
type AnyDirection = Direction | TradeDirection;

/** A killzone badge in its semantic colour (Asian indigo / London teal / NY orange / PM amber). */
export function KillzoneBadge({
  killzone,
}: {
  killzone: Killzone | null;
}): React.JSX.Element | null {
  if (!killzone || killzone === 'None') {
    return null;
  }
  const c = killzoneColors[killzone] ?? killzoneColors.None;
  return (
    <span className="chip" style={{ color: c.fg, background: c.bg }} title={`Killzone: ${killzone}`}>
      {killzone}
    </span>
  );
}

/** A direction chip — green long/bullish, red short/bearish. */
export function DirectionChip({
  direction,
}: {
  direction: AnyDirection | null;
}): React.JSX.Element | null {
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
export function StyleChip({ style }: { style: TradeStyle | null }): React.JSX.Element | null {
  if (!style) {
    return null;
  }
  return (
    <span className="chip chip--style" title={`Style: ${style}`}>
      {style}
    </span>
  );
}

/**
 * A setup-model chip ("2022" / "2024") tagging which ICT model produced a setup/trade/alert. Renders
 * nothing when the model is absent (an old payload / a non-model-scoped alert) so callers can pass a
 * nullable field directly.
 */
export function ModelBadge({
  model,
}: {
  model: string | null | undefined;
}): React.JSX.Element | null {
  if (!model) {
    return null;
  }
  return (
    <span className="chip chip--model" title={`Setup model: ${modelLabel(model)}`}>
      {modelBadgeText(model)}
    </span>
  );
}

/** A setup-grade chip (A/B tradeable, C watchlist, Reject muted). */
export function GradeChip({ grade }: { grade: SetupGrade }): React.JSX.Element {
  const c = gradeColors[grade] ?? gradeColors.Reject;
  return (
    <span className="chip" style={{ color: c.fg, background: c.bg }} title={`Grade: ${grade}`}>
      {grade}
    </span>
  );
}

/** A trade-status pill — Open amber (in-flight), Closed neutral (settled). */
export function StatusPill({ status }: { status: TradeStatus }): React.JSX.Element {
  const cls = status === 'Open' ? 'pill pill--open' : 'pill pill--closed';
  return (
    <span className={cls} title={`Status: ${status}`}>
      {status}
    </span>
  );
}

/**
 * A close-reason pill in its semantic colour: TargetHit green (win), StopHit red (loss),
 * TimeExit amber (forced flat), Manual neutral. Null (an open trade) renders nothing.
 */
export function CloseReasonPill({
  reason,
}: {
  reason: TradeCloseReason | null;
}): React.JSX.Element | null {
  if (!reason) {
    return null;
  }
  const cls =
    reason === 'TargetHit'
      ? 'pill pill--win'
      : reason === 'StopHit'
        ? 'pill pill--loss'
        : reason === 'TimeExit'
          ? 'pill pill--time'
          : 'pill pill--manual';
  return (
    <span className={cls} title={`Close: ${reason}`}>
      {reason}
    </span>
  );
}
