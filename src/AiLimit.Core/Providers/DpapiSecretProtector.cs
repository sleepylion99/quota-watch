using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AiLimit.Core.Providers;

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    public string Protect(string value)
        => Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    public string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
