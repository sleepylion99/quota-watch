using System.Net;
using System.Text;
using AiLimit.Core.Domain;
using AiLimit.Core.Providers;

namespace AiLimit.Tests;

public sealed class AntigravityUsageProviderTests
{
    [Fact]
    public async Task RefreshAsyncMapsQuotaBucketsToSnapshot()
    {
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
        [
            new AntigravityQuotaBucket("gemini-2.5-pro", 99, DateTimeOffset.Parse("2026-05-31T00:00:00Z")),
            new AntigravityQuotaBucket("gemini-2.5-flash", 77, DateTimeOffset.Parse("2026-05-31T00:00:00Z")),
            new AntigravityQuotaBucket("gemini-2.5-flash-lite", 91, DateTimeOffset.Parse("2026-05-31T00:00:00Z")),
            new AntigravityQuotaBucket("gemini-3.1-pro-preview", 100, DateTimeOffset.Parse("2026-05-31T00:00:00Z"))
        ]));
        var provider = new AntigravityUsageProvider(
            client,
            resolveOAuthClientOrigin: () => AntigravityOAuthClientOrigin.None);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal("gemini-pro", snapshot.ProviderId);
        Assert.Null(snapshot.AccountKey);
        Assert.Null(snapshot.SourceChannel);
        Assert.Null(snapshot.CloudFailureSummary);
        Assert.Null(snapshot.IdeFailureSummary);
        Assert.Equal("none", snapshot.OAuthClientOrigin);
        Assert.Equal("Google Antigravity", snapshot.DisplayName);
        Assert.Equal(UsageSource.Agent, snapshot.Source);
        Assert.Equal(UsageStatus.Fresh, snapshot.Status);
        Assert.Collection(
            snapshot.Windows,
            window =>
            {
                Assert.Equal("antigravity-gemini-2-5-flash", window.Id);
                Assert.Equal("gemini-2.5-flash", window.Label);
                Assert.Equal(23, window.PercentRemaining);
            },
            window =>
            {
                Assert.Equal("antigravity-gemini-2-5-flash-lite", window.Id);
                Assert.Equal("gemini-2.5-flash-lite", window.Label);
                Assert.Equal(9, window.PercentRemaining);
            },
            window =>
            {
                Assert.Equal("antigravity-gemini-2-5-pro", window.Id);
                Assert.Equal("gemini-2.5-pro", window.Label);
                Assert.Equal(1, window.PercentRemaining);
            },
            window =>
            {
                Assert.Equal("antigravity-gemini-3-1-pro-preview", window.Id);
                Assert.Equal("gemini-3.1-pro-preview", window.Label);
                Assert.Equal(0, window.PercentRemaining);
            });
    }

    [Fact]
    public async Task RefreshAsyncShowsClaudeAndGptOssAsSeparateModelQuotas()
    {
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
        [
            new AntigravityQuotaBucket("Gemini 3.5 Flash (Medium)", 91, DateTimeOffset.Parse("2026-05-31T00:00:00Z")),
            new AntigravityQuotaBucket("Claude Sonnet 4.6 (Thinking)", 73, DateTimeOffset.Parse("2026-05-31T09:00:00Z")),
            new AntigravityQuotaBucket("GPT-OSS 120B (Medium)", 88, DateTimeOffset.Parse("2026-05-31T10:00:00Z"))
        ]));
        var provider = new AntigravityUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Collection(
            snapshot.Windows,
            window =>
            {
                Assert.Equal("antigravity-gemini-3-5-flash-medium", window.Id);
                Assert.Equal("Gemini 3.5 Flash (Medium)", window.Label);
                Assert.Equal(9, window.PercentRemaining);
            },
            window =>
            {
                Assert.Equal("antigravity-claude-sonnet-4-6-thinking", window.Id);
                Assert.Equal("Claude Sonnet 4.6 (Thinking)", window.Label);
                Assert.Equal(27, window.PercentRemaining);
                Assert.Equal(DateTimeOffset.Parse("2026-05-31T09:00:00Z"), window.ResetAt);
            },
            window =>
            {
                Assert.Equal("antigravity-gpt-oss-120b-medium", window.Id);
                Assert.Equal("GPT-OSS 120B (Medium)", window.Label);
                Assert.Equal(12, window.PercentRemaining);
                Assert.Equal(DateTimeOffset.Parse("2026-05-31T10:00:00Z"), window.ResetAt);
            });
    }

    [Fact]
    public async Task RefreshAsyncKeepsTierVariantsAndUsesHighConfidence()
    {
        var resetAt = DateTimeOffset.Parse("2026-06-15T08:17:00Z");
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
        [
            new AntigravityQuotaBucket("Gemini 3.5 Flash (Medium)", 100, resetAt),
            new AntigravityQuotaBucket("Gemini 3.5 Flash (High)", 100, resetAt),
            new AntigravityQuotaBucket("Gemini 3.5 Flash (Low)", 100, resetAt),
            new AntigravityQuotaBucket("Gemini 3.1 Pro (Low)", 100, resetAt),
            new AntigravityQuotaBucket("Gemini 3.1 Pro (High)", 100, resetAt),
            new AntigravityQuotaBucket("Claude Sonnet 4.6 (Thinking)", 100, resetAt),
            new AntigravityQuotaBucket("Claude Opus 4.6 (Thinking)", 100, resetAt),
            new AntigravityQuotaBucket("GPT-OSS 120B (Medium)", 100, resetAt)
        ]));
        var provider = new AntigravityUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Collection(
            snapshot.Windows,
            window =>
            {
                Assert.Equal("Gemini 3.5 Flash (Medium)", window.Label);
                Assert.Equal("high", window.Confidence);
            },
            window =>
            {
                Assert.Equal("Gemini 3.5 Flash (High)", window.Label);
                Assert.Equal("high", window.Confidence);
            },
            window =>
            {
                Assert.Equal("Gemini 3.5 Flash (Low)", window.Label);
                Assert.Equal("high", window.Confidence);
            },
            window =>
            {
                Assert.Equal("Gemini 3.1 Pro (High)", window.Label);
                Assert.Equal("high", window.Confidence);
            },
            window =>
            {
                Assert.Equal("Gemini 3.1 Pro (Low)", window.Label);
                Assert.Equal("high", window.Confidence);
            },
            window =>
            {
                Assert.Equal("Claude Sonnet 4.6 (Thinking)", window.Label);
                Assert.Equal("high", window.Confidence);
            },
            window =>
            {
                Assert.Equal("Claude Opus 4.6 (Thinking)", window.Label);
                Assert.Equal("high", window.Confidence);
            },
            window =>
            {
                Assert.Equal("GPT-OSS 120B (Medium)", window.Label);
                Assert.Equal("high", window.Confidence);
            });
    }

    [Fact]
    public async Task RefreshAsyncCollapsesDuplicateModelLabels()
    {
        var resetAt = DateTimeOffset.Parse("2026-06-23T05:30:52Z");
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
        [
            new AntigravityQuotaBucket("Gemini 3.1 Flash Lite", 0, resetAt),
            new AntigravityQuotaBucket("Gemini 3.1 Flash Lite", 0, resetAt),
            new AntigravityQuotaBucket("Gemini 3.1 Flash Lite", 0, resetAt),
            new AntigravityQuotaBucket("Gemini 3.1 Flash Lite", 0, resetAt),
            new AntigravityQuotaBucket("Gemini 3.1 Pro (High)", 0, resetAt),
            new AntigravityQuotaBucket("Gemini 3.1 Pro (High)", 0, resetAt),
            new AntigravityQuotaBucket("Gemini 3.1 Pro (Low)", 0, resetAt)
        ]));
        var provider = new AntigravityUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        // Identical labels collapse to one window; tier variants (High vs Low) survive.
        Assert.Collection(
            snapshot.Windows,
            window =>
            {
                Assert.Equal("antigravity-gemini-3-1-flash-lite", window.Id);
                Assert.Equal("Gemini 3.1 Flash Lite", window.Label);
            },
            window =>
            {
                Assert.Equal("antigravity-gemini-3-1-pro-high", window.Id);
                Assert.Equal("Gemini 3.1 Pro (High)", window.Label);
            },
            window =>
            {
                Assert.Equal("antigravity-gemini-3-1-pro-low", window.Id);
                Assert.Equal("Gemini 3.1 Pro (Low)", window.Label);
            });
        Assert.Equal(
            snapshot.Windows.Count,
            snapshot.Windows.Select(window => window.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task RefreshAsyncHidesCloudOnlyModelsWithoutReasoningVariant()
    {
        var resetAt = DateTimeOffset.Parse("2026-06-23T05:30:52Z");
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
            [
                new AntigravityQuotaBucket("Gemini 3.5 Flash (Medium)", 0, resetAt),
                new AntigravityQuotaBucket("Gemini 3 Flash", 0, resetAt),
                new AntigravityQuotaBucket("Gemini 3.1 Flash Image", 0, resetAt),
                new AntigravityQuotaBucket("Gemini 3.1 Flash Lite", 0, resetAt),
                new AntigravityQuotaBucket("Gemini 2.5 Pro", 0, resetAt),
                new AntigravityQuotaBucket("Gemini 3.1 Pro (High)", 0, resetAt),
                new AntigravityQuotaBucket("Claude Opus 4.6 (Thinking)", 0, resetAt),
                new AntigravityQuotaBucket("GPT-OSS 120B (Medium)", 0, resetAt)
            ],
            channel: "cloud"));
        var provider = new AntigravityUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        // Cloud-only base models without a reasoning/thinking variant are hidden to match the
        // Antigravity IDE picker; tier and thinking variants survive.
        Assert.Equal("cloud", snapshot.SourceChannel);
        Assert.Equal(
            new[]
            {
                "Gemini 3.5 Flash (Medium)",
                "Gemini 3.1 Pro (High)",
                "Claude Opus 4.6 (Thinking)",
                "GPT-OSS 120B (Medium)"
            },
            snapshot.Windows.Select(window => window.Label).ToArray());
    }

    [Fact]
    public async Task RefreshAsyncFiltersIdeFallbackToReasoningModels()
    {
        var resetAt = DateTimeOffset.Parse("2026-06-23T05:30:52Z");
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
            [
                new AntigravityQuotaBucket("gemini-3.5-flash-medium", 0, resetAt),
                new AntigravityQuotaBucket("gemini-2.5-pro", 0, resetAt)
            ],
            channel: "ide-fallback"));
        var provider = new AntigravityUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        // The same reasoning-tier filter applies to the IDE fallback: the bare base model is hidden.
        Assert.Equal("ide-fallback", snapshot.SourceChannel);
        Assert.Equal(
            new[] { "gemini-3.5-flash-medium" },
            snapshot.Windows.Select(window => window.Label).ToArray());
    }

    [Fact]
    public async Task RefreshAsyncDoesNotInventInactiveSharedQuotaWindows()
    {
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
        [
            new AntigravityQuotaBucket("Claude Sonnet 4.6 (Thinking)", 73, DateTimeOffset.Parse("2026-05-31T09:00:00Z"))
        ]));
        var provider = new AntigravityUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        var window = Assert.Single(snapshot.Windows);
        Assert.Equal("antigravity-claude-sonnet-4-6-thinking", window.Id);
        Assert.Equal("Claude Sonnet 4.6 (Thinking)", window.Label);
        Assert.Equal(27, window.PercentRemaining);
    }

    [Fact]
    public async Task RefreshAsyncShowsCloudAndIdeSetupGuidanceWhenAntigravityFails()
    {
        var provider = new AntigravityUsageProvider(new ThrowingAntigravityUsageClient(
            "Cloud failed; IDE endpoint discovery returned no candidates."),
            hasOAuthClientValues: () => true);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageStatus.Failed, snapshot.Status);
        Assert.Contains("Antigravity Cloud", snapshot.LastError);
        Assert.Contains("sign in to Antigravity with your Google account", snapshot.LastError);
        Assert.Contains("Antigravity IDE", snapshot.LastError);
    }

    [Fact]
    public async Task RefreshAsyncAddsMissingOAuthClientHintWhenClientValuesAreAbsent()
    {
        var provider = new AntigravityUsageProvider(
            new ThrowingAntigravityUsageClient("Usage refresh timed out after 10 seconds."),
            hasOAuthClientValues: () => false);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageStatus.Failed, snapshot.Status);
        Assert.Contains("OAuth client values were not found", snapshot.LastError);
        Assert.Contains("Usage refresh timed out", snapshot.LastError);
    }



    [Fact]
    public void ParseQuotaBucketsFindsNestedPrivateApiShape()
    {
        const string json = """
        {
          "quotaGroups": [
            {
              "quotaBuckets": [
                {
                  "modelId": "gemini-2.5-pro",
                  "remainingFraction": 0.42,
                  "resetTime": "2026-05-31T00:00:00Z"
                }
              ]
            }
          ]
        }
        """;

        var buckets = AntigravityQuotaParser.ParseQuotaBuckets(json);

        var bucket = Assert.Single(buckets);
        Assert.Equal("gemini-2.5-pro", bucket.ModelId);
        Assert.Equal(42, bucket.PercentRemaining);
        Assert.Equal(DateTimeOffset.Parse("2026-05-31T00:00:00Z"), bucket.ResetAt);
    }

    [Fact]
    public void ParseQuotaBucketsFindsAntigravityLocalUserStatusShape()
    {
        const string json = """
        {
          "userStatus": {
            "email": "User@Example.COM",
            "cascadeModelConfigData": {
              "clientModelConfigs": [
                {
                  "modelOrAlias": { "model": "claude-sonnet-4.6-thinking" },
                  "label": "Claude Sonnet 4.6 (Thinking)",
                  "quotaInfo": {
                    "remainingFraction": 0.75,
                    "resetTime": "2026-05-31T09:00:00Z"
                  }
                },
                {
                  "modelOrAlias": { "model": "chat_internal" },
                  "label": "Internal",
                  "quotaInfo": {
                    "remainingFraction": 0.5
                  }
                }
              ]
            }
          }
        }
        """;

        var buckets = AntigravityQuotaParser.ParseQuotaBuckets(json);

        var bucket = Assert.Single(buckets);
        Assert.Equal("Claude Sonnet 4.6 (Thinking)", bucket.ModelId);
        Assert.Equal(75, bucket.PercentRemaining);
        Assert.Equal(DateTimeOffset.Parse("2026-05-31T09:00:00Z"), bucket.ResetAt);
        Assert.Equal("email:user@example.com", AntigravityQuotaParser.ExtractAccountKey(json));
    }

    [Fact]
    public async Task RefreshAsyncCopiesAccountKeyFromUsageClient()
    {
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
        [
            new AntigravityQuotaBucket("Gemini 3.5 Flash (Medium)", 91, DateTimeOffset.Parse("2026-05-31T00:00:00Z"))
        ], "email:user@example.com"));
        var provider = new AntigravityUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal("email:user@example.com", snapshot.AccountKey);
    }

    [Fact]
    public void AntigravityLocalRequestsUseIdeMetadata()
    {
        Assert.Contains(@"""metadata""", AntigravityOAuthUsageClient.LocalRequestBody);
        Assert.Contains(@"""ideName"":""Antigravity""", AntigravityOAuthUsageClient.LocalRequestBody);
        Assert.Contains(@"""extensionName"":""antigravity""", AntigravityOAuthUsageClient.LocalRequestBody);
    }

    [Fact]
    public void AntigravityCloudProjectRequestUsesMinimalAntigravityMetadata()
    {
        Assert.Equal(
            "{\"metadata\":{\"ideType\":\"ANTIGRAVITY\"}}",
            AntigravityOAuthUsageClient.CloudLoadCodeAssistBody);
        Assert.DoesNotContain("pluginType", AntigravityOAuthUsageClient.CloudLoadCodeAssistBody);
        Assert.DoesNotContain("platform", AntigravityOAuthUsageClient.CloudLoadCodeAssistBody);
        Assert.Equal("antigravity/windows/amd64", AntigravityOAuthUsageClient.CloudUserAgent);
    }

    [Fact]
    public void AntigravityLogRootsIncludeStandaloneIde()
    {
        Assert.Contains(
            AntigravityLocalEndpoint.LogRoots,
            path => path.EndsWith(Path.Combine("Antigravity IDE", "logs"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AntigravityFailureMentionsMissingInstallation()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.Core", "Providers", "AntigravityUsageProvider.cs"));
        var installSource = File.ReadAllText(SourceFile("src", "AiLimit.Core", "Providers", "AntigravityInstallation.cs"));

        Assert.Contains("AntigravityInstallation.IsProbablyInstalled()", source);
        Assert.Contains("Antigravity installation was not found", source);
        Assert.Contains("Uninstall", installSource);
        Assert.Contains("Antigravity IDE.lnk", installSource);
    }

    [Fact]
    public void AntigravityEndpointDiscoveryOnlyUsesLogsForLiveProcess()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.Core", "Providers", "AntigravityLocalEndpoint.cs"));

        Assert.Contains("return processEndpoint is null ? null : Merge(logEndpoint, processEndpoint);", source);
        Assert.Contains("SelectMany(endpoint => endpoint.BaseUrls)", source);
    }

    [Fact]
    public void AntigravityEndpointOnlySendsCsrfTokenToProtectedBaseUrls()
    {
        var endpoint = new AntigravityLocalEndpoint(
            ["http://127.0.0.1:1000", "http://127.0.0.1:2000"],
            "csrf-token",
            new HashSet<string>(["http://127.0.0.1:1000"], StringComparer.OrdinalIgnoreCase));

        Assert.True(endpoint.ShouldSendCsrfToken("http://127.0.0.1:1000"));
        Assert.True(endpoint.ShouldSendCsrfToken("HTTP://127.0.0.1:1000"));
        Assert.False(endpoint.ShouldSendCsrfToken("http://127.0.0.1:2000"));
    }

    [Fact]
    public void AntigravityEndpointDiscoveryPrefersExtensionHttpAndLanguageServerHttps()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.Core", "Providers", "AntigravityLocalEndpoint.cs"));

        Assert.Contains("baseUrls.Add($\"http://127.0.0.1:{extensionPort.Value}\")", source);
        Assert.Contains("baseUrls.Add($\"https://127.0.0.1:{port}\")", source);
        Assert.DoesNotContain("""url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 0 : 1""", source);
    }

    [Fact]
    public void AntigravityReadUsageTriesIdeBeforeCloudWhenLocalDetectionIsEnabled()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.Core", "Providers", "AntigravityUsageProvider.cs"));

        Assert.True(
            source.IndexOf("TryReadIdeUsageAsync", StringComparison.Ordinal)
            < source.IndexOf("TryReadCloudUsageAsync", StringComparison.Ordinal));
        Assert.Contains("Sign in to Antigravity with a Google account, or start Antigravity IDE as a fallback", source);
    }

    [Fact]
    public void AntigravityProcessDiscoveryFiltersToLanguageServer()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.Core", "Providers", "AntigravityLocalEndpoint.cs"));

        Assert.Contains("$_.Name -eq 'language_server_windows_x64.exe'", source);
    }

    [Fact]
    public void AntigravityLogDiscoveryReadsActiveLogsAndMergesCandidates()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.Core", "Providers", "AntigravityLocalEndpoint.cs"));

        Assert.Contains("FileShare.ReadWrite | FileShare.Delete", source);
        Assert.Contains("Merge(candidates.Select(file => TryDiscoverFromLog(file.FullName)).ToArray())", source);
    }

    [Fact]
    public void ParseQuotaBucketsFindsAntigravityFetchAvailableModelsShape()
    {
        const string json = """
        {
          "models": {
            "gemini-3.5-flash-medium": {
              "displayName": "Gemini 3.5 Flash (Medium)",
              "quotaInfo": {
                "remainingFraction": 0.99
              }
            },
            "gpt-oss-120b-medium": {
              "displayName": "GPT-OSS 120B (Medium)",
              "quotaInfo": {
                "remainingFraction": 0.88
              }
            },
            "imagen-3.0-generate": {
              "displayName": "Imagen 3",
              "quotaInfo": {
                "remainingFraction": 0.67
              }
            },
            "chat_internal": {
              "displayName": "Internal",
              "quotaInfo": {
                "remainingFraction": 0.5
              }
            }
          }
        }
        """;

        var buckets = AntigravityQuotaParser.ParseQuotaBuckets(json);

        Assert.Collection(
            buckets,
            bucket =>
            {
                Assert.Equal("Gemini 3.5 Flash (Medium)", bucket.ModelId);
                Assert.Equal(99, bucket.PercentRemaining);
            },
            bucket =>
            {
                Assert.Equal("GPT-OSS 120B (Medium)", bucket.ModelId);
                Assert.Equal(88, bucket.PercentRemaining);
            },
            bucket =>
            {
                Assert.Equal("Imagen 3", bucket.ModelId);
                Assert.Equal(67, bucket.PercentRemaining);
            });
    }

    [Fact]
    public void SourceDoesNotDisableTlsValidationOrLogTokens()
    {
        var usageSource = File.ReadAllText(SourceFile("src", "AiLimit.Core", "Providers", "AntigravityUsageProvider.cs"));
        var credentialStoreSource = File.ReadAllText(SourceFile("src", "AiLimit.Core", "Providers", "AntigravityOAuthCredentialStore.cs"));
        var combinedSource = usageSource + credentialStoreSource;

        Assert.DoesNotContain("DangerousAcceptAnyServerCertificateValidator", combinedSource);
        Assert.Contains("ANTIGRAVITY_OAUTH_CLIENT_SECRET", credentialStoreSource);
        Assert.DoesNotContain("File.WriteAllText", usageSource);
        Assert.DoesNotContain("accessToken}", usageSource);
        Assert.DoesNotContain("RefreshToken}", credentialStoreSource);
    }

    [Fact]
    public void AntigravityRefreshFallsBackToBundledOAuthClientSecretWhenNoneConfigured()
    {
        // The bundled default OAuth client is always available, so secret resolution no
        // longer throws "configure your secret" — it transparently yields the bundled secret.
        var previousSecret = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", null);
            var credentials = new AntigravityOAuthCredentials(null, "1//refresh-token", null, null, null);

            var secret = AntigravityOAuthCredentialStore.ResolveOAuthClientSecret(credentials);

            Assert.False(string.IsNullOrEmpty(secret));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", previousSecret);
        }
    }

    [Fact]
    public void AntigravityRefreshCanUseExplicitOAuthClientIdFromEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", "explicit-client-id.apps.googleusercontent.com");
            var credentials = new AntigravityOAuthCredentials(null, "1//refresh-token", null, null, null);

            Assert.Equal(
                "explicit-client-id.apps.googleusercontent.com",
                AntigravityOAuthCredentialStore.ResolveOAuthClientId(credentials));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", previous);
        }
    }

    [Fact]
    public async Task ReadUsageAsyncRefreshesExpiredAccessTokenAndRetriesQuota()
    {
        var previousClientId = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID");
        var previousClientSecret = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET");
        var previousAccessToken = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", "test-client-id.apps.googleusercontent.com");
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", "test-client-secret");
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN", null);

            var credentials = new AntigravityOAuthCredentials(
                AccessToken: "expired-token",
                RefreshToken: "refresh-token",
                ClientId: null,
                ClientSecret: null,
                ExpiresAt: DateTimeOffset.UnixEpoch);
            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
                {
                    var form = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    Assert.Contains("client_secret=test-client-secret", form);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"access_token":"fresh-token","expires_in":3600,"token_type":"Bearer"}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("fresh-token", request.Headers.Authorization?.Parameter);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"buckets":[{"modelId":"gemini-3.5-flash-medium","remainingFraction":0.8}]}""",
                        Encoding.UTF8,
                        "application/json")
                };
            });
            using var httpClient = new HttpClient(handler);
            var client = new AntigravityOAuthUsageClient(
                httpClient,
                allowLocalDetection: false,
                credentialLoader: () => credentials);

            var result = await client.ReadUsageAsync(CancellationToken.None);

            Assert.Equal(AntigravityUsageReadStatus.Fresh, result.Status);
            var bucket = Assert.Single(result.Buckets);
            Assert.Equal("gemini-3.5-flash-medium", bucket.ModelId);
            Assert.Equal(80, bucket.PercentRemaining);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", previousClientId);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", previousClientSecret);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN", previousAccessToken);
        }
    }

    [Fact]
    public async Task ReadUsageAsyncTriesQuotaWithoutProjectWhenLoadCodeAssistFails()
    {
        var previousAccessToken = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN", null);

            var credentials = new AntigravityOAuthCredentials(
                AccessToken: "valid-token",
                RefreshToken: null,
                ClientId: null,
                ClientSecret: null,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));
            var sawProjectlessQuotaRequest = false;
            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri?.AbsoluteUri == "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist")
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("temporary project discovery failure")
                    };
                }

                if (request.RequestUri?.AbsoluteUri.EndsWith("/v1internal:fetchAvailableModels", StringComparison.Ordinal) == true)
                {
                    var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    sawProjectlessQuotaRequest = body == "{}";
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"models":{"gemini-3.5-flash-medium":{"quotaInfo":{"remainingFraction":0.8}}}}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            using var httpClient = new HttpClient(handler);
            var client = new AntigravityOAuthUsageClient(
                httpClient,
                allowLocalDetection: false,
                credentialLoader: () => credentials);

            var result = await client.ReadUsageAsync(CancellationToken.None);

            Assert.True(sawProjectlessQuotaRequest);
            var bucket = Assert.Single(result.Buckets);
            Assert.Equal("gemini-3.5-flash-medium", bucket.ModelId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN", previousAccessToken);
        }
    }

    [Fact]
    public async Task ReadUsageAsyncConvertsHttpTimeoutBeforeFallbackDecision()
    {
        var previousAccessToken = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN", null);

            var credentials = new AntigravityOAuthCredentials(
                AccessToken: "valid-token",
                RefreshToken: null,
                ClientId: null,
                ClientSecret: null,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));
            var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("request timed out"));
            using var httpClient = new HttpClient(handler);
            var client = new AntigravityOAuthUsageClient(
                httpClient,
                allowLocalDetection: false,
                credentialLoader: () => credentials);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.ReadUsageAsync(CancellationToken.None));

            Assert.Contains("Antigravity quota was not available", error.Message);
            Assert.Contains("Cloud request timed out", error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN", previousAccessToken);
        }
    }

    [Fact]
    public async Task ReadUsageAsyncRefreshesTokenWithExplicitEnvironmentOAuthClient()
    {
        var previousClientId = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID");
        var previousClientSecret = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET");
        var previousAccessToken = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", "env-client-id.apps.googleusercontent.com");
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", "env-client-secret");
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN", null);

            var credentials = new AntigravityOAuthCredentials(
                AccessToken: null,
                RefreshToken: "refresh-token",
                ClientId: null,
                ClientSecret: null,
                ExpiresAt: DateTimeOffset.UnixEpoch);
            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
                {
                    var form = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    Assert.Contains("client_id=env-client-id.apps.googleusercontent.com", form);
                    Assert.Contains("client_secret=env-client-secret", form);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"access_token":"fresh-token","expires_in":3600,"token_type":"Bearer"}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                Assert.Equal("fresh-token", request.Headers.Authorization?.Parameter);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"models":{"gemini-3.5-flash-medium":{"quotaInfo":{"remainingFraction":0.8}}}}""",
                        Encoding.UTF8,
                        "application/json")
                };
            });
            using var httpClient = new HttpClient(handler);
            var client = new AntigravityOAuthUsageClient(
                httpClient,
                allowLocalDetection: false,
                credentialLoader: () => credentials);

            var result = await client.ReadUsageAsync(CancellationToken.None);

            Assert.Equal(AntigravityUsageReadStatus.Fresh, result.Status);
            Assert.Single(result.Buckets);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", previousClientId);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", previousClientSecret);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN", previousAccessToken);
        }
    }

    [Fact]
    public void OAuthClientIdResolvesToBundledDefaultWhenNoneConfigured()
    {
        // With the bundled default client, an explicit client ID is no longer required —
        // resolution falls back to the bundled id instead of yielding null.
        var previousClientId = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", null);

            var credentials = new AntigravityOAuthCredentials(
                AccessToken: null,
                RefreshToken: "refresh-token",
                ClientId: null,
                ClientSecret: null,
                ExpiresAt: DateTimeOffset.UnixEpoch);

            Assert.False(string.IsNullOrEmpty(
                AntigravityOAuthCredentialStore.ResolveOAuthClientId(credentials)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", previousClientId);
        }
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginReportsBundledDefaultWhenOnlyBundledAvailable()
    {
        // No env or user-selected client → the active client is the bundled default, and the
        // transparency origin reports it as BundledDefault (not UserSavedSettings). Assumes no
        // user-added OAuth client has been selected (same clean-environment assumption these
        // resolution tests have always made).
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var previousClientId = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID");
        var previousClientSecret = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", null);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", null);

            Assert.Equal(
                AntigravityOAuthClientOrigin.BundledDefault,
                AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", previousClientId);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", previousClientSecret);
        }
    }

    [Fact]
    public async Task RefreshAsyncReportsCloudSourceChannel()
    {
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
            [new AntigravityQuotaBucket("gemini-3.5-flash-medium", 99, DateTimeOffset.Parse("2026-05-31T00:00:00Z"))],
            channel: "cloud"));
        var provider = new AntigravityUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal("cloud", snapshot.SourceChannel);
        Assert.Null(snapshot.CloudFailureSummary);
        Assert.Null(snapshot.IdeFailureSummary);
    }

    [Fact]
    public async Task RefreshAsyncReportsIdeFallbackSourceChannel()
    {
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
            [new AntigravityQuotaBucket("gemini-3.5-flash-medium", 50, DateTimeOffset.Parse("2026-05-31T00:00:00Z"))],
            channel: "ide-fallback",
            cloudFailure: "HTTP 401"));
        var provider = new AntigravityUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal("ide-fallback", snapshot.SourceChannel);
        Assert.Equal("HTTP 401", snapshot.CloudFailureSummary);
    }

    [Fact]
    public async Task RefreshAsyncCarriesOAuthClientOriginOnSuccess()
    {
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
            [new AntigravityQuotaBucket("gemini-2.5-pro", 99, DateTimeOffset.Parse("2026-05-31T00:00:00Z"))],
            channel: "cloud"));
        var provider = new AntigravityUsageProvider(
            client,
            hasOAuthClientValues: null,
            resolveOAuthClientOrigin: () => AntigravityOAuthClientOrigin.IdeCredentialFile);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal("ide", snapshot.OAuthClientOrigin);
    }

    [Fact]
    public async Task RefreshAsyncCarriesOAuthClientOriginOnFailure()
    {
        var client = new ThrowingAntigravityUsageClient(
            "Antigravity quota was not available. [cloud=HTTP 401] [ide=no endpoint discovered]");
        var provider = new AntigravityUsageProvider(
            client,
            hasOAuthClientValues: null,
            resolveOAuthClientOrigin: () => AntigravityOAuthClientOrigin.UserSavedSettings);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageStatus.Failed, snapshot.Status);
        Assert.Equal("user-saved", snapshot.OAuthClientOrigin);
    }

    [Fact]
    public async Task RefreshAsyncSplitsCloudAndIdeFailureSummaries()
    {
        var client = new ThrowingAntigravityUsageClient(
            "Antigravity quota was not available. Sign in to Antigravity with a Google account, or start Antigravity IDE as a fallback."
            + " [cloud=HTTP 401] [ide=no endpoint discovered]");
        var provider = new AntigravityUsageProvider(client);

        var snapshot = await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageStatus.Failed, snapshot.Status);
        Assert.Equal("HTTP 401", snapshot.CloudFailureSummary);
        Assert.Equal("no endpoint discovered", snapshot.IdeFailureSummary);
        Assert.Null(snapshot.SourceChannel);
        Assert.NotNull(snapshot.LastError);
        Assert.DoesNotContain("[cloud=", snapshot.LastError);
        Assert.DoesNotContain("[ide=", snapshot.LastError);
    }

    /// <summary>
    /// Guards against re-introducing per-call disk I/O on the refresh path.
    /// This stubs <see cref="IAntigravityUsageClient"/> at the provider boundary,
    /// so it does NOT exercise the in-memory access-token cache inside
    /// <c>AntigravityOAuthUsageClient</c>; it only asserts that multiple
    /// provider-level refreshes succeed without persistence side effects.
    /// </summary>
    [Fact]
    public async Task RefreshAsyncSucceedsAcrossMultipleCallsWithoutPersistence()
    {
        var reads = 0;
        var client = new CountingAntigravityUsageClient(() =>
        {
            reads++;
            return AntigravityUsageReadResult.Fresh(
                [new AntigravityQuotaBucket("gemini-2.5-pro", 80, DateTimeOffset.UtcNow.AddHours(1))],
                channel: "cloud");
        });
        var provider = new AntigravityUsageProvider(client);

        await provider.RefreshAsync(CancellationToken.None);
        await provider.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task ResolveAccessTokenAsyncCachesRefreshedTokenAcrossCalls()
    {
        var previousClientId = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID");
        var previousClientSecret = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET");
        var previousAccessToken = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", "test-client.apps.googleusercontent.com");
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", "test-client-secret");
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN", null);

            using var handler = new TokenCountingMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"refreshed-token","expires_in":3600}""",
                        Encoding.UTF8,
                        "application/json")
                });
            using var httpClient = new HttpClient(handler);
            var expiredCredentials = new AntigravityOAuthCredentials(
                AccessToken: "stale-token",
                RefreshToken: "1//refresh-token",
                ClientId: null,
                ClientSecret: null,
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(-1));
            var client = new AntigravityOAuthUsageClient(httpClient, () => expiredCredentials);

            var first = await client.ResolveAccessTokenAsync(CancellationToken.None);
            var second = await client.ResolveAccessTokenAsync(CancellationToken.None);

            Assert.Equal("refreshed-token", first);
            Assert.Equal("refreshed-token", second);
            Assert.Equal(1, handler.TokenEndpointHits);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", previousClientId);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", previousClientSecret);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_ACCESS_TOKEN", previousAccessToken);
        }
    }

    [Fact]
    public void AccountAwareFactoryReevaluatesWhetherIdeFallbackIsAllowed()
    {
        var allowIdeFallback = true;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));

        var provider = AntigravityUsageProvider.CreateWithCredentialResolver(
            credentialResolver: () => null,
            httpClient: httpClient,
            allowLocalDetectionResolver: () => allowIdeFallback);

        var clientField = typeof(AntigravityUsageProvider).GetField(
            "_client",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var client = Assert.IsType<AntigravityOAuthUsageClient>(clientField!.GetValue(provider));
        var policyField = typeof(AntigravityOAuthUsageClient).GetField(
            "_allowLocalDetectionResolver",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var policy = Assert.IsType<Func<bool>>(policyField!.GetValue(client));

        Assert.True(policy());
        allowIdeFallback = false;
        Assert.False(policy());
    }

    private sealed class TokenCountingMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;
        private int _tokenEndpointHits;

        public TokenCountingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        {
            _send = send;
        }

        public int TokenEndpointHits => Volatile.Read(ref _tokenEndpointHits);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri
                && string.Equals(uri.AbsoluteUri, "https://oauth2.googleapis.com/token", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _tokenEndpointHits);
            }

            return Task.FromResult(_send(request));
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubAntigravityUsageClient(AntigravityUsageReadResult result) : IAntigravityUsageClient
    {
        public Task<AntigravityUsageReadResult> ReadUsageAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class CountingAntigravityUsageClient(Func<AntigravityUsageReadResult> factory) : IAntigravityUsageClient
    {
        public Task<AntigravityUsageReadResult> ReadUsageAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(factory());
        }
    }

    private sealed class ThrowingAntigravityUsageClient(string message) : IAntigravityUsageClient
    {
        public Task<AntigravityUsageReadResult> ReadUsageAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string SourceFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "quota-watch.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }
}
