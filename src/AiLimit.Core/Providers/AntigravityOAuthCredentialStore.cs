using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AiLimit.Core.Providers;

public enum AntigravityOAuthClientOrigin
{
    None,
    Environment,
    IdeCredentialFile,
    UserSavedSettings
}

internal sealed class AntigravityOAuthCredentialStore
{
    private const string OAuthAccessTokenEnvVar = "ANTIGRAVITY_OAUTH_ACCESS_TOKEN";
    private const string OAuthClientIdEnvVar = "ANTIGRAVITY_OAUTH_CLIENT_ID";
    private const string OAuthClientSecretEnvVar = "ANTIGRAVITY_OAUTH_CLIENT_SECRET";
    private readonly string _path;
    private readonly bool _allowVscdbFallback;

    public AntigravityOAuthCredentialStore(string path)
    {
        _path = path;
        _allowVscdbFallback = Path.GetFullPath(path).Equals(
            Path.GetFullPath(DefaultCredentialsPath()),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string DefaultCredentialsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".antigravity",
            "oauth_creds.json");
    }

    public string? ResolveAccessToken()
    {
        return ResolveEnvironmentAccessToken() ?? Load()?.AccessToken;
    }

    public static string? ResolveEnvironmentAccessToken()
    {
        var envToken = Environment.GetEnvironmentVariable(OAuthAccessTokenEnvVar);
        return string.IsNullOrWhiteSpace(envToken) ? null : envToken.Trim();
    }

    public AntigravityOAuthCredentials? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return LoadVscdbFallback();
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            return new AntigravityOAuthCredentials(
                root.TryGetProperty("access_token", out var accessToken) ? accessToken.GetString() : null,
                root.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() : null,
                root.TryGetProperty("client_id", out var clientId) ? clientId.GetString() : null,
                root.TryGetProperty("client_secret", out var clientSecret) ? clientSecret.GetString() : null,
                ReadExpiry(root));
        }
        catch
        {
            return LoadVscdbFallback();
        }
    }

    public static string ResolveOAuthClientSecret(AntigravityOAuthCredentials credentials)
    {
        return TryResolveOAuthClientSecret(credentials)
            ?? throw new InvalidOperationException($"Google Antigravity OAuth client secret was not found. Set {OAuthClientSecretEnvVar} or sign in to Antigravity again.");
    }

    public static string? TryResolveOAuthClientSecret(AntigravityOAuthCredentials credentials)
    {
        var envSecret = Environment.GetEnvironmentVariable(OAuthClientSecretEnvVar);
        if (!string.IsNullOrWhiteSpace(envSecret))
        {
            return envSecret.Trim();
        }

        if (!string.IsNullOrWhiteSpace(credentials.ClientSecret))
        {
            return credentials.ClientSecret!;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var userSaved = new AntigravityOAuthClientStore(AntigravityOAuthClientStore.DefaultPath()).Load();
        if (!string.IsNullOrWhiteSpace(userSaved.ClientSecret))
        {
            return userSaved.ClientSecret;
        }

        return null;
    }

    public static AntigravityOAuthClientOrigin ResolveActiveOAuthClientOrigin()
    {
        var userClientStorePath = OperatingSystem.IsWindows()
            ? AntigravityOAuthClientStore.DefaultPath()
            : string.Empty;
        return ResolveActiveOAuthClientOrigin(
            DefaultCredentialsPath(),
            userClientStorePath);
    }

    internal static AntigravityOAuthClientOrigin ResolveActiveOAuthClientOrigin(
        string ideCredentialsPath,
        string userClientStorePath)
    {
        var envSecret = Environment.GetEnvironmentVariable(OAuthClientSecretEnvVar);
        if (!string.IsNullOrWhiteSpace(envSecret))
        {
            return AntigravityOAuthClientOrigin.Environment;
        }

        var ideCredentials = new AntigravityOAuthCredentialStore(ideCredentialsPath).Load();
        if (ideCredentials is not null
            && !string.IsNullOrWhiteSpace(ideCredentials.ClientSecret))
        {
            return AntigravityOAuthClientOrigin.IdeCredentialFile;
        }

        if (OperatingSystem.IsWindows())
        {
            var userClient = new AntigravityOAuthClientStore(userClientStorePath).Load();
            if (!string.IsNullOrWhiteSpace(userClient.ClientSecret))
            {
                return AntigravityOAuthClientOrigin.UserSavedSettings;
            }
        }

        return AntigravityOAuthClientOrigin.None;
    }

    public static string? ResolveOAuthClientId(AntigravityOAuthCredentials credentials)
    {
        var envClientId = Environment.GetEnvironmentVariable(OAuthClientIdEnvVar);
        if (!string.IsNullOrWhiteSpace(envClientId))
        {
            return envClientId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(credentials.ClientId))
        {
            return credentials.ClientId!.Trim();
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var userSaved = new AntigravityOAuthClientStore(AntigravityOAuthClientStore.DefaultPath()).Load();
        if (!string.IsNullOrWhiteSpace(userSaved.ClientId))
        {
            return userSaved.ClientId;
        }

        return null;
    }

    public void SaveRefreshedAccessToken(string accessToken, DateTimeOffset expiresAt)
    {
        var node = File.Exists(_path)
            ? JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
            : null;
        node ??= [];
        node["access_token"] = accessToken;
        node["expiry_date"] = expiresAt.ToUnixTimeMilliseconds();

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                tempPath,
                node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static DateTimeOffset? ReadExpiry(JsonElement root)
    {
        if (!root.TryGetProperty("expiry_date", out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return FromUnixValue(number);
        }

        if (value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out var parsed))
        {
            return FromUnixValue(parsed);
        }

        return null;
    }

    private static DateTimeOffset FromUnixValue(long value)
    {
        return value >= 10_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : DateTimeOffset.FromUnixTimeSeconds(value);
    }

    private AntigravityOAuthCredentials? LoadVscdbFallback()
    {
        return _allowVscdbFallback
            ? new AntigravityVscdbCredentialStore().Load()
            : null;
    }
}

internal sealed record AntigravityOAuthCredentials(
    string? AccessToken,
    string? RefreshToken,
    string? ClientId,
    string? ClientSecret,
    DateTimeOffset? ExpiresAt)
{
    public bool ShouldRefresh(DateTimeOffset now)
    {
        return ExpiresAt is not null && ExpiresAt.Value <= now.AddMinutes(1);
    }
}

internal sealed class AntigravityVscdbCredentialStore
{
    private const string OAuthTokenKey = "antigravityUnifiedStateSync.oauthToken";
    private const string UserStatusKey = "antigravityUnifiedStateSync.userStatus";
    private const string LegacyAgentStateKey = "jetskiStateSync.agentManagerInitState";
    private const string AuthStatusKey = "antigravityAuthStatus";
    private static readonly string[] CredentialStateKeys = [AuthStatusKey, OAuthTokenKey, UserStatusKey, LegacyAgentStateKey];
    private static readonly Regex StoredValuePattern = new("[A-Za-z0-9+/]{32,}={0,2}", RegexOptions.Compiled);
    private static readonly Regex AccessTokenPattern = new("ya29\\.[A-Za-z0-9_.-]+", RegexOptions.Compiled);
    private static readonly Regex RefreshTokenPattern = new("1//[A-Za-z0-9_-]+", RegexOptions.Compiled);
    private static readonly Regex ClientIdPattern = new("[0-9]+-[A-Za-z0-9_-]+\\.apps\\.googleusercontent\\.com", RegexOptions.Compiled);
    private static readonly Regex ClientSecretPropertyPattern = new(
        "(?:client_secret|clientSecret)[\"'\\s:=]+([A-Za-z0-9_-]{8,})",
        RegexOptions.Compiled);
    private readonly IReadOnlyList<string> _candidatePaths;

    public AntigravityVscdbCredentialStore()
        : this(DefaultCandidatePaths())
    {
    }

    internal AntigravityVscdbCredentialStore(IReadOnlyList<string> candidatePaths)
    {
        _candidatePaths = candidatePaths;
    }

    public AntigravityOAuthCredentials? Load()
    {
        AntigravityOAuthCredentials? merged = null;
        foreach (var path in _candidatePaths)
        {
            var credentials = LoadFromPath(path);
            if (credentials is not null)
            {
                merged = MergeCredentials(merged, credentials);
                if (!string.IsNullOrWhiteSpace(merged.AccessToken)
                    && !string.IsNullOrWhiteSpace(merged.RefreshToken))
                {
                    return merged;
                }
            }
        }

        return merged;
    }

    internal static AntigravityOAuthCredentials? ParseStoredOAuthToken(string storedValue, string sourceKey = "")
    {
        WriteDiagnostic($"parse start key={sourceKey} value-length={storedValue.Length}");

        var fromPlainText = ParseTokenText(storedValue);
        if (fromPlainText is not null)
        {
            WriteDiagnostic($"key={sourceKey} matched plain text. {SummarizeCredentials(fromPlainText)}");
            return fromPlainText;
        }

        try
        {
            var outer = Convert.FromBase64String(AddBase64Padding(storedValue.Trim()));
            WriteDiagnostic($"key={sourceKey} base64-decoded bytes={outer.Length}");

            var fromDecodedText = ParseTokenText(Encoding.Latin1.GetString(outer));
            if (fromDecodedText is not null)
            {
                WriteDiagnostic($"key={sourceKey} matched decoded text. {SummarizeCredentials(fromDecodedText)}");
                return fromDecodedText;
            }

            var outerFields = ParseProtobuf(outer).ToList();
            WriteDiagnostic($"key={sourceKey} outer protobuf fields=[{SummarizeFields(outerFields)}]");

            var inner = outerFields
                .FirstOrDefault(field => field.Number == 1 && field.WireType == 2)?
                .Bytes;
            if (inner is not null)
            {
                var innerFields = ParseProtobuf(inner).ToList();
                WriteDiagnostic($"key={sourceKey} inner(field1) protobuf fields=[{SummarizeFields(innerFields)}]");

                var encodedTokenPayload = innerFields
                    .FirstOrDefault(field => field.Number == 2 && field.WireType == 2)?
                    .Bytes;
                if (encodedTokenPayload is not null && encodedTokenPayload.Length > 2)
                {
                    var payloadSlice = encodedTokenPayload.AsSpan(2).ToArray();
                    var payloadBytes = TryDecodeBase64(payloadSlice) ?? payloadSlice;
                    var fromUnifiedPayload = ParseTokenText(Encoding.Latin1.GetString(payloadBytes));
                    if (fromUnifiedPayload is not null)
                    {
                        WriteDiagnostic($"key={sourceKey} matched unified payload. {SummarizeCredentials(fromUnifiedPayload)}");
                        return fromUnifiedPayload;
                    }
                }
            }

            var fromNestedFields = ParseTokenTextFragments(outer);
            if (fromNestedFields is not null)
            {
                WriteDiagnostic($"key={sourceKey} matched via nested-fragment scan. {SummarizeCredentials(fromNestedFields)}");
                return fromNestedFields;
            }

            WriteDiagnostic($"key={sourceKey} no credentials matched after base64+protobuf scan");
        }
        catch (Exception ex)
        {
            WriteDiagnostic($"key={sourceKey} parse exception {ex.GetType().Name}: {ex.Message}");
        }

        var fromRawScan = ParseTokenTextFragments(Encoding.Latin1.GetBytes(storedValue));
        if (fromRawScan is not null)
        {
            WriteDiagnostic($"key={sourceKey} matched via raw-byte fragment scan. {SummarizeCredentials(fromRawScan)}");
        }
        else
        {
            WriteDiagnostic($"key={sourceKey} no credentials matched at all");
        }
        return fromRawScan;
    }

    private static string SummarizeFields(IReadOnlyCollection<ProtobufField> fields)
    {
        return string.Join(",", fields.Select(f => $"#{f.Number}:wt{f.WireType}:len{(f.Bytes?.Length ?? 0)}"));
    }

    private static string SummarizeCredentials(AntigravityOAuthCredentials credentials)
    {
        return $"access={!string.IsNullOrWhiteSpace(credentials.AccessToken)} refresh={!string.IsNullOrWhiteSpace(credentials.RefreshToken)} clientId={!string.IsNullOrWhiteSpace(credentials.ClientId)} clientSecret={!string.IsNullOrWhiteSpace(credentials.ClientSecret)}";
    }

    private static void WriteDiagnostic(string message)
    {
        if (!IsDiagnosticLoggingEnabled())
        {
            return;
        }

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AiLimit");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "dashboard-debug.log"),
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [Info] [AntigravityVscdb] {DiagnosticSanitizer.Redact(message)}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break credential loading.
        }
    }

    private static bool IsDiagnosticLoggingEnabled()
    {
        var value = Environment.GetEnvironmentVariable("AILIMIT_DEBUG_LOG");
        return value is "1" || value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    private AntigravityOAuthCredentials? LoadFromPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var sqliteCredentials = LoadFromSqlite(path);
            if (sqliteCredentials is not null)
            {
                return sqliteCredentials;
            }

            var text = ReadFileText(path);
            var keyIndex = 0;
            AntigravityOAuthCredentials? merged = null;
            while ((keyIndex = text.IndexOf(OAuthTokenKey, keyIndex, StringComparison.Ordinal)) >= 0)
            {
                var searchStart = keyIndex + OAuthTokenKey.Length;
                var searchLength = Math.Min(8192, text.Length - searchStart);
                keyIndex = searchStart;
                if (searchLength <= 0)
                {
                    continue;
                }

                var window = text.Substring(searchStart, searchLength);
                foreach (Match match in StoredValuePattern.Matches(window))
                {
                    var credentials = ParseStoredOAuthToken(match.Value, $"{OAuthTokenKey}#textscan");
                    if (credentials is null)
                    {
                        continue;
                    }

                    merged = MergeCredentials(merged, credentials);
                    if (!string.IsNullOrWhiteSpace(merged.AccessToken)
                        && !string.IsNullOrWhiteSpace(merged.RefreshToken)
                        && !string.IsNullOrWhiteSpace(merged.ClientId)
                        && !string.IsNullOrWhiteSpace(merged.ClientSecret))
                    {
                        return merged;
                    }
                }
            }

            return merged;
        }
        catch
        {
            // Stored IDE credentials are a best-effort fallback. Never leak token data in errors.
        }

        return null;
    }

    private static AntigravityOAuthCredentials? LoadFromSqlite(string path)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT key, value
                FROM ItemTable
                WHERE key IN ({string.Join(", ", CredentialStateKeys.Select((_, index) => $"$key{index}"))})
                ORDER BY CASE key
                    WHEN $key0 THEN 0
                    WHEN $key1 THEN 1
                    WHEN $key2 THEN 2
                    WHEN $key3 THEN 3
                    ELSE 4
                END
                """;
            for (var i = 0; i < CredentialStateKeys.Length; i++)
            {
                command.Parameters.AddWithValue($"$key{i}", CredentialStateKeys[i]);
            }
            using var reader = command.ExecuteReader();

            AntigravityOAuthCredentials? merged = null;
            while (reader.Read())
            {
                var key = reader.GetString(0);
                var value = ReadSqliteValue(reader, 1);
                var credentials = ParseStoredOAuthToken(value, key);
                if (credentials is null)
                {
                    continue;
                }

                merged = MergeCredentials(merged, credentials);
            }

            return merged;
        }
        catch
        {
            return null;
        }
    }

    private static AntigravityOAuthCredentials? ParseTokenTextFragments(byte[] data, int depth = 0)
    {
        if (depth > 8)
        {
            return null;
        }

        var merged = ParseTokenText(Encoding.Latin1.GetString(data));
        foreach (var field in ParseProtobuf(data))
        {
            if (field.WireType != 2 || field.Bytes is null || field.Bytes.Length == 0)
            {
                continue;
            }

            var nested = ParseTokenTextFragments(field.Bytes, depth + 1);
            if (nested is not null)
            {
                merged = MergeCredentials(merged, nested);
            }

            var decoded = TryDecodeBase64(field.Bytes);
            if (decoded is null)
            {
                continue;
            }

            nested = ParseTokenTextFragments(decoded, depth + 1);
            if (nested is not null)
            {
                merged = MergeCredentials(merged, nested);
            }
        }

        return merged;
    }

    private static AntigravityOAuthCredentials? ParseTokenText(string text)
    {
        var accessToken = AccessTokenPattern.Match(text);
        var refreshToken = RefreshTokenPattern.Match(text);
        var clientId = ClientIdPattern.Match(text);
        var clientSecret = ClientSecretPropertyPattern.Match(text);
        return accessToken.Success || refreshToken.Success || clientId.Success || clientSecret.Success
            ? new AntigravityOAuthCredentials(
                accessToken.Success ? accessToken.Value : null,
                refreshToken.Success ? refreshToken.Value : null,
                clientId.Success ? clientId.Value : null,
                clientSecret.Success ? clientSecret.Groups[1].Value : null,
                null)
            : null;
    }

    private static string ReadSqliteValue(SqliteDataReader reader, int ordinal)
    {
        return reader.GetFieldType(ordinal) == typeof(byte[])
            ? Convert.ToBase64String((byte[])reader.GetValue(ordinal))
            : reader.GetString(ordinal);
    }

    private static string ReadFileText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Encoding.Latin1.GetString(memory.ToArray());
    }

    private static AntigravityOAuthCredentials MergeCredentials(
        AntigravityOAuthCredentials? current,
        AntigravityOAuthCredentials next)
    {
        if (current is null)
        {
            return next;
        }

        return current with
        {
            AccessToken = string.IsNullOrWhiteSpace(current.AccessToken) ? next.AccessToken : current.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(current.RefreshToken) ? next.RefreshToken : current.RefreshToken,
            ClientId = string.IsNullOrWhiteSpace(current.ClientId) ? next.ClientId : current.ClientId,
            ClientSecret = string.IsNullOrWhiteSpace(current.ClientSecret) ? next.ClientSecret : current.ClientSecret,
            ExpiresAt = current.ExpiresAt ?? next.ExpiresAt
        };
    }

    private static IReadOnlyList<string> DefaultCandidatePaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            return [];
        }

        var paths = new List<string>
        {
            Path.Combine(appData, "Antigravity IDE", "User", "globalStorage", "state.vscdb"),
            Path.Combine(appData, "Antigravity", "User", "globalStorage", "state.vscdb")
        };
        foreach (var root in new[]
        {
            Path.Combine(appData, "Antigravity IDE", "User", "workspaceStorage"),
            Path.Combine(appData, "Antigravity", "User", "workspaceStorage")
        })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            paths.AddRange(Directory.EnumerateFiles(root, "state.vscdb", SearchOption.AllDirectories));
        }

        return paths;
    }

    private static byte[]? TryDecodeBase64(byte[] bytes)
    {
        try
        {
            return Convert.FromBase64String(AddBase64Padding(Encoding.ASCII.GetString(bytes)));
        }
        catch
        {
            return null;
        }
    }

    private static string AddBase64Padding(string value)
    {
        var remainder = value.Length % 4;
        return remainder == 0
            ? value
            : value.PadRight(value.Length + 4 - remainder, '=');
    }

    private static IEnumerable<ProtobufField> ParseProtobuf(byte[] data)
    {
        var offset = 0;
        while (offset < data.Length)
        {
            if (!TryReadVarint(data, ref offset, out var tag))
            {
                yield break;
            }

            var wireType = (int)(tag & 0x7);
            var number = (int)(tag >> 3);
            if (wireType == 0)
            {
                if (!TryReadVarint(data, ref offset, out _))
                {
                    yield break;
                }

                yield return new ProtobufField(number, wireType, null);
                continue;
            }

            if (wireType != 2 || !TryReadVarint(data, ref offset, out var length))
            {
                yield break;
            }

            if (length > int.MaxValue || offset + (int)length > data.Length)
            {
                yield break;
            }

            var bytes = data.AsSpan(offset, (int)length).ToArray();
            offset += (int)length;
            yield return new ProtobufField(number, wireType, bytes);
        }
    }

    private static bool TryReadVarint(byte[] data, ref int offset, out ulong value)
    {
        value = 0;
        var shift = 0;
        while (offset < data.Length && shift < 64)
        {
            var b = data[offset++];
            value |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        return false;
    }

    private sealed record ProtobufField(int Number, int WireType, byte[]? Bytes);
}
