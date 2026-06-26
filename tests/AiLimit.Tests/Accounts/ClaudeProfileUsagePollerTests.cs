using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiLimit.Core.Domain;
using AiLimit.Core.Providers;
using AiLimit.Core.Providers.Accounts;

namespace AiLimit.Tests.Accounts;

public sealed class ClaudeProfileUsagePollerTests
{
    [Fact]
    public async Task PollAsyncReadsRealUsageAndCachesProfileIdentityWithFreshToken()
    {
        var now = DateTimeOffset.UtcNow;
        var fixture = new ProfileFixture(
            accessToken: "fresh-access",
            refreshToken: "fresh-refresh",
            expiresAt: now.AddHours(2).ToUnixTimeMilliseconds(),
            subscriptionType: "pro");
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/oauth/usage" => Json(HttpStatusCode.OK,
                """{"five_hour":{"utilization":40,"resets_at":"2026-06-24T09:00:00Z"},"seven_day":{"utilization":25,"resets_at":"2026-06-30T00:00:00Z"}}"""),
            "/api/oauth/profile" => Json(HttpStatusCode.OK,
                """{"account":{"email":"lotchip6@gmail.com","display_name":"Lotchip","has_claude_max":true,"has_claude_pro":false}}"""),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });

        try
        {
            var poller = new ClaudeProfileUsagePoller(
                new HttpClient(handler),
                () => now);

            var snapshot = await poller.PollAsync(fixture.CredentialsPath, CancellationToken.None);

            Assert.True(snapshot.IsSuccess, snapshot.ErrorMessage);
            Assert.Equal(AccountPlan.Max, snapshot.Plan);
            Assert.Collection(
                snapshot.Buckets,
                bucket =>
                {
                    Assert.Equal("Current session", bucket.ModelId);
                    Assert.Equal(60, bucket.PercentRemaining);
                },
                bucket =>
                {
                    Assert.Equal("Current week (all models)", bucket.ModelId);
                    Assert.Equal(75, bucket.PercentRemaining);
                });
            Assert.Equal("lotchip6@gmail.com", ClaudeAccountCache.ReadEmail(fixture.ProfilePath));
            Assert.DoesNotContain(handler.Requests, request => request.Path == "/v1/oauth/token");
            Assert.All(handler.Requests, request => Assert.Equal("fresh-access", request.BearerToken));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task PollAsyncRefreshesExpiredTokenAndAtomicallyUpdatesOnlyTargetProfile()
    {
        var now = DateTimeOffset.UtcNow;
        var fixture = new ProfileFixture(
            accessToken: "expired-access",
            refreshToken: "keep-if-omitted",
            expiresAt: now.AddHours(-1).ToUnixTimeMilliseconds(),
            subscriptionType: "pro",
            extraRootJson: "\"unrelated\":{\"keep\":true},");
        var siblingPath = fixture.WriteSiblingCredentials(".claude3", "sibling-access");
        var siblingBefore = File.ReadAllText(siblingPath);
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/oauth/token" => Json(HttpStatusCode.OK,
                """{"access_token":"renewed-access","expires_in":3600}"""),
            "/api/oauth/usage" => Json(HttpStatusCode.OK,
                """{"five_hour":{"utilization":10,"resets_at":null}}"""),
            "/api/oauth/profile" => Json(HttpStatusCode.NotFound, "{}"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });

        try
        {
            var poller = new ClaudeProfileUsagePoller(
                new HttpClient(handler),
                () => now);

            var snapshot = await poller.PollAsync(fixture.CredentialsPath, CancellationToken.None);

            Assert.True(snapshot.IsSuccess, snapshot.ErrorMessage);
            Assert.Single(snapshot.Buckets);
            Assert.Equal(90, snapshot.Buckets[0].PercentRemaining);
            using var updated = JsonDocument.Parse(File.ReadAllText(fixture.CredentialsPath));
            Assert.True(updated.RootElement.GetProperty("unrelated").GetProperty("keep").GetBoolean());
            var oauth = updated.RootElement.GetProperty("claudeAiOauth");
            Assert.Equal("renewed-access", oauth.GetProperty("accessToken").GetString());
            Assert.Equal("keep-if-omitted", oauth.GetProperty("refreshToken").GetString());
            Assert.Equal("pro", oauth.GetProperty("subscriptionType").GetString());
            Assert.True(oauth.GetProperty("expiresAt").GetInt64() > now.ToUnixTimeMilliseconds());
            Assert.Equal(siblingBefore, File.ReadAllText(siblingPath));
            Assert.Equal("renewed-access", handler.Requests.Single(r => r.Path == "/api/oauth/usage").BearerToken);
            Assert.Empty(Directory.EnumerateFiles(fixture.ProfilePath, "*.tmp"));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task PollAsyncRefreshesAndRetriesOnceWhenUsageReturnsUnauthorized()
    {
        var now = DateTimeOffset.UtcNow;
        var fixture = new ProfileFixture(
            accessToken: "rejected-access",
            refreshToken: "refresh-after-401",
            expiresAt: now.AddHours(2).ToUnixTimeMilliseconds(),
            subscriptionType: "max");
        var usageAttempts = 0;
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/oauth/usage" when ++usageAttempts == 1 => Json(HttpStatusCode.Unauthorized, "{}"),
            "/v1/oauth/token" => Json(HttpStatusCode.OK,
                """{"access_token":"retry-access","refresh_token":"retry-refresh","expires_in":3600}"""),
            "/api/oauth/usage" => Json(HttpStatusCode.OK,
                """{"five_hour":{"utilization":55,"resets_at":null}}"""),
            "/api/oauth/profile" => Json(HttpStatusCode.NotFound, "{}"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });

        try
        {
            var poller = new ClaudeProfileUsagePoller(
                new HttpClient(handler),
                () => now);

            var snapshot = await poller.PollAsync(fixture.CredentialsPath, CancellationToken.None);

            Assert.True(snapshot.IsSuccess, snapshot.ErrorMessage);
            Assert.Equal(2, usageAttempts);
            Assert.Equal(45, snapshot.Buckets.Single().PercentRemaining);
            Assert.Equal(
                ["rejected-access", "retry-access"],
                handler.Requests.Where(r => r.Path == "/api/oauth/usage").Select(r => r.BearerToken!).ToArray());
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task PollAsyncKeepsUsageWhenOptionalProfileLookupFails()
    {
        var now = DateTimeOffset.UtcNow;
        var fixture = new ProfileFixture(
            accessToken: "usage-still-valid",
            refreshToken: "refresh-token",
            expiresAt: now.AddHours(2).ToUnixTimeMilliseconds(),
            subscriptionType: "pro");
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/oauth/usage" => Json(HttpStatusCode.OK,
                """{"five_hour":{"utilization":30,"resets_at":null}}"""),
            "/api/oauth/profile" => throw new HttpRequestException("profile endpoint unavailable"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });

        try
        {
            var poller = new ClaudeProfileUsagePoller(new HttpClient(handler), () => now);

            var snapshot = await poller.PollAsync(fixture.CredentialsPath, CancellationToken.None);

            Assert.True(snapshot.IsSuccess, snapshot.ErrorMessage);
            Assert.Equal(AccountPlan.Pro, snapshot.Plan);
            Assert.Equal(70, snapshot.Buckets.Single().PercentRemaining);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task PollAsyncReturnsFailureWhenUsageTimesOut()
    {
        var now = DateTimeOffset.UtcNow;
        var fixture = new ProfileFixture(
            accessToken: "fresh-access",
            refreshToken: "fresh-refresh",
            expiresAt: now.AddHours(2).ToUnixTimeMilliseconds(),
            subscriptionType: "pro");
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/oauth/usage" => throw new TaskCanceledException("usage timed out"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });

        try
        {
            var poller = new ClaudeProfileUsagePoller(new HttpClient(handler), () => now);

            // A network timeout (TaskCanceledException) is not caller cancellation; it must become a
            // Failure snapshot, not propagate out of PollAsync and crash the async-void caller.
            var snapshot = await poller.PollAsync(fixture.CredentialsPath, CancellationToken.None);

            Assert.False(snapshot.IsSuccess);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task PollUsageAsyncReturnsFreshSnapshotWithWindowsAndAccountKey()
    {
        var now = DateTimeOffset.UtcNow;
        var fixture = new ProfileFixture(
            accessToken: "fresh-access",
            refreshToken: "fresh-refresh",
            expiresAt: now.AddHours(2).ToUnixTimeMilliseconds(),
            subscriptionType: "pro");
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/oauth/usage" => Json(HttpStatusCode.OK,
                """{"five_hour":{"utilization":40,"resets_at":"2026-06-24T09:00:00Z"}}"""),
            "/api/oauth/profile" => Json(HttpStatusCode.NotFound, "{}"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });

        try
        {
            var poller = new ClaudeProfileUsagePoller(new HttpClient(handler), () => now);

            var snapshot = await poller.PollUsageAsync(fixture.CredentialsPath, CancellationToken.None);

            Assert.Equal(UsageStatus.Fresh, snapshot.Status);
            Assert.NotEmpty(snapshot.Windows);
            Assert.True(snapshot.Windows[0].IsUsedPercent);
            Assert.Equal(40, snapshot.Windows[0].PercentRemaining);
            Assert.False(string.IsNullOrEmpty(snapshot.AccountKey));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.Scheme == "Bearer" ? request.Headers.Authorization.Parameter : null));
            return Task.FromResult(route(request));
        }
    }

    private sealed record CapturedRequest(string Path, string? BearerToken);

    private sealed class ProfileFixture : IDisposable
    {
        public ProfileFixture(
            string accessToken,
            string refreshToken,
            long expiresAt,
            string subscriptionType,
            string extraRootJson = "")
        {
            HomePath = Path.Combine(Path.GetTempPath(), "claude-poll-" + Guid.NewGuid().ToString("N"));
            ProfilePath = Path.Combine(HomePath, ".claude2");
            Directory.CreateDirectory(ProfilePath);
            CredentialsPath = Path.Combine(ProfilePath, ".credentials.json");
            File.WriteAllText(
                CredentialsPath,
                $$"""
                {
                  {{extraRootJson}}
                  "claudeAiOauth": {
                    "accessToken": "{{accessToken}}",
                    "refreshToken": "{{refreshToken}}",
                    "expiresAt": {{expiresAt}},
                    "subscriptionType": "{{subscriptionType}}",
                    "customField": "preserve-me"
                  }
                }
                """);
        }

        public string HomePath { get; }
        public string ProfilePath { get; }
        public string CredentialsPath { get; }

        public string WriteSiblingCredentials(string profileName, string accessToken)
        {
            var sibling = Path.Combine(HomePath, profileName);
            Directory.CreateDirectory(sibling);
            var path = Path.Combine(sibling, ".credentials.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                claudeAiOauth = new { accessToken }
            }));
            return path;
        }

        public void Dispose() => Directory.Delete(HomePath, recursive: true);
    }
}
