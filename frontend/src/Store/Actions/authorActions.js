import _ from 'lodash';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { filterTypePredicates, filterTypes, sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import { LIGHT_NOVEL } from 'Utilities/Author/libraries';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import dateFilterPredicate from 'Utilities/Date/dateFilterPredicate';
import translate from 'Utilities/String/translate';
import { set, updateItem } from './baseActions';
import { fetchBooks } from './bookActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createRemoveItemHandler from './Creators/createRemoveItemHandler';
import createSaveProviderHandler from './Creators/createSaveProviderHandler';
import createClearReducer from './Creators/Reducers/createClearReducer';
import createSetSettingValueReducer from './Creators/Reducers/createSetSettingValueReducer';

//
// Variables

export const section = 'authors';
const matchCandidates = `${section}.matchCandidates`;

let abortMatchCandidatesRequest = null;

export const filters = [
  {
    key: 'all',
    label: () => translate('All'),
    filters: []
  },
  {
    key: 'monitored',
    label: () => translate('MonitoredOnly'),
    filters: [
      {
        key: 'monitored',
        value: true,
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'unmonitored',
    label: () => translate('UnmonitoredOnly'),
    filters: [
      {
        key: 'monitored',
        value: false,
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'continuing',
    label: () => translate('ContinuingOnly'),
    filters: [
      {
        key: 'status',
        value: 'continuing',
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'ended',
    label: () => translate('EndedOnly'),
    filters: [
      {
        key: 'status',
        value: 'ended',
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'stalled',
    label: () => translate('StalledOnly'),
    filters: [
      {
        key: 'status',
        value: 'stalled',
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'missing',
    label: () => translate('MissingBooks'),
    filters: [
      {
        key: 'missing',
        value: true,
        type: filterTypes.EQUAL
      }
    ]
  }
];

export const filterPredicates = {
  // Missing Volumes: a manga with any volume still to come; a light novel with any EPUB OR any
  // audiobook still to come (EPUB = bookCount/bookFileCount, audio = audioBookCount/
  // audioBookFileCount — the numbers the cards show). A series whose audiobooks do not exist
  // yet (audioAvailable off — the card's "not yet" row) is not missing audio: nothing to get.
  missing: function(item) {
    const { statistics = {} } = item;
    const missingEbooks = statistics.bookCount - statistics.bookFileCount > 0;

    if (item.library !== LIGHT_NOVEL || !item.audioAvailable) {
      return missingEbooks;
    }

    return missingEbooks ||
      (statistics.audioBookCount || 0) - (statistics.audioBookFileCount || 0) > 0;
  },

  nextBook: function(item, filterValue, type) {
    return dateFilterPredicate(item.nextBook, filterValue, type);
  },

  lastBook: function(item, filterValue, type) {
    return dateFilterPredicate(item.lastBook, filterValue, type);
  },

  added: function(item, filterValue, type) {
    return dateFilterPredicate(item.added, filterValue, type);
  },

  ratings: function(item, filterValue, type) {
    const predicate = filterTypePredicates[type];

    return predicate(item.ratings.value * 10, filterValue);
  },

  bookCount: function(item, filterValue, type) {
    const predicate = filterTypePredicates[type];
    const bookCount = item.statistics ? item.statistics.bookCount : 0;

    return predicate(bookCount, filterValue);
  },

  sizeOnDisk: function(item, filterValue, type) {
    const predicate = filterTypePredicates[type];
    const sizeOnDisk = item.statistics && item.statistics.sizeOnDisk ?
      item.statistics.sizeOnDisk :
      0;

    return predicate(sizeOnDisk, filterValue);
  }
};

export const sortPredicates = {
  status: function(item) {
    let result = 0;

    if (item.monitored) {
      result += 2;
    }

    // a stalled licence is still watched for new volumes: it sorts with the continuing ones
    if (item.status === 'continuing' || item.status === 'stalled') {
      result++;
    }

    return result;
  },

  sizeOnDisk: function(item) {
    const { statistics = {} } = item;

    return statistics.sizeOnDisk || 0;
  }
};

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  isSaving: false,
  saveError: null,
  items: [],
  sortKey: 'sortName',
  sortDirection: sortDirections.ASCENDING,
  pendingChanges: {},

  matchCandidates: {
    isFetching: false,
    isPopulated: false,
    error: null,
    isSaving: false,
    saveError: null,
    items: []
  }
};

//
// Actions Types

export const FETCH_AUTHOR = 'authors/fetchAuthor';
export const SET_AUTHOR_VALUE = 'authors/setAuthorValue';
export const SAVE_AUTHOR = 'authors/saveAuthor';
export const DELETE_AUTHOR = 'authors/deleteAuthor';

export const TOGGLE_AUTHOR_MONITORED = 'authors/toggleAuthorMonitored';
export const TOGGLE_BOOK_MONITORED = 'authors/toggleBookMonitored';
export const UPDATE_BOOK_MONITORED = 'authors/updateBookMonitored';

export const FETCH_MATCH_CANDIDATES = 'authors/fetchMatchCandidates';
export const CLEAR_MATCH_CANDIDATES = 'authors/clearMatchCandidates';
export const SAVE_MATCH = 'authors/saveMatch';

//
// Action Creators

export const fetchAuthor = createThunk(FETCH_AUTHOR);
export const saveAuthor = createThunk(SAVE_AUTHOR, (payload) => {
  const newPayload = {
    ...payload
  };

  if (payload.moveFiles) {
    newPayload.queryParams = {
      moveFiles: true
    };
  }

  delete newPayload.moveFiles;

  return newPayload;
});

export const deleteAuthor = createThunk(DELETE_AUTHOR, (payload) => {
  return {
    ...payload,
    queryParams: {
      deleteFiles: payload.deleteFiles,
      addImportListExclusion: payload.addImportListExclusion
    }
  };
});

export const toggleAuthorMonitored = createThunk(TOGGLE_AUTHOR_MONITORED);
export const toggleBookMonitored = createThunk(TOGGLE_BOOK_MONITORED);
export const updateBookMonitor = createThunk(UPDATE_BOOK_MONITORED);

export const fetchMatchCandidates = createThunk(FETCH_MATCH_CANDIDATES);
export const saveMatch = createThunk(SAVE_MATCH);
export const clearMatchCandidates = createAction(CLEAR_MATCH_CANDIDATES);

export const setAuthorValue = createAction(SET_AUTHOR_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

//
// Helpers

function getSaveAjaxOptions({ ajaxOptions, payload }) {
  if (payload.moveFolder) {
    ajaxOptions.url = `${ajaxOptions.url}?moveFolder=true`;
  }

  return ajaxOptions;
}

//
// Action Handlers

export const actionHandlers = handleThunks({

  [FETCH_AUTHOR]: createFetchHandler(section, '/author'),
  [SAVE_AUTHOR]: createSaveProviderHandler(section, '/author', { getAjaxOptions: getSaveAjaxOptions }),
  [DELETE_AUTHOR]: createRemoveItemHandler(section, '/author'),

  [TOGGLE_AUTHOR_MONITORED]: (getState, payload, dispatch) => {
    const {
      authorId: id,
      monitored
    } = payload;

    const author = _.find(getState().authors.items, { id });

    dispatch(updateItem({
      id,
      section,
      isSaving: true
    }));

    const promise = createAjaxRequest({
      url: `/author/${id}`,
      method: 'PUT',
      data: JSON.stringify({
        ...author,
        monitored
      }),
      dataType: 'json'
    }).request;

    promise.done((data) => {
      dispatch(updateItem({
        id,
        section,
        isSaving: false,
        monitored
      }));
    });

    promise.fail((xhr) => {
      dispatch(updateItem({
        id,
        section,
        isSaving: false
      }));
    });
  },

  [TOGGLE_BOOK_MONITORED]: function(getState, payload, dispatch) {
    const {
      authorId: id,
      seasonNumber,
      monitored
    } = payload;

    const author = _.find(getState().authors.items, { id });
    const seasons = _.cloneDeep(author.seasons);
    const season = _.find(seasons, { seasonNumber });

    season.isSaving = true;

    dispatch(updateItem({
      id,
      section,
      seasons
    }));

    season.monitored = monitored;

    const promise = createAjaxRequest({
      url: `/author/${id}`,
      method: 'PUT',
      data: JSON.stringify({
        ...author,
        seasons
      }),
      dataType: 'json'
    }).request;

    promise.done((data) => {
      const books = _.filter(getState().books.items, { authorId: id, seasonNumber });

      dispatch(batchActions([
        updateItem({
          id,
          section,
          ...data
        }),

        ...books.map((book) => {
          return updateItem({
            id: book.id,
            section: 'books',
            monitored
          });
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(updateItem({
        id,
        section,
        seasons: author.seasons
      }));
    });
  },

  [UPDATE_BOOK_MONITORED]: function(getState, payload, dispatch) {
    const {
      id,
      monitor
    } = payload;

    const authorToUpdate = { id };

    if (monitor !== 'None') {
      authorToUpdate.monitored = true;
    }

    dispatch(set({
      section,
      isSaving: true
    }));

    const promise = createAjaxRequest({
      url: '/bookshelf',
      method: 'POST',
      data: JSON.stringify({
        authors: [{ id }],
        monitoringOptions: { monitor }
      }),
      dataType: 'json'
    }).request;

    promise.done((data) => {
      dispatch(fetchBooks({ authorId: id }));

      dispatch(set({
        section,
        isSaving: false,
        saveError: null
      }));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
      }));
    });
  },

  [FETCH_MATCH_CANDIDATES]: function(getState, payload, dispatch) {
    const {
      authorId,
      term
    } = payload;

    if (abortMatchCandidatesRequest) {
      abortMatchCandidatesRequest = abortMatchCandidatesRequest();
    }

    dispatch(set({
      section: matchCandidates,
      isFetching: true
    }));

    const { request, abortRequest } = createAjaxRequest({
      url: `/author/${authorId}/matchcandidates`,
      data: { term }
    });

    abortMatchCandidatesRequest = abortRequest;

    request.done((data) => {
      dispatch(set({
        section: matchCandidates,
        isFetching: false,
        isPopulated: true,
        error: null,
        items: data
      }));
    });

    request.fail((xhr) => {
      dispatch(set({
        section: matchCandidates,
        isFetching: false,
        isPopulated: false,
        error: xhr.aborted ? null : xhr
      }));
    });
  },

  [SAVE_MATCH]: function(getState, payload, dispatch) {
    const {
      authorId: id,
      aniListId
    } = payload;

    dispatch(set({
      section: matchCandidates,
      isSaving: true,
      saveError: null
    }));

    const promise = createAjaxRequest({
      url: `/author/${id}/match`,
      method: 'PUT',
      data: JSON.stringify({ aniListId }),
      dataType: 'json'
    }).request;

    promise.done((data) => {
      dispatch(batchActions([
        updateItem({
          section,
          ...data
        }),

        set({
          section: matchCandidates,
          isSaving: false,
          saveError: null
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section: matchCandidates,
        isSaving: false,
        saveError: xhr
      }));
    });
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_AUTHOR_VALUE]: createSetSettingValueReducer(section),

  [CLEAR_MATCH_CANDIDATES]: createClearReducer(matchCandidates, {
    isFetching: false,
    isPopulated: false,
    error: null,
    isSaving: false,
    saveError: null,
    items: []
  })

}, defaultState, section);
