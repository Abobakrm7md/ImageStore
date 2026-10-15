using ImageStore.Domain.Posts;
using ImageStore.Domain.Social;
using ImageStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ImageStore.IntegrationTests;

/// <summary>
/// SQL Server model checks for the social schema (no running server needed): the constraints and
/// indexes the feature relies on exist, the feed query translates to SQL with the follow filter
/// applied in the database, and the migration set includes the social migration.
/// </summary>
public sealed class SocialPersistenceTests
{
    private static ImageStoreDbContext CreateSqlServerContext() =>
        new(new DbContextOptionsBuilder<ImageStoreDbContext>()
            .UseSqlServer("Server=localhost;Database=ImageStoreDesignTime;Integrated Security=true;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void Social_Migration_Is_Available()
    {
        using var context = CreateSqlServerContext();

        Assert.False(context.Database.HasPendingModelChanges(), "Run 'dotnet ef migrations add' – the model has changed.");
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddSocialSharing", StringComparison.Ordinal));
    }

    [Fact]
    public void Follow_Relationships_Are_Unique_At_The_Database_Level()
    {
        using var context = CreateSqlServerContext();
        var follows = context.Model.FindEntityType(typeof(UserFollow))!;

        // The composite primary key (FollowerUserId, FollowedUserId) is the uniqueness guarantee.
        Assert.Equal(
            ["FollowerUserId", "FollowedUserId"],
            follows.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.Equal(2, follows.GetForeignKeys().Count());

        // Restrict, not Cascade: SQL Server rejects two cascade paths from AspNetUsers into the
        // same table, and account deletion is guarded by the Restrict FKs on authored content.
        Assert.All(follows.GetForeignKeys(), foreignKey =>
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
    }

    [Fact]
    public void Reactions_Are_Unique_Per_User_And_Post()
    {
        using var context = CreateSqlServerContext();
        var reactions = context.Model.FindEntityType(typeof(PostReaction))!;

        var unique = reactions.GetIndexes().SingleOrDefault(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(["PostId", "UserId"]));
        Assert.NotNull(unique);
        Assert.True(unique.IsUnique);

        // Reactions are stored as stable integers, not client-supplied strings.
        Assert.Equal(typeof(ReactionType), reactions.FindProperty(nameof(PostReaction.Reaction))!.ClrType);
        Assert.Equal(
            typeof(int),
            reactions.FindProperty(nameof(PostReaction.Reaction))!.GetProviderClrType());
    }

    [Fact]
    public void Posts_Images_And_User_Names_Have_The_Required_Constraints()
    {
        using var context = CreateSqlServerContext();
        var posts = context.Model.FindEntityType(typeof(ImagePost))!;
        var profiles = context.Model.FindEntityType(typeof(UserProfile))!;
        var comments = context.Model.FindEntityType(typeof(Comment))!;

        Assert.True(posts.GetIndexes().Single(index => index.Properties[0].Name == nameof(ImagePost.ImageId) && index.Properties.Count == 1).IsUnique);
        Assert.Contains(posts.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual(["AuthorUserId", "CreatedAtUtc"]));
        Assert.True(profiles.GetIndexes().Single(index => index.Properties[0].Name == nameof(UserProfile.NormalizedUserName)).IsUnique);

        // Deleting a post removes its comments and reactions; deleting an image unpublishes its post.
        Assert.Equal(
            DeleteBehavior.Cascade,
            comments.GetForeignKeys().Single(foreignKey => foreignKey.Properties[0].Name == nameof(Comment.PostId)).DeleteBehavior);
        Assert.Equal(
            DeleteBehavior.Cascade,
            posts.GetForeignKeys().Single(foreignKey => foreignKey.Properties[0].Name == nameof(ImagePost.ImageId)).DeleteBehavior);
        Assert.Equal(
            DeleteBehavior.Restrict,
            posts.GetForeignKeys().Single(foreignKey => foreignKey.Properties[0].Name == nameof(ImagePost.AuthorUserId)).DeleteBehavior);
    }

    [Fact]
    public void The_Feed_Query_Translates_To_Sql_With_The_Follow_Filter_In_The_Database()
    {
        using var context = CreateSqlServerContext();
        var repository = new PostRepository(context);

        var sql = repository.BuildFeedQuery(Guid.NewGuid())
            .OrderByDescending(post => post.CreatedAtUtc)
            .ThenByDescending(post => post.Id)
            .Skip(5)
            .Take(10)
            .ToQueryString();

        Assert.Contains("[UserFollows]", sql, StringComparison.Ordinal);
        Assert.Contains("[FollowerUserId] = @", sql, StringComparison.Ordinal);
        Assert.Contains("[CreatedAtUtc] DESC", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
    }
}
