// ---------------------------------------------------------------------------------------------------
// Plain-language glossary for the Settings page (operator complaint: "the setting part is hard to
// understand"). Each entry maps a jargon TERM → a short, non-expert explanation surfaced by InfoTip.
//
// This is pure client copy — it does NOT change the wire contract or the model. It only explains the
// concepts the existing settings already expose. Keep the wording plain (a non-trader should follow it)
// and keep it consistent with the ICT model the backend actually runs (§2.5).
// ---------------------------------------------------------------------------------------------------

export interface GlossaryEntry {
  /** The short headline shown at the top of the tooltip. */
  title: string;
  /** One or two plain sentences a non-expert can follow. */
  body: string;
}

/** The glossary, keyed by a stable short id (used as the InfoTip `term`). */
export const GLOSSARY: Readonly<Record<string, GlossaryEntry>> = {
  confluence: {
    title: 'Confluence',
    body: 'How many independent reasons line up before the scanner calls a trade. More reasons agreeing = a higher-quality signal. The model weighs a fixed set of these reasons and only alerts when enough of them stack up.',
  },
  kOfN: {
    title: 'Required conditions (k of n)',
    body: 'The model normally needs ALL of its required checks to pass (strict). "k of n" relaxes that so a signal can fire when only k of the n checks pass — more signals, but each is a bit less certain. Leave it blank to keep the strict default.',
  },
  htfBias: {
    title: 'HTF daily-bias agreement',
    body: 'A higher-timeframe filter: only take a trade if its direction agrees with where price sits versus the day’s opening reference (above = lean short, below = lean long). It throws away trades that fight the bigger trend. Off by default.',
  },
  referenceOpen: {
    title: 'Reference open',
    body: 'The day’s anchor price (the midnight-NY open, or the 08:30 macro open for indices). The model compares current price to it to decide the day’s lean.',
  },
  killzone: {
    title: 'Killzone',
    body: 'A specific time window during the day when ICT setups are most likely (e.g. London Open 02:00–05:00 NY, New York Open 07:00–10:00 NY). The scanner only hunts inside the killzones you select.',
  },
  requiredSubset: {
    title: 'Required subset',
    body: 'Which specific checks must pass for THIS instrument. Leave empty to inherit the global set. If you pick your own list it must include the direction lock (see DisplacementMss).',
  },
  displacementMss: {
    title: 'DisplacementMss (direction lock)',
    body: 'The market-structure shift with displacement — a forceful break that tells the model which way to trade. It is mandatory in every required set because it is what locks the trade’s direction; the engine cannot run without it.',
  },
  gradeThresholds: {
    title: 'Grade thresholds (A / B / C)',
    body: 'Every signal gets a 0–100 confluence score, then a letter grade. A is the strongest, B the alert floor, C below the bar. Only A and B trigger alerts/trades by default.',
  },
  ote: {
    title: 'OTE (Optimal Trade Entry)',
    body: 'A retracement zone (about 62–79% back into the move) where ICT looks to enter. Entering here keeps the stop tight and the reward-to-risk high.',
  },
  drawOnLiquidity: {
    title: 'Draw on liquidity',
    body: 'Where price is likely heading next — a pool of resting orders (old highs/lows) the move is "drawn" toward. The model uses it to set the profit target.',
  },
  expectancy: {
    title: 'Expectancy',
    body: 'The average profit (in R, i.e. multiples of the risk per trade) you can expect per trade over many trades. Positive = the strategy makes money on average.',
  },
  profitFactor: {
    title: 'Profit factor',
    body: 'Total winnings divided by total losses. Above 1 means wins outweigh losses; the higher the better. Shown as ∞ when there are no losses yet.',
  },
  liquiditySweep: {
    title: 'Liquidity sweep',
    body: 'A quick spike that runs past an old high or low to trip stop-losses, then snaps back. ICT treats this "stop hunt" as the trigger that often precedes the real move.',
  },
  riskPerTrade: {
    title: 'Risk per trade',
    body: 'The slice of the account risked on one trade (e.g. 1%). The model sizes each position so a full stop-out loses exactly this amount.',
  },
  portfolioCap: {
    title: 'Portfolio risk cap',
    body: 'The most total risk allowed across all open trades at once (e.g. 5%). New trades are refused once this cap is reached.',
  },
  spreadCommission: {
    title: 'Spread & commission',
    body: 'The trading costs the simulator subtracts so paper results stay honest: the spread (gap between buy and sell price) and the broker commission per lot.',
  },
  minStop: {
    title: 'Minimum stop distance',
    body: 'The smallest allowed stop size (in pips/points). It stops the model from sizing up dangerously on an unrealistically tight stop.',
  },
  alertFloor: {
    title: 'Alert floor',
    body: 'The lowest grade that still produces an alert/paper trade. By default that is grade B — anything weaker is ignored.',
  },
  entryMode: {
    title: 'Entry mode (Auto / Manual)',
    body: 'How a confirmed setup becomes a paper trade. Auto: the engine arms/opens it automatically. Manual: it is only ranked/alerted on the Signals page and you take it with the Take button. Either way it is paper — there is never a live order.',
  },
} as const;

/** Type-safe set of glossary ids so callers can’t reference a missing term. */
export type GlossaryTerm = keyof typeof GLOSSARY;
