using System;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.ImportLists.MangaDex
{
    public class MangaDexFollows : HttpImportListBase<MangaDexSettings>
    {
        public override string Name => "MangaDex Follows";

        public override ImportListType ListType => ImportListType.Other;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);
        public override int PageSize => 100;
        public override TimeSpan RateLimit => TimeSpan.FromMilliseconds(500);

        public MangaDexFollows(IHttpClient httpClient, IImportListStatusService importListStatusService, IConfigService configService, IParsingService parsingService, Logger logger)
            : base(httpClient, importListStatusService, configService, parsingService, logger)
        {
        }

        public override IImportListRequestGenerator GetRequestGenerator()
        {
            return new MangaDexRequestGenerator
            {
                Settings = Settings,
                AccessToken = GetAccessToken()
            };
        }

        public override IParseImportListResponse GetParser()
        {
            return new MangaDexParser();
        }

        // Personal-client password grant; tokens live 15 minutes, which covers a
        // full sync, so no refresh-token handling is needed.
        private string GetAccessToken()
        {
            var request = new HttpRequestBuilder(Settings.AuthUrl)
                .Post()
                .AddFormParameter("grant_type", "password")
                .AddFormParameter("username", Settings.Username)
                .AddFormParameter("password", Settings.Password)
                .AddFormParameter("client_id", Settings.ClientId)
                .AddFormParameter("client_secret", Settings.ClientSecret)
                .Build();

            var response = _httpClient.Post<MangaDexAuthResponse>(request);
            var token = response.Resource?.AccessToken;

            if (token.IsNullOrWhiteSpace())
            {
                throw new ImportListException(null, "Failed to authenticate with MangaDex; check username/password and personal client id/secret");
            }

            return token;
        }
    }
}
