using ImageStore.Application.Common;
using ImageStore.Application.Images.Storage;
using ImageStore.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ImageStore.Api.ErrorHandling;

/// <summary>
/// Maps known exceptions to RFC 7807 Problem Details. Unknown exceptions fall through to the
/// default handler, which returns a generic 500 without exception details or file paths.
/// </summary>
internal sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to send a response to.
            LogRequestAborted(logger, httpContext.Request.Path);
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var problem = exception switch
        {
            RequestValidationException validation => Validation(validation.Errors),
            DomainValidationException domain => Validation(new Dictionary<string, string[]> { [domain.PropertyName] = [domain.Message] }),
            NotFoundException notFound => Create(StatusCodes.Status404NotFound, "Not found", notFound.Message),

            // Same response as a missing image: never reveal storage details.
            StoredFileNotFoundException or InvalidStoragePathException =>
                Create(StatusCodes.Status404NotFound, "Not found", "The image content is unavailable."),
            UnauthenticatedException => Create(StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication is required."),
            ConflictException conflict => Create(StatusCodes.Status409Conflict, "Conflict", conflict.Message),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                Create(StatusCodes.Status413PayloadTooLarge, "Payload too large", "The request body is too large."),
            BadHttpRequestException badRequest => Create(badRequest.StatusCode, "Bad request", "The request could not be processed."),
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ValidationProblemDetails Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(errors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };

    private static ProblemDetails Create(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    [LoggerMessage(Level = LogLevel.Information, Message = "Request to {Path} was aborted by the client.")]
    private static partial void LogRequestAborted(ILogger logger, PathString path);
}
