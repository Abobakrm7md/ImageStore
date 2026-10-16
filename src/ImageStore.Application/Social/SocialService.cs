using ImageStore.Application.Common;
using ImageStore.Application.Images;
using ImageStore.Application.Images.Storage;
using ImageStore.Application.Images.Validation;
using ImageStore.Application.Posts;
using ImageStore.Domain.Social;
using Microsoft.Extensions.Logging;

namespace ImageStore.Application.Social;

public sealed partial class SocialService(
    ISocialRepository repository,
    IPostRepository posts,
    IImageFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<SocialService> logger) : ISocialService
{
    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var callerId = currentUser.GetRequiredUserId();
        var profile = await repository.GetProfileAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User");

        var isCurrentUser = userId == callerId;
        var isFollowing = !isCurrentUser && await repository.IsFollowingAsync(callerId, userId, cancellationToken);

        // Post counts are only disclosed to the author and their followers – the posts themselves
        // are subject to the same rule, and a count would leak their existence.
        var postCount = isCurrentUser || isFollowing
            ? await posts.CountByAuthorAsync(userId, cancellationToken)
            : 0;

        return new UserProfileDto(
            profile.UserId,
            profile.UserName,
            profile.DisplayName,
            profile.Bio,
            profile.HasProfilePicture,
            await repository.CountFollowersAsync(userId, cancellationToken),
            await repository.CountFollowingAsync(userId, cancellationToken),
            postCount,
            isFollowing,
            isCurrentUser,
            profile.CreatedAtUtc);
    }

    public async Task<PagedResult<UserSummaryDto>> SearchUsersAsync(UserSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var viewerId = currentUser.GetRequiredUserId();

        var errors = new ValidationErrors();
        SocialRequestValidator.ValidateSearchQuery(query, errors);
        errors.ThrowIfAny();
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        return await repository.SearchProfilesAsync(viewerId, search, page, pageSize, cancellationToken);
    }

    public async Task<UserProfileDto> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.GetRequiredUserId();

        var errors = new ValidationErrors();
        SocialRequestValidator.ValidateProfileUpdate(request.DisplayName, request.Bio, errors);
        errors.ThrowIfAny();

        var profile = await repository.GetProfileAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User");

        profile.Update(request.DisplayName!, request.Bio, timeProvider.GetUtcNow().UtcDateTime);
        await repository.SaveChangesAsync(cancellationToken);

        return await GetProfileAsync(userId, cancellationToken);
    }

    public async Task FollowAsync(Guid followedUserId, CancellationToken cancellationToken)
    {
        var followerId = currentUser.GetRequiredUserId();
        if (followerId == followedUserId)
        {
            throw new RequestValidationException("userId", "You cannot follow yourself.");
        }

        _ = await repository.GetProfileAsync(followedUserId, cancellationToken)
            ?? throw new NotFoundException("User");

        // Idempotent: an existing relationship is a success, not a conflict.
        if (await repository.FindFollowAsync(followerId, followedUserId, cancellationToken) is not null)
        {
            return;
        }

        repository.Add(UserFollow.Create(followerId, followedUserId, timeProvider.GetUtcNow().UtcDateTime));
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            // A concurrent request created the same follow first – the desired state exists.
            if (await repository.FindFollowAsync(followerId, followedUserId, cancellationToken) is null)
            {
                throw;
            }
        }
    }

    public async Task UnfollowAsync(Guid followedUserId, CancellationToken cancellationToken)
    {
        var followerId = currentUser.GetRequiredUserId();

        var follow = await repository.FindFollowAsync(followerId, followedUserId, cancellationToken);
        if (follow is null)
        {
            return; // Idempotent: unfollowing twice is not an error.
        }

        repository.Remove(follow);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public Task<PagedResult<UserSummaryDto>> ListFollowersAsync(Guid userId, PagedQuery query, CancellationToken cancellationToken) =>
        ListRelatedUsersAsync(userId, query, isFollowers: true, cancellationToken);

    public Task<PagedResult<UserSummaryDto>> ListFollowingAsync(Guid userId, PagedQuery query, CancellationToken cancellationToken) =>
        ListRelatedUsersAsync(userId, query, isFollowers: false, cancellationToken);

    public async Task<UserProfileDto> SetProfilePictureAsync(UploadFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var userId = currentUser.GetRequiredUserId();
        var profile = await repository.GetProfileAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User");

        IStagedImageFile stagedFile;
        try
        {
            await using var source = file.OpenReadStream();
            stagedFile = await storage.StageAsync(source, SocialRules.MaxProfilePictureBytes, cancellationToken);
        }
        catch (FileSizeLimitExceededException)
        {
            throw new RequestValidationException(
                "file",
                $"The profile picture exceeds the maximum size of {SocialRules.MaxProfilePictureBytes / (1024 * 1024)} MB.");
        }

        var keep = false;
        try
        {
            if (stagedFile.Length == 0)
            {
                throw new RequestValidationException("file", "The profile picture is empty.");
            }

            ImageInspectionResult inspection;
            await using (var content = stagedFile.OpenRead())
            {
                inspection = ImageFormatInspector.Inspect(content);
            }

            if (!inspection.IsValid)
            {
                throw new RequestValidationException("file", inspection.Error ?? "The file is not a valid image.");
            }

            var format = ImageFormats.Get(inspection.Format!.Value);
            var relativePath = ProfilePictureStoragePaths.CreateRelativePath(userId, format.CanonicalExtension);
            var previousPath = profile.ProfilePicturePath;
            var replacingSamePath = string.Equals(previousPath, relativePath, StringComparison.Ordinal);

            // Stored files are never overwritten: when the new picture uses the same path as the
            // current one, the old file is removed first. A crash in between leaves a row whose
            // file is missing – the endpoint then answers 404 until a new picture is uploaded.
            if (replacingSamePath)
            {
                await TryDeletePictureAsync(previousPath!, userId, cancellationToken);
                previousPath = null;
            }

            await stagedFile.CommitAsync(relativePath, cancellationToken);
            keep = true;

            profile.SetProfilePicture(relativePath, format.MimeType, timeProvider.GetUtcNow().UtcDateTime);

            // Do not forward the request token: once the file is in place the metadata write must
            // finish, otherwise a disconnect could leave the outcome uncertain.
            await repository.SaveChangesAsync(CancellationToken.None);

            if (previousPath is not null)
            {
                await TryDeletePictureAsync(previousPath, userId, CancellationToken.None);
            }
        }
        finally
        {
            if (!keep)
            {
                await stagedFile.DisposeAsync();
            }
        }

        return await GetProfileAsync(userId, cancellationToken);
    }

    public async Task RemoveProfilePictureAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.GetRequiredUserId();
        var profile = await repository.GetProfileAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User");

        if (profile.ProfilePicturePath is null)
        {
            return; // Idempotent.
        }

        var previousPath = profile.ProfilePicturePath;
        profile.ClearProfilePicture(timeProvider.GetUtcNow().UtcDateTime);
        await repository.SaveChangesAsync(CancellationToken.None);
        await TryDeletePictureAsync(previousPath, userId, CancellationToken.None);
    }

    public async Task<ProfilePictureContent> GetProfilePictureAsync(Guid userId, CancellationToken cancellationToken)
    {
        currentUser.GetRequiredUserId();
        var profile = await repository.GetProfileAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User");

        if (profile.ProfilePicturePath is not { } path || profile.ProfilePictureContentType is not { } contentType)
        {
            throw new NotFoundException("Profile picture");
        }

        Stream content;
        try
        {
            content = storage.OpenRead(path);
        }
        catch (StoredFileNotFoundException)
        {
            LogPictureMissing(logger, userId);
            throw;
        }
        catch (InvalidStoragePathException)
        {
            LogInvalidPicturePath(logger, userId);
            throw;
        }

        var lastModified = new DateTimeOffset(profile.UpdatedAtUtc ?? profile.CreatedAtUtc, TimeSpan.Zero);
        return new ProfilePictureContent(content, contentType, lastModified, $"\"{userId:N}-{lastModified.ToUnixTimeSeconds()}\"");
    }

    private async Task<PagedResult<UserSummaryDto>> ListRelatedUsersAsync(
        Guid userId,
        PagedQuery query,
        bool isFollowers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var viewerId = currentUser.GetRequiredUserId();

        var errors = new ValidationErrors();
        Paging.Validate(query.Page, query.PageSize, errors);
        errors.ThrowIfAny();

        _ = await repository.GetProfileAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User");

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        return isFollowers
            ? await repository.ListFollowersAsync(viewerId, userId, page, pageSize, cancellationToken)
            : await repository.ListFollowingAsync(viewerId, userId, page, pageSize, cancellationToken);
    }

    private async Task TryDeletePictureAsync(string relativePath, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(relativePath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidStoragePathException)
        {
            // The old picture becomes unreachable as soon as the metadata points elsewhere; a
            // leftover file under profiles/ is harmless (it is not publicly served).
            LogPictureCleanupFailed(logger, exception, userId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The profile picture of user {UserId} is missing from storage.")]
    private static partial void LogPictureMissing(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Critical, Message = "User {UserId} has an invalid profile picture path; possible data tampering.")]
    private static partial void LogInvalidPicturePath(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An old profile picture of user {UserId} could not be removed.")]
    private static partial void LogPictureCleanupFailed(ILogger logger, Exception exception, Guid userId);
}
