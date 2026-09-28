import PropTypes from 'prop-types';
import React from 'react';
import { collectionAddDefaults } from 'Collections/collectionAddDefaults';
import Link from 'Components/Link/Link';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRowButton from 'Components/Table/TableRowButton';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './LibraryImportSelectFolder.css';

const columns = [
  {
    name: 'path',
    label: () => translate('Path'),
    isVisible: true
  },
  {
    name: 'freeSpace',
    label: () => translate('FreeSpace'),
    isVisible: true
  },
  {
    name: 'unmappedFolders',
    label: () => translate('UnmappedFolders'),
    isVisible: true
  }
];

// Step 1, as Sonarr's ImportSeriesSelectFolder: every root folder, whichever library it holds (a
// standard user may keep both under one root). The folder the tab's Add defaults would use --
// the same pick Collections makes (collectionAddDefaults) -- is marked, never the only one shown.
function LibraryImportSelectFolder(props) {
  const {
    library,
    rootFolders,
    qualityProfiles,
    searchState,
    onRootFolderSelect
  } = props;

  if (!rootFolders.items.length) {
    return (
      <div className={styles.message}>
        <Link to="/settings/mediamanagement">
          {translate('LibraryImportNoRootFolders')}
        </Link>
      </div>
    );
  }

  const suggested = collectionAddDefaults(searchState, rootFolders, library, qualityProfiles);
  const suggestedPath = suggested ? suggested.rootFolderPath : null;

  return (
    <div>
      <div className={styles.helpText}>
        {translate('LibraryImportHelpText')}
      </div>

      <Table columns={columns}>
        <TableBody>
          {
            rootFolders.items.map((rootFolder) => {
              const unmappedCount = (rootFolder.unmappedFolders || []).length;

              return (
                <TableRowButton
                  key={rootFolder.id}
                  className={rootFolder.path === suggestedPath ? styles.suggestedRow : styles.row}
                  onPress={() => onRootFolderSelect(rootFolder.id)}
                >
                  <TableRowCell className={styles.path}>
                    {rootFolder.path}
                  </TableRowCell>

                  <TableRowCell className={styles.freeSpace}>
                    {rootFolder.freeSpace == null ? '' : formatBytes(rootFolder.freeSpace)}
                  </TableRowCell>

                  <TableRowCell className={styles.unmappedFolders}>
                    {unmappedCount}
                  </TableRowCell>
                </TableRowButton>
              );
            })
          }
        </TableBody>
      </Table>
    </div>
  );
}

LibraryImportSelectFolder.propTypes = {
  library: PropTypes.string.isRequired,
  rootFolders: PropTypes.object.isRequired,
  qualityProfiles: PropTypes.arrayOf(PropTypes.object).isRequired,
  searchState: PropTypes.object.isRequired,
  onRootFolderSelect: PropTypes.func.isRequired
};

export default LibraryImportSelectFolder;
