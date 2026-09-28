import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { Tab, TabList, TabPanel, Tabs } from 'react-tabs';
import AuthorHistoryTable from 'Author/History/AuthorHistoryTable';
import DeleteBookModal from 'Book/Delete/DeleteBookModal';
import EditBookModalConnector from 'Book/Edit/EditBookModalConnector';
import BookFileEditorTable from 'BookFile/Editor/BookFileEditorTable';
import FlipPageOrderModal from 'BookFile/Flip/FlipPageOrderModal';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import SwipeHeaderConnector from 'Components/Swipe/SwipeHeaderConnector';
import { icons, kinds, sizes } from 'Helpers/Props';
import InteractiveSearchFilterMenuConnector from 'InteractiveSearch/InteractiveSearchFilterMenuConnector';
import InteractiveSearchTable from 'InteractiveSearch/InteractiveSearchTable';
import OrganizePreviewModalConnector from 'Organize/OrganizePreviewModalConnector';
import RetagPreviewModalConnector from 'Retag/RetagPreviewModalConnector';
import { AUDIO, EBOOK } from 'Utilities/Book/mediaTypes';
import translate from 'Utilities/String/translate';
import BookDetailsHeaderConnector from './BookDetailsHeaderConnector';
import styles from './BookDetails.css';

class BookDetails extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isOrganizeModalOpen: false,
      isRetagModalOpen: false,
      isEditBookModalOpen: false,
      isDeleteBookModalOpen: false,
      isFlipModalOpen: false,
      selectedTabIndex: 0
    };
  }

  //
  // Listeners

  onOrganizePress = () => {
    this.setState({ isOrganizeModalOpen: true });
  };

  onOrganizeModalClose = () => {
    this.setState({ isOrganizeModalOpen: false });
  };

  onRetagPress = () => {
    this.setState({ isRetagModalOpen: true });
  };

  onRetagModalClose = () => {
    this.setState({ isRetagModalOpen: false });
  };

  onFlipModalPress = () => {
    this.setState({ isFlipModalOpen: true });
  };

  onFlipModalClose = () => {
    this.setState({ isFlipModalOpen: false });
  };

  onEditBookPress = () => {
    this.setState({ isEditBookModalOpen: true });
  };

  onEditBookModalClose = () => {
    this.setState({ isEditBookModalOpen: false });
  };

  onDeleteBookPress = () => {
    this.setState({
      isEditBookModalOpen: false,
      isDeleteBookModalOpen: true
    });
  };

  onDeleteBookModalClose = () => {
    this.setState({ isDeleteBookModalOpen: false });
  };

  onTabSelect = (index, lastIndex) => {
    this.setState({ selectedTabIndex: index });
  };

  //
  // Render

  render() {
    const {
      id,
      title,
      displayTitle,
      isRefreshing,
      isFetching,
      isPopulated,
      bookFilesError,
      hasBookFiles,
      author,
      previousBook,
      nextBook,
      isSearching,
      onRefreshPress,
      onSearchPress,
      hasPdfFile,
      hasCbzFile,
      cbzFile,
      isConvertingPdf,
      isFlippingPages,
      onConvertPdfPress,
      onFlipPress,
      statistics = {},
      selectedMediaType,
      coveredByVolume,
      onMediaTypeSelect
    } = this.props;

    const isLightNovel = author.library === 'lightNovel';

    const {
      bookFileCount = 0
    } = statistics;

    const {
      isOrganizeModalOpen,
      isRetagModalOpen,
      isEditBookModalOpen,
      isDeleteBookModalOpen,
      isFlipModalOpen,
      selectedTabIndex
    } = this.state;

    return (
      <PageContent title={displayTitle || title}>
        <PageToolbar>
          {/* UI pass (2026-09-24, VD-1): one toolbar in the *arr order -- Refresh, Search | file
              actions | Re-resolve (manga) | Edit, Delete. Suggest a Correction lives in the
              header's Links. */}
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('Refresh')}
              iconName={icons.REFRESH}
              spinningName={icons.REFRESH}
              title={translate('RefreshInformation')}
              isSpinning={isRefreshing}
              onPress={onRefreshPress}
            />

            {
              isLightNovel ?
                <PageToolbarButton
                  label={selectedMediaType === AUDIO ? translate('SearchAudiobook') : translate('SearchEbook')}
                  iconName={icons.SEARCH}
                  isSpinning={isSearching}
                  onPress={onSearchPress}
                /> :
                <PageToolbarButton
                  label={translate('SearchBook')}
                  iconName={icons.SEARCH}
                  isSpinning={isSearching}
                  onPress={onSearchPress}
                />
            }

            <PageToolbarSeparator />

            {/* No Preview Rename for a light novel: its files live in Calibre and in
                Audiobookshelf, which own their own naming, so the preview is always empty.
                Retag still applies -- Mangarr writes the additive tags itself. */}
            {
              isLightNovel ?
                null :
                <PageToolbarButton
                  label={translate('PreviewRename')}
                  iconName={icons.ORGANIZE}
                  isDisabled={!hasBookFiles}
                  onPress={this.onOrganizePress}
                />
            }

            <PageToolbarButton
              label={translate('PreviewRetag')}
              iconName={icons.RETAG}
              isDisabled={!hasBookFiles}
              onPress={this.onRetagPress}
            />

            {
              !isLightNovel && hasPdfFile ?
                <PageToolbarButton
                  label={translate('ConvertPdfToCbz')}
                  iconName={icons.FILE_PDF}
                  title={translate('ConvertPdfToCbzTooltip')}
                  isSpinning={isConvertingPdf}
                  onPress={onConvertPdfPress}
                /> :
                null
            }

            {
              isLightNovel ?
                null :
                <PageToolbarButton
                  label={translate('FlipPageOrder')}
                  iconName={icons.FLIP}
                  title={hasCbzFile ? translate('FlipPageOrderTooltip') : translate('FlipPageOrderDisabledTooltip')}
                  isSpinning={isFlippingPages}
                  isDisabled={!hasCbzFile}
                  onPress={this.onFlipModalPress}
                />
            }

            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('Edit')}
              iconName={icons.EDIT}
              onPress={this.onEditBookPress}
            />

            <PageToolbarButton
              label={translate('Delete')}
              iconName={icons.DELETE}
              onPress={this.onDeleteBookPress}
            />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody innerClassName={styles.innerContentBody}>
          <SwipeHeaderConnector
            className={styles.header}
            nextLink={`/book/${nextBook.titleSlug}`}
            nextComponent={(width) => (
              <BookDetailsHeaderConnector
                bookId={nextBook.id}
                author={author}
                width={width}
              />
            )}
            prevLink={`/book/${previousBook.titleSlug}`}
            prevComponent={(width) => (
              <BookDetailsHeaderConnector
                bookId={previousBook.id}
                author={author}
                width={width}
              />
            )}
            currentComponent={(width) => (
              <BookDetailsHeaderConnector
                bookId={id}
                author={author}
                width={width}
              />
            )}
          >
            <div className={styles.bookNavigationButtons}>
              <IconButton
                className={styles.bookNavigationButton}
                name={icons.ARROW_LEFT}
                size={30}
                title={translate('GoToInterp', [previousBook.title])}
                to={`/book/${previousBook.titleSlug}`}
              />

              <IconButton
                className={styles.bookUpButton}
                name={icons.ARROW_UP}
                size={30}
                title={translate('GoToInterp', [author.authorName])}
                to={`/author/${author.titleSlug}`}
              />

              <IconButton
                className={styles.bookNavigationButton}
                name={icons.ARROW_RIGHT}
                size={30}
                title={translate('GoToInterp', [nextBook.title])}
                to={`/book/${nextBook.titleSlug}`}
              />
            </div>
          </SwipeHeaderConnector>

          <div className={styles.contentContainer}>
            {
              isLightNovel ?
                <div className={styles.mediaTypeTabs}>
                  <Button
                    kind={selectedMediaType === EBOOK ? kinds.PRIMARY : kinds.DEFAULT}
                    size={sizes.MEDIUM}
                    onPress={() => onMediaTypeSelect(EBOOK)}
                  >
                    {translate('Ebook')}
                  </Button>

                  <Button
                    kind={selectedMediaType === AUDIO ? kinds.PRIMARY : kinds.DEFAULT}
                    size={sizes.MEDIUM}
                    onPress={() => onMediaTypeSelect(AUDIO)}
                  >
                    {translate('Audiobook')}
                  </Button>
                </div> :
                null
            }

            {
              isLightNovel && selectedMediaType === AUDIO && coveredByVolume != null ?
                <Label kind={kinds.INFO}>
                  {translate('CoveredByVolume', { volume: coveredByVolume })}
                </Label> :
                null
            }

            {
              !isPopulated && !bookFilesError &&
                <LoadingIndicator />
            }

            {
              !isFetching && bookFilesError &&
                <div>
                  {translate('LoadingBookFilesFailed')}
                </div>
            }

            <Tabs selectedIndex={this.state.tabIndex} onSelect={this.onTabSelect}>
              <TabList
                className={styles.tabList}
              >
                <Tab
                  className={styles.tab}
                  selectedClassName={styles.selectedTab}
                >
                  {translate('History')}
                </Tab>

                <Tab
                  className={styles.tab}
                  selectedClassName={styles.selectedTab}
                >
                  {translate('Search')}
                </Tab>

                <Tab
                  className={styles.tab}
                  selectedClassName={styles.selectedTab}
                >
                  {translate('FilesTotal', [bookFileCount])}
                </Tab>

                {
                  selectedTabIndex === 1 &&
                    <div className={styles.filterIcon}>
                      <InteractiveSearchFilterMenuConnector
                        type="book"
                      />
                    </div>
                }

              </TabList>

              <TabPanel>
                <AuthorHistoryTable
                  authorId={author.id}
                  bookId={id}
                />
              </TabPanel>

              <TabPanel>
                {
                  isLightNovel ?
                    <InteractiveSearchTable
                      bookId={id}
                      type="book"
                      mediaType={selectedMediaType}
                    /> :
                    <InteractiveSearchTable
                      bookId={id}
                      type="book"
                    />
                }
              </TabPanel>

              <TabPanel>
                <BookFileEditorTable
                  authorId={author.id}
                  bookId={id}
                  isLightNovel={isLightNovel}
                />
              </TabPanel>
            </Tabs>
          </div>

          <OrganizePreviewModalConnector
            isOpen={isOrganizeModalOpen}
            authorId={author.id}
            bookId={id}
            onModalClose={this.onOrganizeModalClose}
          />

          <RetagPreviewModalConnector
            isOpen={isRetagModalOpen}
            authorId={author.id}
            bookId={id}
            onModalClose={this.onRetagModalClose}
          />

          {
            hasCbzFile ?
              <FlipPageOrderModal
                isOpen={isFlipModalOpen}
                bookFileId={cbzFile.id}
                bookTitle={title}
                onFlipPress={onFlipPress}
                onModalClose={this.onFlipModalClose}
              /> :
              null
          }

          <EditBookModalConnector
            isOpen={isEditBookModalOpen}
            bookId={id}
            authorId={author.id}
            onModalClose={this.onEditBookModalClose}
            onDeleteAuthorPress={this.onDeleteBookPress}
          />

          <DeleteBookModal
            isOpen={isDeleteBookModalOpen}
            bookId={id}
            authorSlug={author.titleSlug}
            onModalClose={this.onDeleteBookModalClose}
          />

        </PageContentBody>
      </PageContent>
    );
  }
}

BookDetails.propTypes = {
  id: PropTypes.number.isRequired,
  titleSlug: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  displayTitle: PropTypes.string,
  seriesTitle: PropTypes.string.isRequired,
  pageCount: PropTypes.number,
  overview: PropTypes.string,
  releaseDate: PropTypes.string.isRequired,
  ratings: PropTypes.object.isRequired,
  images: PropTypes.arrayOf(PropTypes.object).isRequired,
  links: PropTypes.arrayOf(PropTypes.object).isRequired,
  statistics: PropTypes.object.isRequired,
  monitored: PropTypes.bool.isRequired,
  shortDateFormat: PropTypes.string.isRequired,
  isSaving: PropTypes.bool.isRequired,
  isRefreshing: PropTypes.bool,
  isSearching: PropTypes.bool,
  isFetching: PropTypes.bool,
  isPopulated: PropTypes.bool,
  bookFilesError: PropTypes.object,
  hasBookFiles: PropTypes.bool.isRequired,
  hasPdfFile: PropTypes.bool,
  hasCbzFile: PropTypes.bool,
  cbzFile: PropTypes.object,
  isConvertingPdf: PropTypes.bool,
  isFlippingPages: PropTypes.bool,
  onConvertPdfPress: PropTypes.func.isRequired,
  onFlipPress: PropTypes.func.isRequired,
  author: PropTypes.object,
  previousBook: PropTypes.object,
  nextBook: PropTypes.object,
  isSmallScreen: PropTypes.bool.isRequired,
  onMonitorTogglePress: PropTypes.func.isRequired,
  onRefreshPress: PropTypes.func,
  onSearchPress: PropTypes.func.isRequired,
  selectedMediaType: PropTypes.string.isRequired,
  coveredByVolume: PropTypes.number,
  onMediaTypeSelect: PropTypes.func.isRequired
};

BookDetails.defaultProps = {
  isSaving: false
};

export default BookDetails;
