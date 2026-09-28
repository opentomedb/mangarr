import { LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';

const routes = {
  [MANGA]: '/collections/manga',
  [LIGHT_NOVEL]: '/collections/lightnovels'
};

// A persisted value that is not one of the two opens Manga -- same rule as the index's libraryRoute
// (Utilities/Author/libraries), just under /collections instead of at the root.
export function collectionsLibraryRoute(library) {
  return routes[library] || routes[MANGA];
}
