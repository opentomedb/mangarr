import { get, set } from 'lodash';

// Series page round (2026-09-24): the volume table's Rating column (the series' score copied onto
// every volume) became hidden by default. createPersistState's mergeColumns keeps a saved
// column's isVisible, so a browser that saved the columns before would never see the new default:
// hide it once. books.ratingColumnMigrated (persisted, true in a fresh state) marks it done, so a
// user who turns the column back on keeps it.
export default function migrateBooksRatingColumn(persistedState) {
  const columns = get(persistedState, 'books.columns');

  if (!columns || get(persistedState, 'books.ratingColumnMigrated')) {
    return;
  }

  const rating = columns.find((column) => column.name === 'rating');

  if (rating) {
    rating.isVisible = false;
  }

  set(persistedState, 'books.ratingColumnMigrated', true);
}
