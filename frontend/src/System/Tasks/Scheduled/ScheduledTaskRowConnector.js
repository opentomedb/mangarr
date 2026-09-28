import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import { executeCommand } from 'Store/Actions/commandActions';
import { fetchTask } from 'Store/Actions/systemActions';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import { findCommand, isCommandExecuting } from 'Utilities/Command';
import translate from 'Utilities/String/translate';
import ScheduledTaskRow from './ScheduledTaskRow';

// Manual tasks that rewrite files the user already owns across the whole library ask first.
const CONFIRM_TASKS = {
  WriteComicInfo: { title: 'EmbedMetadata', message: 'EmbedMetadataConfirm' }
};

function createMapStateToProps() {
  return createSelector(
    (state, { taskName }) => taskName,
    createCommandsSelector(),
    createUISettingsSelector(),
    (taskName, commands, uiSettings) => {
      const command = findCommand(commands, { name: taskName });

      return {
        isQueued: !!(command && command.state === 'queued'),
        isExecuting: isCommandExecuting(command),
        showRelativeDates: uiSettings.showRelativeDates,
        shortDateFormat: uiSettings.shortDateFormat,
        longDateFormat: uiSettings.longDateFormat,
        timeFormat: uiSettings.timeFormat
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  const taskName = props.taskName;

  return {
    dispatchFetchTask() {
      dispatch(fetchTask({
        id: props.id
      }));
    },

    onExecutePress() {
      dispatch(executeCommand({
        name: taskName
      }));
    }
  };
}

class ScheduledTaskRowConnector extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isConfirmOpen: false
    };
  }

  componentDidUpdate(prevProps) {
    const {
      isExecuting,
      dispatchFetchTask
    } = this.props;

    if (!isExecuting && prevProps.isExecuting) {
      // Give the host a moment to update after the command completes
      setTimeout(() => {
        dispatchFetchTask();
      }, 1000);
    }
  }

  //
  // Listeners

  onExecutePress = () => {
    if (CONFIRM_TASKS[this.props.taskName]) {
      this.setState({ isConfirmOpen: true });
    } else {
      this.props.onExecutePress();
    }
  };

  onConfirm = () => {
    this.setState({ isConfirmOpen: false });
    this.props.onExecutePress();
  };

  onConfirmClose = () => {
    this.setState({ isConfirmOpen: false });
  };

  //
  // Render

  render() {
    const {
      taskName,
      dispatchFetchTask,
      onExecutePress,
      ...otherProps
    } = this.props;

    const confirm = CONFIRM_TASKS[taskName];

    return (
      <>
        <ScheduledTaskRow
          {...otherProps}
          onExecutePress={this.onExecutePress}
        />

        {
          confirm ?
            <ConfirmModal
              isOpen={this.state.isConfirmOpen}
              kind={kinds.PRIMARY}
              title={translate(confirm.title)}
              message={translate(confirm.message)}
              confirmLabel={translate(confirm.title)}
              onConfirm={this.onConfirm}
              onCancel={this.onConfirmClose}
            /> :
            null
        }
      </>
    );
  }
}

ScheduledTaskRowConnector.propTypes = {
  id: PropTypes.number.isRequired,
  taskName: PropTypes.string.isRequired,
  onExecutePress: PropTypes.func.isRequired,
  isExecuting: PropTypes.bool.isRequired,
  dispatchFetchTask: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, createMapDispatchToProps)(ScheduledTaskRowConnector);
