import PropTypes from 'prop-types';
import React from 'react';
import formatPrecisionDate from 'Utilities/Date/formatPrecisionDate';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './BookIndexPosterInfo.css';

function BookIndexPosterInfo(props) {
  const {
    qualityProfile,
    showQualityProfile,
    added,
    releaseDate,
    releaseDatePrecision,
    author,
    bookFileCount,
    sizeOnDisk,
    sortKey,
    showRelativeDates,
    shortDateFormat,
    timeFormat
  } = props;

  if (sortKey === 'qualityProfileId' && !showQualityProfile) {
    return (
      <div className={styles.info}>
        {qualityProfile.name}
      </div>
    );
  }

  if (sortKey === 'added' && added) {
    const addedDate = getRelativeDate(
      added,
      shortDateFormat,
      showRelativeDates,
      {
        timeFormat,
        timeForToday: false
      }
    );

    return (
      <div className={styles.info}>
        {translate('AddedRelativeDate', { date: addedDate })}
      </div>
    );
  }

  if (sortKey === 'releaseDate' && added) {
    // Preferred Edition (2026-09-24, D6, final fix round Minor 1): an edition's year/month date reads "2019" / "Nov 2026".
    const date = releaseDate && releaseDatePrecision ?
      formatPrecisionDate(releaseDate, releaseDatePrecision) :
      getRelativeDate(
        releaseDate,
        shortDateFormat,
        showRelativeDates,
        {
          timeFormat,
          timeForToday: false
        }
      );

    return (
      <div className={styles.info}>
        {translate('ReleasedRelativeDate', { date })}
      </div>
    );
  }

  if (sortKey === 'bookFileCount') {
    let books = translate('EditionFilesCountOne');

    if (bookFileCount === 0) {
      books = translate('NoFiles');
    } else if (bookFileCount > 1) {
      books = translate('EditionFilesCount', { count: bookFileCount });
    }

    return (
      <div className={styles.info}>
        {books}
      </div>
    );
  }

  if (sortKey === 'path') {
    return (
      <div className={styles.info}>
        {author.path}
      </div>
    );
  }

  if (sortKey === 'sizeOnDisk') {
    return (
      <div className={styles.info}>
        {formatBytes(sizeOnDisk)}
      </div>
    );
  }

  return null;
}

BookIndexPosterInfo.propTypes = {
  qualityProfile: PropTypes.object.isRequired,
  showQualityProfile: PropTypes.bool.isRequired,
  author: PropTypes.object.isRequired,
  added: PropTypes.string,
  releaseDate: PropTypes.string,
  releaseDatePrecision: PropTypes.string,
  bookFileCount: PropTypes.number.isRequired,
  sizeOnDisk: PropTypes.number,
  sortKey: PropTypes.string.isRequired,
  showRelativeDates: PropTypes.bool.isRequired,
  shortDateFormat: PropTypes.string.isRequired,
  timeFormat: PropTypes.string.isRequired
};

export default BookIndexPosterInfo;
