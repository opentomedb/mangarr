import { LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';

const routes = {
  [MANGA]: '/books/manga',
  [LIGHT_NOVEL]: '/books/lightnovels'
};

// A persisted value that is not one of the two opens Manga -- same rule as the author index's
// libraryRoute (Utilities/Author/libraries), just under /books instead of at the root.
export function bookIndexLibraryRoute(library) {
  return routes[library] || routes[MANGA];
}
