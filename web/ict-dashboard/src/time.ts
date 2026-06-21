// ---------------------------------------------------------------------------------------------------
// Time formatting — New York by default (plan §4.8 / §9). UTC is the only source of truth on the wire;
// the dashboard renders in America/New_York (DST-aware) via date-fns-tz, labelled "NY". Never use the
// browser's local zone for ICT session math.
// ---------------------------------------------------------------------------------------------------

import { formatInTimeZone } from 'date-fns-tz';

export const NY_TZ = 'America/New_York';

/** A UTC ISO instant → "HH:mm:ss" in NY. */
export function formatNyTime(utcIso: string): string {
  return formatInTimeZone(new Date(utcIso), NY_TZ, 'HH:mm:ss');
}

/** A UTC ISO instant → "MMM d HH:mm" in NY (for alert/trade timestamps). */
export function formatNyDateTime(utcIso: string): string {
  return formatInTimeZone(new Date(utcIso), NY_TZ, 'MMM d HH:mm');
}

/** A UTC ISO instant → seconds since epoch (UTC) for the lightweight-charts UTCTimestamp axis. */
export function toUtcTimestamp(utcIso: string): number {
  return Math.floor(new Date(utcIso).getTime() / 1000);
}

/** Now → "HH:mm:ss NY" for the header clock. */
export function nyClockLabel(now: Date): string {
  return `${formatInTimeZone(now, NY_TZ, 'HH:mm:ss')} NY`;
}
