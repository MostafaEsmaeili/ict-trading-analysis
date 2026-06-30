// ---------------------------------------------------------------------------------------------------
// useLocalStorageState — a tiny useState that persists a JSON-serialisable value to localStorage under a
// key, so a UI preference (e.g. the Settings Simple/Advanced mode) survives a reload. SSR/test-safe: it
// reads lazily and swallows storage errors (jsdom/private-mode) so it can never crash a render.
// ---------------------------------------------------------------------------------------------------

import { useCallback, useState } from 'react';

export function useLocalStorageState<T>(key: string, initial: T): [T, (next: T) => void] {
  const [value, setValue] = useState<T>(() => {
    try {
      const raw = globalThis.localStorage?.getItem(key);
      return raw == null ? initial : (JSON.parse(raw) as T);
    } catch {
      return initial;
    }
  });

  const set = useCallback(
    (next: T) => {
      setValue(next);
      try {
        globalThis.localStorage?.setItem(key, JSON.stringify(next));
      } catch {
        // ignore write failures (private mode / quota / no storage in the test env)
      }
    },
    [key],
  );

  return [value, set];
}
