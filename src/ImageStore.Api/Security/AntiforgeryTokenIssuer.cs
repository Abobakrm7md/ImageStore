using ImageStore.Api.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace ImageStore.Api.Security;

/// <summary>
/// Implements the "double submit" pattern used by Angular's HttpClient: the anti-forgery
/// cookie token stays HttpOnly, while the request token is exposed in the JS-readable
/// <c>XSRF-TOKEN</c> cookie. The SPA echoes it in the <c>X-XSRF-TOKEN</c> header, which a
/// cross-site attacker cannot read or forge.
/// </summary>
public sealed class AntiforgeryTokenIssuer(IAntiforgery antiforgery, IOptions<AuthCookieSettings> settings)
{
    /// <summary>
    /// Issues tokens bound to <see cref="HttpContext.User"/>. After sign-in or sign-out the caller
    /// must set <c>HttpContext.User</c> to the new principal first, because tokens are tied to
    /// the user identity.
    /// </summary>
    public void Issue(HttpContext httpContext)
    {
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        var cookieSettings = settings.Value;

        httpContext.Response.Cookies.Append(
            AntiforgeryDefaults.RequestTokenCookieName,
            tokens.RequestToken!,
            new CookieOptions
            {
                HttpOnly = false, // Must be readable by the SPA.
                Secure = cookieSettings.IsSecure(httpContext.Request),
                SameSite = cookieSettings.SameSite,
                Path = "/",
                IsEssential = true,
            });
    }
}
