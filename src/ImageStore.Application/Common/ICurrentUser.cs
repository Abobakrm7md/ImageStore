namespace ImageStore.Application.Common;

/// <summary>
/// Provides the identity of the user making the current request. Application services use this
/// to scope every query and mutation, so ownership is always derived from the authenticated
/// principal instead of from request data.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user's identifier, or <c>null</c> for anonymous requests.</summary>
    Guid? UserId { get; }
}

public static class CurrentUserExtensions
{
    public static Guid GetRequiredUserId(this ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        return currentUser.UserId ?? throw new UnauthenticatedException();
    }
}
