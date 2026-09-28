/* eslint max-params: 0 */
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import withScrollPosition from 'Components/withScrollPosition';
import { saveBookEditor, setBookFilter, setBookLibrary, setBookSort, setBookTableOption, setBookView } from 'Store/Actions/bookIndexActions';
import { executeCommand } from 'Store/Actions/commandActions';
import scrollPositions from 'Store/scrollPositions';
import createBookClientSideCollectionItemsSelector from 'Store/Selectors/createBookClientSideCollectionItemsSelector';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import { filtersForLibrary, LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import BookIndex from './BookIndex';
import { bookIndexLibraryRoute } from './bookIndexLibraries';

// The index shows one library at a time: the route's `library` (the tab) scopes the filtered and
// sorted list -- filters and sort are per item and keep their order, so scoping after them equals
// scoping before them -- and the tab row's counts are each library's entries before any filter. A
// volume's library is its series': createBookClientSideCollectionItemsSelector's stripped items
// keep authorId, state.authors holds the author records (Author/Index/AuthorIndexConnector, same
// shape).
function createLibraryItemsSelector() {
  return createSelector(
    createBookClientSideCollectionItemsSelector('bookIndex'),
    (state) => state.books.items,
    (state) => state.authors.items,
    (state, { library }) => library,
    (book, allBooks, authors, library) => {
      const authorsById = {};
      authors.forEach((author) => {
        authorsById[author.id] = author;
      });

      const libraryOf = (authorId) => (authorsById[authorId] || {}).library;

      const mangaCount = allBooks.filter((b) => libraryOf(b.authorId) === MANGA).length;
      const lightNovelCount = allBooks.filter((b) => libraryOf(b.authorId) === LIGHT_NOVEL).length;

      return {
        ...book,
        library,
        filters: filtersForLibrary(book.filters, library),
        items: book.items.filter((b) => libraryOf(b.authorId) === library),
        totalItems: library === LIGHT_NOVEL ? lightNovelCount : mangaCount,
        mangaCount,
        lightNovelCount
      };
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    createLibraryItemsSelector(),
    createCommandExecutingSelector(commandNames.BULK_REFRESH_AUTHOR),
    createCommandExecutingSelector(commandNames.BULK_REFRESH_BOOK),
    createCommandExecutingSelector(commandNames.RSS_SYNC),
    createCommandExecutingSelector(commandNames.CUTOFF_UNMET_BOOK_SEARCH),
    createCommandExecutingSelector(commandNames.MISSING_BOOK_SEARCH),
    createDimensionsSelector(),
    (
      book,
      isRefreshingAuthorCommand,
      isRefreshingBookCommand,
      isRssSyncExecuting,
      isCutoffBooksSearch,
      isMissingBooksSearch,
      dimensionsState
    ) => {
      const isRefreshingBook = isRefreshingBookCommand || isRefreshingAuthorCommand;
      return {
        ...book,
        isRefreshingBook,
        isRssSyncExecuting,
        isSearching: isCutoffBooksSearch || isMissingBooksSearch,
        isSmallScreen: dimensionsState.isSmallScreen
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  return {
    onTableOptionChange(payload) {
      dispatch(setBookTableOption(payload));
    },

    onSortSelect(sortKey) {
      dispatch(setBookSort({ sortKey }));
    },

    onFilterSelect(selectedFilterKey) {
      dispatch(setBookFilter({ selectedFilterKey }));
    },

    dispatchSetBookView(view) {
      dispatch(setBookView({ view }));
    },

    dispatchSetBookLibrary(library) {
      dispatch(setBookLibrary({ library }));
    },

    dispatchSaveBookEditor(payload) {
      dispatch(saveBookEditor(payload));
    },

    onRefreshBookPress(items) {
      dispatch(executeCommand({
        name: commandNames.BULK_REFRESH_BOOK,
        bookIds: items
      }));
    },

    onRssSyncPress() {
      dispatch(executeCommand({
        name: commandNames.RSS_SYNC
      }));
    },

    onSearchPress(items) {
      dispatch(executeCommand({
        name: commandNames.BOOK_SEARCH,
        bookIds: items
      }));
    }
  };
}

class BookIndexConnector extends Component {

  //
  // Lifecycle

  // The tab is the route (/books/manga, /books/lightnovels); remember it so /books reopens it --
  // the same pattern as AuthorIndexConnector/authorIndex.library.
  componentDidMount() {
    this.props.dispatchSetBookLibrary(this.props.library);
  }

  componentDidUpdate(prevProps) {
    if (prevProps.library !== this.props.library) {
      this.props.dispatchSetBookLibrary(this.props.library);
    }
  }

  //
  // Listeners

  onLibrarySelect = (library) => {
    if (library === this.props.library) {
      return;
    }

    this.props.history.push(getPathWithUrlBase(bookIndexLibraryRoute(library)));
  };

  onViewSelect = (view) => {
    this.props.dispatchSetBookView(view);
  };

  onSaveSelected = (payload) => {
    this.props.dispatchSaveBookEditor(payload);
  };

  onScroll = ({ scrollTop }) => {
    scrollPositions.bookIndex = scrollTop;
  };

  //
  // Render

  render() {
    return (
      <BookIndex
        {...this.props}
        onLibrarySelect={this.onLibrarySelect}
        onViewSelect={this.onViewSelect}
        onScroll={this.onScroll}
        onSaveSelected={this.onSaveSelected}
      />
    );
  }
}

BookIndexConnector.propTypes = {
  isSmallScreen: PropTypes.bool.isRequired,
  view: PropTypes.string.isRequired,
  library: PropTypes.string.isRequired,
  history: PropTypes.object.isRequired,
  dispatchSetBookView: PropTypes.func.isRequired,
  dispatchSetBookLibrary: PropTypes.func.isRequired,
  dispatchSaveBookEditor: PropTypes.func.isRequired
};

export default withScrollPosition(
  connect(createMapStateToProps, createMapDispatchToProps)(BookIndexConnector),
  'bookIndex'
);
