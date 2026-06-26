using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using AiLimit.Core.Domain;
using AiLimit.Core.Providers;

namespace AiLimit.Core.Providers.Accounts;

/// <summary>
/// IAccountProvider implementation for Google Antigravity. Composes the per-user
/// account store, the active-account pointer, the OAuth userinfo client, the
/// shared OAuth client resolver, and an HttpClient. The keychain raw-blob reader
/// is the fallback path for users who never open the Accounts window — when no
/// account is explicitly selected we still surface the IDE-signed-in user.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AntigravityAccountProvider : IUsageAccountProvider, ILocalIdeAccount, ITrashableAccountProvider
{
    private readonly AntigravityAccountStore _store;
    private readonly AntigravityActiveSelection _activeSelection;
    private readonly AntigravityUserInfoClient _userInfo;
    private readonly Func<string?> _keychainRawBlobReader;
    private readonly Func<AntigravityOAuthClientConfig> _clientResolver;
    private readonly HttpClient _httpClient;
    private readonly AccountTrashStore _trashStore;

    public AntigravityAccountProvider(
        AntigravityAccountStore store,
        AntigravityActiveSelection activeSelection,
        AntigravityUserInfoClient userInfo,
        Func<string?> keychainRawBlobReader,
        Func<AntigravityOAuthClientConfig> clientResolver,
        HttpClient httpClient,
        AccountTrashStore? trashStore = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _activeSelection = activeSelection ?? throw new ArgumentNullException(nameof(activeSelection));
        _userInfo = userInfo ?? throw new ArgumentNullException(nameof(userInfo));
        _keychainRawBlobReader = keychainRawBlobReader ?? throw new ArgumentNullException(nameof(keychainRawBlobReader));
        _clientResolver = clientResolver ?? throw new ArgumentNullException(nameof(clientResolver));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _trashStore = trashStore ?? new AccountTrashStore(AccountTrashStore.DefaultPath());
    }

    public string ProviderKey => "gemini-pro";
    public string DisplayName => "Google Antigravity";

    public IReadOnlyList<AccountRecord> LoadAccounts() => _store.Load()
        .Where(r => !_trashStore.Contains(ProviderKey, r.Id))
        .ToList()
        .AsReadOnly();

    public AccountRecord Add(string displayName, string? email, string refreshToken)
        => _store.Add(displayName, email, refreshToken);

    public void Remove(Guid id)
    {
        _store.Remove(id);
        if (_activeSelection.Get() == id)
        {
            _activeSelection.Set(null);
        }
    }

    public void MarkActive(Guid? id) => _activeSelection.Set(id);

    public Guid? GetActiveId() => _activeSelection.Get();

    /// <summary>
    /// Returns the credentials the dashboard tile should use. When the user has
    /// explicitly selected an account that still exists in the store, returns a
    /// credentials record anchored to that refresh token and the resolved OAuth
    /// client. Otherwise falls back to the live keychain blob — same behaviour
    /// as before the account switcher landed.
    /// </summary>
    /// <remarks>
    /// Returned as <c>internal</c> because <see cref="AntigravityOAuthCredentials"/>
    /// itself is internal to AiLimit.Core. The composition root in AiLimit.App
    /// (Task 7) consumes this via <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal AntigravityOAuthCredentials? ResolveActiveCredentials()
    {
        var activeId = _activeSelection.Get();
        if (activeId is { } id)
        {
            var refreshToken = _store.ReadRefreshToken(id);
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var client = _clientResolver();
                if (string.IsNullOrWhiteSpace(client.ClientId) || string.IsNullOrWhiteSpace(client.ClientSecret))
                {
                    return null;
                }

                // ExpiresAt forced into the past so the downstream usage
                // client refreshes immediately rather than handing back the
                // null access token we encoded here.
                return new AntigravityOAuthCredentials(
                    AccessToken: null,
                    RefreshToken: refreshToken,
                    ClientId: client.ClientId,
                    ClientSecret: client.ClientSecret,
                    ExpiresAt: DateTimeOffset.UnixEpoch);
            }
        }

        var blob = _keychainRawBlobReader();
        if (string.IsNullOrWhiteSpace(blob))
        {
            return null;
        }

        return AntigravityWindowsCredentialStore.ParseCredentialBlob(blob);
    }

    public async Task<AccountSnapshot> PollAsync(AccountRecord record, CancellationToken cancellationToken)
    {
        var lookup = _store.ReadRefreshTokenLookup(record.Id);
        if (lookup.Status == RefreshTokenStatus.NotStored)
        {
            return AccountSnapshot.Failure("Account removed.");
        }

        if (lookup.Status == RefreshTokenStatus.Undecryptable)
        {
            // The account still exists but its stored token can no longer be decrypted
            // (e.g. registry-entropy desync). Tell the user to re-authenticate instead
            // of misreporting the account as removed.
            return AccountSnapshot.Failure("Saved credentials could not be read. Sign in again.");
        }

        var refreshToken = lookup.Token!;

        var client = _clientResolver();
        if (string.IsNullOrWhiteSpace(client.ClientId) || string.IsNullOrWhiteSpace(client.ClientSecret))
        {
            return AccountSnapshot.Failure("OAuth client unavailable.");
        }

        // ExpiresAt = UnixEpoch forces ShouldRefresh()=true so the usage client
        // exchanges the refresh token for a fresh access token immediately — we
        // never have a cached access token of our own to hand back.
        var credentials = new AntigravityOAuthCredentials(
            AccessToken: null,
            RefreshToken: refreshToken,
            ClientId: client.ClientId,
            ClientSecret: client.ClientSecret,
            ExpiresAt: DateTimeOffset.UnixEpoch);

        try
        {
            var usageClient = new AntigravityOAuthUsageClient(
                _httpClient,
                allowLocalDetection: false,
                credentialLoader: () => credentials);

            var result = await usageClient.ReadUsageAsync(cancellationToken).ConfigureAwait(false);
            var buckets = result.Buckets
                .Select(b => new QuotaBucket(b.ModelId, b.PercentRemaining, b.ResetAt))
                .ToList()
                .AsReadOnly();

            return AccountSnapshot.Success(buckets, AccountPlan.Unknown);
        }
        catch (HttpRequestException ex)
        {
            return AccountSnapshot.Failure(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return AccountSnapshot.Failure(ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            return AccountSnapshot.Failure(ex.Message);
        }
    }

    private AntigravityOAuthCredentials? BuildCredentials(Guid id)
    {
        var lookup = _store.ReadRefreshTokenLookup(id);
        if (lookup.Status != RefreshTokenStatus.Available || string.IsNullOrWhiteSpace(lookup.Token))
        {
            return null;
        }

        var client = _clientResolver();
        if (string.IsNullOrWhiteSpace(client.ClientId) || string.IsNullOrWhiteSpace(client.ClientSecret))
        {
            return null;
        }

        return new AntigravityOAuthCredentials(
            AccessToken: null,
            RefreshToken: lookup.Token,
            ClientId: client.ClientId,
            ClientSecret: client.ClientSecret,
            ExpiresAt: DateTimeOffset.UnixEpoch);
    }

    public async Task<UsageSnapshot> PollUsageAsync(AccountRecord record, CancellationToken cancellationToken)
    {
        var credentials = BuildCredentials(record.Id);
        if (credentials is null)
        {
            return UsageAccountLabel.Failed(ProviderKey, DisplayName, record, "Account removed or credentials unavailable.");
        }

        var usageProvider = AntigravityUsageProvider.CreateWithCredentialResolver(
            credentialResolver: () => credentials,
            httpClient: _httpClient,
            allowLocalDetectionResolver: () => false);

        var snapshot = await usageProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return UsageAccountLabel.Stamp(snapshot, DisplayName, record);
    }

    public async Task<AccountRecord?> ImportFromLocalSourceAsync(CancellationToken cancellationToken)
    {
        var rawBlob = _keychainRawBlobReader();
        if (string.IsNullOrWhiteSpace(rawBlob))
        {
            return null;
        }

        var credentials = AntigravityWindowsCredentialStore.ParseCredentialBlob(rawBlob);
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            return null;
        }

        var client = _clientResolver();
        if (string.IsNullOrWhiteSpace(client.ClientId) || string.IsNullOrWhiteSpace(client.ClientSecret))
        {
            return null;
        }

        var refreshResult = await AntigravityOAuthCredentialStore.RefreshAccessTokenAsync(
            credentials.RefreshToken!,
            client,
            _httpClient,
            cancellationToken).ConfigureAwait(false);
        if (refreshResult is not { } refreshed || string.IsNullOrWhiteSpace(refreshed.AccessToken))
        {
            return null;
        }

        var email = await _userInfo.FetchEmailAsync(refreshed.AccessToken, cancellationToken).ConfigureAwait(false);

        try
        {
            return _store.Add(
                displayName: string.IsNullOrWhiteSpace(email) ? "account" : email!,
                email: email,
                refreshToken: credentials.RefreshToken!);
        }
        catch (InvalidOperationException)
        {
            // Duplicate refresh token — find the existing matching record.
            foreach (var existing in _store.Load())
            {
                var existingRefresh = _store.ReadRefreshToken(existing.Id);
                if (string.Equals(existingRefresh, credentials.RefreshToken, StringComparison.Ordinal))
                {
                    return existing;
                }
            }

            return null;
        }
    }

    public bool CanTrash(AccountRecord account)
        => string.Equals(account.ProviderKey, ProviderKey, StringComparison.Ordinal)
            && account.Id != Guid.Empty
            && _store.Load().Any(r => r.Id == account.Id);

    public IReadOnlyList<TrashedAccountRecord> LoadTrash() => _trashStore.List(ProviderKey);

    public Task MoveToTrashAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = _store.Load().FirstOrDefault(r => r.Id == id);
        if (record is null || !CanTrash(record))
        {
            return Task.CompletedTask;
        }

        _trashStore.Put(new TrashedAccountRecord(
            record.Id,
            ProviderKey,
            record.DisplayName,
            record.Email,
            DateTimeOffset.UtcNow));
        if (_activeSelection.Get() == id)
        {
            _activeSelection.Set(null);
        }

        return Task.CompletedTask;
    }

    public Task RestoreFromTrashAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _trashStore.Remove(ProviderKey, id);
        return Task.CompletedTask;
    }

    public Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.Remove(id);
        _trashStore.Remove(ProviderKey, id);
        if (_activeSelection.Get() == id)
        {
            _activeSelection.Set(null);
        }

        return Task.CompletedTask;
    }
}
