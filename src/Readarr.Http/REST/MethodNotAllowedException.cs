using System.Net;
using NzbDrone.Common.Localization;
using Readarr.Http.Exceptions;

namespace Readarr.Http.REST
{
    public class MethodNotAllowedException : ApiException
    {
        public MethodNotAllowedException(object content = null)
            : base(HttpStatusCode.MethodNotAllowed, content)
        {
        }

        public MethodNotAllowedException(ServerText content)
            : base(HttpStatusCode.MethodNotAllowed, content)
        {
        }
    }
}
