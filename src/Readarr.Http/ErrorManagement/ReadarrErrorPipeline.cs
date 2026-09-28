using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Localization;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Validation;
using Readarr.Http.Exceptions;

namespace Readarr.Http.ErrorManagement
{
    public class ReadarrErrorPipeline
    {
        private readonly Logger _logger;

        // Server messages (2026-09-26, final review): the localizer is resolved while handling an exception, never
        // with the pipeline. Startup.Configure takes this pipeline as a parameter, and the localizer pulls
        // ConfigService -> ConfigRepository -> IMainDatabase, so resolving it here would open (and migrate) the
        // main database before Configure's logger init, pid file and single-instance check. Lazy<T> can't defer
        // it: DryIoc creates a wrapped service's singleton dependencies while it builds the Lazy's expression.
        private readonly IServiceFactory _serviceFactory;
        private bool _localizerFailed;

        public ReadarrErrorPipeline(Logger logger, IServiceFactory serviceFactory)
        {
            _logger = logger;
            _serviceFactory = serviceFactory;
        }

        public async Task HandleException(HttpContext context)
        {
            _logger.Trace("Handling Exception");

            var response = context.Response;
            var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
            var exception = exceptionHandlerPathFeature?.Error;

            var statusCode = HttpStatusCode.InternalServerError;
            var errorModel = new ErrorModel
            {
                Message = exception?.Message,
                Description = exception?.ToString()
            };

            if (exception is ApiException apiException)
            {
                _logger.Warn(apiException, "API Error:\n{0}", apiException.Message);

                errorModel = new ErrorModel(apiException);
                statusCode = apiException.StatusCode;

                // Server messages (2026-09-26): the UI language, after the English log line. The message keeps
                // ApiException's "{StatusCode}: " prefix.
                if (apiException.Content is string content)
                {
                    var localized = Localize(content, apiException.Text);

                    if (localized != content)
                    {
                        errorModel.Message = $"{apiException.StatusCode}: {localized}";
                        errorModel.Content = localized;
                    }
                }
            }
            else if (exception is ValidationException validationException)
            {
                // Single-line with method+path: the FluentValidation Message is multi-line, which
                // reads as a phantom empty entry in docker logs, and without the request context
                // there's no telling a Prowlarr sync PUT from a UI test POST.
                var failures = validationException.Errors?
                    .Select(f => string.Join(": ", new[] { f.PropertyName, f.ErrorMessage }.Where(s => !string.IsNullOrWhiteSpace(s))))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                _logger.Warn("Invalid request [{0} {1}] Validation failed: {2}",
                    context.Request.Method,
                    context.Request.Path,
                    failures?.Any() == true ? string.Join("; ", failures) : "(no failure details)");

                // Server messages (2026-09-26): validation and provider Test failures in the UI language, for the
                // response only -- the log line above is written, and the exception ends here.
                LocalizeFailures(validationException.Errors);

                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.ContentType = "application/json";
                await response.WriteAsync(STJson.ToJson(validationException.Errors));
                return;
            }
            else if (exception is NzbDroneClientException clientException)
            {
                statusCode = clientException.StatusCode;
            }
            else if (exception is ModelNotFoundException)
            {
                statusCode = HttpStatusCode.NotFound;
            }
            else if (exception is ModelConflictException)
            {
                statusCode = HttpStatusCode.Conflict;
            }
            else if (exception is SQLiteException sqLiteException)
            {
                if (context.Request.Method == "PUT" || context.Request.Method == "POST")
                {
                    if (sqLiteException.Message.Contains("constraint failed"))
                    {
                        statusCode = HttpStatusCode.Conflict;
                    }
                }

                _logger.Error(sqLiteException, "[{0} {1}]", context.Request.Method, context.Request.Path);
            }
            else
            {
                _logger.Fatal(exception, "Request Failed. {0} {1}", context.Request.Method, context.Request.Path);
            }

            // Server messages (2026-09-26): an NzbDrone exception's message in the UI language (its template when
            // it was built from one), after the log lines above. Description keeps the English stack trace.
            if (exception is NzbDroneException nzbDroneException)
            {
                errorModel.Message = Localize(nzbDroneException.Message, nzbDroneException.Text);
            }

            await errorModel.WriteToResponse(response, statusCode);
        }

        // Server messages (2026-09-26, final review): a localizer failure must never turn the error response
        // being written into a bodiless 500 -- the English is always a correct answer.
        private string Localize(string english, ServerText text)
        {
            try
            {
                return _serviceFactory.Build<IServerMessageLocalizer>().Localize(english, text);
            }
            catch (Exception ex)
            {
                LocalizerFailed(ex);
                return english;
            }
        }

        // Localizes in place (the exception ends here); a failure part-way restores every message's English,
        // so the response is never a mix of languages.
        private void LocalizeFailures(IEnumerable<ValidationFailure> failures)
        {
            var list = failures?.ToList();

            if (list == null || list.Count == 0)
            {
                return;
            }

            var english = list.Select(f => f?.ErrorMessage).ToList();
            var details = list.Select(f => (f as NzbDroneValidationFailure)?.DetailedDescription).ToList();

            try
            {
                _serviceFactory.Build<IServerMessageLocalizer>().Localize(list);
            }
            catch (Exception ex)
            {
                LocalizerFailed(ex);

                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] != null)
                    {
                        list[i].ErrorMessage = english[i];
                    }

                    if (list[i] is NzbDroneValidationFailure nzbDroneFailure)
                    {
                        nzbDroneFailure.DetailedDescription = details[i];
                    }
                }
            }
        }

        // Warn once, then Debug: a localizer that stays broken must not add a stack trace to every API error.
        private void LocalizerFailed(Exception ex)
        {
            if (_localizerFailed)
            {
                _logger.Debug(ex, "Unable to localize an error response, using the English");
                return;
            }

            _localizerFailed = true;
            _logger.Warn(ex, "Unable to localize an error response, using the English");
        }
    }
}
