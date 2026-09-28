import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';

// Shared by the Ebooks/Audiobooks cards (library name for the summary line) and their edit modals
// (the Test button and the library dropdown): one place that knows the request shapes the
// LightNovelStorageController endpoints expect.

export const preferredLightNovelFormatOptions = [
  {
    key: 'epub',
    get value() {
      return translate('LightNovelFormatEpubNoConversion');
    }
  },
  {
    key: 'azw3',
    get value() {
      return translate('LightNovelFormatAzw3Kindle');
    }
  },
  {
    key: 'kepub',
    get value() {
      return translate('LightNovelFormatKepubKobo');
    }
  }
];

export function valueOf(settings, name) {
  const setting = settings[name];

  return setting && setting.value != null ? setting.value : '';
}

// The saved value (previousValue while a change is pending) or the typed one is non-blank.
export function hasSavedOrTypedValue(settings, name) {
  const setting = settings[name];

  return !!setting && (!!setting.value || (!!setting.pending && !!setting.previousValue));
}

export function calibreBody(settings) {
  return {
    url: valueOf(settings, 'calibreContentServerUrl'),
    library: valueOf(settings, 'calibreLibrary'),
    username: valueOf(settings, 'calibreUsername'),
    password: valueOf(settings, 'calibrePassword'),
    remotePath: valueOf(settings, 'calibreRemotePath'),
    localPath: valueOf(settings, 'calibreLocalPath')
  };
}

export function audiobookshelfBody(settings) {
  return {
    url: valueOf(settings, 'audiobookshelfUrl'),
    apiKey: valueOf(settings, 'audiobookshelfApiKey'),
    libraryId: valueOf(settings, 'audiobookshelfLibraryId'),
    remotePath: valueOf(settings, 'audiobookshelfRemotePath'),
    localPath: valueOf(settings, 'audiobookshelfLocalPath')
  };
}

export function post(url, body) {
  return createAjaxRequest({
    url,
    method: 'POST',
    dataType: 'json',
    contentType: 'application/json',
    data: JSON.stringify(body)
  }).request;
}

export function fetchCalibreLibraries(settings) {
  return post('/config/lightnovelstorage/calibre/libraries', calibreBody(settings));
}

export function fetchAudiobookshelfLibraries(settings) {
  return post('/config/lightnovelstorage/audiobookshelf/libraries', audiobookshelfBody(settings));
}

// The stored choice stays listed before (or without) an answer from the server.
export function libraryValues(libraries, current, firstOption) {
  const values = [firstOption, ...libraries.map((library) => ({ key: library.id, value: library.name }))];

  if (current && !libraries.some((library) => library.id === current)) {
    values.push({ key: current, value: current });
  }

  return values;
}

// "http://calibre:8081" -> "calibre:8081"; an address that doesn't parse as a URL (still being
// typed, or just a bare host) is shown as-is.
export function hostPort(url) {
  if (!url) {
    return '';
  }

  try {
    const parsed = new URL(url);

    return parsed.port ? `${parsed.hostname}:${parsed.port}` : parsed.hostname;
  } catch (error) {
    return url;
  }
}

// The library id resolved to its name once the library list answers; the id itself (or "Server
// Default") while it hasn't, so the summary line never blocks on a fetch.
export function libraryName(libraries, id, defaultLabel) {
  if (!id) {
    return defaultLabel;
  }

  const match = libraries.find((library) => library.id === id);

  return match ? match.name : id;
}
