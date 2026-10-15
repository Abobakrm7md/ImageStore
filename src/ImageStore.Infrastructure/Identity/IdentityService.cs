using ImageStore.Application.Auth;
using Microsoft.AspNetCore.Identity;

namespace ImageStore.Infrastructure.Identity;

internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    TimeProvider timeProvider) : IIdentityService
{
    private static readonly ApplicationUser TimingDummyUser = new() { UserName = "timing-dummy" };

    // A real hash of a random secret, used to spend the same time on unknown accounts.
    private static readonly Lazy<string> TimingDummyHash = new(() =>
        new PasswordHasher<ApplicationUser>().HashPassword(TimingDummyUser, Guid.NewGuid().ToString("N")));

    public async Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return new RegistrationResult(null, null, MapErrors(result.Errors));
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        return new RegistrationResult(ToDto(user), principal, new Dictionary<string, string[]>());
    }

    public async Task<AuthResult> PasswordSignInAsync(string email, string password, bool rememberMe, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Hash anyway so response timing does not reveal whether the account exists.
            userManager.PasswordHasher.VerifyHashedPassword(TimingDummyUser, TimingDummyHash.Value, password);
            return AuthResult.Failure(SignInStatus.Failed);
        }

        var result = await signInManager.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            var principal = await signInManager.CreateUserPrincipalAsync(user);
            return new AuthResult(SignInStatus.Succeeded, ToDto(user), principal);
        }

        return AuthResult.Failure(result.IsLockedOut
            ? SignInStatus.LockedOut
            : result.IsNotAllowed ? SignInStatus.NotAllowed : SignInStatus.Failed);
    }

    public Task SignOutAsync() => signInManager.SignOutAsync();

    public async Task<CurrentUserDto?> FindUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : ToDto(user);
    }

    private static CurrentUserDto ToDto(ApplicationUser user) => new(user.Id, user.Email ?? user.UserName ?? string.Empty);

    /// <summary>Maps Identity error codes onto request fields with user-friendly messages.</summary>
    private static Dictionary<string, string[]> MapErrors(IEnumerable<IdentityError> errors)
    {
        var mapped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var error in errors)
        {
            var (field, message) = error.Code switch
            {
                nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)
                    => ("email", "An account with this email address already exists."),
                nameof(IdentityErrorDescriber.InvalidEmail)
                    => ("email", "Enter a valid email address."),
                nameof(IdentityErrorDescriber.InvalidUserName)
                    => ("email", "The email address contains characters that are not allowed."),
                _ when error.Code.StartsWith("Password", StringComparison.Ordinal)
                    => ("password", error.Description),
                _ => (string.Empty, error.Description),
            };

            if (!mapped.TryGetValue(field, out var messages))
            {
                messages = [];
                mapped[field] = messages;
            }

            if (!messages.Contains(message, StringComparer.Ordinal))
            {
                messages.Add(message);
            }
        }

        return mapped.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }
}
