using ImageStore.Application.Images.Validation;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace ImageStore.Api.Filters;

/// <summary>
/// Raises the request-size limits for the upload endpoint only, derived from configuration
/// (max file size × max files + multipart overhead). Every other endpoint keeps the server's
/// default limit. The form is read here, before model binding, so oversized or malformed
/// bodies are rejected with a clear 413/400 response.
/// </summary>
internal sealed class ImageUploadLimitsFilter(
    IOptions<ImageUploadOptions> uploadOptions,
    ProblemDetailsFactory problemDetailsFactory) : IAsyncResourceFilter
{
    internal const long MultipartOverheadBytes = 1024 * 1024;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var options = uploadOptions.Value;
        var maxBodyBytes = checked((options.MaxFileSizeBytes * options.MaxFilesPerRequest) + MultipartOverheadBytes);
        var httpContext = context.HttpContext;

        var bodySizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = maxBodyBytes;
        }

        if (httpContext.Request.HasFormContentType)
        {
            if (httpContext.Features.Get<IFormFeature>()?.Form is null)
            {
                httpContext.Features.Set<IFormFeature>(new FormFeature(httpContext.Request, new FormOptions
                {
                    MultipartBodyLengthLimit = maxBodyBytes,
                    ValueCountLimit = 256,
                }));
            }

            try
            {
                await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
            }
            catch (BadHttpRequestException exception)
            {
                context.Result = Problem(httpContext, exception.StatusCode);
                return;
            }
            catch (InvalidDataException exception)
            {
                // FormFeature reports exceeded limits ("... limit ... exceeded") and malformed
                // multipart bodies with the same exception type.
                var tooLarge = exception.Message.Contains("limit", StringComparison.OrdinalIgnoreCase);
                context.Result = Problem(httpContext, tooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest);
                return;
            }
        }

        await next();
    }

    private ObjectResult Problem(HttpContext httpContext, int statusCode)
    {
        var tooLarge = statusCode == StatusCodes.Status413PayloadTooLarge;
        var options = uploadOptions.Value;
        var problem = problemDetailsFactory.CreateProblemDetails(
            httpContext,
            statusCode,
            title: tooLarge ? "Payload too large" : "Invalid upload request",
            detail: tooLarge
                ? $"Uploads are limited to {options.MaxFilesPerRequest} files of at most {options.MaxFileSizeBytes} bytes each."
                : "The multipart form data could not be read.");

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
