using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Readarr.Http.Extensions;

namespace Readarr.Http.Middleware
{
    public class CacheHeaderMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ICacheableSpecification _cacheableSpecification;

        public CacheHeaderMiddleware(RequestDelegate next, ICacheableSpecification cacheableSpecification)
        {
            _next = next;
            _cacheableSpecification = cacheableSpecification;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Method != "OPTIONS")
            {
                if (_cacheableSpecification.IsCacheable(context.Request))
                {
                    context.Response.Headers.EnableCache();

                    // The cache decision is made from the request path alone, before the
                    // response exists. A failure on a cacheable path (404 while the UI is
                    // mid-deploy, an auth redirect) must not go out with max-age=31536000
                    // or browsers pin the failure for a year. Downgrade at send time;
                    // never upgrade, so a downstream DisableCache (index.html) still wins.
                    context.Response.OnStarting(() =>
                    {
                        var status = context.Response.StatusCode;

                        if ((status < 200 || status >= 300) && status != 304)
                        {
                            context.Response.Headers.DisableCache();
                        }

                        return Task.CompletedTask;
                    });
                }
                else
                {
                    context.Response.Headers.DisableCache();
                }
            }

            await _next(context);
        }
    }
}
