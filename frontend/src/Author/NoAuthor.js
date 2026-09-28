import PropTypes from 'prop-types';
import React from 'react';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './NoAuthor.css';

function NoAuthor(props) {
  const {
    totalItems
  } = props;

  // UI translations v1 (2026-09-25): the item noun is read at render time (a defaultProps
  // literal would stay English); BookIndex passes its own translated noun.
  const itemType = props.itemType ?? translate('ItemTypeSeries');

  if (totalItems > 0) {
    return (
      <div>
        <div className={styles.message}>
          {translate('AllItemsHiddenByFilter', { itemType })}
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className={styles.message}>
        {translate('NoItemsFoundGetStarted', { itemType })}
      </div>

      <div className={styles.buttonContainer}>
        <Button
          to="/add/import"
          kind={kinds.PRIMARY}
        >
          {translate('ImportExistingSeries')}
        </Button>
      </div>

      <div className={styles.buttonContainer}>
        <Button
          to="/add/search"
          kind={kinds.PRIMARY}
        >
          {translate('AddNewAuthor')}
        </Button>
      </div>
    </div>
  );
}

NoAuthor.propTypes = {
  totalItems: PropTypes.number.isRequired,
  itemType: PropTypes.string
};

export default NoAuthor;
