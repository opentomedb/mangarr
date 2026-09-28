using System.Collections.Generic;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.MangaDex
{
    public class MangaDexRequestGenerator : IImportListRequestGenerator
    {
        public MangaDexSettings Settings { get; set; }
        public string AccessToken { get; set; }

        public int MaxPages { get; set; }
        public int PageSize { get; set; }

        public MangaDexRequestGenerator()
        {
            MaxPages = 10;
            PageSize = 100;
        }

        public virtual ImportListPageableRequestChain GetListItems()
        {
            var pageableRequests = new ImportListPageableRequestChain();

            pageableRequests.Add(GetPagedRequests());

            return pageableRequests;
        }

        private IEnumerable<ImportListRequest> GetPagedRequests()
        {
            for (var page = 0; page < MaxPages; page++)
            {
                var request = new ImportListRequest($"{Settings.BaseUrl.TrimEnd('/')}/user/follows/manga?limit={PageSize}&offset={page * PageSize}", HttpAccept.Json);

                request.HttpRequest.Headers.Set("Authorization", $"Bearer {AccessToken}");

                yield return request;
            }
        }
    }
}
