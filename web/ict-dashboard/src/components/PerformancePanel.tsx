// ---------------------------------------------------------------------------------------------------
// Performance panel (right/bottom, plan §9 / §5.3) — win rate, avg R, profit factor, max drawdown +
// the equity curve (via Recharts, the one chart that stays Recharts; the price chart is lightweight-
// charts — §9.1). All figures come from the closed-trade performance calculator (WP6); mocked for now.
// ---------------------------------------------------------------------------------------------------

import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import type { EquityPointDto, PerformanceSummaryDto } from '../types/api';
import { UNDEFINED_PROFIT_FACTOR } from '../types/api';
import { palette } from '../theme';
import { formatNyDateTime } from '../time';

export interface PerformancePanelProps {
  summary: PerformanceSummaryDto | undefined;
  equityCurve: EquityPointDto[];
  isLoading: boolean;
}

function pct(v: number): string {
  return `${(v * 100).toFixed(1)}%`;
}

/**
 * Profit factor display. The backend emits the {@link UNDEFINED_PROFIT_FACTOR} sentinel for
 * "no losing trades" (undefined / infinite) — render it as ∞, with n/a when there are no trades.
 */
function fmtProfitFactor(profitFactor: number, tradeCount: number): string {
  if (tradeCount === 0) return 'n/a';
  if (profitFactor >= UNDEFINED_PROFIT_FACTOR) return '∞';
  return profitFactor.toFixed(2);
}

/**
 * Y-axis domain for the cumulative-R equity curve. The wire emits cumulative R from a zero baseline
 * (single-digit range), so pad relative to the data range and anchor to 0 — never a fixed dollar pad.
 */
function equityDomain(values: number[]): [number, number] {
  const lo = Math.min(0, ...values);
  const hi = Math.max(0, ...values);
  const pad = Math.max(1, (hi - lo) * 0.1);
  return [lo - pad, hi + pad];
}

export function PerformancePanel({
  summary,
  equityCurve,
  isLoading,
}: PerformancePanelProps): React.JSX.Element {
  const data = equityCurve.map((p) => ({ t: formatNyDateTime(p.atUtc), equity: p.equity }));
  const yDomain = equityDomain(data.map((d) => d.equity));

  return (
    <section className="panel" aria-label="Performance">
      <header className="panel__head">
        <span>Performance</span>
        {summary ? <span className="neutral num">{summary.tradeCount} trades</span> : null}
      </header>
      <div className="panel__body">
        {isLoading || !summary ? (
          <p className="empty">Loading performance…</p>
        ) : (
          <>
            <div className="metrics">
              <div className="metric">
                <div className="metric__label">Win rate</div>
                <div className="metric__value long">{pct(summary.winRate)}</div>
              </div>
              <div className="metric">
                <div className="metric__label">Avg R</div>
                <div className={`metric__value ${summary.averageR >= 0 ? 'long' : 'short'}`}>
                  {summary.averageR.toFixed(2)}
                </div>
              </div>
              <div className="metric">
                <div className="metric__label">Profit factor</div>
                <div className="metric__value">
                  {fmtProfitFactor(summary.profitFactor, summary.tradeCount)}
                </div>
              </div>
              <div className="metric">
                <div className="metric__label">Expectancy</div>
                <div className="metric__value">{summary.expectancy.toFixed(2)}R</div>
              </div>
              <div className="metric">
                <div className="metric__label">Max DD (R)</div>
                <div className="metric__value short">{summary.maxDrawdown.toFixed(2)}R</div>
              </div>
            </div>

            <div style={{ height: 160, marginTop: 12 }} data-testid="equity-curve">
              <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={data} margin={{ top: 6, right: 6, bottom: 0, left: -18 }}>
                  <defs>
                    <linearGradient id="equityFill" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor={palette.long} stopOpacity={0.35} />
                      <stop offset="100%" stopColor={palette.long} stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid stroke={palette.border} strokeDasharray="2 4" />
                  <XAxis dataKey="t" tick={{ fill: palette.textFaint, fontSize: 10 }} minTickGap={24} />
                  <YAxis
                    tick={{ fill: palette.textFaint, fontSize: 10 }}
                    domain={yDomain}
                    width={48}
                    label={{
                      value: 'Cumulative R',
                      angle: -90,
                      position: 'insideLeft',
                      fill: palette.textFaint,
                      fontSize: 10,
                      style: { textAnchor: 'middle' },
                    }}
                  />
                  <Tooltip
                    contentStyle={{
                      background: palette.panelRaised,
                      border: `1px solid ${palette.borderStrong}`,
                      borderRadius: 6,
                      color: palette.text,
                      fontSize: 12,
                    }}
                  />
                  <Area
                    type="monotone"
                    dataKey="equity"
                    stroke={palette.long}
                    strokeWidth={2}
                    fill="url(#equityFill)"
                    isAnimationActive={false}
                  />
                </AreaChart>
              </ResponsiveContainer>
            </div>
          </>
        )}
      </div>
    </section>
  );
}
