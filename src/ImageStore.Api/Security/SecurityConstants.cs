namespace ImageStore.Api.Security;

public static class AuthorizationPolicies
{
    /// <summary>An authenticated user whose identifier claim is present.</summary>
    public const string AuthenticatedUser = nameof(AuthenticatedUser);
}

public static class RateLimitPolicies
{
    public const string Authentication = nameof(Authentication);
}

public static class CookieBaseNames
{
    public const string Authentication = "ImageStore.Auth";
    public const string Antiforgery = "ImageStore.Antiforgery";
}

public static class AntiforgeryDefaults
{
    /// <summary>JS-readable cookie holding the request token (Angular's default name).</summary>
    public const string RequestTokenCookieName = "XSRF-TOKEN";

    /// <summary>Header the SPA echoes the token in (Angular's default name).</summary>
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>Machine-readable error code so clients can refresh the token and retry.</summary>
    public const string ValidationFailedCode = "antiforgery_validation_failed";
}
