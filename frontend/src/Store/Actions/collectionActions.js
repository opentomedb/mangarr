import { createAction } from 'redux-actions';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getNewAuthor from 'Utilities/Author/getNewAuthor';
import { MANGA } from 'Utilities/Author/libraries';
import { fetchAuthor } from './authorActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import { set } from './baseActions';

//
// Variables

// Collections: the library's series grouped by the line they belong to (an arc under its parent,
// a side story under the main line), per the metadata artifact. GET /api/v1/collection.
export const section = 'collections';

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  isAdding: false,
  addError: null,
  items: [],

  // The tab the page shows -- 'manga' | 'lightNovel'. The route (/collections/manga,
  // /collections/lightnovels) sets this; /collections reopens the remembered one.
  library: MANGA
};

//
// Actions Types

export const FETCH_COLLECTIONS = 'collections/fetchCollections';
export const CLEAR_COLLECTIONS = 'collections/clearCollections';
export const ADD_COLLECTION_MEMBERS = 'collections/addCollectionMembers';
export const SET_COLLECTIONS_LIBRARY = 'collections/setCollectionsLibrary';

//
// Action Creators

export const fetchCollections = createThunk(FETCH_COLLECTIONS);
export const clearCollections = createAction(CLEAR_COLLECTIONS);
export const addCollectionMembers = createThunk(ADD_COLLECTION_MEMBERS);
export const setCollectionsLibrary = createAction(SET_COLLECTIONS_LIBRARY);

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_COLLECTIONS]: createFetchHandler(section, '/collection'),

  // Add the missing members of a collection next to their siblings: the standard bulk add
  // (POST /author/import) with the root folder and profiles of a series already in the library.
  // A single Add (UI pass 2026-09-24, CO-4) comes from the standard Add modal instead and carries
  // its choices as `options` -- the same payload an Add New search result sends (getNewAuthor).
  [ADD_COLLECTION_MEMBERS]: function(getState, payload, dispatch) {
    const { members, defaults, searchForMissingBooks, options } = payload;

    const authors = members.map((member) => {
      if (options) {
        return getNewAuthor({ foreignAuthorId: member.foreignAuthorId, authorName: member.name }, options);
      }

      return {
        foreignAuthorId: member.foreignAuthorId,
        authorName: member.name,
        rootFolderPath: defaults.rootFolderPath,
        qualityProfileId: defaults.qualityProfileId,
        metadataProfileId: defaults.metadataProfileId,
        monitored: true,
        monitorNewItems: defaults.monitorNewItems || 'all',
        tags: defaults.tags || [],
        addOptions: {
          monitor: 'all',
          searchForMissingBooks: !!searchForMissingBooks
        }
      };
    });

    dispatch(set({ section, isAdding: true, addError: null }));

    // A single Add goes through POST /author like Add New, so a failure (already added, lookup
    // failed) comes back as an error the modal shows; the bulk import only logs per-item failures.
    const single = options && authors.length === 1;

    const promise = createAjaxRequest({
      url: single ? '/author' : '/author/import',
      method: 'POST',
      contentType: 'application/json',
      data: JSON.stringify(single ? authors[0] : authors)
    }).request;

    promise.done(() => {
      dispatch(set({ section, isAdding: false, addError: null }));
      dispatch(fetchAuthor());
      dispatch(fetchCollections());
    });

    promise.fail((xhr) => {
      dispatch(set({ section, isAdding: false, addError: xhr }));
    });
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [CLEAR_COLLECTIONS]: (state) => {
    return Object.assign({}, state, {
      isFetching: false,
      isPopulated: false,
      error: null,
      items: []
    });
  },

  [SET_COLLECTIONS_LIBRARY]: (state, { payload }) => {
    return Object.assign({}, state, { library: payload.library });
  }

}, defaultState, section);

//
// Persisted state -- the tab reopens the way the author index's does (authorIndexActions).

export const persistState = [
  'collections.library'
];
