import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import SelectInput from './SelectInput';

const NOT_MONITORED = '';

function describe(bookEdition) {
  let value = `${bookEdition.title}`;

  if (bookEdition.disambiguation) {
    value = `${value} (${titleCase(bookEdition.disambiguation)})`;
  }

  const extras = [];
  if (bookEdition.language) {
    extras.push(bookEdition.language);
  }
  if (bookEdition.publisher) {
    extras.push(bookEdition.publisher);
  }
  if (bookEdition.isbn13) {
    extras.push(bookEdition.isbn13);
  }
  if (bookEdition.format) {
    extras.push(bookEdition.format);
  }
  if (bookEdition.pageCount > 0) {
    extras.push(`${bookEdition.pageCount}p`);
  }

  if (extras) {
    value = `${value} [${extras.join(', ')}]`;
  }

  return {
    key: bookEdition.foreignEditionId,
    value
  };
}

function createMapStateToProps() {
  return createSelector(
    (state, { bookEditions }) => bookEditions,
    (bookEditions) => {
      // One select per media type. A manga volume has one media type (archive) and renders
      // exactly as before; a light-novel volume gets an ebook select and an audio select, each
      // with its own "Not Monitored" option, because its editions are monitored per media type.
      // Edit Volume renders each in its own FormGroup (UI pass 2026-09-24, VD-4) by passing
      // `mediaType`; the save value is the same editions array either way.
      const groups = _.map(_.groupBy(bookEditions.value, (e) => e.mediaType || 'archive'), (editions, mediaType) => {
        const monitored = _.find(editions, { monitored: true });

        return {
          mediaType,
          values: _.orderBy(_.map(editions, describe), ['value']),
          value: monitored ? monitored.foreignEditionId : NOT_MONITORED
        };
      });

      return { groups };
    }
  );
}

class BookEditionSelectInputConnector extends Component {

  //
  // Listeners

  onChange = ({ name, value }) => {
    const {
      bookEditions,
      groups
    } = this.props;

    // `name` is the media type of the select that changed; only that media type's editions move.
    const isSingleGroup = groups.length === 1;
    const updatedEditions = _.map(bookEditions.value, (e) => {
      const inGroup = isSingleGroup || (e.mediaType || 'archive') === name;

      return inGroup ? { ...e, monitored: e.foreignEditionId === value } : e;
    });

    this.props.onChange({ name: this.props.name, value: updatedEditions });
  };

  render() {
    const {
      groups,
      name,
      bookEditions,
      mediaType,
      dispatch,
      ...otherProps
    } = this.props;

    if (groups.length === 1) {
      return (
        <SelectInput
          {...otherProps}
          name={groups[0].mediaType}
          values={groups[0].values}
          value={groups[0].value}
          onChange={this.onChange}
        />
      );
    }

    return (
      <div>
        {
          groups.filter((group) => !mediaType || group.mediaType === mediaType).map((group) => {
            return (
              <SelectInput
                key={group.mediaType}
                {...otherProps}
                name={group.mediaType}
                values={[
                  ...group.values,
                  { key: NOT_MONITORED, value: translate('NotMonitored') }
                ]}
                value={group.value}
                onChange={this.onChange}
              />
            );
          })
        }
      </div>
    );
  }
}

BookEditionSelectInputConnector.propTypes = {
  name: PropTypes.string.isRequired,
  groups: PropTypes.arrayOf(PropTypes.object).isRequired,
  onChange: PropTypes.func.isRequired,
  bookEditions: PropTypes.object,
  mediaType: PropTypes.string,
  dispatch: PropTypes.func
};

export default connect(createMapStateToProps)(BookEditionSelectInputConnector);
