using System;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Gcd;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Config
{
    // Settings -> Metadata Source. Two writable keys (auto-update, manifest URL) plus read-only
    // facts about the loaded artifact. The read-only properties are not config keys, so
    // ConfigController.SaveConfig skips them (SaveConfigDictionary ignores unknown keys).
    public class MetadataSourceConfigResource : RestResource
    {
        public bool MetadataAutoUpdate { get; set; }
        public string MetadataManifestUrl { get; set; }

        // Light novels (2026-09): the page the catalogue-miss link opens. The storage settings live on Media Management (2026-09-22).
        public string OpenTomeUrl { get; set; }

        // UI pass (2026-09-24, ME-1): the Google Books key shows on Metadata Source (where lookups
        // come from). Same config key and masking as MetadataProviderConfigResource, which keeps
        // accepting it so an older client's PUT there still works.
        public string GoogleBooksApiKey { get; set; }
        public string GoogleBooksApiKeySource { get; set; }

        public string DefaultManifestUrl { get; set; }
        public string MetadataLastCheckResult { get; set; }
        public DateTime? LastCheck { get; set; }

        public bool Available { get; set; }
        public string ArtifactPath { get; set; }
        public string Version { get; set; }
        public string Generator { get; set; }
        public string Source { get; set; }
        public string Attribution { get; set; }
        public string Licence { get; set; }
        public string GeneratedAt { get; set; }
        public string SchemaVersion { get; set; }
        public int SeriesCount { get; set; }
        public int VolumeCount { get; set; }
    }

    public static class MetadataSourceConfigResourceMapper
    {
        public static MetadataSourceConfigResource ToResource(
            IConfigService model,
            IGcdMetadataService gcdMetadataService,
            IScheduledTaskRepository taskRepository)
        {
            var info = gcdMetadataService.ArtifactInfo();

            DateTime? lastCheck = null;
            try
            {
                var task = taskRepository.GetDefinition(typeof(MetadataUpdateCommand));
                if (task != null && task.LastExecution > DateTime.MinValue)
                {
                    lastCheck = task.LastExecution;
                }
            }
            catch (Exception)
            {
                // no task row yet (fresh install) — nothing to show
            }

            return new MetadataSourceConfigResource
            {
                MetadataAutoUpdate = model.MetadataAutoUpdate,
                MetadataManifestUrl = model.MetadataManifestUrl,
                OpenTomeUrl = model.OpenTomeUrl,
                GoogleBooksApiKey = GoogleBooksService.MaskApiKey(GoogleBooksService.ResolveApiKey(
                    model.GoogleBooksApiKey,
                    Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY"))),
                GoogleBooksApiKeySource = GoogleBooksService.ResolveApiKeySource(
                    model.GoogleBooksApiKey,
                    Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY")),
                DefaultManifestUrl = MetadataUpdateService.DefaultManifestUrl,
                MetadataLastCheckResult = model.MetadataLastCheckResult,
                LastCheck = lastCheck,

                Available = info.Available,
                ArtifactPath = info.Path,
                Version = info.Version,
                Generator = info.Generator,
                Source = info.Source,
                Attribution = info.Attribution,
                Licence = info.Licence,
                GeneratedAt = info.GeneratedAt,
                SchemaVersion = info.SchemaVersion,
                SeriesCount = info.SeriesCount,
                VolumeCount = info.VolumeCount
            };
        }
    }
}
