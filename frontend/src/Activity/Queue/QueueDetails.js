import PropTypes from 'prop-types';
import React from 'react';
import Icon from 'Components/Icon';
import getQueueItemState from './getQueueItemState';

// Sonarr's QueueDetails: an icon for every state but a download under way, which shows the
// caller's progress bar once it passes 5%. A completed download says where its import stands
// (waiting, importing, blocked with the reasons, failed) instead of a full bar.
function QueueDetails(props) {
  const state = getQueueItemState(props);

  if (state.isDownloading && state.progress >= 5) {
    return props.progressBar;
  }

  return (
    <Icon
      name={state.iconName}
      kind={state.kind}
      title={state.title}
    />
  );
}

QueueDetails.propTypes = {
  title: PropTypes.string.isRequired,
  size: PropTypes.number.isRequired,
  sizeleft: PropTypes.number.isRequired,
  estimatedCompletionTime: PropTypes.string,
  status: PropTypes.string.isRequired,
  trackedDownloadState: PropTypes.string,
  trackedDownloadStatus: PropTypes.string,
  statusMessages: PropTypes.arrayOf(PropTypes.object),
  errorMessage: PropTypes.string,
  progressBar: PropTypes.node.isRequired
};

export default QueueDetails;
