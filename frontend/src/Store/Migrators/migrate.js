import migrateAddAuthorDefaults from './migrateAddAuthorDefaults';
import migrateAuthorDetailsSortKey from './migrateAuthorDetailsSortKey';
import migrateAuthorIndexLibraryFilter from './migrateAuthorIndexLibraryFilter';
import migrateAuthorSortKey from './migrateAuthorSortKey';
import migrateBlacklistToBlocklist from './migrateBlacklistToBlocklist';
import migrateBooksRatingColumn from './migrateBooksRatingColumn';

export default function migrate(persistedState) {
  migrateAddAuthorDefaults(persistedState);
  migrateAuthorDetailsSortKey(persistedState);
  migrateAuthorIndexLibraryFilter(persistedState);
  migrateAuthorSortKey(persistedState);
  migrateBlacklistToBlocklist(persistedState);
  migrateBooksRatingColumn(persistedState);
}
