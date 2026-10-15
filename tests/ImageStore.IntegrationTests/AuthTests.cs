using System.Net;
using System.Net.Http.Json;
using ImageStore.Application.Auth;
using ImageStore.IntegrationTests.TestSupport;

namespace ImageStore.IntegrationTests;

public sealed class AuthTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Register_Signs_The_User_In()
    {
        using var client = new ApiClient(factory);
        var email = ApiClient.NewEmail();

        var user = await client.RegisterAsync(email);
        var me = await client.Http.GetFromJsonAsync<CurrentUserDto>("/api/auth/me", ApiClient.Json);

        Assert.Equal(email, user.Email);
        Assert.Equal(user, me);
    }

    [Fact]
    public async Task Session_Cookies_Are_HttpOnly_Secure_And_SameSite_Strict()
    {
        using var client = new ApiClient(factory);
        await client.FetchCsrfTokenAsync();

        var response = await client.Http.PostAsJsonAsync("/api/auth/register", new { email = ApiClient.NewEmail(), password = ApiClient.DefaultPassword }, ApiClient.Json);

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var auth = Assert.Single(cookies, cookie => cookie.StartsWith("__Host-ImageStore.Auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", auth, StringComparison.OrdinalIgnoreCase);

        // The XSRF request token must be readable by the SPA (not HttpOnly).
        var xsrf = Assert.Single(cookies, cookie => cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.DoesNotContain("httponly", xsrf, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_And_Logout_Round_Trip()
    {
        var email = ApiClient.NewEmail();
        using (var registration = new ApiClient(factory))
        {
            await registration.RegisterAsync(email);
        }

        using var client = new ApiClient(factory);
        await client.FetchCsrfTokenAsync();
        var login = await client.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, ApiClient.DefaultPassword), ApiClient.Json);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode);

        var logout = await client.Http.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var me = await client.Http.GetAsync(new Uri("/api/auth/me", UriKind.Relative));
        await me.ReadProblemAsync(HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync(new Uri("/api/images", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Failed_Logins_Get_The_Same_Generic_Response()
    {
        var email = ApiClient.NewEmail();
        using (var registration = new ApiClient(factory))
        {
            await registration.RegisterAsync(email);
        }

        using var client = new ApiClient(factory);
        await client.FetchCsrfTokenAsync();
        var wrongPassword = await client.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Wrong-Passw0rd"), ApiClient.Json);
        var unknownUser = await client.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiClient.NewEmail(), "Wrong-Passw0rd"), ApiClient.Json);

        var first = await wrongPassword.ReadProblemAsync(HttpStatusCode.Unauthorized);
        var second = await unknownUser.ReadProblemAsync(HttpStatusCode.Unauthorized);
        Assert.Equal(first.GetProperty("detail").GetString(), second.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Account_Is_Locked_After_Repeated_Failures()
    {
        var email = ApiClient.NewEmail();
        using (var registration = new ApiClient(factory))
        {
            await registration.RegisterAsync(email);
        }

        using var client = new ApiClient(factory);
        await client.FetchCsrfTokenAsync();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Wrong-Passw0rd"), ApiClient.Json);
        }

        // Even the correct password is refused while the account is locked.
        var login = await client.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, ApiClient.DefaultPassword), ApiClient.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Register_Rejects_Duplicate_Emails_And_Weak_Passwords()
    {
        var email = ApiClient.NewEmail();
        using var client = new ApiClient(factory);
        await client.RegisterAsync(email);

        using var other = new ApiClient(factory);
        await other.FetchCsrfTokenAsync();
        var duplicate = await other.Http.PostAsJsonAsync("/api/auth/register", new { email, password = ApiClient.DefaultPassword }, ApiClient.Json);
        var weak = await other.Http.PostAsJsonAsync("/api/auth/register", new { email = ApiClient.NewEmail(), password = "short" }, ApiClient.Json);
        var invalid = await other.Http.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = "" }, ApiClient.Json);

        Assert.Equal(["email"], (await duplicate.ReadProblemAsync(HttpStatusCode.BadRequest)).ErrorKeys());
        Assert.Equal(["password"], (await weak.ReadProblemAsync(HttpStatusCode.BadRequest)).ErrorKeys());
        Assert.Equal(["email", "password"], (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).ErrorKeys());
    }

    [Fact]
    public async Task Unsafe_Requests_Without_A_Valid_Antiforgery_Token_Are_Rejected()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        client.SendXsrfHeader = false;

        var upload = await client.UploadAsync("sample.png");
        var logout = await client.Http.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null);

        var problem = await upload.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Equal("antiforgery_validation_failed", problem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.DoesNotContain(factory.StoredFiles(), path => path.Contains(client.User!.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Antiforgery_Tokens_Are_Bound_To_The_User()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);

        // Bob replays Alice's token: must be refused.
        bob.SendXsrfHeader = false;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-XSRF-TOKEN", alice.XsrfToken);
        var response = await bob.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string, string> ProtectedEndpoints() => new()
    {
        { "GET", "/api/images" },
        { "GET", "/api/images/upload-policy" },
        { "GET", $"/api/images/{Guid.NewGuid()}" },
        { "GET", $"/api/images/{Guid.NewGuid()}/content" },
        { "GET", $"/api/images/{Guid.NewGuid()}/download" },
        { "PUT", $"/api/images/{Guid.NewGuid()}" },
        { "DELETE", $"/api/images/{Guid.NewGuid()}" },
        { "POST", "/api/images" },
        { "GET", "/api/auth/me" },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Anonymous_Requests_To_Protected_Endpoints_Return_401(string method, string path)
    {
        using var client = new ApiClient(factory);
        await client.FetchCsrfTokenAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "PUT")
        {
            request.Content = JsonContent.Create(new { title = "x" });
        }
        else if (method is "POST")
        {
            request.Content = new MultipartFormDataContent { { new ByteArrayContent([1]), "files", "a.png" } };
        }

        var response = await client.Http.SendAsync(request);

        await response.ReadProblemAsync(HttpStatusCode.Unauthorized);
    }
}
