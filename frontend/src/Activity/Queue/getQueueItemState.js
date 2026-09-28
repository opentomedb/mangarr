import moment from 'moment';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

// A pending release's status is its PendingReleaseReason (delay / downloadClientUnavailable /
// fallback), never 'pending'.
const PENDING_STATUSES = ['pending', 'delay', 'downloadclientunavailable', 'fallback'];
const DOWNLOADED_STATES = ['importPending', 'importing', 'importFailed', 'imported', 'ignored'];

function reasonsOf(statusMessages) {
  return (statusMessages || []).reduce((acc, { title, messages }) => {
    return acc.concat(messages && messages.length ? messages : [title]);
  }, []).filter((reason) => !!reason);
}

// Series page round (2026-09-24): the one reading of a queue item that the volume row
// (QueueDetails) and the volume page header share, in Sonarr's order -- pending, then the
// completed download's import states (TrackedDownloadState / TrackedDownloadStatus), then download
// warnings and failures, then queued / paused / progress. An import blocked by the decision
// engine is importPending with a Warning status and the reasons in statusMessages
// ([{ title, messages[] }]).
export default function getQueueItemState(item) {
  const {
    title,
    size,
    sizeleft,
    estimatedCompletionTime,
    status = '',
    trackedDownloadState,
    trackedDownloadStatus,
    statusMessages,
    errorMessage
  } = item;

  const lowerStatus = status.toLowerCase();
  const progress = size ? (100 - sizeleft / size * 100) : 0;
  const reasons = reasonsOf(statusMessages);
  const hasWarning = trackedDownloadStatus === 'warning';
  const hasError = trackedDownloadStatus === 'error';

  if (PENDING_STATUSES.includes(lowerStatus)) {
    return {
      iconName: icons.PENDING,
      kind: kinds.DEFAULT,
      label: translate('Pending'),
      title: translate('ReleaseWillBeProcessedInterp', [moment(estimatedCompletionTime).fromNow()])
    };
  }

  if (lowerStatus === 'completed' || DOWNLOADED_STATES.includes(trackedDownloadState)) {
    let label = translate('DownloadedWaitingToImport');
    let kind = kinds.PURPLE;
    let shownReasons = [];

    // A completed download's errorMessage alone is the client's note (NZBGet's "PAR Status:
    // SUCCESS"), not a failure: only the tracked state/status say the import failed or is blocked.
    if (hasError || trackedDownloadState === 'importFailed') {
      label = translate('DownloadedImportFailed');
      kind = kinds.DANGER;
      shownReasons = reasons;
    } else if (hasWarning) {
      label = translate('DownloadedUnableToImport');
      kind = kinds.WARNING;
      shownReasons = reasons;
    } else if (trackedDownloadState === 'importing') {
      label = translate('DownloadedImporting');
    } else if (trackedDownloadState === 'imported' || trackedDownloadState === 'ignored') {
      label = translate('Downloaded');
    }

    return {
      iconName: icons.DOWNLOAD,
      kind,
      label,
      title: [label, ...shownReasons, title].filter((line) => !!line).join('\n')
    };
  }

  // QueueStatusCell's order: a warning is not a failure, and its errorMessage is the reason.
  if (lowerStatus === 'warning' || hasWarning) {
    const warningReasons = errorMessage ? [errorMessage, ...reasons] : reasons;

    return {
      iconName: icons.DOWNLOADING,
      kind: kinds.WARNING,
      label: translate('DownloadWarning'),
      title: warningReasons.length ?
        [translate('DownloadWarning'), ...warningReasons].join('\n') :
        translate('DownloadWarningCheckDownloadClientForMoreDetails')
    };
  }

  if (lowerStatus === 'failed' || hasError || trackedDownloadState === 'downloadFailed') {
    return {
      iconName: icons.DOWNLOADING,
      kind: kinds.DANGER,
      label: translate('DownloadFailed'),
      title: errorMessage ?
        translate('DownloadFailedInterp', [errorMessage]) :
        translate('DownloadFailedCheckDownloadClientForMoreDetails')
    };
  }

  // Queued in the client; an errorMessage here is its note (qBittorrent's "downloading metadata").
  if (lowerStatus === 'queued') {
    return {
      iconName: icons.QUEUED,
      kind: kinds.DEFAULT,
      label: translate('Queued'),
      title: [translate('Queued'), errorMessage, title].filter((line) => !!line).join('\n')
    };
  }

  const downloadingTitle = translate('BookIsDownloadingInterp', [progress.toFixed(1), title]);

  if (lowerStatus === 'paused') {
    return {
      isDownloading: true,
      progress,
      iconName: icons.PAUSED,
      kind: kinds.DEFAULT,
      label: translate('Paused'),
      title: [translate('Paused'), downloadingTitle, errorMessage].filter((line) => !!line).join('\n')
    };
  }

  return {
    isDownloading: true,
    progress,
    iconName: icons.DOWNLOADING,
    kind: kinds.DEFAULT,
    label: translate('DownloadingProgress', { progress: progress.toFixed(0) }),
    title: [downloadingTitle, errorMessage].filter((line) => !!line).join('\n')
  };
}
