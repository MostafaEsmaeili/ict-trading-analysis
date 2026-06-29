// ---------------------------------------------------------------------------------------------------
// InfoTip — an accessible "?" help popover for the Settings page jargon. It is a real focusable button
// (NOT a bare `title=` attribute, which keyboard + screen-reader users can't reach) that shows a short
// plain-language explanation on hover/focus, wired with `aria-describedby` so assistive tech announces
// the help text as the button's description.
//
// Content comes from the pure glossary (src/settings/glossary.ts) keyed by `term`, or an explicit
// `title`/`children` override. Read-only: a help popover carries no order/execute control (§6.3).
// ---------------------------------------------------------------------------------------------------

import { useId, useState } from 'react';
import { GLOSSARY, type GlossaryTerm } from '../settings/glossary';

interface InfoTipProps {
  /** A glossary id — its title/body fill the popover. Omit when passing explicit `title`/`children`. */
  term?: GlossaryTerm;
  /** Override headline (defaults to the glossary entry's title). */
  title?: string;
  /** Override body text (defaults to the glossary entry's body). */
  children?: React.ReactNode;
  /** Accessible label for the trigger button (defaults to "Help: <title>"). */
  label?: string;
}

export function InfoTip({ term, title, children, label }: InfoTipProps): React.JSX.Element {
  const entry = term ? GLOSSARY[term] : undefined;
  const headline = title ?? entry?.title ?? 'Help';
  const body = children ?? entry?.body ?? '';
  const popId = useId();
  const [open, setOpen] = useState(false);

  return (
    <span
      className="infotip"
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
    >
      <button
        type="button"
        className="infotip__btn"
        aria-label={label ?? `Help: ${headline}`}
        aria-describedby={popId}
        aria-expanded={open}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onClick={() => setOpen((o) => !o)}
        onKeyDown={(e) => {
          if (e.key === 'Escape') setOpen(false);
        }}
      >
        ?
      </button>
      {/* Always rendered (so aria-describedby resolves for screen readers); visually toggled by `open`. */}
      <span
        id={popId}
        role="tooltip"
        className={`infotip__pop${open ? ' infotip__pop--open' : ''}`}
      >
        <strong className="infotip__title">{headline}</strong>
        <span className="infotip__body">{body}</span>
      </span>
    </span>
  );
}
