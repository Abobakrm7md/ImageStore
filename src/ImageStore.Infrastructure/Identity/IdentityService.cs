using ImageStore.Application.Auth;
using ImageStore.Domain.Social;
using ImageStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImageStore.Infrastructure.Identity;

internal sealed partial class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ImageStoreDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<IdentityService> logger) : IIdentityService
{
    private const int MaxUserNameAttempts = 5;

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

        try
        {
            // Every account gets a social profile (unique user name derived from the email).
            await CreateProfileAsync(user, cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
        {
            // A profile is part of a usable account: roll the registration back instead of
            // leaving a user row that the social features cannot resolve.
            LogProfileCreationFailed(logger, exception, user.Id);
            var rollback = await userManager.DeleteAsync(user);
            if (!rollback.Succeeded)
            {
                LogRollbackFailed(logger, user.Id);
            }

            return new RegistrationResult(
                null,
                null,
                new Dictionary<string, string[]> { [string.Empty] = ["Registration failed. Please try again."] });
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

    /// <summary>
    /// Creates the profile for a new account. The user name is derived from the email's local
    /// part; taken names get a numeric suffix, and a concurrent unique-key violation retries
    /// with the next candidate. The user name is immutable afterwards.
    /// </summary>
    private async Task CreateProfileAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var email = user.Email ?? user.UserName ?? string.Empty;
        var suggested = UserProfile.SuggestUserName(email);
        if (!UserProfile.IsValidUserName(suggested))
        {
            suggested = "user";
        }

        var displayName = DisplayNameFromEmail(email);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        for (var attempt = 1; attempt <= MaxUserNameAttempts; attempt++)
        {
            var candidate = attempt == 1 ? suggested : WithSuffix(suggested, attempt);

            var profile = UserProfile.Create(user.Id, candidate, displayName, now);
            dbContext.UserProfiles.Add(profile);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                dbContext.Entry(profile).State = EntityState.Detached;
            }
        }

        // Practically unreachable; a random name always succeeds (17 valid characters).
        var fallback = UserProfile.Create(user.Id, $"user-{Guid.NewGuid():N}"[..17], displayName, now);
        dbContext.UserProfiles.Add(fallback);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string WithSuffix(string baseName, int attempt)
    {
        var suffix = "-" + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var maxBaseLength = SocialRules.UserNameMaxLength - suffix.Length;
        var truncated = baseName.Length > maxBaseLength ? baseName[..maxBaseLength] : baseName;

        // The name must still end with a letter or digit.
        while (truncated.Length > 0 && !char.IsAsciiLetterOrDigit(truncated[^1]))
        {
            truncated = truncated[..^1];
        }

        return truncated.Length == 0 ? "user" + suffix : truncated + suffix;
    }

    internal static string DisplayNameFromEmail(string email)
    {
        var localPart = email.Split('@')[0].Trim();
        if (localPart.Length == 0)
        {
            return "User";
        }

        return localPart.Length > SocialRules.DisplayNameMaxLength
            ? localPart[..SocialRules.DisplayNameMaxLength]
            : localPart;
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

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not create the profile for user {UserId}; rolling the registration back.")]
    private static partial void LogProfileCreationFailed(ILogger logger, Exception exception, Guid userId);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Registration of user {UserId} failed and the account could not be removed; it has no profile.")]
    private static partial void LogRollbackFailed(ILogger logger, Guid userId);
}
