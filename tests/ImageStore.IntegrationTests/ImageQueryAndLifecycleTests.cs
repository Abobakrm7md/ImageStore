using System.Net;
using System.Net.Http.Json;
using ImageStore.Application.Images;
using ImageStore.IntegrationTests.TestSupport;

namespace ImageStore.IntegrationTests;

public sealed class ImageQueryTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Pagination_Returns_The_Requested_Page_And_Totals()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        for (var i = 1; i <= 5; i++)
        {
            await client.UploadSingleAsync("sample.png", $"Photo {i}");
        }

        var page1 = await client.ListAsync("?page=1&pageSize=2&sortBy=title&sortDirection=asc");
        var page3 = await client.ListAsync("?page=3&pageSize=2&sortBy=title&sortDirection=asc");
        var beyond = await client.ListAsync("?page=9&pageSize=2");

        Assert.Equal((5, 3, 2), (page1.TotalCount, page1.TotalPages, page1.PageSize));
        Assert.Equal(["Photo 1", "Photo 2"], page1.Items.Select(i => i.Title));
        Assert.Equal(["Photo 5"], page3.Items.Select(i => i.Title));
        Assert.Empty(beyond.Items);
        Assert.Equal(5, beyond.TotalCount);
    }

    [Fact]
    public async Task Default_Order_Is_Newest_First()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        await client.UploadSingleAsync("sample.png", "older");
        await client.UploadSingleAsync("sample.png", "newer");

        var result = await client.ListAsync();

        Assert.Equal(["newer", "older"], result.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Search_Matches_Title_Or_Description_Case_Insensitively()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        await client.UploadSingleAsync("sample.png", "Sunset at the beach");
        await client.UploadSingleAsync("sample.jpg", "Mountains", "Hiking near the SUNSET point");
        await client.UploadSingleAsync("sample-lossy.webp", "City lights");

        var result = await client.ListAsync("?search=sunset");

        Assert.Equal(["Mountains", "Sunset at the beach"], result.Items.Select(i => i.Title).Order());
    }

    [Fact]
    public async Task Search_Treats_Wildcards_As_Literal_Characters()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        await client.UploadSingleAsync("sample.png", "100% organic");
        await client.UploadSingleAsync("sample.png", "1000 apples");
        await client.UploadSingleAsync("sample.png", "file_name");
        await client.UploadSingleAsync("sample.png", "filename");

        Assert.Equal(["100% organic"], (await client.ListAsync("?search=100%25")).Items.Select(i => i.Title));
        Assert.Equal(["file_name"], (await client.ListAsync("?search=_name")).Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Sorts_By_Size_And_Filters_By_Tag()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        await client.UploadSingleAsync("sample.jpg", "jpg", tags: ["Travel"]);         // 779 bytes
        await client.UploadSingleAsync("sample-lossless.webp", "webp", tags: ["travel"]); // 44 bytes
        await client.UploadSingleAsync("sample.png", "png");                            // 99 bytes

        var bySize = await client.ListAsync("?sortBy=fileSize&sortDirection=asc");
        var travel = await client.ListAsync("?tag=TRAVEL&sortBy=title");

        Assert.Equal(["webp", "png", "jpg"], bySize.Items.Select(i => i.Title));
        Assert.Equal(["jpg", "webp"], travel.Items.Select(i => i.Title));
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?sortBy=owner")]
    [InlineData("?sortDirection=sideways")]
    public async Task Invalid_Queries_Return_400(string query)
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var response = await client.Http.GetAsync(new Uri($"/api/images{query}", UriKind.Relative));

        await response.ReadProblemAsync(HttpStatusCode.BadRequest);
    }
}

public sealed class ImageLifecycleTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Metadata_Can_Be_Edited()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await client.UploadSingleAsync("sample.png", "Draft", tags: ["old"]);

        var response = await client.Http.PutAsJsonAsync(
            $"/api/images/{image.Id}",
            new UpdateImageRequest("Final title", "A description", ["New", "tags"]),
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await client.Http.GetFromJsonAsync<ImageDto>($"/api/images/{image.Id}", ApiClient.Json);
        Assert.Equal("Final title", fetched!.Title);
        Assert.Equal("A description", fetched.Description);
        Assert.Equal(["New", "tags"], fetched.Tags);
        Assert.NotNull(fetched.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, fetched.UpdatedAtUtc!.Value.Kind);
        Assert.Equal(0, (await client.ListAsync("?tag=old")).TotalCount);
    }

    [Fact]
    public async Task Invalid_Edits_Return_Validation_Problems()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await client.UploadSingleAsync("sample.png");

        var response = await client.Http.PutAsJsonAsync(
            $"/api/images/{image.Id}",
            new UpdateImageRequest("", new string('x', 2001), Enumerable.Range(0, 21).Select(i => $"t{i}").ToList()),
            ApiClient.Json);

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Equal(["description", "tags", "title"], problem.ErrorKeys());
    }

    [Fact]
    public async Task Delete_Removes_The_Metadata_And_The_File()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await client.UploadSingleAsync("sample.png");
        Assert.Contains(factory.StoredFiles(), path => path.Contains(image.Id.ToString(), StringComparison.Ordinal));

        var delete = await client.Http.DeleteAsync(new Uri($"/api/images/{image.Id}", UriKind.Relative));
        var again = await client.Http.DeleteAsync(new Uri($"/api/images/{image.Id}", UriKind.Relative));
        var get = await client.Http.GetAsync(new Uri($"/api/images/{image.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        await again.ReadProblemAsync(HttpStatusCode.NotFound);
        await get.ReadProblemAsync(HttpStatusCode.NotFound);
        Assert.DoesNotContain(factory.StoredFiles(), path => path.Contains(image.Id.ToString(), StringComparison.Ordinal));
    }
}
