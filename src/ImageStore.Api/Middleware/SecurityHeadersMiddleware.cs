namespace ImageStore.Api.Middleware;

/// <summary>Adds defensive HTTP headers to every response.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // API responses (JSON and image bytes) never need to run scripts or be framed.
    private const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; sandbox";

    // The Angular SPA (when served from wwwroot). Component styles are injected at runtime,
    // hence 'unsafe-inline' for styles only; blob: is needed for upload previews.
    private const string SpaContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(
            static state =>
            {
                var httpContext = (HttpContext)state;
                var headers = httpContext.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

                var path = httpContext.Request.Path;
                if (path.StartsWithSegments("/swagger"))
                {
                    // Swagger UI relies on inline scripts and styles; it is disabled in production by default.
                }
                else if (path.StartsWithSegments("/api"))
                {
                    headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
                }
                else
                {
                    headers.ContentSecurityPolicy = SpaContentSecurityPolicy;
                }

                return Task.CompletedTask;
            },
            context);

        return next(context);
    }
}
