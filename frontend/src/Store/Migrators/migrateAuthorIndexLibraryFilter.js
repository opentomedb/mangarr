import { get, set } from 'lodash';

// 2026-09-15: the index's Manga / Light Novels switch became a tab row (the route) and the two
// built-in filters behind the old sidebar entries went away. A browser that persisted one of them
// would otherwise load with a filter key no filter has (every item shown, "Update filtered" on the
// toolbar, "Matching filter not found" in the console).
export default function migrateAuthorIndexLibraryFilter(persistedState) {
  const key = 'authorIndex.selectedFilterKey';
  const selectedFilterKey = get(persistedState, key);

  if (selectedFilterKey === 'manga' || selectedFilterKey === 'lightNovels') {
    set(persistedState, key, 'all');
  }
}
