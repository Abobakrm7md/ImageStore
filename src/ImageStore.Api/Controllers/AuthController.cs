using System.Security.Claims;
using ImageStore.Api.Security;
using ImageStore.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ImageStore.Api.Controllers;

/// <summary>Cookie-based authentication for the SPA.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService, AntiforgeryTokenIssuer antiforgeryTokens) : ControllerBase
{
    /// <summary>
    /// Issues the anti-forgery token. The token is returned in the JS-readable <c>XSRF-TOKEN</c>
    /// cookie and must be echoed in the <c>X-XSRF-TOKEN</c> header of every POST/PUT/DELETE.
    /// </summary>
    [HttpGet("csrf-token")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult GetCsrfToken()
    {
        antiforgeryTokens.Issue(HttpContext);
        return NoContent();
    }

    /// <summary>Creates an account and signs the new user in (sets the authentication cookie).</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<ActionResult<CurrentUserDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        RefreshAntiforgeryTokens(result.Principal!);
        return CreatedAtAction(nameof(Me), result.User);
    }

    /// <summary>Signs in with email and password (sets the authentication cookie).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<ActionResult<CurrentUserDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            // One generic message for unknown users, wrong passwords and locked accounts, so the
            // response does not reveal which accounts exist.
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Sign-in failed",
                detail: "Invalid email or password. Repeated failures temporarily lock the account.");
        }

        RefreshAntiforgeryTokens(result.Principal!);
        return Ok(result.User);
    }

    /// <summary>Signs out (clears the authentication cookie). Succeeds even if the session already expired.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await authService.LogoutAsync();
        RefreshAntiforgeryTokens(new ClaimsPrincipal(new ClaimsIdentity()));
        return NoContent();
    }

    /// <summary>Returns the signed-in user.</summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken cancellationToken)
    {
        var user = await authService.GetCurrentUserAsync(cancellationToken);
        return user is null
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "The account no longer exists.")
            : Ok(user);
    }

    /// <summary>Anti-forgery tokens are bound to the user, so they are re-issued whenever it changes.</summary>
    private void RefreshAntiforgeryTokens(ClaimsPrincipal principal)
    {
        HttpContext.User = principal;
        antiforgeryTokens.Issue(HttpContext);
    }
}
