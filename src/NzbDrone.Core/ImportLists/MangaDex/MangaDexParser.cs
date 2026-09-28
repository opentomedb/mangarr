using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.MangaDex
{
    public class MangaDexParser : IParseImportListResponse
    {
        public IList<ImportListItemInfo> ParseResponse(ImportListResponse importListResponse)
        {
            var items = new List<ImportListItemInfo>();

            if (!PreProcess(importListResponse))
            {
                return items;
            }

            var jsonResponse = JsonConvert.DeserializeObject<MangaDexFollowsResponse>(importListResponse.Content);

            if (jsonResponse?.Data == null)
            {
                return items;
            }

            foreach (var manga in jsonResponse.Data)
            {
                var title = GetPreferredTitle(manga.Attributes);

                if (title.IsNullOrWhiteSpace())
                {
                    continue;
                }

                items.Add(new ImportListItemInfo
                {
                    Author = title
                });
            }

            return items;
        }

        // MangaDex titles are language maps; prefer the English title, then an
        // English alt title, then romanized Japanese, then whatever exists.
        private static string GetPreferredTitle(MangaDexMangaAttributes attributes)
        {
            if (attributes == null)
            {
                return null;
            }

            if (attributes.Title != null && attributes.Title.TryGetValue("en", out var enTitle) && enTitle.IsNotNullOrWhiteSpace())
            {
                return enTitle;
            }

            var altEn = attributes.AltTitles?.Select(x => x.TryGetValue("en", out var alt) ? alt : null)
                .FirstOrDefault(x => x.IsNotNullOrWhiteSpace());

            if (altEn.IsNotNullOrWhiteSpace())
            {
                return altEn;
            }

            if (attributes.Title != null && attributes.Title.TryGetValue("ja-ro", out var romaji) && romaji.IsNotNullOrWhiteSpace())
            {
                return romaji;
            }

            return attributes.Title?.Values.FirstOrDefault(x => x.IsNotNullOrWhiteSpace());
        }

        protected virtual bool PreProcess(ImportListResponse importListResponse)
        {
            if (importListResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new ImportListException(importListResponse, "Import List API call resulted in an unexpected StatusCode [{0}]", importListResponse.HttpResponse.StatusCode);
            }

            return true;
        }
    }
}
