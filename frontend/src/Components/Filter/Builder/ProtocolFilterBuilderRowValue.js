import React from 'react';
import translate from 'Utilities/String/translate';
import FilterBuilderRowValue from './FilterBuilderRowValue';

// UI translations v1 (2026-09-25): getters, because FilterBuilderRowValue reads `name` as a string
// and this list is built at import time, before the translations load.
const protocols = [
  {
    id: 'torrent',
    get name() {
      return translate('Torrent');
    }
  },
  {
    id: 'usenet',
    get name() {
      return translate('Usenet');
    }
  }
];

function ProtocolFilterBuilderRowValue(props) {
  return (
    <FilterBuilderRowValue
      tagList={protocols}
      {...props}
    />
  );
}

export default ProtocolFilterBuilderRowValue;
