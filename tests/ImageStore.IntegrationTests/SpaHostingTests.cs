using System.Net;
using System.Net.Http.Json;
using ImageStore.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Hosting;

namespace ImageStore.IntegrationTests;

/// <summary>Hosts the API with a built SPA in its web root (single-server deployment).</summary>
public sealed class SpaHostingApiFactory : ImageStoreApiFactory
{
    public const string IndexMarker = "<!-- imagestore-spa-shell -->";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(WebRoot);
        File.WriteAllText(Path.Combine(WebRoot, "index.html"), $"<!doctype html><html><body>{IndexMarker}<app-root></app-root></body></html>");
        File.WriteAllText(Path.Combine(WebRoot, "main.js"), "console.log('spa');");
        base.ConfigureWebHost(builder);
    }
}

public sealed class SpaHostingTests(SpaHostingApiFactory factory) : IClassFixture<SpaHostingApiFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/images")]
    [InlineData("/images/0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("/login")]
    public async Task Client_Routes_Fall_Back_To_The_Spa_Shell_With_A_Strict_Csp(string path)
    {
        using var client = new ApiClient(factory);

        var response = await client.Http.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(SpaHostingApiFactory.IndexMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Static_Assets_Are_Served()
    {
        using var client = new ApiClient(factory);

        var response = await client.Http.GetAsync(new Uri("/main.js", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("spa", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_Routes_Never_Fall_Back_To_The_Spa()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var unknown = await client.Http.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));
        var wrongContentType = await client.Http.PostAsJsonAsync("/api/images", new { files = "x" }, ApiClient.Json);
        var missingImage = await client.Http.GetAsync(new Uri($"/api/images/{Guid.NewGuid()}/content", UriKind.Relative));

        await unknown.ReadProblemAsync(HttpStatusCode.NotFound);
        await wrongContentType.ReadProblemAsync(HttpStatusCode.UnsupportedMediaType);
        await missingImage.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dot_Segments_Never_Reach_Image_Content()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        await client.UploadSingleAsync("sample.png");

        // The server normalizes the dot segments; the result is a client route, never a stored file.
        var response = await client.Http.GetAsync(new Uri("/api/images/%2e%2e/%2e%2e/content", UriKind.Relative));

        Assert.NotEqual("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound, $"Unexpected {response.StatusCode}");
    }
}
