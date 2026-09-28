import React, { Component } from 'react';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItemDescription from 'Components/DescriptionList/DescriptionListItemDescription';
import DescriptionListItemTitle from 'Components/DescriptionList/DescriptionListItemTitle';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';

class MoreInfo extends Component {

  //
  // Render

  render() {
    return (
      <FieldSet legend={translate('MoreInfo')}>
        <DescriptionList>
          <DescriptionListItemTitle>{translate('HomePage')}</DescriptionListItemTitle>
          <DescriptionListItemDescription>
            <Link to="https://github.com/DrAwesome441/mangarr">github.com/DrAwesome441/mangarr</Link>
          </DescriptionListItemDescription>

          <DescriptionListItemTitle>{translate('Wiki')}</DescriptionListItemTitle>
          <DescriptionListItemDescription>
            <Link to="https://wiki.servarr.com/readarr">{translate('WikiBasedOnReadarr')}</Link>
          </DescriptionListItemDescription>

          <DescriptionListItemTitle>{translate('SourceCode')}</DescriptionListItemTitle>
          <DescriptionListItemDescription>
            <Link to="https://github.com/DrAwesome441/mangarr">github.com/DrAwesome441/mangarr</Link>
          </DescriptionListItemDescription>

          <DescriptionListItemTitle>{translate('FeatureRequests')}</DescriptionListItemTitle>
          <DescriptionListItemDescription>
            <Link to="https://github.com/DrAwesome441/mangarr/issues">github.com/DrAwesome441/mangarr/issues</Link>
          </DescriptionListItemDescription>

        </DescriptionList>
      </FieldSet>
    );
  }
}

MoreInfo.propTypes = {

};

export default MoreInfo;
