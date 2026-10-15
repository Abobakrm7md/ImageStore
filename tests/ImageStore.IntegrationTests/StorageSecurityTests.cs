using System.Net;
using ImageStore.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ImageStore.IntegrationTests;

public sealed class StorageSecurityTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("../../secret.txt")]
    [InlineData("/etc/passwd")]
    public async Task A_Tampered_Storage_Path_Is_Never_Followed(string tamperedPath)
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await client.UploadSingleAsync("sample.png");
        var secret = Path.Combine(factory.TestRoot, "secret.txt");
        await File.WriteAllTextAsync(secret, "TOP-SECRET");

        // Simulate a compromised database row pointing outside the storage root.
        await factory.WithDbContextAsync(async db =>
        {
            var row = await db.Images.SingleAsync(i => i.Id == image.Id);
            db.Entry(row).Property(i => i.RelativeStoragePath).CurrentValue = tamperedPath;
            return await db.SaveChangesAsync();
        });

        var content = await client.Http.GetAsync(new Uri($"/api/images/{image.Id}/content", UriKind.Relative));
        var download = await client.Http.GetAsync(new Uri($"/api/images/{image.Id}/download", UriKind.Relative));
        var delete = await client.Http.DeleteAsync(new Uri($"/api/images/{image.Id}", UriKind.Relative));

        foreach (var response in new[] { content, download })
        {
            await response.ReadProblemAsync(HttpStatusCode.NotFound);
            Assert.DoesNotContain("TOP-SECRET", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.True(File.Exists(secret), "Files outside the storage root must never be deleted.");
    }

    [Fact]
    public async Task A_Missing_File_Returns_404_Without_Leaking_Server_Paths()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await client.UploadSingleAsync("sample.jpg");
        File.Delete(Assert.Single(factory.StoredFiles(), path => path.Contains(image.Id.ToString(), StringComparison.Ordinal)));

        var response = await client.Http.GetAsync(new Uri($"/api/images/{image.Id}/content", UriKind.Relative));

        await response.ReadProblemAsync(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(factory.StorageRoot, body, StringComparison.Ordinal);
        Assert.DoesNotContain(client.User!.Id.ToString(), body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/images/..%2F..%2Fetc%2Fpasswd/content")]
    [InlineData("/api/images/%2e%2e/%2e%2e/content")]
    [InlineData("/api/images/not-a-guid/download")]
    public async Task Malformed_Image_Identifiers_Never_Reach_The_File_System(string path)
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var response = await client.Http.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_Storage_Directory_Is_Not_Served_As_Static_Files()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await client.UploadSingleAsync("sample.png");

        foreach (var path in new[] { $"/{client.User!.Id}/{image.Id}.png", $"/uploads/{client.User.Id}/{image.Id}.png", $"/storage/uploads/{client.User.Id}/{image.Id}.png" })
        {
            var response = await client.Http.GetAsync(new Uri(path, UriKind.Relative));
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
