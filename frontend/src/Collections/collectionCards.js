import { LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';

// LibraryType.cs: the library is encoded in the foreign id, never stored -- a light novel adds
// "~ln" (LibraryTypes.LightNovelSuffix); Author.Library reads it back with LibraryTypes.Parse
// (a Contains check). /author/import resolves the entry it creates the same way, off the id it
// is given, regardless of which root folder or profile came with it.
const LIGHT_NOVEL_SUFFIX = '~ln';

// GcdMetadataService.IsLightNovel, mirrored for the frontend: a medium naming any kind of novel
// (the catalogue's raw value is underscored, e.g. "light_novel"), or an artbook.
function isLightNovelMedium(medium) {
  if (!medium) {
    return false;
  }

  const m = medium.toLowerCase();

  return m.includes('novel') || m === 'artbook';
}

// Which library a card belongs to, for the tab it shows on. A member in the library is its
// author's library; one that is not, the catalogue's medium (isLightNovelMedium above) -- a
// collection's root has no medium of its own, so it is the collection's own library instead.
export function libraryOfCard(card, collectionLibrary, authorsById) {
  if (card.isRoot) {
    return collectionLibrary;
  }

  const author = card.authorId ? authorsById[card.authorId] : null;

  if (author) {
    return author.library;
  }

  return isLightNovelMedium(card.medium) ? LIGHT_NOVEL : MANGA;
}

// LibraryTypes.WithType, mirrored: the id CollectionController gave a not-in-library member is
// keyed by the COLLECTION's library (every member of a (parentName, library) collection is
// looked up in that one library), not the member's own medium -- right for the common case, wrong
// for a mismatched-medium child. Idempotent, so it is safe to run on every not-in-library card,
// matching or not: adding it posts an id that actually parses back to the card's own library,
// instead of quietly becoming the wrong kind of entry no matter which defaults went with it.
function withLibrary(foreignAuthorId, library) {
  const base = foreignAuthorId.split(LIGHT_NOVEL_SUFFIX).join('');

  return library === LIGHT_NOVEL ? `${base}${LIGHT_NOVEL_SUFFIX}` : base;
}

// Every card of a collection: the root line first, then its members (arcs, side stories,
// spin-off editions), in the library or not -- each tagged with the library its tab shows it on.
export function cardsOf(collection, authorsById) {
  const root = {
    foreignAuthorId: collection.foreignAuthorId,
    name: collection.name,
    authorId: collection.authorId,
    titleSlug: collection.titleSlug,
    remotePoster: collection.remotePoster,
    inLibrary: !!collection.authorId,
    isRoot: true
  };

  return [root, ...collection.members].map((card) => {
    const library = libraryOfCard(card, collection.library, authorsById);

    return {
      ...card,
      library,
      foreignAuthorId: card.isRoot || card.inLibrary ? card.foreignAuthorId : withLibrary(card.foreignAuthorId, library)
    };
  });
}
