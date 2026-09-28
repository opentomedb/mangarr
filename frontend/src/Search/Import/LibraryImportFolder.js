import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import PageContentBody from 'Components/Page/PageContentBody';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import getNewAuthor from 'Utilities/Author/getNewAuthor';
import { LIGHT_NOVEL } from 'Utilities/Author/libraries';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import { libraryImportDefaults } from './libraryImportDefaults';
import LibraryImportFooter from './LibraryImportFooter';
import LibraryImportRow from './LibraryImportRow';
import styles from './LibraryImportFolder.css';

// A large root would otherwise fire one lookup per folder at once; each lookup is a
// multi-provider crawl, so only a few run at a time and the rest wait their turn.
const MAX_SEARCHES_IN_FLIGHT = 3;

// 1 s, as on Add New and Fix Match: every partial term typed into the picker is a live lookup.
const TERM_DEBOUNCE_MS = 1000;

const FOOTER_FIELDS = ['monitor', 'monitorNewItems', 'qualityProfileId', 'audioQualityProfileId'];

function getColumns(isLightNovel) {
  return [
    {
      name: 'folder',
      label: () => translate('Folder'),
      isVisible: true
    },
    {
      name: 'monitor',
      label: () => translate('Monitor'),
      isVisible: true
    },
    {
      name: 'qualityProfileId',
      label: () => (isLightNovel ? translate('EbookQualityProfile') : translate('QualityProfile')),
      isVisible: true
    },
    {
      name: 'audioQualityProfileId',
      label: () => translate('AudioQualityProfile'),
      isVisible: isLightNovel
    },
    {
      name: 'formats',
      label: () => translate('Formats'),
      isVisible: isLightNovel
    },
    {
      name: 'series',
      label: () => translate('Series'),
      isVisible: true
    }
  ];
}

function cleanPath(path) {
  return (path || '').replace(/[\\/]+$/, '');
}

// Step 2, as Sonarr's ImportSeries: one row per unmapped folder of one root folder, each matched
// by a library-scoped search of its folder name. The rows and their searches live here, not in the
// store: they belong to this one visit to this one root folder and are thrown away on leaving it.
class LibraryImportFolder extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    const defaults = this.getDefaults();

    this.state = {
      defaults: _.pick(defaults, FOOTER_FIELDS),
      rows: this.createRows(props.rootFolder.unmappedFolders, defaults),
      searchForMissingBooks: false,
      isImporting: false,
      importError: null,
      importResult: null
    };

    this._queue = [];
    this._inFlight = {};
    this._termTimeouts = {};
    this._isUnmounted = false;
  }

  componentDidMount() {
    this.state.rows.forEach((row) => this.queueSearch(row.path));
  }

  componentDidUpdate(prevProps) {
    // Root folders are refetched after an import: imported folders leave the table and any new
    // folder joins it. Rows that stay keep their match and their edits.
    if (prevProps.rootFolder.unmappedFolders !== this.props.rootFolder.unmappedFolders) {
      this.syncRows();
    }

    // A series that just joined the library (e.g. two folders matched the same series and one
    // was imported) can't be checked for another folder.
    if (
      prevProps.existingForeignIds !== this.props.existingForeignIds &&
      this.state.rows.some((row) => row.isSelected && !this.canSelect(row.match))
    ) {
      this.setState((prevState) => ({
        rows: prevState.rows.map((row) => {
          return row.isSelected && !this.canSelect(row.match) ? { ...row, isSelected: false } : row;
        })
      }));
    }
  }

  componentWillUnmount() {
    this._isUnmounted = true;
    this._queue = [];
    Object.values(this._termTimeouts).forEach((t) => clearTimeout(t));
    Object.values(this._inFlight).forEach((abort) => abort());
    this._inFlight = {};
  }

  //
  // Control

  getDefaults() {
    const {
      library,
      rootFolder,
      searchState,
      qualityProfiles,
      metadataProfiles
    } = this.props;

    return libraryImportDefaults(rootFolder, library, searchState, qualityProfiles, metadataProfiles);
  }

  createRows(unmappedFolders = [], values) {
    return unmappedFolders.map((folder) => ({
      path: folder.path,
      name: folder.name,
      term: folder.name,
      generation: 0,
      isFetching: true,
      items: [],
      match: null,
      isSelected: false,
      monitor: values.monitor,
      qualityProfileId: values.qualityProfileId,
      audioQualityProfileId: values.audioQualityProfileId,
      addEbook: true,
      addAudio: true
    }));
  }

  syncRows() {
    const unmappedFolders = this.props.rootFolder.unmappedFolders || [];
    const paths = new Set(unmappedFolders.map((f) => f.path));
    const existing = _.keyBy(this.state.rows, 'path');

    this.state.rows
      .filter((row) => !paths.has(row.path))
      .forEach((row) => this.cancelSearch(row.path));

    const newFolders = unmappedFolders.filter((f) => !existing[f.path]);
    const newRows = _.keyBy(this.createRows(newFolders, this.state.defaults), 'path');

    this.setState({
      rows: unmappedFolders.map((f) => existing[f.path] || newRows[f.path])
    }, () => {
      newFolders.forEach((f) => this.queueSearch(f.path));

      // A cancelled search freed a slot; let a waiting row have it.
      this.pumpQueue();
    });
  }

  canSelect(match) {
    return !!match && !this.props.existingForeignIds.has(match.foreignAuthorId);
  }

  isImportable(row) {
    const hasFormat = this.props.library !== LIGHT_NOVEL || row.addEbook || row.addAudio;

    return row.isSelected && !row.isFetching && this.canSelect(row.match) && hasFormat;
  }

  updateRow(path, changes, generation) {
    this.setState((prevState) => ({
      rows: prevState.rows.map((row) => {
        if (row.path !== path || (generation != null && row.generation !== generation)) {
          return row;
        }

        return { ...row, ...(typeof changes === 'function' ? changes(row) : changes) };
      })
    }));
  }

  // A search the user asked for (a new term in the picker) jumps the queue, as Sonarr's
  // topOfQueue does; the initial per-folder searches wait their turn.
  queueSearch(path, topOfQueue = false) {
    this._queue = this._queue.filter((p) => p !== path);

    if (topOfQueue) {
      this._queue.unshift(path);
    } else {
      this._queue.push(path);
    }

    this.pumpQueue();
  }

  cancelSearch(path) {
    clearTimeout(this._termTimeouts[path]);
    delete this._termTimeouts[path];
    this._queue = this._queue.filter((p) => p !== path);

    if (this._inFlight[path]) {
      const abort = this._inFlight[path];

      // Free the slot now; the aborted request's own handlers see it isn't theirs any more.
      delete this._inFlight[path];
      abort();
    }
  }

  pumpQueue() {
    while (!this._isUnmounted && this._queue.length && Object.keys(this._inFlight).length < MAX_SEARCHES_IN_FLIGHT) {
      this.search(this._queue.shift());
    }
  }

  search(path) {
    const row = this.state.rows.find((r) => r.path === path);

    if (!row) {
      return;
    }

    const { generation, term } = row;

    const { request, abortRequest } = createAjaxRequest({
      url: '/search',
      data: {
        term,
        library: this.props.library
      }
    });

    this._inFlight[path] = abortRequest;

    // Each request gives its slot back exactly once, and only if the slot is still its own.
    const release = () => {
      if (this._inFlight[path] === abortRequest) {
        delete this._inFlight[path];
      }
    };

    request.done((data) => {
      release();

      if (this._isUnmounted) {
        return;
      }

      // Search can also return volumes; only series can be imported. The picker lists up to 10.
      const items = (Array.isArray(data) ? data : [])
        .filter((result) => !!result.author)
        .slice(0, 10)
        .map((result) => result.author);

      const match = items.length ? items[0] : null;

      this.updateRow(path, {
        isFetching: false,
        items,
        match,
        isSelected: this.canSelect(match)
      }, generation);

      this.pumpQueue();
    });

    request.fail((xhr) => {
      release();

      // Aborted: superseded by a newer term or the page is closing; nothing to show.
      if (xhr.aborted || this._isUnmounted) {
        return;
      }

      this.updateRow(path, {
        isFetching: false,
        items: [],
        match: null,
        isSelected: false
      }, generation);

      this.pumpQueue();
    });
  }

  //
  // Listeners

  onSelectAllChange = ({ value }) => {
    this.setState((prevState) => ({
      rows: prevState.rows.map((row) => {
        return this.canSelect(row.match) && !row.isFetching ? { ...row, isSelected: value } : row;
      })
    }));
  };

  onSelectedChange = ({ id, value }) => {
    // TableSelectCell reports null when it unmounts; that isn't a choice.
    if (value == null) {
      return;
    }

    this.updateRow(id, (row) => ({
      isSelected: value && this.canSelect(row.match)
    }));
  };

  onRowInputChange = (path, { name, value }) => {
    this.updateRow(path, { [name]: value });
  };

  onTermChange = (path, term) => {
    this.updateRow(path, { term });
    clearTimeout(this._termTimeouts[path]);

    if (!term.trim()) {
      return;
    }

    this._termTimeouts[path] = setTimeout(() => {
      delete this._termTimeouts[path];
      this.cancelSearch(path);
      this.updateRow(path, (row) => ({
        generation: row.generation + 1,
        isFetching: true,
        items: [],
        match: null,
        isSelected: false
      }));

      // setState is batched; queue once the new generation is in place.
      this.setState({}, () => this.queueSearch(path, true));
    }, TERM_DEBOUNCE_MS);
  };

  onMatchSelect = (path, match) => {
    this.updateRow(path, {
      match,
      isSelected: this.canSelect(match)
    });
  };

  // A footer default is applied to every selected row, as Sonarr's ImportSeriesFooter does, and to
  // every row still searching: those check themselves when their match arrives, and would
  // otherwise keep the value from before the change. Monitor New Volumes has no column, so it
  // stays in the footer alone and is read from there on import.
  onFooterInputChange = ({ name, value }) => {
    this.setState((prevState) => ({
      defaults: { ...prevState.defaults, [name]: value },
      rows: name === 'monitorNewItems' ?
        prevState.rows :
        prevState.rows.map((row) => (row.isSelected || row.isFetching ? { ...row, [name]: value } : row))
    }));
  };

  onSearchForMissingBooksChange = ({ value }) => {
    this.setState({ searchForMissingBooks: value });
  };

  onImportPress = () => {
    const {
      library,
      rootFolder
    } = this.props;

    const {
      rows,
      defaults,
      searchForMissingBooks
    } = this.state;

    const isLightNovel = library === LIGHT_NOVEL;
    const toImport = rows.filter((row) => this.isImportable(row));

    if (!toImport.length) {
      return;
    }

    // Metadata profile and tags have no column: the same values an Add of this library would use.
    const { metadataProfileId, tags } = this.getDefaults();

    const payload = toImport.map((row) => {
      const newAuthor = getNewAuthor(_.cloneDeep(row.match), {
        rootFolderPath: rootFolder.path,
        monitor: row.monitor,
        monitorNewItems: defaults.monitorNewItems,
        qualityProfileId: row.qualityProfileId,
        audioQualityProfileId: isLightNovel ? row.audioQualityProfileId : undefined,
        metadataProfileId,
        tags,
        searchForMissingBooks,
        // Light novel: both editions of every volume are minted; an unticked format's editions
        // start unmonitored (the Add modal's addOptions.monitorMediaTypes).
        monitorMediaTypes: isLightNovel ?
          [row.addEbook ? 'ebook' : null, row.addAudio ? 'audio' : null].filter(Boolean) :
          undefined
      });

      // The EXACT existing folder, so the series' files there are matched instead of a new
      // folder being created from the naming format.
      newAuthor.path = row.path;

      return newAuthor;
    });

    this.setState({
      isImporting: true,
      importError: null,
      importResult: null
    });

    const { request } = createAjaxRequest({
      url: '/author/import',
      method: 'POST',
      dataType: 'json',
      contentType: 'application/json',
      data: JSON.stringify(payload)
    });

    request.done((data) => {
      const imported = Array.isArray(data) ? data : [];

      // The bulk endpoint logs a series it couldn't add and leaves it out of the reply, so a
      // folder that didn't come back is the only sign of a failure.
      const importedPaths = new Set(imported.map((a) => cleanPath(a.path)));
      const notImported = toImport
        .filter((row) => !importedPaths.has(cleanPath(row.path)))
        .map((row) => row.name);

      if (!this._isUnmounted) {
        this.setState({
          isImporting: false,
          importResult: {
            count: imported.length,
            notImported
          }
        });
      }

      this.props.onImported(imported);
    });

    request.fail((xhr) => {
      // A failure (or a proxy timeout) can still have added some series: reload the root folders
      // and the series list so the table and the store show what actually landed.
      this.props.onImportFailed();

      if (this._isUnmounted) {
        return;
      }

      this.setState({
        isImporting: false,
        importError: translate('ImportFailedInterp', [getErrorMessage(xhr, `HTTP ${xhr.status}`)])
      });
    });
  };

  //
  // Render

  render() {
    const {
      library,
      existingForeignIds
    } = this.props;

    const {
      rows,
      defaults,
      searchForMissingBooks,
      isImporting,
      importError,
      importResult
    } = this.state;

    const isLightNovel = library === LIGHT_NOVEL;
    const selectable = rows.filter((row) => !row.isFetching && this.canSelect(row.match));
    const allSelected = !!selectable.length && selectable.every((row) => row.isSelected);
    const allUnselected = selectable.every((row) => !row.isSelected);
    const importCount = rows.filter((row) => this.isImportable(row)).length;

    return (
      <>
        <PageContentBody>
          {
            rows.length ?
              <Table
                columns={getColumns(isLightNovel)}
                selectAll={true}
                allSelected={allSelected}
                allUnselected={allUnselected}
                onSelectAllChange={this.onSelectAllChange}
              >
                <TableBody>
                  {
                    rows.map((row) => {
                      return (
                        <LibraryImportRow
                          key={row.path}
                          {...row}
                          isLightNovel={isLightNovel}
                          isExisting={!!row.match && existingForeignIds.has(row.match.foreignAuthorId)}
                          existingForeignIds={existingForeignIds}
                          onSelectedChange={this.onSelectedChange}
                          onInputChange={this.onRowInputChange}
                          onTermChange={this.onTermChange}
                          onMatchSelect={this.onMatchSelect}
                        />
                      );
                    })
                  }
                </TableBody>
              </Table> :
              <div className={styles.message}>
                {translate('LibraryImportNoUnmappedFolders')}
              </div>
          }
        </PageContentBody>

        <LibraryImportFooter
          library={library}
          {...defaults}
          searchForMissingBooks={searchForMissingBooks}
          importCount={importCount}
          isImporting={isImporting}
          importError={importError}
          importResult={importResult}
          onInputChange={this.onFooterInputChange}
          onSearchForMissingBooksChange={this.onSearchForMissingBooksChange}
          onImportPress={this.onImportPress}
        />
      </>
    );
  }
}

LibraryImportFolder.propTypes = {
  library: PropTypes.string.isRequired,
  rootFolder: PropTypes.object.isRequired,
  qualityProfiles: PropTypes.arrayOf(PropTypes.object).isRequired,
  metadataProfiles: PropTypes.arrayOf(PropTypes.object).isRequired,
  searchState: PropTypes.object.isRequired,
  existingForeignIds: PropTypes.instanceOf(Set).isRequired,
  onImported: PropTypes.func.isRequired,
  onImportFailed: PropTypes.func.isRequired
};

export default LibraryImportFolder;
