using System.Net;
using NzbDrone.Common.Localization;
using Readarr.Http.Exceptions;

namespace Readarr.Http.REST
{
    public class UnsupportedMediaTypeException : ApiException
    {
        public UnsupportedMediaTypeException(object content = null)
            : base(HttpStatusCode.UnsupportedMediaType, content)
        {
        }

        public UnsupportedMediaTypeException(ServerText content)
            : base(HttpStatusCode.UnsupportedMediaType, content)
        {
        }
    }
}
