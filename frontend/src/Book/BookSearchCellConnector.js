import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { executeCommand } from 'Store/Actions/commandActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import { isCommandExecuting } from 'Utilities/Command';
import BookSearchCell from './BookSearchCell';

function createMapStateToProps() {
  return createSelector(
    (state, { bookId }) => bookId,
    (state, { mediaType }) => mediaType,
    createAuthorSelector(),
    createCommandsSelector(),
    (bookId, mediaType, author, commands) => {
      const isSearching = commands.some((command) => {
        const bookSearch = command.name === commandNames.BOOK_SEARCH;

        if (!bookSearch || !isCommandExecuting(command)) {
          return false;
        }

        // A light-novel row spins for its own edition: an EPUB search must not spin the Audio
        // row of the same volume. An untyped search covers every class, so it spins both.
        if (mediaType && command.body.mediaType && command.body.mediaType !== mediaType) {
          return false;
        }

        return command.body.bookIds.indexOf(bookId) > -1;
      });

      return {
        authorMonitored: author.monitored,
        isSearching
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  return {
    onSearchPress(name, path) {
      // Light novels: the row searches the edition the series page is showing. Manga rows and
      // the Wanted rows pass no media type -- the every-class search it always was.
      dispatch(executeCommand({
        name: commandNames.BOOK_SEARCH,
        bookIds: [props.bookId],
        ...(props.mediaType ? { mediaType: props.mediaType } : {})
      }));
    }
  };
}

export default connect(createMapStateToProps, createMapDispatchToProps)(BookSearchCell);
