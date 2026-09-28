import { createAction } from 'redux-actions';
import { sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import translate from 'Utilities/String/translate';
import { set } from './baseActions';
import { filterPredicates, sortPredicates } from './bookActions';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';

//
// Variables

export const section = 'authorDetails';

//
// State

export const defaultState = {
  sortKey: 'title',
  sortDirection: sortDirections.ASCENDING,
  secondarySortKey: 'releaseDate',
  secondarySortDirection: sortDirections.DESCENDING,

  // Light novels: which edition the volume table shows — 'ebook' (EPUB) or 'audio'. Manga
  // pages have one edition and ignore it. Persisted so the tab survives navigation.
  selectedMediaType: 'ebook',

  selectedFilterKey: 'authorId',

  sortPredicates: {
    ...sortPredicates,

    // Manga: the "Title" column is "{Series} Vol. N" — sort by the numeric volume number so it
    // orders 1, 2, 3, ... 10 instead of the raw string order 1, 10, 11, 2, ... Scoped to the
    // author-details volume table; the global Book Index keeps alphabetical title sort.
    title: function(item) {
      return item.volumeNumber || 0;
    }
  },

  filters: [
    {
      key: 'authorId',
      label: () => translate('Author'),
      filters: [
        {
          key: 'authorId',
          value: 0
        }
      ]
    }
  ],

  filterPredicates

};

// Deliberately do NOT persist this view's sort. redux-persist writes the default to
// localStorage on first load, so persisting it would pin every existing user to the OLD
// releaseDate/descending default forever and the new volume-number default would never take
// effect. The volume list should always open in volume order; a manual re-sort lasts the
// session and resets on reload, which is the desired behaviour for this table.
export const persistState = [
  'authorDetails.selectedMediaType'
];

//
// Actions Types

export const SET_AUTHOR_DETAILS_SORT = 'authorIndex/setAuthorDetailsSort';
export const SET_AUTHOR_DETAILS_ID = 'authorIndex/setAuthorDetailsId';
export const SET_AUTHOR_DETAILS_MEDIA_TYPE = 'authorDetails/setAuthorDetailsMediaType';

//
// Action Creators

export const setAuthorDetailsSort = createAction(SET_AUTHOR_DETAILS_SORT);
export const setAuthorDetailsId = createThunk(SET_AUTHOR_DETAILS_ID);
export const setAuthorDetailsMediaType = createAction(SET_AUTHOR_DETAILS_MEDIA_TYPE);

//
// Action Handlers

export const actionHandlers = handleThunks({
  [SET_AUTHOR_DETAILS_ID]: function(getState, payload, dispatch) {
    const {
      authorId
    } = payload;

    dispatch(set({
      section,
      filters: [
        {
          key: 'authorId',
          label: () => translate('Author'),
          filters: [
            {
              key: 'authorId',
              value: authorId
            }
          ]
        }
      ]
    }));
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_AUTHOR_DETAILS_SORT]: createSetClientSideCollectionSortReducer(section),

  [SET_AUTHOR_DETAILS_MEDIA_TYPE]: function(state, { payload }) {
    return Object.assign({}, state, { selectedMediaType: payload.mediaType });
  }

}, defaultState, section);
