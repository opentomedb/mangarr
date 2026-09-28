import _ from 'lodash';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { createThunk, handleThunks } from 'Store/thunks';
import getNewAuthor from 'Utilities/Author/getNewAuthor';
import monitorNewItemsOptions from 'Utilities/Author/monitorNewItemsOptions';
import monitorOptions from 'Utilities/Author/monitorOptions';
import getNewBook from 'Utilities/Book/getNewBook';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';
import { set, update, updateItem } from './baseActions';
import createHandleActions from './Creators/createHandleActions';

//
// Variables

export const section = 'search';
let abortCurrentRequest = null;

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  isAdding: false,
  isAdded: false,
  addError: null,
  items: [],

  authorDefaults: {
    rootFolderPath: '',
    monitor: monitorOptions[0].key,
    monitorNewItems: monitorNewItemsOptions[0].key,
    qualityProfileId: 0,
    metadataProfileId: 0,
    tags: [],
    searchForMissingBooks: false
  },

  // Add page toggle: which library the search runs in and which defaults the modal uses.
  // API values (contract): 'manga' | 'lightNovel'.
  library: 'manga',

  lightNovelDefaults: {
    rootFolderPath: '',
    monitor: monitorOptions[0].key,
    monitorNewItems: monitorNewItemsOptions[0].key,
    qualityProfileId: 0,
    audioQualityProfileId: 0,
    metadataProfileId: 0,
    tags: [],
    searchForMissingBooks: false
  },

  bookDefaults: {
    rootFolderPath: '',
    monitor: monitorOptions[0].key,
    monitorNewItems: monitorNewItemsOptions[0].key,
    qualityProfileId: 0,
    metadataProfileId: 0,
    tags: []
  }
};

export const persistState = [
  'search.bookDefaults',
  'search.authorDefaults',
  'search.lightNovelDefaults',
  'search.library'
];

//
// Actions Types

export const GET_SEARCH_RESULTS = 'search/getSearchResults';
export const ADD_AUTHOR = 'search/addAuthor';
export const ADD_BOOK = 'search/addBook';
export const CLEAR_SEARCH_RESULTS = 'search/clearSearchResults';
export const SET_AUTHOR_ADD_DEFAULT = 'search/setAuthorAddDefault';
export const SET_BOOK_ADD_DEFAULT = 'search/setBookAddDefault';
export const SET_SEARCH_LIBRARY = 'search/setSearchLibrary';

//
// Action Creators

export const getSearchResults = createThunk(GET_SEARCH_RESULTS);
export const addAuthor = createThunk(ADD_AUTHOR);
export const addBook = createThunk(ADD_BOOK);
export const clearSearchResults = createAction(CLEAR_SEARCH_RESULTS);
export const setAuthorAddDefault = createAction(SET_AUTHOR_ADD_DEFAULT);
export const setBookAddDefault = createAction(SET_BOOK_ADD_DEFAULT);
export const setSearchLibrary = createAction(SET_SEARCH_LIBRARY);

//
// Action Handlers

export const actionHandlers = handleThunks({

  [GET_SEARCH_RESULTS]: function(getState, payload, dispatch) {
    dispatch(set({ section, isFetching: true }));

    if (abortCurrentRequest) {
      abortCurrentRequest();
    }

    const { request, abortRequest } = createAjaxRequest({
      url: '/search',
      data: {
        term: payload.term,
        library: getState().search.library
      }
    });

    abortCurrentRequest = abortRequest;

    request.done((data) => {
      dispatch(batchActions([
        update({ section, data }),

        set({
          section,
          isFetching: false,
          isPopulated: true,
          error: null
        })
      ]));
    });

    request.fail((xhr) => {
      // An aborted request means a newer search superseded this one; its state
      // is already correct. jQuery fires this synchronously from abort(), so
      // touching state here clobbers the NEW request's isFetching=true and the
      // page shows "couldn't find any results" while that search is still
      // running - the phantom empty state that made users retry searches.
      if (xhr.aborted) {
        return;
      }

      dispatch(set({
        section,
        isFetching: false,
        isPopulated: false,
        error: xhr
      }));
    });
  },

  [ADD_AUTHOR]: function(getState, payload, dispatch) {
    dispatch(set({ section, isAdding: true }));

    const foreignAuthorId = payload.foreignAuthorId;
    const items = getState().search.items;
    const itemToAdd = _.find(items, { foreignId: foreignAuthorId });
    const newAuthor = getNewAuthor(_.cloneDeep(itemToAdd.author), payload);

    const promise = createAjaxRequest({
      url: '/author',
      method: 'POST',
      dataType: 'json',
      contentType: 'application/json',
      data: JSON.stringify(newAuthor)
    }).request;

    promise.done((data) => {
      dispatch(batchActions([
        updateItem({ section: 'authors', ...data }),

        set({
          section,
          isAdding: false,
          isAdded: true,
          addError: null
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isAdding: false,
        isAdded: false,
        addError: xhr
      }));
    });
  },

  [ADD_BOOK]: function(getState, payload, dispatch) {
    dispatch(set({ section, isAdding: true }));

    const foreignBookId = payload.foreignBookId;
    const items = getState().search.items;
    const itemToAdd = _.find(items, { foreignId: foreignBookId });
    const newBook = getNewBook(_.cloneDeep(itemToAdd.book), payload);

    const promise = createAjaxRequest({
      url: '/book',
      method: 'POST',
      dataType: 'json',
      contentType: 'application/json',
      data: JSON.stringify(newBook)
    }).request;

    promise.done((data) => {
      itemToAdd.book = data;
      dispatch(batchActions([
        updateItem({ section: 'authors', ...data.author }),
        updateItem({ section: 'books', ...data }),
        updateItem({ section, ...itemToAdd }),

        set({
          section,
          isAdding: false,
          isAdded: true,
          addError: null
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isAdding: false,
        isAdded: false,
        addError: xhr
      }));
    });
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_AUTHOR_ADD_DEFAULT]: function(state, { payload }) {
    const newState = getSectionState(state, section);
    const { library, ...values } = payload;
    const key = library === 'lightNovel' ? 'lightNovelDefaults' : 'authorDefaults';

    newState[key] = {
      ...newState[key],
      ...values
    };

    return updateSectionState(state, section, newState);
  },

  [SET_SEARCH_LIBRARY]: function(state, { payload }) {
    const newState = getSectionState(state, section);
    newState.library = payload.library;

    return updateSectionState(state, section, newState);
  },

  [SET_BOOK_ADD_DEFAULT]: function(state, { payload }) {
    const newState = getSectionState(state, section);

    newState.bookDefaults = {
      ...newState.bookDefaults,
      ...payload
    };

    return updateSectionState(state, section, newState);
  },

  [CLEAR_SEARCH_RESULTS]: function(state) {
    const {
      authorDefaults,
      bookDefaults,
      lightNovelDefaults,
      library,
      ...otherDefaultState
    } = defaultState;

    return Object.assign({}, state, otherDefaultState);
  }

}, defaultState, section);
