import PropTypes from 'prop-types';
import React from 'react';
import Label from 'Components/Label';
import { kinds, sizes } from 'Helpers/Props';
import { AUDIO, EBOOK, mediaTypeLabel } from 'Utilities/Book/mediaTypes';
import formatDate from 'Utilities/Date/formatDate';
import translate from 'Utilities/String/translate';
import styles from './FormatLabel.css';

// Ebook / Audiobook badges for a light-novel volume, one per entry of book.mediaTypes the caller
// hands in (optionally narrowed with `only`). Archive entries (manga) render nothing, so manga
// rows are unchanged. Colour: file present = success, covered by another volume's audiobook =
// info, pending (Not Yet) = disabled, unmonitored = warning, missing = danger.
function kindOf(entry) {
  if (entry.hasFile) {
    return kinds.SUCCESS;
  }

  if (entry.coveredByVolume != null) {
    return kinds.INFO;
  }

  if (entry.pending || entry.unreleased) {
    return kinds.DISABLED;
  }

  if (!entry.monitored) {
    return kinds.WARNING;
  }

  return kinds.DANGER;
}

function titleOf(entry) {
  if (entry.coveredByVolume != null) {
    return translate('CoveredByVolume', { volume: entry.coveredByVolume });
  }

  if (entry.pending) {
    return translate('AudioNotYetHelpText');
  }

  if (entry.unreleased) {
    return entry.audioReleaseDate ?
      translate('AudioUnreleasedHelpText', { date: formatDate(entry.audioReleaseDate.slice(0, 10), 'MMM D, YYYY') }) :
      translate('AudioUnlistedHelpText');
  }

  return undefined;
}

// The suffix a not-yet-available edition carries: the Audible date when known, Coming Soon when
// Audible does not list it yet, Not Yet while the series has no audiobook at all.
function suffixOf(entry) {
  if (!(entry.pending || entry.unreleased) || entry.coveredByVolume != null) {
    return '';
  }

  if (entry.unreleased) {
    return ` · ${entry.audioReleaseDate ? formatDate(entry.audioReleaseDate.slice(0, 10), 'MMM D') : translate('ComingSoon')}`;
  }

  return ` · ${translate('NotYet')}`;
}

function FormatLabel(props) {
  const {
    mediaTypes,
    only,
    size,
    className
  } = props;

  const entries = (mediaTypes || []).filter((m) => {
    return (m.mediaType === EBOOK || m.mediaType === AUDIO) && (!only || only.includes(m.mediaType));
  });

  if (!entries.length) {
    return null;
  }

  return (
    <span className={className}>
      {
        entries.map((m) => {
          return (
            <Label
              key={m.mediaType}
              className={styles.label}
              kind={kindOf(m)}
              size={size}
              title={titleOf(m)}
            >
              {mediaTypeLabel(m.mediaType)}{suffixOf(m)}
            </Label>
          );
        })
      }
    </span>
  );
}

FormatLabel.propTypes = {
  mediaTypes: PropTypes.arrayOf(PropTypes.object),
  only: PropTypes.arrayOf(PropTypes.string),
  size: PropTypes.string,
  className: PropTypes.string
};

FormatLabel.defaultProps = {
  size: sizes.SMALL
};

export default FormatLabel;
