using ImageStore.Application.Images;
using ImageStore.Domain.Images;
using ImageStore.Infrastructure.Persistence;
using ImageStore.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace ImageStore.IntegrationTests;

/// <summary>
/// SQL Server-specific checks that need no running server: the migrations match the model,
/// the schema has the expected indexes, and repository queries translate to SQL with the
/// owner filter applied in the database.
/// </summary>
public sealed class PersistenceTests
{
    private static ImageStoreDbContext CreateSqlServerContext() =>
        new(new DbContextOptionsBuilder<ImageStoreDbContext>()
            .UseSqlServer("Server=localhost;Database=ImageStoreDesignTime;Integrated Security=true;TrustServerCertificate=True")
            .Options);

    private static ImageStoreDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<ImageStoreDbContext>().UseInMemoryDatabase($"persistence-{Guid.NewGuid():N}").Options);

    [Fact]
    public void Migrations_Are_In_Sync_With_The_Model()
    {
        using var context = CreateSqlServerContext();

        Assert.False(context.Database.HasPendingModelChanges(), "Run 'dotnet ef migrations add' – the model has changed.");
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public void Schema_Has_Indexes_For_Owner_Scoped_Queries()
    {
        using var context = CreateSqlServerContext();
        var images = context.Model.FindEntityType(typeof(Image))!;
        var tags = context.Model.FindEntityType(typeof(Tag))!;

        string[][] imageIndexes = images.GetIndexes().Select(index => index.Properties.Select(p => p.Name).ToArray()).ToArray();
        Assert.Contains(imageIndexes, columns => columns.SequenceEqual(["OwnerUserId", "CreatedAtUtc"]));
        Assert.Contains(imageIndexes, columns => columns.SequenceEqual(["OwnerUserId", "Title"]));
        Assert.True(images.GetIndexes().Single(index => index.Properties.Count == 1 && index.Properties[0].Name == "RelativeStoragePath").IsUnique);
        Assert.True(tags.GetIndexes().Single(index => index.Properties.Select(p => p.Name).SequenceEqual(["OwnerUserId", "NormalizedName"])).IsUnique);
        Assert.NotNull(context.Model.FindEntityType(ImageConfiguration.ImageTagsTable));
    }

    [Fact]
    public void Timestamps_Are_Read_Back_As_Utc()
    {
        using var context = CreateSqlServerContext();
        var images = context.Model.FindEntityType(typeof(Image))!;

        Assert.NotNull(images.FindProperty(nameof(Image.CreatedAtUtc))!.GetValueConverter());
        Assert.NotNull(images.FindProperty(nameof(Image.UpdatedAtUtc))!.GetValueConverter());
    }

    [Fact]
    public void Listing_Query_Translates_To_Sql_With_The_Owner_Filter_And_Escaped_Search()
    {
        using var context = CreateSqlServerContext();
        var repository = new ImageRepository(context);
        var criteria = new ImageListCriteria(2, 10, "50%_off", "TRAVEL", ImageSortField.Title, SortDirection.Asc);

        var filtered = repository.BuildFilteredQuery(Guid.NewGuid(), criteria);
        var sql = ImageRepository.BuildPageQuery(filtered, criteria).ToQueryString();
        var countSql = filtered.ToQueryString();

        Assert.Contains("[OwnerUserId] = @", sql, StringComparison.Ordinal);
        Assert.Contains("LIKE", sql, StringComparison.Ordinal);
        Assert.Contains("ESCAPE N'\\'", sql, StringComparison.Ordinal);
        Assert.Contains("[NormalizedName]", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET", sql, StringComparison.Ordinal);
        Assert.Contains("[OwnerUserId] = @", countSql, StringComparison.Ordinal);
        Assert.Equal("50\\%\\_off", ImageRepository.EscapeLikePattern("50%_off"));
    }

    [Fact]
    public async Task Repository_Reads_Are_Always_Owner_Scoped()
    {
        await using var context = CreateInMemoryContext();
        var repository = new ImageRepository(context);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var alicesImage = NewImage(alice, "Holiday");
        repository.Add(alicesImage);
        repository.Add(NewImage(bob, "Holiday"));
        await repository.SaveChangesAsync(CancellationToken.None);

        var bobsView = await repository.ListOwnedAsync(bob, new ImageListCriteria(1, 10, "holiday", null, ImageSortField.CreatedAt, SortDirection.Desc), CancellationToken.None);

        Assert.Equal(bob, Assert.Single(bobsView.Items).OwnerUserId);
        Assert.Null(await repository.GetOwnedAsync(bob, alicesImage.Id, CancellationToken.None));
        Assert.Null(await repository.GetOwnedForUpdateAsync(bob, alicesImage.Id, CancellationToken.None));
        Assert.NotNull(await repository.GetOwnedAsync(alice, alicesImage.Id, CancellationToken.None));
        Assert.False(await repository.AnyExistAsync(bob, [alicesImage.Id], CancellationToken.None));
    }

    private static Image NewImage(Guid owner, string title)
    {
        var id = Guid.NewGuid();
        return Image.Create(
            id,
            owner,
            title,
            null,
            new ImageFileDetails("a.png", $"{id}.png", $"{owner}/{id}.png", "image/png", 10, 1, 1),
            DateTime.UtcNow);
    }
}
