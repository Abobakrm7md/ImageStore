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

/// <summary>
/// Reverse-proxy support. When the API runs behind a TLS-terminating proxy (nginx, a load
/// balancer, a container ingress), enable this so the original scheme and client IP are used –
/// otherwise Secure cookies, HTTPS redirection and per-IP rate limiting see the proxy instead.
/// Only requests from the listed proxies/networks (plus loopback) are trusted.
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; set; }

    /// <summary>IP addresses of trusted proxies, e.g. <c>10.0.0.5</c>.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Trusted proxy networks in CIDR notation, e.g. <c>10.0.0.0/8</c>.</summary>
    public string[] KnownNetworks { get; set; } = [];
}
