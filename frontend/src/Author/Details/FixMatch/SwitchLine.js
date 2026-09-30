import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import Alert from 'Components/Alert';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds, sizes } from 'Helpers/Props';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import { isCommandExecuting } from 'Utilities/Command';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import editionName from 'Utilities/String/editionName';
import translate from 'Utilities/String/translate';
import styles from './SwitchLine.css';

// Line safety (2026-09-28): Fix Match's Switch Line. Lists the other lines of the series' work in its market
// (GET edition/author/{id}/lines), confirms what a switch moves, runs SwitchLine, and -- for a light novel whose
// switched files live in calibre or Audiobookshelf -- asks once, after the refresh the switch queued has
// finished, whether to update calibre and Audiobookshelf too (POST .../lines/sync). No answer, no update.
function createMapStateToProps() {
  return createSelector(
    createCommandsSelector(),
    (commands) => ({ commands })
  );
}

function findById(commands, id) {
  return id ? commands.find((command) => command.id === id) : null;
}

class SwitchLine extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      choices: null,
      isFetching: true,
      confirmOption: null,
      isStarting: false,
      commandId: null,
      isSyncDismissed: false,
      isSyncing: false,
      syncError: null
    };
  }

  componentDidMount() {
    this.fetchLines();
  }

  componentDidUpdate(prevProps) {
    const previous = findById(prevProps.commands, this.state.commandId);
    const command = findById(this.props.commands, this.state.commandId);
    const refreshId = command && command.body.refreshCommandId;
    const previousRefresh = findById(prevProps.commands, refreshId);
    const refresh = findById(this.props.commands, refreshId);

    // The switch has written the new binding (the list's current line is the new one now), and its
    // refresh has finished (the "refresh is running" blocks are gone).
    if ((previous && isCommandExecuting(previous) && command && !isCommandExecuting(command)) ||
        (previousRefresh && isCommandExecuting(previousRefresh) && refresh && !isCommandExecuting(refresh))) {
      this.fetchLines();
    }
  }

  componentWillUnmount() {
    if (this._abortLinesRequest) {
      this._abortLinesRequest();
    }
  }

  //
  // Control

  fetchLines() {
    const { request, abortRequest } = createAjaxRequest({ url: `/edition/author/${this.props.authorId}/lines` });

    this._abortLinesRequest = abortRequest;

    request.done((choices) => {
      this.setState({ choices, isFetching: false });
    });

    request.fail(() => {
      this.setState({ choices: null, isFetching: false });
    });
  }

  //
  // Listeners

  onSwitchPress = (option) => {
    this.setState({ confirmOption: option });
  };

  onConfirmCancel = () => {
    this.setState({ confirmOption: null });
  };

  onConfirmSwitch = () => {
    const { confirmOption } = this.state;

    this.setState({ isStarting: true });

    const { request } = createAjaxRequest({
      url: '/command',
      method: 'POST',
      contentType: 'application/json',
      dataType: 'json',
      data: JSON.stringify({
        name: commandNames.SWITCH_LINE,
        authorId: this.props.authorId,
        tomeLineId: confirmOption.tomeLineId
      })
    });

    request.done((command) => {
      this.setState({ commandId: command.id, confirmOption: null, isStarting: false, isSyncDismissed: false, syncError: null });
    });

    request.fail(() => {
      this.setState({ confirmOption: null, isStarting: false });
    });
  };

  onSyncNo = () => {
    this.setState({ isSyncDismissed: true });
  };

  onSyncYes = () => {
    const command = findById(this.props.commands, this.state.commandId);

    this.setState({ isSyncing: true });

    const { request } = createAjaxRequest({
      url: `/edition/author/${this.props.authorId}/lines/sync`,
      method: 'POST',
      contentType: 'application/json',
      dataType: 'json',
      data: JSON.stringify({ bookFileIds: command.body.movedBookFileIds })
    });

    request.done(() => {
      this.setState({ isSyncing: false, isSyncDismissed: true });
    });

    request.fail((xhr) => {
      const message = xhr.responseJSON && xhr.responseJSON.message;
      this.setState({ isSyncing: false, isSyncDismissed: true, syncError: message || translate('SwitchLineExternalFailed') });
    });
  };

  //
  // Render

  renderConfirmMessage(option) {
    const { authorName } = this.props;

    return (
      <div>
        <p>{translate('SwitchLineConfirmRename', { name: authorName, newName: option.name })}</p>
        <p>{translate('SwitchLineConfirmFiles', { count: option.filesMoving })}</p>
        {
          option.keptVolumes.length ?
            <p>{translate('SwitchLineConfirmKept', { volumes: option.keptVolumes.join(', ') })}</p> :
            null
        }
        {
          option.volumesAdded ?
            <p>{translate('SwitchLineConfirmAdded', { count: option.volumesAdded })}</p> :
            null
        }
        {
          option.noAniListMatch ?
            <p className={styles.warning}>{translate('SwitchLineConfirmNoAniList')}</p> :
            null
        }
        <p>{translate('SwitchLineConfirmDisk')}</p>
      </div>
    );
  }

  render() {
    const { commands } = this.props;

    const {
      choices,
      isFetching,
      confirmOption,
      isStarting,
      commandId,
      isSyncDismissed,
      isSyncing,
      syncError
    } = this.state;

    const command = findById(commands, commandId);
    const body = command ? command.body : null;
    const isSwitching = !!command && isCommandExecuting(command);
    const isDone = !!command && command.status === 'completed';
    const isFailed = !!command && command.status === 'failed';
    const refresh = body ? findById(commands, body.refreshCommandId) : null;
    const isRefreshing = !!refresh && isCommandExecuting(refresh);
    const offerSync = isDone && body.switched && body.offerExternalSync && !isSyncDismissed;

    // Nothing to offer (unbound, no sibling line, still loading): no section -- unless a switch started
    // here still has something to say (its result, the calibre/Audiobookshelf prompt).
    const options = choices && !isFetching ? choices.options : [];

    if (!options.length && !command) {
      return null;
    }

    return (
      <div className={styles.switchLine}>
        <div className={styles.heading}>
          {translate('SwitchLineHeading')}
        </div>

        <div className={styles.hint}>
          {translate('SwitchLineHelpText')}
        </div>

        {
          options.map((option) => {
            const facts = [
              editionName(option.language),
              translate('CountVolumes', { count: option.volumeCount }),
              option.publisher,
              option.isMain ? translate('SwitchLineMainLine') : null,
              option.spinOffOf ? translate('SpinOffOf', { name: option.spinOffOf }) : null
            ].filter(Boolean).join(' · ');

            return (
              <div
                key={option.tomeLineId}
                className={styles.line}
              >
                <div className={styles.lineText}>
                  <div className={styles.lineName}>{option.name}</div>
                  <div className={styles.lineFacts}>{facts}</div>
                  {
                    option.blockedReason ?
                      <div className={styles.blocked}>{option.blockedReason}</div> :
                      null
                  }
                </div>

                <SpinnerButton
                  size={sizes.SMALL}
                  isSpinning={isSwitching && body.tomeLineId === option.tomeLineId}
                  isDisabled={!!option.blockedReason || isSwitching || isStarting}
                  onPress={() => this.onSwitchPress(option)}
                >
                  {translate('SwitchLine')}
                </SpinnerButton>
              </div>
            );
          })
        }

        {
          isFailed || (isDone && !body.switched) ?
            <Alert kind={kinds.DANGER}>{command.message}</Alert> :
            null
        }

        {
          isDone && body.switched ?
            <Alert kind={kinds.SUCCESS}>{command.message}</Alert> :
            null
        }

        {
          offerSync && isRefreshing ?
            <div className={styles.hint}>{translate('SwitchLineRefreshing')}</div> :
            null
        }

        {
          syncError ?
            <Alert kind={kinds.DANGER}>{syncError}</Alert> :
            null
        }

        <ConfirmModal
          isOpen={!!confirmOption}
          kind={kinds.PRIMARY}
          title={translate('SwitchLine')}
          message={confirmOption ? this.renderConfirmMessage(confirmOption) : ''}
          confirmLabel={translate('SwitchLine')}
          isSpinning={isStarting}
          onConfirm={this.onConfirmSwitch}
          onCancel={this.onConfirmCancel}
        />

        <ConfirmModal
          isOpen={offerSync && !isRefreshing}
          kind={kinds.PRIMARY}
          title={translate('SwitchLineExternalTitle')}
          message={body && body.movedBookFileIds ? translate('SwitchLineExternalMessage', { count: body.movedBookFileIds.length }) : ''}
          confirmLabel={translate('Yes')}
          cancelLabel={translate('No')}
          isSpinning={isSyncing}
          onConfirm={this.onSyncYes}
          onCancel={this.onSyncNo}
        />
      </div>
    );
  }
}

SwitchLine.propTypes = {
  authorId: PropTypes.number.isRequired,
  authorName: PropTypes.string.isRequired,
  commands: PropTypes.arrayOf(PropTypes.object).isRequired
};

export default connect(createMapStateToProps)(SwitchLine);
