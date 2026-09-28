import PropTypes from 'prop-types';
import React from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import styles from './HeartRating.css';

function HeartRating({ rating, iconSize }) {
  // No real score (value 0) -> show nothing instead of an empty "0.0" heart. See StarRating.
  if (!rating) {
    return null;
  }

  return (
    <span className={styles.rating}>
      <Icon
        className={styles.heart}
        name={icons.HEART}
        size={iconSize}
      />

      {rating.toFixed(1)}
    </span>
  );
}

HeartRating.propTypes = {
  rating: PropTypes.number.isRequired,
  iconSize: PropTypes.number.isRequired
};

HeartRating.defaultProps = {
  iconSize: 14
};

export default HeartRating;
