using ImageStore.Api.Configuration;
using ImageStore.Api.Middleware;
using ImageStore.Infrastructure.Storage;

namespace ImageStore.Api.Hosting;

internal static class WebApplicationExtensions
{
    /// <summary>Any path without a file extension that does not start with <c>api/</c>.</summary>
    internal const string SpaFallbackPattern = "{*path:nonfile:regex(^(?!api/).*$)}";

    /// <summary>
    /// Refuses to start if the upload directory is inside the publicly served web root, where the
    /// static-file middleware could expose it.
    /// </summary>
    public static void EnsureStorageIsOutsideWebRoot(this WebApplication app)
    {
        var storageRoot = app.Services.GetRequiredService<LocalImageFileStorage>().RootPath;
        var webRoot = app.Environment.WebRootPath;
        if (string.IsNullOrEmpty(webRoot))
        {
            return;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedWebRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(webRoot)) + Path.DirectorySeparatorChar;
        var normalizedStorage = Path.TrimEndingDirectorySeparator(storageRoot) + Path.DirectorySeparatorChar;
        if (normalizedStorage.StartsWith(normalizedWebRoot, comparison) || normalizedWebRoot.StartsWith(normalizedStorage, comparison))
        {
            throw new InvalidOperationException("Storage:RootPath must not overlap the web root (wwwroot).");
        }
    }

    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        if (app.Services.GetRequiredService<IConfiguration>().GetValue<bool>($"{SwaggerSettings.SectionName}:Enabled"))
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "ImageStore API v1");
                options.DocumentTitle = "ImageStore API";
                // Echo the XSRF-TOKEN cookie in the anti-forgery header, like the Angular client does.
                options.UseRequestInterceptor(
                    "(request) => { const match = document.cookie.match(/(?:^|;\\s*)XSRF-TOKEN=([^;]+)/); " +
                    "if (match) { request.headers['X-XSRF-TOKEN'] = decodeURIComponent(match[1]); } return request; }");
            });
        }

        // Serves the built Angular app from wwwroot in single-server deployments. Uploaded images
        // live outside wwwroot and are only reachable through the authorized API endpoints.
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers.CacheControl = "no-cache";
                }
            },
        });

        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapControllers();
        app.MapHealthChecks("/health").AllowAnonymous();

        // Client-side routes of the SPA fall back to index.html. API paths are excluded so that
        // unknown API routes (404) and content-type mismatches (415) keep their Problem Details
        // responses instead of being answered with the SPA shell.
        if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html")))
        {
            app.MapFallbackToFile(SpaFallbackPattern, "index.html", new StaticFileOptions
            {
                OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
            }).AllowAnonymous();
        }

        return app;
    }
}
