import PropTypes from 'prop-types';
import React from 'react';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

const logLevelOptions = [
  {
    key: 'info',
    get value() {
      return translate('LogLevelInfo');
    }
  },
  {
    key: 'debug',
    get value() {
      return translate('LogLevelDebug');
    }
  },
  {
    key: 'trace',
    get value() {
      return translate('LogLevelTrace');
    }
  }
];

function LoggingSettings(props) {
  const {
    settings,
    onInputChange
  } = props;

  const {
    logLevel
  } = settings;

  return (
    <FieldSet legend={translate('Logging')}>
      <FormGroup>
        <FormLabel>
          {translate('LogLevel')}
        </FormLabel>

        <FormInputGroup
          type={inputTypes.SELECT}
          name="logLevel"
          values={logLevelOptions}
          helpTextWarning={logLevel.value === 'trace' ? translate('LogLevelvalueTraceTraceLoggingShouldOnlyBeEnabledTemporarily') : undefined}
          onChange={onInputChange}
          {...logLevel}
        />
      </FormGroup>
    </FieldSet>
  );
}

LoggingSettings.propTypes = {
  settings: PropTypes.object.isRequired,
  onInputChange: PropTypes.func.isRequired
};

export default LoggingSettings;
