import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { clearMatchCandidates, fetchMatchCandidates, saveMatch } from 'Store/Actions/authorActions';
import { executeCommand } from 'Store/Actions/commandActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import { findCommand, isCommandExecuting } from 'Utilities/Command';
import FixMatchModalContent from './FixMatchModalContent';

function createMapStateToProps() {
  return createSelector(
    (state) => state.authors.matchCandidates,
    createAuthorSelector(),
    createCommandsSelector(),
    (matchCandidates, author, commands) => {
      const {
        isFetching,
        isPopulated,
        error,
        isSaving,
        saveError,
        items
      } = matchCandidates;

      return {
        authorName: author.authorName,
        aniListId: author.aniListId,
        isLightNovel: author.library === 'lightNovel',
        isReResolving: isCommandExecuting(findCommand(commands, { name: commandNames.RE_RESOLVE_METADATA, authorId: author.id })),
        isFetching,
        isPopulated,
        error,
        isSaving,
        saveError,
        items
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchMatchCandidates: fetchMatchCandidates,
  dispatchClearMatchCandidates: clearMatchCandidates,
  dispatchSaveMatch: saveMatch,
  dispatchExecuteCommand: executeCommand
};

class FixMatchModalContentConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    this.search(this.props.authorName);
  }

  componentDidUpdate(prevProps) {
    if (prevProps.isSaving && !this.props.isSaving && !this.props.saveError) {
      this.props.onModalClose();
    }
  }

  componentWillUnmount() {
    if (this._searchTimeout) {
      clearTimeout(this._searchTimeout);
    }

    this.props.dispatchClearMatchCandidates();
  }

  //
  // Control

  search(term) {
    this.props.dispatchFetchMatchCandidates({
      authorId: this.props.authorId,
      term
    });
  }

  //
  // Listeners

  onSearchChange = (term) => {
    if (this._searchTimeout) {
      clearTimeout(this._searchTimeout);
    }

    if (term.trim() === '') {
      return;
    }

    // 1 s, as on the add page: every partial term is a live AniList request.
    this._searchTimeout = setTimeout(() => {
      this.search(term.trim());
    }, 1000);
  };

  onSelectPress = (aniListId) => {
    this.props.dispatchSaveMatch({
      authorId: this.props.authorId,
      aniListId
    });
  };

  onUnbindPress = () => {
    this.props.dispatchSaveMatch({
      authorId: this.props.authorId,
      aniListId: null
    });
  };

  // The same command the series toolbar's Re-resolve Metadata button ran (moved here, SD-5).
  onReResolvePress = () => {
    this.props.dispatchExecuteCommand({
      name: commandNames.RE_RESOLVE_METADATA,
      authorId: this.props.authorId
    });
  };

  //
  // Render

  render() {
    return (
      <FixMatchModalContent
        {...this.props}
        onSearchChange={this.onSearchChange}
        onSelectPress={this.onSelectPress}
        onUnbindPress={this.onUnbindPress}
        onReResolvePress={this.onReResolvePress}
      />
    );
  }
}

FixMatchModalContentConnector.propTypes = {
  authorId: PropTypes.number.isRequired,
  authorName: PropTypes.string.isRequired,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  dispatchFetchMatchCandidates: PropTypes.func.isRequired,
  dispatchClearMatchCandidates: PropTypes.func.isRequired,
  dispatchSaveMatch: PropTypes.func.isRequired,
  dispatchExecuteCommand: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(FixMatchModalContentConnector);
