import PropTypes from 'prop-types';
import React from 'react';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

function OAuthInput(props) {
  const {
    label,
    authorizing,
    error,
    onPress
  } = props;

  return (
    <div>
      <SpinnerErrorButton
        kind={kinds.PRIMARY}
        isSpinning={authorizing}
        error={error}
        onPress={onPress}
      >
        {label ?? translate('StartOAuth')}
      </SpinnerErrorButton>
    </div>
  );
}

OAuthInput.propTypes = {
  label: PropTypes.string,
  authorizing: PropTypes.bool.isRequired,
  error: PropTypes.object,
  onPress: PropTypes.func.isRequired
};

export default OAuthInput;
