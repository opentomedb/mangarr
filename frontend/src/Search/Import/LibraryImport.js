import PropTypes from 'prop-types';
import React from 'react';
import { Redirect } from 'react-router-dom';
import AuthorIndexLibraryTabs from 'Author/Index/Tabs/AuthorIndexLibraryTabs';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import { importLibraryRoute } from './importLibraries';
import LibraryImportFolder from './LibraryImportFolder';
import LibraryImportSelectFolder from './LibraryImportSelectFolder';

// Library Import, laid out as Sonarr's Import Series: step 1 lists the root folders, step 2
// matches one root folder's unmapped folders to series. The Manga | Light Novels tab sets the
// library the matches are searched in and whose defaults the rows start from.
function LibraryImport(props) {
  const {
    library,
    rootFolderId,
    isRootFoldersPopulated,
    rootFoldersError,
    rootFolders,
    qualityProfiles,
    metadataProfiles,
    searchState,
    existingForeignIds,
    onLibrarySelect,
    onRootFolderSelect,
    onImported,
    onImportFailed
  } = props;

  const rootFolder = rootFolderId == null ?
    null :
    rootFolders.items.find((f) => f.id === rootFolderId);

  // An id that no longer names a root folder (removed, or a stale bookmark) falls back to step 1.
  if (isRootFoldersPopulated && rootFolderId != null && !rootFolder) {
    return (
      <Redirect to={getPathWithUrlBase(importLibraryRoute(library))} />
    );
  }

  const isLoading = !isRootFoldersPopulated && !rootFoldersError;

  return (
    <PageContent title={translate('LibraryImport')}>
      <AuthorIndexLibraryTabs
        library={library}
        onLibrarySelect={onLibrarySelect}
      />

      {
        rootFolder ?
          // Keyed by tab and folder: either change starts the rows and their searches afresh.
          <LibraryImportFolder
            key={`${library}-${rootFolder.id}`}
            library={library}
            rootFolder={rootFolder}
            qualityProfiles={qualityProfiles}
            metadataProfiles={metadataProfiles}
            searchState={searchState}
            existingForeignIds={existingForeignIds}
            onImported={onImported}
            onImportFailed={onImportFailed}
          /> :
          <PageContentBody>
            {
              isLoading ?
                <LoadingIndicator /> :
                null
            }

            {
              rootFoldersError ?
                <Alert kind={kinds.DANGER}>
                  {getErrorMessage(rootFoldersError, translate('UnableToLoadRootFolders'))}
                </Alert> :
                null
            }

            {
              isRootFoldersPopulated ?
                <LibraryImportSelectFolder
                  library={library}
                  rootFolders={rootFolders}
                  qualityProfiles={qualityProfiles}
                  searchState={searchState}
                  onRootFolderSelect={onRootFolderSelect}
                /> :
                null
            }
          </PageContentBody>
      }
    </PageContent>
  );
}

LibraryImport.propTypes = {
  library: PropTypes.string.isRequired,
  rootFolderId: PropTypes.number,
  isRootFoldersPopulated: PropTypes.bool.isRequired,
  rootFoldersError: PropTypes.object,
  rootFolders: PropTypes.object.isRequired,
  qualityProfiles: PropTypes.arrayOf(PropTypes.object).isRequired,
  metadataProfiles: PropTypes.arrayOf(PropTypes.object).isRequired,
  searchState: PropTypes.object.isRequired,
  existingForeignIds: PropTypes.instanceOf(Set).isRequired,
  onLibrarySelect: PropTypes.func.isRequired,
  onRootFolderSelect: PropTypes.func.isRequired,
  onImported: PropTypes.func.isRequired,
  onImportFailed: PropTypes.func.isRequired
};

export default LibraryImport;
