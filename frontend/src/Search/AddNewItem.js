import PropTypes from 'prop-types';
import React, { Component } from 'react';
import AuthorIndexLibraryTabs from 'Author/Index/Tabs/AuthorIndexLibraryTabs';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { icons, kinds } from 'Helpers/Props';
import { LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import AddNewAuthorSearchResultConnector from './Author/AddNewAuthorSearchResultConnector';
import AddNewBookSearchResultConnector from './Book/AddNewBookSearchResultConnector';
import styles from './AddNewItem.css';

class AddNewItem extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      term: props.term || '',
      isFetching: false
    };
  }

  componentDidMount() {
    const term = this.state.term;

    if (term) {
      this.props.onSearchChange(term);
    }
  }

  componentDidUpdate(prevProps) {
    const {
      term,
      isFetching
    } = this.props;

    if (term && term !== prevProps.term) {
      this.setState({
        term,
        isFetching: true
      });
      this.props.onSearchChange(term);
    } else if (isFetching !== prevProps.isFetching) {
      this.setState({
        isFetching
      });
    }
  }

  //
  // Listeners

  onSearchInputChange = ({ value }) => {
    const hasValue = !!value.trim();

    this.setState({ term: value, isFetching: hasValue }, () => {
      if (hasValue) {
        this.props.onSearchChange(value);
      } else {
        this.props.onClearSearch();
      }
    });
  };

  onClearSearchPress = () => {
    this.setState({ term: '' });
    this.props.onClearSearch();
  };

  onLibraryPress = (library) => {
    this.props.onLibraryChange(library);

    // The connector only knows the URL term; re-run whatever is typed in the box.
    if (this.state.term.trim() && this.state.term !== this.props.term) {
      this.setState({ isFetching: true });
      this.props.onSearchChange(this.state.term);
    }
  };

  //
  // Render

  render() {
    const {
      error,
      items,
      hasExistingAuthors,
      library,
      openTomeUrl
    } = this.props;

    const isLightNovel = library === 'lightNovel';

    const term = this.state.term;
    const isFetching = this.state.isFetching;

    return (
      <PageContent title={translate('AddNewItem')}>
        {/* UI pass (2026-09-24, AN-6/V4): the shared tab row, without counts. */}
        <AuthorIndexLibraryTabs
          library={isLightNovel ? LIGHT_NOVEL : MANGA}
          onLibrarySelect={this.onLibraryPress}
        />

        <PageContentBody>

          <div className={styles.searchContainer}>
            <div className={styles.searchIconContainer}>
              <Icon
                name={icons.SEARCH}
                size={20}
              />
            </div>

            <TextInput
              className={styles.searchInput}
              name="searchBox"
              value={term}
              placeholder={translate('SearchBoxPlaceHolder')}
              autoFocus={true}
              onChange={this.onSearchInputChange}
            />

            <Button
              className={styles.clearLookupButton}
              onPress={this.onClearSearchPress}
            >
              <Icon
                name={icons.REMOVE}
                size={20}
              />
            </Button>
          </div>

          {
            isFetching &&
              <LoadingIndicator />
          }

          {
            !isFetching && !!error ?
              <div className={styles.message}>
                <div className={styles.helpText}>
                  {translate('FailedLoadingSearchResults')}
                </div>

                <Alert kind={kinds.WARNING}>{getErrorMessage(error)}</Alert>

                <div>
                  <Link to="https://wiki.servarr.com/readarr/troubleshooting#invalid-response-received-from-metadata-api">
                    {translate('WhySearchesCouldBeFailing')}
                  </Link>
                </div>
              </div> : null
          }

          {
            !isFetching && !error && !!items.length &&
              <div className={styles.searchResults}>
                {
                  items.map((item) => {
                    if (item.author) {
                      const author = item.author;
                      return (
                        <AddNewAuthorSearchResultConnector
                          key={item.id}
                          {...author}
                        />
                      );
                    } else if (item.book) {
                      const book = item.book;
                      return (
                        <AddNewBookSearchResultConnector
                          key={item.id}
                          isExistingBook={'id' in book && book.id !== 0}
                          isExistingAuthor={'id' in book.author && book.author.id !== 0}
                          {...book}
                        />
                      );
                    }
                    return null;
                  })
                }
              </div>
          }

          {
            !isFetching && !error && !items.length && !!term &&
              <div className={styles.message}>
                <div className={styles.noResults}>
                  {translate('CouldntFindAnyResultsForTerm', [term])}
                </div>
                {
                  isLightNovel ?
                    <div className={styles.catalogueMiss}>
                      {translate('LightNovelNotInCatalogue')}
                      {' '}
                      {
                        openTomeUrl ?
                          <Link to={openTomeUrl}>
                            {translate('SuggestItOnOpenTome')}
                          </Link> :
                          translate('SuggestItOnOpenTome')
                      }
                    </div> :
                    <div>
                      {translate('TryAnotherTitleHint')}
                    </div>
                }
              </div>
          }

          {
            term ?
              null :
              <div className={styles.message}>
                <div className={styles.helpText}>
                  {translate('ItsEasyToAddANewAuthorOrBookJustStartTypingTheNameOfTheItemYouWantToAdd')}
                </div>
              </div>
          }

          {
            !term && !hasExistingAuthors ?
              <div className={styles.message}>
                <div className={styles.noAuthorsText}>
                  {translate('NoSeriesAddedYet')}
                </div>
                <div>
                  <Button
                    to="/add/import"
                    kind={kinds.PRIMARY}
                  >
                    {translate('ImportExistingSeries')}
                  </Button>
                </div>
              </div> :
              null
          }

          <div />
        </PageContentBody>
      </PageContent>
    );
  }
}

AddNewItem.propTypes = {
  term: PropTypes.string,
  isFetching: PropTypes.bool.isRequired,
  error: PropTypes.object,
  isAdding: PropTypes.bool.isRequired,
  addError: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  hasExistingAuthors: PropTypes.bool.isRequired,
  library: PropTypes.string.isRequired,
  openTomeUrl: PropTypes.string,
  onSearchChange: PropTypes.func.isRequired,
  onClearSearch: PropTypes.func.isRequired,
  onLibraryChange: PropTypes.func.isRequired
};

export default AddNewItem;
