import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { toggleBooksMonitored } from 'Store/Actions/bookActions';
import createBookSelector from 'Store/Selectors/createBookSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import { AUDIO, mediaTypeOfQuality } from 'Utilities/Book/mediaTypes';
import queueItemFillsFiles from 'Utilities/Book/queueItemFillsFiles';
import BookDetailsHeader from './BookDetailsHeader';

const selectOverview = createSelector(
  (state) => state.editions,
  (editions) => {
    // The primary edition (BookExtensions.PrimaryEdition): the monitored non-audio edition,
    // else any monitored one. A manga volume has one edition either way.
    const primary = editions.items.find((e) => e.monitored === true && e.mediaType !== 'audio') ||
      editions.items.find((e) => e.monitored === true);
    return primary?.overview;
  }
);

// Per-edition header (2026-09-21): on a light-novel volume the header follows the selected tab.
// The Audiobook tab shows runtime instead of pages, Audible's release date (grey
// while unreleased), the audio files' size and an Audible link; the EPUB tab shows the print
// edition as before. Editions and files are the page's own (fetched per book), so a prev/next
// header in the swipe strip, whose book is not loaded, falls back to the book-level facts.
function createMapStateToProps() {
  return createSelector(
    createBookSelector(),
    selectOverview,
    createUISettingsSelector(),
    createDimensionsSelector(),
    (state) => state.authorDetails.selectedMediaType,
    (state) => state.editions.items,
    (state) => state.bookFiles.items,
    (state, { author }) => author,
    (state) => state.queue.details.items,
    (state) => state.settings.qualityProfiles.items,
    (state) => state.settings.mediaManagement.item.downloadPropersAndRepacks,
    (state) => state.editions.isPopulated && state.bookFiles.isPopulated,
    (book, overview, uiSettings, dimensions, selectedMediaType, editions, bookFiles, author, queueItems, qualityProfiles, downloadPropersAndRepacks, isFilesPopulated) => {
      const isLightNovel = author?.library === 'lightNovel';
      const edition = isLightNovel ?
        editions.find((e) => e.bookId === book.id && e.mediaType === selectedMediaType) :
        null;
      const editionFiles = edition ? bookFiles.filter((f) => f.editionId === edition.id) : [];
      const mediaTypeEntry = edition ? (book.mediaTypes || []).find((m) => m.mediaType === selectedMediaType) : null;

      // Series page round (2026-09-24): the volume's download, the selected edition's on a light
      // novel, shown by the header only when it will fill the volume -- the rule the series
      // page's rows use (queueItemFillsFiles), so a pack over an owned volume says nothing here
      // either. Judged only once the volume's editions and files are loaded, so an owned volume
      // never flashes a label.
      const profileId = isLightNovel && selectedMediaType === AUDIO ?
        (author.audioQualityProfileId || author.qualityProfileId) :
        author?.qualityProfileId;
      const profile = qualityProfiles.find((p) => p.id === profileId);
      const shownFiles = isLightNovel ? editionFiles : bookFiles.filter((f) => f.bookId === book.id);
      const queueItem = isFilesPopulated ?
        (queueItems || []).find((item) => {
          return item.book && item.book.id === book.id &&
            (!isLightNovel || mediaTypeOfQuality(item.quality?.quality?.name) === selectedMediaType) &&
            queueItemFillsFiles(item, shownFiles, profile, downloadPropersAndRepacks);
        }) :
        null;

      return {
        ...book,
        overview,
        selectedMediaType,
        selectedEdition: edition,
        selectedEditionSize: edition ? editionFiles.reduce((sum, f) => sum + (f.size || 0), 0) : null,
        selectedEditionUnreleased: !!(mediaTypeEntry && mediaTypeEntry.unreleased),
        queueItem: queueItem || null,
        shortDateFormat: uiSettings.shortDateFormat,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

const mapDispatchToProps = {
  toggleBooksMonitored
};

class BookDetailsHeaderConnector extends Component {

  //
  // Listeners

  onMonitorTogglePress = (monitored) => {
    this.props.toggleBooksMonitored({
      bookIds: [this.props.bookId],
      monitored
    });
  };

  //
  // Render

  render() {
    return (
      <BookDetailsHeader
        {...this.props}
        onMonitorTogglePress={this.onMonitorTogglePress}
      />
    );
  }
}

BookDetailsHeaderConnector.propTypes = {
  bookId: PropTypes.number,
  toggleBooksMonitored: PropTypes.func.isRequired,
  author: PropTypes.object
};

export default connect(createMapStateToProps, mapDispatchToProps)(BookDetailsHeaderConnector);
