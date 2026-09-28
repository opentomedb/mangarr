import PropTypes from 'prop-types';
import React from 'react';
import ProgressBar from 'Components/ProgressBar';
import { kinds, sizes } from 'Helpers/Props';
import getProgressBarKind from 'Utilities/Author/getProgressBarKind';
import translate from 'Utilities/String/translate';
import styles from './AuthorIndexProgressBar.css';

// Below this width the label (56 px), count (44 px), gaps and padding leave the bar no room on one
// line (a 390 px phone's posters are 68-120 px wide), so the bar goes on its own line under the
// label and count. The index grids budget the taller rows with the same test.
export const LIGHT_NOVEL_STACKED_BELOW = 150;

export function isLightNovelStacked(width) {
  return width < LIGHT_NOVEL_STACKED_BELOW;
}

// One line of a light-novel entry (poster, overview and table): format label, thin bar, "x / y"
// (or Not Yet while the series has no audio at all — grey, no bar, contract "Pending audio").
function FormatRow(props) {
  const {
    label,
    monitored,
    status,
    fileCount,
    bookCount,
    notYet,
    width,
    compact
  } = props;

  const progress = bookCount ? (fileCount / bookCount) * 100 : 100;
  const stacked = !compact && isLightNovelStacked(width);

  const bar = (
    <ProgressBar
      className={styles.progressBar}
      containerClassName={stacked ? styles.rowProgressStacked : styles.rowProgress}
      progress={notYet ? 0 : progress}
      kind={notYet ? kinds.WARNING : getProgressBarKind(status, monitored, progress)}
      size={sizes.SMALL}
      showText={false}
      title={notYet ? translate('AudioNotYetHelpText') : `${fileCount} / ${bookCount}`}
    />
  );

  const count = (
    <span className={notYet ? styles.rowCountNotYet : styles.rowCount}>
      {notYet ? translate('NotYet') : `${fileCount} / ${bookCount}`}
    </span>
  );

  if (stacked) {
    return (
      <div
        className={styles.rowStacked}
        style={{ width: `${width}px` }}
      >
        <span className={styles.rowLabelStacked}>
          {label}
        </span>

        {count}

        {bar}
      </div>
    );
  }

  return (
    <div
      className={compact ? styles.rowCompact : styles.row}
      style={{ width: `${width}px` }}
    >
      <span className={styles.rowLabel}>
        {label}
      </span>

      {bar}

      {count}
    </div>
  );
}

FormatRow.propTypes = {
  label: PropTypes.string.isRequired,
  monitored: PropTypes.bool.isRequired,
  status: PropTypes.string.isRequired,
  fileCount: PropTypes.number.isRequired,
  bookCount: PropTypes.number.isRequired,
  notYet: PropTypes.bool.isRequired,
  width: PropTypes.number.isRequired,
  compact: PropTypes.bool.isRequired
};

function AuthorIndexProgressBar(props) {
  const {
    library,
    monitored,
    status,
    bookCount,
    availableBookCount,
    bookFileCount,
    totalBookCount,
    audioBookCount,
    audioBookFileCount,
    audioAvailable,
    posterWidth,
    detailedProgressBar,
    compact
  } = props;

  // Light novel: Ebook and Audiobook are tracked per edition, so the entry shows one row each.
  // bookFileCount / bookCount = volumes whose ebook edition has a file; audioBookFileCount /
  // audioBookCount likewise for the audiobook edition (contract "Statistics and wanted").
  if (library === 'lightNovel') {
    return (
      <div className={styles.rows}>
        <FormatRow
          label={translate('Ebook')}
          monitored={monitored}
          status={status}
          fileCount={bookFileCount}
          bookCount={bookCount}
          notYet={false}
          width={posterWidth}
          compact={compact}
        />

        <FormatRow
          label={translate('Audiobook')}
          monitored={monitored}
          status={status}
          fileCount={audioBookFileCount}
          bookCount={audioBookCount}
          notYet={!audioAvailable && audioBookFileCount === 0}
          width={posterWidth}
          compact={compact}
        />
      </div>
    );
  }

  const progress = bookCount ? (availableBookCount / bookCount) * 100 : 100;
  const text = `${availableBookCount} / ${bookCount}`;

  return (
    <ProgressBar
      className={styles.progressBar}
      containerClassName={styles.progress}
      progress={progress}
      kind={getProgressBarKind(status, monitored, progress)}
      size={detailedProgressBar ? sizes.MEDIUM : sizes.SMALL}
      showText={detailedProgressBar}
      text={text}
      title={translate('AuthorProgressBarText', { bookCount, availableBookCount, bookFileCount, totalBookCount })}
      width={posterWidth}
    />
  );
}

AuthorIndexProgressBar.propTypes = {
  library: PropTypes.string,
  monitored: PropTypes.bool.isRequired,
  status: PropTypes.string.isRequired,
  bookCount: PropTypes.number.isRequired,
  availableBookCount: PropTypes.number.isRequired,
  bookFileCount: PropTypes.number.isRequired,
  totalBookCount: PropTypes.number.isRequired,
  audioBookCount: PropTypes.number,
  audioBookFileCount: PropTypes.number,
  audioAvailable: PropTypes.bool,
  posterWidth: PropTypes.number.isRequired,
  detailedProgressBar: PropTypes.bool.isRequired,
  compact: PropTypes.bool
};

AuthorIndexProgressBar.defaultProps = {
  library: 'manga',
  compact: false,
  audioBookCount: 0,
  audioBookFileCount: 0,
  audioAvailable: false
};

export default AuthorIndexProgressBar;
