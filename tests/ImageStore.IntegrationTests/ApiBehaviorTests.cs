using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageStore.Application.Images;
using ImageStore.IntegrationTests.TestSupport;

namespace ImageStore.IntegrationTests;

public sealed class ApiBehaviorTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Unknown_Api_Routes_Return_Problem_Details()
    {
        using var anonymous = new ApiClient(factory);
        using var authenticated = await ApiClient.CreateAuthenticatedAsync(factory);

        var anonymousResponse = await anonymous.Http.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));
        var authenticatedResponse = await authenticated.Http.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        // The fallback authorization policy also covers unmatched routes, so anonymous callers
        // cannot probe which routes exist.
        await anonymousResponse.ReadProblemAsync(HttpStatusCode.Unauthorized);
        await authenticatedResponse.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Responses_Carry_Security_Headers()
    {
        using var client = new ApiClient(factory);

        var response = await client.Http.GetAsync(new Uri("/api/auth/csrf-token", UriKind.Relative));

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApi_Document_Describes_The_Endpoints()
    {
        using var client = new ApiClient(factory);

        using var document = JsonDocument.Parse(await client.Http.GetStringAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)));

        var paths = document.RootElement.GetProperty("paths");
        foreach (var path in new[]
        {
            "/api/auth/register", "/api/auth/login", "/api/auth/logout", "/api/auth/me",
            "/api/images", "/api/images/{id}", "/api/images/{id}/content", "/api/images/{id}/download",
            "/api/users", "/api/users/me", "/api/users/{userId}", "/api/users/{userId}/posts",
            "/api/users/{userId}/follow", "/api/users/{userId}/followers", "/api/users/{userId}/following",
            "/api/users/{userId}/profile-picture", "/api/feed", "/api/posts", "/api/posts/{postId}",
            "/api/posts/{postId}/content", "/api/posts/{postId}/download", "/api/posts/{postId}/comments",
            "/api/posts/{postId}/reactions", "/api/posts/{postId}/my-reaction",
        })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"Missing {path}");
        }

        var upload = paths.GetProperty("/api/images").GetProperty("post");
        Assert.True(upload.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
    }

    [Fact]
    public async Task Health_Endpoint_Is_Available_Anonymously()
    {
        using var client = new ApiClient(factory);

        var response = await client.Http.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_Policy_Reflects_The_Configuration()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var policy = await client.Http.GetFromJsonAsync<UploadPolicyDto>("/api/images/upload-policy", ApiClient.Json);

        Assert.Equal(ImageStoreApiFactory.MaxFileSizeBytes, policy!.MaxFileSizeBytes);
        Assert.Equal(ImageStoreApiFactory.MaxFilesPerRequest, policy.MaxFilesPerRequest);
        Assert.Equal([".jpg", ".jpeg", ".png", ".webp"], policy.AllowedExtensions);
    }
}
