// ---------------------------------------------------------------------------------------------------
// useNyClock — the live New York desk clock (plan §9 — times default to New York). Owns the 1s timer
// so the Dashboard shell stays presentational. Returns a pre-formatted "HH:mm:ss NY" label.
// ---------------------------------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import { nyClockLabel } from '../time';

/** Ticks once a second; returns the current NY-time label. */
export function useNyClock(): string {
  const [clock, setClock] = useState(() => nyClockLabel(new Date()));

  useEffect(() => {
    const id = setInterval(() => setClock(nyClockLabel(new Date())), 1000);
    return () => clearInterval(id);
  }, []);

  return clock;
}
