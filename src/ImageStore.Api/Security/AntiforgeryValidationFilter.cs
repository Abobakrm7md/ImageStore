using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ImageStore.Api.Security;

/// <summary>
/// Global filter: every unsafe request (POST, PUT, PATCH, DELETE) must carry a valid anti-forgery
/// token in the <c>X-XSRF-TOKEN</c> header. Failures return Problem Details with a
/// machine-readable code so the SPA can refresh its token and retry once.
/// An action can opt out with <c>[IgnoreAntiforgeryToken]</c>.
/// </summary>
internal sealed partial class AntiforgeryValidationFilter(
    IAntiforgery antiforgery,
    ProblemDetailsFactory problemDetailsFactory,
    ILogger<AntiforgeryValidationFilter> logger) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return;
        }

        if (context.FindEffectivePolicy<IAntiforgeryPolicy>() is IgnoreAntiforgeryTokenAttribute)
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException exception)
        {
            LogValidationFailed(logger, context.HttpContext.Request.Path, exception.Message);

            var problem = problemDetailsFactory.CreateProblemDetails(
                context.HttpContext,
                StatusCodes.Status400BadRequest,
                title: "Invalid anti-forgery token",
                detail: $"Send the value of the {AntiforgeryDefaults.RequestTokenCookieName} cookie in the {AntiforgeryDefaults.HeaderName} header. A fresh token is available from GET /api/auth/csrf-token.");
            problem.Extensions["code"] = AntiforgeryDefaults.ValidationFailedCode;

            context.Result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" },
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anti-forgery validation failed for {Path}: {Reason}")]
    private static partial void LogValidationFailed(ILogger logger, PathString path, string reason);
}
