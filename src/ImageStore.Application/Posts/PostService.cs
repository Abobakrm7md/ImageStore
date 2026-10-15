using ImageStore.Application.Common;
using ImageStore.Application.Images;
using ImageStore.Application.Images.Storage;
using ImageStore.Application.Social;
using ImageStore.Domain.Posts;
using Microsoft.Extensions.Logging;

namespace ImageStore.Application.Posts;

public sealed partial class PostService(
    IPostRepository posts,
    IImageRepository images,
    ISocialRepository social,
    IImageFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<PostService> logger) : IPostService
{
    public async Task<ImagePostDto> CreateAsync(CreatePostRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authorId = currentUser.GetRequiredUserId();

        var errors = new ValidationErrors();
        if (request.ImageId is null || request.ImageId == Guid.Empty)
        {
            errors.Add("imageId", "Select an image to publish.");
        }

        PostRequestValidator.ValidatePostText(request.Title, titleRequired: false, request.Description, errors);
        errors.ThrowIfAny();

        // The image must be one of the caller's own images (404 otherwise, like everywhere).
        var image = await images.GetOwnedAsync(authorId, request.ImageId!.Value, cancellationToken)
            ?? throw new NotFoundException("Image");

        if (await posts.IsImagePublishedAsync(image.Id, cancellationToken))
        {
            throw new ConflictException("This image is already published as a post.");
        }

        var title = string.IsNullOrWhiteSpace(request.Title) ? image.Title : request.Title;
        var post = ImagePost.Create(
            Guid.CreateVersion7(),
            authorId,
            image.Id,
            title,
            request.Description,
            timeProvider.GetUtcNow().UtcDateTime);

        posts.Add(post);
        await posts.SaveChangesAsync(cancellationToken);

        var details = await posts.GetAccessibleAsync(post.Id, authorId, cancellationToken)
            ?? throw new NotFoundException("Post");
        return ToDto(details, authorId);
    }

    public async Task<ImagePostDto> UpdateAsync(Guid postId, UpdatePostRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authorId = currentUser.GetRequiredUserId();

        var errors = new ValidationErrors();
        PostRequestValidator.ValidatePostText(request.Title, titleRequired: true, request.Description, errors);
        errors.ThrowIfAny();

        // Posts of other users are indistinguishable from missing posts.
        var post = await posts.GetOwnedForUpdateAsync(postId, authorId, cancellationToken)
            ?? throw new NotFoundException("Post");

        post.Update(request.Title!, request.Description, timeProvider.GetUtcNow().UtcDateTime);
        await posts.SaveChangesAsync(cancellationToken);

        var details = await posts.GetAccessibleAsync(postId, authorId, cancellationToken)
            ?? throw new NotFoundException("Post");
        return ToDto(details, authorId);
    }

    public async Task DeleteAsync(Guid postId, CancellationToken cancellationToken)
    {
        var authorId = currentUser.GetRequiredUserId();
        var post = await posts.GetOwnedForUpdateAsync(postId, authorId, cancellationToken)
            ?? throw new NotFoundException("Post");

        // Comments and reactions cascade with the post (in the database); the image itself and
        // its file stay untouched – deleting a post only unpublishes the image.
        posts.Remove(post);
        await posts.SaveChangesAsync(cancellationToken);
        LogPostDeleted(logger, postId, authorId);
    }

    public async Task<ImagePostDto> GetAsync(Guid postId, CancellationToken cancellationToken)
    {
        var viewerId = currentUser.GetRequiredUserId();
        var details = await posts.GetAccessibleAsync(postId, viewerId, cancellationToken)
            ?? throw new NotFoundException("Post");
        return ToDto(details, viewerId);
    }

    public async Task<PagedResult<ImagePostDto>> GetFeedAsync(PagedQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var viewerId = currentUser.GetRequiredUserId();

        var errors = new ValidationErrors();
        Paging.Validate(query.Page, query.PageSize, errors);
        errors.ThrowIfAny();

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize, defaultPageSize: 10);
        var result = await posts.ListFeedAsync(viewerId, page, pageSize, cancellationToken);
        return MapPage(result, viewerId);
    }

    public async Task<PagedResult<ImagePostDto>> ListUserPostsAsync(Guid userId, PagedQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var viewerId = currentUser.GetRequiredUserId();

        var errors = new ValidationErrors();
        Paging.Validate(query.Page, query.PageSize, errors);
        errors.ThrowIfAny();

        if (userId != viewerId)
        {
            _ = await social.GetProfileAsync(userId, cancellationToken)
                ?? throw new NotFoundException("User");

            if (!await social.IsFollowingAsync(viewerId, userId, cancellationToken))
            {
                // The user exists (profiles are public) but their posts are follower-only.
                throw new NotFoundException("Posts");
            }
        }

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var result = await posts.ListByAuthorAsync(userId, viewerId, page, pageSize, cancellationToken);
        return MapPage(result, viewerId);
    }

    public async Task<ImageContent> GetContentAsync(Guid postId, CancellationToken cancellationToken)
    {
        var viewerId = currentUser.GetRequiredUserId();
        var post = await posts.GetAccessiblePostAsync(postId, viewerId, cancellationToken)
            ?? throw new NotFoundException("Post");

        var image = post.Image ?? throw new NotFoundException("Post");

        Stream content;
        try
        {
            content = storage.OpenRead(image.RelativeStoragePath);
        }
        catch (StoredFileNotFoundException)
        {
            LogStoredFileMissing(logger, image.Id);
            throw;
        }
        catch (InvalidStoragePathException)
        {
            LogInvalidStoragePath(logger, image.Id);
            throw;
        }

        return new ImageContent(
            content,
            image.ContentType,
            image.OriginalFileName,
            new DateTimeOffset(post.CreatedAtUtc, TimeSpan.Zero),
            $"\"{postId:N}\"");
    }

    private PagedResult<ImagePostDto> MapPage(PagedResult<PostDetails> page, Guid viewerId) =>
        new(page.Items.Select(details => ToDto(details, viewerId)).ToList(), page.Page, page.PageSize, page.TotalCount);

    internal static ImagePostDto ToDto(PostDetails details, Guid viewerId)
    {
        var post = details.Post;
        var image = post.Image ?? throw new InvalidOperationException("The post was loaded without its image.");

        var counts = ReactionTypes.All
            .Select(type => new ReactionCountDto(type, details.ReactionCounts.GetValueOrDefault(type)))
            .ToList();

        return new ImagePostDto(
            post.Id,
            post.Title,
            post.Description,
            post.CreatedAtUtc,
            post.UpdatedAtUtc,
            new PostAuthorDto(
                details.Author.UserId,
                details.Author.UserName,
                details.Author.DisplayName,
                details.Author.HasProfilePicture),
            new PostImageDto(image.Id, image.ContentType, image.Width, image.Height),
            details.CommentCount,
            counts.Sum(count => count.Count),
            counts,
            details.MyReaction,
            post.AuthorUserId == viewerId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Post {PostId} was deleted by its author {UserId}.")]
    private static partial void LogPostDeleted(ILogger logger, Guid postId, Guid userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The file for image {ImageId} is missing from storage.")]
    private static partial void LogStoredFileMissing(ILogger logger, Guid imageId);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Image {ImageId} has an invalid storage path; possible data tampering.")]
    private static partial void LogInvalidStoragePath(ILogger logger, Guid imageId);
}
