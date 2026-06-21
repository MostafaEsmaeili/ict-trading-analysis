// ---------------------------------------------------------------------------------------------------
// useOverlayVisibility — per-kind chart-overlay legend toggles (plan §9.1: every overlay toggles
// individually). Owns the visibility map + the toggle callback so the Dashboard shell stays
// presentational. Starts from the all-on default.
// ---------------------------------------------------------------------------------------------------

import { useCallback, useState } from 'react';
import { defaultOverlayVisibility, type OverlayKind, type OverlayVisibility } from '../types/overlays';

export interface OverlayVisibilityState {
  visibility: OverlayVisibility;
  toggleOverlay: (kind: OverlayKind) => void;
}

export function useOverlayVisibility(): OverlayVisibilityState {
  const [visibility, setVisibility] = useState(defaultOverlayVisibility);

  const toggleOverlay = useCallback((kind: OverlayKind) => {
    setVisibility((v) => ({ ...v, [kind]: !v[kind] }));
  }, []);

  return { visibility, toggleOverlay };
}
