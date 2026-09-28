import PropTypes from 'prop-types';
import React, { Component } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Measure from 'Components/Measure';
import { icons, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import QualityProfileItemDragPreview from './QualityProfileItemDragPreview';
import QualityProfileItemDragSource from './QualityProfileItemDragSource';
import styles from './QualityProfileItems.css';

// Quality ids are stable (see src/NzbDrone.Core/Qualities/Quality.cs); anything not listed here
// is a manga archive format, Unknown included.
const QUALITY_CLASS_BY_ID = {
  6: 'QualityClassEbook',
  7: 'QualityClassEbook',
  8: 'QualityClassEbook',
  10: 'QualityClassAudio',
  11: 'QualityClassAudio',
  12: 'QualityClassAudio',
  13: 'QualityClassAudio'
};

function qualityClassKey({ quality, items }) {
  const first = quality || (items && items.length ? items[0].quality : null);

  return (first && QUALITY_CLASS_BY_ID[first.id]) || 'QualityClassArchives';
}

class QualityProfileItems extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      qualitiesHeight: 0,
      qualitiesHeightEditGroups: 0
    };
  }

  //
  // Listeners

  onMeasure = ({ height }) => {
    if (this.props.editGroups) {
      this.setState({
        qualitiesHeightEditGroups: height
      });
    } else {
      this.setState({ qualitiesHeight: height });
    }
  };

  onToggleEditGroupsMode = () => {
    this.props.onToggleEditGroupsMode();
  };

  //
  // Render

  render() {
    const {
      editGroups,
      dropQualityIndex,
      dropPosition,
      qualityProfileItems,
      errors,
      warnings,
      ...otherProps
    } = this.props;

    const {
      qualitiesHeight,
      qualitiesHeightEditGroups
    } = this.state;

    const isDragging = dropQualityIndex !== null;
    const isDraggingUp = isDragging && dropPosition === 'above';
    const isDraggingDown = isDragging && dropPosition === 'below';
    const minHeight = editGroups ? qualitiesHeightEditGroups : qualitiesHeight;

    // Class headings are display only: each row keeps the qualityIndex of its stored position, so
    // drag-and-drop is unchanged. A heading opens every run of a new class, which keeps a
    // hand-reordered list readable instead of asserting an order that is not there.
    const rows = [];
    let lastClassKey = null;

    qualityProfileItems
      .map((item, index) => ({ item, index }))
      .reverse()
      .forEach(({ item, index }) => {
        const { id, name, allowed, quality, items } = item;
        const identifier = quality ? quality.id : id;
        const classKey = qualityClassKey(item);

        if (classKey !== lastClassKey) {
          lastClassKey = classKey;

          rows.push(
            <div
              key={`class-${identifier}`}
              className={styles.classHeading}
            >
              {translate(classKey)}
            </div>
          );
        }

        rows.push(
          <QualityProfileItemDragSource
            key={identifier}
            editGroups={editGroups}
            groupId={id}
            qualityId={quality && quality.id}
            name={quality ? quality.name : name}
            allowed={allowed}
            items={items}
            qualityIndex={`${index + 1}`}
            isInGroup={false}
            isDragging={isDragging}
            isDraggingUp={isDraggingUp}
            isDraggingDown={isDraggingDown}
            {...otherProps}
          />
        );
      });

    return (
      <FormGroup size={sizes.EXTRA_SMALL}>
        <FormLabel size={sizes.SMALL}>
          {translate('Qualities')}
        </FormLabel>

        <div>
          <FormInputHelpText
            text={translate('QualitiesHelpText')}
          />

          {
            errors.map((error, index) => {
              return (
                <FormInputHelpText
                  key={index}
                  text={error.message}
                  isError={true}
                  isCheckInput={false}
                />
              );
            })
          }

          {
            warnings.map((warning, index) => {
              return (
                <FormInputHelpText
                  key={index}
                  text={warning.message}
                  isWarning={true}
                  isCheckInput={false}
                />
              );
            })
          }

          <Button
            className={styles.editGroupsButton}
            kind={kinds.PRIMARY}
            onPress={this.onToggleEditGroupsMode}
          >
            <div>
              <Icon
                className={styles.editGroupsButtonIcon}
                name={editGroups ? icons.REORDER : icons.GROUP}
              />

              {
                editGroups ? translate('DoneEditingGroups') : translate('EditGroups')
              }
            </div>
          </Button>

          <FormInputHelpText
            text={translate('QualityOrderExplainer')}
          />

          <Measure
            includeMargin={false}
            onMeasure={this.onMeasure}
            className={styles.qualities}
            style={{ minHeight: `${minHeight}px` }}
          >
            {rows}

            <QualityProfileItemDragPreview />
          </Measure>
        </div>
      </FormGroup>
    );
  }
}

QualityProfileItems.propTypes = {
  editGroups: PropTypes.bool.isRequired,
  dragQualityIndex: PropTypes.string,
  dropQualityIndex: PropTypes.string,
  dropPosition: PropTypes.string,
  qualityProfileItems: PropTypes.arrayOf(PropTypes.object).isRequired,
  errors: PropTypes.arrayOf(PropTypes.object),
  warnings: PropTypes.arrayOf(PropTypes.object),
  onToggleEditGroupsMode: PropTypes.func.isRequired
};

QualityProfileItems.defaultProps = {
  errors: [],
  warnings: []
};

export default QualityProfileItems;
