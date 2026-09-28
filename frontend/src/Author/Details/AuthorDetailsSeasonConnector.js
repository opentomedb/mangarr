/* eslint max-params: 0 */
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { setAuthorDetailsId, setAuthorDetailsMediaType, setAuthorDetailsSort } from 'Store/Actions/authorDetailsActions';
import { setBooksTableOption, toggleBooksMonitored } from 'Store/Actions/bookActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { toggleEditionMonitored } from 'Store/Actions/editionActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import isVolumeReleased from 'Utilities/Book/isVolumeReleased';
import { mediaTypeOfQuality } from 'Utilities/Book/mediaTypes';
import AuthorDetailsSeason from './AuthorDetailsSeason';

function createMapStateToProps() {
  return createSelector(
    createClientSideCollectionSelector('books', 'authorDetails'),
    createAuthorSelector(),
    createDimensionsSelector(),
    createUISettingsSelector(),
    (state) => state.authorDetails.selectedMediaType,
    (state) => state.queue.details.items,
    (state) => state.bookFiles.items,
    (books, author, dimensions, uiSettings, selectedMediaType, queueItems, bookFiles) => {
      // Series page round (2026-09-24): which edition tabs of a light novel have a download
      // under way (the page's queue is this series').
      const downloadingMediaTypes = (queueItems || []).map((item) => mediaTypeOfQuality(item.quality?.quality?.name));

      // Sonarr's season-header count (series page round, 2026-09-24), for the manga table: volumes
      // with a file / volumes that should have one -- a file, or monitored and released as the rows
      // show it (isVolumeReleased; greyed future-dated rows and Coming Soon placeholders are not).
      const volumesWithFiles = new Set(bookFiles.map((file) => file.bookId));
      const volumeCounts = books.items.reduce((acc, book) => {
        const hasFile = volumesWithFiles.has(book.id);

        if (hasFile || (book.monitored && isVolumeReleased(book.releaseDate))) {
          acc.released += 1;
          acc.files += hasFile ? 1 : 0;
        }

        return acc;
      }, { files: 0, released: 0 });

      return {
        // books.items is already filtered AND sorted by createClientSideCollectionSelector
        // (sortCollection applies sortKey + sortDirection + sortPredicates). Re-sorting here with
        // a raw _.orderBy(items, sortKey) bypassed the predicates and broke title (string-sorted
        // "Vol. 1/10/2"), rating (item.ratings.value) and status (computed) sorting.
        items: books.items,
        columns: books.columns,
        sortKey: books.sortKey,
        sortDirection: books.sortDirection,
        authorMonitored: author.monitored,
        totalVolumes: author.totalVolumes,
        library: author.library,
        audioAvailable: author.audioAvailable,
        statistics: author.statistics || {},
        selectedMediaType,
        downloadingMediaTypes,
        volumeCounts,
        isSmallScreen: dimensions.isSmallScreen,
        uiSettings
      };
    }
  );
}

const mapDispatchToProps = {
  setAuthorDetailsId,
  setAuthorDetailsSort,
  setAuthorDetailsMediaType,
  toggleBooksMonitored,
  toggleEditionMonitored,
  setBooksTableOption,
  executeCommand
};

class AuthorDetailsSeasonConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    this.props.setAuthorDetailsId({ authorId: this.props.authorId });
  }

  //
  // Listeners

  onTableOptionChange = (payload) => {
    this.props.setBooksTableOption(payload);
  };

  onSortPress = (sortKey) => {
    this.props.setAuthorDetailsSort({ sortKey });
  };

  onMonitorBookPress = (bookIds, monitored) => {
    this.props.toggleBooksMonitored({
      bookIds,
      monitored
    });
  };

  onMediaTypeSelect = (mediaType) => {
    this.props.setAuthorDetailsMediaType({ mediaType });
  };

  onMonitorEditionPress = (bookId, editionId, mediaType, monitored) => {
    this.props.toggleEditionMonitored({
      bookId,
      editionId,
      mediaType,
      monitored
    });
  };

  //
  // Render

  render() {
    return (
      <AuthorDetailsSeason
        {...this.props}
        onSortPress={this.onSortPress}
        onTableOptionChange={this.onTableOptionChange}
        onMonitorBookPress={this.onMonitorBookPress}
        onMediaTypeSelect={this.onMediaTypeSelect}
        onMonitorEditionPress={this.onMonitorEditionPress}
      />
    );
  }
}

AuthorDetailsSeasonConnector.propTypes = {
  authorId: PropTypes.number.isRequired,
  toggleBooksMonitored: PropTypes.func.isRequired,
  setBooksTableOption: PropTypes.func.isRequired,
  setAuthorDetailsId: PropTypes.func.isRequired,
  setAuthorDetailsSort: PropTypes.func.isRequired,
  setAuthorDetailsMediaType: PropTypes.func.isRequired,
  toggleEditionMonitored: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AuthorDetailsSeasonConnector);
