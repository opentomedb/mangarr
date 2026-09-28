using System;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Config
{
    public class MetadataProviderConfigResource : RestResource
    {
        public WriteAudioTagsType WriteAudioTags { get; set; }
        public bool ScrubAudioTags { get; set; }
        public WriteBookTagsType WriteBookTags { get; set; }
        public bool UpdateCovers { get; set; }
        public bool EmbedMetadata { get; set; }
        public WriteComicInfoType WriteComicInfo { get; set; }

        // The real key never leaves the server: a stored key is presented as a fixed mask.
        // GoogleBooksApiKeySource ("settings" | "environment" | "none") tells the UI which key
        // is in effect; it is read-only (SaveConfigDictionary skips unknown config keys).
        public string GoogleBooksApiKey { get; set; }
        public string GoogleBooksApiKeySource { get; set; }
    }

    public static class MetadataProviderConfigResourceMapper
    {
        public static MetadataProviderConfigResource ToResource(IConfigService model)
        {
            return new MetadataProviderConfigResource
            {
                WriteAudioTags = model.WriteAudioTags,
                ScrubAudioTags = model.ScrubAudioTags,
                WriteBookTags = model.WriteBookTags,
                UpdateCovers = model.UpdateCovers,
                EmbedMetadata = model.EmbedMetadata,
                WriteComicInfo = model.WriteComicInfo,

                // Mask the EFFECTIVE key (settings or environment): asterisks in the field mean
                // "a key is in use", an empty field means none — regardless of where it came from.
                GoogleBooksApiKey = GoogleBooksService.MaskApiKey(GoogleBooksService.ResolveApiKey(
                    model.GoogleBooksApiKey,
                    Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY"))),
                GoogleBooksApiKeySource = GoogleBooksService.ResolveApiKeySource(
                    model.GoogleBooksApiKey,
                    Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY"))
            };
        }
    }
}
