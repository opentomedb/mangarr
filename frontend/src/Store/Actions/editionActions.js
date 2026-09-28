import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getProviderState from 'Utilities/State/getProviderState';
import { updateItem } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createClearReducer from './Creators/Reducers/createClearReducer';

//
// Variables

export const section = 'editions';

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  items: [],
  itemMap: {}
};

//
// Actions Types

export const FETCH_EDITIONS = 'editions/fetchEditions';
export const CLEAR_EDITIONS = 'editions/clearEditions';
export const SAVE_EDITIONS = 'editions/saveEditions';
export const TOGGLE_EDITION_MONITORED = 'editions/toggleEditionMonitored';

//
// Action Creators

export const fetchEditions = createThunk(FETCH_EDITIONS);
export const clearEditions = createAction(CLEAR_EDITIONS);
export const saveEditions = createThunk(SAVE_EDITIONS);
export const toggleEditionMonitored = createThunk(TOGGLE_EDITION_MONITORED);

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_EDITIONS]: createFetchHandler(section, '/edition'),

  [SAVE_EDITIONS]: function(getState, payload, dispatch) {
    const {
      id,
      ...otherPayload
    } = payload;

    const saveData = getProviderState({ id, ...otherPayload }, getState, 'books');

    dispatch(batchActions([
      ...saveData.editions.map((edition) => {
        return updateItem({
          id: edition.id,
          section: 'editions',
          ...edition
        });
      })
    ]));
  },

  // Light novels: monitor / unmonitor ONE edition of a volume (PUT /edition/{id}, contract "API
  // additions"). Rows stay keyed on the book id, so the book's editions[] and mediaTypes[] are
  // patched in the books store and the row re-renders without a refetch.
  [TOGGLE_EDITION_MONITORED]: function(getState, payload, dispatch) {
    const {
      bookId,
      editionId,
      mediaType,
      monitored
    } = payload;

    dispatch(updateItem({
      id: bookId,
      section: 'books',
      isSaving: true
    }));

    const promise = createAjaxRequest({
      url: `/edition/${editionId}`,
      method: 'PUT',
      data: JSON.stringify({ monitored }),
      dataType: 'json'
    }).request;

    promise.done(() => {
      // Read the book now, not before the request: the backend also broadcasts the updated book
      // over SignalR, and patching a pre-request snapshot could overwrite that fresher copy.
      const book = getState().books.items.find((b) => b.id === bookId);

      if (!book) {
        return;
      }

      dispatch(updateItem({
        id: bookId,
        section: 'books',
        isSaving: false,
        editions: (book.editions || []).map((e) => (e.id === editionId ? { ...e, monitored } : e)),
        mediaTypes: (book.mediaTypes || []).map((m) => (m.mediaType === mediaType ? { ...m, monitored } : m))
      }));
    });

    promise.fail(() => {
      dispatch(updateItem({
        id: bookId,
        section: 'books',
        isSaving: false
      }));
    });
  }
});

//
// Reducers
export const reducers = createHandleActions({

  [CLEAR_EDITIONS]: createClearReducer(section, {
    isFetching: false,
    isPopulated: false,
    error: null,
    items: [],
    itemMap: {}
  })

}, defaultState, section);
