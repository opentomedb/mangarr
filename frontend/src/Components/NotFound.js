import PropTypes from 'prop-types';
import React from 'react';
import PageContent from 'Components/Page/PageContent';
import translate from 'Utilities/String/translate';
import styles from './NotFound.css';

// The 404 card: a kaomoji in the brand purple instead of Readarr's green monster
// (the maintainer, 2026-09-19). The message stays a prop so the series / volume pages keep
// their own "cannot be found" lines.
function NotFound({ message }) {
  return (
    <PageContent title={translate('MIA')}>
      <div className={styles.container}>
        <div className={styles.message}>
          {message ?? translate('NotFoundMessage')}
        </div>

        <div className={styles.card}>
          <div className={styles.kaomoji}>
            (╯°□°)╯︵ ┻━┻
          </div>
          <div className={styles.code}>
            404
          </div>
          <div className={styles.caption}>
            {translate('NotFoundCaption')}
          </div>
        </div>
      </div>
    </PageContent>
  );
}

NotFound.propTypes = {
  message: PropTypes.string
};

export default NotFound;
