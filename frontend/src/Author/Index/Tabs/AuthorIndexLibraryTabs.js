import PropTypes from 'prop-types';
import React from 'react';
import Button from 'Components/Link/Button';
import { kinds, sizes } from 'Helpers/Props';
import { LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';
import translate from 'Utilities/String/translate';
import styles from './AuthorIndexLibraryTabs.css';

// The library switch under the toolbar: Manga <n> · Light Novels <n>, the same look as a
// light-novel series page's Ebook | Audiobook tabs. The tab is the route; the counts are each
// library's entries before any filter. Shared by Library, Volumes, Collections, Wanted and Add
// New; a page with no meaningful count (Wanted, Add New) omits them.
function AuthorIndexLibraryTabs(props) {
  const {
    library,
    mangaCount,
    lightNovelCount,
    onLibrarySelect
  } = props;

  return (
    <div className={styles.tabs}>
      <Button
        kind={library === MANGA ? kinds.PRIMARY : kinds.DEFAULT}
        size={sizes.MEDIUM}
        onPress={() => onLibrarySelect(MANGA)}
      >
        {translate('Manga')}{mangaCount == null ? null : <span className={styles.tabCount}> {mangaCount}</span>}
      </Button>

      <Button
        kind={library === LIGHT_NOVEL ? kinds.PRIMARY : kinds.DEFAULT}
        size={sizes.MEDIUM}
        onPress={() => onLibrarySelect(LIGHT_NOVEL)}
      >
        {translate('LightNovels')}{lightNovelCount == null ? null : <span className={styles.tabCount}> {lightNovelCount}</span>}
      </Button>
    </div>
  );
}

AuthorIndexLibraryTabs.propTypes = {
  library: PropTypes.string.isRequired,
  mangaCount: PropTypes.number,
  lightNovelCount: PropTypes.number,
  onLibrarySelect: PropTypes.func.isRequired
};

export default AuthorIndexLibraryTabs;
