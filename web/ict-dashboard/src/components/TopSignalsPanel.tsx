// ---------------------------------------------------------------------------------------------------
// TopSignalsPanel — the Live-page right-rail summary: the top-3 TAKEABLE ranked signals, each with a
// Take (paper) button. Reuses useSignals (the shared cache SignalsUpdated pushes onto) + useTakeSignal.
// A compact peer of the Active Paper Trades panel — the operator can act on the best opportunity without
// leaving the Live view. Read-only/advisory: Take opens a PAPER trade only, no live order path (§6.3).
// ---------------------------------------------------------------------------------------------------

import { useMemo } from 'react';
import { useSignals, useTakeSignal } from '../api/hooks';
import type { Direction, Killzone, SetupGrade, TradeStyle } from '../types/api';
import { errorMessage } from '../format-error';
import { DirectionChip, GradeChip, KillzoneBadge, ModelBadge, StyleChip } from './Badges';
import { ScoreBar } from './ScoreBar';
import { TakeControl } from './SignalsFeed';
import { isTakeable } from './signalUtils';
import type { FocusTarget } from './AlertsFeed';

const TOP_N = 3;

export interface TopSignalsPanelProps {
  /** Focus the chart on a signal (symbol + timeframe + style). */
  onFocus?: (target: FocusTarget) => void;
}

export function TopSignalsPanel({ onFocus }: TopSignalsPanelProps): React.JSX.Element {
  const signalsQ = useSignals();
  const take = useTakeSignal();

  // The best TAKEABLE signals (Manual, not taken, not blocked) — the panel is an action shortlist, so it
  // shows only signals the operator can act on. Already-ranked by the backend; take the top N.
  const top = useMemo(
    () => (signalsQ.data ?? []).filter(isTakeable).slice(0, TOP_N),
    [signalsQ.data],
  );

  const takeError = take.isError ? errorMessage(take.error) : '';
  const takingId = take.isPending ? take.variables?.setupId ?? null : null;

  return (
    <section className="panel" aria-label="Top signals">
      <header className="panel__head">
        <span>Top Signals</span>
        <span className="badge-advisory">Paper</span>
      </header>
      <div className="panel__body panel__body--flush">
        {signalsQ.isError ? (
          <p className="empty error" role="alert">
            Signals unavailable — {errorMessage(signalsQ.error)}
          </p>
        ) : signalsQ.isLoading ? (
          <p className="empty">Loading signals…</p>
        ) : top.length === 0 ? (
          <p className="empty">No takeable signals right now.</p>
        ) : (
          <>
            {takeError ? (
              <p className="empty error" role="alert">
                Take failed — {takeError}
              </p>
            ) : null}
            {top.map((s) => {
              const su = s.setup;
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
                <div key={su.id} className="topsig-row">
                  <button
                    type="button"
                    className="topsig"
                    aria-label={`Focus chart on ${su.symbol}`}
                    onClick={focus}
                  >
                    <div className="topsig__top">
                      <span className="num" style={{ fontWeight: 700 }}>
                        #{s.rank} {su.symbol}
                      </span>
                      <DirectionChip direction={su.direction as Direction} />
                      <GradeChip grade={su.grade as SetupGrade} />
                      <StyleChip style={su.style as TradeStyle} />
                      <KillzoneBadge killzone={su.killzone as Killzone | null} />
                      <span className="chip chip--style num">{su.triggerTimeframe}</span>
                      <ModelBadge model={su.model} />
                    </div>
                    <ScoreBar score={s.score} grade={su.grade as SetupGrade} />
                  </button>
                  <div className="topsig__action">
                    <TakeControl signal={s} onTake={(id) => take.mutate({ setupId: id })} taking={takingId === su.id} />
                  </div>
                </div>
              );
            })}
          </>
        )}
      </div>
    </section>
  );
}
