using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Qualities;
using Readarr.Api.V1.Author;
using Readarr.Api.V1.Books;
using Readarr.Api.V1.CustomFormats;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Queue
{
    public class QueueResource : RestResource
    {
        public int? AuthorId { get; set; }
        public int? BookId { get; set; }
        public AuthorResource Author { get; set; }
        public BookResource Book { get; set; }
        public QualityModel Quality { get; set; }
        public List<CustomFormatResource> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public decimal Size { get; set; }
        public string Title { get; set; }
        public decimal Sizeleft { get; set; }
        public TimeSpan? Timeleft { get; set; }
        public DateTime? EstimatedCompletionTime { get; set; }
        public string Status { get; set; }
        public TrackedDownloadStatus? TrackedDownloadStatus { get; set; }
        public TrackedDownloadState? TrackedDownloadState { get; set; }
        public List<TrackedDownloadStatusMessage> StatusMessages { get; set; }
        public string ErrorMessage { get; set; }
        public string DownloadId { get; set; }
        public DownloadProtocol Protocol { get; set; }
        public string DownloadClient { get; set; }
        public bool DownloadClientHasPostImportCategory { get; set; }
        public string Indexer { get; set; }
        public string OutputPath { get; set; }
        public bool DownloadForced { get; set; }
    }

    public static class QueueResourceMapper
    {
        public static QueueResource ToResource(this NzbDrone.Core.Queue.Queue model, bool includeAuthor, bool includeBook, IServerMessageLocalizer messages = null)
        {
            if (model == null)
            {
                return null;
            }

            var customFormats = model.RemoteBook?.CustomFormats;
            var customFormatScore = model.RemoteBook?.Author?.QualityProfile?.Value?.CalculateCustomFormatScore(customFormats) ?? 0;

            return new QueueResource
            {
                Id = model.Id,
                AuthorId = model.Author?.Id,
                BookId = model.Book?.Id,
                Author = includeAuthor && model.Author != null ? model.Author.ToResource() : null,
                Book = includeBook && model.Book != null ? model.Book.ToResource() : null,
                Quality = model.Quality,
                CustomFormats = customFormats?.ToResource(false),
                CustomFormatScore = customFormatScore,
                Size = model.Size,
                Title = model.Title,
                Sizeleft = model.Sizeleft,
                Timeleft = model.Timeleft,
                EstimatedCompletionTime = model.EstimatedCompletionTime,
                Status = model.Status.FirstCharToLower(),
                TrackedDownloadStatus = model.TrackedDownloadStatus,
                TrackedDownloadState = model.TrackedDownloadState,
                StatusMessages = messages == null ? model.StatusMessages : Localize(model.StatusMessages, messages),
                ErrorMessage = messages == null ? model.ErrorMessage : messages.Localize(model.ErrorMessage),
                DownloadId = model.DownloadId,
                Protocol = model.Protocol,
                DownloadClient = model.DownloadClient,
                DownloadClientHasPostImportCategory = model.DownloadClientHasPostImportCategory,
                Indexer = model.Indexer,
                OutputPath = model.OutputPath,
                DownloadForced = model.DownloadForced
            };
        }

        public static List<QueueResource> ToResource(this IEnumerable<NzbDrone.Core.Queue.Queue> models, bool includeAuthor, bool includeBook, IServerMessageLocalizer messages = null)
        {
            return models.Select((m) => ToResource(m, includeAuthor, includeBook, messages)).ToList();
        }

        // Server messages (2026-09-26): copies in the UI language. The tracked download's own status messages
        // stay English: CompletedDownloadService and TrackedDownloadService match them.
        private static List<TrackedDownloadStatusMessage> Localize(List<TrackedDownloadStatusMessage> statusMessages, IServerMessageLocalizer messages)
        {
            return statusMessages?
                .Select(m => new TrackedDownloadStatusMessage(m.Title, m.Messages?.Select((message, i) => messages.Localize(message, m.MessageTexts?.ElementAtOrDefault(i))).ToList()))
                .ToList();
        }
    }
}
