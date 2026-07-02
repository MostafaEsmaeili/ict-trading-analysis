// ---------------------------------------------------------------------------------------------------
// SignalsFeed — the ranked, advisory live-signals list (the SIGNALS view + manual TAKE workflow). Each
// row shows the rank, grade chip, a grade-tinted ScoreBar, symbol + direction + style + killzone + TF
// chips, entry→stop, targets, RR, a one-line reason, an Auto/Manual indicator, and a Take (paper) button.
//
// DEFENSIVE GUARDRAIL (§6.3): "Take" opens a PAPER trade only — its label avoids execute/buy/sell/
// place-order/go-live (the Dashboard test asserts no such control). The button label is "Take (paper)"
// and its aria-label is `Take paper trade on ${symbol}`. There is no live order path anywhere.
//
// PRESENTATIONAL: the page owns the data (useSignals) + the take mutation (useTakeSignal); this component
// renders the list and calls onTake(setupId). A row body click focuses the chart on the signal's
// symbol + timeframe + style (the DTO carries them).
// ---------------------------------------------------------------------------------------------------

import type {
  Direction,
  Killzone,
  RankedSignalDto,
  SetupGrade,
  TradeStyle,
} from '../types/api';
import { formatPrice } from '../format';
import { errorMessage } from '../format-error';
import { DirectionChip, GradeChip, KillzoneBadge, ModelBadge, StyleChip } from './Badges';
import { ScoreBar } from './ScoreBar';
import { blockedReason } from './signalUtils';
import type { FocusTarget } from './AlertsFeed';

export interface SignalsFeedProps {
  signals: RankedSignalDto[];
  isLoading: boolean;
  isError?: boolean;
  error?: unknown;
  /** Open a PAPER trade off this signal (the page wires the useTakeSignal mutation). */
  onTake: (setupId: string) => void;
  /** The setupId currently being taken (shows "Pending…" + disables that row's button). */
  takingId?: string | null;
  /** Focus the chart on a signal (symbol + timeframe + style). */
  onFocus?: (target: FocusTarget) => void;
}

/**
 * The Take (paper) action for ONE signal. Auto signals render an "Auto — opens itself" chip instead of a
 * button. The button label/aria-label deliberately avoid every forbidden verb (execute/buy/sell/place-
 * order/go-live) so the defensive guardrail holds at the UI.
 */
export function TakeControl({
  signal,
  onTake,
  taking,
}: {
  signal: RankedSignalDto;
  onTake: (setupId: string) => void;
  taking: boolean;
}): React.JSX.Element {
  if (signal.entryMode === 'Auto') {
    return (
      <span className="chip chip--auto" title="The engine arms/opens this paper trade automatically">
        Auto — opens itself
      </span>
    );
  }
  const reason = blockedReason(signal);
  const disabled = taking || reason != null;
  return (
    <button
      type="button"
      className="btn btn--primary btn--take"
      disabled={disabled}
      title={reason ?? 'Open a paper trade off this signal'}
      aria-label={`Take paper trade on ${signal.setup.symbol}`}
      onClick={(e) => {
        e.stopPropagation();
        onTake(signal.setup.id);
      }}
    >
      {taking ? 'Pending…' : 'Take (paper)'}
    </button>
  );
}

function SignalRow({
  signal,
  onTake,
  taking,
  onFocus,
}: {
  signal: RankedSignalDto;
  onTake: (setupId: string) => void;
  taking: boolean;
  onFocus?: (target: FocusTarget) => void;
}): React.JSX.Element {
  const su = signal.setup;
  const target = su.targets.at(-1);
  const focus = onFocus
    ? () =>
        onFocus({
          symbol: su.symbol,
          atUtc: su.detectedAtUtc,
          timeframe: su.triggerTimeframe,
          style: su.style as TradeStyle,
        })
    : undefined;

  return (
    <div className="signal-row">
      <button
        type="button"
        className="signal"
        aria-label={`Focus chart on ${su.symbol}`}
        onClick={focus}
      >
        <div className="signal__rank num" aria-label={`Rank ${signal.rank}`}>
          #{signal.rank}
        </div>
        <div className="signal__main">
          <div className="signal__top">
            <span className="signal__symbol num">{su.symbol}</span>
            <DirectionChip direction={su.direction as Direction} />
            <GradeChip grade={su.grade as SetupGrade} />
            <StyleChip style={su.style as TradeStyle} />
            <KillzoneBadge killzone={su.killzone as Killzone | null} />
            <span className="chip chip--style num">{su.triggerTimeframe}</span>
            <ModelBadge model={su.model} />
            <span className={`chip ${signal.entryMode === 'Auto' ? 'chip--auto' : 'chip--manual'}`}>
              {signal.entryMode === 'Auto' ? 'Auto' : 'Manual'}
            </span>
          </div>
          <ScoreBar score={signal.score} grade={su.grade as SetupGrade} />
          <div className="signal__levels num">
            <span>
              entry <strong>{formatPrice(su.entry, su.symbol)}</strong>
            </span>
            <span className="short">
              stop {formatPrice(su.stop, su.symbol)}
            </span>
            <span className="long">
              target {target !== undefined ? formatPrice(target, su.symbol) : '—'}
            </span>
            <span className="signal__rr">RR {su.rewardRatio.toFixed(1)}</span>
          </div>
          <p className="signal__reason">{su.reason}</p>
        </div>
      </button>
      <div className="signal__action">
        <TakeControl signal={signal} onTake={onTake} taking={taking} />
      </div>
    </div>
  );
}

export function SignalsFeed({
  signals,
  isLoading,
  isError,
  error,
  onTake,
  takingId,
  onFocus,
}: SignalsFeedProps): React.JSX.Element {
  return (
    <section className="panel" aria-label="Signals feed">
      <header className="panel__head">
        <span>Signals</span>
        <span className="panel__head-right">
          <span className="neutral num">{signals.length}</span>
          <span className="badge-advisory">Advisory · Paper</span>
        </span>
      </header>
      <div className="panel__body panel__body--flush">
        {isError ? (
          <p className="empty error" role="alert">
            Signals unavailable — {errorMessage(error)}
          </p>
        ) : isLoading ? (
          <p className="empty">Loading signals…</p>
        ) : signals.length === 0 ? (
          <p className="empty">No signals match these filters.</p>
        ) : (
          signals.map((s) => (
            <SignalRow
              key={s.setup.id}
              signal={s}
              onTake={onTake}
              taking={takingId === s.setup.id}
              onFocus={onFocus}
            />
          ))
        )}
      </div>
    </section>
  );
}
