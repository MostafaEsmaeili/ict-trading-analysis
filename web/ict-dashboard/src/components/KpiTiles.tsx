// ---------------------------------------------------------------------------------------------------
// KpiTiles (plan §15 §5) — a row of KPI tiles for the Backtest results (ending balance + Δ, trades,
// win rate, avg R, profit factor, max DD R, setups). Pure/presentational; reuses the .metric tokens.
// ---------------------------------------------------------------------------------------------------

export interface KpiTile {
  label: string;
  value: string;
  tone?: 'long' | 'short' | 'neutral';
}

export function KpiTiles({ tiles }: { tiles: KpiTile[] }): React.JSX.Element {
  return (
    <div className="kpis" data-testid="kpi-tiles">
      {tiles.map((t) => (
        <div className="metric" key={t.label}>
          <div className="metric__label">{t.label}</div>
          <div className={`metric__value${t.tone ? ` ${t.tone}` : ''}`}>{t.value}</div>
        </div>
      ))}
    </div>
  );
}
