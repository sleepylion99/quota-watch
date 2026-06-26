using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace AiLimit.Core.Providers;

internal sealed class AntigravityWindowsCredentialStore
{
    public const string DefaultTarget = "LegacyGeneric:target=gemini:antigravity";

    private readonly Func<string, string?> _blobReader;

    // NOTE: this default ctor used to raise CA1416 (ReadBlobFromCredentialManager is Windows-only
    // Credential Manager P/Invoke, referenced here without a platform guard under Core's
    // cross-platform net10.0 TFM). Suppressed below.
#pragma warning disable CA1416
    public AntigravityWindowsCredentialStore()
        : this(ReadBlobFromCredentialManager)
    {
    }
#pragma warning restore CA1416

    internal AntigravityWindowsCredentialStore(Func<string, string?> blobReader)
    {
        _blobReader = blobReader;
    }

    public AntigravityOAuthCredentials? Load()
    {
        var blob = _blobReader(DefaultTarget);
        return string.IsNullOrWhiteSpace(blob) ? null : ParseCredentialBlob(blob);
    }

    [SupportedOSPlatform("windows")]
    public static string? ReadRawBlob() => ReadBlobFromCredentialManager(DefaultTarget);

    internal static AntigravityOAuthCredentials? ParseCredentialBlob(string blob)
    {
        try
        {
            using var document = JsonDocument.Parse(blob);
            if (!document.RootElement.TryGetProperty("token", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var accessToken = tokenElement.TryGetProperty("access_token", out var access)
                ? access.GetString()
                : null;
            var refreshToken = tokenElement.TryGetProperty("refresh_token", out var refresh)
                ? refresh.GetString()
                : null;
            DateTimeOffset? expiresAt = null;
            if (tokenElement.TryGetProperty("expiry", out var expiry)
                && expiry.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(expiry.GetString(), out var parsed))
            {
                expiresAt = parsed;
            }

            if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
            }

            return new AntigravityOAuthCredentials(accessToken, refreshToken, null, null, expiresAt);
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadBlobFromCredentialManager(string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!NativeMethods.CredRead(target, NativeMethods.CRED_TYPE_GENERIC, 0, out var ptr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(ptr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, (int)credential.CredentialBlobSize);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            NativeMethods.CredFree(ptr);
        }
    }

    private static class NativeMethods
    {
        public const uint CRED_TYPE_GENERIC = 1;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern void CredFree(IntPtr credential);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }
    }
}
