using AiLimit.Core.Providers;
using System.Text.Json;

namespace AiLimit.Tests;

public sealed class AntigravityOAuthCredentialStoreTests
{
    [Fact]
    public void OAuthClientOriginsOnlyContainConfiguredSources()
    {
        var names = Enum.GetNames<AntigravityOAuthClientOrigin>();
        Assert.Equal(
            ["None", "Environment", "IdeCredentialFile", "UserSavedSettings"],
            names);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginPicksEnvironmentWhenSecretIsSet()
    {
        using var _ = TemporaryEnvironment.Set(
            ("ANTIGRAVITY_OAUTH_CLIENT_ID", "env-id.apps.googleusercontent.com"),
            ("ANTIGRAVITY_OAUTH_CLIENT_SECRET", "env-secret"));
        using var tmp = TemporaryPaths.Create();

        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            tmp.IdeCredentialsPath,
            tmp.UserClientStorePath);

        Assert.Equal(AntigravityOAuthClientOrigin.Environment, origin);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginPicksIdeFileWhenSecretLivesThere()
    {
        using var _ = TemporaryEnvironment.Set(
            ("ANTIGRAVITY_OAUTH_CLIENT_ID", null),
            ("ANTIGRAVITY_OAUTH_CLIENT_SECRET", null));
        using var tmp = TemporaryPaths.Create();
        WriteIdeCredentials(
            tmp.IdeCredentialsPath,
            clientId: "ide-id.apps.googleusercontent.com",
            clientSecret: "ide-secret");

        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            tmp.IdeCredentialsPath,
            tmp.UserClientStorePath);

        Assert.Equal(AntigravityOAuthClientOrigin.IdeCredentialFile, origin);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginPicksUserSavedWhenIdeFileLacksSecret()
    {
        using var _ = TemporaryEnvironment.Set(
            ("ANTIGRAVITY_OAUTH_CLIENT_ID", null),
            ("ANTIGRAVITY_OAUTH_CLIENT_SECRET", null));
        using var tmp = TemporaryPaths.Create();
        WriteIdeCredentials(
            tmp.IdeCredentialsPath,
            clientId: "ide-id.apps.googleusercontent.com",
            clientSecret: null);
        new AntigravityOAuthClientStore(tmp.UserClientStorePath).Save(
            "user-id.apps.googleusercontent.com",
            "user-secret");

        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            tmp.IdeCredentialsPath,
            tmp.UserClientStorePath);

        Assert.Equal(AntigravityOAuthClientOrigin.UserSavedSettings, origin);
    }

    [Fact]
    public void ResolveActiveOAuthClientOriginReturnsNoneWhenNothingHasSecret()
    {
        using var _ = TemporaryEnvironment.Set(
            ("ANTIGRAVITY_OAUTH_CLIENT_ID", null),
            ("ANTIGRAVITY_OAUTH_CLIENT_SECRET", null));
        using var tmp = TemporaryPaths.Create();

        var origin = AntigravityOAuthCredentialStore.ResolveActiveOAuthClientOrigin(
            tmp.IdeCredentialsPath,
            tmp.UserClientStorePath);

        Assert.Equal(AntigravityOAuthClientOrigin.None, origin);
    }

    private static void WriteIdeCredentials(string path, string? clientId, string? clientSecret)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new Dictionary<string, object?>
        {
            ["refresh_token"] = "rt",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload));
    }

    private sealed class TemporaryEnvironment : IDisposable
    {
        private readonly (string Key, string? Previous)[] _restore;

        private TemporaryEnvironment((string Key, string? Previous)[] restore)
        {
            _restore = restore;
        }

        public static TemporaryEnvironment Set(params (string Key, string? Value)[] entries)
        {
            var restore = entries
                .Select(entry =>
                {
                    var previous = Environment.GetEnvironmentVariable(entry.Key);
                    Environment.SetEnvironmentVariable(entry.Key, entry.Value);
                    return (entry.Key, previous);
                })
                .ToArray();
            return new TemporaryEnvironment(restore);
        }

        public void Dispose()
        {
            foreach (var (key, previous) in _restore)
            {
                Environment.SetEnvironmentVariable(key, previous);
            }
        }
    }

    private sealed class TemporaryPaths : IDisposable
    {
        public string IdeCredentialsPath { get; }
        public string UserClientStorePath { get; }
        private readonly string _root;

        private TemporaryPaths(string root)
        {
            _root = root;
            IdeCredentialsPath = Path.Combine(root, "ide", "oauth_creds.json");
            UserClientStorePath = Path.Combine(root, "user", "antigravity-oauth-client.json");
        }

        public static TemporaryPaths Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "AiLimit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "ide"));
            Directory.CreateDirectory(Path.Combine(root, "user"));
            return new TemporaryPaths(root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
