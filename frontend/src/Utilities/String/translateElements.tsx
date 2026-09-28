import React from 'react';
import translate from './translate';

// UI translations v1 (2026-09-25): a sentence with an inline element (an icon, a link, a bold run)
// is one key whose {name} placeholders are elements, so a translation can move the element instead
// of being cut into pieces around it. Placeholders without an element stay as written.
// `tokens` (Task 5) fills the sentence's data placeholders ({packageVersion}) the way translate()
// does; the element placeholders survive that pass because translate() leaves an unknown {name}.
export default function translateElements(
  key: string,
  elements: Record<string, React.ReactElement>,
  tokens: Record<string, string | number | boolean> = {}
) {
  return translate(key, tokens)
    .split(/(\{[a-z0-9]+\})/i)
    .map((part, index) => {
      const match = /^\{([a-z0-9]+)\}$/i.exec(part);
      const element = match ? elements[match[1]] : undefined;

      return element ? React.cloneElement(element, { key: index }) : part;
    });
}
