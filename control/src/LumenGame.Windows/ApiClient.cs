using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LumenGame.Contracts;

namespace LumenGame.Windows;

public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _activationHttp = new() { Timeout = TimeSpan.FromMinutes(6) };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public void SetEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Enter a valid HTTPS control API address.", nameof(endpoint));
        }

        _http.BaseAddress = uri;
        _activationHttp.BaseAddress = uri;
    }

    public async Task<LoginResponse> LoginAsync(string userName, string password, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync("api/login", new LoginRequest(userName, password), _json, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("The username or password was not accepted.");
        }

        await EnsureSuccessAsync(response, cancellationToken);
        LoginResponse login = await response.Content.ReadFromJsonAsync<LoginResponse>(_json, cancellationToken)
            ?? throw new InvalidDataException("The login response was empty.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        _activationHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return login;
    }

    public async Task<LibraryResponse> GetLibraryAsync(CancellationToken cancellationToken) =>
        await _http.GetFromJsonAsync<LibraryResponse>("api/library", _json, cancellationToken)
        ?? throw new InvalidDataException("The world library response was empty.");

    public async Task<ServerStatusResponse> GetStatusAsync(CancellationToken cancellationToken) =>
        await _http.GetFromJsonAsync<ServerStatusResponse>("api/status", _json, cancellationToken)
        ?? throw new InvalidDataException("The server status response was empty.");

    public async Task ActivateAsync(ActivationRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _activationHttp.PostAsJsonAsync("api/admin/activate", request, _json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<UserSummary> CreateOrUpdateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync("api/admin/users", request, _json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<UserSummary>(_json, cancellationToken)
            ?? throw new InvalidDataException("The user response was empty.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiError>(_json, cancellationToken);
        }
        catch (JsonException)
        {
        }

        throw new InvalidOperationException(error?.Message ?? $"The control API returned HTTP {(int)response.StatusCode}.");
    }

    public void Dispose()
    {
        _http.Dispose();
        _activationHttp.Dispose();
    }
}
