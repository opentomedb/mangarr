import React from 'react';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import styles from './UpdateChanges.css';

interface UpdateChangesProps {
  title: string;
  changes: string[];
}

function UpdateChanges(props: UpdateChangesProps) {
  const { title, changes } = props;

  if (changes.length === 0) {
    return null;
  }

  const uniqueChanges = [...new Set(changes)];

  return (
    <div>
      <div className={styles.title}>{title}</div>
      <ul>
        {uniqueChanges.map((change, index) => {
          // Beta docs (2026-09-28): #N links to Mangarr's own issues. Mangarr's numbers are short, so
          // any #N at the start or after a space or "(" -- not inside a word.
          const checkChange = change.replace(
            /(^|[\s(])#(\d+)\b/g,
            (_match, before, issue) =>
              `${before}[#${issue}](https://github.com/opentomedb/mangarr/issues/${issue})`
          );

          return (
            <li key={index}>
              <InlineMarkdown data={checkChange} />
            </li>
          );
        })}
      </ul>
    </div>
  );
}

export default UpdateChanges;
