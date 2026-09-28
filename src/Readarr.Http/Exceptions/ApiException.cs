using System;
using System.Net;
using NzbDrone.Common.Localization;

namespace Readarr.Http.Exceptions
{
    public abstract class ApiException : Exception
    {
        public object Content { get; private set; }

        public HttpStatusCode StatusCode { get; private set; }

        // Server messages (2026-09-26): Content's template when it was built from one, for the UI language.
        // Not serialized: an exception is never returned as API JSON itself (ReadarrErrorPipeline maps it to
        // ErrorModel).
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ServerText Text { get; private set; }

        protected ApiException(HttpStatusCode statusCode, object content = null)
            : base(GetMessage(statusCode, content))
        {
            StatusCode = statusCode;
            Content = content;
        }

        protected ApiException(HttpStatusCode statusCode, ServerText content)
            : this(statusCode, (object)content?.English)
        {
            Text = content;
        }

        private static string GetMessage(HttpStatusCode statusCode, object content)
        {
            var result = statusCode.ToString();

            if (content != null)
            {
                result = $"{result}: {content}";
            }

            return result;
        }
    }
}
