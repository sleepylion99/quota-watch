using System.Net.Http.Headers;
using System.Text.Json;

namespace AiLimit.Core.Providers.Accounts;

public sealed class AntigravityUserInfoClient
{
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v2/userinfo";
    private readonly HttpClient _httpClient;

    public AntigravityUserInfoClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string?> FetchEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("email", out var emailElement)
                && emailElement.ValueKind == JsonValueKind.String)
            {
                return emailElement.GetString();
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return null;
    }
}
