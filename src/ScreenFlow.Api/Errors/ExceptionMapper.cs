using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenFlow.Domain;

namespace ScreenFlow.Api.Errors;

/// <summary>
/// Turns exceptions into contract error codes in one place, so endpoints stay free of try/catch.
/// Unknown exceptions become a generic 500: the message is logged, never returned, because an
/// exception message is exactly where sensitive data tends to leak.
/// </summary>
public sealed partial class ExceptionMapper(IProblemDetailsService problemDetails, ILogger<ExceptionMapper> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail, code, slug) = exception switch
        {
            InvalidTransitionException e =>
                (409, "Invalid status transition", e.Message, ErrorCodes.InvalidTransition, "invalid-transition"),
            SearchNotFoundException e =>
                (404, "Not found", e.Message, ErrorCodes.NotFound, "not-found"),
            DbUpdateConcurrencyException =>
                (409, "Concurrency conflict", "The order was changed by another request. Re-read it and retry.",
                    ErrorCodes.ConcurrencyConflict, "concurrency-conflict"),
            BadHttpRequestException =>
                (400, "Validation failed", "The request body or parameters could not be read.",
                    ErrorCodes.ValidationFailed, "validation-failed"),
            _ =>
                (500, "Internal error", "An unexpected error occurred.", ErrorCodes.InternalError, "internal-error"),
        };

        if (status == 500)
        {
            LogUnhandled(exception);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Type = "https://screenflow.dev/errors/" + slug,
                Extensions = { ["code"] = code },
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private partial void LogUnhandled(Exception exception);
}
