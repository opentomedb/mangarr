import { AuthorStatus } from 'Author/Author';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

export function getAuthorStatusDetails(status: AuthorStatus) {
  let statusDetails = {
    icon: icons.AUTHOR_CONTINUING,
    title: translate('StatusEndedContinuing'),
    message: translate('ContinuingMoreBooksAreExpected'),
  };

  if (status === 'stalled') {
    // OpenTome (2026-09-22): the licence stopped behind the original; still watched for new volumes.
    statusDetails = {
      icon: icons.PAUSED,
      title: translate('StatusStalled'),
      message: translate('StalledNoRecentVolumes'),
    };
  }

  if (status === 'ended') {
    statusDetails = {
      icon: icons.AUTHOR_ENDED,
      title: translate('StatusEndedEnded'),
      message: translate('ContinuingNoAdditionalBooksAreExpected'),
    };
  }

  return statusDetails;
}
