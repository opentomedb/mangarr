import { unset } from 'lodash';

// The author-details volume table used to default to releaseDate/descending. redux-localstorage
// writes the default into every user's localStorage on first load, and createPersistState's
// merge() rehydrates the whole stored blob regardless of the current persist whitelist — so a
// stale persisted sort would pin existing users to the old order forever and the new
// volume-number default (authorDetailsActions defaultState) would never take effect. Drop the
// persisted sort so the default applies. authorDetails sort is no longer persisted
// (persistState = []), so this is a one-way cleanup that never fights a deliberate choice.
export default function migrateAuthorDetailsSortKey(persistedState) {
  unset(persistedState, 'authorDetails.sortKey');
  unset(persistedState, 'authorDetails.sortDirection');
}
