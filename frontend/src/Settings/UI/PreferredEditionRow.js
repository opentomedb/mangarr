import PropTypes from 'prop-types';
import React, { Component } from 'react';
import IconButton from 'Components/Link/IconButton';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './PreferredEditionInput.css';

class PreferredEditionRow extends Component {

  //
  // Listeners

  onUpPress = () => {
    this.props.onMove(this.props.index, -1);
  };

  onDownPress = () => {
    this.props.onMove(this.props.index, 1);
  };

  onRemovePress = () => {
    this.props.onRemove(this.props.index);
  };

  //
  // Render

  render() {
    const {
      name,
      lines,
      isFirst,
      isLast,
      isOnly
    } = this.props;

    return (
      <div className={styles.row}>
        <span className={styles.name}>{name}</span>

        <span className={styles.lines}>
          {lines == null ? '' : translate('EditionLines', { count: lines })}
        </span>

        <IconButton
          className={styles.button}
          name={icons.SORT_ASCENDING}
          title={translate('MoveUp')}
          isDisabled={isFirst}
          onPress={this.onUpPress}
        />

        <IconButton
          className={styles.button}
          name={icons.SORT_DESCENDING}
          title={translate('MoveDown')}
          isDisabled={isLast}
          onPress={this.onDownPress}
        />

        <IconButton
          className={styles.button}
          name={icons.REMOVE}
          title={translate('Remove')}
          isDisabled={isOnly}
          onPress={this.onRemovePress}
        />
      </div>
    );
  }
}

PreferredEditionRow.propTypes = {
  index: PropTypes.number.isRequired,
  name: PropTypes.string.isRequired,
  lines: PropTypes.number,
  isFirst: PropTypes.bool.isRequired,
  isLast: PropTypes.bool.isRequired,
  isOnly: PropTypes.bool.isRequired,
  onMove: PropTypes.func.isRequired,
  onRemove: PropTypes.func.isRequired
};

export default PreferredEditionRow;
