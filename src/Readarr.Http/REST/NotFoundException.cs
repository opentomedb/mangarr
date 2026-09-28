using System.Net;
using NzbDrone.Common.Localization;
using Readarr.Http.Exceptions;

namespace Readarr.Http.REST
{
    public class NotFoundException : ApiException
    {
        public NotFoundException(object content = null)
            : base(HttpStatusCode.NotFound, content)
        {
        }

        public NotFoundException(ServerText content)
            : base(HttpStatusCode.NotFound, content)
        {
        }
    }
}
