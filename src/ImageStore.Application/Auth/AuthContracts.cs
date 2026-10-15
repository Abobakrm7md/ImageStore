using System.Security.Claims;

namespace ImageStore.Application.Auth;

/// <summary>Registration request.</summary>
/// <param name="Email">The email address, used as the user name.</param>
/// <param name="Password">At least 8 characters with upper-case, lower-case and digit characters.</param>
public sealed record RegisterRequest(string? Email, string? Password);

/// <summary>Login request.</summary>
/// <param name="Email">The account email address.</param>
/// <param name="Password">The account password.</param>
/// <param name="RememberMe">Keep the session cookie across browser restarts.</param>
public sealed record LoginRequest(string? Email, string? Password, bool RememberMe = false);

/// <summary>The authenticated user.</summary>
/// <param name="Id">The user identifier.</param>
/// <param name="Email">The user's email address.</param>
public sealed record CurrentUserDto(Guid Id, string Email);

public enum SignInStatus
{
    Succeeded,
    Failed,
    LockedOut,
    NotAllowed,
}

/// <summary>
/// Outcome of a sign-in. <see cref="Principal"/> is the newly signed-in identity, used by the
/// API to issue anti-forgery tokens bound to that user within the same response.
/// </summary>
public sealed record AuthResult(SignInStatus Status, CurrentUserDto? User, ClaimsPrincipal? Principal)
{
    public bool Succeeded => Status == SignInStatus.Succeeded;

    public static AuthResult Failure(SignInStatus status) => new(status, null, null);
}

/// <summary>Result of a registration attempt; on failure contains field errors.</summary>
public sealed record RegistrationResult(
    CurrentUserDto? User,
    ClaimsPrincipal? Principal,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => User is not null;
}

/// <summary>Port to the identity system (implemented with ASP.NET Core Identity).</summary>
public interface IIdentityService
{
    /// <summary>Creates the account and signs the new user in.</summary>
    Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken cancellationToken);

    Task<AuthResult> PasswordSignInAsync(string email, string password, bool rememberMe, CancellationToken cancellationToken);

    Task SignOutAsync();

    Task<CurrentUserDto?> FindUserAsync(Guid userId, CancellationToken cancellationToken);
}
