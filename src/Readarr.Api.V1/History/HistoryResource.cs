using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Qualities;
using Readarr.Api.V1.Author;
using Readarr.Api.V1.Books;
using Readarr.Api.V1.CustomFormats;
using Readarr.Http.REST;

namespace Readarr.Api.V1.History
{
    public class HistoryResource : RestResource
    {
        public int BookId { get; set; }
        public int AuthorId { get; set; }
        public string SourceTitle { get; set; }
        public QualityModel Quality { get; set; }
        public List<CustomFormatResource> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public bool QualityCutoffNotMet { get; set; }
        public DateTime Date { get; set; }
        public string DownloadId { get; set; }

        public EntityHistoryEventType EventType { get; set; }

        public Dictionary<string, string> Data { get; set; }

        public BookResource Book { get; set; }
        public AuthorResource Author { get; set; }
    }

    public static class HistoryResourceMapper
    {
        public static HistoryResource ToResource(this EntityHistory model, ICustomFormatCalculationService formatCalculator, IServerMessageLocalizer messages = null)
        {
            if (model == null)
            {
                return null;
            }

            var customFormats = formatCalculator.ParseCustomFormat(model, model.Author);
            var customFormatScore = model.Author?.QualityProfile?.Value?.CalculateCustomFormatScore(customFormats) ?? 0;

            return new HistoryResource
            {
                Id = model.Id,

                BookId = model.BookId,
                AuthorId = model.AuthorId,
                SourceTitle = model.SourceTitle,
                Quality = model.Quality,
                CustomFormats = customFormats.ToResource(false),
                CustomFormatScore = customFormatScore,

                //QualityCutoffNotMet
                Date = model.Date,
                DownloadId = model.DownloadId,

                EventType = model.EventType,

                Data = MessageData(model.Data, messages)

                //Episode
                //Series
            };
        }

        // Server messages (2026-09-26, plan ruling R5): data.message in the UI language -- from messageKey and
        // messageArgs when the row stored them, else matched whole -- and those two keys never reach the API,
        // so an English response stays byte-identical. Always a copy: the model's dictionary is never changed.
        private static Dictionary<string, string> MessageData(Dictionary<string, string> data, IServerMessageLocalizer messages)
        {
            if (data == null)
            {
                return null;
            }

            var key = Value(data, "messageKey");
            var args = Value(data, "messageArgs");
            var statusKeys = Value(data, "statusMessageKeys");
            var copy = new Dictionary<string, string>(
                data.Where(e => !e.Key.Equals("messageKey", StringComparison.OrdinalIgnoreCase) &&
                                !e.Key.Equals("messageArgs", StringComparison.OrdinalIgnoreCase) &&
                                !e.Key.Equals("statusMessageKeys", StringComparison.OrdinalIgnoreCase)),
                data.Comparer);
            var message = copy.Keys.FirstOrDefault(k => k.Equals("message", StringComparison.OrdinalIgnoreCase));
            var statusMessages = copy.Keys.FirstOrDefault(k => k.Equals("statusMessages", StringComparison.OrdinalIgnoreCase));

            if (messages != null && message != null)
            {
                copy[message] = key != null && TryParseArgs(args, out var parsedArgs)
                    ? messages.LocalizeStored(copy[message], key, parsedArgs)
                    : messages.Localize(copy[message]);
            }

            if (messages != null && statusMessages != null && copy[statusMessages] != null)
            {
                copy[statusMessages] = LocalizeStatusMessages(copy[statusMessages], statusKeys, messages);
            }

            return copy;
        }

        // Server messages (2026-09-27, follow-ups): an import-incomplete row's statusMessages JSON with each message
        // in the UI language -- from its statusMessageKeys record when the row stored one, else matched whole.
        // Titles are file and release names and stay. Unchanged messages (an English UI) return the stored string
        // itself, so English stays byte-identical; malformed JSON in either value returns it too.
        private static string LocalizeStatusMessages(string json, string keysJson, IServerMessageLocalizer messages)
        {
            List<TrackedDownloadStatusMessage> stored;
            List<List<ServerMessageRecord>> records;

            try
            {
                stored = Json.Deserialize<List<TrackedDownloadStatusMessage>>(json);
                records = keysJson == null ? null : Json.Deserialize<List<List<ServerMessageRecord>>>(keysJson);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return json;
            }

            if (stored == null)
            {
                return json;
            }

            var changed = false;
            var localized = stored.Select((statusMessage, i) =>
            {
                var messageRecords = records != null && i < records.Count ? records[i] : null;
                var texts = statusMessage?.Messages?.Select((english, j) =>
                {
                    var record = messageRecords != null && j < messageRecords.Count ? messageRecords[j] : null;
                    var text = record?.Key != null
                        ? messages.LocalizeStored(english, record.Key, record.Args)
                        : messages.Localize(english);

                    changed |= text != english;

                    return text;
                }).ToList();

                return statusMessage == null ? null : new TrackedDownloadStatusMessage(statusMessage.Title, texts);
            }).ToList();

            return changed ? localized.ToJson() : json;
        }

        private static string Value(Dictionary<string, string> data, string key)
        {
            return data.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
        }

        // Server messages (2026-09-26, task 1b review): a malformed messageArgs value (a hand-edited or
        // otherwise corrupted row) falls back to the plain English message instead of throwing out of the API.
        private static bool TryParseArgs(string args, out List<string> parsed)
        {
            if (args == null)
            {
                parsed = null;
                return true;
            }

            try
            {
                parsed = JsonSerializer.Deserialize<List<string>>(args);
                return true;
            }
            catch (JsonException)
            {
                parsed = null;
                return false;
            }
        }
    }
}
