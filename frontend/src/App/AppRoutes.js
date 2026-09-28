import PropTypes from 'prop-types';
import React from 'react';
import { Redirect, Route } from 'react-router-dom';
import BlocklistConnector from 'Activity/Blocklist/BlocklistConnector';
import HistoryConnector from 'Activity/History/HistoryConnector';
import QueueConnector from 'Activity/Queue/QueueConnector';
import AuthorDetailsPageConnector from 'Author/Details/AuthorDetailsPageConnector';
import AuthorIndexConnector from 'Author/Index/AuthorIndexConnector';
import LibraryRedirect from 'Author/Index/LibraryRedirect';
import BookDetailsPageConnector from 'Book/Details/BookDetailsPageConnector';
import BookIndexConnector from 'Book/Index/BookIndexConnector';
import BookIndexLibraryRedirect from 'Book/Index/LibraryRedirect';
import CalendarPageConnector from 'Calendar/CalendarPageConnector';
import CollectionsConnector from 'Collections/CollectionsConnector';
import CollectionsLibraryRedirect from 'Collections/CollectionsLibraryRedirect';
import NotFound from 'Components/NotFound';
import Switch from 'Components/Router/Switch';
import AddNewItemConnector from 'Search/AddNewItemConnector';
import LibraryImportConnector from 'Search/Import/LibraryImportConnector';
import LibraryImportRedirect from 'Search/Import/LibraryImportRedirect';
import CustomFormatSettingsConnector from 'Settings/CustomFormats/CustomFormatSettingsConnector';
import DevelopmentSettingsConnector from 'Settings/Development/DevelopmentSettingsConnector';
import DownloadClientSettingsConnector from 'Settings/DownloadClients/DownloadClientSettingsConnector';
import GeneralSettingsConnector from 'Settings/General/GeneralSettingsConnector';
import ImportListSettingsConnector from 'Settings/ImportLists/ImportListSettingsConnector';
import IndexerSettingsConnector from 'Settings/Indexers/IndexerSettingsConnector';
import MediaManagementConnector from 'Settings/MediaManagement/MediaManagementConnector';
import MetadataSettings from 'Settings/Metadata/MetadataSettings';
import MetadataSourceSettings from 'Settings/MetadataSource/MetadataSourceSettings';
import NotificationSettings from 'Settings/Notifications/NotificationSettings';
import Profiles from 'Settings/Profiles/Profiles';
import QualityConnector from 'Settings/Quality/QualityConnector';
import Settings from 'Settings/Settings';
import TagSettings from 'Settings/Tags/TagSettings';
import UISettingsConnector from 'Settings/UI/UISettingsConnector';
import BackupsConnector from 'System/Backup/BackupsConnector';
import LogsTableConnector from 'System/Events/LogsTableConnector';
import Logs from 'System/Logs/Logs';
import Status from 'System/Status/Status';
import Tasks from 'System/Tasks/Tasks';
import Updates from 'System/Updates/Updates';
import UnmappedFilesTableConnector from 'UnmappedFiles/UnmappedFilesTableConnector';
import { LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import CutoffUnmetConnector from 'Wanted/CutoffUnmet/CutoffUnmetConnector';
import MissingConnector from 'Wanted/Missing/MissingConnector';
import WantedLibraryRedirect from 'Wanted/WantedLibraryRedirect';

function AppRoutes(props) {
  const {
    app
  } = props;

  return (
    <Switch>
      {/*
        Author
      */}

      <Route
        exact={true}
        path="/"
        component={LibraryRedirect}
      />

      {
        window.Readarr.urlBase &&
          <Route
            exact={true}
            path="/"
            addUrlBase={false}
            render={() => {
              return (
                <Redirect
                  to={getPathWithUrlBase('/')}
                  component={app}
                />
              );
            }}
          />
      }

      <Route
        path="/authors"
        component={LibraryRedirect}
      />

      {/* UI pass (2026-09-24, SI-8): the series lists live under /series like /books and
          /collections; the old root paths redirect so bookmarks keep working. */}
      <Route
        exact={true}
        path="/series"
        component={LibraryRedirect}
      />

      <Route
        path="/series/manga"
        render={(routeProps) => <AuthorIndexConnector {...routeProps} library={MANGA} />}
      />

      <Route
        path="/series/lightnovels"
        render={(routeProps) => <AuthorIndexConnector {...routeProps} library={LIGHT_NOVEL} />}
      />

      <Route
        path="/manga"
        render={() => <Redirect to={getPathWithUrlBase('/series/manga')} />}
      />

      <Route
        path="/lightnovels"
        render={() => <Redirect to={getPathWithUrlBase('/series/lightnovels')} />}
      />

      <Route
        path="/add/search"
        component={AddNewItemConnector}
      />

      {/* Library Import (2026-09-24): /add/import opens the remembered tab; each tab's bare path
          is step 1 (choose a root folder), and /:rootFolderId is step 2, as in Sonarr. */}
      <Route
        exact={true}
        path="/add/import"
        component={LibraryImportRedirect}
      />

      <Route
        path="/add/import/manga/:rootFolderId?"
        render={(routeProps) => <LibraryImportConnector {...routeProps} library={MANGA} />}
      />

      <Route
        path="/add/import/lightnovels/:rootFolderId?"
        render={(routeProps) => <LibraryImportConnector {...routeProps} library={LIGHT_NOVEL} />}
      />

      <Route
        exact={true}
        path="/books"
        component={BookIndexLibraryRedirect}
      />

      <Route
        path="/books/manga"
        render={(routeProps) => <BookIndexConnector {...routeProps} library={MANGA} />}
      />

      <Route
        path="/books/lightnovels"
        render={(routeProps) => <BookIndexConnector {...routeProps} library={LIGHT_NOVEL} />}
      />

      <Route
        path="/unmapped"
        component={UnmappedFilesTableConnector}
      />

      <Route
        exact={true}
        path="/collections"
        component={CollectionsLibraryRedirect}
      />

      <Route
        path="/collections/manga"
        render={(routeProps) => <CollectionsConnector {...routeProps} library={MANGA} />}
      />

      <Route
        path="/collections/lightnovels"
        render={(routeProps) => <CollectionsConnector {...routeProps} library={LIGHT_NOVEL} />}
      />

      <Route
        path="/author/:titleSlug"
        component={AuthorDetailsPageConnector}
      />

      <Route
        path="/book/:titleSlug"
        component={BookDetailsPageConnector}
      />

      {/*
        Calendar
      */}

      <Route
        path="/calendar"
        component={CalendarPageConnector}
      />

      {/*
        Activity
      */}

      <Route
        path="/activity/history"
        component={HistoryConnector}
      />

      <Route
        path="/activity/queue"
        component={QueueConnector}
      />

      <Route
        path="/activity/blocklist"
        component={BlocklistConnector}
      />

      {/*
        Wanted
      */}

      <Route
        exact={true}
        path="/wanted/missing"
        render={() => <WantedLibraryRedirect page="missing" />}
      />

      <Route
        path="/wanted/missing/manga"
        render={(routeProps) => <MissingConnector {...routeProps} library={MANGA} />}
      />

      <Route
        path="/wanted/missing/lightnovels"
        render={(routeProps) => <MissingConnector {...routeProps} library={LIGHT_NOVEL} />}
      />

      <Route
        exact={true}
        path="/wanted/cutoffunmet"
        render={() => <WantedLibraryRedirect page="cutoffUnmet" />}
      />

      <Route
        path="/wanted/cutoffunmet/manga"
        render={(routeProps) => <CutoffUnmetConnector {...routeProps} library={MANGA} />}
      />

      <Route
        path="/wanted/cutoffunmet/lightnovels"
        render={(routeProps) => <CutoffUnmetConnector {...routeProps} library={LIGHT_NOVEL} />}
      />

      {/*
        Settings
      */}

      <Route
        exact={true}
        path="/settings"
        component={Settings}
      />

      <Route
        path="/settings/mediamanagement"
        component={MediaManagementConnector}
      />

      <Route
        path="/settings/profiles"
        component={Profiles}
      />

      <Route
        path="/settings/quality"
        component={QualityConnector}
      />

      <Route
        path="/settings/customformats"
        component={CustomFormatSettingsConnector}
      />

      <Route
        path="/settings/indexers"
        component={IndexerSettingsConnector}
      />

      <Route
        path="/settings/downloadclients"
        component={DownloadClientSettingsConnector}
      />

      <Route
        path="/settings/importlists"
        component={ImportListSettingsConnector}
      />

      <Route
        path="/settings/connect"
        component={NotificationSettings}
      />

      <Route
        path="/settings/metadata"
        component={MetadataSettings}
      />

      <Route
        path="/settings/metadatasource"
        component={MetadataSourceSettings}
      />

      <Route
        path="/settings/tags"
        component={TagSettings}
      />

      <Route
        path="/settings/general"
        component={GeneralSettingsConnector}
      />

      <Route
        path="/settings/ui"
        component={UISettingsConnector}
      />

      <Route
        path="/settings/development"
        component={DevelopmentSettingsConnector}
      />

      {/*
        System
      */}

      <Route
        path="/system/status"
        component={Status}
      />

      <Route
        path="/system/tasks"
        component={Tasks}
      />

      <Route
        path="/system/backup"
        component={BackupsConnector}
      />

      <Route
        path="/system/updates"
        component={Updates}
      />

      <Route
        path="/system/events"
        component={LogsTableConnector}
      />

      <Route
        path="/system/logs/files"
        component={Logs}
      />

      {/*
        Not Found
      */}

      <Route
        path="*"
        component={NotFound}
      />

    </Switch>
  );
}

AppRoutes.propTypes = {
  app: PropTypes.func.isRequired
};

export default AppRoutes;
