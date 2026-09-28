import PropTypes from 'prop-types';
import React from 'react';
import Alert from 'Components/Alert';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

function value(field) {
  return field ? field.value : undefined;
}

function formatDate(iso) {
  if (!iso) {
    return translate('Never');
  }

  const date = new Date(iso);

  return isNaN(date.getTime()) ? iso : date.toLocaleString();
}

function sourceName(generator, source) {
  if (generator === 'opentome') {
    return 'OpenTome';
  }

  if (generator === 'gcd' || source === 'GCD') {
    return 'Grand Comics Database';
  }

  return source || generator || '—';
}

function MetadataSource(props) {
  const {
    advancedSettings,
    isFetching,
    error,
    settings,
    hasSettings,
    onInputChange
  } = props;

  const available = value(settings.available);
  const generator = value(settings.generator);
  const source = value(settings.source);
  const attribution = value(settings.attribution);
  const licence = value(settings.licence);
  const lastCheck = value(settings.lastCheck);
  const lastCheckResult = value(settings.metadataLastCheckResult);
  const defaultManifestUrl = value(settings.defaultManifestUrl);

  return (
    <div>
      {
        isFetching &&
          <LoadingIndicator />
      }

      {
        !isFetching && error &&
          <Alert kind={kinds.DANGER}>
            {translate('UnableToLoadMetadataSourceSettings')}
          </Alert>
      }

      {
        hasSettings && !isFetching && !error &&
          <Form>
            <FieldSet legend={translate('MetadataSourceArtifact')}>
              {
                available ?
                  <div>
                    <DescriptionList>
                      <DescriptionListItem
                        title={translate('Source')}
                        data={sourceName(generator, source)}
                      />
                      <DescriptionListItem
                        title={translate('Version')}
                        data={value(settings.version) || '—'}
                      />
                      <DescriptionListItem
                        title={translate('Generated')}
                        data={formatDate(value(settings.generatedAt))}
                      />
                      <DescriptionListItem
                        title={translate('ReleaseLines')}
                        data={(value(settings.seriesCount) || 0).toLocaleString()}
                      />
                      <DescriptionListItem
                        title={translate('Books')}
                        data={(value(settings.volumeCount) || 0).toLocaleString()}
                      />
                      {
                        advancedSettings &&
                          <DescriptionListItem
                            title={translate('Path')}
                            data={value(settings.artifactPath)}
                          />
                      }
                    </DescriptionList>

                    {
                      attribution &&
                        <Alert kind={kinds.INFO}>
                          {attribution}
                          {
                            licence ?
                              <div>{licence}</div> :
                              null
                          }
                        </Alert>
                    }
                  </div> :
                  <Alert kind={kinds.WARNING}>
                    {translate('MetadataSourceNoArtifact')}
                  </Alert>
              }
            </FieldSet>

            <FieldSet legend={translate('MetadataSourceUpdates')}>
              <DescriptionList>
                <DescriptionListItem
                  title={translate('LastCheck')}
                  data={formatDate(lastCheck)}
                />
                {
                  lastCheckResult ?
                    <DescriptionListItem
                      title={translate('Result')}
                      data={lastCheckResult}
                    /> :
                    null
                }
              </DescriptionList>

              <FormGroup>
                <FormLabel>{translate('MetadataAutoUpdate')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="metadataAutoUpdate"
                  helpText={translate('MetadataAutoUpdateHelpText')}
                  onChange={onInputChange}
                  {...settings.metadataAutoUpdate}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={advancedSettings}
                isAdvanced={true}
              >
                <FormLabel>{translate('MetadataManifestUrl')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="metadataManifestUrl"
                  placeholder={defaultManifestUrl}
                  helpText={translate('MetadataManifestUrlHelpText')}
                  onChange={onInputChange}
                  {...settings.metadataManifestUrl}
                />
              </FormGroup>
            </FieldSet>

            {/* UI pass (2026-09-24, ME-1): where lookups come from is Metadata Source, as in
                Sonarr; the key moved here from Settings -> Metadata (same config key). */}
            <FieldSet legend={translate('GoogleBooks')}>
              <FormGroup>
                <FormLabel>{translate('GoogleBooksApiKey')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="googleBooksApiKey"
                  helpText={translate('GoogleBooksApiKeyHelpText')}
                  onChange={onInputChange}
                  {...settings.googleBooksApiKey}
                />
              </FormGroup>
            </FieldSet>
          </Form>
      }
    </div>
  );
}

MetadataSource.propTypes = {
  advancedSettings: PropTypes.bool.isRequired,
  isFetching: PropTypes.bool.isRequired,
  error: PropTypes.object,
  settings: PropTypes.object.isRequired,
  hasSettings: PropTypes.bool.isRequired,
  onInputChange: PropTypes.func.isRequired
};

export default MetadataSource;
