// ---------------------------------------------------------------------------------------------------
// BalanceCurve (plan §15 §5) — the backtest equity curve via Recharts. Toggles between the ACCOUNT
// BALANCE (money) and the CUMULATIVE R (running ΣR) series carried on each BacktestEquityPointDto.
// The price chart stays lightweight-charts; this analytics curve stays Recharts (mirrors the
// PerformancePanel curve). Pure/presentational.
// ---------------------------------------------------------------------------------------------------

import { useState } from 'react';
import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import type { BacktestEquityPointDto } from '../types/api';
import { palette } from '../theme';
import { formatNyDateTime } from '../time';
import { formatMoney } from '../format';

export interface BalanceCurveProps {
  equity: BacktestEquityPointDto[];
}

type Mode = 'balance' | 'cumulativeR';

function domain(values: number[], anchorZero: boolean): [number, number] {
  const lo = anchorZero ? Math.min(0, ...values) : Math.min(...values);
  const hi = anchorZero ? Math.max(0, ...values) : Math.max(...values);
  const pad = Math.max(anchorZero ? 1 : (hi - lo) * 0.05 || 1, (hi - lo) * 0.05);
  return [lo - pad, hi + pad];
}

export function BalanceCurve({ equity }: BalanceCurveProps): React.JSX.Element {
  const [mode, setMode] = useState<Mode>('balance');

  const data = equity.map((p) => ({
    t: formatNyDateTime(p.atUtc),
    value: mode === 'balance' ? p.equity : p.cumulativeR,
  }));
  const yDomain = domain(
    data.map((d) => d.value),
    mode === 'cumulativeR',
  );

  return (
    <div className="balance-curve" data-testid="balance-curve">
      <div className="balance-curve__head">
        <span className="metric__label">{mode === 'balance' ? 'Account balance' : 'Cumulative R'}</span>
        <div className="seg" role="group" aria-label="Curve units">
          <button type="button" aria-pressed={mode === 'balance'} onClick={() => setMode('balance')}>
            $ Balance
          </button>
          <button
            type="button"
            aria-pressed={mode === 'cumulativeR'}
            onClick={() => setMode('cumulativeR')}
          >
            ΣR
          </button>
        </div>
      </div>
      <div style={{ height: 240 }}>
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={data} margin={{ top: 6, right: 12, bottom: 0, left: 4 }}>
            <defs>
              <linearGradient id="balanceFill" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={palette.accent} stopOpacity={0.35} />
                <stop offset="100%" stopColor={palette.accent} stopOpacity={0} />
              </linearGradient>
            </defs>
            <CartesianGrid stroke={palette.border} strokeDasharray="2 4" />
            <XAxis dataKey="t" tick={{ fill: palette.textFaint, fontSize: 10 }} minTickGap={32} />
            <YAxis
              tick={{ fill: palette.textFaint, fontSize: 10 }}
              domain={yDomain}
              width={70}
              tickFormatter={(v: number) =>
                mode === 'balance' ? formatMoney(v) : v.toFixed(1)
              }
            />
            <Tooltip
              contentStyle={{
                background: palette.panelRaised,
                border: `1px solid ${palette.borderStrong}`,
                borderRadius: 6,
                color: palette.text,
                fontSize: 12,
              }}
              formatter={(v: number) =>
                mode === 'balance' ? formatMoney(v) : `${v.toFixed(2)}R`
              }
            />
            <Area
              type="monotone"
              dataKey="value"
              stroke={palette.accent}
              strokeWidth={2}
              fill="url(#balanceFill)"
              isAnimationActive={false}
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
