import PropTypes from 'prop-types';
import React, { Component } from 'react';
import SelectInput from 'Components/Form/SelectInput';
import translate from 'Utilities/String/translate';
import PreferredEditionRow from './PreferredEditionRow';
import styles from './PreferredEditionInput.css';

// Preferred Edition (2026-09-24): the ordered fallback chain of edition languages for NEW series,
// stored as a comma-separated list ("fr,en"). Choices are the markets the loaded catalogue carries.
function parse(value) {
  const codes = (value || '').split(',').map((code) => code.trim()).filter((code) => code);

  return codes.length ? codes : ['en'];
}

class PreferredEditionInput extends Component {

  //
  // Listeners

  change(codes) {
    this.props.onChange({ name: this.props.name, value: codes.join(',') });
  }

  onMove = (index, offset) => {
    const codes = parse(this.props.value);
    const target = index + offset;

    if (target < 0 || target >= codes.length) {
      return;
    }

    const moved = codes[index];
    codes[index] = codes[target];
    codes[target] = moved;

    this.change(codes);
  };

  onRemove = (index) => {
    const codes = parse(this.props.value);

    if (codes.length > 1) {
      codes.splice(index, 1);
      this.change(codes);
    }
  };

  onAdd = ({ value }) => {
    const codes = parse(this.props.value);

    if (value && !codes.includes(value)) {
      this.change([...codes, value]);
    }
  };

  //
  // Render

  render() {
    const {
      name,
      value,
      markets
    } = this.props;

    const codes = parse(value);
    const byCode = {};

    markets.forEach((market) => {
      byCode[market.language] = market;
    });

    const addValues = [
      { key: '', value: translate('AddLanguage'), isDisabled: true },
      ...markets
        .filter((market) => !codes.includes(market.language))
        .map((market) => ({
          key: market.language,
          value: `${market.name} (${translate('EditionLines', { count: market.lines })})`
        }))
    ];

    return (
      <div className={styles.list}>
        {
          codes.map((code, index) => {
            const market = byCode[code];

            return (
              <PreferredEditionRow
                key={code}
                index={index}
                name={market ? market.name : code}
                lines={market ? market.lines : null}
                isFirst={index === 0}
                isLast={index === codes.length - 1}
                isOnly={codes.length === 1}
                onMove={this.onMove}
                onRemove={this.onRemove}
              />
            );
          })
        }

        <SelectInput
          name={`${name}Add`}
          value=""
          values={addValues}
          onChange={this.onAdd}
        />
      </div>
    );
  }
}

PreferredEditionInput.propTypes = {
  name: PropTypes.string.isRequired,
  value: PropTypes.string,
  markets: PropTypes.arrayOf(PropTypes.object).isRequired,
  onChange: PropTypes.func.isRequired
};

export default PreferredEditionInput;
