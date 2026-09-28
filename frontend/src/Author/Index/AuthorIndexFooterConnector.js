import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import createDeepEqualSelector from 'Store/Selectors/createDeepEqualSelector';
import AuthorIndexFooter from './AuthorIndexFooter';

function createUnoptimizedSelector() {
  return createSelector(
    createClientSideCollectionSelector('authors', 'authorIndex'),
    (state, { library }) => library,
    (authors, library) => {
      // The index shows one library at a time (the tab); the footer counts the same entries.
      return authors.items.filter((s) => s.library === library).map((s) => {
        const {
          monitored,
          status,
          statistics
        } = s;

        return {
          monitored,
          status,
          statistics
        };
      });
    }
  );
}

function createAuthorSelector() {
  return createDeepEqualSelector(
    createUnoptimizedSelector(),
    (author) => author
  );
}

function createMapStateToProps() {
  return createSelector(
    createAuthorSelector(),
    (author) => {
      return {
        author
      };
    }
  );
}

export default connect(createMapStateToProps)(AuthorIndexFooter);
