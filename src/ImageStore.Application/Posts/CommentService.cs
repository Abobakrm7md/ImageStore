using ImageStore.Application.Common;
using ImageStore.Domain.Posts;

namespace ImageStore.Application.Posts;

public sealed class CommentService(
    ICommentRepository comments,
    IPostRepository posts,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommentService
{
    public async Task<PagedResult<CommentDto>> ListAsync(Guid postId, PagedQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var viewerId = currentUser.GetRequiredUserId();
        var post = await EnsureAccessibleAsync(postId, viewerId, cancellationToken);

        var errors = new ValidationErrors();
        Paging.Validate(query.Page, query.PageSize, errors);
        errors.ThrowIfAny();

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize, defaultPageSize: 20);
        var result = await comments.ListAsync(postId, page, pageSize, cancellationToken);

        return new PagedResult<CommentDto>(
            result.Items.Select(comment => ToDto(comment, post.AuthorUserId, viewerId)).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<CommentDto> CreateAsync(Guid postId, CreateCommentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authorId = currentUser.GetRequiredUserId();
        var post = await EnsureAccessibleAsync(postId, authorId, cancellationToken);

        var errors = new ValidationErrors();
        PostRequestValidator.ValidateCommentContent(request.Content, errors);
        errors.ThrowIfAny();

        var comment = Comment.Create(
            Guid.CreateVersion7(),
            postId,
            authorId,
            request.Content!,
            timeProvider.GetUtcNow().UtcDateTime);

        comments.Add(comment);
        await comments.SaveChangesAsync(cancellationToken);

        var created = await comments.GetAsync(postId, comment.Id, cancellationToken) ?? comment;
        return ToDto(created, post.AuthorUserId, authorId);
    }

    public async Task<CommentDto> UpdateAsync(Guid postId, Guid commentId, UpdateCommentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var editorId = currentUser.GetRequiredUserId();
        var post = await EnsureAccessibleAsync(postId, editorId, cancellationToken);

        var errors = new ValidationErrors();
        PostRequestValidator.ValidateCommentContent(request.Content, errors);
        errors.ThrowIfAny();

        var comment = await comments.GetForUpdateAsync(postId, commentId, cancellationToken)
            ?? throw new NotFoundException("Comment");

        if (comment.AuthorUserId != editorId)
        {
            // Only the author may edit; someone else's comment looks like a missing one.
            throw new NotFoundException("Comment");
        }

        comment.UpdateContent(request.Content!, timeProvider.GetUtcNow().UtcDateTime);
        await comments.SaveChangesAsync(cancellationToken);

        var updated = await comments.GetAsync(postId, commentId, cancellationToken) ?? comment;
        return ToDto(updated, post.AuthorUserId, editorId);
    }

    public async Task DeleteAsync(Guid postId, Guid commentId, CancellationToken cancellationToken)
    {
        var callerId = currentUser.GetRequiredUserId();
        var post = await EnsureAccessibleAsync(postId, callerId, cancellationToken);

        var comment = await comments.GetForUpdateAsync(postId, commentId, cancellationToken)
            ?? throw new NotFoundException("Comment");

        // The comment author may delete their own comment; the post author may moderate.
        if (comment.AuthorUserId != callerId && post.AuthorUserId != callerId)
        {
            throw new NotFoundException("Comment");
        }

        comments.Remove(comment);
        await comments.SaveChangesAsync(cancellationToken);
    }

    private async Task<ImagePost> EnsureAccessibleAsync(Guid postId, Guid viewerId, CancellationToken cancellationToken) =>
        await posts.GetAccessiblePostAsync(postId, viewerId, cancellationToken)
            ?? throw new NotFoundException("Post");

    internal static CommentDto ToDto(Comment comment, Guid postAuthorId, Guid viewerId)
    {
        var author = comment.Author ?? throw new InvalidOperationException("The comment was loaded without its author.");
        var isMine = comment.AuthorUserId == viewerId;
        return new CommentDto(
            comment.Id,
            comment.PostId,
            new PostAuthorDto(author.UserId, author.UserName, author.DisplayName, author.HasProfilePicture),
            comment.Content,
            comment.CreatedAtUtc,
            comment.UpdatedAtUtc,
            isMine,
            isMine || postAuthorId == viewerId);
    }
}
