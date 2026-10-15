using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ImageStore.Api.Configuration;
using ImageStore.Api.ErrorHandling;
using ImageStore.Api.Filters;
using ImageStore.Api.OpenApi;
using ImageStore.Api.Security;
using ImageStore.Application.Common;
using ImageStore.Application.Images.Validation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace ImageStore.Api.Hosting;

internal static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ImageUploadOptions>()
            .Bind(configuration.GetSection(ImageUploadOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<AuthCookieSettings>()
            .Bind(configuration.GetSection(AuthCookieSettings.SectionName))
            .Validate(
                settings => settings.SameSite != SameSiteMode.None || settings.SecurePolicy == CookieSecurePolicy.Always,
                "AuthCookies:SameSite=None requires AuthCookies:SecurePolicy=Always.")
            .Validate(settings => settings.ExpireHours > 0, "AuthCookies:ExpireHours must be positive.")
            .ValidateOnStart();
        services.AddOptions<CorsSettings>().Bind(configuration.GetSection(CorsSettings.SectionName));
        services.AddOptions<RateLimitingSettings>().Bind(configuration.GetSection(RateLimitingSettings.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<AntiforgeryTokenIssuer>();
        services.AddScoped<ImageUploadLimitsFilter>();

        services
            .AddControllers(options =>
            {
                // Every unsafe request (POST/PUT/PATCH/DELETE) must carry a valid anti-forgery token.
                options.Filters.Add<AntiforgeryValidationFilter>();
            })
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier));
        services.AddExceptionHandler<ApiExceptionHandler>();

        AddAuthentication(services);
        AddForwardedHeaders(services, configuration);
        AddCors(services);
        AddRateLimiting(services);
        AddSwagger(services);
        AddDataProtection(services, configuration);

        return services;
    }

    private static void AddAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IOptions<AuthCookieSettings>>((options, settings) =>
            {
                var cookie = settings.Value;
                options.Cookie.Name = cookie.CookieName(CookieBaseNames.Authentication);
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = cookie.SameSite;
                options.Cookie.SecurePolicy = cookie.SecurePolicy;
                options.ExpireTimeSpan = TimeSpan.FromHours(cookie.ExpireHours);
                options.SlidingExpiration = true;

                // An API answers with status codes instead of redirecting to login pages.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToLogout = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return Task.CompletedTask;
                };
            });

        services.AddAntiforgery();
        services.AddOptions<AntiforgeryOptions>()
            .Configure<IOptions<AuthCookieSettings>>((options, settings) =>
            {
                var cookie = settings.Value;
                options.HeaderName = AntiforgeryDefaults.HeaderName;
                options.Cookie.Name = cookie.CookieName(CookieBaseNames.Antiforgery);
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = cookie.SameSite;
                options.Cookie.SecurePolicy = cookie.SecurePolicy;
                options.SuppressXFrameOptionsHeader = true; // Emitted globally by SecurityHeadersMiddleware.
            });

        services.AddAuthorizationBuilder()
            // Secure by default: every endpoint requires an authenticated user unless it opts out.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AuthorizationPolicies.AuthenticatedUser, policy =>
                policy.RequireAuthenticatedUser().RequireClaim(ClaimTypes.NameIdentifier));
    }

    private static void AddForwardedHeaders(IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>() ?? new();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            // Loopback proxies are trusted by default; add the configured ones.
            foreach (var proxy in settings.KnownProxies.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in settings.KnownNetworks.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
    }

    private static void AddCors(IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
            .Configure<IOptions<CorsSettings>>((options, settings) =>
            {
                var origins = settings.Value.AllowedOrigins.Where(origin => !string.IsNullOrWhiteSpace(origin)).ToArray();
                if (origins.Length == 0)
                {
                    return; // Same-origin only (recommended: serve the SPA and API from one origin).
                }

                options.AddDefaultPolicy(policy => policy
                    .WithOrigins(origins)
                    .AllowCredentials()
                    .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete)
                    .WithHeaders("Content-Type", AntiforgeryDefaults.HeaderName)
                    .WithExposedHeaders("Content-Disposition", "Location"));
            });
    }

    private static void AddRateLimiting(IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Too many attempts. Please wait a moment and try again.",
                    },
                });
            };

            // Slows down credential stuffing and account enumeration, partitioned per client IP.
            // Behind a reverse proxy, enable forwarded headers so the real client IP is used.
            options.AddPolicy(RateLimitPolicies.Authentication, httpContext =>
            {
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value.Authentication;
                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, settings.PermitLimit),
                        Window = TimeSpan.FromSeconds(Math.Max(1, settings.WindowSeconds)),
                        QueueLimit = 0,
                    });
            });
        });

    private static void AddSwagger(IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ImageStore API",
                Version = "v1",
                Description =
                    "Private image storage. Authentication uses an HttpOnly cookie: call POST /api/auth/login " +
                    "(or register) first. State-changing requests require the X-XSRF-TOKEN header – Swagger UI " +
                    "adds it automatically after GET /api/auth/csrf-token.",
            });

            foreach (var xmlFile in new[] { "ImageStore.Api.xml", "ImageStore.Application.xml" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(path))
                {
                    options.IncludeXmlComments(path);
                }
            }

            options.AddSecurityDefinition(SecurityOperationFilter.CookieSchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = CookieBaseNames.Authentication,
                Description = "HttpOnly authentication cookie set by /api/auth/login (prefixed with __Host- when cookies are always Secure).",
            });
            options.OperationFilter<SecurityOperationFilter>();
        });
    }

    private static void AddDataProtection(IServiceCollection services, IConfiguration configuration)
    {
        // Cookies and anti-forgery tokens are encrypted with Data Protection keys. Persist them
        // (and share them between instances) so sessions survive restarts and redeployments.
        var builder = services.AddDataProtection().SetApplicationName("ImageStore");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            builder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }
    }
}
