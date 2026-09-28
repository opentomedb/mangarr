import { createAction } from 'redux-actions';
import { filterTypes, sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import { isFilterKeyForLibrary, LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';
import { AUDIO, EBOOK } from 'Utilities/Book/mediaTypes';
import serverSideCollectionHandlers from 'Utilities/serverSideCollectionHandlers';
import translate from 'Utilities/String/translate';
import createBatchToggleBookMonitoredHandler from './Creators/createBatchToggleBookMonitoredHandler';
import createHandleActions from './Creators/createHandleActions';
import createServerSideCollectionHandlers from './Creators/createServerSideCollectionHandlers';
import createClearReducer from './Creators/Reducers/createClearReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

//
// Variables

export const section = 'wanted';

// UI pass (2026-09-24, V3): Wanted has the same Manga | Light Novels tab row as Library,
// Volumes and Collections. The tab is the route (/wanted/missing/manga, ...); `wanted.library`
// remembers it for /wanted/missing and /wanted/cutoffunmet and is sent as the `library` query
// parameter on every fetch (the fetch augmenter below), so each tab lists and searches its own
// library only. The edition presets exist on the Light Novels tab alone.
export const LIGHT_NOVEL_EBOOK_FILTER = 'lightNovelEbook';
export const LIGHT_NOVEL_AUDIO_FILTER = 'lightNovelAudio';
export const LIGHT_NOVEL_BOTH_FILTER = 'lightNovelBoth';

// The media type a search launched from a Wanted page takes from the selected filter, so the page
// and the search agree on what is being hunted. "Both missing" lists volumes whose ebook and
// audiobook are both wanted, so its search is every class -- as is every other filter.
export const FILTER_MEDIA_TYPES = {
  [LIGHT_NOVEL_EBOOK_FILTER]: EBOOK,
  [LIGHT_NOVEL_AUDIO_FILTER]: AUDIO,
  [LIGHT_NOVEL_BOTH_FILTER]: undefined
};

// The remaining query-parameter value the Both preset sets, spelled the way ASP.NET binds
// WantedMediaTypeFilter.
const BOTH_MEDIA_TYPE = 'both';

// Wanting both editions is a property of the VOLUME, not of one row, so neither the library nor
// the media type can express it: the tab's library alone would have Search All hunt every wanted
// light-novel edition while the page lists only the volumes whose ebook and audiobook are both
// wanted. This flag scopes the search to those volumes. Every other preset sends nothing.
export const FILTER_BOTH_EDITIONS = {
  [LIGHT_NOVEL_BOTH_FILTER]: true
};

// Adds the tab's library to every Wanted fetch.
function addLibrary(getState, payload, data) {
  data.library = getState().wanted.library;
}

// One preset list for both Wanted pages, built fresh per section so the two never share an object.
// A selected filter's every key/value pair is sent as a query parameter of that name
// (createFetchServerSideCollectionHandler), which is how `monitored` has always reached the API and
// how `mediaType` reaches it now. The edition presets are ANDed with monitored: true -- an
// unmonitored volume is not being hunted -- and carry `libraries` so only the Light Novels tab
// offers them (filtersForLibrary). The Both preset lists the same volumes on either page but not
// for the same reason, so each page passes its own label: missing on one, below cutoff on the other.
function wantedFilters(bothFilterLabel) {
  return [
    {
      key: 'monitored',
      label: () => translate('Monitored'),
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
      label: () => translate('Unmonitored'),
      filters: [
        {
          key: 'monitored',
          value: false,
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: LIGHT_NOVEL_EBOOK_FILTER,
      label: () => translate('FilterLightNovelEbook'),
      libraries: [LIGHT_NOVEL],
      filters: [
        {
          key: 'monitored',
          value: true,
          type: filterTypes.EQUAL
        },
        {
          key: 'mediaType',
          value: EBOOK,
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: LIGHT_NOVEL_AUDIO_FILTER,
      label: () => translate('FilterLightNovelAudio'),
      libraries: [LIGHT_NOVEL],
      filters: [
        {
          key: 'monitored',
          value: true,
          type: filterTypes.EQUAL
        },
        {
          key: 'mediaType',
          value: AUDIO,
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: LIGHT_NOVEL_BOTH_FILTER,
      label: bothFilterLabel,
      libraries: [LIGHT_NOVEL],
      filters: [
        {
          key: 'monitored',
          value: true,
          type: filterTypes.EQUAL
        },
        {
          key: 'mediaType',
          value: BOTH_MEDIA_TYPE,
          type: filterTypes.EQUAL
        }
      ]
    }
  ];
}

//
// State

export const defaultState = {
  library: MANGA,

  missing: {
    isFetching: false,
    isPopulated: false,
    pageSize: 20,
    sortKey: 'releaseDate',
    sortDirection: sortDirections.DESCENDING,
    error: null,
    items: [],

    columns: [
      {
        name: 'authorMetadata.sortName',
        label: () => translate('Author'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'books.title',
        label: () => translate('Book'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'releaseDate',
        label: () => translate('ReleaseDate'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'books.lastSearchTime',
        label: () => translate('LastSearched'),
        isSortable: true,
        isVisible: false
      },
      {
        name: 'actions',
        columnLabel: () => translate('Actions'),
        isVisible: true,
        isModifiable: false
      }
    ],

    selectedFilterKey: 'monitored',

    filters: wantedFilters(() => translate('FilterLightNovelBoth'))
  },

  cutoffUnmet: {
    isFetching: false,
    isPopulated: false,
    pageSize: 20,
    sortKey: 'releaseDate',
    sortDirection: sortDirections.DESCENDING,
    items: [],

    columns: [
      {
        name: 'authorMetadata.sortName',
        label: () => translate('Author'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'books.title',
        label: () => translate('Book'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'releaseDate',
        label: () => translate('ReleaseDate'),
        isSortable: true,
        isVisible: true
      },
      {
        name: 'books.lastSearchTime',
        label: () => translate('LastSearched'),
        isSortable: true,
        isVisible: false
      },
      {
        name: 'actions',
        columnLabel: () => translate('Actions'),
        isVisible: true,
        isModifiable: false
      }
    ],

    selectedFilterKey: 'monitored',

    filters: wantedFilters(() => translate('FilterLightNovelBothCutoff'))
  }
};

export const persistState = [
  'wanted.library',
  'wanted.missing.pageSize',
  'wanted.missing.sortKey',
  'wanted.missing.sortDirection',
  'wanted.missing.selectedFilterKey',
  'wanted.missing.columns',
  'wanted.cutoffUnmet.pageSize',
  'wanted.cutoffUnmet.sortKey',
  'wanted.cutoffUnmet.sortDirection',
  'wanted.cutoffUnmet.selectedFilterKey',
  'wanted.cutoffUnmet.columns'
];

//
// Actions Types

export const FETCH_MISSING = 'wanted/missing/fetchMissing';
export const GOTO_FIRST_MISSING_PAGE = 'wanted/missing/gotoMissingFirstPage';
export const GOTO_PREVIOUS_MISSING_PAGE = 'wanted/missing/gotoMissingPreviousPage';
export const GOTO_NEXT_MISSING_PAGE = 'wanted/missing/gotoMissingNextPage';
export const GOTO_LAST_MISSING_PAGE = 'wanted/missing/gotoMissingLastPage';
export const GOTO_MISSING_PAGE = 'wanted/missing/gotoMissingPage';
export const SET_MISSING_SORT = 'wanted/missing/setMissingSort';
export const SET_MISSING_FILTER = 'wanted/missing/setMissingFilter';
export const SET_MISSING_TABLE_OPTION = 'wanted/missing/setMissingTableOption';
export const CLEAR_MISSING = 'wanted/missing/clearMissing';

export const BATCH_TOGGLE_MISSING_BOOKS = 'wanted/missing/batchToggleMissingBooks';

export const FETCH_CUTOFF_UNMET = 'wanted/cutoffUnmet/fetchCutoffUnmet';
export const GOTO_FIRST_CUTOFF_UNMET_PAGE = 'wanted/cutoffUnmet/gotoCutoffUnmetFirstPage';
export const GOTO_PREVIOUS_CUTOFF_UNMET_PAGE = 'wanted/cutoffUnmet/gotoCutoffUnmetPreviousPage';
export const GOTO_NEXT_CUTOFF_UNMET_PAGE = 'wanted/cutoffUnmet/gotoCutoffUnmetNextPage';
export const GOTO_LAST_CUTOFF_UNMET_PAGE = 'wanted/cutoffUnmet/gotoCutoffUnmetFastPage';
export const GOTO_CUTOFF_UNMET_PAGE = 'wanted/cutoffUnmet/gotoCutoffUnmetPage';
export const SET_CUTOFF_UNMET_SORT = 'wanted/cutoffUnmet/setCutoffUnmetSort';
export const SET_CUTOFF_UNMET_FILTER = 'wanted/cutoffUnmet/setCutoffUnmetFilter';
export const SET_CUTOFF_UNMET_TABLE_OPTION = 'wanted/cutoffUnmet/setCutoffUnmetTableOption';
export const CLEAR_CUTOFF_UNMET = 'wanted/cutoffUnmet/clearCutoffUnmet';

export const BATCH_TOGGLE_CUTOFF_UNMET_BOOKS = 'wanted/cutoffUnmet/batchToggleCutoffUnmetBooks';

export const SET_WANTED_LIBRARY = 'wanted/setWantedLibrary';

//
// Action Creators

export const fetchMissing = createThunk(FETCH_MISSING);
export const gotoMissingFirstPage = createThunk(GOTO_FIRST_MISSING_PAGE);
export const gotoMissingPreviousPage = createThunk(GOTO_PREVIOUS_MISSING_PAGE);
export const gotoMissingNextPage = createThunk(GOTO_NEXT_MISSING_PAGE);
export const gotoMissingLastPage = createThunk(GOTO_LAST_MISSING_PAGE);
export const gotoMissingPage = createThunk(GOTO_MISSING_PAGE);
export const setMissingSort = createThunk(SET_MISSING_SORT);
export const setMissingFilter = createThunk(SET_MISSING_FILTER);
export const setMissingTableOption = createAction(SET_MISSING_TABLE_OPTION);
export const clearMissing = createAction(CLEAR_MISSING);

export const batchToggleMissingBooks = createThunk(BATCH_TOGGLE_MISSING_BOOKS);

export const fetchCutoffUnmet = createThunk(FETCH_CUTOFF_UNMET);
export const gotoCutoffUnmetFirstPage = createThunk(GOTO_FIRST_CUTOFF_UNMET_PAGE);
export const gotoCutoffUnmetPreviousPage = createThunk(GOTO_PREVIOUS_CUTOFF_UNMET_PAGE);
export const gotoCutoffUnmetNextPage = createThunk(GOTO_NEXT_CUTOFF_UNMET_PAGE);
export const gotoCutoffUnmetLastPage = createThunk(GOTO_LAST_CUTOFF_UNMET_PAGE);
export const gotoCutoffUnmetPage = createThunk(GOTO_CUTOFF_UNMET_PAGE);
export const setCutoffUnmetSort = createThunk(SET_CUTOFF_UNMET_SORT);
export const setCutoffUnmetFilter = createThunk(SET_CUTOFF_UNMET_FILTER);
export const setCutoffUnmetTableOption = createAction(SET_CUTOFF_UNMET_TABLE_OPTION);
export const clearCutoffUnmet = createAction(CLEAR_CUTOFF_UNMET);

export const batchToggleCutoffUnmetBooks = createThunk(BATCH_TOGGLE_CUTOFF_UNMET_BOOKS);

export const setWantedLibrary = createAction(SET_WANTED_LIBRARY);

//
// Action Handlers

export const actionHandlers = handleThunks({

  ...createServerSideCollectionHandlers(
    'wanted.missing',
    '/wanted/missing',
    fetchMissing,
    {
      [serverSideCollectionHandlers.FETCH]: FETCH_MISSING,
      [serverSideCollectionHandlers.FIRST_PAGE]: GOTO_FIRST_MISSING_PAGE,
      [serverSideCollectionHandlers.PREVIOUS_PAGE]: GOTO_PREVIOUS_MISSING_PAGE,
      [serverSideCollectionHandlers.NEXT_PAGE]: GOTO_NEXT_MISSING_PAGE,
      [serverSideCollectionHandlers.LAST_PAGE]: GOTO_LAST_MISSING_PAGE,
      [serverSideCollectionHandlers.EXACT_PAGE]: GOTO_MISSING_PAGE,
      [serverSideCollectionHandlers.SORT]: SET_MISSING_SORT,
      [serverSideCollectionHandlers.FILTER]: SET_MISSING_FILTER
    },
    addLibrary
  ),

  [BATCH_TOGGLE_MISSING_BOOKS]: createBatchToggleBookMonitoredHandler('wanted.missing', fetchMissing),

  ...createServerSideCollectionHandlers(
    'wanted.cutoffUnmet',
    '/wanted/cutoff',
    fetchCutoffUnmet,
    {
      [serverSideCollectionHandlers.FETCH]: FETCH_CUTOFF_UNMET,
      [serverSideCollectionHandlers.FIRST_PAGE]: GOTO_FIRST_CUTOFF_UNMET_PAGE,
      [serverSideCollectionHandlers.PREVIOUS_PAGE]: GOTO_PREVIOUS_CUTOFF_UNMET_PAGE,
      [serverSideCollectionHandlers.NEXT_PAGE]: GOTO_NEXT_CUTOFF_UNMET_PAGE,
      [serverSideCollectionHandlers.LAST_PAGE]: GOTO_LAST_CUTOFF_UNMET_PAGE,
      [serverSideCollectionHandlers.EXACT_PAGE]: GOTO_CUTOFF_UNMET_PAGE,
      [serverSideCollectionHandlers.SORT]: SET_CUTOFF_UNMET_SORT,
      [serverSideCollectionHandlers.FILTER]: SET_CUTOFF_UNMET_FILTER
    },
    addLibrary
  ),

  [BATCH_TOGGLE_CUTOFF_UNMET_BOOKS]: createBatchToggleBookMonitoredHandler('wanted.cutoffUnmet', fetchCutoffUnmet)

});

//
// Reducers

export const reducers = createHandleActions({

  // A tab switch keeps each page's filter when that tab offers it; an edition preset (Light
  // Novels only) falls back to Monitored on the Manga tab.
  [SET_WANTED_LIBRARY]: function(state, { payload }) {
    const { library } = payload;
    const scoped = (page) => {
      return isFilterKeyForLibrary(page.selectedFilterKey, page.filters, [], library) ?
        page :
        { ...page, selectedFilterKey: 'monitored' };
    };

    return {
      ...state,
      library,
      missing: scoped(state.missing),
      cutoffUnmet: scoped(state.cutoffUnmet)
    };
  },

  [SET_MISSING_TABLE_OPTION]: createSetTableOptionReducer('wanted.missing'),
  [SET_CUTOFF_UNMET_TABLE_OPTION]: createSetTableOptionReducer('wanted.cutoffUnmet'),

  [CLEAR_MISSING]: createClearReducer(
    'wanted.missing',
    {
      isFetching: false,
      isPopulated: false,
      error: null,
      items: [],
      totalPages: 0,
      totalRecords: 0
    }
  ),

  [CLEAR_CUTOFF_UNMET]: createClearReducer(
    'wanted.cutoffUnmet',
    {
      isFetching: false,
      isPopulated: false,
      error: null,
      items: [],
      totalPages: 0,
      totalRecords: 0
    }
  )

}, defaultState, section);
