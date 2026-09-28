import PropTypes from 'prop-types';
import React from 'react';
import QueueDetails from 'Activity/Queue/QueueDetails';
import BookQuality from 'Book/BookQuality';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import ProgressBar from 'Components/ProgressBar';
import { icons, kinds, sizes } from 'Helpers/Props';
import formatDate from 'Utilities/Date/formatDate';
import translate from 'Utilities/String/translate';
import styles from './BookStatus.css';

function BookStatus(props) {
  const {
    isAvailable,
    monitored,
    pending,
    unreleased,
    audioReleaseDate,
    coveredByVolume,
    grabbed,
    queueItem,
    bookFile,
    fileCount
  } = props;

  const hasBookFile = !!bookFile;
  // An audio edition is several files (MP3 parts); the badge says so.
  const parts = fileCount > 1 ? ` ${translate('FileParts', { count: fileCount })}` : '';

  // Live download lifecycle (series page round, 2026-09-24): Sonarr's EpisodeStatus -- the
  // queue item alone, as an import-state icon or a small purple bar with the percentage and the
  // release title in its tooltip. The connector passes a queue item only when the download will
  // fill this volume (queueItemFillsFiles), so an owned volume a pack won't change keeps its
  // quality badge below.
  if (queueItem) {
    const {
      title,
      size,
      sizeleft
    } = queueItem;

    const progress = size ? (100 - sizeleft / size * 100) : 0;

    return (
      <div className={styles.center}>
        <QueueDetails
          {...queueItem}
          progressBar={
            <ProgressBar
              title={translate('BookIsDownloadingInterp', [progress.toFixed(1), title])}
              progress={progress}
              kind={kinds.PURPLE}
              size={sizes.MEDIUM}
            />
          }
        />
      </div>
    );
  }

  if (hasBookFile) {
    const quality = bookFile.quality;

    return (
      <div className={styles.center}>
        <BookQuality
          title={`${translate('Downloaded')} – ${quality.quality.name}${parts}`}
          size={bookFile.size}
          quality={quality}
          isMonitored={monitored}
          isCutoffNotMet={bookFile.qualityCutoffNotMet}
        />
      </div>
    );
  }

  // Grabbed but not yet showing in the queue (brief window between grab and the download client
  // reporting it). Checked after hasBookFile by design: during an upgrade we keep showing the
  // existing quality badge until the queue item appears (then the queueItem branch above wins),
  // so this bare icon only fires for fileless books.
  if (grabbed) {
    return (
      <div className={styles.center}>
        <Icon
          name={icons.DOWNLOADING}
          title={translate('BookIsDownloading')}
        />
      </div>
    );
  }

  // Covered volumes (2026-09-17, D8): the audiobook is inside another volume's file — satisfied,
  // whatever the monitored / pending / available flags say below.
  if (coveredByVolume != null) {
    return (
      <div className={styles.center}>
        <Label
          title={translate('CoveredByVolume', { volume: coveredByVolume })}
          kind={kinds.INFO}
        >
          {translate('CoveredByVolume', { volume: coveredByVolume })}
        </Label>
      </div>
    );
  }

  if (!monitored) {
    return (
      <div className={styles.center}>
        <Label
          title={translate('NotMonitored')}
          kind={kinds.WARNING}
        >
          {translate('NotMonitored')}
        </Label>
      </div>
    );
  }

  // Unreleased audio (2026-09-21): the series has audiobooks, but Audible lists this one for a
  // later date (shown) or not yet at all. Not missing; it becomes wanted once it is out.
  if (unreleased) {
    // A calendar date (UTC midnight): format the date part only, or CDT shows the day before.
    const when = audioReleaseDate ? formatDate(audioReleaseDate.slice(0, 10), 'MMM D, YYYY') : null;

    return (
      <div className={styles.center}>
        <Label
          title={when ? translate('AudioUnreleasedHelpText', { date: when }) : translate('AudioUnlistedHelpText')}
          kind={kinds.DISABLED}
        >
          {when || translate('ComingSoon')}
        </Label>
      </div>
    );
  }

  // A light-novel audio edition of a series that has no audio yet (contract "Pending audio"):
  // wanted, but searched only weekly and shown grey, not as a red Missing.
  if (pending) {
    return (
      <div className={styles.center}>
        <Label
          title={translate('AudioNotYetHelpText')}
          kind={kinds.DISABLED}
        >
          {translate('NotYet')}
        </Label>
      </div>
    );
  }

  if (isAvailable) {
    return (
      <div className={styles.center}>
        <Label
          title={translate('BookAvailableButMissing')}
          kind={kinds.DANGER}
        >
          {translate('Missing')}
        </Label>
      </div>
    );
  }

  return (
    <div className={styles.center}>
      <Label
        title={translate('ComingSoon')}
        kind={kinds.INFO}
      >
        {translate('ComingSoon')}
      </Label>
    </div>
  );
}

BookStatus.propTypes = {
  isAvailable: PropTypes.bool,
  monitored: PropTypes.bool.isRequired,
  pending: PropTypes.bool,
  unreleased: PropTypes.bool,
  audioReleaseDate: PropTypes.string,
  coveredByVolume: PropTypes.number,
  grabbed: PropTypes.bool,
  queueItem: PropTypes.object,
  bookFile: PropTypes.object,
  fileCount: PropTypes.number
};

BookStatus.defaultProps = {
  pending: false,
  unreleased: false,
  audioReleaseDate: null,
  coveredByVolume: null,
  fileCount: 1
};

export default BookStatus;
