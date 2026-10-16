using ImageStore.Domain.Common;
using ImageStore.Domain.Posts;

namespace ImageStore.UnitTests.Posts;

public sealed class ImagePostEntityTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static ImagePost NewPost(string title = "Sunset", string? description = null) =>
        ImagePost.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), title, description, Now);

    [Fact]
    public void Create_Normalizes_Text_And_Rejects_Empty_Values()
    {
        var post = NewPost("  Sunset  ", "  Golden hour  ");
        Assert.Equal("Sunset", post.Title);
        Assert.Equal("Golden hour", post.Description);
        Assert.Null(post.UpdatedAtUtc);

        Assert.Throws<DomainValidationException>(() => NewPost("   "));
        Assert.Throws<DomainValidationException>(() => NewPost(new string('x', PostRules.TitleMaxLength + 1)));
        Assert.Throws<DomainValidationException>(() => NewPost("Sunset", new string('x', PostRules.DescriptionMaxLength + 1)));
        Assert.Throws<DomainValidationException>(
            () => ImagePost.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "Sunset", null, Now));
        Assert.Throws<DomainValidationException>(
            () => ImagePost.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Sunset", null, Now));
        Assert.Throws<DomainValidationException>(
            () => ImagePost.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "Sunset", null, Now));
    }

    [Fact]
    public void Update_Replaces_Text_And_Sets_The_Timestamp()
    {
        var post = NewPost();

        post.Update("Repainted", null, Now.AddHours(1));

        Assert.Equal("Repainted", post.Title);
        Assert.Null(post.Description); // empty clears it
        Assert.Equal(Now.AddHours(1), post.UpdatedAtUtc);

        Assert.Throws<DomainValidationException>(() => post.Update("  ", null, Now));
    }
}

public sealed class CommentEntityTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Trims_Content_And_Rejects_Empty_Or_Oversized_Comments()
    {
        var comment = Comment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  Lovely!  ", Now);
        Assert.Equal("Lovely!", comment.Content);
        Assert.Null(comment.UpdatedAtUtc);

        Assert.Throws<DomainValidationException>(() => Comment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "   ", Now));
        Assert.Throws<DomainValidationException>(
            () => Comment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('x', PostRules.CommentMaxLength + 1), Now));
        Assert.Throws<DomainValidationException>(
            () => Comment.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "Lovely!", Now));
    }

    [Fact]
    public void UpdateContent_Validates_And_Timestamps()
    {
        var comment = Comment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Lovely!", Now);

        comment.UpdateContent("Lovely! (edited)", Now.AddMinutes(5));
        Assert.Equal("Lovely! (edited)", comment.Content);
        Assert.Equal(Now.AddMinutes(5), comment.UpdatedAtUtc);

        Assert.Throws<DomainValidationException>(() => comment.UpdateContent("\t", Now));
    }
}

public sealed class PostReactionEntityTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Rejects_Unknown_Reaction_Types()
    {
        Assert.Throws<DomainValidationException>(
            () => PostReaction.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (ReactionType)999, Now));

        var reaction = PostReaction.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReactionType.Love, Now);
        Assert.Equal(ReactionType.Love, reaction.Reaction);
    }

    [Fact]
    public void ChangeReaction_Updates_Type_And_Timestamp_And_Ignores_Same_Type()
    {
        var reaction = PostReaction.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReactionType.Like, Now);

        reaction.ChangeReaction(ReactionType.Like, Now.AddMinutes(1));
        Assert.Null(reaction.UpdatedAtUtc); // no change, no timestamp

        reaction.ChangeReaction(ReactionType.Wow, Now.AddMinutes(2));
        Assert.Equal(ReactionType.Wow, reaction.Reaction);
        Assert.Equal(Now.AddMinutes(2), reaction.UpdatedAtUtc);

        Assert.Throws<DomainValidationException>(() => reaction.ChangeReaction((ReactionType)0, Now));
    }

    [Theory]
    [InlineData("like", ReactionType.Like)]
    [InlineData("LOVE", ReactionType.Love)]
    [InlineData("laugh", ReactionType.Laugh)]
    [InlineData("wow", ReactionType.Wow)]
    [InlineData("sad", ReactionType.Sad)]
    [InlineData("dislike", ReactionType.Dislike)]
    public void TryParse_Accepts_The_Stable_Names(string name, ReactionType expected)
    {
        Assert.True(ReactionTypes.TryParse(name, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("angry")]
    [InlineData("1")] // numeric values are not accepted from clients
    [InlineData("0")]
    public void TryParse_Rejects_Unknown_And_Numeric_Values(string? name) =>
        Assert.False(ReactionTypes.TryParse(name, out _));

    [Fact]
    public void All_Contains_Exactly_The_Six_Supported_Reactions() =>
        Assert.Equal(
            new[] { ReactionType.Like, ReactionType.Love, ReactionType.Laugh, ReactionType.Wow, ReactionType.Sad, ReactionType.Dislike },
            ReactionTypes.All);
}
