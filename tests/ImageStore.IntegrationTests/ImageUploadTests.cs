using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ImageStore.Api.Contracts;
using ImageStore.IntegrationTests.TestSupport;
using ImageStore.UnitTests.TestSupport;

namespace ImageStore.IntegrationTests;

public sealed class ImageUploadTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Theory]
    [InlineData("sample.jpg", "image/jpeg", 16, 12)]
    [InlineData("sample.png", "image/png", 20, 10)]
    [InlineData("sample-lossy.webp", "image/webp", 24, 18)]
    [InlineData("sample-lossless.webp", "image/webp", 8, 6)]
    [InlineData("sample-alpha.webp", "image/webp", 30, 22)]
    public async Task Uploads_Each_Supported_Format(string fixture, string contentType, int width, int height)
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var response = await client.UploadAsync(fixture);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var image = Assert.Single((await response.Content.ReadFromJsonAsync<UploadImagesResponse>(ApiClient.Json))!.Items);
        Assert.Equal($"/api/images/{image.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal(contentType, image.ContentType);
        Assert.Equal((width, height), (image.Width, image.Height));
        Assert.Equal(TestFiles.Read(fixture).Length, image.FileSizeBytes);
        Assert.Equal(fixture, image.OriginalFileName);
        Assert.Equal(DateTimeKind.Utc, image.CreatedAtUtc.Kind);

        var expectedPath = Path.Combine(factory.StorageRoot, client.User!.Id.ToString(), $"{image.Id}{Path.GetExtension(fixture)}");
        Assert.True(File.Exists(expectedPath), "The file should be stored as {userId}/{imageId}.{ext}.");
    }

    [Fact]
    public async Task Uploads_Several_Files_With_Shared_Metadata()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var response = await client.UploadAsync(
            [new UploadPart("a.png", TestFiles.Read("sample.png")), new UploadPart("b.jpg", TestFiles.Read("sample.jpg"))],
            title: "Trip",
            description: "Mountains",
            tags: ["Hiking", "alps"]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<UploadImagesResponse>(ApiClient.Json))!.Items;
        Assert.Equal(2, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal("Trip", item.Title);
            Assert.Equal("Mountains", item.Description);
            Assert.Equal(["alps", "Hiking"], item.Tags);
        });
    }

    [Fact]
    public async Task Content_Is_Streamed_With_Safe_Headers_And_Supports_Conditional_Requests()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await client.UploadSingleAsync("sample.jpg");

        var response = await client.Http.GetAsync(new Uri($"/api/images/{image.Id}/content", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestFiles.Read("sample.jpg"), await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.NotNull(response.Headers.ETag);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/images/{image.Id}/content");
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await client.Http.SendAsync(conditional)).StatusCode);
    }

    [Fact]
    public async Task Download_Uses_An_Attachment_With_The_Original_File_Name()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var response = await client.UploadAsync([new UploadPart("Été à la plage.png", TestFiles.Read("sample.png"))]);
        var image = (await response.Content.ReadFromJsonAsync<UploadImagesResponse>(ApiClient.Json))!.Items[0];

        var download = await client.Http.GetAsync(new Uri($"/api/images/{image.Id}/download", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("Été à la plage.png", download.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(TestFiles.Read("sample.png"), await download.Content.ReadAsByteArrayAsync());
    }

    public static TheoryData<string, string, byte[]> InvalidFiles() => new()
    {
        { "script disguised as png", "shell.png", Encoding.UTF8.GetBytes("<?php system($_GET['c']); ?>") },
        { "svg with script", "vector.png", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>") },
        { "html page", "page.jpg", Encoding.UTF8.GetBytes("<html><script>alert(document.cookie)</script></html>") },
        { "unsupported extension", "animation.gif", Encoding.UTF8.GetBytes("GIF89a....") },
        { "executable extension", "tool.exe", TestFiles.Read("sample.png") },
        { "png content with jpg extension", "photo.jpg", TestFiles.Read("sample.png") },
        { "truncated png", "broken.png", TestFiles.Read("sample.png")[..20] },
        { "empty file", "empty.png", [] },
    };

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public async Task Rejects_Invalid_Files_Without_Storing_Anything(string description, string fileName, byte[] content)
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var response = await client.UploadAsync([new UploadPart(fileName, content, "image/png")]);

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.True(problem.ErrorKeys().SequenceEqual(["files[0]"]), $"{description}: expected an error for files[0]");
        Assert.Equal(0, (await client.ListAsync()).TotalCount);
        Assert.DoesNotContain(factory.StoredFiles(), path => path.Contains(client.User!.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejects_Oversized_Files()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var oversized = TestFiles.CreatePng(64, 64).Concat(new byte[ImageStoreApiFactory.MaxFileSizeBytes]).ToArray();

        var response = await client.UploadAsync([new UploadPart("big.png", oversized)]);

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("maximum size", problem.GetProperty("errors").GetProperty("files[0]")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_Requests_Larger_Than_The_Upload_Limit_With_413()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var huge = new byte[(ImageStoreApiFactory.MaxFileSizeBytes * ImageStoreApiFactory.MaxFilesPerRequest) + (2 * 1024 * 1024)];

        var response = await client.UploadAsync([new UploadPart("huge.png", huge)]);

        await response.ReadProblemAsync(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Rejects_Too_Many_Files()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var tooMany = Enumerable.Range(0, ImageStoreApiFactory.MaxFilesPerRequest + 1)
            .Select(i => new UploadPart($"img{i}.png", TestFiles.Read("sample.png")))
            .ToArray();

        var response = await client.UploadAsync(tooMany);

        Assert.Equal(["files"], (await response.ReadProblemAsync(HttpStatusCode.BadRequest)).ErrorKeys());
        Assert.Equal(0, (await client.ListAsync()).TotalCount);
    }

    [Fact]
    public async Task Rejects_Forms_Without_Files_And_Malformed_Multipart_Bodies()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var withoutFiles = await client.UploadAsync([], title: "Only a title");
        using var emptyBody = new MultipartFormDataContent();
        var malformed = await client.Http.PostAsync(new Uri("/api/images", UriKind.Relative), emptyBody);

        Assert.Equal(["files"], (await withoutFiles.ReadProblemAsync(HttpStatusCode.BadRequest)).ErrorKeys());
        await malformed.ReadProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_Batch_With_One_Invalid_File_Stores_Nothing()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var response = await client.UploadAsync(
            [new UploadPart("ok.png", TestFiles.Read("sample.png")), new UploadPart("bad.png", Encoding.UTF8.GetBytes("nope"))]);

        Assert.Equal(["files[1]"], (await response.ReadProblemAsync(HttpStatusCode.BadRequest)).ErrorKeys());
        Assert.Equal(0, (await client.ListAsync()).TotalCount);
        Assert.Empty(factory.StagingFiles());
    }

    [Theory]
    [InlineData("../../../../etc/cron.d/evil.png", "evil.png")]
    [InlineData("..\\..\\..\\Windows\\evil.png", "evil.png")]
    [InlineData("/absolute/path/evil.png", "evil.png")]
    public async Task Client_File_Names_Never_Influence_The_Storage_Location(string maliciousName, string expectedName)
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var response = await client.UploadAsync([new UploadPart(maliciousName, TestFiles.Read("sample.png"))]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var image = (await response.Content.ReadFromJsonAsync<UploadImagesResponse>(ApiClient.Json))!.Items[0];
        Assert.Equal(expectedName, image.OriginalFileName);

        var stored = Assert.Single(factory.StoredFiles(), path => path.Contains(image.Id.ToString(), StringComparison.Ordinal));
        Assert.Equal(Path.Combine(factory.StorageRoot, client.User!.Id.ToString(), $"{image.Id}.png"), stored);
        Assert.Empty(Directory.GetFiles(factory.TestRoot, "evil.png", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Upload_Requires_Multipart_Form_Data()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var response = await client.Http.PostAsJsonAsync("/api/images", new { files = "x" }, ApiClient.Json);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Declared_Content_Type_Is_Ignored_In_Favour_Of_The_File_Signature()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(TestFiles.Read("sample-lossy.webp"));
        part.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        form.Add(part, "files", "photo.webp");

        var response = await client.Http.PostAsync(new Uri("/api/images", UriKind.Relative), form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("image/webp", (await response.Content.ReadFromJsonAsync<UploadImagesResponse>(ApiClient.Json))!.Items[0].ContentType);
    }
}

/// <summary>Uses its own host so the simulated database failure cannot affect other tests.</summary>
public sealed class UploadFailureTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Files_Are_Removed_When_The_Database_Write_Fails()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        factory.SaveChangesFailure.FailImageInserts = true;
        try
        {
            var response = await client.UploadAsync("sample.png", "sample.jpg");

            var problem = await response.ReadProblemAsync(HttpStatusCode.InternalServerError);
            Assert.False(problem.TryGetProperty("detail", out var detail) && detail.GetString()!.Contains("Simulated", StringComparison.Ordinal),
                "Exception messages must not leak to clients.");
        }
        finally
        {
            factory.SaveChangesFailure.FailImageInserts = false;
        }

        Assert.Empty(factory.StoredFiles());
        Assert.Empty(factory.StagingFiles());
        Assert.Equal(0, (await client.ListAsync()).TotalCount);
    }
}
