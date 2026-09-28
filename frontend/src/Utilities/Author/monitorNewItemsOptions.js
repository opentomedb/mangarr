import translate from 'Utilities/String/translate';

// UI translations v1 (2026-09-25): getters, because SelectInput renders `value` as a string and
// this list is built at import time, before the translations load.
const monitorNewItemsOptions = [
  {
    key: 'all',
    get value() {
      return translate('AllBooks');
    }
  },
  {
    key: 'none',
    get value() {
      return translate('None');
    }
  },
  {
    key: 'new',
    get value() {
      return translate('MonitorNewItemsNew');
    }
  }
];

export default monitorNewItemsOptions;
