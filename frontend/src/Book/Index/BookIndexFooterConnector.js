import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import createDeepEqualSelector from 'Store/Selectors/createDeepEqualSelector';
import BookIndexFooter from './BookIndexFooter';

// Scoped to the current tab (bookIndex.library, set by BookIndexConnector from the route) the
// same way the item list itself is, so the statistics reflect only what the tab shows -- this
// selector is independent of BookIndexConnector's, so the scoping has to happen here too.
function createUnoptimizedSelector() {
  return createSelector(
    createClientSideCollectionSelector('books', 'bookIndex'),
    (state) => state.authors.items,
    (state) => state.bookIndex.library,
    (books, authors, library) => {
      const authorsById = {};
      authors.forEach((author) => {
        authorsById[author.id] = author;
      });

      return books.items
        .filter((s) => (authorsById[s.authorId] || {}).library === library)
        .map((s) => {
          const {
            authorId,
            monitored,
            status,
            statistics
          } = s;

          return {
            authorId,
            monitored,
            status,
            statistics
          };
        });
    }
  );
}

function createBookSelector() {
  return createDeepEqualSelector(
    createUnoptimizedSelector(),
    (book) => book
  );
}

function createMapStateToProps() {
  return createSelector(
    createBookSelector(),
    (book) => {
      return {
        book
      };
    }
  );
}

export default connect(createMapStateToProps)(BookIndexFooter);
