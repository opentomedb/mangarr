import { LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';

// UI pass (2026-09-24, V3): each Wanted page has a Manga | Light Novels tab, and the tab is the
// route. A persisted value that is not one of the two opens Manga -- same rule as the index's
// libraryRoute (Utilities/Author/libraries).
const routes = {
  missing: {
    [MANGA]: '/wanted/missing/manga',
    [LIGHT_NOVEL]: '/wanted/missing/lightnovels'
  },
  cutoffUnmet: {
    [MANGA]: '/wanted/cutoffunmet/manga',
    [LIGHT_NOVEL]: '/wanted/cutoffunmet/lightnovels'
  }
};

export function wantedLibraryRoute(page, library) {
  return routes[page][library] || routes[page][MANGA];
}
