import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import withCurrentPage from 'Components/withCurrentPage';
import { executeCommand } from 'Store/Actions/commandActions';
import { clearQueueDetails, fetchQueueDetails } from 'Store/Actions/queueActions';
import * as wantedActions from 'Store/Actions/wantedActions';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import { filtersForLibrary } from 'Utilities/Author/libraries';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import hasDifferentItems from 'Utilities/Object/hasDifferentItems';
import selectUniqueIds from 'Utilities/Object/selectUniqueIds';
import { registerPagePopulator, unregisterPagePopulator } from 'Utilities/pagePopulator';
import { wantedLibraryRoute } from '../wantedLibraries';
import Missing from './Missing';

function createMapStateToProps() {
  return createSelector(
    (state) => state.wanted.missing,
    (state) => state.authors,
    createCommandExecutingSelector(commandNames.MISSING_BOOK_SEARCH),
    (missing, authors, isSearchingForMissingBooks) => {

      return {
        isAuthorFetching: authors.isFetching,
        isAuthorPopulated: authors.isPopulated,
        isSearchingForMissingBooks,
        isSaving: missing.items.filter((m) => m.isSaving).length > 1,
        ...missing
      };
    }
  );
}

const mapDispatchToProps = {
  ...wantedActions,
  executeCommand,
  fetchQueueDetails,
  clearQueueDetails
};

class MissingConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    const {
      useCurrentPage,
      fetchMissing,
      gotoMissingFirstPage
    } = this.props;

    registerPagePopulator(this.repopulate, ['bookFileUpdated', 'bookFileDeleted']);

    // The tab is the route; the store's library scopes every fetch, so set it first.
    this.props.setWantedLibrary({ library: this.props.library });

    if (useCurrentPage) {
      fetchMissing();
    } else {
      gotoMissingFirstPage();
    }
  }

  componentDidUpdate(prevProps) {
    if (prevProps.library !== this.props.library) {
      this.props.setWantedLibrary({ library: this.props.library });
      this.props.gotoMissingFirstPage();
    }

    if (hasDifferentItems(prevProps.items, this.props.items)) {
      const bookIds = selectUniqueIds(this.props.items, 'id');
      this.props.fetchQueueDetails({ bookIds });
    }
  }

  componentWillUnmount() {
    unregisterPagePopulator(this.repopulate);
    this.props.clearMissing();
    this.props.clearQueueDetails();
  }

  //
  // Control

  repopulate = () => {
    this.props.fetchMissing();
  };

  //
  // Listeners

  onLibrarySelect = (library) => {
    if (library !== this.props.library) {
      this.props.history.push(getPathWithUrlBase(wantedLibraryRoute('missing', library)));
    }
  };

  onFirstPagePress = () => {
    this.props.gotoMissingFirstPage();
  };

  onPreviousPagePress = () => {
    this.props.gotoMissingPreviousPage();
  };

  onNextPagePress = () => {
    this.props.gotoMissingNextPage();
  };

  onLastPagePress = () => {
    this.props.gotoMissingLastPage();
  };

  onPageSelect = (page) => {
    this.props.gotoMissingPage({ page });
  };

  onSortPress = (sortKey) => {
    this.props.setMissingSort({ sortKey });
  };

  onFilterSelect = (selectedFilterKey) => {
    this.props.setMissingFilter({ selectedFilterKey });
  };

  onTableOptionChange = (payload) => {
    this.props.setMissingTableOption(payload);

    if (payload.pageSize) {
      this.props.gotoMissingFirstPage();
    }
  };

  onSearchSelectedPress = (selected) => {
    // A page filtered to one light-novel edition searches that edition only: the EPUB filter
    // must not also fetch audiobooks. Every other filter sends no media type, which is the
    // every-class search it always was.
    const mediaType = wantedActions.FILTER_MEDIA_TYPES[this.props.selectedFilterKey];

    this.props.executeCommand({
      name: commandNames.BOOK_SEARCH,
      bookIds: selected,
      ...(mediaType ? { mediaType } : {}),
      commandFinished: this.repopulate
    });
  };

  onSearchAllMissingPress = () => {
    const mediaType = wantedActions.FILTER_MEDIA_TYPES[this.props.selectedFilterKey];

    // Search All hunts the library the page lists: the tab's library, never both.
    const library = this.props.library;

    // The Both preset hunts only the volumes whose EPUB and audiobook are both wanted -- the
    // volumes the page lists. Its library alone would sweep every wanted light-novel edition.
    const bothEditions = wantedActions.FILTER_BOTH_EDITIONS[this.props.selectedFilterKey];

    this.props.executeCommand({
      name: commandNames.MISSING_BOOK_SEARCH,
      ...(mediaType ? { mediaType } : {}),
      library,
      ...(bothEditions ? { bothEditions } : {}),
      commandFinished: this.repopulate
    });
  };

  //
  // Render

  render() {
    return (
      <Missing
        onFirstPagePress={this.onFirstPagePress}
        onPreviousPagePress={this.onPreviousPagePress}
        onNextPagePress={this.onNextPagePress}
        onLastPagePress={this.onLastPagePress}
        onPageSelect={this.onPageSelect}
        onSortPress={this.onSortPress}
        onFilterSelect={this.onFilterSelect}
        onTableOptionChange={this.onTableOptionChange}
        onSearchSelectedPress={this.onSearchSelectedPress}
        onToggleSelectedPress={this.onToggleSelectedPress}
        onSearchAllMissingPress={this.onSearchAllMissingPress}
        onLibrarySelect={this.onLibrarySelect}
        {...this.props}
        filters={filtersForLibrary(this.props.filters, this.props.library)}
      />
    );
  }
}

MissingConnector.propTypes = {
  useCurrentPage: PropTypes.bool.isRequired,
  library: PropTypes.string.isRequired,
  history: PropTypes.object.isRequired,
  filters: PropTypes.arrayOf(PropTypes.object).isRequired,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  selectedFilterKey: PropTypes.oneOfType([PropTypes.string, PropTypes.number]).isRequired,
  fetchMissing: PropTypes.func.isRequired,
  gotoMissingFirstPage: PropTypes.func.isRequired,
  gotoMissingPreviousPage: PropTypes.func.isRequired,
  gotoMissingNextPage: PropTypes.func.isRequired,
  gotoMissingLastPage: PropTypes.func.isRequired,
  gotoMissingPage: PropTypes.func.isRequired,
  setMissingSort: PropTypes.func.isRequired,
  setMissingFilter: PropTypes.func.isRequired,
  setMissingTableOption: PropTypes.func.isRequired,
  clearMissing: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired,
  fetchQueueDetails: PropTypes.func.isRequired,
  clearQueueDetails: PropTypes.func.isRequired,
  setWantedLibrary: PropTypes.func.isRequired
};

export default withCurrentPage(
  connect(createMapStateToProps, mapDispatchToProps)(MissingConnector)
);
