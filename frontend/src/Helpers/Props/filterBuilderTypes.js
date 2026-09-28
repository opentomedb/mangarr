import translate from 'Utilities/String/translate';
import * as filterTypes from './filterTypes';

export const ARRAY = 'array';
export const DATE = 'date';
export const EXACT = 'exact';
export const NUMBER = 'number';
export const STRING = 'string';

export const all = [
  ARRAY,
  DATE,
  EXACT,
  NUMBER,
  STRING
];

// UI translations v1 (2026-09-25): getters, because SelectInput renders `value` as a string and
// this map is built at import time, before the translations load.
export const possibleFilterTypes = {
  [ARRAY]: [
    {
      key: filterTypes.CONTAINS,
      get value() {
        return translate('FilterContains');
      }
    },
    {
      key: filterTypes.NOT_CONTAINS,
      get value() {
        return translate('FilterDoesNotContain');
      }
    }
  ],

  [DATE]: [
    {
      key: filterTypes.LESS_THAN,
      get value() {
        return translate('FilterIsBefore');
      }
    },
    {
      key: filterTypes.GREATER_THAN,
      get value() {
        return translate('FilterIsAfter');
      }
    },
    {
      key: filterTypes.IN_LAST,
      get value() {
        return translate('FilterInLast');
      }
    },
    {
      key: filterTypes.NOT_IN_LAST,
      get value() {
        return translate('FilterNotInLast');
      }
    },
    {
      key: filterTypes.IN_NEXT,
      get value() {
        return translate('FilterInNext');
      }
    },
    {
      key: filterTypes.NOT_IN_NEXT,
      get value() {
        return translate('FilterNotInNext');
      }
    }
  ],

  [EXACT]: [
    {
      key: filterTypes.EQUAL,
      get value() {
        return translate('FilterIs');
      }
    },
    {
      key: filterTypes.NOT_EQUAL,
      get value() {
        return translate('FilterIsNot');
      }
    }
  ],

  [NUMBER]: [
    {
      key: filterTypes.EQUAL,
      get value() {
        return translate('FilterEqual');
      }
    },
    {
      key: filterTypes.GREATER_THAN,
      get value() {
        return translate('FilterGreaterThan');
      }
    },
    {
      key: filterTypes.GREATER_THAN_OR_EQUAL,
      get value() {
        return translate('FilterGreaterThanOrEqual');
      }
    },
    {
      key: filterTypes.LESS_THAN,
      get value() {
        return translate('FilterLessThan');
      }
    },
    {
      key: filterTypes.LESS_THAN_OR_EQUAL,
      get value() {
        return translate('FilterLessThanOrEqual');
      }
    },
    {
      key: filterTypes.NOT_EQUAL,
      get value() {
        return translate('FilterNotEqual');
      }
    }
  ],

  [STRING]: [
    {
      key: filterTypes.CONTAINS,
      get value() {
        return translate('FilterContains');
      }
    },
    {
      key: filterTypes.NOT_CONTAINS,
      get value() {
        return translate('FilterDoesNotContain');
      }
    },
    {
      key: filterTypes.EQUAL,
      get value() {
        return translate('FilterEqual');
      }
    },
    {
      key: filterTypes.NOT_EQUAL,
      get value() {
        return translate('FilterNotEqual');
      }
    },
    {
      key: filterTypes.STARTS_WITH,
      get value() {
        return translate('FilterStartsWith');
      }
    },
    {
      key: filterTypes.NOT_STARTS_WITH,
      get value() {
        return translate('FilterDoesNotStartWith');
      }
    },
    {
      key: filterTypes.ENDS_WITH,
      get value() {
        return translate('FilterEndsWith');
      }
    },
    {
      key: filterTypes.NOT_ENDS_WITH,
      get value() {
        return translate('FilterDoesNotEndWith');
      }
    }
  ]
};
