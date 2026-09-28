import PropTypes from 'prop-types';
import React from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './StarRating.css';

function StarRating({ rating, votes, iconSize }) {
  // Manga volumes carry a synthetic vote count (to satisfy the metadata popularity filter) but
  // no real per-volume score, so value stays 0. Render nothing rather than an empty "0.0 (N Votes)"
  // five-star widget, which reads as "rated 0/5".
  if (!rating) {
    return null;
  }

  const starWidth = {
    width: `${rating * 20}%`
  };

  const helpText = translate('StarRatingVotes', { rating: rating.toFixed(1), votes });

  return (
    <span className={styles.starRating} title={helpText}>
      <div className={styles.backStar}>
        <Icon name={icons.STAR_FULL} size={iconSize} />
        <Icon name={icons.STAR_FULL} size={iconSize} />
        <Icon name={icons.STAR_FULL} size={iconSize} />
        <Icon name={icons.STAR_FULL} size={iconSize} />
        <Icon name={icons.STAR_FULL} size={iconSize} />
        <div className={styles.frontStar} style={starWidth}>
          <Icon name={icons.STAR_FULL} size={iconSize} />
          <Icon name={icons.STAR_FULL} size={iconSize} />
          <Icon name={icons.STAR_FULL} size={iconSize} />
          <Icon name={icons.STAR_FULL} size={iconSize} />
          <Icon name={icons.STAR_FULL} size={iconSize} />
        </div>
      </div>
    </span>
  );
}

StarRating.propTypes = {
  rating: PropTypes.number.isRequired,
  votes: PropTypes.number.isRequired,
  iconSize: PropTypes.number.isRequired
};

StarRating.defaultProps = {
  iconSize: 14
};

export default StarRating;
