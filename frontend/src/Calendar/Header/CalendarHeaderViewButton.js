import PropTypes from 'prop-types';
import React, { Component } from 'react';
import * as calendarViews from 'Calendar/calendarViews';
import Button from 'Components/Link/Button';
import translate from 'Utilities/String/translate';
// import styles from './CalendarHeaderViewButton.css';

// UI translations v1 (2026-09-25): the label was titleCase(view), English built from the view id;
// each view now has its own key.
function getViewLabel(view) {
  switch (view) {
    case calendarViews.DAY:
      return translate('Day');
    case calendarViews.WEEK:
      return translate('Week');
    case calendarViews.MONTH:
      return translate('Month');
    case calendarViews.FORECAST:
      return translate('Forecast');
    case calendarViews.AGENDA:
      return translate('Agenda');
    default:
      return view;
  }
}

class CalendarHeaderViewButton extends Component {

  //
  // Listeners

  onPress = () => {
    this.props.onPress(this.props.view);
  };

  //
  // Render

  render() {
    const {
      view,
      selectedView,
      ...otherProps
    } = this.props;

    return (
      <Button
        isDisabled={selectedView === view}
        {...otherProps}
        onPress={this.onPress}
      >
        {getViewLabel(view)}
      </Button>
    );
  }
}

CalendarHeaderViewButton.propTypes = {
  view: PropTypes.oneOf(calendarViews.all).isRequired,
  selectedView: PropTypes.oneOf(calendarViews.all).isRequired,
  onPress: PropTypes.func.isRequired
};

export default CalendarHeaderViewButton;
