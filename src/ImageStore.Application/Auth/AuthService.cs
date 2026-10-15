using ImageStore.Application.Common;

namespace ImageStore.Application.Auth;

public interface IAuthService
{
    Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task LogoutAsync();

    Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken);
}

/// <summary>Validates authentication requests and delegates to the identity system.</summary>
public sealed class AuthService(IIdentityService identityService, ICurrentUser currentUser) : IAuthService
{
    public const int EmailMaxLength = 256;
    public const int PasswordMaxLength = 128;

    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ValidationErrors();
        var email = ValidateEmail(request.Email, errors);
        ValidatePassword(request.Password, errors);
        errors.ThrowIfAny();

        var result = await identityService.RegisterAsync(email, request.Password!, cancellationToken);
        if (!result.Succeeded)
        {
            throw new RequestValidationException(result.Errors);
        }

        return result;
    }

    public Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ValidationErrors();
        var email = ValidateEmail(request.Email, errors);
        ValidatePassword(request.Password, errors);
        errors.ThrowIfAny();

        return identityService.PasswordSignInAsync(email, request.Password!, request.RememberMe, cancellationToken);
    }

    public Task LogoutAsync() => identityService.SignOutAsync();

    public async Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.GetRequiredUserId();
        return await identityService.FindUserAsync(userId, cancellationToken);
    }

    private static string ValidateEmail(string? email, ValidationErrors errors)
    {
        var trimmed = email?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            errors.Add("email", "Email is required.");
        }
        else if (trimmed.Length > EmailMaxLength)
        {
            errors.Add("email", $"Email must be at most {EmailMaxLength} characters long.");
        }
        else if (trimmed.IndexOf('@', StringComparison.Ordinal) is <= 0 || trimmed.EndsWith('@'))
        {
            errors.Add("email", "Enter a valid email address.");
        }

        return trimmed;
    }

    private static void ValidatePassword(string? password, ValidationErrors errors)
    {
        if (string.IsNullOrEmpty(password))
        {
            errors.Add("password", "Password is required.");
        }
        else if (password.Length > PasswordMaxLength)
        {
            errors.Add("password", $"Password must be at most {PasswordMaxLength} characters long.");
        }
    }
}
