import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { icons, kinds, sizes, sortDirections } from 'Helpers/Props';
import { AUDIO, EBOOK } from 'Utilities/Book/mediaTypes';
import hasDifferentItemsOrOrder from 'Utilities/Object/hasDifferentItemsOrOrder';
import translate from 'Utilities/String/translate';
import getToggledRange from 'Utilities/Table/getToggledRange';
import BookRowConnector from './BookRowConnector';
import PlaceholderBookRow from './PlaceholderBookRow';
import styles from './AuthorDetailsSeason.css';

// Sonarr on a phone: the secondary columns step aside so the title, the status and the search
// buttons fit a 390 px screen. Render-time only -- the saved column choice is never changed.
const SMALL_SCREEN_HIDDEN_COLUMNS = ['series', 'releaseDate', 'pageCount', 'rating', 'indexerFlags'];

function hideSecondaryColumns(columns) {
  return columns.map((column) => {
    return SMALL_SCREEN_HIDDEN_COLUMNS.includes(column.name) ? { ...column, isVisible: false } : column;
  });
}

class AuthorDetailsSeason extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      lastToggledBook: null
    };
  }

  componentDidMount() {
    this.props.setSelectedState(this.props.items);
  }

  componentDidUpdate(prevProps) {
    const {
      items,
      sortKey,
      sortDirection,
      setSelectedState
    } = this.props;

    if (sortKey !== prevProps.sortKey ||
        sortDirection !== prevProps.sortDirection ||
        hasDifferentItemsOrOrder(prevProps.items, items)
    ) {
      setSelectedState(items);
    }
  }

  //
  // Listeners

  onMonitorBookPress = (bookId, monitored, { shiftKey }) => {
    const lastToggled = this.state.lastToggledBook;
    const bookIds = [bookId];

    if (shiftKey && lastToggled) {
      const { lower, upper } = getToggledRange(this.props.items, bookId, lastToggled);
      const items = this.props.items;

      for (let i = lower; i < upper; i++) {
        bookIds.push(items[i].id);
      }
    }

    this.setState({ lastToggledBook: bookId });

    this.props.onMonitorBookPress(_.uniq(bookIds), monitored);
  };

  onSelectedChange = ({ id, value, shiftKey = false }) => {
    const {
      onSelectedChange,
      items
    } = this.props;

    return onSelectedChange(items, id, value, shiftKey);
  };

  onMonitorEditionPress = (bookId, editionId, mediaType, monitored) => {
    this.props.onMonitorEditionPress(bookId, editionId, mediaType, monitored);
  };

  // On a phone the table options list the columns as shown (the hidden ones unticked); a save from
  // there keeps each hidden column's saved choice unless it was ticked on.
  onTableOptionChange = (payload) => {
    const {
      columns,
      isSmallScreen,
      onTableOptionChange
    } = this.props;

    if (!isSmallScreen || !payload.columns) {
      onTableOptionChange(payload);
      return;
    }

    onTableOptionChange({
      ...payload,
      columns: payload.columns.map((column) => {
        const saved = columns.find((c) => c.name === column.name);

        return SMALL_SCREEN_HIDDEN_COLUMNS.includes(column.name) && saved ?
          { ...column, isVisible: column.isVisible || saved.isVisible } :
          column;
      })
    });
  };

  //
  // Render

  render() {
    const {
      items,
      isEditorActive,
      isSmallScreen,
      sortKey,
      sortDirection,
      onSortPress,
      selectedState,
      totalVolumes = 0,
      library,
      audioAvailable,
      statistics,
      selectedMediaType,
      downloadingMediaTypes,
      volumeCounts,
      authorMonitored,
      onMediaTypeSelect
    } = this.props;

    const columns = isSmallScreen ? hideSecondaryColumns(this.props.columns) : this.props.columns;
    const isLightNovel = library === 'lightNovel';
    const {
      bookCount = 0,
      bookFileCount = 0,
      audioBookCount = 0,
      audioBookFileCount = 0
    } = statistics;
    const audioNotYet = !audioAvailable && audioBookFileCount === 0;

    // Sonarr's season header colours: green complete, orange unmonitored, red missing.
    let volumeCountKind = kinds.DANGER;

    if (volumeCounts.released > 0 && volumeCounts.files === volumeCounts.released) {
      volumeCountKind = kinds.SUCCESS;
    } else if (!authorMonitored) {
      volumeCountKind = kinds.WARNING;
    }

    let titleColumns = columns;
    if (!isEditorActive) {
      titleColumns = columns.filter((x) => x.name !== 'select');
    }

    // The Audio tab's rows show the audiobook's runtime in the Pages column (BookRow), so the
    // header says so. Sorting still uses the column's key.
    if (isLightNovel && selectedMediaType === AUDIO) {
      titleColumns = titleColumns.map((column) => {
        return column.name === 'pageCount' ? { ...column, label: translate('Runtime') } : column;
      });
    }

    // Placeholder volumes (volumes within totalVolumes not in items) — display-only
    // "Coming Soon" rows for volumes that exist upstream but aren't in the library yet.
    const existingVolumes = new Set(items.map((item) => item.volumeNumber));
    const placeholderVolumes = [];
    for (let volumeNumber = 1; volumeNumber <= totalVolumes; volumeNumber++) {
      if (!existingVolumes.has(volumeNumber)) {
        placeholderVolumes.push(volumeNumber);
      }
    }

    // One flat list: real volumes in the selector's sort order, placeholders interleaved
    // at their volume number. Interleaving only makes sense in volume order (the 'title'
    // sort — its authorDetails predicate is volumeNumber); under any other sort the
    // placeholders have no value to sort by and sink to the bottom instead.
    let rows = [
      ...items.map((item) => ({ key: item.id, item })),
      ...placeholderVolumes.map((volumeNumber) => ({ key: `placeholder-${volumeNumber}`, volumeNumber }))
    ];

    if (sortKey === 'title') {
      rows = _.orderBy(
        rows,
        (row) => (row.item ? row.item.volumeNumber || 0 : row.volumeNumber),
        sortDirection === sortDirections.DESCENDING ? 'desc' : 'asc'
      );
    }

    return (
      <div
        className={styles.bookType}
      >
        {
          isLightNovel ?
            <div className={styles.mediaTypeHeader}>
              <div className={styles.mediaTypeTitle}>
                {translate('Books')}
                <span className={styles.mediaTypeCount}>
                  ({totalVolumes || items.length})
                </span>
              </div>

              <div className={styles.mediaTypeTabs}>
                <Button
                  kind={selectedMediaType === EBOOK ? kinds.PRIMARY : kinds.DEFAULT}
                  size={sizes.MEDIUM}
                  onPress={() => onMediaTypeSelect(EBOOK)}
                >
                  {translate('Ebook')} <span className={styles.tabCount}>{bookFileCount} / {bookCount}</span>
                  {
                    downloadingMediaTypes.includes(EBOOK) ?
                      <Icon
                        className={styles.tabDownloading}
                        name={icons.DOWNLOADING}
                        title={translate('Downloading')}
                      /> :
                      null
                  }
                </Button>

                <Button
                  kind={selectedMediaType === AUDIO ? kinds.PRIMARY : kinds.DEFAULT}
                  size={sizes.MEDIUM}
                  title={audioNotYet ? translate('AudioNotYetHelpText') : undefined}
                  onPress={() => onMediaTypeSelect(AUDIO)}
                >
                  {translate('Audiobook')} <span className={styles.tabCount}>{audioNotYet ? translate('NotYet') : `${audioBookFileCount} / ${audioBookCount}`}</span>
                  {
                    downloadingMediaTypes.includes(AUDIO) ?
                      <Icon
                        className={styles.tabDownloading}
                        name={icons.DOWNLOADING}
                        title={translate('Downloading')}
                      /> :
                      null
                  }
                </Button>
              </div>
            </div> :
            <div className={styles.mediaTypeHeader}>
              <div className={styles.mediaTypeTitle}>
                {translate('Books')}

                <Label
                  className={styles.volumeCount}
                  kind={volumeCountKind}
                  size={sizes.LARGE}
                  title={translate('VolumeCountTooltip', { files: volumeCounts.files, released: volumeCounts.released })}
                >
                  {volumeCounts.files} / {volumeCounts.released}
                </Label>
              </div>
            </div>
        }

        <div className={styles.books}>
          <Table
            columns={titleColumns}
            sortKey={sortKey}
            sortDirection={sortDirection}
            onSortPress={onSortPress}
            onTableOptionChange={this.onTableOptionChange}
          >
            <TableBody>
              {
                rows.map((row) => {
                  if (row.item) {
                    return (
                      <BookRowConnector
                        key={row.key}
                        columns={columns}
                        {...row.item}
                        onMonitorBookPress={this.onMonitorBookPress}
                        onMonitorEditionPress={this.onMonitorEditionPress}
                        isEditorActive={isEditorActive}
                        isSelected={selectedState[row.item.id]}
                        onSelectedChange={this.onSelectedChange}
                      />
                    );
                  }

                  return (
                    <PlaceholderBookRow
                      key={row.key}
                      volumeNumber={row.volumeNumber}
                      columns={columns}
                    />
                  );
                })
              }
            </TableBody>
          </Table>
        </div>
      </div>
    );
  }
}

AuthorDetailsSeason.propTypes = {
  sortKey: PropTypes.string,
  sortDirection: PropTypes.oneOf(sortDirections.all),
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  isEditorActive: PropTypes.bool.isRequired,
  isSmallScreen: PropTypes.bool.isRequired,
  selectedState: PropTypes.object.isRequired,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  onTableOptionChange: PropTypes.func.isRequired,
  onExpandPress: PropTypes.func.isRequired,
  setSelectedState: PropTypes.func.isRequired,
  onSelectedChange: PropTypes.func.isRequired,
  onSortPress: PropTypes.func.isRequired,
  onMonitorBookPress: PropTypes.func.isRequired,
  library: PropTypes.string,
  audioAvailable: PropTypes.bool,
  statistics: PropTypes.object,
  selectedMediaType: PropTypes.string.isRequired,
  downloadingMediaTypes: PropTypes.arrayOf(PropTypes.string),
  volumeCounts: PropTypes.object.isRequired,
  authorMonitored: PropTypes.bool.isRequired,
  onMediaTypeSelect: PropTypes.func.isRequired,
  onMonitorEditionPress: PropTypes.func.isRequired,
  uiSettings: PropTypes.object.isRequired
};

AuthorDetailsSeason.defaultProps = {
  statistics: {},
  downloadingMediaTypes: []
};

export default AuthorDetailsSeason;
