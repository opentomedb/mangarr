import React from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import AudiobookStorageCard from './AudiobookStorageCard';
import EbookStorageCard from './EbookStorageCard';
import styles from './LightNovelStorageCards.css';

// Connect pattern (2026-09-24): one FieldSet, two cards -- Ebooks and Audiobooks -- each opening an
// edit modal instead of inline fields. Neither card needs settings/onInputChange as props: each
// connects to the same settings.mediaManagement section its edit modal does, for its own summary line.
function LightNovelStorageCards() {
  return (
    <FieldSet legend={translate('LightNovelStorage')}>
      <div className={styles.cards}>
        <EbookStorageCard />
        <AudiobookStorageCard />
      </div>
    </FieldSet>
  );
}

export default LightNovelStorageCards;
