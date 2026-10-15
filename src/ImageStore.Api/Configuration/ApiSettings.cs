using Microsoft.AspNetCore.Http;

namespace ImageStore.Api.Configuration;

/// <summary>Cookie hardening for the authentication, anti-forgery and XSRF-TOKEN cookies.</summary>
public sealed class AuthCookieSettings
{
    public const string SectionName = "AuthCookies";

    /// <summary>
    /// <c>Strict</c> by default. Use <c>None</c> only when the SPA must run inside a cross-site
    /// iframe; CSRF protection then relies entirely on the anti-forgery token.
    /// </summary>
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Strict;

    /// <summary><c>Always</c> in production. <c>SameAsRequest</c> allows plain-HTTP local development.</summary>
    public CookieSecurePolicy SecurePolicy { get; set; } = CookieSecurePolicy.Always;

    public int ExpireHours { get; set; } = 8;

    /// <summary>
    /// The <c>__Host-</c> prefix (browser-enforced: Secure, Path=/, no Domain) is only valid for
    /// cookies that are always Secure.
    /// </summary>
    public string CookieName(string baseName) =>
        SecurePolicy == CookieSecurePolicy.Always ? "__Host-" + baseName : baseName;

    public bool IsSecure(HttpRequest request) =>
        SecurePolicy == CookieSecurePolicy.Always || (SecurePolicy == CookieSecurePolicy.SameAsRequest && request.IsHttps);
}

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    /// <summary>Origins allowed to call the API with credentials. Empty = same-origin only.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    public FixedWindowSettings Authentication { get; set; } = new();
}

public sealed class FixedWindowSettings
{
    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;
}

public sealed class SwaggerSettings
{
    public const string SectionName = "Swagger";

    public bool Enabled { get; set; }
}
