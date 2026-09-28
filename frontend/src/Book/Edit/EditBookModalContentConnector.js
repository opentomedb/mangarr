import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { saveBook, setBookValue } from 'Store/Actions/bookActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { saveEditions } from 'Store/Actions/editionActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createBookSelector from 'Store/Selectors/createBookSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import selectSettings from 'Store/Selectors/selectSettings';
import { findCommand, isCommandExecuting } from 'Utilities/Command';
import EditBookModalContent from './EditBookModalContent';

function createMapStateToProps() {
  return createSelector(
    (state) => state.books,
    (state) => state.editions,
    createBookSelector(),
    createAuthorSelector(),
    createCommandsSelector(),
    (bookState, editionState, book, author, commands) => {
      const {
        isSaving,
        saveError,
        pendingChanges
      } = bookState;

      const {
        isFetching,
        isPopulated,
        error
      } = editionState;

      const bookSettings = _.pick(book, [
        'monitored',
        'anyEditionOk'
      ]);
      bookSettings.editions = editionState.items;

      const settings = selectSettings(bookSettings, pendingChanges, saveError);

      return {
        title: book.title,
        // The modal names the volume by its display title (a light novel's "<series>: <subtitle>
        // (Vol. N)"; a manga volume's own "<series> Vol. N"), never the series twice.
        displayTitle: book.displayTitle || book.title,
        authorId: book.authorId,
        volumeNumber: book.volumeNumber,
        // From the series, not the editions: those load after the modal opens.
        isLightNovelSeries: author.library === 'lightNovel',
        isReResolving: isCommandExecuting(findCommand(commands, { name: commandNames.RE_RESOLVE_METADATA, bookId: book.id })),
        bookType: book.bookType,
        statistics: book.statistics,
        isFetching,
        isPopulated,
        error,
        isSaving,
        saveError,
        item: settings.settings,
        ...settings
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchSetBookValue: setBookValue,
  dispatchSaveBook: saveBook,
  dispatchSaveEditions: saveEditions,
  dispatchExecuteCommand: executeCommand
};

class EditBookModalContentConnector extends Component {

  //
  // Lifecycle

  componentDidUpdate(prevProps, prevState) {
    if (prevProps.isSaving && !this.props.isSaving && !this.props.saveError) {
      this.props.onModalClose();
    }
  }

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.props.dispatchSetBookValue({ name, value });
  };

  onSavePress = () => {
    this.props.dispatchSaveBook({
      id: this.props.bookId
    });
    this.props.dispatchSaveEditions({
      id: this.props.bookId
    });
  };

  // The same command the volume toolbar's Re-resolve Metadata button ran (moved here, SD-5).
  onReResolvePress = () => {
    this.props.dispatchExecuteCommand({
      name: commandNames.RE_RESOLVE_METADATA,
      authorId: this.props.authorId,
      bookId: this.props.bookId
    });
  };

  //
  // Render

  render() {
    return (
      <EditBookModalContent
        {...this.props}
        onInputChange={this.onInputChange}
        onSavePress={this.onSavePress}
        onReResolvePress={this.onReResolvePress}
      />
    );
  }
}

EditBookModalContentConnector.propTypes = {
  bookId: PropTypes.number,
  authorId: PropTypes.number.isRequired,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  dispatchSetBookValue: PropTypes.func.isRequired,
  dispatchSaveBook: PropTypes.func.isRequired,
  dispatchSaveEditions: PropTypes.func.isRequired,
  dispatchExecuteCommand: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(EditBookModalContentConnector);
