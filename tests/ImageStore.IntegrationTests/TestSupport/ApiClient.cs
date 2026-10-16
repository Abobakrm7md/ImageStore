using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageStore.Api.Contracts;
using ImageStore.Application.Auth;
using ImageStore.Application.Common;
using ImageStore.Application.Images;
using ImageStore.Application.Posts;
using ImageStore.Application.Social;
using ImageStore.UnitTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace ImageStore.IntegrationTests.TestSupport;

/// <summary>
/// Behaves like the SPA: keeps cookies, and echoes the XSRF-TOKEN cookie in the X-XSRF-TOKEN
/// header for unsafe requests. Uses HTTPS so that Secure cookies are honoured.
/// </summary>
public sealed class ApiClient : IDisposable
{
    public const string DefaultPassword = "Passw0rd123";
    private static readonly Uri BaseAddress = new("https://localhost");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly XsrfHandler _xsrfHandler;

    public ApiClient(WebApplicationFactory<Program> factory)
    {
        _xsrfHandler = new XsrfHandler(Cookies, BaseAddress);
        Http = factory.CreateDefaultClient(BaseAddress, _xsrfHandler, new CookieContainerHandler(Cookies));
    }

    public CookieContainer Cookies { get; } = new();

    public HttpClient Http { get; }

    public CurrentUserDto? User { get; private set; }

    /// <summary>When false, unsafe requests are sent without the anti-forgery header.</summary>
    public bool SendXsrfHeader
    {
        get => _xsrfHandler.Enabled;
        set => _xsrfHandler.Enabled = value;
    }

    public string? XsrfToken => Cookies.GetCookies(BaseAddress)["XSRF-TOKEN"]?.Value;

    public static async Task<ApiClient> CreateAuthenticatedAsync(WebApplicationFactory<Program> factory, string? email = null)
    {
        var client = new ApiClient(factory);
        await client.RegisterAsync(email);
        return client;
    }

    public async Task<HttpResponseMessage> FetchCsrfTokenAsync()
    {
        var response = await Http.GetAsync(new Uri("/api/auth/csrf-token", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return response;
    }

    public async Task<CurrentUserDto> RegisterAsync(string? email = null, string password = DefaultPassword)
    {
        await FetchCsrfTokenAsync();
        var response = await Http.PostAsJsonAsync("/api/auth/register", new { email = email ?? NewEmail(), password }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        User = await response.Content.ReadFromJsonAsync<CurrentUserDto>(Json);
        return User!;
    }

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public Task<HttpResponseMessage> UploadAsync(params string[] fixtures) =>
        UploadAsync(fixtures.Select(name => new UploadPart(name, TestFiles.Read(name))).ToArray());

    public async Task<HttpResponseMessage> UploadAsync(
        UploadPart[] files,
        string? title = null,
        string? description = null,
        IEnumerable<string>? tags = null)
    {
        using var form = new MultipartFormDataContent();
        foreach (var file in files)
        {
            var content = new ByteArrayContent(file.Content);
            content.Headers.ContentType = new MediaTypeHeaderValue(file.DeclaredContentType);
            form.Add(content, "files", file.FileName);
        }

        if (title is not null)
        {
            form.Add(new StringContent(title), "title");
        }

        if (description is not null)
        {
            form.Add(new StringContent(description), "description");
        }

        foreach (var tag in tags ?? [])
        {
            form.Add(new StringContent(tag), "tags");
        }

        return await Http.PostAsync(new Uri("/api/images", UriKind.Relative), form);
    }

    public async Task<ImageDto> UploadSingleAsync(string fixture, string? title = null, string? description = null, IEnumerable<string>? tags = null)
    {
        var response = await UploadAsync([new UploadPart(fixture, TestFiles.Read(fixture))], title, description, tags);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UploadImagesResponse>(Json);
        return Assert.Single(body!.Items);
    }

    public async Task<PagedResult<ImageDto>> ListAsync(string query = "")
    {
        var response = await Http.GetAsync(new Uri($"/api/images{query}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<ImageDto>>(Json))!;
    }

    public Task<HttpResponseMessage> FollowAsync(Guid userId) =>
        Http.PostAsync(new Uri($"/api/users/{userId}/follow", UriKind.Relative), content: null);

    public Task<HttpResponseMessage> UnfollowAsync(Guid userId) =>
        Http.DeleteAsync(new Uri($"/api/users/{userId}/follow", UriKind.Relative));

    public async Task<UserProfileDto> GetProfileAsync(Guid userId)
    {
        var response = await Http.GetAsync(new Uri($"/api/users/{userId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserProfileDto>(Json))!;
    }

    /// <summary>Uploads a fixture image and publishes it as a post in one step.</summary>
    public async Task<ImagePostDto> PostImageAsync(string fixture = "sample.png", string? title = null, string? description = null)
    {
        var image = await UploadSingleAsync(fixture, title, description);
        return await CreatePostAsync(image.Id, title, description);
    }

    public async Task<ImagePostDto> CreatePostAsync(Guid imageId, string? title = null, string? description = null)
    {
        var response = await Http.PostAsJsonAsync("/api/posts", new CreatePostRequest(imageId, title, description), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImagePostDto>(Json))!;
    }

    public async Task<PagedResult<ImagePostDto>> GetFeedAsync(string query = "")
    {
        var response = await Http.GetAsync(new Uri($"/api/feed{query}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<ImagePostDto>>(Json))!;
    }

    public Task<HttpResponseMessage> ReactAsync(Guid postId, string reaction) =>
        Http.PutAsJsonAsync($"/api/posts/{postId}/my-reaction", new SetReactionRequest(reaction), Json);

    public Task<HttpResponseMessage> CommentAsync(Guid postId, string content) =>
        Http.PostAsJsonAsync($"/api/posts/{postId}/comments", new CreateCommentRequest(content), Json);

    public void Dispose() => Http.Dispose();

    private sealed class XsrfHandler(CookieContainer cookies, Uri baseAddress) : DelegatingHandler
    {
        public bool Enabled { get; set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Enabled && request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            {
                var token = cookies.GetCookies(baseAddress)["XSRF-TOKEN"]?.Value;
                if (token is not null)
                {
                    request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", token);
                }
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}

public sealed record UploadPart(string FileName, byte[] Content, string DeclaredContentType = "application/octet-stream");

public static class ProblemDetailsAssertions
{
    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement.Clone();
        Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
        Assert.True(root.TryGetProperty("traceId", out _), "Problem Details should carry a traceId.");
        return root;
    }

    public static string[] ErrorKeys(this JsonElement problem) =>
        problem.TryGetProperty("errors", out var errors) ? errors.EnumerateObject().Select(e => e.Name).Order().ToArray() : [];
}
