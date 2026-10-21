import PropTypes from 'prop-types';
import React from 'react';
import Button from 'Components/Link/Button';
import { kinds, sizes } from 'Helpers/Props';
import { LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';
import translate from 'Utilities/String/translate';
import styles from './AuthorIndexLibraryTabs.css';

// The library switch under the toolbar: Manga · Light Novels, the same look as a light-novel
// series page's Ebook | Audiobook tabs. The tab is the route. Shared by Library, Volumes,
// Collections, Wanted and Add New. Titles only, no counts (the maintainer, 2026-10-20).
function AuthorIndexLibraryTabs(props) {
  const {
    library,
    onLibrarySelect
  } = props;

  return (
    <div className={styles.tabs}>
      <Button
        kind={library === MANGA ? kinds.PRIMARY : kinds.DEFAULT}
        size={sizes.MEDIUM}
        onPress={() => onLibrarySelect(MANGA)}
      >
        {translate('Manga')}
      </Button>

      <Button
        kind={library === LIGHT_NOVEL ? kinds.PRIMARY : kinds.DEFAULT}
        size={sizes.MEDIUM}
        onPress={() => onLibrarySelect(LIGHT_NOVEL)}
      >
        {translate('LightNovels')}
      </Button>
    </div>
  );
}

AuthorIndexLibraryTabs.propTypes = {
  library: PropTypes.string.isRequired,
  onLibrarySelect: PropTypes.func.isRequired
};

export default AuthorIndexLibraryTabs;
