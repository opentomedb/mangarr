import React from 'react';
import translate from 'Utilities/String/translate';
import FilterBuilderRowValue from './FilterBuilderRowValue';

// UI translations v1 (2026-09-25): getters, because FilterBuilderRowValue reads `name` as a string
// and this list is built at import time, before the translations load.
const protocols = [
  {
    id: 'continuing',
    get name() {
      return translate('Continuing');
    }
  },
  {
    id: 'ended',
    get name() {
      return translate('Ended');
    }
  },
  {
    id: 'stalled',
    get name() {
      return translate('StatusStalled');
    }
  }
];

function AuthorStatusFilterBuilderRowValue(props) {
  return (
    <FilterBuilderRowValue
      tagList={protocols}
      {...props}
    />
  );
}

export default AuthorStatusFilterBuilderRowValue;
