import PropTypes from 'prop-types';
import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import translateElements from 'Utilities/String/translateElements';
import TagConnector from './TagConnector';
import styles from './Tags.css';

function Tags(props) {
  const {
    items,
    ...otherProps
  } = props;

  if (!items.length) {
    // UI translations v1 (2026-09-25): translate() stringified the link element passed as {0}
    // ("[object Object]"); translateElements renders it where the sentence places it.
    return (
      <Alert kind={kinds.INFO}>
        {
          translateElements('NoTagsHaveBeenAddedYetMessageText', {
            wikiLink: <Link to='https://wiki.servarr.com/readarr/settings#tags'>{translate('LinkHere')}</Link>
          })
        }
      </Alert>
    );
  }

  return (
    <FieldSet
      legend={translate('Tags')}
    >
      <PageSectionContent
        errorMessage={translate('UnableToLoadTags')}
        {...otherProps}
      >
        <div className={styles.tags}>
          {
            items.map((item) => {
              return (
                <TagConnector
                  key={item.id}
                  {...item}
                />
              );
            })
          }
        </div>
      </PageSectionContent>
    </FieldSet>
  );
}

Tags.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired
};

export default Tags;
