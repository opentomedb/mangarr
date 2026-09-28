using System.Net;
using NzbDrone.Common.Localization;
using Readarr.Http.Exceptions;

namespace Readarr.Http.REST
{
    public class BadRequestException : ApiException
    {
        public BadRequestException(object content = null)
            : base(HttpStatusCode.BadRequest, content)
        {
        }

        public BadRequestException(ServerText content)
            : base(HttpStatusCode.BadRequest, content)
        {
        }
    }
}
