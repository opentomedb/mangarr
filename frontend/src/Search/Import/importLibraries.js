import { LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';

// Library Import (2026-09-24, LI-3): the Manga | Light Novels tab is the route, as on Wanted.
// Step 1 (choose a root folder) is the tab's bare path; step 2 appends the root folder's id, the
// way Sonarr's Import Series does (/add/import/:rootFolderId). A persisted value that is not one
// of the two opens Manga -- same rule as libraryRoute (Utilities/Author/libraries).
const routes = {
  [MANGA]: '/add/import/manga',
  [LIGHT_NOVEL]: '/add/import/lightnovels'
};

export function importLibraryRoute(library, rootFolderId) {
  const route = routes[library] || routes[MANGA];

  return rootFolderId == null ? route : `${route}/${rootFolderId}`;
}
