import translate from 'Utilities/String/translate';

// UI translations v1 (2026-09-25): getters, because SelectInput renders `value` as a string and
// this list is built at import time, before the translations load.
const monitorOptions = [
  {
    key: 'all',
    get value() {
      return translate('AllBooks');
    }
  },
  {
    key: 'future',
    get value() {
      return translate('FutureBooks');
    }
  },
  {
    key: 'missing',
    get value() {
      return translate('MissingBooks');
    }
  },
  {
    key: 'existing',
    get value() {
      return translate('ExistingBooks');
    }
  },
  {
    key: 'first',
    get value() {
      return translate('MonitorFirstVolume');
    }
  },
  {
    key: 'latest',
    get value() {
      return translate('MonitorLatestVolume');
    }
  },
  {
    key: 'none',
    get value() {
      return translate('None');
    }
  }
];

export default monitorOptions;
