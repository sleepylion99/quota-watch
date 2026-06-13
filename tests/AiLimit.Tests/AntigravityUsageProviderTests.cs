using AiLimit.Core.Domain;
using AiLimit.Core.Providers;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Text;

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
        var provider = new AntigravityUsageProvider(client);

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
    public void AntigravityReadUsageTriesCloudBeforeIdeLocalFallback()
    {
        var source = File.ReadAllText(SourceFile("src", "AiLimit.Core", "Providers", "AntigravityUsageProvider.cs"));

        Assert.True(
            source.IndexOf("TryReadCloudUsageAsync", StringComparison.Ordinal)
            < source.IndexOf("TryReadIdeUsageAsync", StringComparison.Ordinal));
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
    public async Task ReadUsageAsyncRefreshesExpiredAccessTokenAndRetriesQuota()
    {
        var directory = CreateTempDirectory();
        var credentialsPath = Path.Combine(directory, "oauth_creds.json");
        await File.WriteAllTextAsync(
            credentialsPath,
            """
            {
              "access_token": "expired-token",
              "refresh_token": "refresh-token",
              "client_id": "test-client-id.apps.googleusercontent.com",
              "client_secret": "test-client-secret",
              "token_type": "Bearer",
              "expiry_date": 0
            }
            """);
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
            {
                var form = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("client_secret=test-client-secret", form);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "access_token": "fresh-token",
                          "expires_in": 3600,
                          "token_type": "Bearer"
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("fresh-token", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "buckets": [
                        {
                          "modelId": "gemini-3.5-flash-medium",
                          "remainingFraction": 0.8
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var client = new AntigravityOAuthUsageClient(new HttpClient(handler), credentialsPath);

        var result = await client.ReadUsageAsync(CancellationToken.None);

        Assert.Equal(AntigravityUsageReadStatus.Fresh, result.Status);
        var bucket = Assert.Single(result.Buckets);
        Assert.Equal("gemini-3.5-flash-medium", bucket.ModelId);
        Assert.Equal(80, bucket.PercentRemaining);
        var refreshed = await File.ReadAllTextAsync(credentialsPath);
        Assert.Contains("fresh-token", refreshed);
        Assert.Contains("refresh-token", refreshed);
        Assert.Contains("test-client-secret", refreshed);
    }

    [Fact]
    public async Task ReadUsageAsyncTriesQuotaWithoutProjectWhenLoadCodeAssistFails()
    {
        var directory = CreateTempDirectory();
        var credentialsPath = Path.Combine(directory, "oauth_creds.json");
        await File.WriteAllTextAsync(
            credentialsPath,
            """
            {
              "access_token": "valid-token",
              "token_type": "Bearer"
            }
            """);
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
                        """
                        {
                          "models": {
                            "gemini-3.5-flash-medium": {
                              "quotaInfo": {
                                "remainingFraction": 0.8
                              }
                            }
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = new AntigravityOAuthUsageClient(new HttpClient(handler), credentialsPath);

        var result = await client.ReadUsageAsync(CancellationToken.None);

        Assert.True(sawProjectlessQuotaRequest);
        var bucket = Assert.Single(result.Buckets);
        Assert.Equal("gemini-3.5-flash-medium", bucket.ModelId);
    }

    [Fact]
    public async Task ReadUsageAsyncConvertsHttpTimeoutBeforeFallbackDecision()
    {
        var directory = CreateTempDirectory();
        var credentialsPath = Path.Combine(directory, "oauth_creds.json");
        await File.WriteAllTextAsync(
            credentialsPath,
            """
            {
              "access_token": "valid-token",
              "token_type": "Bearer"
            }
            """);
        var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("request timed out"));
        var client = new AntigravityOAuthUsageClient(new HttpClient(handler), credentialsPath);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ReadUsageAsync(CancellationToken.None));

        Assert.Contains("Antigravity quota was not available", error.Message);
        Assert.Contains("Cloud request timed out", error.Message);
    }

    [Fact]
    public async Task ReadUsageAsyncRefreshesVscdbTokenWithExplicitEnvironmentOAuthClient()
    {
        var previousClientId = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID");
        var previousClientSecret = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", "env-client-id.apps.googleusercontent.com");
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", "env-client-secret");

            var directory = CreateTempDirectory();
            var credentialsPath = Path.Combine(directory, "oauth_creds.json");
            await File.WriteAllTextAsync(
                credentialsPath,
                """
                {
                  "refresh_token": "refresh-token",
                  "token_type": "Bearer",
                  "expiry_date": 0
                }
                """);
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
                            """
                            {
                              "access_token": "fresh-token",
                              "expires_in": 3600,
                              "token_type": "Bearer"
                            }
                            """,
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                Assert.Equal("fresh-token", request.Headers.Authorization?.Parameter);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "models": {
                            "gemini-3.5-flash-medium": {
                              "quotaInfo": {
                                "remainingFraction": 0.8
                              }
                            }
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            });
            var client = new AntigravityOAuthUsageClient(new HttpClient(handler), credentialsPath);

            var result = await client.ReadUsageAsync(CancellationToken.None);

            Assert.Equal(AntigravityUsageReadStatus.Fresh, result.Status);
            Assert.Single(result.Buckets);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_ID", previousClientId);
            Environment.SetEnvironmentVariable("ANTIGRAVITY_OAUTH_CLIENT_SECRET", previousClientSecret);
        }
    }

    [Fact]
    public async Task ReadUsageAsyncRequiresConfiguredOAuthClientIdForRefresh()
    {
        var directory = CreateTempDirectory();
        var credentialsPath = Path.Combine(directory, "oauth_creds.json");
        await File.WriteAllTextAsync(
            credentialsPath,
            """
            {
              "refresh_token": "refresh-token",
              "token_type": "Bearer",
              "expiry_date": 0
            }
            """);
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
            {
                throw new Xunit.Sdk.XunitException("Token endpoint must not be called without a configured client ID.");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = new AntigravityOAuthUsageClient(new HttpClient(handler), credentialsPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ReadUsageAsync(CancellationToken.None));

        Assert.Contains("OAuth client ID", exception.Message);
        Assert.Contains("Settings > Antigravity OAuth", exception.Message);
        Assert.Contains("ANTIGRAVITY_OAUTH_CLIENT_ID", exception.Message);
    }

    [Fact]
    public async Task ReadUsageAsyncExplainsMissingOAuthClientSecretWhenRefreshFails()
    {
        var directory = CreateTempDirectory();
        var credentialsPath = Path.Combine(directory, "oauth_creds.json");
        await File.WriteAllTextAsync(
            credentialsPath,
            """
            {
              "refresh_token": "refresh-token",
              "client_id": "test-client-id.apps.googleusercontent.com",
              "token_type": "Bearer",
              "expiry_date": 0
            }
            """);
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = new AntigravityOAuthUsageClient(new HttpClient(handler), credentialsPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ReadUsageAsync(CancellationToken.None));

        Assert.Contains("no OAuth client secret is available", exception.Message);
        Assert.Contains("Settings > Antigravity OAuth", exception.Message);
        Assert.Contains("ANTIGRAVITY_OAUTH_CLIENT_SECRET", exception.Message);
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
    public void AntigravityRefreshRequiresExplicitOAuthClientSecretWhenVscdbHasOnlyRefreshToken()
    {
        var credentials = new AntigravityOAuthCredentials(null, "1//refresh-token", null, null, null);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AntigravityOAuthCredentialStore.ResolveOAuthClientSecret(credentials));
        Assert.Contains("ANTIGRAVITY_OAUTH_CLIENT_SECRET", exception.Message);
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
    public void AntigravityRefreshCanUseClientIdStoredWithCredentials()
    {
        var credentials = new AntigravityOAuthCredentials(
            null,
            "1//refresh-token",
            "stored-client-id.apps.googleusercontent.com",
            null,
            null);

        Assert.Equal(
            "stored-client-id.apps.googleusercontent.com",
            AntigravityOAuthCredentialStore.ResolveOAuthClientId(credentials));
    }

    [Fact]
    public void AntigravityOAuthClientStoreProtectsAndRestoresClientValues()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "antigravity-oauth-client.json");
        var store = new AntigravityOAuthClientStore(path);

        store.Save("saved-client-id.apps.googleusercontent.com", "saved-client-secret");
        var fileText = File.ReadAllText(path);
        var loaded = store.Load();

        Assert.Equal("saved-client-id.apps.googleusercontent.com", loaded.ClientId);
        Assert.Equal("saved-client-secret", loaded.ClientSecret);
        Assert.DoesNotContain("saved-client-secret", fileText);
        Assert.DoesNotContain("saved-client-id.apps.googleusercontent.com", fileText);
    }

    [Fact]
    public void AntigravityVscdbCredentialsExtractRefreshTokenFromStoredProtobuf()
    {
        const string refreshToken = "1//test-refresh-token_123";
        var storedValue = BuildAntigravityStoredOAuthToken(refreshToken);

        var credentials = AntigravityVscdbCredentialStore.ParseStoredOAuthToken(storedValue);

        Assert.NotNull(credentials);
        Assert.Null(credentials.AccessToken);
        Assert.Equal(refreshToken, credentials.RefreshToken);
        Assert.Null(credentials.ClientSecret);
    }

    [Fact]
    public void AntigravityVscdbCredentialsExtractAccessTokenFromStoredProtobuf()
    {
        const string accessToken = "ya29.test-access-token_123";
        var storedValue = BuildAntigravityStoredOAuthToken(accessToken);

        var credentials = AntigravityVscdbCredentialStore.ParseStoredOAuthToken(storedValue);

        Assert.NotNull(credentials);
        Assert.Equal(accessToken, credentials.AccessToken);
        Assert.Null(credentials.RefreshToken);
        Assert.Null(credentials.ClientSecret);
    }

    [Fact]
    public void AntigravityVscdbCredentialsExtractStoredOAuthClientFields()
    {
        const string refreshToken = "1//test-refresh-token_abc";
        const string clientId = "1234567890-local-test.apps.googleusercontent.com";
        const string clientSecret = "local-client-secret_123";
        var storedValue = BuildAntigravityStoredOAuthToken(
            $$"""
            {
              "refresh_token": "{{refreshToken}}",
              "client_id": "{{clientId}}",
              "client_secret": "{{clientSecret}}"
            }
            """);

        var credentials = AntigravityVscdbCredentialStore.ParseStoredOAuthToken(storedValue);

        Assert.NotNull(credentials);
        Assert.Equal(refreshToken, credentials.RefreshToken);
        Assert.Equal(clientId, credentials.ClientId);
        Assert.Equal(clientSecret, credentials.ClientSecret);
        Assert.Equal(clientSecret, AntigravityOAuthCredentialStore.ResolveOAuthClientSecret(credentials));
    }

    [Fact]
    public void AntigravityVscdbCredentialsExtractPlainJsonAuthStatusAccessToken()
    {
        const string accessToken = "ya29.test-access-token_auth_status";

        var credentials = AntigravityVscdbCredentialStore.ParseStoredOAuthToken(
            $$"""{"apiKey":"{{accessToken}}","email":"user@example.com"}""");

        Assert.NotNull(credentials);
        Assert.Equal(accessToken, credentials.AccessToken);
    }

    [Fact]
    public async Task AntigravityVscdbCredentialsScanStateDbWithoutLoggingToken()
    {
        const string refreshToken = "1//test-refresh-token_456";
        var directory = CreateTempDirectory();
        var stateDb = Path.Combine(directory, "state.vscdb");
        await File.WriteAllTextAsync(
            stateDb,
            "prefix antigravityUnifiedStateSync.oauthToken " +
            BuildAntigravityStoredOAuthToken(refreshToken) +
            " suffix");

        var store = new AntigravityVscdbCredentialStore([stateDb]);
        var credentials = store.Load();

        Assert.NotNull(credentials);
        Assert.Equal(refreshToken, credentials.RefreshToken);
    }

    [Fact]
    public async Task AntigravityVscdbCredentialsReadLegacyKeyFromSqliteStateDb()
    {
        const string refreshToken = "1//legacy-refresh-token_123";
        var directory = CreateTempDirectory();
        var stateDb = Path.Combine(directory, "state.vscdb");
        await CreateStateDbAsync(
            stateDb,
            "jetskiStateSync.agentManagerInitState",
            Convert.ToBase64String(EncodeLengthDelimitedField(6, Encoding.Latin1.GetBytes(refreshToken))));

        var store = new AntigravityVscdbCredentialStore([stateDb]);
        var credentials = store.Load();

        Assert.NotNull(credentials);
        Assert.Equal(refreshToken, credentials.RefreshToken);
    }

    [Fact]
    public async Task AntigravityVscdbCredentialsPreferAuthStatusAccessTokenFromSqliteStateDb()
    {
        const string authStatusAccessToken = "ya29.auth-status-access-token_123";
        const string legacyAccessToken = "ya29.legacy-access-token_123";
        const string refreshToken = "1//legacy-refresh-token_456";
        var directory = CreateTempDirectory();
        var stateDb = Path.Combine(directory, "state.vscdb");
        await CreateStateDbAsync(
            stateDb,
            [
                ("jetskiStateSync.agentManagerInitState", Convert.ToBase64String(EncodeLengthDelimitedField(6, Encoding.Latin1.GetBytes($"{legacyAccessToken} {refreshToken}")))),
                ("antigravityAuthStatus", $$"""{"apiKey":"{{authStatusAccessToken}}","email":"user@example.com"}""")
            ]);

        var store = new AntigravityVscdbCredentialStore([stateDb]);
        var credentials = store.Load();

        Assert.NotNull(credentials);
        Assert.Equal(authStatusAccessToken, credentials.AccessToken);
        Assert.Equal(refreshToken, credentials.RefreshToken);
    }

    [Fact]
    public async Task AntigravityVscdbCredentialsScanPastDecoyOAuthTokenKeys()
    {
        const string refreshToken = "1//test-refresh-token_after_decoy";
        var directory = CreateTempDirectory();
        var stateDb = Path.Combine(directory, "state.vscdb");
        await File.WriteAllTextAsync(
            stateDb,
            "prefix antigravityUnifiedStateSync.oauthToken not-a-token " +
            new string('x', 9000) +
            " antigravityUnifiedStateSync.oauthToken " +
            BuildAntigravityStoredOAuthToken(refreshToken) +
            " suffix");

        var store = new AntigravityVscdbCredentialStore([stateDb]);
        var credentials = store.Load();

        Assert.NotNull(credentials);
        Assert.Equal(refreshToken, credentials.RefreshToken);
    }

    [Fact]
    public async Task AntigravityVscdbCredentialsMergeAccessAndRefreshTokensFromSeparateStoredValues()
    {
        const string accessToken = "ya29.test-access-token_456";
        const string refreshToken = "1//test-refresh-token_789";
        var directory = CreateTempDirectory();
        var stateDb = Path.Combine(directory, "state.vscdb");
        await File.WriteAllTextAsync(
            stateDb,
            "prefix antigravityUnifiedStateSync.oauthToken " +
            BuildAntigravityStoredOAuthToken(accessToken) +
            " middle " +
            BuildAntigravityStoredOAuthToken(refreshToken) +
            " suffix");

        var store = new AntigravityVscdbCredentialStore([stateDb]);
        var credentials = store.Load();

        Assert.NotNull(credentials);
        Assert.Equal(accessToken, credentials.AccessToken);
        Assert.Equal(refreshToken, credentials.RefreshToken);
    }

    [Fact]
    public async Task AntigravityVscdbCredentialsMergeAccessAndRefreshTokensAcrossCandidatePaths()
    {
        const string accessToken = "ya29.test-access-token_987";
        const string refreshToken = "1//test-refresh-token_987";
        var directory = CreateTempDirectory();
        var ideStateDb = Path.Combine(directory, "Antigravity IDE", "User", "globalStorage", "state.vscdb");
        var standaloneStateDb = Path.Combine(directory, "Antigravity", "User", "globalStorage", "state.vscdb");
        Directory.CreateDirectory(Path.GetDirectoryName(ideStateDb)!);
        Directory.CreateDirectory(Path.GetDirectoryName(standaloneStateDb)!);
        await File.WriteAllTextAsync(
            ideStateDb,
            "prefix antigravityUnifiedStateSync.oauthToken " +
            BuildAntigravityStoredOAuthToken(accessToken) +
            " suffix");
        await File.WriteAllTextAsync(
            standaloneStateDb,
            "prefix antigravityUnifiedStateSync.oauthToken " +
            BuildAntigravityStoredOAuthToken(refreshToken) +
            " suffix");

        var store = new AntigravityVscdbCredentialStore([ideStateDb, standaloneStateDb]);
        var credentials = store.Load();

        Assert.NotNull(credentials);
        Assert.Equal(accessToken, credentials.AccessToken);
        Assert.Equal(refreshToken, credentials.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsyncReportsCloudSourceChannel()
    {
        var client = new StubAntigravityUsageClient(AntigravityUsageReadResult.Fresh(
            [new AntigravityQuotaBucket("gemini-2.5-pro", 99, DateTimeOffset.Parse("2026-05-31T00:00:00Z"))],
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
            [new AntigravityQuotaBucket("gemini-2.5-pro", 50, DateTimeOffset.Parse("2026-05-31T00:00:00Z"))],
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

    private sealed class StubAntigravityUsageClient(AntigravityUsageReadResult result) : IAntigravityUsageClient
    {
        public Task<AntigravityUsageReadResult> ReadUsageAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingAntigravityUsageClient(string message) : IAntigravityUsageClient
    {
        public Task<AntigravityUsageReadResult> ReadUsageAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(SourceFile(".tmp"), "AiLimit.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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

    private static Task CreateStateDbAsync(string path, string key, string value)
    {
        return CreateStateDbAsync(path, [(key, value)]);
    }

    private static async Task CreateStateDbAsync(string path, IReadOnlyList<(string Key, string Value)> values)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder { DataSource = path };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE ItemTable (key TEXT UNIQUE ON CONFLICT REPLACE, value BLOB)";
            await command.ExecuteNonQueryAsync();
        }

        foreach (var value in values)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO ItemTable (key, value) VALUES ($key, $value)";
            command.Parameters.AddWithValue("$key", value.Key);
            command.Parameters.AddWithValue("$value", value.Value);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string BuildAntigravityStoredOAuthToken(string refreshToken)
    {
        var tokenPayload = Encoding.Latin1.GetBytes($"before {refreshToken} after");
        var encodedPayload = Encoding.ASCII.GetBytes("xx" + Convert.ToBase64String(tokenPayload).TrimEnd('='));
        var inner = EncodeLengthDelimitedField(2, encodedPayload);
        var outer = EncodeLengthDelimitedField(1, inner);
        return Convert.ToBase64String(outer).TrimEnd('=');
    }

    private static byte[] EncodeLengthDelimitedField(int fieldNumber, byte[] value)
    {
        return [.. EncodeVarint((ulong)((fieldNumber << 3) | 2)), .. EncodeVarint((ulong)value.Length), .. value];
    }

    private static byte[] EncodeVarint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
            {
                b |= 0x80;
            }

            bytes.Add(b);
        }
        while (value != 0);

        return [.. bytes];
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
}
