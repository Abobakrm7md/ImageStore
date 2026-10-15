using System.Net;
using System.Net.Http.Json;
using ImageStore.Application.Images;
using ImageStore.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ImageStore.IntegrationTests;

public sealed class ImageOwnershipTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Another_Users_Image_Is_Indistinguishable_From_A_Missing_One()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await alice.UploadSingleAsync("sample.png", "Alice's secret", "private", ["family"]);
        var missingId = Guid.NewGuid();

        foreach (var suffix in new[] { string.Empty, "/content", "/download" })
        {
            var forOthersImage = await bob.Http.GetAsync(new Uri($"/api/images/{image.Id}{suffix}", UriKind.Relative));
            var forMissingImage = await bob.Http.GetAsync(new Uri($"/api/images/{missingId}{suffix}", UriKind.Relative));

            var problem = await forOthersImage.ReadProblemAsync(HttpStatusCode.NotFound);
            var missing = await forMissingImage.ReadProblemAsync(HttpStatusCode.NotFound);
            Assert.Equal(missing.GetProperty("detail").GetString(), problem.GetProperty("detail").GetString());
            Assert.DoesNotContain("Alice", await forOthersImage.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var update = await bob.Http.PutAsJsonAsync($"/api/images/{image.Id}", new UpdateImageRequest("Hacked", null, null), ApiClient.Json);
        var delete = await bob.Http.DeleteAsync(new Uri($"/api/images/{image.Id}", UriKind.Relative));
        await update.ReadProblemAsync(HttpStatusCode.NotFound);
        await delete.ReadProblemAsync(HttpStatusCode.NotFound);

        // Alice's image is untouched.
        var stillThere = await alice.Http.GetFromJsonAsync<ImageDto>($"/api/images/{image.Id}", ApiClient.Json);
        Assert.Equal("Alice's secret", stillThere!.Title);
        Assert.Contains(factory.StoredFiles(), path => path.Contains(image.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lists_And_Searches_Only_Return_The_Callers_Images()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await alice.UploadSingleAsync("sample.png", "shared-title", tags: ["common"]);
        await alice.UploadSingleAsync("sample.jpg", "shared-title");
        await bob.UploadSingleAsync("sample-lossy.webp", "shared-title", tags: ["common"]);

        var bobs = await bob.ListAsync("?search=shared");
        var bobsByTag = await bob.ListAsync("?tag=common");

        Assert.Equal(1, bobs.TotalCount);
        Assert.Equal("image/webp", Assert.Single(bobs.Items).ContentType);
        Assert.Equal(1, bobsByTag.TotalCount);
        Assert.Equal(2, (await alice.ListAsync("?search=shared")).TotalCount);
    }

    [Fact]
    public async Task Ownership_Comes_From_The_Session_Not_From_The_Request()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await alice.UploadSingleAsync("sample.png");

        // Smuggle another owner into the update body: it must be ignored.
        var response = await alice.Http.PutAsJsonAsync(
            $"/api/images/{image.Id}",
            new { title = "Renamed", ownerUserId = bob.User!.Id, relativeStoragePath = "../../etc/passwd" },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await factory.WithDbContextAsync(db => db.Images.AsNoTracking().SingleAsync(i => i.Id == image.Id));
        Assert.Equal(alice.User!.Id, stored.OwnerUserId);
        Assert.Equal($"{alice.User.Id}/{image.Id}.png", stored.RelativeStoragePath);
        Assert.Equal(0, (await bob.ListAsync()).TotalCount);
    }
}
