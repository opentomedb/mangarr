import classNames from 'classnames';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import AuthorPoster from 'Author/AuthorPoster';
import AuthorIndexProgressBar from 'Author/Index/ProgressBar/AuthorIndexProgressBar';
import AuthorIndexLibraryTabs from 'Author/Index/Tabs/AuthorIndexLibraryTabs';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds, sizes } from 'Helpers/Props';
import AddNewAuthorModal from 'Search/Author/AddNewAuthorModal';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import styles from './Collections.css';

function MemberCard({ member, author, defaults, isAdding, onAddPress }) {
  const statistics = (author && author.statistics) || {};
  let images = (author && author.images) || [];

  // A member outside the library has no MediaCover; the API sends the catalogue's (or
  // AniList's) cover instead, in the shape AuthorPoster reads.
  if (!author && member.remotePoster) {
    images = [{ coverType: 'poster', url: member.remotePoster }];
  }

  // UI pass (2026-09-24, CO-5): a member in the library shows the same progress bar(s) as its
  // index poster (one bar for manga, Ebook + Audiobook rows for a light novel); one outside it
  // shows the catalogue facts, or Not in Library.
  const outsideLine = [
    member.volumeCount ? translate('CountVolumes', { count: member.volumeCount }) : null,
    member.publisher || null,
    member.medium ? titleCase(member.medium.replace('_', ' ')) : null,

    // Line safety (2026-09-28): the Add results' "Spin-off of" label.
    member.spinOffOf ? translate('SpinOffOf', { name: member.spinOffOf }) : null
  ].filter(Boolean).join(' · ') || translate('NotInLibrary');

  // react-lazyload's default scroll detection is window-based; PageContentBody scrolls its own
  // inner Scroller div instead, so the lazy trigger for a card below the fold never fires and it
  // is stuck on the placeholder. Every other poster/cover grid in the app (AuthorIndexPoster,
  // BookIndexPoster, the Overview views, the add-new search results, ...) already disables lazy
  // for exactly this reason -- Collections was the one place still requesting it.
  const poster = (
    <AuthorPoster
      className={styles.poster}
      images={images}
      size={250}
      lazy={false}
      overflow={true}
    />
  );

  return (
    <div
      className={classNames(styles.card, !member.inLibrary && styles.missing, member.isRoot && styles.root)}
      title={member.isRoot ? translate('MainSeries') : undefined}
    >
      {
        member.inLibrary ?
          <Link className={styles.posterLink} to={`/author/${member.titleSlug}`}>{poster}</Link> :
          <div className={styles.posterLink}>{poster}</div>
      }

      {
        member.inLibrary && author ?
          <AuthorIndexProgressBar
            library={author.library}
            monitored={author.monitored}
            status={author.status}
            bookCount={statistics.bookCount || 0}
            availableBookCount={statistics.availableBookCount || 0}
            bookFileCount={statistics.bookFileCount || 0}
            totalBookCount={statistics.totalBookCount || 0}
            audioBookCount={statistics.audioBookCount || 0}
            audioBookFileCount={statistics.audioBookFileCount || 0}
            audioAvailable={!!author.audioAvailable}
            posterWidth={140}
            detailedProgressBar={false}
          /> :
          null
      }

      <div className={styles.title}>
        {
          member.inLibrary ?
            <Link to={`/author/${member.titleSlug}`}>{member.name}</Link> :
            member.name
        }
      </div>

      {
        member.inLibrary ?
          null :
          <div className={styles.sub}>{outsideLine}</div>
      }

      {
        !member.inLibrary && defaults ?
          <Button
            className={styles.addButton}
            kind={kinds.PRIMARY}
            size={sizes.SMALL}
            isDisabled={isAdding}
            onPress={onAddPress}
          >
            {translate('Add')}
          </Button> :
          null
      }
    </div>
  );
}

MemberCard.propTypes = {
  member: PropTypes.object.isRequired,
  author: PropTypes.object,
  defaults: PropTypes.object,
  isAdding: PropTypes.bool.isRequired,
  onAddPress: PropTypes.func.isRequired
};

class Collections extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      addMemberId: null
    };
  }

  //
  // Listeners

  // UI pass (2026-09-24, CO-4): a single Add opens the standard Add modal, the same one Add New
  // opens for a search result; Add N Missing stays a one-click bulk add.
  onAddMemberPress = (member) => {
    this.setState({ addMemberId: member.foreignAuthorId });
  };

  onAddMemberModalClose = () => {
    this.setState({ addMemberId: null });
  };

  onAddMember = (options) => {
    const member = this.findMember(this.state.addMemberId);

    if (member) {
      this.props.onAddMemberPress(member, options);
    }
  };

  //
  // Control

  findMember(foreignAuthorId) {
    for (const collection of this.props.items) {
      const member = collection.cards.find((c) => c.foreignAuthorId === foreignAuthorId);

      if (member) {
        return member;
      }
    }

    return null;
  }

  //
  // Render

  render() {
    const {
      isFetching,
      isPopulated,
      error,
      items,
      isAdding,
      addError,
      authorsById,
      library,
      mangaCount,
      lightNovelCount,
      onLibrarySelect,
      onAddMembersPress,
      onSearchAllPress
    } = this.props;

    // The member the Add modal is open for; once the add lands (the refetch shows it in the
    // library) the modal closes itself, as a search result's does.
    const addMember = this.findMember(this.state.addMemberId);

    return (
      <PageContent title={translate('Collections')}>

        {
          isPopulated ?
            <AuthorIndexLibraryTabs
              library={library}
              mangaCount={mangaCount}
              lightNovelCount={lightNovelCount}
              onLibrarySelect={onLibrarySelect}
            /> :
            null
        }

        <PageContentBody>
          {
            isFetching && !isPopulated ?
              <LoadingIndicator /> :
              null
          }

          {
            !isFetching && !!error ?
              <Alert kind={kinds.DANGER}>{translate('UnableToLoadCollections')}</Alert> :
              null
          }

          {
            addError ?
              <Alert kind={kinds.DANGER}>{translate('AddingFailed', { message: addError.responseJSON && addError.responseJSON.message ? addError.responseJSON.message : addError.statusText })}</Alert> :
              null
          }

          {
            isPopulated && !error && !items.length ?
              <Alert kind={kinds.INFO}>
                {translate('NoCollectionsYet')}
              </Alert> :
              null
          }

          {
            isPopulated && !error ?
              items.map((collection) => {
                const cards = collection.cards;
                const inLibrary = cards.filter((c) => c.inLibrary);
                const missing = cards.filter((c) => !c.inLibrary);

                return (
                  <div
                    key={collection.foreignAuthorId}
                    id={collection.foreignAuthorId}
                    className={styles.collection}
                  >
                    <div className={styles.header}>
                      <h2 className={styles.name}>
                        {
                          collection.authorId ?
                            <Link to={`/author/${collection.titleSlug}`}>{collection.name}</Link> :
                            collection.name
                        }
                      </h2>

                      <span className={styles.counts}>
                        {translate('CountInLibrary', { count: inLibrary.length, total: cards.length })}
                      </span>

                      <div className={styles.actions}>
                        {
                          missing.length && collection.defaults ?
                            <Button
                              kind={kinds.PRIMARY}
                              size={sizes.SMALL}
                              isDisabled={isAdding}
                              onPress={() => onAddMembersPress(collection, missing)}
                            >
                              {translate('AddCountMissing', { count: missing.length })}
                            </Button> :
                            null
                        }

                        {
                          inLibrary.length ?
                            <Button
                              size={sizes.SMALL}
                              onPress={() => onSearchAllPress(collection, inLibrary)}
                            >
                              {translate('SearchAll')}
                            </Button> :
                            null
                        }
                      </div>
                    </div>

                    <div className={styles.members}>
                      {
                        cards.map((member) => {
                          return (
                            <MemberCard
                              key={member.foreignAuthorId}
                              member={member}
                              author={member.authorId ? authorsById[member.authorId] : null}
                              defaults={collection.defaults}
                              isAdding={isAdding}
                              onAddPress={() => this.onAddMemberPress(member)}
                            />
                          );
                        })
                      }
                    </div>
                  </div>
                );
              }) :
              null
          }
        </PageContentBody>

        {
          addMember ?
            <AddNewAuthorModal
              isOpen={!addMember.inLibrary}
              foreignAuthorId={addMember.foreignAuthorId}
              authorName={addMember.name}
              folder={addMember.name}
              images={addMember.remotePoster ? [{ coverType: 'poster', url: addMember.remotePoster }] : []}
              library={library}
              isAdding={isAdding}
              addError={addError}
              onAddAuthor={this.onAddMember}
              onModalClose={this.onAddMemberModalClose}
            /> :
            null
        }
      </PageContent>
    );
  }
}

Collections.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  isAdding: PropTypes.bool.isRequired,
  addError: PropTypes.object,
  authorsById: PropTypes.object.isRequired,
  library: PropTypes.string.isRequired,
  mangaCount: PropTypes.number.isRequired,
  lightNovelCount: PropTypes.number.isRequired,
  onLibrarySelect: PropTypes.func.isRequired,
  onAddMembersPress: PropTypes.func.isRequired,
  onAddMemberPress: PropTypes.func.isRequired,
  onSearchAllPress: PropTypes.func.isRequired
};

export default Collections;
