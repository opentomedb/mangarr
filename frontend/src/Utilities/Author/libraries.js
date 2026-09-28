// The two libraries, spelled as the author record's `library` spells them (the API's LibraryType
// enum, camel-cased). The index's tab, the route prop and the persisted `authorIndex.library` use
// these values verbatim; only the paths differ (/series/manga, /series/lightnovels), and only here.
export const MANGA = 'manga';
export const LIGHT_NOVEL = 'lightNovel';

const routes = {
  [MANGA]: '/series/manga',
  [LIGHT_NOVEL]: '/series/lightnovels'
};

// A persisted value that is not one of the two opens Manga.
export function libraryRoute(library) {
  return routes[library] || routes[MANGA];
}

// The index filter menu is per tab: a filter tagged with `libraries` shows only on those tabs
// (Missing Volumes on Manga; the EPUB/audio ones on Light Novels); an untagged one shows on both.
export function filtersForLibrary(filters = [], library) {
  return filters.filter((f) => !f.libraries || f.libraries.includes(library));
}

// A selected filter key is usable on a tab when it is 'all', one of that tab's filters, or a
// custom filter (custom filters are numeric ids and apply to either tab).
export function isFilterKeyForLibrary(selectedFilterKey, filters, customFilters = [], library) {
  if (selectedFilterKey === 'all' || typeof selectedFilterKey === 'number') {
    return true;
  }

  return filtersForLibrary(filters, library).some((f) => f.key === selectedFilterKey) ||
    customFilters.some((f) => f.id === selectedFilterKey);
}
