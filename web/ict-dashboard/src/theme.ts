// ---------------------------------------------------------------------------------------------------
// Design tokens — dark trading-desk theme (plan §9 "Visual intent").
//
// Semantic colour: green long/win, red short/loss, amber pending. Killzone badge colours: Asian indigo,
// London teal, NY orange, PM amber (plan §9.1). Single source of truth so the CSS variables (index.css)
// and the lightweight-charts series colours can never drift. The `frontend-design` typography/palette/
// spacing pass is encoded here directly (the skill is not installed in this environment).
// ---------------------------------------------------------------------------------------------------

import type { Killzone } from './types/api';

export const palette = {
  // Surfaces — near-black desk with raised panels.
  bg: '#0b0e11',
  panel: '#12161c',
  panelRaised: '#171c24',
  border: '#232b36',
  borderStrong: '#313c4b',

  // Text.
  text: '#e6edf3',
  textMuted: '#8b98a9',
  textFaint: '#5a6675',

  // Semantic (direction / win-loss / pending).
  long: '#26a69a', // green — long / win / target
  short: '#ef5350', // red — short / loss / stop
  pending: '#f5a623', // amber — pending / partial
  entry: '#4c8dff', // blue — entry line
  neutral: '#8b98a9',

  // Accents.
  accent: '#4c8dff',
  ote: '#c792ea', // OTE band violet
} as const;

/** Killzone badge colours (plan §9.1). PM amber; AM reuses NY orange (instrument-class morning). */
export const killzoneColors: Record<Killzone, { fg: string; bg: string; band: string }> = {
  None: { fg: '#8b98a9', bg: 'rgba(139,152,169,0.12)', band: 'rgba(139,152,169,0.06)' },
  Asian: { fg: '#a99bf5', bg: 'rgba(124,108,235,0.16)', band: 'rgba(124,108,235,0.08)' }, // indigo
  LondonOpen: { fg: '#4fd1c5', bg: 'rgba(38,166,154,0.16)', band: 'rgba(38,166,154,0.08)' }, // teal
  LondonClose: { fg: '#4fd1c5', bg: 'rgba(38,166,154,0.16)', band: 'rgba(38,166,154,0.08)' }, // teal
  NewYorkOpen: { fg: '#ffa94d', bg: 'rgba(245,140,40,0.16)', band: 'rgba(245,140,40,0.08)' }, // orange
  Pm: { fg: '#f5c16c', bg: 'rgba(245,166,35,0.16)', band: 'rgba(245,166,35,0.08)' }, // amber
  Am: { fg: '#ffa94d', bg: 'rgba(245,140,40,0.16)', band: 'rgba(245,140,40,0.08)' }, // orange
};

/** Setup-grade chip colours (A/B tradeable green-ish, C watchlist amber, Reject muted). */
export const gradeColors: Record<string, { fg: string; bg: string }> = {
  A: { fg: '#26a69a', bg: 'rgba(38,166,154,0.16)' },
  B: { fg: '#7bc96f', bg: 'rgba(123,201,111,0.16)' },
  C: { fg: '#f5a623', bg: 'rgba(245,166,35,0.16)' },
  Reject: { fg: '#8b98a9', bg: 'rgba(139,152,169,0.12)' },
};

export type SemanticTone = 'long' | 'short' | 'pending' | 'neutral';

/** Maps a wire direction string (Bullish/Bearish/Long/Short) to a semantic tone. */
export function directionTone(direction: string | null | undefined): SemanticTone {
  switch (direction) {
    case 'Bullish':
    case 'Long':
      return 'long';
    case 'Bearish':
    case 'Short':
      return 'short';
    default:
      return 'neutral';
  }
}
