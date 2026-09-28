using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Moq;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Readarr.Http.Extensions;
using Readarr.Http.Middleware;

namespace NzbDrone.Api.Test.MiddlewareTests
{
    [TestFixture]
    public class CacheHeaderMiddlewareFixture : TestBase
    {
        private DefaultHttpContext _context;
        private OnStartingCaptureFeature _responseFeature;

        [SetUp]
        public void Setup()
        {
            _responseFeature = new OnStartingCaptureFeature();
            _context = new DefaultHttpContext();
            _context.Features.Set<IHttpResponseFeature>(_responseFeature);
            _context.Request.Method = "GET";
        }

        private async Task Invoke(bool cacheableRequest, int statusCode, Action<HttpContext> downstream = null)
        {
            Mocker.GetMock<ICacheableSpecification>()
                  .Setup(s => s.IsCacheable(It.IsAny<HttpRequest>()))
                  .Returns(cacheableRequest);

            var middleware = new CacheHeaderMiddleware(
                ctx =>
                {
                    ctx.Response.StatusCode = statusCode;
                    downstream?.Invoke(ctx);
                    return Task.CompletedTask;
                },
                Mocker.GetMock<ICacheableSpecification>().Object);

            await middleware.InvokeAsync(_context);
            await _responseFeature.FireOnStartingAsync();
        }

        [Test]
        public async Task should_cache_successful_response_on_cacheable_request()
        {
            await Invoke(true, StatusCodes.Status200OK);

            _context.Response.Headers["Cache-Control"].ToString().Should().Be("max-age=31536000, public");
        }

        [Test]
        public async Task should_keep_cache_headers_on_not_modified_response()
        {
            await Invoke(true, StatusCodes.Status304NotModified);

            _context.Response.Headers["Cache-Control"].ToString().Should().Be("max-age=31536000, public");
        }

        [Test]
        public async Task should_not_cache_not_found_response_on_cacheable_request()
        {
            await Invoke(true, StatusCodes.Status404NotFound);

            _context.Response.Headers["Cache-Control"].ToString().Should().Be("no-cache, no-store");
            _context.Response.Headers.ContainsKey("Last-Modified").Should().BeFalse();
        }

        [Test]
        public async Task should_not_cache_server_error_response_on_cacheable_request()
        {
            await Invoke(true, StatusCodes.Status500InternalServerError);

            _context.Response.Headers["Cache-Control"].ToString().Should().Be("no-cache, no-store");
        }

        [Test]
        public async Task should_not_cache_redirect_response_on_cacheable_request()
        {
            await Invoke(true, StatusCodes.Status302Found);

            _context.Response.Headers["Cache-Control"].ToString().Should().Be("no-cache, no-store");
        }

        [Test]
        public async Task should_keep_downstream_no_cache_on_successful_response()
        {
            await Invoke(true, StatusCodes.Status200OK, ctx => ctx.Response.Headers.DisableCache());

            _context.Response.Headers["Cache-Control"].ToString().Should().Be("no-cache, no-store");
        }

        [Test]
        public async Task should_not_cache_response_on_non_cacheable_request()
        {
            await Invoke(false, StatusCodes.Status200OK);

            _context.Response.Headers["Cache-Control"].ToString().Should().Be("no-cache, no-store");
        }

        // DefaultHttpContext's response feature discards OnStarting callbacks; capture
        // them so the middleware's send-time header logic can be exercised in-process.
        private class OnStartingCaptureFeature : HttpResponseFeature
        {
            private readonly List<(Func<object, Task> Callback, object State)> _callbacks = new List<(Func<object, Task>, object)>();

            public override void OnStarting(Func<object, Task> callback, object state)
            {
                _callbacks.Add((callback, state));
            }

            public async Task FireOnStartingAsync()
            {
                // Kestrel runs OnStarting callbacks in reverse registration order.
                for (var i = _callbacks.Count - 1; i >= 0; i--)
                {
                    await _callbacks[i].Callback(_callbacks[i].State);
                }
            }
        }
    }
}
