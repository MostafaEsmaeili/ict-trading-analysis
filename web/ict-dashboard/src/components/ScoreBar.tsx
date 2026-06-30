// ---------------------------------------------------------------------------------------------------
// ScoreBar — a 0–100 weighted-§2.5.4-score meter, grade-tinted (A/B/C/Reject colours from the theme).
// Pure + presentational. Used by the signals feed row + the top-signals panel so the operator can rank a
// signal at a glance. Read-only/advisory — a score is analysis, never an order trigger (§6.3).
// ---------------------------------------------------------------------------------------------------

import { gradeColors } from '../theme';
import type { SetupGrade } from '../types/api';

export interface ScoreBarProps {
  /** The 0–100 score (clamped for display). */
  score: number;
  /** The setup grade — tints the fill (A/B green-ish, C amber, Reject muted). */
  grade: SetupGrade;
}

export function ScoreBar({ score, grade }: ScoreBarProps): React.JSX.Element {
  const clamped = Math.max(0, Math.min(100, score));
  const c = gradeColors[grade] ?? gradeColors.Reject;
  return (
    <div
      className="scorebar"
      role="meter"
      aria-valuenow={clamped}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-label={`Score ${clamped} of 100`}
      title={`Score ${clamped}/100`}
    >
      <div className="scorebar__track">
        <div
          className="scorebar__fill"
          style={{ width: `${clamped}%`, background: c.fg }}
        />
      </div>
      <span className="scorebar__num num" style={{ color: c.fg }}>
        {clamped}
      </span>
    </div>
  );
}
