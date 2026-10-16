using ImageStore.Application.Common;
using ImageStore.Domain.Posts;

namespace ImageStore.Application.Posts;

public sealed class ReactionService(
    IPostReactionRepository reactions,
    IPostRepository posts,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IReactionService
{
    public async Task<ReactionSummaryDto> GetSummaryAsync(Guid postId, CancellationToken cancellationToken)
    {
        var viewerId = currentUser.GetRequiredUserId();
        await EnsureAccessibleAsync(postId, viewerId, cancellationToken);

        var counts = await reactions.CountByTypeAsync(postId, cancellationToken);
        var mine = await reactions.FindUserReactionAsync(postId, viewerId, cancellationToken);
        return BuildSummary(counts, mine?.Reaction);
    }

    public async Task<ReactionSummaryDto> SetReactionAsync(Guid postId, SetReactionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.GetRequiredUserId();

        if (!ReactionTypes.TryParse(request.Reaction, out var reaction))
        {
            throw new RequestValidationException(
                "reaction",
                $"Unknown reaction '{request.Reaction}'. Supported reactions: {string.Join(", ", ReactionTypes.All.Select(type => type.ToString().ToLowerInvariant()))}.");
        }

        await EnsureAccessibleAsync(postId, userId, cancellationToken);

        var existing = await reactions.FindUserReactionAsync(postId, userId, cancellationToken);
        if (existing is not null)
        {
            existing.ChangeReaction(reaction, timeProvider.GetUtcNow().UtcDateTime);
            await reactions.SaveChangesAsync(cancellationToken);
        }
        else
        {
            reactions.Add(PostReaction.Create(
                Guid.CreateVersion7(),
                postId,
                userId,
                reaction,
                timeProvider.GetUtcNow().UtcDateTime));

            try
            {
                await reactions.SaveChangesAsync(cancellationToken);
            }
            catch (ConflictException)
            {
                // A concurrent request inserted the user's first reaction at the same moment
                // (unique (PostId, UserId) index): turn the insert into an update and retry once.
                existing = await reactions.FindUserReactionAsync(postId, userId, cancellationToken)
                    ?? throw new NotFoundException("User reaction");
                existing.ChangeReaction(reaction, timeProvider.GetUtcNow().UtcDateTime);
                await reactions.SaveChangesAsync(cancellationToken);
            }
        }

        var counts = await reactions.CountByTypeAsync(postId, cancellationToken);
        return BuildSummary(counts, reaction);
    }

    public async Task RemoveReactionAsync(Guid postId, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetRequiredUserId();
        await EnsureAccessibleAsync(postId, userId, cancellationToken);

        var existing = await reactions.FindUserReactionAsync(postId, userId, cancellationToken);
        if (existing is null)
        {
            return; // Idempotent.
        }

        reactions.Remove(existing);
        await reactions.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAccessibleAsync(Guid postId, Guid viewerId, CancellationToken cancellationToken)
    {
        _ = await posts.GetAccessiblePostAsync(postId, viewerId, cancellationToken)
            ?? throw new NotFoundException("Post");
    }

    private static ReactionSummaryDto BuildSummary(IReadOnlyDictionary<ReactionType, int> counts, ReactionType? myReaction) =>
        new(
            counts.Values.Sum(),
            ReactionTypes.All
                .Select(type => new ReactionCountDto(type, counts.GetValueOrDefault(type)))
                .ToList(),
            myReaction);
}
