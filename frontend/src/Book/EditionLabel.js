import PropTypes from 'prop-types';
import React from 'react';
import Label from 'Components/Label';
import { kinds, sizes } from 'Helpers/Props';
import { AUDIO, EBOOK, mediaTypeLabel, mediaTypeOfQuality } from 'Utilities/Book/mediaTypes';
import styles from './FormatLabel.css';

// UI pass (2026-09-24, AC-4): the Ebook / Audiobook badge Calendar and Wanted show (FormatLabel),
// for a Queue or History row of a light novel. The row is one release, so the edition comes from
// its quality (MediaTypes.OfQuality) and the badge is neutral -- whether the volume already has
// that edition is not what the row is about. Manga rows render nothing.
function EditionLabel({ library, quality }) {
  const mediaType = library === 'lightNovel' && quality && quality.quality ?
    mediaTypeOfQuality(quality.quality.name) :
    null;

  if (mediaType !== EBOOK && mediaType !== AUDIO) {
    return null;
  }

  return (
    <Label
      className={styles.label}
      kind={kinds.DEFAULT}
      size={sizes.SMALL}
    >
      {mediaTypeLabel(mediaType)}
    </Label>
  );
}

EditionLabel.propTypes = {
  library: PropTypes.string,
  quality: PropTypes.object
};

export default EditionLabel;
