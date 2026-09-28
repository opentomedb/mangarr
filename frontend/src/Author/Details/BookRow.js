import PropTypes from 'prop-types';
import React, { Component } from 'react';
import BookSearchCellConnector from 'Book/BookSearchCellConnector';
import BookTitleLink from 'Book/BookTitleLink';
import IndexerFlags from 'Book/IndexerFlags';
import Icon from 'Components/Icon';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import StarRating from 'Components/StarRating';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import Popover from 'Components/Tooltip/Popover';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import formatRuntime from 'Utilities/Book/formatRuntime';
import isVolumeReleased from 'Utilities/Book/isVolumeReleased';
import { AUDIO } from 'Utilities/Book/mediaTypes';
import volumeRowTitle from 'Utilities/Book/volumeRowTitle';
import formatPrecisionDate from 'Utilities/Date/formatPrecisionDate';
import translate from 'Utilities/String/translate';
import BookStatus from './BookStatus';
import styles from './BookRow.css';

class BookRow extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isDetailsModalOpen: false,
      isEditBookModalOpen: false
    };
  }

  //
  // Listeners

  onManualSearchPress = () => {
    this.setState({ isDetailsModalOpen: true });
  };

  onDetailsModalClose = () => {
    this.setState({ isDetailsModalOpen: false });
  };

  onEditBookPress = () => {
    this.setState({ isEditBookModalOpen: true });
  };

  onEditBookModalClose = () => {
    this.setState({ isEditBookModalOpen: false });
  };

  onMonitorBookPress = (monitored, options) => {
    this.props.onMonitorBookPress(this.props.id, monitored, options);
  };

  // Light novels: the toggle in the monitored column is the SELECTED edition's, not the book's
  // (contract "Monitoring rule"). No shift-range for editions.
  onMonitorEditionPress = (monitored) => {
    const {
      id,
      editions,
      selectedMediaType
    } = this.props;

    const edition = (editions || []).find((e) => e.mediaType === selectedMediaType);

    if (edition) {
      this.props.onMonitorEditionPress(id, edition.id, selectedMediaType, monitored);
    }
  };

  //
  // Render

  render() {
    const {
      id,
      authorId,
      monitored,
      releaseDate,
      releaseDatePrecision,
      title,
      displayTitle,
      displaySubtitle,
      volumeNumber,
      seriesTitle,
      authorName,
      position,
      pageCount,
      ratings,
      isSaving,
      authorMonitored,
      titleSlug,
      bookFiles,
      indexerFlags,
      grabbed,
      queueItem,
      isEditorActive,
      isSelected,
      onSelectedChange,
      columns,
      authorLibrary,
      selectedMediaType,
      editions,
      mediaTypes
    } = this.props;

    const bookFile = bookFiles[0];

    // Light novel: status and monitoring are the selected edition's (book.mediaTypes carries the
    // per-edition monitored / hasFile / pending flags); a manga row keeps the book's values.
    const isLightNovel = authorLibrary === 'lightNovel';
    const mediaTypeEntry = isLightNovel ? (mediaTypes || []).find((m) => m.mediaType === selectedMediaType) : null;
    const selectedEdition = isLightNovel ? (editions || []).find((e) => e.mediaType === selectedMediaType) : null;
    const hasEdition = !isLightNovel || !!selectedEdition;
    const rowMonitored = mediaTypeEntry ? mediaTypeEntry.monitored : monitored;
    const pending = !!(mediaTypeEntry && mediaTypeEntry.pending);
    const unreleased = !!(mediaTypeEntry && mediaTypeEntry.unreleased);
    const audioReleaseDate = mediaTypeEntry ? mediaTypeEntry.audioReleaseDate : null;

    // The Audio tab is about the audiobook: its date is Audible's, and it has a runtime, not
    // pages. The EPUB tab (and every manga row) keeps the print edition's date and page count.
    const audioTab = isLightNovel && selectedMediaType === AUDIO;
    const shownDate = audioTab ? audioReleaseDate : releaseDate;
    const shownLength = audioTab ? formatRuntime(selectedEdition ? selectedEdition.runtimeMinutes : null) : pageCount;
    const coveredByVolume = mediaTypeEntry && mediaTypeEntry.coveredByVolume != null ? mediaTypeEntry.coveredByVolume : null;

    // Released (isVolumeReleased): a past or unknown release date; a future one greys the row.
    const isAvailable = isVolumeReleased(releaseDate);

    return (
      <TableRow className={isAvailable ? undefined : styles.unreleased}>
        {
          columns.map((column) => {
            const {
              name,
              isVisible
            } = column;

            if (!isVisible) {
              return null;
            }

            if (isEditorActive && name === 'select') {
              return (
                <TableSelectCell
                  key={name}
                  id={id}
                  isSelected={isSelected}
                  isDisabled={false}
                  onSelectedChange={onSelectedChange}
                />
              );
            }

            if (name === 'monitored') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.monitored}
                >
                  <MonitorToggleButton
                    monitored={rowMonitored}
                    isDisabled={!authorMonitored || !hasEdition}
                    isSaving={isSaving}
                    onPress={isLightNovel ? this.onMonitorEditionPress : this.onMonitorBookPress}
                  />
                </TableRowCell>
              );
            }

            if (name === 'title') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.title}
                >
                  <BookTitleLink
                    titleSlug={titleSlug}
                    title={volumeRowTitle({ title, displayTitle, displaySubtitle, volumeNumber }, authorName)}
                  />
                </TableRowCell>
              );
            }

            if (name === 'series') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.title}
                >
                  {seriesTitle || ''}
                </TableRowCell>
              );
            }

            if (name === 'position') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.position}
                >
                  {position || ''}
                </TableRowCell>
              );
            }

            if (name === 'rating') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.rating}
                >
                  {
                    <StarRating
                      rating={ratings.value}
                      votes={ratings.votes}
                    />
                  }
                </TableRowCell>
              );
            }

            if (name === 'releaseDate') {
              // Manga metadata often has no per-volume English release date; show "TBA" instead
              // of a blank cell when the date is unknown (an unlisted audiobook reads the same).
              if (!shownDate) {
                return (
                  <TableRowCell
                    key={name}
                    className={styles.releaseDate}
                  >
                    {translate('TBA')}
                  </TableRowCell>
                );
              }

              // Preferred Edition (2026-09-24, D6): an edition's year/month date reads "2019" / "Nov 2026".
              if (!audioTab && releaseDatePrecision) {
                return (
                  <TableRowCell
                    key={name}
                    className={styles.releaseDate}
                  >
                    {formatPrecisionDate(shownDate, releaseDatePrecision)}
                  </TableRowCell>
                );
              }

              return (
                <RelativeDateCellConnector
                  className={styles.releaseDate}
                  key={name}
                  date={shownDate.slice(0, 10)}
                />
              );
            }

            if (name === 'pageCount') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.pageCount}
                >
                  {shownLength || ''}
                </TableRowCell>
              );
            }

            if (name === 'indexerFlags') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.indexerFlags}
                >
                  {indexerFlags ? (
                    <Popover
                      anchor={<Icon name={icons.FLAG} kind={kinds.PRIMARY} />}
                      title={translate('IndexerFlags')}
                      body={<IndexerFlags indexerFlags={indexerFlags} />}
                      position={tooltipPositions.LEFT}
                    />
                  ) : null}
                </TableRowCell>
              );
            }

            if (name === 'status') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.status}
                >
                  <BookStatus
                    isAvailable={isAvailable}
                    monitored={isLightNovel ? (monitored && rowMonitored) : monitored}
                    pending={pending}
                    unreleased={unreleased}
                    audioReleaseDate={audioReleaseDate}
                    coveredByVolume={coveredByVolume}
                    grabbed={grabbed}
                    queueItem={queueItem}
                    bookFile={bookFile}
                    fileCount={isLightNovel ? bookFiles.length : 1}
                  />
                </TableRowCell>
              );
            }

            if (name === 'actions') {
              return (
                <BookSearchCellConnector
                  key={name}
                  bookId={id}
                  authorId={authorId}
                  bookTitle={title}
                  authorName={authorName}
                  mediaType={isLightNovel ? selectedMediaType : undefined}
                />
              );
            }
            return null;
          })
        }
      </TableRow>
    );
  }
}

BookRow.propTypes = {
  id: PropTypes.number.isRequired,
  authorId: PropTypes.number.isRequired,
  monitored: PropTypes.bool.isRequired,
  releaseDate: PropTypes.string,
  releaseDatePrecision: PropTypes.string,
  title: PropTypes.string.isRequired,
  displayTitle: PropTypes.string,
  displaySubtitle: PropTypes.string,
  volumeNumber: PropTypes.number,
  seriesTitle: PropTypes.string.isRequired,
  authorName: PropTypes.string.isRequired,
  position: PropTypes.string,
  pageCount: PropTypes.number,
  ratings: PropTypes.object.isRequired,
  indexerFlags: PropTypes.number.isRequired,
  titleSlug: PropTypes.string.isRequired,
  isSaving: PropTypes.bool,
  authorMonitored: PropTypes.bool.isRequired,
  bookFiles: PropTypes.arrayOf(PropTypes.object).isRequired,
  grabbed: PropTypes.bool,
  queueItem: PropTypes.object,
  isEditorActive: PropTypes.bool.isRequired,
  isSelected: PropTypes.bool,
  onSelectedChange: PropTypes.func.isRequired,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  onMonitorBookPress: PropTypes.func.isRequired,
  authorLibrary: PropTypes.string,
  selectedMediaType: PropTypes.string,
  editions: PropTypes.arrayOf(PropTypes.object),
  mediaTypes: PropTypes.arrayOf(PropTypes.object),
  onMonitorEditionPress: PropTypes.func.isRequired
};

BookRow.defaultProps = {
  indexerFlags: 0
};

export default BookRow;
