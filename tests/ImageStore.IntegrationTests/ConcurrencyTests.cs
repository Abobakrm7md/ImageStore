using System.Net;
using ImageStore.Domain.Images;
using ImageStore.Infrastructure.Persistence;
using ImageStore.IntegrationTests.TestSupport;
using ImageStore.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ImageStore.IntegrationTests;

/// <summary>
/// Regression tests for concurrent requests that create the same new tags (the SPA uploads
/// several files in parallel, each carrying the same tags).
/// </summary>
public sealed class ConcurrencyTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Parallel_Uploads_With_The_Same_New_Tags_All_Succeed()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            client.UploadAsync([new UploadPart($"photo-{i}.png", TestFiles.Read("sample.png"))], tags: ["Shared", "parallel"])));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

        // Previously, a later request with the same tags failed once duplicate tag rows existed.
        var later = await client.UploadSingleAsync("sample.jpg", tags: ["shared", "PARALLEL"]);
        Assert.Equal(2, later.Tags.Count);
        Assert.Equal(7, (await client.ListAsync("?tag=SHARED&pageSize=50")).TotalCount);
    }
}

public sealed class TagRepositoryTests
{
    private static ImageStoreDbContext CreateContext(string name) =>
        new(new DbContextOptionsBuilder<ImageStoreDbContext>().UseInMemoryDatabase(name).Options);

    [Fact]
    public async Task GetOrCreate_Creates_Missing_Tags_And_Reuses_Existing_Ones_Case_Insensitively()
    {
        var database = $"tags-{Guid.NewGuid():N}";
        var owner = Guid.NewGuid();
        IReadOnlyList<Tag> first;
        await using (var context = CreateContext(database))
        {
            first = await new ImageRepository(context).GetOrCreateTagsAsync(owner, ["Travel", "Family"], CancellationToken.None);
        }

        await using (var context = CreateContext(database))
        {
            var second = await new ImageRepository(context).GetOrCreateTagsAsync(owner, ["travel", "New"], CancellationToken.None);

            Assert.Equal(first[0].Id, second[0].Id);
            Assert.Equal("Travel", second[0].Name);
            Assert.Equal(3, await context.Tags.CountAsync(tag => tag.OwnerUserId == owner));
        }
    }

    [Fact]
    public async Task GetOrCreate_Keeps_Tags_Private_Per_User()
    {
        await using var context = CreateContext($"tags-{Guid.NewGuid():N}");
        var repository = new ImageRepository(context);

        var alice = await repository.GetOrCreateTagsAsync(Guid.NewGuid(), ["holiday"], CancellationToken.None);
        var bob = await repository.GetOrCreateTagsAsync(Guid.NewGuid(), ["holiday"], CancellationToken.None);

        Assert.NotEqual(alice[0].Id, bob[0].Id);
    }

    [Fact]
    public async Task GetOrCreate_Tolerates_Duplicate_Rows()
    {
        var database = $"tags-{Guid.NewGuid():N}";
        var owner = Guid.NewGuid();
        await using (var context = CreateContext(database))
        {
            // Simulates the outcome of a race on a provider without unique constraints.
            context.Tags.AddRange(Tag.Create(Guid.NewGuid(), owner, "dup"), Tag.Create(Guid.NewGuid(), owner, "DUP"));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(database))
        {
            var tags = await new ImageRepository(context).GetOrCreateTagsAsync(owner, ["Dup"], CancellationToken.None);
            Assert.Single(tags);
        }
    }

    [Fact]
    public async Task GetOrCreate_Refuses_To_Run_With_Other_Pending_Changes()
    {
        await using var context = CreateContext($"tags-{Guid.NewGuid():N}");
        context.Tags.Add(Tag.Create(Guid.NewGuid(), Guid.NewGuid(), "pending"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ImageRepository(context).GetOrCreateTagsAsync(Guid.NewGuid(), ["x"], CancellationToken.None));
    }
}
