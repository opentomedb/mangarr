import { createAction } from 'redux-actions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createSaveHandler from 'Store/Actions/Creators/createSaveHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';

//
// Variables

const section = 'settings.metadataSource';

//
// Actions Types

export const FETCH_METADATA_SOURCE = 'settings/metadataSource/fetchMetadataSource';
export const SET_METADATA_SOURCE_VALUE = 'settings/metadataSource/setMetadataSourceValue';
export const SAVE_METADATA_SOURCE = 'settings/metadataSource/saveMetadataSource';

//
// Action Creators

export const fetchMetadataSource = createThunk(FETCH_METADATA_SOURCE);
export const saveMetadataSource = createThunk(SAVE_METADATA_SOURCE);
export const setMetadataSourceValue = createAction(SET_METADATA_SOURCE_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    pendingChanges: {},
    isSaving: false,
    saveError: null,
    item: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_METADATA_SOURCE]: createFetchHandler(section, '/config/metadataSource'),
    [SAVE_METADATA_SOURCE]: createSaveHandler(section, '/config/metadataSource')
  },

  //
  // Reducers

  reducers: {
    [SET_METADATA_SOURCE_VALUE]: createSetSettingValueReducer(section)
  }

};
