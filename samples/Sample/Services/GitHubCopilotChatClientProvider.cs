using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using OpenAI;
using Shiny;
using Shiny.Maui.AI;

namespace Sample.Services;

public class GitHubCopilotChatClientProvider(INavigator navigator) : IChatClientProvider
{
    const string ClientId = "Iv1.b507a08c87ecfe98";
    const string Scope = "read:user";
    const string DeviceCodeUrl = "https://github.com/login/device/code";
    const string AccessTokenUrl = "https://github.com/login/oauth/access_token";
    const string CopilotTokenUrl = "https://api.github.com/copilot_internal/v2/token";
    const string CopilotBaseUrl = "https://api.githubcopilot.com";
    const string DefaultModel = "gpt-4o";
    const string GitHubTokenKey = "github_oauth_token";

    static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders =
        {
            { "Accept", "application/json" },
            { "User-Agent", "GitHubCopilotChat/0.26.7" }
        }
    };

    string? cachedCopilotToken;
    DateTimeOffset copilotTokenExpiry = DateTimeOffset.MinValue;


    public async Task<DeviceCodeResponse> StartDeviceFlow(CancellationToken ct = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Scope
        });

        var response = await Http.PostAsync(DeviceCodeUrl, content, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(ct))!;
    }

    public async Task<bool> PollForToken(DeviceCodeResponse deviceCode, CancellationToken ct = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["device_code"] = deviceCode.DeviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
        });

        var response = await Http.PostAsync(AccessTokenUrl, content, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(ct);

        if (!String.IsNullOrEmpty(result?.AccessToken))
        {
            await SecureStorage.Default.SetAsync(GitHubTokenKey, result.AccessToken);
            return true;
        }

        return false;
    }

    public async Task<IChatClient> GetChatClient(CancellationToken cancelToken = default)
    {
        var githubToken = await SecureStorage.Default.GetAsync(GitHubTokenKey);
        if (githubToken == null)
            await this.RequestAuthentication(cancelToken);

        string copilotToken;
        try
        {
            copilotToken = await this.GetCopilotToken(cancelToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            this.SignOut();
            await this.RequestAuthentication(cancelToken);
            copilotToken = await this.GetCopilotToken(cancelToken);
        }

        var client = new OpenAIClient(
            new System.ClientModel.ApiKeyCredential(copilotToken),
            new OpenAIClientOptions { Endpoint = new Uri(CopilotBaseUrl) }
        );

        return client
            .GetChatClient(DefaultModel)
            .AsIChatClient();
    }

    public void SignOut()
    {
        SecureStorage.Default.Remove(GitHubTokenKey);
        this.cachedCopilotToken = null;
        this.copilotTokenExpiry = DateTimeOffset.MinValue;
    }

    async Task RequestAuthentication(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        using var reg = ct.Register(() => tcs.TrySetCanceled());

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await navigator.NavigateTo<Pages.LoginViewModel>(vm =>
            {
                vm.AuthenticationCompleted = tcs;
            });
        });

        await tcs.Task;
    }

    async Task<string> GetCopilotToken(CancellationToken ct)
    {
        if (this.cachedCopilotToken != null && DateTimeOffset.UtcNow.AddSeconds(60) < this.copilotTokenExpiry)
            return this.cachedCopilotToken;

        var githubToken = await SecureStorage.Default.GetAsync(GitHubTokenKey)
            ?? throw new InvalidOperationException("Not authenticated. Please sign in with GitHub first.");

        using var request = new HttpRequestMessage(HttpMethod.Get, CopilotTokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("token", githubToken);

        var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CopilotTokenResponse>(ct);

        this.cachedCopilotToken = result!.Token;
        this.copilotTokenExpiry = DateTimeOffset.FromUnixTimeSeconds(result.ExpiresAt);

        return this.cachedCopilotToken;
    }
}

public record DeviceCodeResponse(
    [property: JsonPropertyName("device_code")] string DeviceCode,
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("interval")] int Interval
);

record AccessTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription
);

record CopilotTokenResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires_at")] long ExpiresAt
);
