using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiLimit.Core.Domain;
using AiLimit.Core.Providers;
using AiLimit.Core.Providers.Accounts;
using Xunit;

namespace AiLimit.Tests.Accounts;

public sealed class AntigravityAccountProviderTests
{
    private const string KeychainBlob =
        """{"token":{"access_token":"ide-access","refresh_token":"1//ide-rt","expiry":"2099-01-01T00:00:00Z"}}""";

    private static string TempPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"acct-prov-{label}-{Guid.NewGuid():N}.json");

    private static AntigravityOAuthClientConfig DefaultClient() =>
        new("test-client-id.apps.googleusercontent.com", "test-client-secret");

    private static AntigravityOAuthClientConfig EmptyClient() =>
        new(null, null);

    private sealed record Fixture(
        AntigravityAccountProvider Provider,
        AntigravityAccountStore Store,
        AntigravityActiveSelection Selection,
        string AccountsPath,
        string SelectionPath,
        string TrashPath,
        HttpClient HttpClient,
        ScriptedHandler Handler)
        : IDisposable
    {
        public void Dispose()
        {
            HttpClient.Dispose();
            if (File.Exists(AccountsPath))
            {
                File.Delete(AccountsPath);
            }

            if (File.Exists(SelectionPath))
            {
                File.Delete(SelectionPath);
            }

            if (File.Exists(TrashPath))
            {
                File.Delete(TrashPath);
            }
        }
    }

    private static Fixture CreateFixture(
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null,
        Func<string?>? keychainReader = null,
        Func<AntigravityOAuthClientConfig>? clientResolver = null)
    {
        var accountsPath = TempPath("accounts");
        var selectionPath = TempPath("active");
        var trashPath = TempPath("trash");
        var store = new AntigravityAccountStore(accountsPath);
        var selection = new AntigravityActiveSelection(selectionPath);
        var trash = new AccountTrashStore(trashPath);
        var handler = new ScriptedHandler(
            responder ?? (_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var http = new HttpClient(handler);
        var userInfo = new AntigravityUserInfoClient(http);
        var provider = new AntigravityAccountProvider(
            store,
            selection,
            userInfo,
            keychainReader ?? (() => null),
            clientResolver ?? DefaultClient,
            http,
            trash);

        return new Fixture(provider, store, selection, accountsPath, selectionPath, trashPath, http, handler);
    }

    [Fact]
    public void LoadAccounts_ReturnsStoreContents()
    {
        using var fx = CreateFixture();
        var record = fx.Store.Add("alice", "alice@example.com", "1//rt-alice");

        var accounts = fx.Provider.LoadAccounts();

        Assert.Single(accounts);
        Assert.Equal(record.Id, accounts[0].Id);
        Assert.Equal("alice@example.com", accounts[0].Email);
        Assert.Equal("gemini-pro", fx.Provider.ProviderKey);
    }

    [Fact]
    public void MarkActive_PersistsToSelectionStore()
    {
        using var fx = CreateFixture();
        var record = fx.Store.Add("alice", null, "1//rt-alice");

        fx.Provider.MarkActive(record.Id);

        Assert.Equal(record.Id, fx.Provider.GetActiveId());
        Assert.Equal(record.Id, fx.Selection.Get());
    }

    [Fact]
    public void MarkActive_Null_ClearsSelection()
    {
        using var fx = CreateFixture();
        var record = fx.Store.Add("alice", null, "1//rt-alice");
        fx.Provider.MarkActive(record.Id);

        fx.Provider.MarkActive(null);

        Assert.Null(fx.Provider.GetActiveId());
    }

    [Fact]
    public void RemoveActiveAccountClearsSelection()
    {
        using var fx = CreateFixture();
        var record = fx.Store.Add("alice", null, "1//rt-alice");
        fx.Provider.MarkActive(record.Id);

        fx.Provider.Remove(record.Id);

        Assert.Empty(fx.Provider.LoadAccounts());
        Assert.Null(fx.Provider.GetActiveId());
    }

    [Fact]
    public void ResolveActiveCredentials_ReturnsActiveStored_WhenIdSet()
    {
        using var fx = CreateFixture();
        var record = fx.Store.Add("alice", null, "1//rt-alice");
        fx.Provider.MarkActive(record.Id);

        var credentials = fx.Provider.ResolveActiveCredentials();

        Assert.NotNull(credentials);
        Assert.Equal("1//rt-alice", credentials!.RefreshToken);
        Assert.Equal("test-client-id.apps.googleusercontent.com", credentials.ClientId);
        Assert.Equal("test-client-secret", credentials.ClientSecret);
        Assert.Null(credentials.AccessToken);
    }

    [Fact]
    public void ResolveActiveCredentials_FallsBackToKeychain_WhenNoIdSet()
    {
        using var fx = CreateFixture(keychainReader: () => KeychainBlob);

        var credentials = fx.Provider.ResolveActiveCredentials();

        Assert.NotNull(credentials);
        Assert.Equal("1//ide-rt", credentials!.RefreshToken);
        Assert.Equal("ide-access", credentials.AccessToken);
    }

    [Fact]
    public void ResolveActiveCredentials_FallsBackToKeychain_WhenActiveIdMissingFromStore()
    {
        using var fx = CreateFixture(keychainReader: () => KeychainBlob);
        fx.Provider.MarkActive(Guid.NewGuid());

        var credentials = fx.Provider.ResolveActiveCredentials();

        Assert.NotNull(credentials);
        Assert.Equal("1//ide-rt", credentials!.RefreshToken);
    }

    [Fact]
    public async Task ImportFromLocalSourceAsync_AddsRecordAndAttachesEmail()
    {
        using var fx = CreateFixture(
            keychainReader: () => KeychainBlob,
            responder: request =>
            {
                if (request.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"access_token":"fresh-access","token_type":"Bearer"}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                if (request.RequestUri?.AbsoluteUri == "https://www.googleapis.com/oauth2/v2/userinfo")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"email":"alice@example.com"}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        var record = await fx.Provider.ImportFromLocalSourceAsync(CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("alice@example.com", record!.Email);
        var loaded = fx.Provider.LoadAccounts();
        Assert.Single(loaded);
        Assert.Equal("1//ide-rt", fx.Store.ReadRefreshToken(record.Id));
    }

    [Fact]
    public async Task ImportFromLocalSourceAsync_ReturnsNullWhenKeychainEmpty()
    {
        using var fx = CreateFixture(keychainReader: () => null);

        var record = await fx.Provider.ImportFromLocalSourceAsync(CancellationToken.None);

        Assert.Null(record);
    }

    [Fact]
    public async Task ImportFromLocalSourceAsync_ReturnsNullWhenClientUnresolvable()
    {
        using var fx = CreateFixture(
            keychainReader: () => KeychainBlob,
            clientResolver: EmptyClient);

        var record = await fx.Provider.ImportFromLocalSourceAsync(CancellationToken.None);

        Assert.Null(record);
    }

    [Fact]
    public async Task ImportFromLocalSourceAsync_ReturnsExistingRecordOnDuplicate()
    {
        using var fx = CreateFixture(
            keychainReader: () => KeychainBlob,
            responder: request =>
            {
                if (request.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"access_token":"fresh-access","token_type":"Bearer"}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"email":"alice@example.com"}""",
                        Encoding.UTF8,
                        "application/json")
                };
            });
        var existing = fx.Store.Add("existing", "existing@example.com", "1//ide-rt");

        var record = await fx.Provider.ImportFromLocalSourceAsync(CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal(existing.Id, record!.Id);
        Assert.Single(fx.Provider.LoadAccounts());
    }

    [Fact]
    public async Task PollAsync_ReturnsSuccessSnapshotWhenClientResponds()
    {
        // The provider threads its clientResolver-supplied ClientId/ClientSecret
        // through the credentials record into AntigravityOAuthUsageClient.
        // RefreshAccessTokenAsync now honours those values directly, so this
        // test does NOT mutate process-global ANTIGRAVITY_OAUTH_CLIENT_ID/SECRET
        // env vars — that previously raced AntigravityUsageProviderTests'
        // absent-env-var assertions when xUnit ran the classes in parallel.
        using var fx = CreateFixture(
            responder: request =>
            {
                var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
                if (uri == "https://oauth2.googleapis.com/token")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"access_token":"fresh-token","expires_in":3600,"token_type":"Bearer"}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                if (uri == "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"cloudaicompanionProject":"proj-1"}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                if (uri.EndsWith("/v1internal:fetchAvailableModels", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"buckets":[{"modelId":"gemini-3.5-flash-medium","remainingFraction":0.42}]}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
        var record = fx.Store.Add("alice", "alice@example.com", "1//rt-alice");
        var actual = fx.Provider.LoadAccounts()[0];

        var snapshot = await fx.Provider.PollAsync(actual, CancellationToken.None);

        Assert.True(snapshot.IsSuccess, snapshot.ErrorMessage);
        Assert.Equal(AccountPlan.Unknown, snapshot.Plan);
        var bucket = Assert.Single(snapshot.Buckets);
        Assert.Equal("gemini-3.5-flash-medium", bucket.ModelId);
        Assert.Equal(42, bucket.PercentRemaining);
    }

    [Fact]
    public async Task PollAsync_ReturnsFailureWhenRecordRemoved()
    {
        using var fx = CreateFixture();
        var orphan = new AccountRecord(
            Id: Guid.NewGuid(),
            ProviderKey: "gemini-pro",
            DisplayName: "ghost",
            Email: null,
            LastSyncedAt: DateTimeOffset.UtcNow);

        var snapshot = await fx.Provider.PollAsync(orphan, CancellationToken.None);

        Assert.False(snapshot.IsSuccess);
        Assert.Equal("Account removed.", snapshot.ErrorMessage);
    }

    [Fact]
    public async Task PollAsync_ReturnsSignInAgainWhenStoredTokenUndecryptable()
    {
        using var fx = CreateFixture();
        // Reproduces the registry-entropy desync: the account is still in the store
        // but its ciphertext can no longer be decrypted. This must surface as a
        // "sign in again" prompt, NOT as the account having been removed.
        var id = Guid.NewGuid();
        WriteCorruptAccount(fx.AccountsPath, id);
        var record = fx.Provider.LoadAccounts()[0];

        var snapshot = await fx.Provider.PollAsync(record, CancellationToken.None);

        Assert.False(snapshot.IsSuccess);
        Assert.NotEqual("Account removed.", snapshot.ErrorMessage);
        Assert.Equal("Saved credentials could not be read. Sign in again.", snapshot.ErrorMessage);
    }

    private static void WriteCorruptAccount(string path, Guid id)
    {
        File.WriteAllText(
            path,
            $$"""
            {
              "accounts": [
                {
                  "id": "{{id}}",
                  "name": "broken",
                  "email": null,
                  "refresh_token": "not-base64",
                  "created_at": "2026-06-21T00:00:00Z",
                  "last_synced_at": "2026-06-21T00:00:00Z"
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task PollAsync_ReturnsFailureWhenClientConfigMissing()
    {
        using var fx = CreateFixture(clientResolver: EmptyClient);
        var record = fx.Store.Add("alice", null, "1//rt-alice");

        var snapshot = await fx.Provider.PollAsync(record, CancellationToken.None);

        Assert.False(snapshot.IsSuccess);
        Assert.Equal("OAuth client unavailable.", snapshot.ErrorMessage);
    }

    [Fact]
    public async Task MoveToTrash_HidesStoredAccountAndClearsActiveSelection()
    {
        using var fx = CreateFixture();
        var record = fx.Store.Add("alice", "alice@example.com", "1//rt-alice");
        fx.Provider.MarkActive(record.Id);

        await fx.Provider.MoveToTrashAsync(record.Id, CancellationToken.None);

        Assert.Empty(fx.Provider.LoadAccounts());
        Assert.Null(fx.Provider.GetActiveId());
        var trashed = Assert.Single(fx.Provider.LoadTrash());
        Assert.Equal(record.Id, trashed.Id);
        Assert.Equal("alice@example.com", trashed.Email);
        Assert.Equal("1//rt-alice", fx.Store.ReadRefreshToken(record.Id));
    }

    [Fact]
    public async Task RestoreFromTrash_ReturnsStoredAccountWithRefreshToken()
    {
        using var fx = CreateFixture();
        var record = fx.Store.Add("alice", "alice@example.com", "1//rt-alice");
        await fx.Provider.MoveToTrashAsync(record.Id, CancellationToken.None);

        await fx.Provider.RestoreFromTrashAsync(record.Id, CancellationToken.None);

        Assert.Single(fx.Provider.LoadAccounts());
        Assert.Empty(fx.Provider.LoadTrash());
        Assert.Equal("1//rt-alice", fx.Store.ReadRefreshToken(record.Id));
    }

    [Fact]
    public async Task DeletePermanently_RemovesStoredAccountAndTrashEntry()
    {
        using var fx = CreateFixture();
        var record = fx.Store.Add("alice", "alice@example.com", "1//rt-alice");
        await fx.Provider.MoveToTrashAsync(record.Id, CancellationToken.None);

        await fx.Provider.DeletePermanentlyAsync(record.Id, CancellationToken.None);

        Assert.Empty(fx.Provider.LoadAccounts());
        Assert.Empty(fx.Provider.LoadTrash());
        Assert.Null(fx.Store.ReadRefreshToken(record.Id));
    }

    [Fact]
    public async Task PollUsageAsyncReturnsFailedWithLabelWhenCredentialsUnavailable()
    {
        // No token stored for the record → BuildCredentials returns null → Failed snapshot.
        using var fx = CreateFixture();
        var record = new AccountRecord(Guid.NewGuid(), "gemini-pro", "missing", "ghost@x.com", DateTimeOffset.UtcNow);

        var snapshot = await fx.Provider.PollUsageAsync(record, CancellationToken.None);

        Assert.Equal(UsageStatus.Failed, snapshot.Status);
        Assert.Contains("—", snapshot.DisplayName);
        Assert.Contains("ghost@x.com", snapshot.DisplayName);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
