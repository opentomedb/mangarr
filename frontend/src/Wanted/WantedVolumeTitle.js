import PropTypes from 'prop-types';
import React from 'react';
import BookTitleLink from 'Book/BookTitleLink';
import translate from 'Utilities/String/translate';
import styles from './WantedVolumeTitle.css';

// Both Wanted tables name the series in their first column, so the Volume column drops the repeat:
// "Marriagetoxin | Marriagetoxin Vol. 14" reads "Marriagetoxin | Vol. 14". The volume number is the
// reliable way to say that, so it is used whenever the volume has one (spelled as the placeholder
// rows spell it). Stripping the series name off the title is the fallback for a volume that has no
// number; a title that is not the series name followed by something -- a one-off entry, a title the
// metadata spells its own way -- is left whole, because stripping it would leave the cell blank.
const LEADING_SEPARATORS = /^[\s:|·\-–—]+/;

function volumeTitle(title, seriesName, volumeNumber) {
  if (volumeNumber) {
    return translate('VolumeRowTitle', { volume: volumeNumber });
  }

  if (!seriesName || !title.startsWith(seriesName)) {
    return title;
  }

  const remainder = title.slice(seriesName.length).replace(LEADING_SEPARATORS, '');

  return remainder || title;
}

function WantedVolumeTitle(props) {
  const {
    titleSlug,
    title,
    seriesName,
    volumeNumber,
    subtitle,
    disambiguation
  } = props;

  return (
    <>
      <BookTitleLink
        titleSlug={titleSlug}
        title={volumeTitle(title, seriesName, volumeNumber)}
        disambiguation={disambiguation}
      />

      {
        subtitle ?
          <span className={styles.subtitle}>{subtitle}</span> :
          null
      }
    </>
  );
}

WantedVolumeTitle.propTypes = {
  titleSlug: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  seriesName: PropTypes.string,
  volumeNumber: PropTypes.number,
  subtitle: PropTypes.string,
  disambiguation: PropTypes.string
};

export default WantedVolumeTitle;
