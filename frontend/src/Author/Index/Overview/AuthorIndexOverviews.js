import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { Grid, WindowScroller } from 'react-virtualized';
import AuthorIndexItemConnector from 'Author/Index/AuthorIndexItemConnector';
import { isLightNovelStacked } from 'Author/Index/ProgressBar/AuthorIndexProgressBar';
import Measure from 'Components/Measure';
import dimensions from 'Styles/Variables/dimensions';
import getIndexOfFirstCharacter from 'Utilities/Array/getIndexOfFirstCharacter';
import hasDifferentItemsOrOrder from 'Utilities/Object/hasDifferentItemsOrOrder';
import AuthorIndexOverview from './AuthorIndexOverview';
import styles from './AuthorIndexOverviews.css';

// Poster container dimensions
const columnPadding = parseInt(dimensions.authorIndexColumnPadding);
const columnPaddingSmallScreen = parseInt(dimensions.authorIndexColumnPaddingSmallScreen);
const progressBarHeight = parseInt(dimensions.progressBarSmallHeight);
const detailedProgressBarHeight = parseInt(dimensions.progressBarMediumHeight);
const lightNovelProgressBarHeight = parseInt(dimensions.progressBarLightNovelHeight);
const lightNovelStackedProgressBarHeight = parseInt(dimensions.progressBarLightNovelStackedHeight);

function calculatePosterWidth(posterSize, isSmallScreen) {
  const maxiumPosterWidth = isSmallScreen ? 192 : 202;

  if (posterSize === 'large') {
    return maxiumPosterWidth;
  }

  if (posterSize === 'medium') {
    return Math.floor(maxiumPosterWidth * 0.75);
  }

  return Math.floor(maxiumPosterWidth * 0.5);
}

// A light-novel poster shows two format rows instead of the bar, and a grid row is as tall
// as its tallest cell, so one light novel in the list sets the budget for every row.
function hasLightNovels(items) {
  return items.some((item) => item.library === 'lightNovel');
}

function calculateProgressBarHeight(detailedProgressBar, hasLightNovelItems, posterWidth) {
  if (hasLightNovelItems) {
    return isLightNovelStacked(posterWidth) ? lightNovelStackedProgressBarHeight : lightNovelProgressBarHeight;
  }

  return detailedProgressBar ? detailedProgressBarHeight : progressBarHeight;
}

function calculateRowHeight(posterWidth, posterHeight, sortKey, isSmallScreen, overviewOptions, hasLightNovelItems) {
  const {
    detailedProgressBar
  } = overviewOptions;

  const heights = [
    posterHeight,
    calculateProgressBarHeight(detailedProgressBar, hasLightNovelItems, posterWidth),
    isSmallScreen ? columnPaddingSmallScreen : columnPadding
  ];

  return heights.reduce((acc, height) => acc + height, 0);
}

function calculatePosterHeight(posterWidth) {
  return posterWidth;
}

class AuthorIndexOverviews extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      width: 0,
      columnCount: 1,
      posterWidth: 238,
      posterHeight: 238,
      rowHeight: calculateRowHeight(238, 238, null, props.isSmallScreen, {}, hasLightNovels(props.items)),
      scrollRestored: false
    };

    this._grid = null;
  }

  componentDidUpdate(prevProps, prevState) {
    const {
      items,
      sortKey,
      overviewOptions,
      jumpToCharacter,
      scrollTop,
      isEditorActive,
      selectedState
    } = this.props;

    const {
      width,
      rowHeight,
      scrollRestored
    } = this.state;

    if (prevProps.sortKey !== sortKey ||
        prevProps.overviewOptions !== overviewOptions ||
        hasLightNovels(prevProps.items) !== hasLightNovels(items)) {
      this.calculateGrid();
    }

    if (this._grid &&
        (prevState.width !== width ||
            prevState.rowHeight !== rowHeight ||
            hasDifferentItemsOrOrder(prevProps.items, items) ||
            prevProps.isEditorActive !== isEditorActive ||
            prevProps.selectedState !== selectedState ||
            prevProps.overviewOptions.showTitle !== overviewOptions.showTitle)) {
      // recomputeGridSize also forces Grid to discard its cache of rendered cells
      this._grid.recomputeGridSize();
    }

    if (this._grid && scrollTop !== 0 && !scrollRestored) {
      this.setState({ scrollRestored: true });
      this._grid.scrollToPosition({ scrollTop });
    }

    if (jumpToCharacter != null && jumpToCharacter !== prevProps.jumpToCharacter) {
      const index = getIndexOfFirstCharacter(items, sortKey, jumpToCharacter);

      if (this._grid && index != null) {

        this._grid.scrollToCell({
          rowIndex: index,
          columnIndex: 0
        });
      }
    }
  }

  //
  // Control

  setGridRef = (ref) => {
    this._grid = ref;
  };

  calculateGrid = (width = this.state.width, isSmallScreen) => {
    const {
      items,
      sortKey,
      overviewOptions
    } = this.props;

    const posterWidth = calculatePosterWidth(overviewOptions.size, isSmallScreen);
    const posterHeight = calculatePosterHeight(posterWidth);
    const rowHeight = calculateRowHeight(posterWidth, posterHeight, sortKey, isSmallScreen, overviewOptions, hasLightNovels(items));

    this.setState({
      width,
      posterWidth,
      posterHeight,
      rowHeight
    });
  };

  cellRenderer = ({ key, rowIndex, style }) => {
    const {
      items,
      sortKey,
      overviewOptions,
      showRelativeDates,
      shortDateFormat,
      longDateFormat,
      timeFormat,
      isSmallScreen,
      selectedState,
      isEditorActive,
      onSelectedChange
    } = this.props;

    const {
      posterWidth,
      posterHeight,
      rowHeight
    } = this.state;

    const author = items[rowIndex];

    if (!author) {
      return null;
    }

    return (
      <div
        key={key}
        style={style}
      >
        <AuthorIndexItemConnector
          key={author.id}
          component={AuthorIndexOverview}
          sortKey={sortKey}
          posterWidth={posterWidth}
          posterHeight={posterHeight}
          rowHeight={rowHeight}
          overviewOptions={overviewOptions}
          showRelativeDates={showRelativeDates}
          shortDateFormat={shortDateFormat}
          longDateFormat={longDateFormat}
          timeFormat={timeFormat}
          isSmallScreen={isSmallScreen}
          authorId={author.id}
          qualityProfileId={author.qualityProfileId}
          metadataProfileId={author.metadataProfileId}
          isSelected={selectedState[author.id]}
          onSelectedChange={onSelectedChange}
          isEditorActive={isEditorActive}
        />
      </div>
    );
  };

  //
  // Listeners

  onMeasure = ({ width }) => {
    this.calculateGrid(width, this.props.isSmallScreen);
  };

  //
  // Render

  render() {
    const {
      items,
      isSmallScreen,
      scroller
    } = this.props;

    const {
      width,
      rowHeight
    } = this.state;

    return (
      <Measure
        onMeasure={this.onMeasure}
      >
        <WindowScroller
          scrollElement={isSmallScreen ? undefined : scroller}
        >
          {({ height, registerChild, onChildScroll, scrollTop }) => {
            if (!height) {
              return <div />;
            }

            return (
              <div ref={registerChild}>
                <Grid
                  ref={this.setGridRef}
                  className={styles.grid}
                  autoHeight={true}
                  height={height}
                  columnCount={1}
                  columnWidth={width}
                  rowCount={items.length}
                  rowHeight={rowHeight}
                  width={width}
                  onScroll={onChildScroll}
                  scrollTop={scrollTop}
                  overscanRowCount={2}
                  cellRenderer={this.cellRenderer}
                  onSectionRendered={this.onSectionRendered}
                  scrollToAlignment={'start'}
                  isScrollingOptOut={true}
                />
              </div>
            );
          }
          }
        </WindowScroller>
      </Measure>
    );
  }
}

AuthorIndexOverviews.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  sortKey: PropTypes.string,
  overviewOptions: PropTypes.object.isRequired,
  scrollTop: PropTypes.number.isRequired,
  jumpToCharacter: PropTypes.string,
  scroller: PropTypes.instanceOf(Element).isRequired,
  showRelativeDates: PropTypes.bool.isRequired,
  shortDateFormat: PropTypes.string.isRequired,
  longDateFormat: PropTypes.string.isRequired,
  isSmallScreen: PropTypes.bool.isRequired,
  timeFormat: PropTypes.string.isRequired,
  selectedState: PropTypes.object.isRequired,
  onSelectedChange: PropTypes.func.isRequired,
  isEditorActive: PropTypes.bool.isRequired
};

export default AuthorIndexOverviews;
