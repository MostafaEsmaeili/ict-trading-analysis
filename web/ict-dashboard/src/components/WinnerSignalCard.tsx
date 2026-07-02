// ---------------------------------------------------------------------------------------------------
// WinnerSignalCard — the HERO card that features the single BEST opportunity right now: the #1 ranked
// signal (useSignals().data?.[0]). It leads the Live page (a full-width banner under the nav) and the
// Signals page, so the first thing the operator SEES is the top setup — grade, score, the entry/stop/
// targets, the RR, the killzone + timeframe, the one-line §2.5 reason, and a prominent Take (paper).
//
// DEFENSIVE GUARDRAIL (§6.3): the ONLY action is "Take (paper)" — its label/aria-label avoid every
// forbidden verb (execute/buy/sell/place-order/go-live). There is no live order path. An Auto signal
// opens itself (the engine arms/opens it), so it shows the "Auto — opens itself" chip instead of a
// button; a taken/blocked signal disables Take with a reason. Clicking the card body focuses the chart
// on the signal (symbol + timeframe + style), exactly like the SignalsFeed rows.
//
// It owns its own data (useSignals — the shared cache SignalsUpdated pushes onto — + useTakeSignal),
// mirroring TopSignalsPanel, so it can be dropped on either page with at most an onFocus callback.
// ---------------------------------------------------------------------------------------------------

import { useSignals, useTakeSignal } from '../api/hooks';
import type { Direction, Killzone, SetupGrade, TradeStyle } from '../types/api';
import { formatPrice } from '../format';
import { errorMessage } from '../format-error';
import { gradeColors } from '../theme';
import { DirectionChip, GradeChip, KillzoneBadge, ModelBadge, StyleChip } from './Badges';
import { ScoreBar } from './ScoreBar';
import { TakeControl } from './SignalsFeed';
import type { FocusTarget } from './AlertsFeed';

export interface WinnerSignalCardProps {
  /** Focus the chart on the featured signal (symbol + timeframe + style). */
  onFocus?: (target: FocusTarget) => void;
}

export function WinnerSignalCard({ onFocus }: WinnerSignalCardProps): React.JSX.Element {
  const signalsQ = useSignals();
  const take = useTakeSignal();

  // The single best opportunity = the #1 ranked signal (the backend ranks; rank 1 leads the list).
  const winner = signalsQ.data?.[0];

  const takeError = take.isError ? errorMessage(take.error) : '';
  const takingId = take.isPending ? take.variables?.setupId ?? null : null;

  // ----- Error / loading / empty states (calm, not alarming for the empty case) -----
  if (signalsQ.isError) {
    return (
      <section className="winner-card winner-card--empty" aria-label="Top signal">
        <p className="empty error" role="alert">
          Top signal unavailable — {errorMessage(signalsQ.error)}
        </p>
      </section>
    );
  }
  if (signalsQ.isLoading) {
    return (
      <section className="winner-card winner-card--empty" aria-label="Top signal">
        <p className="empty">Loading the top signal…</p>
      </section>
    );
  }
  if (!winner) {
    // A calm waiting state — no signal yet is normal (outside a killzone), NOT an error.
    return (
      <section className="winner-card winner-card--empty" aria-label="Top signal">
        <div className="winner-card__hero">
          <span className="winner-card__star" aria-hidden="true">
            ⭐
          </span>
          <span className="winner-card__title">Top Signal</span>
        </div>
        <p className="winner-card__empty-msg">No A/B setup right now — waiting for a killzone.</p>
      </section>
    );
  }

  const su = winner.setup;
  const grade = su.grade as SetupGrade;
  const gc = gradeColors[grade] ?? gradeColors.Reject;
  const focus = onFocus
    ? () =>
        onFocus({
          symbol: su.symbol,
          atUtc: su.detectedAtUtc,
          timeframe: su.triggerTimeframe,
          style: su.style as TradeStyle,
        })
    : undefined;

  // A grade-tinted accent border/glow drives the card's distinctive look (CSS reads these vars).
  const accentStyle = {
    '--winner-accent': gc.fg,
    '--winner-accent-bg': gc.bg,
  } as React.CSSProperties;

  return (
    <section
      className="winner-card winner-card--live"
      aria-label="Top signal"
      style={accentStyle}
    >
      <div className="winner-card__hero">
        <span className="winner-card__star" aria-hidden="true">
          ⭐
        </span>
        <span className="winner-card__title">Top Signal</span>
        <span className="winner-card__rank num" aria-label={`Rank ${winner.rank}`}>
          #{winner.rank}
        </span>
        <span className="badge-advisory winner-card__advisory">Advisory · Paper</span>
      </div>

      {/* The card body is a button so a click/Enter focuses the chart on this signal. */}
      <button
        type="button"
        className="winner-card__body"
        aria-label={`Focus chart on ${su.symbol}`}
        onClick={focus}
      >
        <div className="winner-card__id">
          <span className="winner-card__symbol num">{su.symbol}</span>
          <DirectionChip direction={su.direction as Direction} />
          <GradeChip grade={grade} />
          <StyleChip style={su.style as TradeStyle} />
          <KillzoneBadge killzone={su.killzone as Killzone | null} />
          <span className="chip chip--style num">{su.triggerTimeframe}</span>
          <ModelBadge model={su.model} />
        </div>

        <ScoreBar score={winner.score} grade={grade} />

        <div className="winner-card__levels num">
          <span className="winner-card__level">
            <span className="winner-card__level-label">Entry</span>
            <strong>{formatPrice(su.entry, su.symbol)}</strong>
          </span>
          <span className="winner-card__arrow" aria-hidden="true">
            →
          </span>
          <span className="winner-card__level short">
            <span className="winner-card__level-label">Stop</span>
            <strong>{formatPrice(su.stop, su.symbol)}</strong>
          </span>
          <span className="winner-card__arrow" aria-hidden="true">
            →
          </span>
          <span className="winner-card__level long">
            <span className="winner-card__level-label">
              {su.targets.length > 1 ? 'Targets' : 'Target'}
            </span>
            <strong>
              {su.targets.length > 0
                ? su.targets.map((t) => formatPrice(t, su.symbol)).join(' / ')
                : '—'}
            </strong>
          </span>
          <span className="winner-card__rr">RR {su.rewardRatio.toFixed(1)}</span>
        </div>

        <p className="winner-card__reason">{su.reason}</p>
      </button>

      {takeError ? (
        <p className="empty error winner-card__take-error" role="alert">
          Take failed — {takeError}
        </p>
      ) : null}

      <div className="winner-card__action">
        <TakeControl
          signal={winner}
          onTake={(id) => take.mutate({ setupId: id })}
          taking={takingId === su.id}
        />
      </div>
    </section>
  );
}
